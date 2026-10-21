param([switch]$NoUI)
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
if (-not (Test-Path -LiteralPath 'node_modules\htmlparser2')) { throw 'Run setup.cmd first.' }
$node = (Get-Command node.exe -ErrorAction Stop).Source
$serverPath = Join-Path $PSScriptRoot 'server.mjs'
$statePath = Join-Path $PSScriptRoot 'runtime\server.json'
$base = 'http://127.0.0.1:4173'
$ready = $false
if (Test-Path -LiteralPath $statePath) {
    $state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
    $running = Get-CimInstance Win32_Process -Filter "ProcessId=$([int]$state.processId)"
    if ($running -and $running.Name -eq 'node.exe' -and $running.CommandLine.Contains($serverPath)) {
        try { $status = Invoke-RestMethod "$base/api/config" -TimeoutSec 3; $ready = $status.mode -in @('live','demo') } catch {}
    }
}
if (-not $ready) {
    if (Get-NetTCPConnection -LocalPort 4173 -State Listen -ErrorAction SilentlyContinue) {
        throw 'Port 4173 is already used. Close the other local service first; this launcher will not replace it.'
    }
    New-Item -ItemType Directory -Path 'runtime' -Force | Out-Null
    $env:PORT = '4173'; $env:BIND_HOST = '127.0.0.1'; $env:PUBLIC_ORIGIN = ''
    $arguments = @('--env-file-if-exists=.env', ('"' + $serverPath + '"'))
    $process = Start-Process -FilePath $node -ArgumentList $arguments -WorkingDirectory $PSScriptRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput 'runtime\server.log' -RedirectStandardError 'runtime\server-error.log'
    @{ processId = $process.Id } | ConvertTo-Json | Set-Content -LiteralPath $statePath
    for ($attempt = 0; $attempt -lt 20; $attempt++) {
        Start-Sleep -Milliseconds 500
        try { $status = Invoke-RestMethod "$base/api/config" -TimeoutSec 2; $ready = $status.mode -in @('live','demo'); if ($ready) { break } } catch {}
        $process.Refresh(); if ($process.HasExited) { break }
    }
    if (-not $ready) { throw 'Server startup failed. See runtime/server-error.log. No assistant was opened.' }
}
Write-Host ('Local server ready. AI=' + $status.liveAvailable + '; video=' + $status.videoAvailable + '; WeChat configured=' + $status.channelsAvailable)
if (-not $NoUI) { Start-Process -FilePath (Join-Path $PSScriptRoot 'zhiyi-windows.exe') -WorkingDirectory $PSScriptRoot -WindowStyle Hidden }
