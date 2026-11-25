param([string]$PythonPath = "python", [ValidateSet('base','small')][string]$Model = 'small')
$ErrorActionPreference = "Stop"
$serviceRoot = $PSScriptRoot
$python = Join-Path $serviceRoot "Runtime~\Scripts\python.exe"
if (!(Test-Path -LiteralPath $python)) {
    & $PythonPath -m venv (Join-Path $serviceRoot "Runtime~")
    if ($LASTEXITCODE -ne 0) { throw "Could not create Python environment. Install Python 3.10+ or pass -PythonPath." }
}
& $python -m pip install -r (Join-Path $serviceRoot "requirements.txt")
if ($LASTEXITCODE -ne 0) { throw "Speech dependency installation failed." }
$env:HF_HUB_DISABLE_XET = "1"
$env:CITYDOG_WHISPER_MODEL = $Model
& $python (Join-Path $serviceRoot "server.py") --download-only
if ($LASTEXITCODE -ne 0) { throw "Whisper model download failed." }
Write-Host "Local speech is ready. Small is selected automatically when installed; set CITYDOG_WHISPER_MODEL=base for a faster CPU option."
