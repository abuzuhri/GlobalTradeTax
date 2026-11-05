-- Historical v0.4 schema: db/001_init.sql at bcec3f994af5ac04cb7dc5f1fa15f41bc6452f02.
-- Keep this fixture independent of later edits to the current migration scripts.
CREATE TABLE IF NOT EXISTS quotes (
    quote_id text PRIMARY KEY,
    tenant_id text NOT NULL,
    idempotency_key text NOT NULL,
    fingerprint text NOT NULL,
    response_json text NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    UNIQUE (tenant_id, idempotency_key)
);
CREATE INDEX IF NOT EXISTS idx_quotes_tenant_created ON quotes (tenant_id, created_at DESC);
