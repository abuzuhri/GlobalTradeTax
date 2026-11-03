# Installed Saleor review App — TASK-012

Implemented and verified on 2026-11-03 in the isolated local Saleor 3.23.45 / Dashboard 3.23.41 sandbox. This advances the earlier manual replay pilot to automatic delivery from real checkout objects in the sandbox database. It does not enable customs collection.

## Implemented flow

1. Saleor fetches the review App manifest and installs it with **MANAGE_CHECKOUTS only**. No HANDLE_TAXES, payment or order-management permission is requested.
2. Saleor sends the installation token to the registration callback. The backend accepts only a preconfigured store identity, verifies the token directly against that store's fixed GraphQL endpoint, and checks the App identifier, active status and exact permission set before saving it.
3. Saleor's Celery worker delivers signed CHECKOUT_CREATED and CHECKOUT_UPDATED events using the manifest subscription query.
4. The receiver checks the preconfigured store header, pinned RSA key and exact raw-body signature. It maps the real checkout lines to a review and saves the result.
5. The App appears inside Dashboard under Extensions. The official AppBridge supplies the user's short-lived token; our backend asks the fixed Saleor API to verify a staff user with checkout access before returning reviews. The App frontend keeps this token in memory and never receives the long-lived installation token.

The manifest has **no synchronous tax events**. All returned customs duty, VAT, clearance-fee and landed-total fields remain null, and approvedForPayment remains false. The UI labels commerce line prices as source prices, lists unresolved checks and marks expired references as historical.

## Routes

| Route | Purpose / access |
|---|---|
| GET `/saleor/{store}/manifest` | Manifest for an operator-preconfigured, enabled store |
| POST `/saleor/{store}/register` | Installation callback; store binding plus server-side App-token verification |
| POST `/saleor/{store}/events` | Signed asynchronous checkout events; an installed binding is required |
| GET `/saleor/{store}/app` | Public UI shell; no reviews or credentials embedded |
| GET `/saleor/{store}/reviews` | Bearer token verified by the fixed Saleor API; staff/checkout access required |
| GET `/saleor/notices.txt` | Bundled frontend dependency notices |

The existing merchant-key-protected `/v1/integrations/saleor/reviews` manual endpoint remains unchanged.

## Configuration and persistence

The new integration is off unless `SaleorApp:Enabled` is explicitly enabled. Registrations are configured in `SaleorApp:Stores` with Id, TenantId, ApiUrl, TransportUrl, PublicBaseUrl, CallbackBaseUrl, DashboardOrigin, KeyId and PublicKeyPem. Incoming requests cannot create their own store registrations or choose a key-discovery URL.

- Production identities use the existing canonical HTTPS GraphQL URL checks. Arbitrary transport overrides are rejected.
- Only in Development mode with `SaleorApp:AllowLocalSandboxHttp=true` are the exact local sandbox URLs allowed. The manual endpoint's HTTPS policy was not loosened.
- `SaleorApp:StateDirectory` must be an absolute path and `SaleorApp:EncryptionKey` a 32-byte hex key.
- App tokens and review snapshots are encrypted with AES-256-GCM using a random nonce and tenant/store identity as authenticated associated data. State filenames are also derived from that identity. An altered file or wrong encryption key fails closed.
- Atomic file replacement and a per-process lock protect writes. Exact delivery hashes deduplicate retries. The most recent 200 event snapshots are retained per binding, ordered by source issuance time. Raw webhook bodies, customer email and street addresses are not stored.
- Events up to 24 hours old are accepted for asynchronous retry delivery; future timestamps beyond one minute are rejected. Original review expiry is preserved, so a delayed event never becomes a fresh payable quote.
- Registration retries with the same App ID/token are idempotent. A different binding/token is rejected with 409 instead of silently replacing a merchant's installation.

This is a **single-instance file store**, not a distributed production ledger. Preserve the encryption key independently of the state volume. The sandbox key is in ignored `dev/saleor/.env`; state is in the dedicated `saleor-app-state` Docker volume. No credential is included in the image or reports.

## Local sandbox changes

`scripts/start_saleor_sandbox.ps1` now prepares:

- the existing API, Dashboard, PostgreSQL and Valkey services;
- a Celery worker for real asynchronous delivery;
- the non-root `gtt` service, published only at http://localhost:18081;
- an ignored, persistent sandbox signing PEM shared read-only by Saleor API/worker, ensuring both use the same signing key;
- an ignored public-key/store settings JSON and a separate random App-state encryption key.

The private PEM, local settings, `.env`, helper metadata and node_modules are excluded from Git; secrets are also excluded from Docker build context. The native Windows PostgreSQL service is not used.

Saleor's private-address HTTP filter is disabled **only inside this disposable DEBUG sandbox** so its API/worker can reach the private Docker App callback. This is an explicit local development exception; do not copy that setting into production. Published ports remain loopback-only. The signing key and original administrator credentials survive normal sandbox restart.

Start/stop commands remain:

```powershell
.\scripts\start_saleor_sandbox.ps1
.\scripts\stop_saleor_sandbox.ps1
```

Stopping retains database, media and encrypted App state volumes. Administrator credentials remain in ignored `dev/saleor/.env`.

## Reproduce the real event test

```powershell
python scripts/check_saleor_app.py --results C:\path\to\results
```

The helper uses the local sandbox administrator without printing credentials. It installs the App through Saleor's appInstall mutation if necessary, creates/reuses a dedicated GBP channel and test product, creates a real checkout with quantity 2, waits for the automatically delivered review, updates quantity to 3 and waits for the second review. It checks product HS/origin metadata and verifies that anonymous/invalid-token review reads receive 401.

The test product deliberately has inventory tracking and shipping requirements disabled to keep this check independent of fulfillment. Its classification/origin metadata is illustrative, not verified legal evidence. No checkout is completed and no order or payment is created.

The browser checker `scripts/check_saleor_dashboard.cjs` uses Playwright and a separate headless Chrome session. Supply `PLAYWRIGHT_MODULE` when Playwright is outside the normal module path and optionally `CHROME_PATH`. It signs into the real Dashboard with the sandbox account, opens the installed App, confirms AppBridge-authenticated reviews and saves a screenshot. It blocks non-loopback browser requests and does not print passwords/tokens.

Dashboard 3.23.41 uses `/extensions/app/{encoded-app-id}`; the legacy `/apps/{id}/app` path redirected to the installed extensions list during validation, so the helper was corrected to the current route.

## Actual evidence

- App installed and active in Saleor; the running database reports `is_installed=true`.
- Real checkout-created and checkout-updated events arrived automatically through the Celery worker, with quantities 2 and 3 respectively. The receiver accepted Saleor signatures and preserved product HS/origin metadata.
- The installed App displayed four retained event cards inside the actual Dashboard after real staff login and AppBridge authentication. The screenshot shows the manual-review/collection-disabled warning.
- Anonymous and invalid-token review reads: 401. All customs payable fields stayed null.
- Registration and encrypted reviews remained available after rebuilding/recreating the App container.
- .NET Release build with warnings as errors: passed, zero warnings/errors. Full xUnit suite: **124 passed**, including **13 new App tests** covering encryption, persistence, duplicate delivery, tenant/store separation, timestamp/header checks and sandbox HTTP isolation.
- Python repository suite: **25 passed**. General HTTP smoke: **10 assertions passed** against the container. Repository integrity/static CI guard/actionlint and limited file-pattern secret scanning passed.
- Frontend uses exact versions: @saleor/app-sdk 1.16.0, React 19.3.1, esbuild 0.28.2. npm audit reported zero vulnerabilities. Build scripts generate the bundle and third-party notices; the new CI job restores the lockfile, audits packages and checks generated files for drift.
- No hosted GitHub Actions run, commit, staging, reset or branch change was performed. Existing source files were preserved.

## Remaining work

TASK-012 remains in progress: production HTTPS deployment, lifecycle revocation/reinstallation and key rotation, stronger provisioning UX, distributed persistence, volume/credential backup and retention policy, failure/retry monitoring, broader tenant-isolation review, carriers/Shopify/WooCommerce and verified line-level customs calculation are still open. The existing 005/007/008 calculation gates are unchanged. An installed review App does not authorize customs collection or a production release.
