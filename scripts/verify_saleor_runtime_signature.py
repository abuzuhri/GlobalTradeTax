import argparse
import base64
import http.client
import json
import os
from pathlib import Path
import re
import secrets
import subprocess
import time

from cryptography.hazmat.primitives import serialization


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--proof', type=Path, required=True)
    parser.add_argument('--application', type=Path, required=True)
    parser.add_argument('--results', type=Path, required=True)
    args = parser.parse_args()
    proof = json.loads(args.proof.read_text(encoding='utf-8-sig'))
    if proof['schemaErrors'] or proof['mode'] != 'synthetic-data-signed-by-running-saleor':
        raise ValueError('Expected a validated local Saleor runtime proof')
    body = base64.b64decode(proof['bodyBase64'], validate=True)
    key = serialization.load_pem_public_key(proof['publicKeyPem'].encode()).public_numbers()
    connection = http.client.HTTPConnection('localhost', 18080, timeout=5)
    try:
        connection.request('GET', '/.well-known/jwks.json')
        response = connection.getresponse()
        if response.status != 200:
            raise RuntimeError('Local Saleor JWKS endpoint failed')
        keys = json.loads(response.read())['keys']
    finally:
        connection.close()
    matches = [candidate for candidate in keys if candidate.get('kid') == proof['keyId']]
    if len(matches) != 1:
        raise ValueError('Runtime signing key is not present in the running API JWKS')
    def integer(value):
        return int.from_bytes(base64.urlsafe_b64decode(value + '=' * (-len(value) % 4)), 'big')
    if integer(matches[0]['n']) != key.n or integer(matches[0]['e']) != key.e:
        raise ValueError('Pinned key differs from the running Saleor API')
    args.results.mkdir(parents=True, exist_ok=True)
    api_key = secrets.token_urlsafe(40)
    store_identity = 'https://saleor.sandbox.test/graphql/'
    environment = os.environ.copy()
    environment.update({
        'ASPNETCORE_ENVIRONMENT': 'Development', 'DOTNET_ENVIRONMENT': 'Development',
        'TRADE_API_KEY': api_key, 'TRADE_TENANT_ID': 'saleor-sandbox',
        'TRADE_POSTGRES_CONNECTION': '', 'ConnectionStrings__Postgres': '', 'TRADE_OPERATOR_KEY': '',
        'TRADE_UK_TARIFF_API_BASE_URL': '', 'Saleor__ReviewEnabled': 'true',
        'Saleor__Stores__0__Enabled': 'true', 'Saleor__Stores__0__TenantId': 'saleor-sandbox',
        'Saleor__Stores__0__ApiUrl': store_identity, 'Saleor__Stores__0__KeyId': proof['keyId'],
        'Saleor__Stores__0__PublicKeyPem': proof['publicKeyPem'],
        'Logging__LogLevel__Default': 'Information',
    })
    log_path = args.results / 'globaltradetax-runtime-check.log'
    with log_path.open('w', encoding='utf-8') as log:
        process = subprocess.Popen(['dotnet', str(args.application.resolve()), '--urls', 'http://127.0.0.1:0'],
                                   env=environment, stdout=log, stderr=subprocess.STDOUT,
                                   creationflags=subprocess.CREATE_NO_WINDOW if os.name == 'nt' else 0)
        try:
            deadline = time.monotonic() + 20
            port = None
            while time.monotonic() < deadline:
                if process.poll() is not None:
                    raise RuntimeError('GlobalTradeTax exited before startup')
                match = re.search(r'Now listening on: http://127\.0\.0\.1:(\d+)', log_path.read_text(encoding='utf-8'))
                if match:
                    port = int(match.group(1))
                    break
                time.sleep(0.1)
            if port is None:
                raise TimeoutError('GlobalTradeTax startup timed out')
            def send(payload, identity=store_identity):
                connection = http.client.HTTPConnection('127.0.0.1', port, timeout=5)
                try:
                    connection.request('POST', '/v1/integrations/saleor/reviews', body=payload, headers={
                        'Content-Type': 'application/json', 'X-API-Key': api_key,
                        'Saleor-Api-Url': identity, 'Saleor-Signature': proof['signature'],
                    })
                    response = connection.getresponse()
                    contents = response.read()
                    return response.status, json.loads(contents) if contents else None
                finally:
                    connection.close()
            status, review = send(body)
            if status != 200:
                raise RuntimeError(f'Saleor runtime signature rejected: HTTP {status}; refresh the runtime proof if expired')
            if review['approvedForPayment'] or any(review[field] is not None for field in ('importDuty', 'importVat', 'clearanceFee', 'landedTotal')):
                raise AssertionError('Review unexpectedly authorized payable amounts')
            if review['lines'][0]['hsCode'] != '6109100000' or review['lines'][0]['countryOfOrigin'] != 'CN':
                raise AssertionError('Typed variant alias lost customs metadata')
            retry_status, retry = send(body)
            if retry_status != 200 or retry != review:
                raise AssertionError('Identical review retry changed its result')
            tampered_status, _ = send(body + b' ')
            wrong_store_status, _ = send(body, 'https://other.sandbox.test/graphql/')
            if tampered_status != 401 or wrong_store_status != 401:
                raise AssertionError('Invalid signer binding was accepted')
            summary = {
                'signatureSource': proof['mode'], 'jwksMatchesRunningApi': True,
                'runtimeSignatureHttpStatus': status, 'identicalRetryHttpStatus': retry_status,
                'tamperedPayloadHttpStatus': tampered_status, 'wrongStoreHttpStatus': wrong_store_status,
                'typedVariantMetadataPreserved': True, 'payableAmountsRemainNull': True,
                'storeIdentity': store_identity, 'transport': 'loopback HTTP; manually pinned logical HTTPS identity',
                'automaticSaleorWebhookDeliveryTested': False,
            }
            (args.results / 'saleor-runtime-signature-results.json').write_text(json.dumps(summary, indent=2), encoding='utf-8')
            (args.results / 'saleor-runtime-review-response.json').write_text(json.dumps(review, indent=2), encoding='utf-8')
            print(json.dumps(summary, indent=2))
        finally:
            process.terminate()
            try:
                process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait(timeout=5)


if __name__ == '__main__':
    main()
