#!/usr/bin/env python3
"""Compatibility provisioning entry point; emits the new secret once on stdout."""
import argparse
import json
import os
import sys
import psycopg
from tenant_keys import manage, tenant_id


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('tenant', type=tenant_id)
    parser.add_argument('name')
    args = parser.parse_args()
    if not 1 <= len(args.name) <= 200 or any(ord(c) < 32 for c in args.name):
        parser.error('display name must be 1-200 printable characters')
    args.action, args.days = 'issue', 90
    try:
        with psycopg.connect(os.environ['DATABASE_URL']) as db:
            result = manage(db, args, create_name=args.name)
        print(json.dumps(result))
        return 0
    except (KeyError, ValueError, psycopg.Error):
        print('Operation failed; verify target, permissions and database configuration.', file=sys.stderr)
        return 1


if __name__ == '__main__':
    sys.exit(main())
