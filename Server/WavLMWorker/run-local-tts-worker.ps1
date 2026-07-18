param(
    [int]$Port = 8090
)

$ErrorActionPreference = "Stop"
$workerRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $workerRoot

$python = Join-Path $workerRoot ".venv\Scripts\python.exe"
if (-not (Test-Path $python)) {
    throw "Worker venv not found at $python. Run: python -m venv .venv; .\.venv\Scripts\python.exe -m pip install -r requirements.txt"
}

$env:LOCAL_TTS_ENABLED = "1"
$env:LOCAL_TTS_ENGINE = "silero"
$env:LOCAL_TTS_DEFAULT_LANGUAGE = "hi"
$env:ALLOW_UNAUTHENTICATED_LOCAL_DEV = "1"
$env:ENABLE_DIRECT_TTS_ENDPOINT = "1"

& $python -m uvicorn app.main:app --host 127.0.0.1 --port $Port
