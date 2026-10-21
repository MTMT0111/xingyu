param([ValidatePattern('^[A-Za-z0-9_-]+\.exe$')][string]$OutputName = 'zhiyi-windows.exe')
$ErrorActionPreference = 'Stop'
$projectRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$compiler = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '没有找到 Windows .NET Framework C# 编译器。' }
$outputDirectory = Join-Path $projectRoot 'downloads'
$output = Join-Path $outputDirectory $OutputName
$automationClient = (Get-ChildItem 'C:\Windows\Microsoft.NET\assembly\GAC_MSIL\UIAutomationClient' -Recurse -Filter 'UIAutomationClient.dll' | Select-Object -First 1 -ExpandProperty FullName)
$automationTypes = (Get-ChildItem 'C:\Windows\Microsoft.NET\assembly\GAC_MSIL\UIAutomationTypes' -Recurse -Filter 'UIAutomationTypes.dll' | Select-Object -First 1 -ExpandProperty FullName)
$windowsBase = (Get-ChildItem 'C:\Windows\Microsoft.NET\assembly\GAC_MSIL\WindowsBase' -Recurse -Filter 'WindowsBase.dll' | Select-Object -First 1 -ExpandProperty FullName)
if (-not $automationClient -or -not $automationTypes -or -not $windowsBase) { throw '没有找到 Windows UI Automation 运行库。' }
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$sources = Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | Select-Object -ExpandProperty FullName
$arguments = @('/nologo', '/target:winexe', '/optimize+', '/platform:anycpu',
  ('/win32manifest:' + (Join-Path $PSScriptRoot 'app.manifest')), ('/out:' + $output),
  '/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Drawing.dll', '/reference:System.Windows.Forms.dll',
  '/reference:System.Net.Http.dll', '/reference:System.Web.Extensions.dll', '/reference:System.Runtime.WindowsRuntime.dll', ('/reference:' + $windowsBase), ('/reference:' + $automationClient), ('/reference:' + $automationTypes)) + @('Windows.Foundation', 'Windows.Globalization', 'Windows.Graphics', 'Windows.Media', 'Windows.Storage') | ForEach-Object { if ($_ -like 'Windows.*') { '/reference:C:\Windows\System32\WinMetadata\' + $_ + '.winmd' } else { $_ } }
$arguments += $sources
$arguments += '/reference:C:\Windows\Microsoft.NET\assembly\GAC_MSIL\System.Runtime\v4.0_4.0.0.0__b03f5f7f11d50a3a\System.Runtime.dll'
& $compiler $arguments
if ($LASTEXITCODE -ne 0) { throw 'Windows 客户端编译失败。' }
$selfTest = Start-Process -FilePath $output -ArgumentList '--self-test' -Wait -PassThru -NoNewWindow
if ($selfTest.ExitCode -ne 0) { throw 'Windows 客户端自检失败。' }
$preview = Join-Path $projectRoot 'screenshots\windows-companion.png'
$previewTest = Start-Process -FilePath $output -ArgumentList @('--preview', $preview) -Wait -PassThru
if ($previewTest.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $preview)) { throw 'Windows 客户端预览生成失败。' }
$hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $output).Hash.ToLowerInvariant()
[pscustomobject]@{ output = $output; bytes = (Get-Item -LiteralPath $output).Length; sha256 = $hash; preview = $preview }
