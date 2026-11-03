CREATE TABLE IF NOT EXISTS tenants (
 tenant_id text PRIMARY KEY,
 display_name text NOT NULL,
 enabled boolean NOT NULL DEFAULT true,
 created_at timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE IF NOT EXISTS tenant_api_keys (
 key_id uuid PRIMARY KEY,
 tenant_id text NOT NULL REFERENCES tenants(tenant_id),
 key_prefix text NOT NULL,
 key_hash text NOT NULL UNIQUE,
 enabled boolean NOT NULL DEFAULT true,
 created_at timestamptz NOT NULL DEFAULT now(),
 revoked_at timestamptz
);
CREATE INDEX IF NOT EXISTS idx_tenant_api_keys_prefix ON tenant_api_keys(key_prefix);
