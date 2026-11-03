# Saleor sandbox — TASK-012

Executed locally on Windows/Ubuntu WSL on 2026-11-02. The sandbox is for development only; collection remains disabled.

## Running services

- Saleor Core **3.23.45**: http://localhost:18080/graphql/
- Saleor Dashboard **3.23.41**: http://localhost:19090/
- Separate PostgreSQL 15 and Valkey 8.1 containers, with no published database/cache ports.
- All published service ports bind to loopback. The existing Windows PostgreSQL service is not used or modified.

The official stable releases were verified before setup: [Core 3.23.45](https://github.com/saleor/saleor/releases/tag/3.23.45) and [Dashboard 3.23.41](https://github.com/saleor/saleor-dashboard/releases/tag/3.23.41). Every image, including PostgreSQL and Valkey, is locked to its downloaded SHA-256 digest in `dev/saleor/compose.yaml`.

Docker 29.1.3 and Compose 2.40.3 were installed from Ubuntu's official packages inside the existing Ubuntu 24.04 WSL distribution. No Windows Docker Desktop installation or system reboot was required.

## Start, stop and credentials

Run these PowerShell scripts from the project checkout:

```powershell
.\scripts\start_saleor_sandbox.ps1
.\scripts\stop_saleor_sandbox.ps1
```

The start script creates random local secrets only when `dev/saleor/.env` does not already exist, starts the database/cache, applies migrations before starting API/Dashboard, then initializes the sandbox administrator. The account is `sandbox.admin@example.test`; its password is the `SANDBOX_ADMIN_PASSWORD` value in the ignored local `.env` file. Do not publish this file. Existing mismatched administrator credentials are not overwritten.

Because WSL can shut down after its final foreground session exits, a hidden `sleep infinity` session keeps this sandbox available. Its process ID and numeric creation timestamp are recorded in ignored `.keepalive.json`; restarting reuses the owned session, and stopping verifies its identity before terminating its process tree. Stop does not remove the database or media volumes. Stop/start and account/data retention were exercised successfully.

Containers use DEBUG mode, local HTTP and a development signing key created by Saleor inside the API container. Do not expose this setup publicly or reuse it for real merchants. Recreating the API container can change its development key; regenerate the runtime proof and pin before testing again.

## Runtime contract validation

```powershell
.\scripts\check_saleor_sandbox.ps1 -ResultsDirectory C:\path\to\local-results
```

The script executes `scripts/saleor_sandbox_probe.py` inside the actual Saleor container, validates `samples/saleor-review.graphql` against the runtime schema, generates a fresh synthetic review payload using Saleor's own signing manager, checks administrator login and reads the authenticated API version. It also checks Dashboard HTTP status. Credentials and login tokens are not printed or saved in the verification reports.

Initial result: the unaliased query failed with a conflict between `ProductVariant!` on CheckoutLine and nullable `ProductVariant` on OrderLine. Corrected the query to use the same distinct aliases used by the Saleor AvaTax integration: `checkoutProductVariant` and `orderProductVariant`. Updated the .NET mapper and added three regression cases; the legacy `variant` form remains supported.

Final query validation: **zero schema errors** on Saleor 3.23.45. Query SHA-256: `8baef564510850483632ce7ce78f1f0ce6ea82128c9d2ebedbd5ebe738a52528`.

## Signature interoperability check

Publish the .NET API, then run with Python and the `cryptography` package:

```powershell
dotnet publish src/GlobalTradeTax.Api/GlobalTradeTax.Api.csproj --no-restore -c Release -o C:\path\to\publish
python scripts/verify_saleor_runtime_signature.py --proof C:\path\to\local-results\saleor-runtime-proof.json --application C:\path\to\publish\GlobalTradeTax.Api.dll --results C:\path\to\local-results
```

Refresh the proof immediately beforehand; the import's five-minute freshness limit applies. The verification helper matches the runtime proof's public key to `http://localhost:18080/.well-known/jwks.json`, starts a temporary loopback-only instance of the published .NET API with the pinned public key and a temporary merchant key, then stops that instance after testing. It does not relax the production URL/key checks.

The configured store identity for this controlled replay is `https://saleor.sandbox.test/graphql/`. This is an explicitly pinned logical identity, not a live TLS endpoint: Saleor's development HTTP API is used for schema/JWKS inspection, and the signed bytes are replayed manually. It does not demonstrate automatic Saleor webhook routing or production transport security.

Actual results:

| Check | Result |
|---|---|
| Authenticated Saleor version | 3.23.45 |
| Dashboard HTTP and rendered page | 200; sign-in form visible |
| Corrected query against runtime schema | 0 errors |
| Proof public key matches running API JWKS | Passed |
| Genuine Saleor RS256 signature imported by published .NET API | HTTP 200 |
| Identical signed replay | HTTP 200, identical review/reference |
| Tampered body / wrong registered store | HTTP 401 / 401 |
| Typed product aliases preserve HS/origin metadata | Passed |
| Payable amounts | All null; approvedForPayment false |
| .NET Release build | Passed, 0 warnings/errors |
| xUnit suite | 111 passed, including 3 new alias regression cases |
| Python repository suite | 25 passed |
| Task links / static CI guard / limited file-pattern scan | Passed |
| Ignored local secrets/helper metadata | Verified by git check-ignore |
| Stop/start with retained database/account | Passed |

## Limits and next work

Update 2026-11-03: the next slice below has now been implemented and tested in this sandbox: [installed review App and actual checkout-event evidence](SALEOR_INSTALLED_APP.md). The synthetic-signature results in this document remain historical evidence. The sandbox now also runs a Celery worker and a non-root review App, shares a persistent development signing PEM, and uses explicit local-only private-network exceptions for its callbacks.

The data signed by the running Saleor library was synthetic sandbox data; it was not generated by a real checkout event. No synchronous tax webhook was registered. No order was placed, funds collected, external tariff service called or live merchant store accessed. The sandbox has an administrator but no sample catalog/order setup yet.

The next slice is a sandbox App with secure registration/token storage and a review UI, followed by a real checkout-event test and durable event handling. Keep the review endpoint out of active tax-calculation registration until verified line-level amounts and explicit checkout failure controls exist. TASK-012 remains in progress, as do the existing tariff correctness gates.
