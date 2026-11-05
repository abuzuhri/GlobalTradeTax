-- Apply after 001-004 on existing volumes. No existing rows are removed or activated.
CREATE UNIQUE INDEX IF NOT EXISTS tariff_batch_digest_identity
 ON tariff_import_batches(batch_id,dataset_sha256);

CREATE TABLE IF NOT EXISTS tariff_import_documents (
 batch_id uuid PRIMARY KEY,
 dataset_sha256 text NOT NULL CHECK (dataset_sha256 ~ '^[0-9a-f]{64}$'),
 document_bytes bytea NOT NULL CHECK (octet_length(document_bytes) BETWEEN 1 AND 1048576),
 archived_at timestamptz NOT NULL DEFAULT now(),
 UNIQUE (batch_id,dataset_sha256),
 FOREIGN KEY (batch_id,dataset_sha256) REFERENCES tariff_import_batches(batch_id,dataset_sha256),
 CHECK (dataset_sha256 = encode(sha256(document_bytes),'hex'))
);

CREATE TABLE IF NOT EXISTS tariff_reference_reviews (
 batch_id uuid NOT NULL,
 dataset_sha256 text NOT NULL,
 review_id uuid PRIMARY KEY,
 revision integer NOT NULL CHECK (revision > 0),
 previous_review_id uuid,
 disposition text NOT NULL CHECK (disposition IN ('reviewed_reference_only','rejected')),
 reviewed_by text NOT NULL CHECK (length(reviewed_by) BETWEEN 1 AND 128),
 licence_url text CHECK (licence_url IS NULL OR length(licence_url) BETWEEN 1 AND 2048),
 review_note text NOT NULL CHECK (length(review_note) BETWEEN 1 AND 2000),
 reviewed_at timestamptz NOT NULL,
 expires_at timestamptz,
 source_url text NOT NULL,
 source_version text NOT NULL,
 request_sha256 text NOT NULL CHECK (request_sha256 ~ '^[0-9a-f]{64}$'),
 UNIQUE (batch_id,revision),
 UNIQUE (batch_id,review_id),
 UNIQUE (batch_id,request_sha256),
 FOREIGN KEY (batch_id,dataset_sha256) REFERENCES tariff_import_documents(batch_id,dataset_sha256),
 FOREIGN KEY (batch_id,previous_review_id) REFERENCES tariff_reference_reviews(batch_id,review_id),
 CHECK ((revision=1 AND previous_review_id IS NULL) OR (revision>1 AND previous_review_id IS NOT NULL)),
 CHECK ((disposition='rejected' AND expires_at IS NULL) OR
        (disposition='reviewed_reference_only' AND licence_url IS NOT NULL AND expires_at IS NOT NULL AND expires_at > reviewed_at))
);
