# UK dated membership and measure candidate evidence

Verified locally on 2026-11-05 for TASK-006; [implementation PR #4](https://github.com/abuzuhri/GlobalTradeTax/pull/4) records the hosted checks and review. This slice resolves reference evidence; it does not calculate or authorize customs duty or import VAT. Conditions, certificates, preferences, quotas and legal eligibility remain open, so TASK-006 stays in progress.

## What changed

Previously, the assessment fetched today's commodity and area data even when `importDate` was historical. The membership parser expected dated membership resources that the real endpoint does not serialize. It also missed the declarable measure's `excluded_countries` relationship. The new assessment sends the requested date, reads the actual typed tree, and adds a separate candidate classification to each measure.

- All routes that already accept `importDate` now pass `as_of=yyyy-MM-dd` upstream: commodity screen/evidence/geography, area membership, assessment and its evidence package. Raw undated reference routes retain their existing behavior.
- `UkReferenceResult.asOfDate` records the date requested by the client. It is request context, not a signed upstream date assertion or independent confirmation of legal accuracy. Source URLs preserve the query. Area cache keys include the date and the existing credential fingerprint; historical, current and undated requests cannot share an entry.
- Unsupported dates more than 20 years ahead of UTC today return HTTP 400 with `invalid_import_date`. The upstream controller otherwise silently substitutes its default date outside its supported range.
- Membership requires the matching primary geographical-area ID, the same requested date, typed `children_geographical_areas` links and complete reachable included resources. Explicit paths can traverse nested groups. An unrelated included country, missing child, malformed linkage, duplicate resource, cycle or exceeded budget produces `unknown`. Absence from a tree does not establish non-membership. Old undated fixtures and the earlier assumed membership-resource format do not establish a dated match.
- Import direction comes from the commodity's typed `import_measures` linkage and boolean direction attributes. A measure with both flags true is accepted as `import_and_export` only when also linked by `export_measures`. Export-only or unrelated included measures are retained separately as other measures.
- Effective windows require valid start/end fields. An explicit null end means open-ended; a missing or invalid end is unknown. Boundaries are inclusive. Accepted forms are an ISO date or UTC midnight timestamp, plus the recorded final-second-of-day timestamp for end dates. Ambiguous locale strings, arbitrary times, offsets and inverted windows remain unknown.
- Exclusions read both official serializer names, `excluded_countries` and `excluded_geographical_areas`, with exact resource types. An explicit origin or positive dated exclusion-group membership blocks a candidate. Only explicit empty arrays establish an empty exclusion list. Other exclusions stay unknown when their membership cannot be established; an unlisted origin is not treated as proof that an exclusion is irrelevant. Exclusion references share the bounded lookup plan.
- Overlapping measures are preserved separately. No precedence, preference entitlement or rate is selected.

The assessment retains its top-level and per-measure `requires_review` status. Each measure adds `applicability`:

| Candidate status | Meaning within the supplied reference data |
|---|---|
| `candidate_for_review` | Linked import measure, valid date window, positive origin evidence and explicit empty exclusions; legal review still required. |
| `not_candidate` | Not linked as an import measure, outside the recorded effective window, or positive origin-exclusion evidence. This is not a final legal determination. |
| `indeterminate` | Missing, inconsistent or incomplete evidence prevents that reference screening conclusion. |

The object also exposes `direction`, `effectiveWindow`, `geographyEvidence`, `exclusionEvidence`, explicit `evidenceChecks` and `verifiedForCalculation=false`. Areas add `asOfDate`, `membershipPath` and `unresolvedChecks`. Import duty and VAT remain null, and no payable calculation provider or collection gate changes.

## Limits and compatibility

The existing 20 area lookups/concurrency four and 200 screened measures remain bounded. Omitted area IDs and an exceeded measure limit are now visible in assessment warnings. Tree traversal is limited to 1,024 included resources, 1,024 links per relationship and a 16-node path; exceeded limits produce unknown evidence. Duplicate or incomplete resources never produce a positive membership result.

JSON response fields above are additive. The stricter membership parser intentionally stops accepting the unverified synthetic relationship format used before the recorded contract was available. `/geography` remains the earlier conservative embedded-evidence projection; the new combined candidate classification is exposed by `/assessments`.

The evidence envelope is now **`uk-reference-evidence/v2`**. Its canonical hash includes applicability, requested area dates, ordered membership paths and sorted checks, and formats the import date using the invariant calendar. Capture time remains excluded. Consumers must preserve the schema version with the digest and must not compare v1/v2 hashes as identical canonical representations. The digest remains an integrity aid, not a signature or legal approval; `approvedForPayment=false`.

## Recorded contracts and sources

Four unmodified public responses are stored alongside [their byte counts, SHA-256 hashes, request dates and UTC capture times](../tests/GlobalTradeTax.Api.Tests/Fixtures/UkTariff/applicability-provenance.json). No credentials or customer data were sent.

| Request | Date | Observed response |
|---|---|---|
| Commodity `0101210000` | 2026-11-05 | HTTP 200; 67 unique embedded measures, 63 import links and five export links, with one shared measure. Recorded exclusions and UTC date forms are tested. |
| Area `1011` | 2026-11-05 | HTTP 200; explicit `1011 → US` path in the world-group tree. |
| Area `1060` | 2026-11-05 | HTTP 200; explicit paths to IN and ID. This is group-reference evidence, not preference entitlement. |
| Area `1060` | 2022-01-01 | HTTP 404; no historical membership inference, even after a current-date cache hit. |

Engineering contracts were reviewed at upstream revision `e5cab2b0fea383c2b5cc80dc6d25469fc3ede323`:

- [Application controller](https://github.com/trade-tariff/trade-tariff-backend/blob/e5cab2b0fea383c2b5cc80dc6d25469fc3ede323/app/controllers/application_controller.rb): `as_of`, supported range and date-scoped execution.
- [Time-machine validity filter](https://github.com/trade-tariff/trade-tariff-backend/blob/e5cab2b0fea383c2b5cc80dc6d25469fc3ede323/app/lib/sequel/plugins/time_machine.rb): inclusive start/end validity checks.
- [Area controller](https://github.com/trade-tariff/trade-tariff-backend/blob/e5cab2b0fea383c2b5cc80dc6d25469fc3ede323/app/controllers/api/v2/geographical_areas_controller.rb), [tree serializer](https://github.com/trade-tariff/trade-tariff-backend/blob/e5cab2b0fea383c2b5cc80dc6d25469fc3ede323/app/serializers/api/v2/geographical_area_tree_serializer.rb) and [area model](https://github.com/trade-tariff/trade-tariff-backend/blob/e5cab2b0fea383c2b5cc80dc6d25469fc3ede323/app/models/geographical_area.rb): dated contained-area memberships and their child relationships.
- [Commodity serializer](https://github.com/trade-tariff/trade-tariff-backend/blob/e5cab2b0fea383c2b5cc80dc6d25469fc3ede323/app/serializers/api/v2/commodities/commodity_serializer.rb), [declarable measure serializer](https://github.com/trade-tariff/trade-tariff-backend/blob/e5cab2b0fea383c2b5cc80dc6d25469fc3ede323/app/serializers/api/v2/declarable/measure_serializer.rb) and [general measure serializer](https://github.com/trade-tariff/trade-tariff-backend/blob/e5cab2b0fea383c2b5cc80dc6d25469fc3ede323/app/serializers/api/v2/measure_serializer.rb): import/export linkage and exclusion names. The recorded declarable response uses `excluded_countries`; synthetic general-contract cases cover the alternative field.

The [government API catalogue](https://www.api.gov.uk/hmrc/gov-uk-trade-tariff-api/) lists OGL v3 for the API data. Contains public sector information licensed under the [Open Government Licence v3.0](https://www.nationalarchives.gov.uk/doc/open-government-licence/version/3/). Upstream source was inspected as a contract reference; no Ruby implementation was copied. Current service-terms review and managed-live credentials remain separate TASK-005 prerequisites; this does not establish production or regulatory approval.

## Verification

Local results on 2026-11-05:

- Release warnings-as-errors build and publish passed with zero warnings/errors.
- **292 offline xUnit cases passed**, including 73 additional cases; ten opt-in live probes skipped by default. Golden cases cover groups, direct and group exclusions, overlapping/import-export measures, effective boundaries, actual timestamp forms, malformed contracts, missing data, cache separation and canonical evidence changes/calendar independence.
- **Six public live probes passed** separately through the production reference adapter. Managed live probes were not run; no operator credentials were available or used.
- **35 dated HTTP assertions and ten general smoke assertions passed** against an isolated loopback API. All six dated routes denied anonymous requests, rejected unsupported future dates and returned HTTP 200 with public configured data. Checks included actual membership paths, shared import/export measures, dated source URLs, v2 evidence and null monetary results. The isolated API was stopped afterward. A redacted local report is generated under ignored `TestResults/uk-applicability-http.json`.
- **25 Python tests**, task integrity, static workflow guard, limited working-tree secret guard and whitespace checks passed on the main-based branch. PostgreSQL/container integration is a separate completed slice in [PR #3](https://github.com/abuzuhri/GlobalTradeTax/pull/3); this change does not alter database schema or authentication policy.
- Hosted build/test/publish, dependency/UI audits, repository guards and full-history secret scan are recorded in the implementation PR after push. No change to merge policy or production deployment.

Reproduce offline:

```powershell
dotnet build tests/GlobalTradeTax.Api.Tests/GlobalTradeTax.Api.Tests.csproj -c Release -warnaserror
dotnet test tests/GlobalTradeTax.Api.Tests/GlobalTradeTax.Api.Tests.csproj --no-build -c Release
python -m unittest discover -s scripts/tests -v
python scripts/check_project_integrity.py
python scripts/check_ci_workflow.py
python scripts/check_secrets.py --include-untracked
```

To run the six bounded read-only public probes, enable the existing `uk_live_contract` workflow input, or set `TRADE_RUN_UK_LIVE_CONTRACT=1` in the test process and filter `Category=UkLiveContract`. Regular CI remains offline for UK references. Managed transport retains its existing endpoint pinning, token handling and credential-partitioned cache; a new offline managed-date case verifies authentication and date separation.
