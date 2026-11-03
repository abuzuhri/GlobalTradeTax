import base64
import hashlib
import json
import os

from cryptography.hazmat.primitives import serialization
from django.conf import settings
from django.utils import timezone
from graphql import parse, validate

from saleor.core.jwt_manager import get_jwt_manager
from saleor.graphql.api import schema


if not settings.DEBUG:
    raise RuntimeError('This probe is restricted to a DEBUG sandbox')

query_bytes = base64.b64decode(os.environ['GTT_REVIEW_QUERY_BASE64'], validate=True)
query = query_bytes.decode('utf-8')
errors = validate(schema, parse(query))
payload = json.loads(base64.b64decode(os.environ['GTT_REVIEW_SAMPLE_BASE64'], validate=True))
payload['issuedAt'] = timezone.now().isoformat()
body = json.dumps(payload, separators=(',', ':'), ensure_ascii=False).encode('utf-8')
manager = get_jwt_manager()
proof = {
    'mode': 'synthetic-data-signed-by-running-saleor',
    'schemaErrors': [str(error) for error in errors],
    'querySha256': hashlib.sha256(query_bytes).hexdigest(),
    'keyId': manager.get_key_id(),
    'publicKeyPem': manager.get_public_key().public_bytes(
        serialization.Encoding.PEM, serialization.PublicFormat.SubjectPublicKeyInfo).decode('ascii'),
    'signature': manager.jws_encode(body),
    'bodyBase64': base64.b64encode(body).decode('ascii'),
}
print('GTT_SANDBOX_PROOF=' + json.dumps(proof, separators=(',', ':')))
