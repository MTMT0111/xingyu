$ErrorActionPreference = 'Stop'
$statePath = Join-Path $PSScriptRoot 'runtime\server.json'
if (Test-Path -LiteralPath $statePath) {
    $state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
    $running = Get-CimInstance Win32_Process -Filter "ProcessId=$([int]$state.processId)"
    $serverPath = Join-Path $PSScriptRoot 'server.mjs'
    if ($running -and $running.Name -eq 'node.exe' -and $running.CommandLine.Contains($serverPath)) { Stop-Process -Id $running.ProcessId }
    Remove-Item -LiteralPath $statePath
}
Write-Host 'This package server has stopped. Exit the floating assistant from its right-click menu.'
