#!/usr/bin/env python3
"""Local-only HTTP smoke checks; never calls upstream tariff services."""
import argparse
import json
import sys
import time
from urllib.error import HTTPError, URLError
from urllib.parse import urlsplit
from urllib.request import Request, build_opener, HTTPRedirectHandler


PROTECTED_ENDPOINTS = (
    ('/v1/integrations/saleor/reviews', 'POST', {}),
    ('/v1/quotes', 'POST', {}),
    ('/v1/landed-cost/estimate', 'POST', {}),
    ('/v1/compliance/check', 'POST', {}),
    ('/v1/references/eu/destinations/DE', 'GET', None),
    ('/v1/references/gcc/SA', 'GET', None),
    ('/v1/references/national/IN', 'GET', None),
    ('/v1/references/national/CN', 'GET', None),
    ('/v1/references/north-america/US', 'GET', None),
    ('/v1/references/uk/commodities/0101210000', 'GET', None),
    ('/v1/references/uk/assessments/0101210000?origin=US&importDate=2026-11-02', 'GET', None),
    ('/v1/references/uk/assessments/0101210000/evidence-package?origin=US&importDate=2026-11-02', 'GET', None),
    ('/v1/references/uk/vat/review', 'POST', {}),
    ('/v1/references/uk/shipments/review', 'POST', {}),
)


def require_loopback(base):
    parsed = urlsplit(base)
    if parsed.scheme != 'http' or parsed.hostname not in ('127.0.0.1', 'localhost', '::1') or parsed.username or parsed.password or parsed.query or parsed.fragment or parsed.path not in ('', '/') or parsed.port is None:
        raise ValueError('smoke test must target a local HTTP loopback address')


class NoRedirects(HTTPRedirectHandler):
    """A local test server must never redirect the smoke client off loopback."""

    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


def fetch(base, path, method='GET', payload=None):
    body = json.dumps(payload).encode() if payload is not None else None
    request = Request(base.rstrip('/') + path, data=body, method=method,
                      headers={'Content-Type': 'application/json'} if body is not None else {})
    try:
        with build_opener(NoRedirects()).open(request, timeout=3) as response:
            return response.status, json.load(response)
    except HTTPError as error:
        return error.code, None


def assert_smoke(fetcher):
    status, health = fetcher('/health')
    assert status == 200 and isinstance(health, dict) and health.get('status') == 'healthy', 'health check failed'
    status, capabilities = fetcher('/v1/capabilities')
    assert status == 200 and isinstance(capabilities, dict), 'capabilities endpoint failed'
    assert capabilities.get('settlement') == 'not-implemented', 'capabilities must not advertise settlement'
    assert capabilities.get('calculation') == 'provider-required', 'unverified calculation must remain disabled'
    for path, method, payload in PROTECTED_ENDPOINTS:
        status, _ = fetcher(path, method, payload)
        assert status == 401, f'unauthenticated {method} {path.split("?")[0]} must be denied (got {status})'
    return 2 + len(PROTECTED_ENDPOINTS)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--base-url', default='http://127.0.0.1:5087')
    parser.add_argument('--wait-seconds', type=int, default=35)
    args = parser.parse_args()
    try:
        require_loopback(args.base_url)
    except ValueError as error:
        print(f'FAIL: {error}', file=sys.stderr)
        return 2
    deadline = time.monotonic() + args.wait_seconds
    while True:
        try:
            count = assert_smoke(lambda path, method='GET', payload=None: fetch(args.base_url, path, method, payload))
            print(f'PASS: {count} API smoke assertions')
            return 0
        except (URLError, TimeoutError, ConnectionError):
            if time.monotonic() >= deadline:
                print('FAIL: API did not become reachable', file=sys.stderr)
                return 1
            time.sleep(0.5)
        except (AssertionError, ValueError, KeyError) as error:
            print(f'FAIL: {error}', file=sys.stderr)
            return 1


if __name__ == '__main__':
    sys.exit(main())
