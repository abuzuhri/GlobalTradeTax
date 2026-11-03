# v3.2 — Quality baseline (TASK-010)

## Implemented
- GitHub Actions `.NET 8` restore, Release build (`-warnaserror`), xUnit tests and publish artifact.
- TRX test report upload even when test job fails.
- Least-privilege workflow permissions and checkout without persisted credentials.
- Independent Python repository integrity check.

## Reproduce
```bash
python3 scripts/check_project_integrity.py
dotnet restore tests/GlobalTradeTax.Api.Tests/GlobalTradeTax.Api.Tests.csproj
dotnet build tests/GlobalTradeTax.Api.Tests/GlobalTradeTax.Api.Tests.csproj -c Release --no-restore -warnaserror
dotnet test tests/GlobalTradeTax.Api.Tests/GlobalTradeTax.Api.Tests.csproj -c Release --no-build
```

## Verification status
Python check passed on 2026-11-02. No .NET SDK available locally. GitHub CI not executed here.

## Remaining
Pin GitHub Actions to audited commit SHAs, dependency lockfiles and vulnerability gate, secret scanning, integration tests with PostgreSQL, container smoke test, and required branch checks.

This release does not change UK customs duty/VAT calculation logic or authorize payable tax amounts.
