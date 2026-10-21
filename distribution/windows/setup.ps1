param([switch]$TextOnly, [string]$PythonExe = '')
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
function Check-Exit([string]$Step) { if ($LASTEXITCODE -ne 0) { throw "$Step failed. Please read the error above and retry." } }
function Set-LocalSetting([string]$Name, [string]$Value) {
    $path = Join-Path $PSScriptRoot '.env'
    $lines = @(Get-Content -LiteralPath $path | Where-Object { $_ -notmatch ('^' + $Name + '=') })
    [IO.File]::WriteAllLines($path, ($lines + ($Name + '="' + $Value.Replace('\', '/') + '"')), (New-Object Text.UTF8Encoding($false)))
}
if (-not (Get-Command node.exe -ErrorAction SilentlyContinue)) { throw 'Install Node.js 22 or newer from https://nodejs.org/ and reopen this script.' }
$nodeVersion = & node.exe -p 'process.versions.node'
Check-Exit 'Checking Node.js'
if ([int]($nodeVersion.Split('.')[0]) -lt 22) { throw 'Node.js 22 or newer is required.' }
& npm.cmd ci --omit=dev --ignore-scripts
Check-Exit 'Installing the server'
if (-not (Test-Path -LiteralPath '.env')) { Copy-Item -LiteralPath '.env.example' -Destination '.env' }
if (-not (Select-String -LiteralPath '.env' -Pattern '^LLM_API_KEY=.+$' -Quiet)) {
    Write-Host 'Enter your own DeepSeek API key. Leave blank for the offline text example.'
    $secret = Read-Host 'API key (hidden)' -AsSecureString
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secret)
    try {
        $plain = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
        if ($plain) {
            if ($plain -notmatch '^[A-Za-z0-9_-]{16,256}$') { throw 'Invalid API key format.' }
            Set-LocalSetting 'LLM_API_KEY' $plain
        }
    } finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer); $plain = $null }
}
if ($TextOnly) { Write-Host 'Text setup complete. Run start-local.cmd.'; exit 0 }
if (-not $PythonExe) {
    $pythonCommand = Get-Command python.exe -ErrorAction SilentlyContinue
    if ($pythonCommand) { $PythonExe = $pythonCommand.Source }
}
if (-not $PythonExe) { throw 'For video support install Python 3.11 or 3.12 (add to PATH), then retry; text setup is already saved.' }
& $PythonExe -c "import sys; assert (3,11) <= sys.version_info[:2] <= (3,12), 'Use Python 3.11 or 3.12'"
Check-Exit 'Checking Python 3.11/3.12'
& $PythonExe -m venv .venv
Check-Exit 'Creating the local Python environment'
$mediaPython = Join-Path $PSScriptRoot '.venv\Scripts\python.exe'
& $mediaPython -m pip install -r scripts/requirements-media.txt
Check-Exit 'Installing video dependencies'
Write-Host 'Downloading the multilingual speech model (first setup only). Keep this window open.'
& $mediaPython -c "from huggingface_hub import snapshot_download; snapshot_download('Systran/faster-whisper-base', local_dir='models/whisper-base', allow_patterns=['config.json','model.bin','tokenizer.json','vocabulary.txt'])"
Check-Exit 'Downloading the speech model'
Set-LocalSetting 'MEDIA_PYTHON' $mediaPython
Set-LocalSetting 'MEDIA_PYTHONPATH' ''
Set-LocalSetting 'WHISPER_MODEL_PATH' (Join-Path $PSScriptRoot 'models\whisper-base')
Write-Host 'Setup complete. Run start-local.cmd. WeChat connection is an optional separate step; see START-HERE.md.'
