import json
import io
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import Mock, patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from check_postgres_integration import Integration, MARKER, VerificationError, validate_spec


class PostgresIntegrationGuardTests(unittest.TestCase):
    project = 'gtt-pgtest-012345abcdef'
    marker = '012345abcdef'

    def spec(self):
        return {
            'services': {'api': {'labels': {MARKER: self.marker}}},
            'volumes': {'db': {'name': self.project + '_db', 'labels': {MARKER: self.marker}}},
            'networks': {'default': {'name': self.project + '_default', 'labels': {MARKER: self.marker}}},
        }

    def test_private_labelled_resources_are_accepted(self):
        validate_spec(self.spec(), self.project, self.marker)

    def test_external_database_volume_is_rejected(self):
        spec = self.spec()
        spec['volumes']['db']['external'] = True
        with self.assertRaises(VerificationError):
            validate_spec(spec, self.project, self.marker)

    def test_shared_names_are_rejected_for_volumes_and_networks(self):
        for category in ('volumes', 'networks'):
            with self.subTest(category=category):
                spec = self.spec()
                next(iter(spec[category].values()))['name'] = 'globaltradetax_existing_data'
                with self.assertRaises(VerificationError):
                    validate_spec(spec, self.project, self.marker)

    def test_writable_host_data_is_rejected(self):
        spec = self.spec()
        spec['services']['api']['volumes'] = [{'type': 'bind', 'source': '/existing-data', 'target': '/data'}]
        with self.assertRaises(VerificationError):
            validate_spec(spec, self.project, self.marker)

    def test_explicit_container_names_are_rejected(self):
        spec = self.spec()
        spec['services']['api']['container_name'] = 'existing-api'
        with self.assertRaises(VerificationError):
            validate_spec(spec, self.project, self.marker)

    def test_unowned_container_blocks_destructive_cleanup(self):
        with tempfile.TemporaryDirectory() as temporary:
            runner = Integration(temporary, Path(temporary) / 'report.json')
            runner.claimed = True
            runner.resource_ids = Mock(return_value=['existing-container'])
            runner.docker = Mock(return_value=subprocess.CompletedProcess([], 0, stdout=json.dumps([
                {'Config': {'Labels': {'com.docker.compose.project': runner.project, MARKER: 'another-run'}}}
            ]).encode()))
            runner.compose = Mock()
            with self.assertRaises(VerificationError):
                runner.cleanup()
            runner.compose.assert_not_called()

    def test_generated_keys_passwords_and_verifiers_are_redacted(self):
        with tempfile.TemporaryDirectory() as temporary:
            runner = Integration(temporary, Path(temporary) / 'report.json')
            diagnostic = ' '.join(runner.hidden)
            redacted = runner.redact(diagnostic)
            self.assertIn('[REDACTED]', redacted)
            for secret in runner.hidden:
                self.assertNotIn(secret, redacted)

    def test_chunked_request_keeps_original_bytes_until_the_http_client_reads_them(self):
        with tempfile.TemporaryDirectory() as temporary:
            runner = Integration(temporary, Path(temporary) / 'report.json')
            runner.base = 'http://127.0.0.1:12345'
            payload = b'\xef\xbb\xbf' + b'test-record\r\n' * 2000

            def receive(request, timeout):
                self.assertEqual(payload, b''.join(request.data))
                self.assertEqual('POST', request.method)
                response = io.BytesIO(b'{"status":"accepted"}')
                response.status = 200
                return response

            opener = Mock()
            opener.open.side_effect = receive
            with patch('check_postgres_integration.build_opener', return_value=opener):
                result = runner.request('POST', '/upload', payload=payload, chunked=True)
            self.assertEqual('accepted', result['status'])


if __name__ == '__main__':
    unittest.main()
