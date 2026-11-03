# v3.5 — Git history secret scanning

## Scope
- GitHub Actions job `git-history-secrets` checks out full repository history and runs Gitleaks v8.24.2 with redacted output against all available Git refs.
- This complements, but does not replace, `scripts/check_secrets.py` on current tracked files.
- `scripts/check_ci_workflow.py` is a lightweight text-based policy guard, **not** YAML validation or execution.

## Verification
- Local: `python3 -m unittest discover -s scripts/tests -v` — 12 tests passed.
- Local: `python3 scripts/check_project_integrity.py`, `python3 scripts/check_secrets.py`, `python3 scripts/check_ci_workflow.py` — passed.
- Not run: GitHub Actions, containerized Gitleaks scan, .NET compilation/xUnit.

## Follow-up
1. Push branch to GitHub and inspect `git-history-secrets`, `test`, `dependency-audit`, and `repository-checks` job logs.
2. Fix any image availability/CLI compatibility issues discovered in hosted CI.
3. Require all relevant checks in GitHub branch protection.
4. Rotate exposed credentials and invalidate tokens if leaks are found. Removing a secret from Git does not invalidate it.
5. Run container smoke tests and verify the UK tariff API against actual upstream fixtures before any financial use.
