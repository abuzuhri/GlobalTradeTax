import sys
import subprocess
import tempfile
import unittest
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from check_secrets import scan_bytes, tracked_files


class SecretGuardTests(unittest.TestCase):
    def test_untracked_inventory_respects_ignores_without_staging(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            subprocess.run(['git', 'init', '--quiet', str(root)], check=True, capture_output=True)
            (root / '.gitignore').write_text('ignored.txt\n', encoding='utf-8')
            (root / 'ignored.txt').write_text('not scanned', encoding='utf-8')
            (root / 'new file.txt').write_text('content', encoding='utf-8')
            self.assertEqual([], tracked_files(root))
            self.assertEqual({'.gitignore', 'new file.txt'},
                             {path.name for path in tracked_files(root, include_untracked=True)})
            self.assertEqual([], tracked_files(root))

    def test_combined_inventory_keeps_tracked_ignored_files(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            subprocess.run(['git', 'init', '--quiet', str(root)], check=True, capture_output=True)
            tracked = root / 'tracked.txt'
            tracked.write_text('content', encoding='utf-8')
            subprocess.run(['git', '-C', str(root), 'add', 'tracked.txt'], check=True, capture_output=True)
            (root / '.gitignore').write_text('tracked.txt\n', encoding='utf-8')
            (root / 'new.txt').write_text('content', encoding='utf-8')
            self.assertEqual([tracked], tracked_files(root))
            self.assertEqual({'.gitignore', 'tracked.txt', 'new.txt'},
                             {path.name for path in tracked_files(root, include_untracked=True)})

    def test_private_key(self):
        self.assertIn('private-key', scan_bytes(b'-----BEGIN ' + b'PRIVATE KEY-----\nabc'))

    def test_github_token(self):
        self.assertIn('github-token', scan_bytes(b'ghp_' + b'A' * 36))

    def test_aws_access_key(self):
        self.assertIn('aws-access-key-id', scan_bytes(b'AKIA' + b'A' * 16))

    def test_placeholder_is_not_secret(self):
        self.assertEqual([], scan_bytes(b'X-API-Key: YOUR_API_KEY\nAWS_ACCESS_KEY_ID=example'))

    def test_no_match_value_leaked(self):
        self.assertEqual(['stripe-live-secret'], scan_bytes(b'sk_live_' + b'A' * 24))


if __name__ == '__main__':
    unittest.main()
