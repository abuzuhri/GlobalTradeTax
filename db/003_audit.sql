CREATE TABLE IF NOT EXISTS audit_events (
 event_id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
 tenant_id text NOT NULL REFERENCES tenants(tenant_id),
 action text NOT NULL,
 status text NOT NULL,
 details_json jsonb NOT NULL,
 created_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS idx_audit_tenant_time ON audit_events(tenant_id, created_at DESC);
