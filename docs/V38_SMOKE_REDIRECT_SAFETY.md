# v3.8 — Smoke redirect safety (TASK-010)

The anonymous API smoke client now disables HTTP redirects. A compromised or misconfigured local API cannot cause the smoke client to follow a 30x response to an external host. The base URL must use plain HTTP on loopback, include an explicit port and have no path prefix, credentials, query or fragment.

Two offline regression tests cover a real local redirecting HTTP server and invalid URL targets. The 302 status is returned as a failed smoke assertion rather than followed. No UK tariff lookups are performed.

Run `python3 -m unittest discover -s scripts/tests -v`, `python3 scripts/check_project_integrity.py`, `python3 scripts/check_secrets.py` and `python3 scripts/check_ci_workflow.py`. Build and hosted CI remain pending.
