param([switch]$Elevated)
$ErrorActionPreference = 'Stop'
$connectorDir = Join-Path $PSScriptRoot 'connector'
$exe = Join-Path $connectorDir 'wx_channel-local.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw 'Download zhiyi-wechat-connector.zip and extract its connector folder next to this script.' }
if ((Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash -ne '0EA35164B9149DAB145FDAD8E58EA8D3B1A98692E9CB766CC512909575E7F3CB') { throw 'Connector checksum mismatch. Download the original release again.' }
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if ($Elevated -and -not $isAdmin) { throw 'Windows administrator approval was not granted.' }
if (-not $Elevated) {
    Write-Host 'Optional WeChat connection: the third-party wx_channel connector installs the SunnyRoot root certificate and proxies WeChat traffic on this PC.'
    Write-Host 'Only continue on your own PC. Keep WeChat logged in and open Channels home. This does not grant access to other people''s accounts.'
    if ((Read-Host 'Type ENABLE to accept the certificate/proxy changes; anything else cancels') -cne 'ENABLE') { exit 0 }
    if (-not (Test-Path -LiteralPath (Join-Path $PSScriptRoot '.env'))) { throw 'Run setup.cmd first.' }
    if (Get-Process -Name wx_channel-local -ErrorAction SilentlyContinue) { throw 'A connector is already running. Exit it before changing its configuration.' }
    $random = New-Object byte[] 32
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create(); $rng.GetBytes($random); $rng.Dispose()
    $token = -join ($random | ForEach-Object { $_.ToString('x2') })
    $yaml = @('port: 2025', 'cloud_enabled: false', 'radar_enabled: false', 'auto_open_channels: false', ('secret_token: "' + $token + '"'), ('web_console_token: "' + $token + '"'))
    [IO.File]::WriteAllLines((Join-Path $connectorDir 'config.yaml'), $yaml, (New-Object Text.UTF8Encoding($false)))
    $envPath = Join-Path $PSScriptRoot '.env'
    $lines = @(Get-Content -LiteralPath $envPath | Where-Object { $_ -notmatch '^CHANNELS_BRIDGE_(URL|TOKEN)=' })
    [IO.File]::WriteAllLines($envPath, ($lines + 'CHANNELS_BRIDGE_URL=http://127.0.0.1:2026' + ('CHANNELS_BRIDGE_TOKEN=' + $token)), (New-Object Text.UTF8Encoding($false)))
    $token = $null
    Start-Process -FilePath 'powershell.exe' -Verb RunAs -WindowStyle Hidden -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', ('"' + $PSCommandPath + '"'), '-Elevated')
    Write-Host 'Approve the Windows certificate prompt if it appears. Restart the local server after the connector is ready.'
    exit 0
}
$process = Start-Process -FilePath $exe -WorkingDirectory $connectorDir -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $connectorDir 'connector.log') -RedirectStandardError (Join-Path $connectorDir 'connector-error.log')
@{ processId = $process.Id } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $connectorDir 'process.json')
