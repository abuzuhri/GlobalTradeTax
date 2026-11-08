#!/usr/bin/env python3
"""Manage merchant credentials with database authority and transactional audit."""
import argparse
import hashlib
import json
import os
import secrets
import sys
import uuid

import psycopg
from psycopg.types.json import Jsonb


def tenant_id(value):
    if not value or len(value) > 100 or not all(c.isalnum() or c in '-_' for c in value):
        raise argparse.ArgumentTypeError('tenant ID must be 1-100 letters, digits, underscores or hyphens')
    return value


def days(value):
    try:
        number = int(value)
        if 1 <= number <= 365:
            return number
    except ValueError:
        pass
    raise argparse.ArgumentTypeError('lifetime must be 1-365 days')


def parser():
    p = argparse.ArgumentParser(description=__doc__)
    sub = p.add_subparsers(dest='action', required=True)
    for action in ('issue', 'list', 'rotate', 'revoke'):
        command = sub.add_parser(action)
        command.add_argument('tenant', type=tenant_id)
        if action in ('rotate', 'revoke'):
            command.add_argument('key_id', type=uuid.UUID)
        if action in ('issue', 'rotate'):
            command.add_argument('--days', type=days, default=90)
    return p


def manage(db, args, create_name=None):
    """Lock the tenant, apply one mutation and its audit in the same transaction.

    A tenant lock serializes commands for that tenant, including stale rotations.
    Caller receives a secret only after the transaction has committed.
    """
    secret = None
    with db.transaction():
        with db.cursor() as cur:
            if create_name is not None:
                cur.execute('INSERT INTO tenants (tenant_id, display_name) VALUES (%s,%s) ON CONFLICT (tenant_id) DO NOTHING', (args.tenant, create_name))
            cur.execute('SELECT enabled FROM tenants WHERE tenant_id=%s FOR UPDATE', (args.tenant,))
            tenant = cur.fetchone()
            if tenant is None:
                raise ValueError('tenant or key unavailable')
            if args.action == 'list':
                cur.execute('SELECT key_id, key_prefix, enabled, created_at, expires_at, revoked_at FROM tenant_api_keys WHERE tenant_id=%s ORDER BY created_at, key_id', (args.tenant,))
                return [dict(zip(('key_id', 'prefix', 'enabled', 'created_at', 'expires_at', 'revoked_at'), row)) for row in cur.fetchall()]
            if args.action in ('issue', 'rotate') and not tenant[0]:
                raise ValueError('tenant or key unavailable')
            if args.action in ('rotate', 'revoke'):
                cur.execute('''UPDATE tenant_api_keys SET enabled=false, revoked_at=clock_timestamp()
                    WHERE tenant_id=%s AND key_id=%s AND enabled AND revoked_at IS NULL
                    AND (%s = 'revoke' OR expires_at IS NULL OR expires_at > clock_timestamp())
                    RETURNING key_id''', (args.tenant, args.key_id, args.action))
                if cur.fetchone() is None:
                    raise ValueError('tenant or key unavailable')
            new_id = None
            if args.action in ('issue', 'rotate'):
                secret = 'gtt_live_' + secrets.token_urlsafe(36)
                new_id = uuid.uuid4()
                digest = hashlib.sha256(secret.encode('utf-8')).hexdigest().upper()
                cur.execute('''INSERT INTO tenant_api_keys (key_id, tenant_id, key_prefix, key_hash, expires_at)
                    VALUES (%s,%s,%s,%s,clock_timestamp() + %s * interval '1 day')''',
                    (new_id, args.tenant, secret[:18], digest, args.days))
            details = {'database_role': None, 'key_id': str(new_id or args.key_id)}
            if args.action == 'rotate':
                details['previous_key_id'] = str(args.key_id)
            if new_id:
                details['lifetime_days'] = args.days
            cur.execute('SELECT current_user')
            details['database_role'] = cur.fetchone()[0]
            cur.execute("INSERT INTO audit_events(tenant_id, action, status, details_json) VALUES (%s,%s,'success',%s)",
                        (args.tenant, 'api_key_' + args.action, Jsonb(details)))
    return {'key_id': str(new_id or args.key_id), 'secret': secret}


def main(argv=None):
    args = parser().parse_args(argv)
    try:
        with psycopg.connect(os.environ['DATABASE_URL']) as db:
            result = manage(db, args)
        print(json.dumps(result, default=str))
        return 0
    except (KeyError, ValueError, psycopg.Error):
        print('Operation failed; verify target, permissions and database configuration.', file=sys.stderr)
        return 1


if __name__ == '__main__':
    sys.exit(main())
