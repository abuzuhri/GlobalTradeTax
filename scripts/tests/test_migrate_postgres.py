import tempfile
import unittest
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import migrate_postgres as migrations


class MigrationGuards(unittest.TestCase):
    def test_manifest_matches_committed_files(self):
        self.assertEqual(len(migrations.load_migrations(migrations.ROOT / 'db')), 5)

    def test_modified_missing_and_extra_files_are_rejected(self):
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            for name, _ in migrations.MANIFEST:
                (directory / name).write_bytes((migrations.ROOT / 'db' / name).read_bytes())
            (directory / migrations.MANIFEST[0][0]).write_bytes(b'modified')
            with self.assertRaisesRegex(ValueError, 'checksum'):
                migrations.load_migrations(directory)
            (directory / migrations.MANIFEST[0][0]).unlink()
            with self.assertRaisesRegex(ValueError, 'manifest'):
                migrations.load_migrations(directory)
            (directory / '999_unreviewed.sql').write_text('SELECT 1;')
            with self.assertRaisesRegex(ValueError, 'manifest'):
                migrations.load_migrations(directory)

    def test_status_does_not_create_persistent_objects(self):
        sql = migrations.build_sql(migrations.load_migrations(migrations.ROOT / 'db'), 'status')
        self.assertNotIn('CREATE TABLE', sql)
        self.assertNotIn('INSERT INTO', sql)

    def test_argument_rejects_unknown_baseline(self):
        import subprocess
        result = subprocess.run([sys.executable, str(migrations.ROOT / 'scripts/migrate_postgres.py'), 'apply', '--adopt', 'other'], capture_output=True)
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual(result.stdout, b'')


if __name__ == '__main__':
    unittest.main()
