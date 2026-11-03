# TASK-010 — Windows local verification

Executed on 2026-11-02 in `D:\Project\Source\Repos\abuzuhri\GlobalTradeTax`.
Task status remains **in-progress**. Results below supersede earlier local NOT RUN statements; they do not establish hosted CI or production readiness.

## Environment and preservation

- Windows x64, .NET SDK 8.0.308, runtime 8.0.33 and Python 3.12.14.
- `global.json` requires stable .NET 8 SDK feature band 300 or later, rolling forward within .NET 8. This avoids the machine's default .NET 11 preview SDK and matches CI's .NET 8 configuration.
- Git started on unborn `main` with no commits and zero tracked files. The existing upstream was shown as `origin/main [gone]`; no fetch or remote changes were performed.
- All 115 initial source/documentation files were backed up and hashed before editing. Existing files were retained; no Git staging, reset, commit or branch changes were performed.
- Python was run using the bundled Python executable because `python`/`python3` were not on PATH. Commands below use `python` as shorthand for that executable.

## Discovered failures and corrections

1. Initial build: CS8803 in `Program.cs`. Moved the partial Program type declaration after the top-level local functions.
2. Next build: CS1061 in `UkMeasureEvidence.cs`. Geography IDs had already been projected to strings but were accessed as tuples; use the string value in the country-shape check. Added different-country and unresolved-group regression cases.
3. Initial xUnit execution: 63 passed, 1 failed. `UkGeographyResolver` called `TryGetProperty` on an undefined JsonElement when memberships were absent. Check for an object before inspecting the property; missing evidence remains unresolved.
4. Initial dependency report parser rejected a valid clean .NET 8 report containing only a project path and no `frameworks`. Accept that real SDK form only within a valid version-1 vulnerability/transitive report with non-empty sources and no diagnostics. Continue rejecting malformed data. Pin `--output-version 1` in CI and audit the API and tests separately.
5. The default tracked-file secret scan correctly refused an empty index. Added an explicit `--include-untracked` mode using Git's tracked plus non-ignored untracked inventory, without modifying the index. CI's default tracked-only scan is unchanged.

The package-list JSON format and version selection are documented in [Microsoft's CLI reference](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-package-list). The clean report behavior above was observed directly with the local SDK.

## Executed checks

| Check | Actual result |
|---|---|
| `dotnet restore tests/GlobalTradeTax.Api.Tests/GlobalTradeTax.Api.Tests.csproj` | Passed during build |
| `dotnet build tests/GlobalTradeTax.Api.Tests/GlobalTradeTax.Api.Tests.csproj --no-restore -c Release -warnaserror` | Passed, 0 warnings, 0 errors |
| `dotnet test tests/GlobalTradeTax.Api.Tests/GlobalTradeTax.Api.Tests.csproj --no-build -c Release --logger 'trx;LogFileName=tests.trx' --results-directory <results>` | 64 passed, 0 failed, 0 skipped; TRX saved |
| `dotnet publish src/GlobalTradeTax.Api/GlobalTradeTax.Api.csproj --no-restore -c Release -o <publish>` | Passed |
| `python -m unittest discover -s scripts/tests -v` | 25 passed |
| `python scripts/check_project_integrity.py` | Passed, 14 task IDs and links |
| `python scripts/check_ci_workflow.py` | Passed; static required-token check only |
| `python scripts/check_secrets.py --include-untracked` | Passed, no high-confidence pattern findings; excludes ignored files |
| `actionlint -color=false -shellcheck= -pyflakes= .github/workflows/dotnet-ci.yml` | Passed with actionlint 1.7.12; optional ShellCheck/Pyflakes disabled |
| `python scripts/smoke_api.py --base-url http://127.0.0.1:5087 --wait-seconds 35` | 9 assertions passed against the Release build and again against the published DLL |

Actionlint was downloaded from [its official release](https://github.com/rhysd/actionlint/releases/tag/v1.7.12) and its Windows amd64 archive checked against the release checksum file: SHA-256 `6e7241b51e6817ea6a047693d8e6fed13b31819c9a0dd6c5a726e1592d22f6e9`.

Dependency checks executed successfully:

```powershell
dotnet restore tests/GlobalTradeTax.Api.Tests/GlobalTradeTax.Api.Tests.csproj -p:NuGetAudit=true -p:NuGetAuditMode=all -p:NuGetAuditLevel=low -warnaserror:NU1901,NU1902,NU1903,NU1904
dotnet list src/GlobalTradeTax.Api/GlobalTradeTax.Api.csproj package --vulnerable --include-transitive --format json --output-version 1
dotnet list tests/GlobalTradeTax.Api.Tests/GlobalTradeTax.Api.Tests.csproj package --vulnerable --include-transitive --format json --output-version 1
```

Both JSON reports were saved and accepted by `scripts/check_nuget_audit.py`. Neither reported vulnerable direct/transitive packages. This result depends on the SDK and advisory sources at execution time, and is not a general security certification.

Smoke tests used Development mode, `ASPNETCORE_URLS=http://127.0.0.1:5087`, no PostgreSQL connection, and no API/operator keys. Public health and capability responses passed; all seven protected probes returned 401. No UK upstream calls or financial calculations were requested. Processes were stopped after testing. The published artifact was checked by running `dotnet <publish>/GlobalTradeTax.Api.dll` under the same local settings.

## Remaining acceptance criteria

- Hosted GitHub Actions, artifact uploads and required PR status-check configuration have not been executed/verified.
- Docker CLI was unavailable, so Docker image build and container smoke were not run. Native HTTP smoke does not satisfy the container criterion.
- No Git history exists locally, and Gitleaks was unavailable; no claim of a successful history scan. The limited file-pattern guard is a separate check.
- PostgreSQL tenant isolation/integration, live UK API contracts, external providers and a broader security review remain outside this local baseline.
- Duty, VAT and settlement remain unverified; no payable amounts were enabled by these fixes.

The task cannot be marked `done` until its remaining acceptance criteria have evidence and review.
