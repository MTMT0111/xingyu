$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
$stage = Join-Path $project ('work\distribution-' + (Get-Date -Format 'yyyy-MM-dd-HHmmss'))
if (Test-Path -LiteralPath $stage) { throw 'Staging directory exists; use a new dated staging directory to avoid overwriting a reviewed package.' }
$app = Join-Path $stage 'zhiyi-local'
New-Item -ItemType Directory -Path $app, (Join-Path $app 'scripts') -Force | Out-Null
foreach ($name in @('package.json','package-lock.json','server.mjs','.env.example','ASSETS.md')) { Copy-Item -LiteralPath (Join-Path $project $name) -Destination $app }
foreach ($name in @('lib','public')) { Copy-Item -LiteralPath (Join-Path $project $name) -Destination $app -Recurse }
foreach ($name in @('process-media.py','requirements-media.txt')) { Copy-Item -LiteralPath (Join-Path $project ('scripts\' + $name)) -Destination (Join-Path $app 'scripts') }
Copy-Item -LiteralPath (Join-Path $project 'downloads\zhiyi-windows.exe') -Destination $app
Get-ChildItem -LiteralPath (Join-Path $project 'distribution\windows') -File | Copy-Item -Destination $app
foreach ($entry in @('setup','start-local','stop-local','enable-wechat')) {
    $cmd = "@echo off`r`npowershell.exe -NoProfile -ExecutionPolicy Bypass -File `"%~dp0$entry.ps1`"`r`nif errorlevel 1 pause`r`n"
    [IO.File]::WriteAllText((Join-Path $app ($entry + '.cmd')), $cmd, [Text.Encoding]::ASCII)
}
$connector = Join-Path $stage 'optional\connector'
New-Item -ItemType Directory -Path $connector -Force | Out-Null
$binary = Join-Path $project 'work\wx-channel\wx_channel-local.exe'
if ((Get-FileHash -LiteralPath $binary).Hash -ne '9A1CB40330D12CD7A4059702A991CDBFE6B26AE92EF576831F6E8ADBDBB07730') { throw 'Unexpected connector binary.' }
Copy-Item -LiteralPath $binary -Destination $connector
Get-ChildItem -LiteralPath (Join-Path $project 'integrations\wx-channel') -File | Copy-Item -Destination $connector
Compress-Archive -LiteralPath $app -DestinationPath (Join-Path $stage 'zhiyi-windows-local.zip') -CompressionLevel Optimal
Compress-Archive -LiteralPath $connector -DestinationPath (Join-Path $stage 'zhiyi-wechat-connector.zip') -CompressionLevel Optimal
$sums = Get-ChildItem -LiteralPath $stage -Filter '*.zip' | ForEach-Object { (Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant() + '  ' + $_.Name }
[IO.File]::WriteAllLines((Join-Path $stage 'SHA256SUMS.txt'), $sums, [Text.Encoding]::ASCII)
Get-ChildItem -LiteralPath $stage -File | Select-Object Name,Length
