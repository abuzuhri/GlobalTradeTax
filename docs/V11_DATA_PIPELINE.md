# v1.1 — tariff data ingestion safety gate

New endpoint: POST /v1/tariff-datasets/validate (authenticated, Content-Type: text/plain).
It accepts UTF-8 CSV with exactly these fields:
`destination,origin,hs_code,ad_valorem_percent,valid_from,valid_to,source_url,source_version`

The parser validates ISO-style country syntax, numeric HS code length, rates, effective dates, HTTPS source provenance, and returns SHA-256 of the raw dataset for auditability. CSV with quoting is deliberately rejected. This is a **validation-only** API; it does not import into PostgreSQL or enable calculations.

**Why not activate rates yet?** GitHub repositories are not authoritative rate feeds. An ad-valorem percentage alone does not resolve preferential tariffs, trade remedies, customs value, excise, VAT, threshold rules, clearance fees or other measures. A legal right to reuse the data commercially is also required.

Next: build a licensed source adapter for EU TARIC and versioned PostgreSQL staging, then independent customs/tax reconciliation test fixtures. Do not expose settlement or guaranteed landed costs before these validations.

Example (development authentication required):
`curl -X POST http://localhost:5080/v1/tariff-datasets/validate -H 'X-API-Key: ...' -H 'Content-Type: text/plain' --data-binary @data/your-curated-rates.csv`

No sample rates are provided because fabricated rates could be mistaken for current legal tariffs.
