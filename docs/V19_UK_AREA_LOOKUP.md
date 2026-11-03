# v1.9 — UK geography reference lookup

This release adds a read-only `GET /v1/references/uk/geographical-areas/{id}` endpoint with merchant authentication, HTTPS host allowlisting, strict area-ID validation, a 1 MiB response limit, a 10-minute bounded in-memory cache and source URLs. It uses the existing UK API base URL (`TRADE_UK_TARIFF_API_BASE_URL`) and does not guess membership from an area label.

The route name `geographical_areas/{id}` is a proposed upstream path; verify against the currently deployed UK Trade Tariff API documentation before production use. A 404 does not prove the area does not exist in the tariff. Cache is per API process, not distributed, and is not a legal audit archive. Do not use this endpoint to determine payable duty.

References: https://github.com/trade-tariff/trade-tariff-backend and https://docs.trade-tariff.service.gov.uk/ . MIT code licensing does not automatically license tariff data.

Run `dotnet test` and integration tests with an approved API endpoint before deployment.
