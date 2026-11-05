# Managed UK tariff API — TASK-005

Implemented and locally verified on 2026-11-04. This is an opt-in, read-only authentication path for the UK reference adapter. All duty/VAT payment gates remain closed.

## Configuration

| Setting | Purpose |
|---|---|
| `TRADE_UK_TARIFF_API_BASE_URL` | Set to `https://api.trade-tariff.service.gov.uk/uk/api/` for managed GB access. |
| `TRADE_UK_TARIFF_XI_API_BASE_URL` | Optional distinct NI prefix: `https://api.trade-tariff.service.gov.uk/xi/api/`. Uses the same credentials and fixed OAuth endpoint; no GB fallback. |
| `TRADE_UK_TARIFF_CLIENT_ID` | Client ID provisioned through the official developer portal. |
| `TRADE_UK_TARIFF_CLIENT_SECRET` | Corresponding secret, supplied through the operator's secret provider or process environment. |

The existing public `https://www.trade-tariff.service.gov.uk/uk/api/v2/` configuration still works without these credentials. The default remains unconfigured. Managed access must be explicitly selected; an authentication failure never switches to public access.

[Official managed-authentication guidance](https://docs.trade-tariff.service.gov.uk/the-trade-tariff-api.html#authenticating-with-managed-credentials), reviewed at [this source revision](https://github.com/trade-tariff/trade-tariff-api-docs/blob/f57cd82addcb747aeb23f16190e66d825ec9ce7d/source/the-trade-tariff-api.html.md.erb), specifies client-credentials form submission to `https://auth.id.trade-tariff.service.gov.uk/oauth2/token`, followed by Bearer authorization and `Accept: application/vnd.hmrc.2.0+json` on the managed API. The same guide explicitly documents unauthenticated exploration through the public `www` host. Production credentials are created through the [developer portal](https://hub.trade-tariff.service.gov.uk/); no account or key was provisioned by this implementation.

Keep real values out of tracked files, command-line arguments, support messages and test reports. The tracked `.env.example` contains commented placeholders only and is not loaded automatically. Environment changes require a service restart; a configuration provider that supports reload also triggers a new token/cache partition when credentials change.

## Token and transport behavior

- Client credentials are submitted only to the fixed official HTTPS token URL. No configurable token/discovery endpoint or request-controlled authentication host exists. HTTP, alternate ports, user information, queries/fragments and unsupported host/path combinations are rejected before credential exchange. As of 2026-11-05, the separate XI setting accepts only the official XI prefix. A GB setting cannot accept XI, or vice versa.
- Public lookups never invoke the token provider or attach its token. Managed lookups fail with `not_configured` before network activity if either credential is absent. Existing merchant authentication still protects the local HTTP routes.
- The process shares one memory-only token provider. Successful refreshes are serialized so concurrent requests reuse one token; failed refreshes have a five-second cooldown per credential configuration. Tokens and credential response bodies are not returned in API results or written to storage.
- Responses must contain a nonempty Bearer token and a numeric `expires_in` between 1 and 86,400 seconds. The parser rejects invalid token characters, tokens over 16 KiB, duplicate fields, OAuth error documents and token bodies over 64 KiB, including responses with unknown length. Unsupported/missing expiry metadata fails closed rather than assuming a one-day lifetime. This response contract has been tested with simulated responses; actual managed credentials remain needed to confirm the live service response.
- Lifetime starts at token-request initiation, with an early-renewal margin of up to 60 seconds. A managed HTTP 401 invalidates that token and permits one refresh/retry; a second 401 stops. A delayed old-token rejection cannot evict a newer token. HTTP 403, 429 and other failures do not trigger automatic retries or alternative hosts.
- Both real HTTP transports disable redirects and cookies. Token exchange has an eight-second budget including its response body; a reference operation has a 20-second overall budget including token wait/renewal and bounded response reading. Individual configured HTTP clients also have an eight-second timeout. Tariff documents retain the 1 MiB limit.
- Valid geographical-area responses are cached for ten minutes. These are public reference data; managed cache keys also include a credential fingerprint, so removing/replacing configuration cannot reuse the previous configuration's cache. Token/HTTP/schema failures are not reference-cache entries.

Changing credentials prevents reuse on subsequent requests; already-running requests can finish under their previous snapshot. Restart/drain the process when immediate retirement is required. There is no distributed token/revocation coordinator in this slice.

## Verification and opt-in live probes

Local commands:

```powershell
dotnet build tests/GlobalTradeTax.Api.Tests/GlobalTradeTax.Api.Tests.csproj --no-restore -c Release -warnaserror
dotnet test tests/GlobalTradeTax.Api.Tests/GlobalTradeTax.Api.Tests.csproj --no-build -c Release
```

A fresh checkout needs `dotnet restore` first. Normal runs passed **219 offline cases** and explicitly skipped eight live probes (four public and four managed). The managed changes add **52 offline cases**, covering encoded forms, host/credential isolation, refresh, concurrency, cooldown, rotation, stale 401s, retry limits, cancellation, response limits and generic failure messages. The four public live probes also passed separately after the refactor.

An isolated Development API passed ten general HTTP smoke assertions in each of its public and managed-without-credentials configurations, six authenticated public live-data checks, and two authenticated managed checks returning HTTP 503 for missing credentials. Temporary processes were stopped afterward. The 25 Python tests and repository integrity/static CI/limited secret checks passed. No live managed token was requested: neither the local environment nor the repository's Actions secrets contained managed credentials at verification time.

After provisioning both settings through the secret provider, opt into the fixed managed probes:

```powershell
$previousManagedProbe = $env:TRADE_RUN_UK_MANAGED_LIVE_CONTRACT
try {
    $env:TRADE_RUN_UK_MANAGED_LIVE_CONTRACT = '1'
    dotnet test tests/GlobalTradeTax.Api.Tests/GlobalTradeTax.Api.Tests.csproj --no-build -c Release --filter 'Category=UkManagedLiveContract'
} finally {
    if ($null -eq $previousManagedProbe) {
        Remove-Item Env:\TRADE_RUN_UK_MANAGED_LIVE_CONTRACT -ErrorAction SilentlyContinue
    } else {
        $env:TRADE_RUN_UK_MANAGED_LIVE_CONTRACT = $previousManagedProbe
    }
}
```

The live tests force the approved managed base URL. Enabling the flag without credentials fails rather than silently running public probes. They cover commodity/area identity and both HTTP 404 cases; no raw token response is recorded as a fixture.

In GitHub, configure Actions secrets named `TRADE_UK_TARIFF_CLIENT_ID` and `TRADE_UK_TARIFF_CLIENT_SECRET`, then manually dispatch the workflow with `uk_managed_live_contract` enabled. Build/restore happens before the secret-bearing test step. Its TRX report contains test outcomes, not OAuth response bodies. Ordinary push/PR checks keep both live jobs disabled; no scheduled monitor was added.

## Hosted CI finding

The previously uninspected [main-branch run](https://github.com/abuzuhri/GlobalTradeTax/actions/runs/41683857783) for `b8eb349` actually passed the application, dependency, UI and repository jobs. Its failure was Gitleaks reporting two documentation placeholders in the initial `bcec3f9` commit: the README's `your-secret-key` and the implementation example's `replace-with-a-long-random-secret` header.

`.gitleaksignore` now lists only those two exact historical finding fingerprints. No rule, path or whole commit is ignored, and the default rules/full-history scan remain enabled. This fixes the identified false positives without changing Git history.

The checksum-verified Gitleaks 8.24.2 local rescan of both existing commits passed with zero remaining findings. Checksum-verified actionlint 1.7.12 also passed (optional ShellCheck/Pyflakes disabled), as did `git diff --check`.

Hosted verification for source commit [2673050](https://github.com/abuzuhri/GlobalTradeTax/commit/267305009a92ffbf94e8929c2ae95b2192118376):

- [PR workflow run](https://github.com/abuzuhri/GlobalTradeTax/actions/runs/41879751626): **success**, including application build/test/publish, dependency audit, UI, repository checks and full-history Gitleaks. Its test log confirms 219 passed, eight live probes skipped, zero build warnings/errors and ten HTTP smoke assertions.
- [Manually enabled public live workflow](https://github.com/abuzuhri/GlobalTradeTax/actions/runs/41879785451): **success**, including four public live probes with zero skips and a successful full-history scan. The managed live job was deliberately skipped because credentials have not been provisioned.
- Both hosted Gitleaks logs confirm three commits scanned and no remaining findings with the two reviewed exact exceptions applied. [Draft PR #1](https://github.com/abuzuhri/GlobalTradeTax/pull/1) contains the changes; its latest checks show verification of any subsequent documentation-only update.

TASK-005 remains in progress: live managed access/response verification, current service-terms review, broader contracts and operational drift monitoring remain open. Hosted CI cannot prove legal applicability or authorize payable customs amounts.
