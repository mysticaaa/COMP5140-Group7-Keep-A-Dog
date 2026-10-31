param([int]$Port = 8765)
$python = Join-Path $PSScriptRoot "Runtime~\Scripts\python.exe"
if (!(Test-Path -LiteralPath $python)) { throw "Run Setup.ps1 first." }
& $python (Join-Path $PSScriptRoot "server.py") --port $Port