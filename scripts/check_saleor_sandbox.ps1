param(
    [string]$Distribution = 'Ubuntu',
    [Parameter(Mandatory = $true)]
    [string]$ResultsDirectory
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$sandboxDirectory = Join-Path $repositoryRoot 'dev\saleor'
$linuxDirectory = (& wsl -d $Distribution --exec wslpath -a $sandboxDirectory).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve sandbox directory in WSL.' }
$queryBytes = [System.IO.File]::ReadAllBytes((Join-Path $repositoryRoot 'samples\saleor-review.graphql'))
$sampleBytes = [System.IO.File]::ReadAllBytes((Join-Path $repositoryRoot 'samples\saleor-review.json'))
$queryEnvironment = 'GTT_REVIEW_QUERY_BASE64=' + [Convert]::ToBase64String($queryBytes)
$sampleEnvironment = 'GTT_REVIEW_SAMPLE_BASE64=' + [Convert]::ToBase64String($sampleBytes)
$probe = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'saleor_sandbox_probe.py') -Raw
$probeOutput = $probe | & wsl -d $Distribution -u root --exec docker compose --project-directory $linuxDirectory -f "$linuxDirectory/compose.yaml" exec -T -e $queryEnvironment -e $sampleEnvironment api python manage.py shell -c "exec(__import__('sys').stdin.read())"
if ($LASTEXITCODE -ne 0) { throw 'Saleor runtime probe failed.' }
$proofLine = @($probeOutput | Where-Object { $_.StartsWith('GTT_SANDBOX_PROOF=') })
if ($proofLine.Count -ne 1) { throw 'Expected one Saleor probe result.' }
$proof = $proofLine[0].Substring('GTT_SANDBOX_PROOF='.Length) | ConvertFrom-Json
New-Item -ItemType Directory -Path $ResultsDirectory -Force | Out-Null
$proof | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $ResultsDirectory 'saleor-runtime-proof.json') -Encoding utf8
Write-Output "Saleor schema validation errors: $($proof.schemaErrors.Count)"
foreach ($validationError in $proof.schemaErrors) { Write-Output $validationError }
if ($proof.schemaErrors.Count -gt 0) { throw 'Saleor GraphQL selection does not match the pinned runtime schema.' }
$localSettings = @{}
foreach ($settingLine in (Get-Content -LiteralPath (Join-Path $sandboxDirectory '.env'))) {
    $parts = $settingLine.Split('=', 2)
    if ($parts.Count -eq 2) { $localSettings[$parts[0]] = $parts[1] }
}
$loginRequest = @{
    query = 'mutation Login($email: String!, $password: String!) { tokenCreate(email: $email, password: $password) { token errors { field message } } }'
    variables = @{ email = $localSettings['SANDBOX_ADMIN_EMAIL']; password = $localSettings['SANDBOX_ADMIN_PASSWORD'] }
} | ConvertTo-Json -Depth 5 -Compress
$loginResponse = Invoke-RestMethod -Uri 'http://localhost:18080/graphql/' -Method Post -ContentType 'application/json' -Body $loginRequest
if ($loginResponse.errors -or -not $loginResponse.data.tokenCreate.token) { throw 'Sandbox staff login failed.' }
$versionResponse = Invoke-RestMethod -Uri 'http://localhost:18080/graphql/' -Method Post -ContentType 'application/json' -Body '{"query":"{ shop { version } }"}' -Headers @{ Authorization = 'Bearer ' + $loginResponse.data.tokenCreate.token }
if ($versionResponse.errors) { throw 'Saleor GraphQL version query failed.' }
if ($versionResponse.data.shop.version -notmatch '^3\.23\.45') { throw 'Unexpected Saleor runtime version.' }
$dashboardResponse = Invoke-WebRequest -Uri 'http://localhost:19090/' -UseBasicParsing
if ($dashboardResponse.StatusCode -ne 200) { throw 'Dashboard HTTP check failed.' }
$summary = [ordered]@{
    saleorVersion = $versionResponse.data.shop.version
    dashboardHttpStatus = $dashboardResponse.StatusCode
    schemaErrors = @($proof.schemaErrors)
    querySha256 = $proof.querySha256
    signatureSource = $proof.mode
}
$summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $ResultsDirectory 'saleor-sandbox-summary.json') -Encoding utf8
$summary | ConvertTo-Json -Depth 8
