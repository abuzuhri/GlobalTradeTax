# Merchant API key operations

Use the migration procedure before deploying the updated API. Migration 006 adds a nullable expiry: existing keys retain their previous indefinite lifetime until an operator rotates or revokes them. New keys default to 90 days; `--days` accepts 1–365. Authentication checks PostgreSQL server time on every request and rejects expiry at or before that time, revocation, disabled keys and disabled tenants. There is no overlap on rotation; arrange a coordinated cutover.

Install `psycopg[binary]` in the operator environment and configure `DATABASE_URL` through a protected environment or secret manager. Do not put credentials or keys in command arguments, logs or tickets. Run from a trusted terminal with output capture, shell tracing and session recording disabled:

```sh
python scripts/provision-tenant.py merchant-001 "First Merchant"
python scripts/tenant_keys.py issue merchant-001 --days 90
python scripts/tenant_keys.py list merchant-001
python scripts/tenant_keys.py rotate merchant-001 KEY_UUID --days 90
python scripts/tenant_keys.py revoke merchant-001 KEY_UUID
```

Issue and rotate print JSON containing the new key exactly once, after commit, on standard output. Transfer it through an approved secret channel and protect the terminal and output. If delivery fails, revoke the new key and issue another; the secret cannot be recovered. List prints only metadata. The old provisioning syntax remains supported, creates a 90-day key and prints JSON; automation that parsed its former two-line output must change. Existing disabled tenants are never enabled, and issue/rotate refuse them. Revoke may operate on a disabled tenant. Rotation rejects inactive, expired, stale or cross-tenant keys; revoke rejects inactive or mismatched keys. Multiple separately issued keys can coexist. Each operation locks the tenant, and mutation plus tenant-bound audit commit together. The audit contains key IDs, lifetime and PostgreSQL role, never a secret or verifier. Database roles are recorded as reported by PostgreSQL, not verified human identities; external access controls and independent audit records remain necessary.

The database connection must have sufficient authority to read tenants/keys and write keys/audits (and create tenants for provisioning). This tool does not establish least privilege or production authorization, and someone with direct database access can bypass its policy and audit. Restrict database access and protect the audit table. Production threat-model and privilege approval remain open. Tenant rate budgets remain keyed by tenant across keys. Tax calculation and payment approval gates are unchanged.
