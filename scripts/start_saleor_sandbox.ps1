param(
    [string]$Distribution = 'Ubuntu'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$sandboxDirectory = Join-Path $repositoryRoot 'dev\saleor'
$environmentFile = Join-Path $sandboxDirectory '.env'
$keepaliveFile = Join-Path $sandboxDirectory '.keepalive.json'
$keepaliveRunning = $false
if (Test-Path -LiteralPath $keepaliveFile) {
    $savedProcess = Get-Content -LiteralPath $keepaliveFile -Raw | ConvertFrom-Json
    $existingProcess = Get-Process -Id $savedProcess.Id -ErrorAction SilentlyContinue
    $savedTicks = if ($savedProcess.StartTimeTicks) { [long]$savedProcess.StartTimeTicks } else { ([DateTimeOffset]$savedProcess.StartTime).UtcTicks }
    $keepaliveRunning = $null -ne $existingProcess -and $existingProcess.ProcessName -eq 'wsl' -and
        $existingProcess.StartTime.ToUniversalTime().Ticks -eq $savedTicks
}
if (-not $keepaliveRunning) {
    $keepaliveProcess = Start-Process -FilePath 'wsl.exe' -ArgumentList @('-d', $Distribution, '--exec', 'sleep', 'infinity') -WindowStyle Hidden -PassThru
    @{ Id = $keepaliveProcess.Id; StartTimeTicks = $keepaliveProcess.StartTime.ToUniversalTime().Ticks } |
        ConvertTo-Json | Set-Content -LiteralPath $keepaliveFile -Encoding utf8
}
if (-not (Test-Path -LiteralPath $environmentFile)) {
    $secrets = @('SANDBOX_DB_PASSWORD', 'SANDBOX_SECRET_KEY', 'SANDBOX_ADMIN_PASSWORD', 'SANDBOX_REVIEW_API_KEY')
    $settings = foreach ($secretName in $secrets) {
        $bytes = [System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32)
        $secretName + '=' + [Convert]::ToHexString($bytes).ToLowerInvariant()
    }
    $settings += 'SANDBOX_ADMIN_EMAIL=sandbox.admin@example.test'
    [System.IO.File]::WriteAllLines($environmentFile, $settings, [System.Text.UTF8Encoding]::new($false))
}
$linuxDirectory = (& wsl -d $Distribution --exec wslpath -a $sandboxDirectory).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve sandbox directory in WSL.' }
$localSettings = @{}
foreach ($settingLine in (Get-Content -LiteralPath $environmentFile)) {
    $parts = $settingLine.Split('=', 2)
    if ($parts.Count -eq 2) { $localSettings[$parts[0]] = $parts[1] }
}
if (-not $localSettings.ContainsKey('SANDBOX_APP_ENCRYPTION_KEY')) {
    $localSettings['SANDBOX_APP_ENCRYPTION_KEY'] = [Convert]::ToHexString([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32)).ToLowerInvariant()
    [System.IO.File]::AppendAllText($environmentFile, 'SANDBOX_APP_ENCRYPTION_KEY=' + $localSettings['SANDBOX_APP_ENCRYPTION_KEY'] + [Environment]::NewLine)
}
$signingKeyPath = Join-Path $sandboxDirectory 'signing-key.pem'
$rsa = [System.Security.Cryptography.RSA]::Create(2048)
try {
    if (Test-Path -LiteralPath $signingKeyPath) {
        $rsa.ImportFromPem([System.IO.File]::ReadAllText($signingKeyPath))
    } else {
        [System.IO.File]::WriteAllText($signingKeyPath, $rsa.ExportRSAPrivateKeyPem(), [System.Text.UTF8Encoding]::new($false))
    }
    $public = $rsa.ExportParameters($false)
    $exponent = [Convert]::ToBase64String($public.Exponent).TrimEnd('=').Replace('+', '-').Replace('/', '_')
    $modulus = [Convert]::ToBase64String($public.Modulus).TrimEnd('=').Replace('+', '-').Replace('/', '_')
    $canonicalKey = '{"e":"' + $exponent + '","kty":"RSA","n":"' + $modulus + '"}'
    $thumbprint = [System.Security.Cryptography.SHA256]::HashData([System.Text.Encoding]::UTF8.GetBytes($canonicalKey))
    $keyId = [Convert]::ToBase64String($thumbprint).TrimEnd('=').Replace('+', '-').Replace('/', '_')
    $appSettings = @{ SaleorApp = @{
        Enabled = $true; AllowLocalSandboxHttp = $true; StateDirectory = '/app/saleor-state'
        Stores = @(@{
            Id = 'sandbox'; Enabled = $true; TenantId = 'saleor-sandbox'; ApiUrl = 'http://localhost:18080/graphql/'
            TransportUrl = 'http://api:8000/graphql/'; PublicBaseUrl = 'http://localhost:18081'
            CallbackBaseUrl = 'http://gtt.sandbox.test:8080'; DashboardOrigin = 'http://localhost:19090'
            KeyId = $keyId; PublicKeyPem = $rsa.ExportSubjectPublicKeyInfoPem()
        })
    } }
    [System.IO.File]::WriteAllText((Join-Path $sandboxDirectory 'app-settings.json'), ($appSettings | ConvertTo-Json -Depth 8), [System.Text.UTF8Encoding]::new($false))
} finally { $rsa.Dispose() }
& wsl -d $Distribution -u root --exec systemctl start docker
if ($LASTEXITCODE -ne 0) { throw 'Cannot start the Docker service in WSL.' }
& wsl -d $Distribution -u root --exec docker compose --project-directory $linuxDirectory -f "$linuxDirectory/compose.yaml" up -d db cache
if ($LASTEXITCODE -ne 0) { throw 'Saleor sandbox startup failed.' }
& wsl -d $Distribution -u root --exec docker compose --project-directory $linuxDirectory -f "$linuxDirectory/compose.yaml" run --rm --no-deps api python manage.py migrate --noinput --verbosity 0
if ($LASTEXITCODE -ne 0) { throw 'Saleor migrations failed.' }
& wsl -d $Distribution -u root --exec docker compose --project-directory $linuxDirectory -f "$linuxDirectory/compose.yaml" up -d --build api dashboard gtt worker
if ($LASTEXITCODE -ne 0) { throw 'Saleor API/Dashboard startup failed.' }
$localSettings = @{}
foreach ($settingLine in (Get-Content -LiteralPath $environmentFile)) {
    $parts = $settingLine.Split('=', 2)
    if ($parts.Count -eq 2) { $localSettings[$parts[0]] = $parts[1] }
}
$initialization = @{
    script = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'initialize_saleor_sandbox.py') -Raw
    credentials = @{ email = $localSettings['SANDBOX_ADMIN_EMAIL']; password = $localSettings['SANDBOX_ADMIN_PASSWORD'] }
} | ConvertTo-Json -Depth 5 -Compress
$initialization | & wsl -d $Distribution -u root --exec docker compose --project-directory $linuxDirectory -f "$linuxDirectory/compose.yaml" exec -T api python manage.py shell -c "import json,sys; data=json.load(sys.stdin); exec(data['script'], {'credentials': data['credentials']})"
if ($LASTEXITCODE -ne 0) { throw 'Sandbox administrator initialization failed.' }
Write-Output 'Saleor API: http://localhost:18080/graphql/'
Write-Output 'Saleor Dashboard: http://localhost:19090/'
Write-Output 'Development sandbox only. Local credentials are in the ignored dev/saleor/.env file.'
