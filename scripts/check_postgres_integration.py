#!/usr/bin/env python3
"""Exercise the real Compose/Dockerfile/PostgreSQL path in a disposable, labelled project.

Requires Docker Compose v2 and Python's standard library. No existing database is selected.
"""
import argparse
import concurrent.futures
import copy
from datetime import datetime, timedelta, timezone
import hashlib
import json
import os
from pathlib import Path
import re
import secrets
import subprocess
import sys
import tempfile
import threading
import time
from urllib.error import HTTPError, URLError
from urllib.request import Request, build_opener
import uuid

from smoke_api import NoRedirects, assert_smoke, fetch, require_loopback
import migrate_postgres


MARKER = 'com.globaltradetax.integration'
SCHEMA_SQL = """
SELECT json_build_object(
 'columns', (SELECT json_agg(x ORDER BY table_name, ordinal_position) FROM
   (SELECT table_name, ordinal_position, column_name, data_type, udt_name,
           is_nullable, column_default, is_identity, identity_generation
      FROM information_schema.columns WHERE table_schema='public') x),
 'constraints', (SELECT json_agg(x ORDER BY table_name, conname) FROM
   (SELECT c.relname AS table_name, k.conname, pg_get_constraintdef(k.oid) AS definition
      FROM pg_constraint k JOIN pg_class c ON c.oid=k.conrelid
      JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public') x),
 'indexes', (SELECT json_agg(x ORDER BY tablename, indexname) FROM
   (SELECT tablename, indexname, indexdef FROM pg_indexes WHERE schemaname='public') x)
)::text;
"""


class VerificationError(RuntimeError):
    pass


def validate_spec(spec, project, marker):
    """Reject external/shared data and names before starting or deleting any test resource."""
    if not re.fullmatch(r'gtt-pgtest-[0-9a-f]{12}', project):
        raise VerificationError('Unexpected test project name')
    for category in ('volumes', 'networks'):
        for resource in spec.get(category, {}).values():
            if resource.get('external') or not resource.get('name', '').startswith(project + '_'):
                raise VerificationError('Test resources must have private project names')
            if resource.get('labels', {}).get(MARKER) != marker:
                raise VerificationError('Test resource is missing its ownership label')
    for service in spec['services'].values():
        if service.get('container_name') or service.get('labels', {}).get(MARKER) != marker:
            raise VerificationError('Test containers must be owned and Compose-named')
        for mount in service.get('volumes', []):
            if mount['type'] == 'bind' and not mount.get('read_only'):
                raise VerificationError('Writable host bind mounts are not allowed in this test')
            if mount['type'] == 'volume' and mount['source'] not in spec['volumes']:
                raise VerificationError('Unmanaged test volume')


class Integration:
    def __init__(self, directory, report_path):
        self.repo = Path(__file__).resolve().parents[1]
        self.directory = Path(directory)
        self.report_path = Path(report_path).resolve()
        self.run_id = uuid.uuid4().hex[:12]
        self.project = 'gtt-pgtest-' + self.run_id
        self.image = 'globaltradetax-pgtest:' + self.run_id
        self.env = os.environ.copy()
        self.env.update({
            'POSTGRES_PASSWORD': secrets.token_hex(32),
            'GTT_TEST_OPERATOR_KEY': secrets.token_hex(32),
            'GTT_TEST_FALLBACK_KEY': secrets.token_hex(32),
            'GTT_TEST_API_DATABASE': 'trade', 'GTT_TEST_API_HOST': 'postgres',
        })
        self.hidden = [self.env[name] for name in
                       ('POSTGRES_PASSWORD', 'GTT_TEST_OPERATOR_KEY', 'GTT_TEST_FALLBACK_KEY')]
        self.keys = {name: self.new_key() for name in ('a', 'b', 'rotated', 'legacy')}
        self.base = None
        self.claimed = False
        self.compose_file = self.directory / 'compose.json'
        self.empty_env = self.directory / 'empty.env'
        self.empty_env.write_text('', encoding='utf-8')
        self.report = {'project': self.project, 'status': 'running', 'assertions': 0,
                       'checks': [], 'cleanup': False}
        self.report_lock = threading.Lock()

    def new_key(self):
        value = 'gtt_live_' + secrets.token_urlsafe(36)
        self.hidden.extend([value, hashlib.sha256(value.encode()).hexdigest().upper()])
        return value

    def redact(self, text):
        for value in sorted(self.hidden, key=len, reverse=True):
            text = text.replace(value, '[REDACTED]')
        return text

    def command(self, arguments, *, data=None, timeout=90, check=True):
        try:
            result = subprocess.run(arguments, input=data, capture_output=True,
                                    env=self.env, timeout=timeout)
        except subprocess.TimeoutExpired as error:
            raise VerificationError('Command timed out: ' + ' '.join(arguments[:4])) from error
        if check and result.returncode:
            detail = self.redact(result.stderr.decode('utf-8', errors='replace')[-1800:])
            raise VerificationError('Command failed: ' + ' '.join(arguments[:4]) + '\n' + detail)
        return result

    def docker(self, *arguments, **kwargs):
        return self.command(['docker', *arguments], **kwargs)

    def compose(self, *arguments, **kwargs):
        return self.docker('compose', '-p', self.project, '--project-directory', str(self.directory),
                           '--env-file', str(self.empty_env), '-f', str(self.compose_file), *arguments, **kwargs)

    def check(self, name, condition, count=1):
        if not condition:
            raise VerificationError(name)
        with self.report_lock:
            self.report['checks'].append(name)
            self.report['assertions'] += count

    def phase(self, name):
        print(name, flush=True)

    def resource_ids(self, kind):
        if kind == 'container':
            args = ('ps', '-aq')
        else:
            args = (kind, 'ls', '-q')
        result = self.docker(*args, '--filter', 'label=com.docker.compose.project=' + self.project)
        return result.stdout.decode().split()

    def owned_resources(self):
        for kind in ('container', 'volume', 'network'):
            identifiers = self.resource_ids(kind)
            if not identifiers:
                continue
            items = json.loads(self.docker(kind, 'inspect', *identifiers).stdout)
            for item in items:
                labels = item['Config'].get('Labels', {}) if kind == 'container' else item.get('Labels', {})
                if labels.get('com.docker.compose.project') != self.project or labels.get(MARKER) != self.run_id:
                    raise VerificationError('Refusing cleanup of a resource not owned by this run')

    def prepare(self):
        self.docker('info', '--format', '{{.ServerVersion}}')
        for kind in ('container', 'volume', 'network'):
            self.check('Unique unused project: ' + kind, not self.resource_ids(kind))
        self.claimed = True
        # Render the repository's actual Compose config, then replace only test isolation/settings.
        original = self.docker('compose', '-p', self.project, '--project-directory', str(self.repo),
                               '--env-file', str(self.empty_env), '-f', str(self.repo / 'compose.yaml'),
                               'config', '--format', 'json')
        spec = json.loads(original.stdout)
        spec['name'] = self.project
        for category in ('volumes', 'networks'):
            for key, resource in spec.get(category, {}).items():
                # External resources are forbidden, even if the root Compose file gains one later.
                if resource.get('external'):
                    raise VerificationError('Root Compose config contains an external resource')
                resource['name'] = self.project + '_' + key
                resource.setdefault('labels', {})[MARKER] = self.run_id
        spec['volumes']['upgrade_pg'] = {'name': self.project + '_upgrade_pg', 'labels': {MARKER: self.run_id}}
        for service in spec['services'].values():
            service.setdefault('labels', {})[MARKER] = self.run_id
        pg = spec['services']['postgres']
        pg['ports'] = [{'target': 5432, 'published': '0', 'host_ip': '127.0.0.1', 'protocol': 'tcp'}]
        pg['environment']['POSTGRES_PASSWORD'] = '${POSTGRES_PASSWORD:?}'
        pg['healthcheck'] = {'test': ['CMD', 'pg_isready', '-h', '127.0.0.1', '-U', 'trade', '-d', 'trade'],
                             'interval': '2s', 'timeout': '2s', 'retries': 40}
        legacy_init = self.directory / 'legacy-init'
        legacy_init.mkdir()
        (legacy_init / '001_init.sql').write_bytes((self.repo / 'tests/fixtures/postgres/v04-schema.sql').read_bytes())
        upgrade = copy.deepcopy(pg)
        upgrade.pop('ports', None)
        upgrade['volumes'] = [
            {'type': 'volume', 'source': 'upgrade_pg', 'target': '/var/lib/postgresql/data'},
            {'type': 'bind', 'source': str(legacy_init), 'target': '/docker-entrypoint-initdb.d', 'read_only': True},
        ]
        spec['services']['upgrade-postgres'] = upgrade
        api = spec['services']['api']
        api['image'] = self.image
        api['build'].setdefault('labels', {})[MARKER] = self.run_id
        api['ports'] = [{'target': 8080, 'published': '0', 'host_ip': '127.0.0.1', 'protocol': 'tcp'}]
        api['environment'] = {
            'TRADE_POSTGRES_CONNECTION': 'Host=${GTT_TEST_API_HOST};Port=5432;Database=${GTT_TEST_API_DATABASE};Username=trade;Password=${POSTGRES_PASSWORD}',
            'ASPNETCORE_ENVIRONMENT': 'Development',
            'TRADE_API_KEY': '${GTT_TEST_FALLBACK_KEY}', 'TRADE_TENANT_ID': 'fallback-must-not-authenticate',
            'TRADE_OPERATOR_KEY': '${GTT_TEST_OPERATOR_KEY}',
            'SaleorApp__Enabled': 'false',
            'TenantRateLimit__PermitLimit': '10000',
            'Logging__LogLevel__Microsoft.AspNetCore': 'Warning',
        }
        validate_spec(spec, self.project, self.run_id)
        self.compose_file.write_text(json.dumps(spec), encoding='utf-8')
        self.report['postgres_image'] = pg['image']
        self.phase('Building the repository Dockerfile')
        self.compose('build', 'api', timeout=360)
        self.phase('Initializing new and legacy PostgreSQL volumes')
        self.compose('up', '-d', 'postgres', 'upgrade-postgres', timeout=150)
        for service in ('postgres', 'upgrade-postgres'):
            identifier = self.compose('ps', '-q', service).stdout.decode().strip()
            deadline = time.monotonic() + 100
            while time.monotonic() < deadline:
                state = json.loads(self.docker('inspect', identifier).stdout)[0]['State']
                if state.get('Health', {}).get('Status') == 'healthy':
                    break
                if state['Status'] in ('dead', 'exited'):
                    raise VerificationError(service + ' exited during initialization')
                time.sleep(1)
            else:
                raise VerificationError(service + ' did not become healthy')
        self.report['postgres_version'] = self.sql('SHOW server_version;')

    def sql(self, statement, *, service='postgres', database='trade', error_state=None):
        result = self.compose('exec', '-T', service, 'psql', '-X', '-qAt', '-U', 'trade', '-d', database,
                              '-v', 'ON_ERROR_STOP=1', '--single-transaction', '-f', '-',
                              data=('\\set VERBOSITY sqlstate\nSET client_min_messages=warning;\n' + statement).encode(),
                              check=error_state is None)
        if error_state:
            self.check('SQL constraint/rollback rejects ' + error_state,
                       result.returncode != 0 and error_state in result.stderr.decode(errors='replace'))
        return result.stdout.decode().strip()

    @staticmethod
    def literal(value):
        return "'" + str(value).replace("'", "''") + "'"

    def provision(self, tenant, key, service='postgres'):
        self.sql("INSERT INTO tenants(tenant_id,display_name) VALUES ({0},{0}) ON CONFLICT DO NOTHING;\n"
                 "INSERT INTO tenant_api_keys(key_id,tenant_id,key_prefix,key_hash) VALUES ({1},{0},{2},{3});".format(
                     self.literal(tenant), self.literal(uuid.uuid4()), self.literal(key[:18]),
                     self.literal(hashlib.sha256(key.encode()).hexdigest().upper())), service=service)

    def start_api(self, host='postgres', database='trade'):
        self.env.update({'GTT_TEST_API_HOST': host, 'GTT_TEST_API_DATABASE': database})
        self.compose('up', '-d', '--no-deps', '--no-build', '--force-recreate', 'api', timeout=100)
        address = self.compose('port', 'api', '8080').stdout.decode().strip()
        self.base = 'http://' + address
        require_loopback(self.base)
        deadline = time.monotonic() + 45
        while time.monotonic() < deadline:
            try:
                if fetch(self.base, '/health')[0] == 200:
                    return
            except (OSError, URLError):
                pass
            time.sleep(.25)
        raise VerificationError('Container API did not become reachable')

    def request(self, method, path, *, key=None, operator=False, payload=None, idempotency=None, expected=200, chunked=False, retry_after=False):
        """Make an isolated API request and optionally validate a positive retry header."""
        headers = {}
        if key is not None:
            headers['X-API-Key'] = key
        if operator:
            headers['X-Operator-Key'] = self.env['GTT_TEST_OPERATOR_KEY']
        if idempotency:
            headers['Idempotency-Key'] = idempotency
        if isinstance(payload, (str, bytes)):
            headers['Content-Type'] = 'text/csv'
            body = payload.encode() if isinstance(payload, str) else payload
        elif payload is not None:
            headers['Content-Type'] = 'application/json'
            body = json.dumps(payload).encode()
        else:
            body = None
        if chunked and body is not None:
            raw_body = body
            body = (raw_body[i:i + 8192] for i in range(0, len(raw_body), 8192))
        request = Request(self.base + path, data=body, headers=headers, method=method)
        try:
            response = build_opener(NoRedirects()).open(request, timeout=12)
        except HTTPError as error:
            response = error
        with response:
            status = response.status
            retry = response.headers.get('Retry-After') if retry_after else None
            raw = response.read(1_048_576)
        self.check(f'{method} {path} expects HTTP {expected} (received {status})',
                   status in expected if isinstance(expected, tuple) else status == expected)
        if retry_after:
            self.check('Rate limit returns a positive Retry-After', bool(retry and retry.isdigit() and int(retry) > 0))
        try:
            return json.loads(raw) if raw else None
        except ValueError:
            raise VerificationError('API response was not JSON') from None

    def incomplete_quote(self, quote):
        self.check('Quote duty/tax/landed total remain unavailable',
                   quote['status'] == 'requires_provider_quote' and
                   all(quote[name] is None for name in ('importDuty', 'importTax', 'landedTotal')))

    def fresh_api(self):
        self.phase('Checking fresh schema, merchant isolation and concurrent idempotency')
        self.fresh_schema = self.sql(SCHEMA_SQL)
        self.check('All eight schema tables initialized', len(json.loads(self.fresh_schema)['indexes']) > 0 and
                   self.sql("SELECT count(*) FROM information_schema.tables WHERE table_schema='public' AND table_type='BASE TABLE';") == '8')
        self.provision('merchant-a', self.keys['a'])
        self.provision('merchant-b', self.keys['b'])
        self.start_api()
        self.check('Runtime container runs without root', self.compose('exec', '-T', 'api', 'id', '-u').stdout.strip() != b'0')
        anonymous = assert_smoke(lambda path, method='GET', payload=None: fetch(self.base, path, method, payload))
        self.check('General container smoke and anonymous routes', anonymous == 21, count=anonymous)
        for country in ('DE', 'PL', 'CY'):
            reference = self.request('GET', '/v1/references/eu/destinations/' + country.lower(), key=self.keys['a'])
            self.check('EU country guidance remains reference only: ' + country,
                       reference['country'] == country and reference['status'] == 'reference_only' and
                       reference['territoryStatus'] == 'unresolved' and not reference['verifiedForCalculation'] and
                       reference['referenceReviewedOn'] == '2026-11-08')
        self.request('GET', '/v1/references/eu/destinations/GB', key=self.keys['a'], expected=404)
        self.request('GET', '/v1/references/eu/destinations/123', key=self.keys['a'], expected=400)
        capabilities = self.request('GET', '/v1/capabilities')
        self.check('Database-backed capabilities', capabilities['persistence'] == 'postgresql' and
                   capabilities['tenantAuthentication'] == 'hashed-postgres-api-keys')
        self.request('GET', '/v1/quotes/missing', key=self.env['GTT_TEST_FALLBACK_KEY'], expected=401)
        payload = json.loads((self.repo / 'samples/us-to-sweden.json').read_text())
        self.payload = payload
        self.quote_a = self.request('POST', '/v1/quotes', key=self.keys['a'], payload=payload, idempotency='shared-key')
        self.incomplete_quote(self.quote_a)
        quote_b = self.request('POST', '/v1/quotes', key=self.keys['b'], payload=payload, idempotency='shared-key')
        self.check('Same key is independent across merchants', quote_b['quoteId'] != self.quote_a['quoteId'])
        self.request('GET', '/v1/quotes/' + self.quote_a['quoteId'], key=self.keys['b'], expected=404)
        self.request('GET', '/v1/quotes/' + quote_b['quoteId'], key=self.keys['a'], expected=404)
        replay = self.request('POST', '/v1/quotes', key=self.keys['a'], payload=payload, idempotency='shared-key')
        self.check('Identical quote replay', replay == self.quote_a)
        changed = copy.deepcopy(payload)
        changed['lines'][0]['quantity'] += 1
        self.request('POST', '/v1/quotes', key=self.keys['a'], payload=changed, idempotency='shared-key', expected=409)
        with concurrent.futures.ThreadPoolExecutor(max_workers=8) as pool:
            results = list(pool.map(lambda _: self.request('POST', '/v1/quotes', key=self.keys['a'],
                                payload=payload, idempotency='concurrent-key'), range(8)))
        self.check('Concurrent requests return one durable quote', len({item['quoteId'] for item in results}) == 1 and
                   self.sql("SELECT count(*) FROM quotes WHERE tenant_id='merchant-a' AND idempotency_key='concurrent-key';") == '1')
        self.sql("INSERT INTO tenant_api_keys(key_id,tenant_id,key_prefix,key_hash) VALUES ('" + str(uuid.uuid4()) +
                 "','missing-merchant','constraint-test','constraint-test');", error_state='23503')
        self.sql("UPDATE tenant_api_keys SET enabled=false WHERE tenant_id='merchant-a';")
        self.request('GET', '/v1/quotes/' + self.quote_a['quoteId'], key=self.keys['a'], expected=401)
        self.sql("UPDATE tenant_api_keys SET enabled=true,revoked_at=now() WHERE tenant_id='merchant-a';")
        self.request('GET', '/v1/quotes/' + self.quote_a['quoteId'], key=self.keys['a'], expected=401)
        self.provision('merchant-a', self.keys['rotated'])
        self.sql("UPDATE tenants SET enabled=false WHERE tenant_id='merchant-b';")
        self.request('GET', '/v1/quotes/' + quote_b['quoteId'], key=self.keys['b'], expected=401)

    def key_lifecycle(self):
        """Exercise the operator command against the labelled database and real HTTP API."""
        self.phase('Checking operator key lifecycle')
        address = self.compose('port', 'postgres', '5432').stdout.decode().strip()
        port = int(address.rsplit(':', 1)[1])
        env = self.env.copy()
        env['DATABASE_URL'] = 'host=127.0.0.1 port=%d dbname=trade user=trade password=%s' % (port, self.env['POSTGRES_PASSWORD'])
        def command(*args, expected=0):
            result = subprocess.run([sys.executable, str(self.repo / 'scripts/tenant_keys.py'), *args],
                                    env=env, capture_output=True, timeout=30)
            self.check('Lifecycle command has expected status', result.returncode == expected)
            if result.returncode:
                self.check('Lifecycle diagnostics omit connection password', self.env['POSTGRES_PASSWORD'].encode() not in result.stderr)
                return None
            obj = json.loads(result.stdout)
            if isinstance(obj, dict) and obj.get('secret'):
                self.hidden.extend([obj['secret'], hashlib.sha256(obj['secret'].encode()).hexdigest().upper()])
            return obj
        self.sql("INSERT INTO tenants(tenant_id,display_name) VALUES ('lifecycle-a','Lifecycle A'),('lifecycle-b','Lifecycle B');")
        first = command('issue', 'lifecycle-a', '--days', '1')
        self.request('GET', '/v1/destinations/GB', key=first['secret'])
        self.check('List exposes metadata only', 'secret' not in json.dumps(command('list', 'lifecycle-a')) and 'key_hash' not in json.dumps(command('list', 'lifecycle-a')))
        command('rotate', 'lifecycle-b', first['key_id'], expected=1)
        self.request('GET', '/v1/destinations/GB', key=first['secret'])
        with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:
            results = list(pool.map(lambda _: subprocess.run([sys.executable, str(self.repo / 'scripts/tenant_keys.py'), 'rotate', 'lifecycle-a', first['key_id']], env=env, capture_output=True, timeout=30), range(2)))
        self.check('Concurrent rotation has one winner', sorted(r.returncode for r in results) == [0, 1])
        rotated = json.loads(next(r.stdout for r in results if r.returncode == 0))
        self.hidden.extend([rotated['secret'], hashlib.sha256(rotated['secret'].encode()).hexdigest().upper()])
        self.request('GET', '/v1/destinations/GB', key=first['secret'], expected=401)
        self.request('GET', '/v1/destinations/GB', key=rotated['secret'])
        self.sql("ALTER TABLE audit_events ADD CONSTRAINT reject_test_key_audit CHECK (action <> 'api_key_rotate') NOT VALID;")
        command('rotate', 'lifecycle-a', rotated['key_id'], expected=1)
        self.sql('ALTER TABLE audit_events DROP CONSTRAINT reject_test_key_audit;')
        self.request('GET', '/v1/destinations/GB', key=rotated['secret'])
        self.check('Failed audit rolled back rotation', self.sql("SELECT count(*) FROM tenant_api_keys WHERE tenant_id='lifecycle-a';") == '2')
        self.check('Successful lifecycle has tenant-bound audit', self.sql("SELECT count(*) FROM audit_events WHERE tenant_id='lifecycle-a' AND action IN ('api_key_issue','api_key_rotate');") == '2')
        self.sql("UPDATE tenant_api_keys SET expires_at=clock_timestamp() WHERE key_id=" + self.literal(rotated['key_id']) + ';')
        self.request('GET', '/v1/destinations/GB', key=rotated['secret'], expected=401)
        command('revoke', 'lifecycle-a', rotated['key_id'])
        command('revoke', 'lifecycle-a', rotated['key_id'], expected=1)
        self.sql("UPDATE tenants SET enabled=false WHERE tenant_id='lifecycle-a';")
        command('issue', 'lifecycle-a', expected=1)
        self.sql("INSERT INTO tenants(tenant_id,display_name) VALUES ('lifecycle-budget','Budget');")
        spec = json.loads(self.compose_file.read_text(encoding='utf-8'))
        spec['services']['api']['environment']['TenantRateLimit__PermitLimit'] = '2'
        spec['services']['api']['environment']['TenantRateLimit__WindowSeconds'] = '60'
        validate_spec(spec, self.project, self.run_id)
        self.compose_file.write_text(json.dumps(spec), encoding='utf-8')
        self.start_api()
        budget_key = command('issue', 'lifecycle-budget')
        self.request('GET', '/v1/destinations/GB', key=budget_key['secret'])
        budget_rotated = command('rotate', 'lifecycle-budget', budget_key['key_id'])
        self.request('GET', '/v1/destinations/GB', key=budget_rotated['secret'])
        self.request('GET', '/v1/destinations/GB', key=budget_rotated['secret'], expected=429, retry_after=True)
        self.check('No verifier stored in lifecycle audit', self.sql("SELECT count(*) FROM audit_events a JOIN tenant_api_keys k ON a.details_json::text LIKE '%' || k.key_hash || '%' WHERE a.tenant_id='lifecycle-a';") == '0')

    def rate_limits(self):
        """Exercise real tenant and rotated-key limits in a separate API process."""
        self.phase('Checking isolated tenant rate limits')
        first, rotated, other = (self.new_key() for _ in range(3))
        self.provision('rate-a', first)
        self.provision('rate-a', rotated)
        self.provision('rate-b', other)
        spec = json.loads(self.compose_file.read_text(encoding='utf-8'))
        environment = spec['services']['api']['environment']
        environment['TenantRateLimit__PermitLimit'] = '2'
        environment['TenantRateLimit__WindowSeconds'] = '60'
        validate_spec(spec, self.project, self.run_id)
        self.compose_file.write_text(json.dumps(spec), encoding='utf-8')
        self.start_api()
        self.request('GET', '/v1/references/latam/ar', key=first)
        self.request('GET', '/v1/destinations/GB', key=rotated)
        self.request('POST', '/v1/landed-cost/estimate', key=first, payload=b'not json', expected=429, retry_after=True)
        self.request('POST', '/v1/landed-cost/estimate', key=first, payload=self.payload, expected=429, retry_after=True)
        self.check('Rejected estimate produced no audit',
                   self.sql("SELECT count(*) FROM audit_events WHERE tenant_id='rate-a';") == '0')
        self.request('GET', '/v1/references/uk/commodities/1234567890', key=rotated, expected=429, retry_after=True)
        self.request('GET', '/v1/references/five-imports/ID', key=rotated, expected=429, retry_after=True)
        self.request('GET', '/v1/references/gcc/AE', key=rotated, expected=429, retry_after=True)
        self.request('GET', '/v1/references/latam/UY', key=rotated, expected=429, retry_after=True)
        self.request('GET', '/v1/references/au-nz/AU', key=rotated, expected=429, retry_after=True)
        self.request('GET', '/v1/destinations/GB', expected=401)
        self.request('GET', '/v1/references/gcc/SA', expected=401)
        self.request('GET', '/v1/references/five-imports/MY', expected=401)
        self.request('GET', '/v1/references/no-ch-is/NO', expected=401)
        self.request('GET', '/v1/references/no-ch-is/CH', key=rotated, expected=429, retry_after=True)
        self.request('GET', '/v1/references/za-ke-ng-eg/ZA', expected=401)
        self.request('GET', '/v1/references/gh-tz-ug/GH', expected=401)
        self.request('GET', '/v1/references/gh-tz-ug/TZ', key=rotated, expected=429, retry_after=True)
        self.request('GET', '/v1/references/jp-kr-sg/JP', expected=401)
        self.request('GET', '/v1/references/au-nz/AU', expected=401)
        self.request('GET', '/v1/references/north-america/US', key=rotated, expected=429, retry_after=True)
        self.request('GET', '/v1/references/national/IN', key=rotated, expected=429, retry_after=True)
        self.request('GET', '/v1/references/national/CN', key=other)
        self.request('GET', '/v1/references/national/IN', expected=401)
        self.request('GET', '/v1/references/za-ke-ng-eg/ZA', key=rotated, expected=429, retry_after=True)
        self.request('GET', '/v1/references/jp-kr-sg/JP', key=rotated, expected=429, retry_after=True)
        self.request('GET', '/v1/references/latam/PY', expected=401)
        self.request('GET', '/v1/references/north-america/US', expected=401)
        self.request('GET', '/health')
        environment['TenantRateLimit__PermitLimit'] = '10000'
        del environment['TenantRateLimit__WindowSeconds']
        self.compose_file.write_text(json.dumps(spec), encoding='utf-8')
        self.start_api()
        for country in ('BH', 'KW', 'OM', 'QA', 'SA', 'AE'):
            result = self.request('GET', '/v1/references/gcc/' + country.lower(), key=first)
            self.check('GCC reference remains unapproved ' + country,
                       result['country'] == country and result['status'] == 'reference_only' and
                       result['importDuty'] is None and result['importTax'] is None and result['landedTotal'] is None and
                       result['verifiedForCalculation'] is False and result['paymentApproved'] is False and result['shipmentReleaseApproved'] is False)
        for country in ('MY', 'ID', 'TH', 'VN', 'PH'):
            result = self.request('GET', '/v1/references/five-imports/' + country.lower(), key=first)
            self.check('National reference remains unapproved ' + country,
                       result['country'] == country and result['status'] == 'reference_only' and
                       result['importDuty'] is None and result['importTax'] is None and result['landedTotal'] is None and
                       result['verifiedForCalculation'] is False and result['paymentApproved'] is False and result['shipmentReleaseApproved'] is False)
        for country in ('NO', 'CH', 'IS'):
            result = self.request('GET', '/v1/references/no-ch-is/' + country.lower(), key=first)
            self.check('NO/CH/IS reference remains unapproved ' + country, result['country'] == country and result['status'] == 'reference_only' and all(result[x] is None for x in ('importDuty', 'importTax', 'landedTotal')) and not any(result[x] for x in ('verifiedForCalculation', 'paymentApproved', 'shipmentReleaseApproved')))
        for country in ('LI', 'NOR', 'ZZ'):
            self.request('GET', '/v1/references/no-ch-is/' + country, key=first, expected=400)
        for country in ('ZA', 'KE', 'NG', 'EG'):
            result = self.request('GET', '/v1/references/za-ke-ng-eg/' + country.lower(), key=first)
            self.check('Four-country reference remains unapproved ' + country,
                       result['country'] == country and result['status'] == 'reference_only' and
                       all(result[x] is None for x in ('importDuty', 'importTax', 'landedTotal')) and
                       not any(result[x] for x in ('verifiedForCalculation', 'paymentApproved', 'shipmentReleaseApproved')))
        for country in ('GH', 'ZAF'):
            self.request('GET', '/v1/references/za-ke-ng-eg/' + country, key=first, expected=400)
        for country in ('GH', 'TZ', 'UG'):
            result = self.request('GET', '/v1/references/gh-tz-ug/' + country.lower(), key=first)
            self.check('National reference remains unapproved ' + country,
                       result['country'] == country and result['status'] == 'reference_only' and
                       all(result[x] is None for x in ('importDuty', 'importTax', 'landedTotal')) and
                       not any(result[x] for x in ('verifiedForCalculation', 'paymentApproved', 'shipmentReleaseApproved')))
        for country in ('ZA', 'GHA'):
            self.request('GET', '/v1/references/gh-tz-ug/' + country, key=first, expected=400)
        for country in ('JP', 'KR', 'SG'):
            result = self.request('GET', '/v1/references/jp-kr-sg/' + country.lower(), key=first)
            self.check('National reference remains unapproved ' + country,
                       result['country'] == country and result['status'] == 'reference_only' and
                       result['importDuty'] is None and result['importTax'] is None and result['landedTotal'] is None and
                       result['verifiedForCalculation'] is False and result['paymentApproved'] is False and result['shipmentReleaseApproved'] is False)
        self.request('GET', '/v1/references/five-imports/SG', key=first, expected=400)
        self.request('GET', '/v1/references/five-imports/MYS', key=first, expected=400)
        self.request('GET', '/v1/references/jp-kr-sg/HK', key=first, expected=400)
        self.request('GET', '/v1/references/jp-kr-sg/JPN', key=first, expected=400)
        for country in ('US', 'CA', 'MX'):
            result = self.request('GET', '/v1/references/north-america/' + country.lower(), key=first)
            self.check('Country reference remains unapproved ' + country,
                       result['country'] == country and result['status'] == 'reference_only' and
                       result['importDuty'] is None and result['importTax'] is None and result['landedTotal'] is None and
                       result['verifiedForCalculation'] is False and result['paymentApproved'] is False and result['shipmentReleaseApproved'] is False)
        for country in ('PR', 'GB', 'USA'):
            self.request('GET', '/v1/references/north-america/' + country, key=first, expected=400)
        self.request('GET', '/v1/references/gcc/US', key=first, expected=400)
        self.request('GET', '/v1/references/gcc/USA', key=first, expected=400)
        for country in ('AR', 'BR', 'CL', 'CO', 'PE', 'PY', 'UY'):
            result = self.request('GET', '/v1/references/latam/' + country.lower(), key=first)
            self.check('LATAM reference remains unapproved ' + country, result['country'] == country and result['status'] == 'reference_only' and all(result[x] is None for x in ('importDuty', 'importTax', 'landedTotal')) and not any(result[x] for x in ('verifiedForCalculation', 'paymentApproved', 'shipmentReleaseApproved')))
        for country in ('MX', 'ZZ', 'GF', 'BRA'):
            self.request('GET', '/v1/references/latam/' + country, key=first, expected=400)
        for country in ('IN', 'CN'):
            result = self.request('GET', '/v1/references/national/' + country.lower(), key=first)
            self.check('National reference is unresolved and unapproved ' + country,
                       result['country'] == country and result['territoryStatus'] == 'unresolved' and
                       result['status'] == 'reference_only' and result['importDuty'] is None and
                       result['importTax'] is None and result['landedTotal'] is None and
                       not result['verifiedForCalculation'] and not result['paymentApproved'] and not result['shipmentReleaseApproved'])
        self.request('GET', '/v1/references/national/HK', key=first, expected=404)
        self.request('GET', '/v1/references/national/123', key=first, expected=400)

    def staging_and_audit(self):
        self.phase('Checking tariff staging and database audit writes')
        csv = ('destination,origin,hs_code,ad_valorem_percent,valid_from,valid_to,source_url,source_version\n'
               'GB,US,0101210000,9.5,2026-01-01,2026-12-31,https://example.test/test-only,integration-only\n'
               'GB,US,0101299000,12.3,2026-01-01,2026-12-31,https://example.test/test-only,integration-only\n')
        csv = '\ufeff' + csv.replace('\n', '\r\n')
        self.staged_csv = csv
        path = '/v1/admin/tariff-datasets/stage'
        self.request('POST', path, key=self.keys['rotated'], payload=csv, expected=401)
        with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
            batches = list(pool.map(lambda _: self.request('POST', path, operator=True, payload=csv), range(4)))
        self.batch_id = batches[0]['batchId']
        self.check('Concurrent dataset staging deduplicates transactionally',
                   len({batch['batchId'] for batch in batches}) == 1 and
                   sum(not batch['alreadyExists'] for batch in batches) == 1 and
                   self.sql('SELECT count(*) FROM tariff_import_batches;') == '1' and
                   self.sql('SELECT count(*) FROM tariff_reference_rows;') == '2')
        self.request('GET', '/v1/admin/tariff-datasets/' + self.batch_id, key=self.keys['rotated'], expected=401)
        batch = self.request('GET', '/v1/admin/tariff-datasets/' + self.batch_id, operator=True)
        self.check('Staged provenance is preserved', batch['sha256'] == hashlib.sha256(csv.encode()).hexdigest() and batch['rows'] == 2)
        self.check('Exact BOM/CRLF upload archived with database-checked digest', batch['documentArchived'] and batch['review'] is None and
                   self.sql("SELECT encode(document_bytes,'hex') LIKE 'efbbbf%' AND dataset_sha256=encode(sha256(document_bytes),'hex') FROM tariff_import_documents;") == 't')
        self.request('POST', path, operator=True, payload=csv.replace('2026-12-31', '2025-12-31'), expected=400)
        duplicate = csv + csv.split('\r\n')[1] + '\r\n'
        for rejected in (duplicate, csv.replace('9.5,', '9.51234,'), b'\xc3\x28'):
            self.request('POST', path, operator=True, payload=rejected, expected=400)
        oversized = b' ' * (1_048_576 + 1)
        self.request('POST', path, operator=True, payload=oversized, expected=413)
        self.request('POST', path, operator=True, payload=oversized, chunked=True, expected=413)
        self.request('POST', '/v1/tariff-datasets/validate', key=self.keys['rotated'], payload=oversized, chunked=True, expected=413)
        validated = self.request('POST', '/v1/tariff-datasets/validate', key=self.keys['rotated'], payload=csv, chunked=True)
        self.check('Merchant validation preserves exact upload digest without enabling rates', validated['sha256'] == batch['sha256'])
        self.check('Rejected CSV leaves no partial batch', self.sql('SELECT count(*) FROM tariff_import_batches;') == '1')
        self.tariff_reviews(batch)
        probe = copy.deepcopy(self.payload)
        probe['shipTo'] = 'GB'
        probe['lines'][0].update(hsCode='0101210000', countryOfOrigin='US')
        quote = self.request('POST', '/v1/quotes', key=self.keys['rotated'], payload=probe, idempotency='staged-not-payable')
        self.incomplete_quote(quote)
        estimate = self.request('POST', '/v1/landed-cost/estimate', key=self.keys['rotated'], payload=probe)
        self.check('Landed-cost estimate remains non-payable', estimate['total'] is None and estimate['importDuty'] is None and estimate['importTax'] is None)
        self.check('Audit JSON is stored as an object for the authenticated merchant',
                   self.sql("SELECT count(*) FROM audit_events WHERE tenant_id='merchant-a' AND action='landed_cost_estimate' AND jsonb_typeof(details_json)='object';") == '1')
        path = '/v1/references/uk/shipments/review'
        for territory in ('GB', 'XI'):
            review = self.request('POST', path, key=self.keys['rotated'], payload={
                'destinationTerritory': territory, 'importDate': '2026-11-05', 'shipFrom': 'US',
                'importProcedure': 'free_circulation', 'currency': 'GBP', 'costs': [],
                'transactionKind': 'commercial', 'intrinsicValueGbp': 1000,
                'lines': [{'lineId': 'a', 'commodityCode': '0101299000', 'goodsOrigin': 'CN', 'customsValue': 1000,
                           'vatTreatment': {'treatment': 'returned_goods_relief', 'evidenceReference': 'synthetic-return'}}]})
            self.check('UK review retains uncertainty without configured territory sources',
                       review['status'] == 'requires_review' and review['destinationTerritory'] == territory and
                       review['approvedForPayment'] is False and review['verifiedForCalculation'] is False and
                       all(review[name] is None for name in ('payableDutyGbp', 'payableVatGbp', 'payableLandedTotalGbp', 'illustrativeTotals')))
            decision = review['lines'][0]['vatTreatmentReview']
            self.check('VAT claim is retained as unapproved review evidence',
                       review['schemaVersion'] == 'uk-shipment-review/v2' and decision['claimedTreatment'] == 'returned_goods_relief' and
                       decision['declaredEvidenceReference'] == 'synthetic-return' and decision['approvedForPayment'] is False and
                       decision['suppressOrdinaryIllustration'] is True)
        for malformed in ('{"currency":"GBP","Currency":"USD"}', '{"intrinsicValueGbp":1e-100}',
                          '{"intrinsicValueGbp":"1e-100"}'):
            self.request('POST', path, key=self.keys['rotated'], payload=malformed, expected=400)
        self.request('POST', path, key=self.keys['rotated'], payload=' ' * 262145, expected=413)
        self.check('UK evidence audit is tenant-bound, contains a digest and omits shipment details',
                   self.sql("SELECT count(*) FROM audit_events WHERE tenant_id='merchant-a' AND action='uk_shipment_review' AND details_json->>'approvedForPayment'='false' AND length(details_json->>'evidenceSha256')=64 AND NOT details_json ? 'lines';") == '2')

    def compliance_holds(self):
        """Prove strict ingress, consistent declared holds and durable tenant/audit evidence."""
        self.phase('Checking declared control holds, exact request parsing and durable quote evidence')
        paths = ('/v1/quotes', '/v1/landed-cost/estimate', '/v1/compliance/check')
        before_quotes = int(self.sql('SELECT count(*) FROM quotes;'))
        before_audits = int(self.sql('SELECT count(*) FROM audit_events;'))
        held = copy.deepcopy(self.payload)
        held.update(shipTo='GB', currency='GBP', itarControlled=True)
        duplicate = json.dumps(held)[:-1] + ',"ItarControlled":false}'
        # Build the numeric probe directly, independently of the sample price spelling.
        exponent_probe = copy.deepcopy(held)
        exponent_probe['lines'][0]['unitPrice'] = 'INEXACT_NUMBER'
        exponent = json.dumps(exponent_probe).replace('"INEXACT_NUMBER"', '1e-100')
        invalid_payloads = [b'{', b'null', duplicate, exponent]
        for changed in ({'lines': [None]}, {'lines': None}, {'lines': []},
                        {'lines': held['lines'] * 101}, {'shipFrom': '12'}, {'currency': 'G1P'},
                        {'itarControlled': 'false'}):
            invalid_payloads.append({**held, **changed})
        overflow = copy.deepcopy(held)
        overflow['lines'][0].update(unitPrice=9999999999999999999999999999, quantity=100)
        invalid_payloads.append(overflow)
        for path in paths:
            self.request('POST', path, payload=b'{', expected=401)
            self.request('POST', path, payload=b' ' * 65537, expected=401)
            for invalid in invalid_payloads:
                self.request('POST', path, key=self.keys['rotated'], payload=invalid,
                             idempotency='invalid-control-input', expected=400)
            self.request('POST', path, key=self.keys['rotated'], payload=b' ' * 65537,
                         idempotency='oversized-control-input', expected=413)
            self.request('POST', path, key=self.keys['rotated'], payload=b' ' * 65537,
                         idempotency='oversized-control-input', chunked=True, expected=413)
        self.check('Rejected quote/control requests create neither quotes nor audit rows',
                   int(self.sql('SELECT count(*) FROM quotes;')) == before_quotes and
                   int(self.sql('SELECT count(*) FROM audit_events;')) == before_audits)
        self.held_quotes = []
        cases = [('US', 'GB', 'GBP', True, False), ('US', 'XI', 'USD', False, True), ('CN', 'GB', 'EUR', True, True)]
        for index, (dispatch, destination, currency, licence, itar) in enumerate(cases):
            payload = copy.deepcopy(self.payload)
            payload.update(shipFrom=dispatch, shipTo=destination, currency=currency,
                           requiresExportLicense=licence, itarControlled=itar)
            compliance = self.request('POST', paths[2], key=self.keys['rotated'], payload=payload)
            quote = self.request('POST', paths[0], key=self.keys['rotated'], payload=payload, idempotency='control-hold-' + str(index))
            estimate = self.request('POST', paths[1], key=self.keys['rotated'], payload=payload)
            self.check('Declared control holds agree across compliance, stored quote and landed-cost routes',
                       compliance == quote['compliance'] == estimate['compliance'] and
                       compliance['specialistHoldRequired'] is True and compliance['shipmentReleaseApproved'] is False and
                       ('declared_export_licence' in compliance['holdReasons']) == licence and
                       ('declared_itar_control' in compliance['holdReasons']) == itar and
                       quote['status'] == estimate['status'] == 'manual_review_required' and
                       all(quote[name] is None for name in ('importDuty', 'importTax', 'landedTotal')) and
                       all(estimate[name] is None for name in ('importDuty', 'importTax', 'clearanceFee', 'total', 'evidence')))
            replay = self.request('POST', paths[0], key=self.keys['rotated'], payload=payload, idempotency='control-hold-' + str(index))
            self.check('Held quote replay retains the same evidence', replay == quote)
            self.request('POST', paths[0], key=self.keys['rotated'], payload={**payload, 'requiresExportLicense': not licence},
                         idempotency='control-hold-' + str(index), expected=409)
            self.held_quotes.append((quote, payload, 'control-hold-' + str(index)))
        self.expected_audit_count = before_audits + len(cases)
        self.check('Held estimates are audited with false release approval and explicit reasons',
                   int(self.sql('SELECT count(*) FROM audit_events;')) == self.expected_audit_count and
                   self.sql("SELECT count(*) FROM audit_events WHERE tenant_id='merchant-a' AND status='manual_review_required' "
                            "AND details_json->>'specialistHoldRequired'='true' AND details_json->>'shipmentReleaseApproved'='false' "
                            "AND jsonb_array_length(details_json->'holdReasons')>0;") == str(len(cases)))

    def vat_review_exceptions(self):
        self.phase('Checking VAT relief, conditional valuation, accounting and strict financial JSON')
        path = '/v1/references/uk/vat/review'
        classification = dict(destinationTerritory='GB', importProcedure='free_circulation', commodityCode='9701100000',
                              goodsOrigin='US', shipFrom='US', importDate='2026-11-05', proposedVatRatePercent=20,
                              rateSourceUrl='https://www.gov.uk/guidance/how-to-value-goods-for-import-vat', rateEffectiveFrom='2026-11-02',
                              commodityClassificationVerified=True, importProcedureVerified=True, vatTreatmentVerified=True,
                              reliefsAndExemptionsVerified=True, sourceContractVerified=True,
                              vatTreatment=dict(treatment='reduced_valuation_art', accountingMethod='pay_at_import', evidenceReference='synthetic-art'))
        amounts = dict(customsValueGbp=1000, importDutyGbp=100, freightToFirstDestinationGbp=30, insuranceGbp=10,
                       otherTaxableCostsGbp=0, vatRatePercent=20, customsValueVerified=True, dutyVerified=True,
                       costsVerified=True, vatRateVerified=True, reliefsAndExemptionsVerified=True)
        payload = dict(classification=classification, amounts=amounts, amountCurrency='GBP')
        self.request('POST', path, payload=b'{', expected=401)
        result = self.request('POST', path, key=self.keys['rotated'], payload=payload)
        decision = result['vatTreatmentReview']
        self.check('Art valuation is conditional and never replaces ordinary or payable VAT',
                   result['status'] == 'requires_review' and result['amounts']['illustrativeVatGbp'] is None and
                   result['payableVatGbp'] is None and result['verifiedForCalculation'] is False and
                   decision['approvedForPayment'] is False and decision['conditionalScenario']['conditionalBaseGbp'] == 285 and
                   decision['conditionalScenario']['conditionalVatGbp'] == 57)
        for treatment in ('returned_goods_relief', 'temporary_admission', 'vat_exemption', 'other_relief', 'unknown'):
            probe = copy.deepcopy(payload)
            probe['classification'].update(proposedVatRatePercent=0, vatTreatment=dict(treatment=treatment))
            probe['amounts']['vatRatePercent'] = 0
            result = self.request('POST', path, key=self.keys['rotated'], payload=probe)
            self.check('A relief claim does not turn self-asserted evidence into zero VAT',
                       result['amounts']['illustrativeVatGbp'] is None and result['payableVatGbp'] is None and
                       result['vatTreatmentReview']['conditionalScenario'] is None)
        probe = copy.deepcopy(payload)
        probe['classification']['vatTreatment'] = dict(treatment='ordinary', accountingMethod='postponed_accounting')
        result = self.request('POST', path, key=self.keys['rotated'], payload=probe)
        self.check('Postponed accounting retains VAT liability without enabling payment',
                   result['amounts']['illustrativeVatGbp'] == 228 and result['payableVatGbp'] is None)
        for territory, dispatch in (('XI', 'GB'), ('XI', 'US'), ('GB', 'XI')):
            probe = copy.deepcopy(payload)
            probe['classification'].update(destinationTerritory=territory, shipFrom=dispatch)
            result = self.request('POST', path, key=self.keys['rotated'], payload=probe)
            self.check('NI movement context prevents ordinary and conditional VAT amounts',
                       result['amounts']['illustrativeVatGbp'] is None and result['vatTreatmentReview']['conditionalScenario'] is None and
                       result['vatTreatmentReview']['suppressOrdinaryIllustration'] is True)
        probe = copy.deepcopy(payload)
        probe['classification']['vatTreatment'] = dict(treatment='approved')
        self.request('POST', path, key=self.keys['rotated'], payload=probe, expected=400)
        for malformed in (b'{', b'{"amountCurrency":"GBP","AmountCurrency":"USD"}',
                          b'{"amounts":{"customsValueGbp":1e-100}}', b'{"amounts":{"customsValueGbp":"1e-100"}}'):
            self.request('POST', path, key=self.keys['rotated'], payload=malformed, expected=400)
        self.request('POST', path, key=self.keys['rotated'], payload=b' ' * 65537, expected=413)
        self.request('POST', path, key=self.keys['rotated'], payload=b' ' * 65537, chunked=True, expected=413)

    def tariff_reviews(self, batch):
        self.phase('Checking reference reviews, expiry, revision preconditions and immutable history')
        path = '/v1/admin/tariff-datasets/' + self.batch_id + '/reviews'
        future = (datetime.now(timezone.utc) + timedelta(seconds=15)).isoformat()
        review = dict(expectedDatasetSha256=batch['sha256'], disposition='reviewed_reference_only',
                      reviewedBy='integration-operator', licenceUrl='https://example.test/test-only-licence',
                      expiresAt=future, note='Synthetic operator attestation only; not official tariff data.')
        self.request('POST', path, payload=review, expected=401)
        self.request('POST', path, key=self.keys['rotated'], payload=review, expected=401)
        self.request('GET', path, key=self.keys['rotated'], expected=401)
        self.request('POST', path, operator=True, payload={**review, 'expectedDatasetSha256': '0' * 64}, expected=409)
        self.request('POST', path, operator=True, payload={**review, 'licenceUrl': None}, expected=400)
        self.request('POST', path, operator=True, payload={**review, 'note': 'x' * 40000}, expected=413)
        self.request('POST', path, operator=True, payload={**review, 'note': 'x' * 40000}, chunked=True, expected=413)
        self.request('POST', path, operator=True, payload=b'{', expected=400)
        self.sql("UPDATE tariff_reference_rows SET ad_valorem_percent=99 WHERE batch_id=" + self.literal(self.batch_id) + ' AND row_number=1;')
        self.request('POST', path, operator=True, payload=review, expected=409)
        self.sql("UPDATE tariff_reference_rows SET ad_valorem_percent=9.5 WHERE batch_id=" + self.literal(self.batch_id) + ' AND row_number=1;')
        self.sql("UPDATE tariff_import_documents SET document_bytes='not the upload'::bytea;", error_state='23514')
        with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
            results = list(pool.map(lambda _: self.request('POST', path, operator=True, payload=review), range(4)))
        first = results[0]['review']
        self.check('Concurrent identical reviews record one immutable revision',
                   len({item['review']['reviewId'] for item in results}) == 1 and
                   sum(item['status'] == 'recorded' for item in results) == 1 and first['revision'] == 1 and
                   self.sql('SELECT count(*) FROM tariff_reference_reviews;') == '1')
        self.check('Review snapshots digest/source and makes no calculation or licensing claim',
                   first['datasetSha256'] == batch['sha256'] and first['sourceUrl'] == batch['sourceUrl'] and
                   not first['approvedForCalculation'] and first['licenceVerification'] == 'operator_attestation_only')
        self.request('POST', path, operator=True, payload={**review, 'note': 'Changed without previous review'}, expected=409)
        deadline = time.monotonic() + 25
        while True:
            # Poll directly to avoid counting timing-dependent attempts as integration assertions.
            req = Request(self.base + '/v1/admin/tariff-datasets/' + self.batch_id,
                          headers={'X-Operator-Key': self.env['GTT_TEST_OPERATOR_KEY']})
            with build_opener(NoRedirects()).open(req, timeout=12) as response:
                expired = json.load(response)
            if expired['review']['evidenceStatus'] == 'expired_reference_review':
                break
            if time.monotonic() >= deadline:
                raise VerificationError('Reference review did not expire at its declared timestamp')
            time.sleep(.25)
        self.check('Expiry is recomputed at read time without modifying review history',
                   expired['review']['reviewId'] == first['reviewId'] and not expired['approvedForCalculation'] and
                   self.sql('SELECT count(*) FROM tariff_reference_reviews;') == '1')
        replay = self.request('POST', path, operator=True, payload=review)
        self.check('Identical review replay remains idempotent after expiry', replay['status'] == 'already_recorded' and
                   replay['review']['reviewId'] == first['reviewId'] and replay['review']['evidenceStatus'] == 'expired_reference_review')
        renewal = {**review, 'previousReviewId': first['reviewId'], 'expiresAt': (datetime.now(timezone.utc) + timedelta(days=1)).isoformat()}
        renewed = self.request('POST', path, operator=True, payload=renewal)['review']
        self.check('Renewal appends a revision and retains the expired decision', renewed['revision'] == 2 and
                   renewed['previousReviewId'] == first['reviewId'] and self.sql('SELECT count(*) FROM tariff_reference_reviews;') == '2')
        rejection = {**review, 'previousReviewId': renewed['reviewId'], 'disposition': 'rejected', 'expiresAt': None,
                     'licenceUrl': None, 'note': 'Synthetic rejection: rights evidence withdrawn.'}
        rejected = self.request('POST', path, operator=True, payload=rejection)['review']
        self.check('Rejection appends evidence without deleting or activating data', rejected['revision'] == 3 and rejected['evidenceStatus'] == 'rejected')
        choices = [{**renewal, 'previousReviewId': rejected['reviewId'], 'note': 'Concurrent renewed attestation'},
                   {**rejection, 'previousReviewId': rejected['reviewId'], 'note': 'Concurrent rejection attestation'}]
        with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:
            raced = list(pool.map(lambda value: self.request('POST', path, operator=True, payload=value, expected=(200, 409)), choices))
        self.check('Competing reviews with the same predecessor have one winner', sorted(r['status'] for r in raced) == ['conflict', 'recorded'])
        self.review_id = next(r['review']['reviewId'] for r in raced if r['status'] == 'recorded')
        history = self.request('GET', path + '?limit=2', operator=True)
        later = self.request('GET', path + '?limit=2&afterRevision=' + str(history['nextAfterRevision']), operator=True)
        self.check('Bounded history pagination preserves all four decisions',
                   [r['revision'] for r in history['reviews'] + later['reviews']] == [1, 2, 3, 4] and history['hasMore'] and not later['hasMore'])
        self.check('First review snapshot survives subsequent decisions', history['reviews'][0]['reviewId'] == first['reviewId'] and
                   history['reviews'][0]['note'] == first['note'])
        self.request('GET', path + '?limit=0', operator=True, expected=400)
        self.request('GET', '/v1/admin/tariff-datasets/' + str(uuid.uuid4()) + '/reviews', operator=True, expected=404)
        self.request('POST', '/v1/admin/tariff-datasets/' + str(uuid.uuid4()) + '/reviews', operator=True, payload=renewal, expected=404)
        self.request('POST', path, operator=True, payload={**renewal, 'previousReviewId': self.review_id,
                     'expiresAt': (datetime.now(timezone.utc) - timedelta(days=1)).isoformat()}, expected=400)
        self.sql("INSERT INTO tariff_import_documents(batch_id,dataset_sha256,document_bytes) SELECT " + self.literal(str(uuid.uuid4())) +
                 '::uuid,dataset_sha256,document_bytes FROM tariff_import_documents LIMIT 1;', error_state='23503')

    def verify_saved_data(self, database='trade'):
        """Compare quotes, hold snapshots, archives and audits after restart or restoration."""
        quote = self.request('GET', '/v1/quotes/' + self.quote_a['quoteId'], key=self.keys['rotated'])
        self.check('Saved quote remains equivalent as JSON', quote == self.quote_a)
        replay = self.request('POST', '/v1/quotes', key=self.keys['rotated'], payload=self.payload, idempotency='shared-key')
        self.check('Idempotency survives recreation/restore', replay == self.quote_a)
        self.request('GET', '/v1/quotes/' + self.quote_a['quoteId'], key=self.keys['a'], expected=401)
        self.request('GET', '/v1/quotes/' + self.quote_a['quoteId'], key=self.keys['b'], expected=401)
        batch = self.request('GET', '/v1/admin/tariff-datasets/' + self.batch_id, operator=True)
        self.check('Staging survives recreation/restore', batch['rows'] == 2 and
                   self.sql('SELECT count(*) FROM tariff_reference_rows;', database=database) == '2')
        self.check('Exact archive and latest review survive recreation/restore', batch['documentArchived'] and
                   batch['review']['reviewId'] == self.review_id and not batch['approvedForCalculation'] and
                   self.sql('SELECT count(*) FROM tariff_reference_reviews;', database=database) == '4' and
                   self.sql("SELECT dataset_sha256=encode(sha256(document_bytes),'hex') FROM tariff_import_documents;", database=database) == 't')
        for quote, payload, idempotency in self.held_quotes:
            saved = self.request('GET', '/v1/quotes/' + quote['quoteId'], key=self.keys['rotated'])
            replay = self.request('POST', '/v1/quotes', key=self.keys['rotated'], payload=payload, idempotency=idempotency)
            self.check('Declared hold and its idempotency survive recreation/restore', saved == replay == quote)
        self.check('Audit survives recreation/restore',
                   self.sql('SELECT count(*) FROM audit_events;', database=database) == str(self.expected_audit_count))

    def persistence_and_restore(self):
        """Recreate private containers and restore a backup without losing review evidence."""
        self.phase('Checking container recreation and isolated backup restoration')
        old_api = self.compose('ps', '-q', 'api').stdout.strip()
        self.start_api()
        self.check('API container was recreated', self.compose('ps', '-q', 'api').stdout.strip() != old_api)
        self.verify_saved_data()
        old_pg = self.compose('ps', '-q', 'postgres').stdout.strip()
        self.compose('stop', 'api')
        self.compose('up', '-d', '--no-deps', '--force-recreate', 'postgres', timeout=100)
        # The API polls below; wait for the replacement database's TCP listener first.
        deadline = time.monotonic() + 60
        while time.monotonic() < deadline:
            ready = self.compose('exec', '-T', 'postgres', 'pg_isready', '-h', '127.0.0.1', '-U', 'trade', '-d', 'trade', check=False)
            if ready.returncode == 0:
                break
            time.sleep(1)
        else:
            raise VerificationError('Recreated PostgreSQL did not become ready')
        self.check('Database container recreated with its original volume', self.compose('ps', '-q', 'postgres').stdout.strip() != old_pg)
        self.start_api()
        self.verify_saved_data()
        dump = self.compose('exec', '-T', 'postgres', 'pg_dump', '-U', 'trade', '-d', 'trade', '-Fc', '--no-owner', '--no-acl').stdout
        self.check('Custom-format backup produced', dump.startswith(b'PGDMP'))
        self.compose('exec', '-T', 'postgres', 'createdb', '-U', 'trade', 'trade_restore')
        self.compose('exec', '-T', 'postgres', 'pg_restore', '-U', 'trade', '-d', 'trade_restore',
                     '--no-owner', '--no-acl', '--exit-on-error', '--single-transaction', data=dump)
        self.check('Restored schema matches fresh schema', self.sql(SCHEMA_SQL, database='trade_restore') == self.fresh_schema)
        self.start_api(database='trade_restore')
        self.verify_saved_data(database='trade_restore')
        new_quote = self.request('POST', '/v1/quotes', key=self.keys['rotated'], payload=self.payload, idempotency='after-restore')
        self.incomplete_quote(new_quote)
        self.request('POST', '/v1/landed-cost/estimate', key=self.keys['rotated'], payload=self.payload)
        self.check('Restored database accepts audit writes with its restored identity sequence',
                   self.sql('SELECT count(*) FROM audit_events;', database='trade_restore') == str(self.expected_audit_count + 1))
        before_adoption = self.sql("SELECT json_build_array((SELECT count(*) FROM quotes),(SELECT count(*) FROM tariff_import_documents),(SELECT count(*) FROM tariff_reference_reviews),(SELECT count(*) FROM audit_events))::text;", database='trade_restore')
        self.migration_command('trade_restore', baseline='current')
        self.check('Current adoption preserves quotes, archives, reviews and audits', before_adoption == self.sql("SELECT json_build_array((SELECT count(*) FROM quotes),(SELECT count(*) FROM tariff_import_documents),(SELECT count(*) FROM tariff_reference_reviews),(SELECT count(*) FROM audit_events))::text;", database='trade_restore'))
        self.request('POST', '/v1/landed-cost/estimate', key=self.keys['rotated'], payload=self.payload)
        self.check('Audit identity still writes after current adoption', self.sql('SELECT count(*) FROM audit_events;', database='trade_restore') == str(self.expected_audit_count + 2))
        self.report['backup_bytes'] = len(dump)

    def upgrade(self):
        self.phase('Checking upgrade, reapplication and transactional rollback')
        service = 'upgrade-postgres'
        self.check('Legacy volume starts with quotes only',
                   self.sql("SELECT to_regclass('public.tenants') IS NULL;", service=service) == 't')
        legacy_id = uuid.uuid4().hex
        legacy = dict(QuoteId=legacy_id, Status='requires_provider_quote', MerchandiseValue=70,
                      Freight=15, Insurance=0, ImportDuty=None, ImportTax=None, LandedTotal=None,
                      EeiMayBeRequired=False, RuleVersion='legacy-test', Warnings=['Historical test quote'], RequiredActions=[])
        self.sql('INSERT INTO quotes(quote_id,tenant_id,idempotency_key,fingerprint,response_json) VALUES (' +
                 ','.join(self.literal(value) for value in (legacy_id, 'legacy-merchant', 'legacy-key', 'legacy-fingerprint', json.dumps(legacy))) + ');', service=service)
        scripts = sorted((self.repo / 'db').glob('*.sql'))
        self.check('Exactly the six reviewed migration scripts are used', [path.name for path in scripts] ==
                   ['001_init.sql', '002_tenants.sql', '003_audit.sql', '004_tariff_staging.sql', '005_tariff_provenance.sql', '006_api_key_expiry.sql'])
        self.sql('\n'.join(path.read_text() for path in scripts[1:3]) + '\n' +
                 (self.repo / 'tests/fixtures/postgres/v12-tariff-schema.sql').read_text(), service=service)
        old_batch_id = str(uuid.uuid4())
        old_sha = hashlib.sha256(self.staged_csv.encode()).hexdigest()
        self.sql("INSERT INTO tariff_import_batches(batch_id,dataset_sha256,source_version,source_url,record_count) VALUES (" +
                 ','.join(self.literal(v) for v in (old_batch_id, old_sha, 'integration-only', 'https://example.test/test-only')) + ",2);" +
                 "INSERT INTO tariff_reference_rows VALUES (" + self.literal(old_batch_id) +
                 ",1,'GB','US','0101210000',9.5,'2026-01-01','2026-12-31','https://example.test/test-only','integration-only'),(" +
                 self.literal(old_batch_id) + ",2,'GB','US','0101299000',12.3,'2026-01-01','2026-12-31','https://example.test/test-only','integration-only');", service=service)
        self.sql('\n'.join(path.read_text() for path in scripts[4:]), service=service)
        self.check('Upgraded schema matches fresh initialization', self.sql(SCHEMA_SQL, service=service) == self.fresh_schema)
        self.sql('\n'.join(path.read_text() for path in scripts), service=service)
        self.check('Reapplying schema scripts preserves legacy data',
                   self.sql('SELECT response_json FROM quotes;', service=service) == json.dumps(legacy))
        self.sql('CREATE TABLE rollback_probe(id integer); SELECT 1/0;', service=service, error_state='22012')
        self.check('Failed migration transaction leaves no schema changes', self.sql(SCHEMA_SQL, service=service) == self.fresh_schema)
        self.provision('legacy-merchant', self.keys['legacy'], service=service)
        self.start_api(host=service)
        old_batch = self.request('GET', '/v1/admin/tariff-datasets/' + old_batch_id, operator=True)
        self.check('Pre-provenance staging survives migration without invented archive or review',
                   old_batch['rows'] == 2 and not old_batch['documentArchived'] and old_batch['review'] is None)
        old_review = dict(expectedDatasetSha256=old_sha, disposition='reviewed_reference_only', reviewedBy='upgrade-test',
                          licenceUrl='https://example.test/licence', expiresAt=(datetime.now(timezone.utc) + timedelta(days=1)).isoformat(),
                          note='Synthetic historical migration test only.')
        self.request('POST', '/v1/admin/tariff-datasets/' + old_batch_id + '/reviews', operator=True, payload=old_review, expected=409)
        self.sql("UPDATE tariff_reference_rows SET ad_valorem_percent=99 WHERE batch_id=" + self.literal(old_batch_id) + ' AND row_number=1;', service=service)
        self.request('POST', '/v1/admin/tariff-datasets/stage', operator=True, payload=self.staged_csv, expected=409)
        self.check('Mismatching legacy rows cannot acquire an archive', self.sql('SELECT count(*) FROM tariff_import_documents;', service=service) == '0')
        self.sql("UPDATE tariff_reference_rows SET ad_valorem_percent=9.5 WHERE batch_id=" + self.literal(old_batch_id) + ' AND row_number=1;', service=service)
        backfill = self.request('POST', '/v1/admin/tariff-datasets/stage', operator=True, payload=self.staged_csv)
        self.check('Exact matching legacy upload backfills the archive without duplicate rows', backfill['alreadyExists'] and
                   backfill['batchId'] == old_batch_id and self.sql('SELECT count(*) FROM tariff_reference_rows;', service=service) == '2')
        self.request('POST', '/v1/admin/tariff-datasets/' + old_batch_id + '/reviews', operator=True, payload=old_review)
        restored = self.request('GET', '/v1/quotes/' + legacy_id, key=self.keys['legacy'])
        self.check('API reads pre-upgrade quote after migration', restored['quoteId'] == legacy_id and restored['merchandiseValue'] == 70)
        self.incomplete_quote(restored)
        new_quote = self.request('POST', '/v1/quotes', key=self.keys['legacy'], payload=self.payload, idempotency='after-upgrade')
        self.incomplete_quote(new_quote)
        self.check('New writes preserve the historical quote', self.sql('SELECT count(*) FROM quotes;', service=service) == '2')

    def migration_command(self, database, mode='apply', baseline=None, *, sql=None, check=True):
        """Run the same generated operator transaction through psql in our owned container."""
        if sql is None:
            args = [sys.executable, str(self.repo / 'scripts/migrate_postgres.py'), mode]
            if baseline:
                args += ['--adopt', baseline]
            result = self.command(args)
            sql = result.stdout.decode('utf-8')
        return self.compose('exec', '-T', 'postgres', 'psql', '-X', '-qAt', '-U', 'trade',
                            '-d', database, '-v', 'ON_ERROR_STOP=1', '-f', '-',
                            data=sql.encode(), check=check)

    def migration_rejected(self, name, result, diagnostic):
        """Require the intended failure, not merely an unrelated psql error."""
        self.check(name, result.returncode != 0 and diagnostic in result.stderr.decode('utf-8', errors='replace'))

    def migration_ledger(self):
        """Exercise adoption, integrity, serialization, atomic failure and restored history."""
        self.phase('Checking versioned migration ledger')
        for database in ('migration_fresh', 'migration_v04', 'migration_v12', 'migration_current', 'migration_v13', 'migration_bad', 'migration_fail', 'migration_unsupported'):
            self.compose('exec', '-T', 'postgres', 'createdb', '-U', 'trade', database)
        self.migration_command('migration_fresh')
        self.check('Fresh ledger records six applied migrations',
                   self.sql("SELECT count(*) FROM gtt_schema_migrations WHERE origin='applied';", database='migration_fresh') == '6')
        before = self.sql('SELECT json_agg(m ORDER BY version) FROM gtt_schema_migrations m;', database='migration_fresh')
        self.migration_command('migration_fresh')
        self.migration_command('migration_fresh', 'status')
        self.check('Repeat and status preserve ledger records', before == self.sql(
            'SELECT json_agg(m ORDER BY version) FROM gtt_schema_migrations m;', database='migration_fresh'))
        migrations = migrate_postgres.load_migrations(self.repo / 'db')
        fixture04 = (self.repo / 'tests/fixtures/postgres/v04-schema.sql').read_text()
        fixture12 = (self.repo / 'tests/fixtures/postgres/v12-tariff-schema.sql').read_text()
        self.sql(fixture04 + "\nINSERT INTO quotes(quote_id,tenant_id,idempotency_key,fingerprint,response_json) VALUES ('old-quote','old-tenant','old-key','old-fingerprint','{}');", database='migration_v04')
        self.sql(fixture04 + '\n' + '\n'.join(migrations[1:3]) + '\n' + fixture12, database='migration_v12')
        self.sql('\n'.join(migrations), database='migration_current')
        self.sql('\n'.join(migrations[:5]), database='migration_v13')
        self.sql("INSERT INTO tariff_import_batches(batch_id,dataset_sha256,source_version,source_url,record_count) VALUES ('00000000-0000-0000-0000-000000000001',repeat('a',64),'old','https://example.test',1);", database='migration_v12')
        for database, baseline, adopted in [('migration_v04', 'v0.4', 1), ('migration_v12', 'v1.2', 4), ('migration_current', 'current', 6), ('migration_v13', 'v1.3', 5)]:
            self.migration_command(database, baseline=baseline)
            self.check('Adoption records honest baseline ' + baseline,
                       self.sql("SELECT count(*) FROM gtt_schema_migrations WHERE origin='adopted';", database=database) == str(adopted))
            self.check('Upgrade completes ' + baseline,
                       self.sql('SELECT count(*) FROM gtt_schema_migrations;', database=database) == '6')
        self.check('Adoption preserves historical quote', self.sql("SELECT count(*) FROM quotes WHERE quote_id='old-quote';", database='migration_v04') == '1')
        self.check('Adoption preserves historical staging', self.sql("SELECT count(*) FROM tariff_import_batches WHERE source_version='old';", database='migration_v12') == '1')
        self.sql('ALTER TABLE quotes ADD COLUMN unexpected text;', database='migration_v04')
        self.migration_rejected('Tracked schema drift rejected', self.migration_command('migration_v04', check=False), 'Schema does not match')
        self.sql('ALTER TABLE quotes DROP COLUMN unexpected;', database='migration_v04')
        self.sql("CREATE FUNCTION public.test_trigger() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RETURN NEW; END $$; CREATE TRIGGER test_trigger BEFORE INSERT ON quotes FOR EACH ROW EXECUTE FUNCTION public.test_trigger(); ALTER TABLE quotes DISABLE TRIGGER test_trigger;", database='migration_v04')
        self.migration_rejected('Unsupported trigger and function are rejected', self.migration_command('migration_v04', check=False), 'Unsupported schema object')
        self.sql('DROP TRIGGER test_trigger ON quotes; DROP FUNCTION public.test_trigger();', database='migration_v04')
        self.sql("CREATE TYPE public.test_enum AS ENUM ('x');", database='migration_v04')
        self.migration_rejected('Unsupported custom type is rejected', self.migration_command('migration_v04', check=False), 'Unsupported schema object')
        self.sql('DROP TYPE public.test_enum; CREATE POLICY test_policy ON quotes USING (true);', database='migration_v04')
        self.migration_rejected('Unsupported policy is rejected', self.migration_command('migration_v04', check=False), 'Unsupported schema object')
        self.sql('DROP POLICY test_policy ON quotes;', database='migration_v04')
        self.sql(fixture04 + '\nALTER TABLE quotes ADD COLUMN unexpected text;', database='migration_bad')
        self.migration_rejected('Mismatched adoption rejected', self.migration_command('migration_bad', baseline='v0.4', check=False), 'Schema does not match')
        self.check('Rejected adoption leaves no ledger', self.sql("SELECT to_regclass('public.gtt_schema_migrations') IS NULL;", database='migration_bad') == 't')
        self.migration_rejected('Untracked schema requires adoption', self.migration_command('migration_bad', check=False), 'Untracked schema')
        self.sql("UPDATE gtt_schema_migrations SET checksum=repeat('0',64) WHERE version=2;", database='migration_fresh')
        self.migration_rejected('Checksum drift rejected', self.migration_command('migration_fresh', check=False), 'Migration history is inconsistent')
        self.sql("UPDATE gtt_schema_migrations SET checksum=" + self.literal(migrate_postgres.MANIFEST[1][1]) + ' WHERE version=2;', database='migration_fresh')
        self.sql('DELETE FROM gtt_schema_migrations WHERE version=2;', database='migration_fresh')
        self.migration_rejected('History gap rejected', self.migration_command('migration_fresh', check=False), 'Migration history is inconsistent')
        self.sql("INSERT INTO gtt_schema_migrations(version,name,checksum,origin) VALUES (2," + self.literal(migrate_postgres.MANIFEST[1][0]) + ',' + self.literal(migrate_postgres.MANIFEST[1][1]) + ",'applied');", database='migration_fresh')
        failing = migrate_postgres.build_sql(migrations, 'apply').replace(migrations[4], migrations[4] + '\nSELECT 1/0;')
        self.migration_rejected('Failed migration returns error', self.migration_command('migration_fail', sql=failing, check=False), 'division by zero')
        self.check('Failed transaction leaves neither schema nor ledger', self.sql("SELECT to_regclass('public.quotes') IS NULL AND to_regclass('public.gtt_schema_migrations') IS NULL;", database='migration_fail') == 't')
        generated = migrate_postgres.build_sql(migrations, 'apply')
        insert5 = "INSERT INTO public.gtt_schema_migrations(version,name,checksum,origin) VALUES (5,"
        offset = generated.index(insert5)
        generated = generated[:offset] + "CREATE FUNCTION public.unsupported() RETURNS integer LANGUAGE sql AS $$ SELECT 1 $$;\n" + generated[offset:]
        self.migration_rejected('Unsupported pending migration rolls back', self.migration_command('migration_unsupported', sql=generated, check=False), 'Unsupported schema object')
        self.check('Unsupported pending migration leaves no ledger', self.sql("SELECT to_regclass('public.gtt_schema_migrations') IS NULL AND to_regclass('public.quotes') IS NULL;", database='migration_unsupported') == 't')
        with concurrent.futures.ThreadPoolExecutor(max_workers=2) as executor:
            results = list(executor.map(lambda _: self.migration_command('migration_fail', check=False), range(2)))
        self.check('Concurrent migrators both succeed', all(r.returncode == 0 for r in results))
        self.check('Concurrent migrators record each version once', self.sql('SELECT count(*) FROM gtt_schema_migrations;', database='migration_fail') == '6')
        dump = self.compose('exec', '-T', 'postgres', 'pg_dump', '-U', 'trade', '-d', 'migration_v12', '-Fc', '--no-owner', '--no-acl').stdout
        self.compose('exec', '-T', 'postgres', 'createdb', '-U', 'trade', 'migration_restore')
        self.compose('exec', '-T', 'postgres', 'pg_restore', '-U', 'trade', '-d', 'migration_restore', '--no-owner', '--no-acl', '--exit-on-error', '--single-transaction', data=dump)
        self.check('Backup restores ledger provenance and times', self.sql('SELECT json_agg(m ORDER BY version) FROM gtt_schema_migrations m;', database='migration_v12') == self.sql('SELECT json_agg(m ORDER BY version) FROM gtt_schema_migrations m;', database='migration_restore'))
        self.migration_command('migration_restore')
        self.check('Restored ledger supports repeat migration', self.sql('SELECT count(*) FROM gtt_schema_migrations;', database='migration_restore') == '6')

    def cleanup(self):
        if not self.claimed:
            return
        self.owned_resources()
        if self.compose_file.exists():
            self.compose('down', '--volumes', '--remove-orphans', timeout=150)
        for kind in ('container', 'volume', 'network'):
            if self.resource_ids(kind):
                raise VerificationError('Cleanup left test resources: ' + kind)
        image = self.docker('image', 'inspect', self.image, check=False)
        if image.returncode == 0:
            labels = json.loads(image.stdout)[0]['Config'].get('Labels', {})
            if labels.get(MARKER) != self.run_id:
                raise VerificationError('Refusing removal of an unowned image')
            self.docker('image', 'rm', self.image)
        self.report['cleanup'] = True

    def execute(self):
        """Run isolated database/API checks and always clean up owned resources."""
        started = time.monotonic()
        try:
            self.prepare()
            self.fresh_api()
            self.key_lifecycle()
            self.rate_limits()
            self.staging_and_audit()
            self.vat_review_exceptions()
            self.compliance_holds()
            self.persistence_and_restore()
            self.upgrade()
            self.migration_ledger()
            self.report['status'] = 'passed'
        except (Exception, KeyboardInterrupt) as error:
            self.report['status'] = 'failed'
            self.report['error'] = self.redact(str(error))
            if self.compose_file.exists():
                try:
                    logs = self.compose('logs', '--no-color', '--tail', '70', 'api', 'postgres', 'upgrade-postgres', check=False)
                    self.report['diagnostics'] = self.redact((logs.stdout + logs.stderr).decode(errors='replace'))
                except Exception:
                    self.report['diagnostics'] = 'Container diagnostics unavailable'
        finally:
            try:
                self.cleanup()
            except Exception as error:
                self.report['status'] = 'failed'
                self.report['cleanup_error'] = self.redact(str(error))
            self.report['elapsed_seconds'] = round(time.monotonic() - started, 2)
            self.report_path.parent.mkdir(parents=True, exist_ok=True)
            self.report_path.write_text(self.redact(json.dumps(self.report, indent=2)) + '\n', encoding='utf-8')
        print(f"{self.report['status'].upper()}: {self.report['assertions']} PostgreSQL/container assertions; cleanup={self.report['cleanup']}", flush=True)
        if self.report['status'] != 'passed':
            print(self.report.get('error', self.report.get('cleanup_error', 'Verification failed')), file=sys.stderr)
            return 1
        return 0


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--report', default='TestResults/postgres-integration.json')
    options = parser.parse_args()
    with tempfile.TemporaryDirectory(prefix='gtt-postgres-test-') as directory:
        return Integration(directory, options.report).execute()


if __name__ == '__main__':
    sys.exit(main())
