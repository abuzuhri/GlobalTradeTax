param(
    [string]$Distribution = 'Ubuntu'
)

$ErrorActionPreference = 'Stop'
$sandboxDirectory = Join-Path (Split-Path -Parent $PSScriptRoot) 'dev\saleor'
$linuxDirectory = (& wsl -d $Distribution --exec wslpath -a $sandboxDirectory).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve sandbox directory in WSL.' }
& wsl -d $Distribution -u root --exec docker compose --project-directory $linuxDirectory -f "$linuxDirectory/compose.yaml" stop
if ($LASTEXITCODE -ne 0) { throw 'Cannot stop the sandbox containers.' }
$keepaliveFile = Join-Path $sandboxDirectory '.keepalive.json'
if (Test-Path -LiteralPath $keepaliveFile) {
    $savedProcess = Get-Content -LiteralPath $keepaliveFile -Raw | ConvertFrom-Json
    $existingProcess = Get-Process -Id $savedProcess.Id -ErrorAction SilentlyContinue
    $savedTicks = if ($savedProcess.StartTimeTicks) { [long]$savedProcess.StartTimeTicks } else { ([DateTimeOffset]$savedProcess.StartTime).UtcTicks }
    if ($null -ne $existingProcess -and $existingProcess.ProcessName -eq 'wsl' -and
        $existingProcess.StartTime.ToUniversalTime().Ticks -eq $savedTicks) {
        & taskkill.exe /PID $existingProcess.Id /T /F | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Cannot stop the sandbox keepalive process tree.' }
    }
}
Write-Output 'Sandbox stopped. Database and media volumes are retained.'
