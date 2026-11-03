# US to worldwide launch plan

## Stage A: data contracts
1. Require seller/exporter, buyer/importer, origin country per item, ship-from, destination, HS and Schedule B classifications (distinct), customs value, currency, freight, insurance, product restrictions and Incoterms.
2. Keep export screening and destination import quotation separate.
3. Store rules by effective date and jurisdiction; never use a static VAT rate for live customs quotes.

## Stage B: pilot corridors
- US -> EU: EU tariff data, IOSS qualification, import VAT, current low-value duties and destination fees.
- US -> Saudi Arabia and UAE: local broker confirmation, VAT and duty, restricted-product requirements.
- US -> Brazil: Remessa Conforme eligibility, import tax, ICMS, authorized logistics partner.

## Stage C: production safeguards
- Licensed rate/tariff provider contract and redistribution rights.
- Quote provenance, versioning, expiration and audit trail.
- PostgreSQL tenant isolation and transactional idempotency.
- API key rotation, rate limits, observability, CI integration tests.
- Settlement only through legally authorized carriers/brokers/payment partners.

## Official starting references
- US Census AES/EEI: https://www.census.gov/foreign-trade/aes/
- US BIS export controls: https://www.bis.gov/
- EU IOSS: https://taxation-customs.ec.europa.eu/ioss_en
- EU customs duties: https://taxation-customs.ec.europa.eu/customs-4/calculation-customs-duties_en
- Saudi ZATCA: https://zatca.gov.sa/en/Pages/default.aspx
- UAE Federal Tax Authority: https://tax.gov.ae/en/default.aspx
- Brazil Receita Federal: https://www.gov.br/receitafederal/pt-br
