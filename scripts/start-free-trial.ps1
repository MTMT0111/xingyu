#Requires -Version 5.1
param([switch]$Stop)
$ErrorActionPreference = 'Stop'
$trialProject = Split-Path $PSScriptRoot -Parent
$trialRoot = Join-Path $trialProject 'work/free-trial'
$trialStatePath = Join-Path $trialRoot 'access.json'
$trialExe = Join-Path $trialRoot 'cloudflared.exe'
$trialDigest = '86aee4017b26625cee8484c113558f48effa4cd47f7aa05fcf425604e5d2b23c'
New-Item -ItemType Directory -Path $trialRoot -Force | Out-Null

function Stop-TrialProcess($entry) {
    if (-not $entry) { return }
    $owned = Get-Process -Id $entry.id -ErrorAction SilentlyContinue
    if ($owned -and $owned.StartTime.ToUniversalTime().ToString('o') -eq $entry.startedAt) {
        Stop-Process -Id $owned.Id -ErrorAction SilentlyContinue
    }
}
if ($Stop) {
    if (Test-Path -LiteralPath $trialStatePath) {
        $saved = Get-Content -LiteralPath $trialStatePath -Raw | ConvertFrom-Json
        Stop-TrialProcess $saved.tunnel
        Stop-TrialProcess $saved.server
        Remove-Item -LiteralPath $trialStatePath
    }
    Write-Host '已停止此启动器记录的试用进程；本地 4173 页面不受影响。'
    return
}
if (Test-Path -LiteralPath $trialStatePath) { throw '已有试用记录，请先使用本脚本 -Stop，再重新启动。' }
if (Get-NetTCPConnection -LocalPort 4174 -State Listen -ErrorAction SilentlyContinue) { throw '4174 端口已占用，未启动任何公网隧道。' }
if (-not (Test-Path -LiteralPath (Join-Path $trialProject 'server.mjs'))) { throw '请将此脚本保存在言外仓库的 scripts 文件夹中。' }
$trialNode = (Get-Command node -ErrorAction Stop).Source
if (-not (Test-Path -LiteralPath $trialExe)) {
    Write-Host '下载 Cloudflare 官方客户端 2026.10.0…'
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -UseBasicParsing -Uri 'https://github.com/cloudflare/cloudflared/releases/download/2026.10.0/cloudflared-windows-amd64.exe' -OutFile $trialExe
}
if ((Get-FileHash -LiteralPath $trialExe -Algorithm SHA256).Hash.ToLowerInvariant() -ne $trialDigest) { throw 'Cloudflare 文件 SHA-256 不匹配，已停止。' }
if ((Get-AuthenticodeSignature -FilePath $trialExe).Status -ne 'Valid') { throw 'Cloudflare 文件数字签名验证未通过，已停止。' }

$trialTunnel = $null
$trialServer = $null
$trialPassword = $null
$trialOldEnvironment = @{}
foreach ($name in @('PORT','BIND_HOST','PUBLIC_ORIGIN','TRIAL_PASSWORD')) { $trialOldEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
function Read-TrialPage([string]$address, [hashtable]$headers) {
    $trialRequestDeadline = [DateTime]::UtcNow.AddSeconds(30)
    do {
        try {
            $response = Invoke-WebRequest -UseBasicParsing -Uri $address -Headers $headers -MaximumRedirection 0 -TimeoutSec 10
            return [int]$response.StatusCode
        } catch {
            if ($_.Exception.Response) { return [int]$_.Exception.Response.StatusCode }
            if ([DateTime]::UtcNow -ge $trialRequestDeadline) { break }
            Start-Sleep -Seconds 2
        }
    } while ([DateTime]::UtcNow -lt $trialRequestDeadline)
    throw '30 秒内无法连接试用地址，可能是当前网络不支持 Cloudflare Tunnel。'
}
try {
    $trialOut = Join-Path $trialRoot 'tunnel-out.log'
    $trialErr = Join-Path $trialRoot 'tunnel-err.log'
    $trialTunnel = Start-Process -FilePath $trialExe -ArgumentList @('tunnel','--no-autoupdate','--protocol','http2','--url','http://127.0.0.1:4174') -WorkingDirectory $trialRoot -WindowStyle Hidden -RedirectStandardOutput $trialOut -RedirectStandardError $trialErr -PassThru
    $trialOrigin = $null
    $trialDeadline = [DateTime]::UtcNow.AddSeconds(60)
    while ([DateTime]::UtcNow -lt $trialDeadline -and -not $trialTunnel.HasExited) {
        $trialLog = (Get-Content -LiteralPath $trialOut,$trialErr -Raw -ErrorAction SilentlyContinue) -join "`n"
        $trialMatch = [regex]::Match($trialLog, 'https://[a-z0-9]+(?:-[a-z0-9]+)*\.trycloudflare\.com(?=[\s|]|$)')
        if ($trialMatch.Success) { $trialOrigin = $trialMatch.Value; break }
        Start-Sleep -Milliseconds 500
    }
    if (-not $trialOrigin) { throw '60 秒内没有取得官方临时域名，请查看 work/free-trial/tunnel-err.log。' }
    $trialUri = [Uri]$trialOrigin
    if ($trialUri.Scheme -ne 'https' -or $trialUri.Port -ne 443 -or $trialUri.UserInfo -or $trialUri.AbsolutePath -ne '/' -or $trialUri.Query -or $trialUri.Fragment -or $trialUri.Host -notmatch '^[a-z0-9-]+\.trycloudflare\.com$') { throw '临时域名校验失败，已停止。' }
    $trialRandom = New-Object byte[] 24
    $trialRng = [Security.Cryptography.RandomNumberGenerator]::Create()
    $trialRng.GetBytes($trialRandom)
    $trialRng.Dispose()
    $trialPassword = [Convert]::ToBase64String($trialRandom).TrimEnd('=').Replace('+','-').Replace('/','_')
    $env:PORT = '4174'
    $env:BIND_HOST = '127.0.0.1'
    $env:PUBLIC_ORIGIN = $trialOrigin
    $env:TRIAL_PASSWORD = $trialPassword
    $trialServer = Start-Process -FilePath $trialNode -ArgumentList @('--env-file-if-exists=.env','server.mjs') -WorkingDirectory $trialProject -WindowStyle Hidden -RedirectStandardOutput (Join-Path $trialRoot 'server-out.log') -RedirectStandardError (Join-Path $trialRoot 'server-err.log') -PassThru
    $trialDeadline = [DateTime]::UtcNow.AddSeconds(20)
    do {
        Start-Sleep -Milliseconds 500
        $trialListener = Get-NetTCPConnection -LocalAddress 127.0.0.1 -LocalPort 4174 -State Listen -ErrorAction SilentlyContinue
    } while (-not $trialListener -and -not $trialServer.HasExited -and [DateTime]::UtcNow -lt $trialDeadline)
    if (-not $trialListener -or $trialListener.OwningProcess -ne $trialServer.Id) { throw '本机受保护的试用服务未启动成功。' }
    $trialAuthorization = 'Basic ' + [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes('trial:' + $trialPassword))
    $trialChecks = @()
    foreach ($case in @(@{path='/';auth=$false;expected=401}, @{path='/api/config';auth=$false;expected=401}, @{path='/';auth=$true;expected=200}, @{path='/api/config';auth=$true;expected=200}, @{path='/.env';auth=$true;expected=404}, @{path='/server.mjs';auth=$true;expected=404})) {
        $trialHeaders = @{}
        if ($case.auth) { $trialHeaders.Authorization = $trialAuthorization }
        $trialStatus = Read-TrialPage ($trialOrigin + $case.path) $trialHeaders
        $trialChecks += @{path=$case.path;authenticated=$case.auth;status=$trialStatus;expected=$case.expected}
        if ($trialStatus -ne $case.expected) { throw ('HTTPS 保护检查未通过：' + $case.path + ' HTTP ' + $trialStatus) }
    }
    $trialState = @{url=$trialOrigin;username='trial';password=$trialPassword;createdAt=[DateTime]::UtcNow.ToString('o');tunnel=@{id=$trialTunnel.Id;startedAt=$trialTunnel.StartTime.ToUniversalTime().ToString('o')};server=@{id=$trialServer.Id;startedAt=$trialServer.StartTime.ToUniversalTime().ToString('o')}}
    $trialState | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $trialStatePath -Encoding UTF8
    $trialIdentity = [Security.Principal.WindowsIdentity]::GetCurrent().User
    $trialAcl = Get-Acl -LiteralPath $trialStatePath
    $trialAcl.SetAccessRuleProtection($true, $false)
    $trialAcl.SetAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($trialIdentity, 'FullControl', 'Allow')))
    Set-Acl -LiteralPath $trialStatePath -AclObject $trialAcl
    $trialChecks | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $trialRoot 'https-results.json') -Encoding UTF8
    Write-Host ('试用地址：' + $trialOrigin)
    Write-Host '用户名：trial'
    Write-Host ('口令保存在本机私有文件：' + $trialStatePath)
    Write-Host 'HTTPS 与访问保护检查通过。此结果不代表模型或微信已接通。'
    Write-Host '关闭电脑或停止隧道后失效；结束试用请运行本脚本 -Stop。'
} catch {
    if ($trialTunnel -and -not $trialTunnel.HasExited) { Stop-Process -Id $trialTunnel.Id -ErrorAction SilentlyContinue }
    if ($trialServer -and -not $trialServer.HasExited) { Stop-Process -Id $trialServer.Id -ErrorAction SilentlyContinue }
    if (Test-Path -LiteralPath $trialStatePath) { Remove-Item -LiteralPath $trialStatePath }
    throw
} finally {
    foreach ($name in $trialOldEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name, $trialOldEnvironment[$name], 'Process') }
    $trialPassword = $null
    $trialAuthorization = $null
}
