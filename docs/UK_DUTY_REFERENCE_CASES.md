# Duty reference cases — TASK-007

Verified on 2026-11-06: nine independent examples exercise the duty engine, comprising five observed live calculator journeys and four published calculator test examples. All five live **import duty rows** match at the displayed two-decimal precision. The matrix stays `awaiting_independent_review` with `approved_for_payment=false`; classification, legal selection, entitlement and payment approval are not established. TASK-007 remains in progress.

## Observed live examples

All journeys use synthetic goods value £1,000, shipping £0, insurance £0, US origin and import date **5 November 2026**. They were observed on **6 November 2026**. No customer data, declaration or payment was submitted.

| Case | Territory / commodity / measure | Quantity | Observed import duty | Headline duty + VAT |
|---|---|---|---|---|
| Percentage | GB / 0101299000 / 20000002 | Not required | £100.00 (10%) | £320.00 |
| Compound | GB / 0201300090 / 20000070 | 100 kg | £373.00 (12% + £253/100 kg) | £373.00 |
| Specific | GB / 1001990094 / 20001362 | 2,000 kg | £158.00 (£79/tonne) | £158.00 |
| Explicit zero | GB / 8471300000 / 20010968 | Not required | £0.00 (published 0%) | £200.00 |
| EUR compound | XI / 0201300090 / 2049572 | 100 kg | £381.58 (12.8% + €303.40/100 kg) | £381.58 |

Compare the expanded **Import duty** row. The percentage example's £320 headline contains £100 duty and £220 VAT; the tests preserve this distinction. Zero duty also does not mean zero VAT.

The XI published November tariff conversion is **0.83578 GBP per EUR**, producing component arithmetic **£381.575652**, displayed as £381.58. The page's exchange-rate note rounds to four decimal places and is not used as the full-precision input. The UI proves only the displayed amount; the unrounded expectation comes from published component/FX data.

For XI, the service displayed a trade-defence/at-risk explanation and asked about document Y155 for measure 695. The synthetic answer was **None of the above**. That explanation, confirmation and result are archived. This single journey does not validate general NI risk, UKIMS, waiver or Union/UK comparison rules.

The GB compound page also offered a separate £0 preferential quota. It was not selected as the base duty; displaying an option does not prove entitlement. The graphs contain other zero rates restricted to group 1080, distinct from selected group-1011 measures. A previous test comment incorrectly called those future measures; the comment is corrected.

## Evidence and replay

The [matrix](../tests/GlobalTradeTax.Api.Tests/Fixtures/UkDutyReference/cases.json) identifies selected measures, inputs, expected amounts and captures. The [provenance manifest](../tests/GlobalTradeTax.Api.Tests/Fixtures/UkDutyReference/provenance.json) records timestamps, lengths, digests, API URLs and reviewed source revision.

- `observations/` contains unedited rendered main-content text for confirmation/results, five screenshots and the XI route explanation. These are browser observations, not raw HTTP bodies.
- `api/` contains two new exact public responses for wheat and portable computers. Other cases reuse the attributed [UK completion fixtures](../tests/GlobalTradeTax.Api.Tests/Fixtures/UkCompletion/provenance.json).
- `.gitattributes` prevents newline conversion of hashed evidence on Windows/Linux. Tests verify archive digests and the captured duty row rather than the headline.
- Public tariff content is attributed under [OGL v3](https://www.nationalarchives.gov.uk/doc/open-government-licence/version/3/), as displayed on the service. Current access/rights review remains separate.

To replay, open the commodity on the [Online Trade Tariff](https://www.gov.uk/trade-tariff), select **Start a duty calculation**, enter the recorded territory/origin/date, valuation and quantity, check the confirmation, continue and expand **Show calculations**. Record the full context and duty row. Use current entry links in the matrix; the archived standalone calculator's route layout does not identify the current service. Results URLs are session-dependent: re-enter the facts rather than opening a result URL alone. Investigate drift before updating frozen expectations; obtain review of changes.

## Published calculator tests

The standalone calculator repository is archived. The maintained [Trade Tariff frontend](https://github.com/trade-tariff/trade-tariff-frontend) was reviewed at [bb5d3b83d07abb95b1cb6c6c548b7393786b86a9](https://github.com/trade-tariff/trade-tariff-frontend/tree/bb5d3b83d07abb95b1cb6c6c548b7393786b86a9). This is not an attestation of the deployed website version.

| Published test | Inputs in this engine's units | Expected unrounded GBP |
|---|---|---|
| Ad valorem | £30 customs value (10 goods + 10 shipping + 10 insurance), 8% | 2.4 |
| GBP specific | £35.10/100 kg, 10,000 kg (100 source units) | 3510 |
| Coerced kilograms | £35.10/100 kg, 100 kg (source multiplier 0.01) | 35.1 |
| EUR specific | €35.10/100 kg, 10,000 kg, test FX 0.8571 | 3008.421 |

The last FX comes from the official test fixture's July 2021 record; it is not a current shipment rate. These cases are labelled `pinned_official_rspec`. Exact spec/factory/FX/licence files under `upstream/` have verified Git-blob identities, SHA-256 digests, pinned links and the full MIT/Crown copyright notice. They provide expectations and are not executed or shipped as application logic. This does not claim execution of the upstream Rails suite.

## Precision correction and verification

Four regressions failed before the fix: premature tonne scaling lost a representable positive amount; a positive percentage could become zero; EUR-to-GBP multiplication could become zero; and the legacy evaluator had the same unit/percentage loss, including inside a compound expression. Both evaluators now multiply before unit scaling and return unknown on positive underflow. Explicit source zero remains illustrative zero; unsupported inputs and overflow stay unresolved. This does not approve statutory rounding or remove every finite-decimal rounding difference.

Run `dotnet test tests/GlobalTradeTax.Api.Tests/GlobalTradeTax.Api.Tests.csproj -c Release -warnaserror` and the Python/repository checks from the main runbook. Local Release verification passed **447 offline xUnit cases**, with **16 optional live API probes skipped**. The five browser comparisons were separate from those API probes. The four precision regressions now pass. Python/hosted results are recorded in the review PR after execution. Payment approval remains disabled.
