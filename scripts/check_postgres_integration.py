#!/usr/bin/env python3
"""Exercise the real Compose/Dockerfile/PostgreSQL path in a disposable, labelled project.

Requires Docker Compose v2 and Python's standard library. No existing database is selected.
"""
import argparse
import concurrent.futures
import copy
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
        pg['environment']['POSTGRES_PASSWORD'] = '${POSTGRES_PASSWORD:?}'
        pg['healthcheck'] = {'test': ['CMD', 'pg_isready', '-h', '127.0.0.1', '-U', 'trade', '-d', 'trade'],
                             'interval': '2s', 'timeout': '2s', 'retries': 40}
        legacy_init = self.directory / 'legacy-init'
        legacy_init.mkdir()
        (legacy_init / '001_init.sql').write_bytes((self.repo / 'tests/fixtures/postgres/v04-schema.sql').read_bytes())
        upgrade = copy.deepcopy(pg)
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

    def request(self, method, path, *, key=None, operator=False, payload=None, idempotency=None, expected=200):
        headers = {}
        if key is not None:
            headers['X-API-Key'] = key
        if operator:
            headers['X-Operator-Key'] = self.env['GTT_TEST_OPERATOR_KEY']
        if idempotency:
            headers['Idempotency-Key'] = idempotency
        if isinstance(payload, str):
            headers['Content-Type'] = 'text/csv'
            body = payload.encode()
        elif payload is not None:
            headers['Content-Type'] = 'application/json'
            body = json.dumps(payload).encode()
        else:
            body = None
        request = Request(self.base + path, data=body, headers=headers, method=method)
        try:
            response = build_opener(NoRedirects()).open(request, timeout=12)
        except HTTPError as error:
            response = error
        with response:
            status = response.status
            raw = response.read(1_048_576)
        self.check(f'{method} {path} expects HTTP {expected} (received {status})', status == expected)
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
        self.check('All six schema tables initialized', len(json.loads(self.fresh_schema)['indexes']) > 0 and
                   self.sql("SELECT count(*) FROM information_schema.tables WHERE table_schema='public' AND table_type='BASE TABLE';") == '6')
        self.provision('merchant-a', self.keys['a'])
        self.provision('merchant-b', self.keys['b'])
        self.start_api()
        self.check('Runtime container runs without root', self.compose('exec', '-T', 'api', 'id', '-u').stdout.strip() != b'0')
        anonymous = assert_smoke(lambda path, method='GET', payload=None: fetch(self.base, path, method, payload))
        self.check('General container smoke and anonymous routes', anonymous == 11, count=anonymous)
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

    def staging_and_audit(self):
        self.phase('Checking tariff staging and database audit writes')
        csv = ('destination,origin,hs_code,ad_valorem_percent,valid_from,valid_to,source_url,source_version\n'
               'GB,US,0101210000,9.5,2026-01-01,2026-12-31,https://example.test/test-only,integration-only\n'
               'GB,US,0101299000,12.3,2026-01-01,2026-12-31,https://example.test/test-only,integration-only\n')
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
        self.request('POST', path, operator=True, payload=csv.replace('2026-12-31', '2025-12-31'), expected=400)
        self.check('Rejected CSV leaves no partial batch', self.sql('SELECT count(*) FROM tariff_import_batches;') == '1')
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
                'lines': [{'lineId': 'a', 'commodityCode': '0101299000', 'goodsOrigin': 'CN', 'customsValue': 1000}]})
            self.check('UK review retains uncertainty without configured territory sources',
                       review['status'] == 'requires_review' and review['destinationTerritory'] == territory and
                       review['approvedForPayment'] is False and review['verifiedForCalculation'] is False and
                       all(review[name] is None for name in ('payableDutyGbp', 'payableVatGbp', 'payableLandedTotalGbp', 'illustrativeTotals')))
        for malformed in ('{"currency":"GBP","Currency":"USD"}', '{"intrinsicValueGbp":1e-100}'):
            self.request('POST', path, key=self.keys['rotated'], payload=malformed, expected=400)
        self.request('POST', path, key=self.keys['rotated'], payload=' ' * 262145, expected=413)
        self.check('UK evidence audit is tenant-bound, contains a digest and omits shipment details',
                   self.sql("SELECT count(*) FROM audit_events WHERE tenant_id='merchant-a' AND action='uk_shipment_review' AND details_json->>'approvedForPayment'='false' AND length(details_json->>'evidenceSha256')=64 AND NOT details_json ? 'lines';") == '2')

    def verify_saved_data(self, database='trade'):
        quote = self.request('GET', '/v1/quotes/' + self.quote_a['quoteId'], key=self.keys['rotated'])
        self.check('Saved quote remains equivalent as JSON', quote == self.quote_a)
        replay = self.request('POST', '/v1/quotes', key=self.keys['rotated'], payload=self.payload, idempotency='shared-key')
        self.check('Idempotency survives recreation/restore', replay == self.quote_a)
        self.request('GET', '/v1/quotes/' + self.quote_a['quoteId'], key=self.keys['a'], expected=401)
        self.request('GET', '/v1/quotes/' + self.quote_a['quoteId'], key=self.keys['b'], expected=401)
        batch = self.request('GET', '/v1/admin/tariff-datasets/' + self.batch_id, operator=True)
        self.check('Staging survives recreation/restore', batch['rows'] == 2 and
                   self.sql('SELECT count(*) FROM tariff_reference_rows;', database=database) == '2')
        self.check('Audit survives recreation/restore', self.sql('SELECT count(*) FROM audit_events;', database=database) == '3')

    def persistence_and_restore(self):
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
                   self.sql('SELECT count(*) FROM audit_events;', database='trade_restore') == '4')
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
        self.check('Exactly the four reviewed migration scripts are used', [path.name for path in scripts] ==
                   ['001_init.sql', '002_tenants.sql', '003_audit.sql', '004_tariff_staging.sql'])
        self.sql('\n'.join(path.read_text() for path in scripts[1:]), service=service)
        self.check('Upgraded schema matches fresh initialization', self.sql(SCHEMA_SQL, service=service) == self.fresh_schema)
        self.sql('\n'.join(path.read_text() for path in scripts), service=service)
        self.check('Reapplying schema scripts preserves legacy data',
                   self.sql('SELECT response_json FROM quotes;', service=service) == json.dumps(legacy))
        self.sql('CREATE TABLE rollback_probe(id integer); SELECT 1/0;', service=service, error_state='22012')
        self.check('Failed migration transaction leaves no schema changes', self.sql(SCHEMA_SQL, service=service) == self.fresh_schema)
        self.provision('legacy-merchant', self.keys['legacy'], service=service)
        self.start_api(host=service)
        restored = self.request('GET', '/v1/quotes/' + legacy_id, key=self.keys['legacy'])
        self.check('API reads pre-upgrade quote after migration', restored['quoteId'] == legacy_id and restored['merchandiseValue'] == 70)
        self.incomplete_quote(restored)
        new_quote = self.request('POST', '/v1/quotes', key=self.keys['legacy'], payload=self.payload, idempotency='after-upgrade')
        self.incomplete_quote(new_quote)
        self.check('New writes preserve the historical quote', self.sql('SELECT count(*) FROM quotes;', service=service) == '2')

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
        started = time.monotonic()
        try:
            self.prepare()
            self.fresh_api()
            self.staging_and_audit()
            self.persistence_and_restore()
            self.upgrade()
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
