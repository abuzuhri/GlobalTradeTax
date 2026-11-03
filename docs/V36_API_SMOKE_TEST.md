# v3.6 — API runtime smoke-test gate

Task: `tasks/010-quality-ci-security.md`.

## Scope

The CI `test` job now launches the Release-built API bound to `127.0.0.1:5087` and runs `scripts/smoke_api.py`. The script checks:

1. `/health` returns 200 and `status=healthy`.
2. `/v1/capabilities` returns 200, `settlement=not-implemented`, and `calculation=provider-required`.
3. Unauthenticated `POST /v1/quotes` returns 401.
4. Unauthenticated `POST /v1/landed-cost/estimate` returns 401.

No credentials, PostgreSQL, external tariff service, or payment account is needed for these smoke tests. The CI step stops the process and shows a bounded log excerpt on failure.

## How to run locally

```bash
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://127.0.0.1:5087 dotnet run --project src/GlobalTradeTax.Api/GlobalTradeTax.Api.csproj -c Release
# In another terminal:
python3 scripts/smoke_api.py --base-url http://127.0.0.1:5087
```

## Verification status

- 15 Python tests passed locally.
- Task integrity and static CI/secret checks passed locally.
- Actual API startup and HTTP smoke checks **not run** here (no .NET SDK).
- GitHub Actions not run. No production readiness or financial correctness claim.
