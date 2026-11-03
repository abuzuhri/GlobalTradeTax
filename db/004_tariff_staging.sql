-- Run manually on existing PostgreSQL volumes: psql -f db/004_tariff_staging.sql
CREATE TABLE IF NOT EXISTS tariff_import_batches (
 batch_id uuid PRIMARY KEY,
 dataset_sha256 text NOT NULL UNIQUE,
 source_version text NOT NULL,
 source_url text NOT NULL,
 record_count integer NOT NULL CHECK (record_count > 0),
 status text NOT NULL DEFAULT 'staged' CHECK (status IN ('staged','rejected')),
 imported_at timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE IF NOT EXISTS tariff_reference_rows (
 batch_id uuid NOT NULL REFERENCES tariff_import_batches(batch_id),
 row_number integer NOT NULL CHECK (row_number > 0),
 destination char(2) NOT NULL,
 origin char(2) NOT NULL,
 hs_code varchar(12) NOT NULL,
 ad_valorem_percent numeric(9,4) NOT NULL CHECK (ad_valorem_percent BETWEEN 0 AND 1000),
 valid_from date NOT NULL,
 valid_to date NOT NULL,
 source_url text NOT NULL,
 source_version text NOT NULL,
 PRIMARY KEY (batch_id, row_number),
 CHECK (valid_from <= valid_to)
);
CREATE INDEX IF NOT EXISTS tariff_reference_lookup ON tariff_reference_rows(destination,origin,hs_code,valid_from,valid_to);
