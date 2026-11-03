# v3.4 — Secret guard

Scope: TASK-010. Added a fail-closed Python scanner over Git-tracked files and CI integration. The script recognizes selected private key headers, GitHub tokens, AWS access key identifiers, Stripe live secrets and Slack tokens. It reports only file names and pattern categories. It does not scan Git history, entropy, binary files larger than 2 MB, or validate whether a token is active.

Run: `python3 scripts/check_secrets.py` and `python3 -m unittest discover -s scripts/tests -v` from a Git checkout. GitHub Actions runs these in repository-checks.

Local checks: 9 Python unit tests passed, 14-task integrity passed, and secret scan passed against 106 staged files. The temporary local Git index was used only to provide a tracked-file inventory for verification; no commit or push was performed. .NET SDK not installed, so C# build and xUnit tests not run. Hosted CI not run.

Next: adopt a dedicated scanner with history support, run CI, remediate actual findings, and require branch protection. Duty and VAT amounts remain unverified.
