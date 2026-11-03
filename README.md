> **Project planning:** [Master task register](TASKS.md) · [Task details](tasks/) · [Task change log](docs/CHANGELOG_TASKS.md)

# GlobalTradeTax — Cross-border Customs & Tax SaaS (C#)

Initial working source scaffold for **US -> Worldwide**, designed to expand to **China/Worldwide -> US**.

## Requirements
.NET 8 SDK (no third-party NuGet dependencies).

## Run
```bash
dotnet run --project src/GlobalTradeTax.Api --urls http://localhost:5080
curl http://localhost:5080/health
curl -X POST http://localhost:5080/v1/quotes -H 'Content-Type: application/json' --data @samples/us-to-sweden.json
```

## Important behavior
The API computes merchandise totals and performs **preliminary** US export EEI screening. It deliberately returns `null` for duty, tax, and landed total until a validated tariff/tax data provider is integrated. It does not collect funds, submit customs declarations, or remit taxes.

## Design priorities
- US export lane first; import-to-US lane second.
- Treat `shipFrom` and `countryOfOrigin` separately.
- Version effective-dated rules; preserve audit evidence and provider responses.
- HS classification is not automatically verified.
- Keep quote and payment/settlement states separate.
- Build tenant isolation, idempotency, signed webhooks, and reconciliation before accepting real merchant traffic.

See docs/ROADMAP.md and docs/SOURCES.md.

## v0.2 development additions
API key required for quote operations, idempotency key on creation, tenant-scoped quote retrieval, and Dockerfile. Set `TRADE_API_KEY` and `TRADE_TENANT_ID` before running; see `docs/IMPLEMENTATION.md`. This is development-only authentication and ephemeral storage, not a production SaaS implementation.

## v0.3 — US export destination routing
- `GET /v1/destinations/{country}` returns a compliance checklist for EU, GCC, selected Latin American destinations, or other countries (requires `X-API-Key`).
- US outbound quotes now include destination-specific required actions without fabricating rates.
- **Not a customs calculator yet**: all destination duty/tax amounts remain null until validated rate data is connected.
- US EEI screening is only a warning heuristic; Schedule B, license exceptions and AES filing obligations must be independently verified.

Example:
```bash
curl http://localhost:5080/v1/destinations/SE -H 'X-API-Key: your-secret-key'
```

## v0.4 PostgreSQL persistence

See [docs/V04_POSTGRES.md](docs/V04_POSTGRES.md). Docker Compose provisions PostgreSQL and the API. The old in-memory store is used only when no connection string is supplied. Customs duties and taxes remain unavailable until an authoritative provider is connected.


## v0.5 Multi-tenant authentication
PostgreSQL-backed SHA-256 hashed per-merchant API keys are now supported. See `docs/V05_MULTI_TENANT.md` for migrations and merchant provisioning. The API still does not calculate authoritative customs duties or taxes.

## v0.6 — Landed-cost provider boundary
Added `POST /v1/landed-cost/estimate` and a replaceable `IImportRateProvider` contract.
The default adapter is deliberately unconfigured and returns **null** duty/tax/total.
See `docs/V06_LANDED_COST.md`. No authoritative rates or settlements yet.


## v0.9 additions
See `docs/V09_RELEASE.md`. Adds external provider HTTPS adapter, fail-closed evidence verification, compliance check endpoint and PostgreSQL audit events. No live tariff provider is connected.

## v1.1 — curated tariff dataset validation
`POST /v1/tariff-datasets/validate` validates a tightly defined reference CSV and reports its SHA-256 fingerprint. It does not publish tariffs or generate payable totals. See `docs/V11_DATA_PIPELINE.md`.

## v1.2: PostgreSQL tariff reference staging

See `docs/V12_STAGING.md`. Operator-only `POST /v1/admin/tariff-datasets/stage` and `GET /v1/admin/tariff-datasets/{id}` store validated CSV reference records transactionally, with SHA-256 deduplication. **Staged records are not used for quotes.** Existing databases must apply `db/004_tariff_staging.sql` manually.

## v1.3 - UK Trade Tariff source registry

The active UK Trade Tariff open-source backend is registered in `TradeDataCatalog` with its actual GitHub URL, MIT code license, API documentation, attribution, scope, and limitations. See [docs/V13_UK_TARIFF_REFERENCE.md](docs/V13_UK_TARIFF_REFERENCE.md). `GET /v1/data-sources/uk-trade-tariff` exposes this metadata. This is a **reference only**, not an activated tariff provider or a source of payable rates.

## v1.4 UK reference connector

Read-only UK commodity lookup: `GET /v1/references/uk/commodities/{code}`. See `docs/V14_UK_REFERENCE_CONNECTOR.md`. No payable rates are produced.

## v1.5 UK normalized reference projection

`GET /v1/references/uk/commodities/{code}/normalized` returns a non-financial, nullable normalized projection of the UK reference response. It never calculates duties or VAT. See [docs/V15_UK_NORMALIZATION.md](docs/V15_UK_NORMALIZATION.md).

## v1.6 UK measure screening
New **reference-only** endpoint: `GET /v1/references/uk/commodities/{code}/screen?origin=US&importDate=2026-11-02`.
Independent C# screening inspired by the public UK Trade Tariff Backend design; it **never** claims a measure is legally applicable or computes payable duties. See `docs/V16_UK_MEASURE_SCREENING.md`. Tests: `dotnet test tests/GlobalTradeTax.Api.Tests/GlobalTradeTax.Api.Tests.csproj`.

## v1.7 UK measure evidence
Read-only `GET /v1/references/uk/commodities/{code}/evidence?origin=US&importDate=2026-11-02` provides conservative relationship evidence (geography, conditions, quotas, exclusions, raw duty expression) without determining eligibility or computing payable duty. See `docs/V17_UK_MEASURE_EVIDENCE.md`.

## v1.8 — UK geographical evidence

Added conservative `/v1/references/uk/commodities/{code}/geography` endpoint, `UkGeographyResolver.cs`, tests and `docs/V18_UK_GEOGRAPHY.md`. This is a non-payable reference screen and **not** a tariff calculator.

## v1.9 — UK area reference lookup

Added authenticated `/v1/references/uk/geographical-areas/{id}` with bounded in-process cache and strict validation. See `docs/V19_UK_AREA_LOOKUP.md`. Reference-only: no payable tariff computation.

## v2.0 UK area membership evidence

Added a conservative, dated UK geographical-area membership reference parser and authenticated read-only endpoint. See `docs/V20_UK_MEMBERSHIP.md`. This does **not** calculate payable customs duties.

## v2.1 UK assessment orchestration
See [docs/V21_UK_ASSESSMENT.md](docs/V21_UK_ASSESSMENT.md). The endpoint `/v1/references/uk/assessments/{code}` combines measures and bounded geographic reference lookups; it returns review evidence only and never invents duty or VAT amounts.

## v2.2 — UK audit evidence

Added a deterministic SHA-256 evidence envelope for UK assessments at
`GET /v1/references/uk/assessments/{code}/evidence-package?origin=US&importDate=2026-11-02`.
See `docs/V22_UK_AUDIT_EVIDENCE.md`. It is not a payable quote.

## v2.3 UK reference transport and contract tests
See [docs/V23_UK_CONTRACT_TESTS.md](docs/V23_UK_CONTRACT_TESTS.md). Offline mocked HTTP tests; no live tariff calculation.

## v2.4 UK API contract guard
See [UK contract guard](docs/V24_UK_CONTRACT_GUARD.md) and [task 005](tasks/005-uk-live-api-contract-fixtures.md). CI added but build and live UK verification remain pending.

### v2.5 — UK lookup coverage
UK assessment exposes omitted area IDs when the 20-lookup safety cap is exceeded. See `docs/V25_UK_LOOKUP_COVERAGE.md` and TASK-006. Not a payable quote.

## v2.6: UK duty expression prototype
`UkDutyExpressionEngine` evaluates only explicitly supported GBP percentage, specific and additive compound expressions for illustrative arithmetic. It is NOT connected to payable quotes; `VerifiedForCalculation` always false. Legal applicability, preferences, quotas, taxes, and verified customs valuation remain blocked. See `tasks/007-uk-duty-calculation-engine.md`.


### v2.7 — UK duty input requirements
Specific GBP-per-unit illustrative calculations require only the relevant quantity. Percentage and compound expressions require customs value. See `docs/V27_UK_DUTY_INPUT_REQUIREMENTS.md`. No payable duty amounts are authorized.

## v2.8 UK VAT readiness
`UkVatReadiness.Evaluate` provides strictly illustrative arithmetic for fully supplied GBP inputs. It is not wired to a payable quote, and never emits `PayableVatGbp`. See `tasks/008-uk-import-vat-landed-cost.md`.

## v2.9 UK VAT evidence gate

`UkVatClassificationGate.Evaluate` checks completeness of proposed VAT classification evidence, distinguishes GB/XI and validates effective-date windows. It never confirms a rate or emits a payable VAT amount. See `docs/V29_UK_VAT_CLASSIFICATION.md`.

### v3.0 UK VAT review endpoint
`POST /v1/references/uk/vat/review` combines UK VAT classification and amount checks. See `docs/V30_UK_VAT_QUOTE_REVIEW.md`. This endpoint is **review-only**; it does not authorize a payable duty, VAT or landed-cost amount.

### v3.1 UK VAT review safety
The review endpoint now suppresses illustrative GBP amounts when core classification, territory, currency, date or rate consistency fails. Payable VAT remains null. See `docs/V31_UK_VAT_CONSISTENCY.md`.


### v3.2 quality baseline
See [TASK-010](tasks/010-quality-ci-security.md) and [CI quality notes](docs/V32_CI_QUALITY.md). Run `python3 scripts/check_project_integrity.py` locally. .NET CI must pass before release.

### v3.3 — Dependency vulnerability checks
GitHub Actions now runs a separate NuGet dependency-audit job. The job restores with NuGet advisory checks and fails on known NU1901–NU1904 warnings, then parses a machine-readable direct/transitive vulnerability report. Run the offline parser tests with `python3 -m unittest discover -s scripts/tests -v`. This does not replace secret scanning, integration tests or production security review.

### Repository secret guard (v3.4)
Run `python3 scripts/check_secrets.py` inside a Git checkout, plus `python3 -m unittest discover -s scripts/tests -v`. The guard checks tracked files for high-confidence patterns without logging matched values. It is not a substitute for Gitleaks, Git history scanning or credential rotation. See `tasks/010-quality-ci-security.md`.

### v3.5 CI security controls
The GitHub Actions workflow now includes a full Git-history Gitleaks scan (in addition to working-tree pattern checks). See `docs/V35_GIT_HISTORY_SECRETS.md`. Local Python tests passing does **not** establish that GitHub Actions, Gitleaks or .NET tests have passed.

### v3.6: Runtime smoke tests (task 010)

CI now starts the compiled API and checks health, capability safety claims, and rejection of unauthenticated quote/landed-cost requests. See `docs/V36_API_SMOKE_TEST.md`. Local Python tests passed; .NET runtime smoke and GitHub Actions remain unverified.

### v3.7 — API authorization smoke coverage

See `docs/V37_AUTH_SMOKE_COVERAGE.md`. The smoke test now probes seven protected routes, including UK tariff reference and VAT review, without credentials or external tariff calls. This is a development security regression test, **not** a production readiness certification.

### v3.8 — Smoke redirect safety
Local-only smoke tests reject HTTP redirects and require an explicit loopback port. See `docs/V38_SMOKE_REDIRECT_SAFETY.md`. This is not evidence of passing hosted CI or verified tariff calculations.

### Windows local verification — 2026-11-02
The .NET 8 Release build, 64 xUnit tests, 25 Python tests, publish and live local API smoke checks passed after fixing the discovered failures. See [commands, results and remaining CI requirements](docs/TASK010_WINDOWS_VERIFICATION.md). `global.json` selects a stable .NET 8 SDK. For a new checkout with untracked source files, run `python scripts/check_secrets.py --include-untracked` without staging files. Hosted CI, container smoke and history scanning remain unverified; TASK-010 is still in progress.

### Saleor review pilot — TASK-012
`POST /v1/integrations/saleor/reviews` accepts a signed Saleor payload for diagnostic review using existing merchant authentication and manually pinned store keys. Disabled by default; payable amounts remain null. See [configuration, samples and limits](docs/SALEOR_REVIEW_PILOT.md). The pilot has 44 new tests (108 xUnit total) and independent local signed-HTTP verification; live Saleor installation, Dashboard UI and checkout calculation are not implemented.

### Local Saleor sandbox
Run `scripts/start_saleor_sandbox.ps1` on Windows with Docker/Compose inside Ubuntu WSL. Core 3.23.45 is available at http://localhost:18080/graphql/ and Dashboard 3.23.41 at http://localhost:19090/. See [setup, credentials location, stop command and runtime evidence](docs/SALEOR_SANDBOX.md). The corrected GraphQL selection and real Saleor signing function passed local interoperability checks; the suite now has 111 passing xUnit cases. The integration App and real checkout-event flow remain follow-up work.

### Installed review App — 2026-11-03

Latest slice: [signed lifecycle and key rotation](docs/SALEOR_APP_LIFECYCLE.md). Real deactivate/reactivate/delete/reinstall preserved four earlier reviews and produced two new cart-event reviews, with six cards verified inside Dashboard. The local suite now has 143 passing xUnit tests. Collection stays disabled; production monitoring, recovery and distributed persistence remain open.
The Saleor App now installs in the sandbox, receives real checkout events automatically and displays authenticated reviews inside Dashboard. Start the sandbox, then run `python scripts/check_saleor_app.py --results <local-results>` to install/verify the App and create a test cart. See [configuration, security boundaries and actual evidence](docs/SALEOR_INSTALLED_APP.md). The suite has 124 passing xUnit cases; collection is still disabled and TASK-012 remains in progress.
