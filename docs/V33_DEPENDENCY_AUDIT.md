# v3.3 — NuGet dependency audit (TASK-010)

The `.github/workflows/dotnet-ci.yml` workflow runs a dedicated `dependency-audit` job. It restores the xUnit project and transitive API dependencies with NuGetAudit enabled and NU1901–NU1904 treated as errors. It also requests a JSON vulnerability report and runs `scripts/check_nuget_audit.py`. The report is uploaded for review.

## Local verification

```bash
python3 scripts/check_project_integrity.py
python3 -m unittest discover -s scripts/tests -v
dotnet restore tests/GlobalTradeTax.Api.Tests/GlobalTradeTax.Api.Tests.csproj -p:NuGetAudit=true -p:NuGetAuditMode=all -p:NuGetAuditLevel=low -warnaserror:NU1901,NU1902,NU1903,NU1904
dotnet list tests/GlobalTradeTax.Api.Tests/GlobalTradeTax.Api.Tests.csproj package --vulnerable --include-transitive --format json > nuget-audit.json
python3 scripts/check_nuget_audit.py nuget-audit.json
```

The JSON report parser rejects malformed or empty project results; it does not independently verify whether the NuGet service was reachable. Do not interpret a clean report as a security guarantee. .NET SDK is unavailable in the authoring environment, so CI results remain unverified.
