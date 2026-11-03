# TASK-012: Carrier & commerce integrations

- **Status:** in-progress
- **Priority:** P2
- **Owner:** Unassigned
- **Dependencies:** Review related tasks in [master register](../TASKS.md) before starting.
- **Last reviewed:** 2026-11-03

## Objective
Connect Saleor, Shopify/WooCommerce and carrier/broker workflows with sandbox credentials.

## 2026-11-03 — Authorized next slice
- Build a sandbox-installable review App with tenant-bound registration and encrypted token/review persistence, a signed asynchronous checkout-event receiver, a real product/cart event test, and an authenticated Dashboard review screen.
- Keep all synchronous tax hooks and collection disabled. Preserve the existing manual review endpoint. Any HTTP/private-network exceptions must be confined to the explicitly enabled local Development sandbox; production store identities remain HTTPS-only.

## 2026-11-03 — Installed App and real checkout-event evidence
- [x] Installed a manifest App with only MANAGE_CHECKOUTS, tenant-bound registration verified against the fixed Saleor API, and AES-256-GCM-protected token/review state.
- [x] Added a pinned-key asynchronous checkout event receiver with bounded bodies, timestamp checks, deterministic review references and persistent delivery deduplication.
- [x] Created a sandbox product/GBP channel and actual checkout through GraphQL; automatic checkout_created/checkout_updated deliveries produced reviews for quantity 2 then 3.
- [x] Added an Arabic Dashboard review screen using the official AppBridge, verified through actual staff login in a separate headless browser. Four retained event cards and the collection-disabled warning were visible.
- [x] Verified persistence across App-container recreation; anonymous/invalid-token reads returned 401. Customs duty/VAT/clearance/landed totals remained null.
- PASS: Release build, 124 xUnit tests (13 new App tests), 25 Python tests, container build, 10 HTTP smoke assertions, npm audit (0 vulnerabilities), workflow guard/actionlint and limited secret-pattern scan.
- Added locked UI dependencies, generated dependency notices and a frontend CI job. Local-only HTTP/private-Docker exceptions are explicitly Development-scoped; the existing manual endpoint's HTTPS policy remains intact.
- Evidence and reproduction: [installed App](../docs/SALEOR_INSTALLED_APP.md). Production deployment, revocation/rotation/reinstallation, distributed persistence and other commerce/carrier integrations remain open. Task status stays in-progress.

## 2026-11-03 — Next authorized lifecycle and key-rotation slice
- Implement signed self-lifecycle events with exact installation identity, deactivation/revocation, idempotent deletion and verified reinstallation; preserve historical reviews and reject events from old installations.
- Add operator-pinned overlapping signing keys and encrypted-state key rotation with backward-compatible reads. Keep minimal permissions, the existing manual endpoint and all non-payable boundaries.
- Verify lifecycle behavior against the running isolated sandbox, including deactivate/reactivate and uninstall/reinstall, and record actual tests before changing this slice's status.

## 2026-11-03 — Lifecycle and key-rotation evidence
- [x] Signed self-lifecycle hooks retain the original MANAGE_CHECKOUTS-only scope. Recipient/App identity, timestamps, live-token checks and atomic binding checks protect reads/writes and reinstallation.
- [x] Actual sandbox deactivate/reactivate/delete/reinstall passed after fixing missing API/worker shared media. App ID changed from QXBwOjE= to QXBwOjI=; four historical reviews survived and new checkout-created/updated reviews arrived automatically.
- [x] Replacement App was verified inside Dashboard through real staff/AppBridge authentication; six review cards were visible. Financial collection remains disabled.
- [x] Added bounded signing-key overlap/retirement and lazy encrypted-state rewrapping with previous keys; automated tests cover invalid signatures, retirement, tenant binding and tampered state. Sandbox keys were not actually rotated.
- PASS: Release build (zero warnings/errors), 143 xUnit tests (19 new cases), 25 Python tests, container build, 10 HTTP smoke checks, npm audit (zero vulnerabilities), reproducible UI build, actionlint and repository checks.
- [Lifecycle behavior, key-rotation runbook and limitations](../docs/SALEOR_APP_LIFECYCLE.md). Hosted CI, operational monitoring, distributed persistence and production recovery remain open; TASK-012 stays in-progress.

## Existing project references
docs/ROADMAP.md; src/GlobalTradeTax.Api; tests/GlobalTradeTax.Api.Tests

## Detailed checklist
- [x] Inspect existing code and identify gaps against objective.
- [ ] Confirm authoritative specifications, data rights, and versioned external contracts where relevant.
- [ ] Implement minimal, isolated C# changes with explicit error handling and fail-closed behavior.
- [ ] Add unit tests for success, missing data, invalid input, and failure modes.
- [ ] Add integration/contract tests where dependencies are involved.
- [ ] Update API documentation, source attribution and configuration examples.
- [ ] Record test commands, outcomes and evidence in the task log.
- [ ] Review security, tenant isolation and backward compatibility.

## Definition of done
Webhook signatures, retries, idempotency and sandbox end-to-end flows pass.

## Evidence / decision log
- 2026-11-02: Task created. Existing implementation may be partial; completion has not been established.
- 2026-11-02: User authorized a Saleor review-only pilot. First slice: authenticated, tenant-bound import of a signed Saleor CalculateTaxes payload, deterministic review reference and regression tests. Preserve nullable financial outputs and never register this endpoint as a synchronous tax calculator. Use manually pinned public keys and no runtime network key discovery. Automatic app installation, Dashboard UI, persistent event processing and live sandbox verification remain follow-up work.

## 2026-11-02 — Saleor pilot evidence
- Implemented `POST /v1/integrations/saleor/reviews`, disabled by default and protected by existing merchant authentication plus a tenant/store/key binding and detached RS256 signature verification.
- Added five-minute payload freshness, a streamed 64 KiB body limit, strict supported JWS headers and review-only mapping of source lines, optional customs metadata and unresolved checks. All payable amount fields remain null.
- Identical signed-payload retries return the same reference without side effects; no durable storage or automatic registration is implemented.
- PASS: Release build (0 warnings/errors), 44 new Saleor xUnit cases, 108 total xUnit cases, 25 Python tests, publish, 10 API smoke assertions, independent Python-signed requests to the published app, task integrity and static CI/file-pattern checks.
- Negative HTTP cases verified: 401 for missing/wrong credentials, wrong store and tampering; 400 for signed stale payload; 413 for oversized body; 415 for unsupported content type; 503 when disabled.
- Added configuration, JSON and GraphQL samples and [pilot documentation](../docs/SALEOR_REVIEW_PILOT.md). Live Saleor schema/sandbox, app installation, UI, persistence, tenant database integration, checkout blocking and hosted CI are still unverified. Definition of done is not met.

## Risks and open questions
- 2026-11-02: User authorized a local Saleor/Dashboard sandbox and validation of the proposed GraphQL selection and genuine Saleor signatures. Pin Core 3.23.45 and Dashboard 3.23.41 (same stable 3.23 series); keep services loopback-only, use separate database volumes and preserve all existing source files. Native Windows PostgreSQL is not reused. Live checkout tax registration remains disabled.
- Who will own and review this task?
- What official source or provider contract is required?
- What evidence proves correctness in staging?

## 2026-11-02 — Pinned running Saleor sandbox
- Installed Docker/Compose from Ubuntu packages inside the existing WSL distribution. Started digest-pinned Core 3.23.45, Dashboard 3.23.41 and isolated PostgreSQL/Valkey services on loopback-only published ports. Created a random-password local sandbox administrator; existing Windows databases were not used.
- Actual runtime schema validation found a query conflict between CheckoutLine's non-null variant and OrderLine's nullable variant. Fixed the subscription selection with typed aliases; updated the importer while preserving the original single-variant form. Added three regression cases.
- PASS: runtime query validation (0 errors), staff login/version query, rendered Dashboard sign-in page, running API JWKS/public-key match and genuine Saleor-generated signature accepted by the published .NET importer (200). Tampering and wrong store returned 401; identical retries were stable; all payable fields remained null.
- PASS: Release build/publish, 111 xUnit tests, 25 Python tests, integrity/static CI/limited file-pattern checks and ignored secret-file checks. Sandbox stop/start preserved its database/account; helper process identity and lifecycle were tested.
- Boundary: the signed data was synthetic and manually replayed using a pinned logical HTTPS identity over local test transport. No automatic webhook delivery, real checkout event, production TLS, App installation or Dashboard review UI has been verified.
- See [sandbox setup and actual evidence](../docs/SALEOR_SANDBOX.md). Task remains in-progress.
