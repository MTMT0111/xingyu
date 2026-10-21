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
$offline = Join-Path $app 'offline-demo'
New-Item -ItemType Directory -Path $offline -Force | Out-Null
foreach ($name in @('index.html','showcase.html','showcase.css','showcase.js','text-demo.html','styles.css','demo.js','history.js','app.js')) {
    Copy-Item -LiteralPath (Join-Path $project ('site\' + $name)) -Destination $offline
}
if ((Get-Content -LiteralPath (Join-Path $offline 'app.js') -Raw) -notmatch 'const staticDemo = true;') { throw 'Offline demo must not call the live backend.' }
$demo = Join-Path $stage 'zhiyi-offline-demo'
New-Item -ItemType Directory -Path $demo -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $app 'START-DEMO.html') -Destination $demo
Copy-Item -LiteralPath $offline -Destination $demo -Recurse
foreach ($entry in @('setup','start-local','stop-local','enable-wechat')) {
    $cmd = "@echo off`r`npowershell.exe -NoProfile -ExecutionPolicy Bypass -File `"%~dp0$entry.ps1`"`r`nif errorlevel 1 pause`r`n"
    [IO.File]::WriteAllText((Join-Path $app ($entry + '.cmd')), $cmd, [Text.Encoding]::ASCII)
}
$connector = Join-Path $stage 'optional\connector'
New-Item -ItemType Directory -Path $connector -Force | Out-Null
$binary = Join-Path $project 'work\wx-channel\wx_channel-local.exe'
if ((Get-FileHash -LiteralPath $binary).Hash -ne '0EA35164B9149DAB145FDAD8E58EA8D3B1A98692E9CB766CC512909575E7F3CB') { throw 'Unexpected connector binary.' }
Copy-Item -LiteralPath $binary -Destination $connector
Get-ChildItem -LiteralPath (Join-Path $project 'integrations\wx-channel') -File | Copy-Item -Destination $connector
Compress-Archive -LiteralPath $app -DestinationPath (Join-Path $stage 'zhiyi-windows-local.zip') -CompressionLevel Optimal
Compress-Archive -LiteralPath $demo -DestinationPath (Join-Path $stage 'zhiyi-offline-demo.zip') -CompressionLevel Optimal
Add-Type -AssemblyName System.IO.Compression
$zipFile = [IO.File]::Create((Join-Path $stage 'zhiyi-wechat-connector.zip'))
$zip = New-Object IO.Compression.ZipArchive($zipFile, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in (Get-ChildItem -LiteralPath $connector -File)) {
        # Antivirus can hold the freshly copied EXE open; permit its shared read access.
        $inputStream = [IO.File]::Open($file.FullName, [IO.FileMode]::Open, [IO.FileAccess]::Read, ([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
        try {
            $entry = $zip.CreateEntry(('connector/' + $file.Name), [IO.Compression.CompressionLevel]::Optimal).Open()
            try { $inputStream.CopyTo($entry) } finally { $entry.Dispose() }
        } finally { $inputStream.Dispose() }
    }
} finally { $zip.Dispose(); $zipFile.Dispose() }
$sums = Get-ChildItem -LiteralPath $stage -Filter '*.zip' | ForEach-Object { (Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant() + '  ' + $_.Name }
[IO.File]::WriteAllLines((Join-Path $stage 'SHA256SUMS.txt'), $sums, [Text.Encoding]::ASCII)
Get-ChildItem -LiteralPath $stage -File | Select-Object Name,Length
