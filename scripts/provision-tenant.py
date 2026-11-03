#!/usr/bin/env python3
"""Provision a merchant and API key; requires psycopg >= 3 and DATABASE_URL."""
import hashlib
import os
import secrets
import sys
import uuid
import psycopg

if len(sys.argv) != 3:
    raise SystemExit('Usage: python scripts/provision-tenant.py TENANT_ID "Display Name"')
tenant, name = sys.argv[1:]
if not tenant or len(tenant) > 100 or not all(c.isalnum() or c in '-_' for c in tenant):
    raise SystemExit('Tenant ID must contain only letters, digits, - or _ (max 100)')
key = 'gtt_live_' + secrets.token_urlsafe(36)
with psycopg.connect(os.environ['DATABASE_URL']) as db:
    with db.cursor() as cur:
        cur.execute('INSERT INTO tenants (tenant_id, display_name) VALUES (%s,%s) ON CONFLICT (tenant_id) DO NOTHING', (tenant, name))
        cur.execute('INSERT INTO tenant_api_keys (key_id,tenant_id,key_prefix,key_hash) VALUES (%s,%s,%s,%s)', (uuid.uuid4(), tenant, key[:18], hashlib.sha256(key.encode()).hexdigest().upper()))
print('SAVE THIS KEY SECURELY; IT WILL NOT BE SHOWN AGAIN:')
print(key)
