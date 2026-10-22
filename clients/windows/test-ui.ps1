param([string]$ClientPath = (Join-Path $PSScriptRoot '..\..\downloads\zhiyi-windows.exe'))
$ErrorActionPreference = 'Stop'
$project = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$output = Join-Path $project 'work\windows-ui-regression.exe'
New-Item -ItemType Directory -Path (Split-Path -Parent $output) -Force | Out-Null
$compiler = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:exe /reference:System.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll ('/out:' + $output) (Join-Path $PSScriptRoot 'tests\UiRegression.cs')
if ($LASTEXITCODE -ne 0) { throw 'UI regression harness compilation failed.' }
& $output (Resolve-Path -LiteralPath $ClientPath).Path | Tee-Object -FilePath (Join-Path $project 'work\windows-ui-regression.log')
if ($LASTEXITCODE -ne 0) { throw 'Windows UI regression failed.' }
