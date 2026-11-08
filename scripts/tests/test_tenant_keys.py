import importlib.util
from pathlib import Path
import sys
import types
import unittest
from unittest.mock import patch

# Parser and diagnostic guards do not require a database driver in repository checks.
if 'psycopg' not in sys.modules:
    try:
        import psycopg
    except ImportError:
        psycopg = types.ModuleType('psycopg')
        psycopg.Error = type('Error', (Exception,), {})
        psycopg.connect = lambda *args: None
        types_pkg = types.ModuleType('psycopg.types')
        json_pkg = types.ModuleType('psycopg.types.json')
        json_pkg.Jsonb = lambda value: value
        sys.modules.update({'psycopg': psycopg, 'psycopg.types': types_pkg, 'psycopg.types.json': json_pkg})
spec = importlib.util.spec_from_file_location('tenant_keys', Path(__file__).resolve().parents[1] / 'tenant_keys.py')
keys = importlib.util.module_from_spec(spec)
spec.loader.exec_module(keys)


class LifecycleGuards(unittest.TestCase):
    def test_bounds_and_target_validation(self):
        for value in ('0', '366', '-1', 'abc'):
            with self.assertRaises(Exception):
                keys.days(value)
        for value in ('', 'x' * 101, '../other', 'a b'):
            with self.assertRaises(Exception):
                keys.tenant_id(value)
        self.assertEqual(keys.days('365'), 365)
        self.assertEqual(keys.tenant_id('merchant_1-A'), 'merchant_1-A')
        self.assertEqual(keys.tenant_id('商'), '商')

    def test_database_errors_are_redacted(self):
        from io import StringIO
        with patch.dict(keys.os.environ, {'DATABASE_URL': 'sensitive-password'}), \
             patch.object(keys.psycopg, 'connect', side_effect=keys.psycopg.Error('sensitive-password')), \
             patch('sys.stderr', new_callable=StringIO) as stderr, \
             patch('sys.stdout', new_callable=StringIO) as stdout:
            self.assertEqual(keys.main(['issue', 'merchant']), 1)
            self.assertNotIn('sensitive-password', stderr.getvalue())
            self.assertEqual(stdout.getvalue(), '')


if __name__ == '__main__':
    unittest.main()
