# v3.7 — UK authorization smoke coverage

Task: 010 — Quality / CI / Security.

The `scripts/smoke_api.py` probe now checks the anonymous-access boundary on seven protected endpoints, in addition to health and capabilities. It deliberately sends no credentials and never contacts the UK tariff provider. Every protected endpoint must return HTTP 401; a 200, 400 or 503 is treated as a failure. This catches route-binding errors that run before authentication as well as accidental public exposure.

The smoke target must be HTTP on localhost / 127.0.0.1 / ::1; this protects against running an unauthenticated probe against an arbitrary remote service.

Run locally after starting the API:

```bash
python3 scripts/smoke_api.py --base-url http://127.0.0.1:5087
python3 -m unittest discover -s scripts/tests -v
```

Offline Python tests passed: 16. Project-integrity and CI static checks passed. Actual .NET build, xUnit, hosted CI and HTTP probe remain unverified. Git-history secret scanning requires a checked-out Git repository and has not run against the packaged archive.

Financial calculation remains disabled pending authoritative UK tariff validation and independent reconciliation against official UK outcomes.
