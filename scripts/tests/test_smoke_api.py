import sys
import unittest
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from smoke_api import assert_smoke, PROTECTED_ENDPOINTS, require_loopback, fetch


class SmokeTests(unittest.TestCase):
    def good(self, path, method='GET', payload=None):
        if path == '/health':
            return 200, {'status': 'healthy'}
        if path == '/v1/capabilities':
            return 200, {'settlement': 'not-implemented', 'calculation': 'provider-required', 'destinationPolicies': 'EU/GCC/LATAM/OTHER'}
        return 401, None

    def test_expected_contract(self):
        self.assertEqual(assert_smoke(self.good), 2 + len(PROTECTED_ENDPOINTS))

    def test_every_protected_endpoint_denies_anonymous(self):
        for target, _, _ in PROTECTED_ENDPOINTS:
            with self.subTest(path=target):
                def mock(path, method='GET', payload=None):
                    return (200, {}) if path == target else self.good(path, method, payload)
                with self.assertRaises(AssertionError):
                    assert_smoke(mock)

    def test_calculation_cannot_be_claimed_verified(self):
        def mock(path, method='GET', payload=None):
            if path == '/v1/capabilities':
                return 200, {'settlement': 'not-implemented', 'calculation': 'verified', 'destinationPolicies': 'EU/GCC/LATAM/OTHER'}
            return self.good(path, method, payload)
        with self.assertRaises(AssertionError):
            assert_smoke(mock)

    def test_reject_remote_smoke_target(self):
        for url in ('https://127.0.0.1:5087', 'http://example.com', 'http://localhost@evil.test', 'http://127.0.0.1:5087/?x=1'):
            with self.subTest(url=url), self.assertRaises(ValueError):
                require_loopback(url)
        require_loopback('http://127.0.0.1:5087')

    def test_redirect_not_followed(self):
        requests = []
        class Handler(BaseHTTPRequestHandler):
            def do_GET(self):
                requests.append(self.path)
                self.send_response(302)
                self.send_header('Location', 'http://example.com/should-not-be-contacted')
                self.end_headers()
            def log_message(self, *args):
                pass
        server = ThreadingHTTPServer(('127.0.0.1', 0), Handler)
        thread = threading.Thread(target=server.serve_forever, daemon=True)
        thread.start()
        try:
            status, data = fetch(f'http://127.0.0.1:{server.server_port}', '/health')
            self.assertEqual(status, 302)
            self.assertIsNone(data)
            self.assertEqual(requests, ['/health'])
        finally:
            server.shutdown()
            server.server_close()
            thread.join(timeout=2)

    def test_loopback_url_must_have_port_and_no_prefix(self):
        for url in ('http://127.0.0.1', 'http://localhost:5087/other', 'http://127.0.0.1:5087/../x'):
            with self.subTest(url=url), self.assertRaises(ValueError):
                require_loopback(url)


if __name__ == '__main__':
    unittest.main()
