# Saleor review-only pilot — TASK-012

Implemented and locally verified on 2026-11-02. This is the first connector slice, not an installed Saleor App or an enabled checkout tax service.

## Endpoint and behavior

`POST /v1/integrations/saleor/reviews` imports a signed Saleor `CalculateTaxes` subscription payload and returns a diagnostic review with a deterministic reference. It never calls a tariff provider, issues a payable quote, writes an order or changes Saleor. Source prices are retained for inspection, not interpreted as verified customs values.

Required headers:

- `X-API-Key`: an existing GlobalTradeTax merchant credential.
- `Saleor-Api-Url`: the exact configured HTTPS GraphQL URL, including `/graphql/`.
- `Saleor-Signature`: detached RS256 JWS over the exact request bytes.
- `Content-Type: application/json`.

The authenticated tenant must match exactly one enabled store registration. The pilot and each store are disabled by default. Request headers cannot create registrations or select keys from another tenant. No key URLs from the request are fetched.

Successful response: HTTP 200 with `status: requires_review`, `reference: sr_<sha256>`, the source ID/channel/currency, ordered source lines, unresolved checks and `Cache-Control: no-store`. `importDuty`, `importVat`, `clearanceFee` and `landedTotal` remain null; `approvedForPayment` is always false. Customs metadata supplied by a merchant remains unverified even when present.

This response intentionally does not implement Saleor's tax-response schema. **Do not subscribe this endpoint to CHECKOUT_CALCULATE_TAXES or ORDER_CALCULATE_TAXES in an active checkout.** A live tax app, per-line verified amounts and end-to-end failure behavior must be implemented and reviewed separately.

## Local configuration

`samples/saleor-review-settings.json` documents the configuration structure; it is not automatically loaded. Supply its values through ASP.NET configuration, for example environment variables:

```text
Saleor__ReviewEnabled=true
Saleor__Stores__0__Enabled=true
Saleor__Stores__0__ApiUrl=https://your-shop.saleor.cloud/graphql/
Saleor__Stores__0__TenantId=your-existing-globaltradetax-tenant
Saleor__Stores__0__KeyId=verified-key-id
Saleor__Stores__0__PublicKeyPem=<verified SPKI public-key PEM>
```

Obtain the key ID and corresponding RSA public key from the store's operator-verified `/.well-known/jwks.json`, then pin the public key in SPKI PEM format. Do not trust an arbitrary key supplied with an import request. Only public keys are configured here; do not add a Saleor signing private key. Rotation is manual in this pilot: replace the pin when the operator verifies a new key. Duplicate store registrations are rejected rather than selecting an ambiguous key.

Existing PostgreSQL merchant authentication is reused when configured. The existing development-key fallback still requires Development mode plus `TRADE_API_KEY` and `TRADE_TENANT_ID`. `.env.example` documents names only; ASP.NET does not load that file automatically.

## Payload contract and limits

- `samples/saleor-review.graphql` uses `checkoutProductVariant` and `orderProductVariant` aliases with optional `gtt.hsCode` / `gtt.countryOfOrigin` product metafields. On 2026-11-02 the initial unaliased query failed validation inside Saleor 3.23.45 because the two variant fields have different nullability; the corrected selection passed against that running schema. The importer accepts the typed aliases and the original single `variant` field, rejecting simultaneous legacy/typed objects as ambiguous.
- `samples/saleor-review.json` is a synthetic payload with a fixed historical timestamp, not a usable signed request. Refresh `issuedAt` before generating a local test signature. Editing any bytes after signing invalidates the signature.
- Root type is `CalculateTaxes`; source type is `Checkout` or `Order`. The scalar `issuedAt` must parse and be no older than five minutes or more than one minute in the future. Signature validity establishes provenance, not financial accuracy.
- The supported signing profile is RS256 with a detached payload, protected `kid`, `b64: false` and `crit: ["b64"]`, matching the inspected Saleor signing implementation. Encoded-payload profiles, HMAC, unsigned tokens, unknown critical/header fields, duplicate header properties and non-canonical base64url are rejected. RSA public keys must be 2048–8192 bits.
- Body limit: 65,536 bytes, enforced while streaming even without Content-Length. JSON depth is limited to 32. Duplicate JSON properties are rejected.
- One to 100 lines; unique source-line IDs; positive integer quantities up to 100,000; nonnegative decimal source prices below one billion; uppercase currency/country codes.
- Missing SKU, HS code, country of origin or destination is reported as unresolved. Required structural/pricing fields that are missing or invalid cause HTTP 400. No rates are inferred from product labels, metadata or countries.
- Exact signed-payload retries within the freshness window return the same reference and review. Tenant, store or payload changes produce a different reference. This is stateless review idempotency, not durable event deduplication. Responses and raw payloads are not stored; resubmit within the freshness window to reproduce a review.

| HTTP status | Meaning |
|---|---|
| 200 | Authenticated diagnostic review; not approval for payment |
| 400 | Invalid, stale or unsupported signed payload |
| 401 | Missing/invalid merchant credential, store binding or signature |
| 413 | Body exceeds the byte limit |
| 415 | Unsupported content type |
| 503 | Authenticated request while the pilot is disabled |

Authentication occurs before the pilot configuration check or body parsing. No API key, signing private key, customer address or entire raw payload is logged by the connector.

## Executed verification

- .NET SDK 8.0.308 Release build with warnings as errors: passed, zero warnings/errors.
- New Saleor xUnit cases: 44 passed, including detached signatures, tampering, wrong tenant/store/key, unsupported algorithms and critical headers, duplicate properties, expiry, missing metadata, multiple lines, streaming body limits and a real local Kestrel HTTP round trip.
- Complete xUnit suite: 108 passed, zero failed/skipped. Python repository suite: 25 passed.
- `dotnet publish`: passed.
- Independently signed HTTP requests: Python cryptography 50.0.2 generated an ephemeral RSA key and signed raw bytes; the published ASP.NET application accepted the review and identical retry. Private keys and API credentials remained in process memory.
- Actual application responses: anonymous/wrong credential/wrong store/tampered payload 401; wrong content type 415; oversized body 413; correctly signed expired payload 400; disabled pilot 503; signed fresh review 200. All four unverified payable amount fields stayed null.
- General API smoke: 10 assertions passed, including rejection of anonymous Saleor imports. Integrity check: 14 tasks. Static workflow guard and limited non-ignored file-pattern secret scan passed.

No new NuGet dependencies were introduced. Tests use offline synthetic data and local HTTP, not live Saleor, an external tariff service, PostgreSQL, a container or hosted GitHub Actions.

## Follow-up work

TASK-012 remains in progress. Next slices are validating the GraphQL selection against a pinned Saleor sandbox release, secure app installation/registration and token persistence, a Dashboard review extension, durable quote/event storage, retries and revocation/key rotation, then verified line-level allocation and explicit checkout blocking before enabling collection. Shopify/WooCommerce and carrier integrations remain unimplemented. The tariff correctness gates in tasks 005/007/008 are unchanged.

The pinned sandbox and schema-validation slice is now complete; see [sandbox execution evidence](SALEOR_SANDBOX.md). A genuine running Saleor signing function was also accepted by the published .NET importer after matching its public key to the running API's JWKS. Payload contents were synthetic, delivery was a controlled local replay, and automatic webhook delivery, a real checkout event and production TLS have not been verified.

## Engineering references

- [Saleor tax-event contract](https://docs.saleor.io/developer/extending/webhooks/synchronous-events/tax)
- [Payload signature documentation](https://docs.saleor.io/developer/extending/webhooks/payload-signature)
- [Saleor signing implementation](https://github.com/saleor/saleor/blob/main/saleor/core/jwt_manager.py)
- [AvaTax subscription payload](https://github.com/saleor/apps/blob/main/apps/avatax/graphql/fragments/TaxBase.graphql)
- [Saleor app requirements](https://docs.saleor.io/developer/extending/apps/architecture/app-requirements)

These informed an independent C# pilot. No Saleor source was vendored and no claim of compatibility with a live version has been made.
