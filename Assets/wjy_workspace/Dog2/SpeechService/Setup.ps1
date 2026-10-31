param([string]$PythonPath = "python")
$ErrorActionPreference = "Stop"
$serviceRoot = $PSScriptRoot
$python = Join-Path $serviceRoot "Runtime~\Scripts\python.exe"
if (!(Test-Path -LiteralPath $python)) {
    & $PythonPath -m venv (Join-Path $serviceRoot "Runtime~")
    if ($LASTEXITCODE -ne 0) { throw "Could not create Python environment. Install Python 3.10+ or pass -PythonPath." }
}
& $python -m pip install -r (Join-Path $serviceRoot "requirements.txt")
if ($LASTEXITCODE -ne 0) { throw "Speech dependency installation failed." }
& $python (Join-Path $serviceRoot "server.py") --download-only
if ($LASTEXITCODE -ne 0) { throw "Whisper model download failed." }
Write-Host "Local speech is ready. Unity will start it when recording begins."