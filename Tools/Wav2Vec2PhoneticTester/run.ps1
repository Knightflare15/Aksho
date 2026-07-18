$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptDir "..\..")
$bundledPython = Join-Path $env:USERPROFILE ".cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe"

if ($env:PYTHON -and (Test-Path $env:PYTHON)) {
    $python = $env:PYTHON
} elseif (Test-Path $bundledPython) {
    $python = $bundledPython
} else {
    $python = "python"
}

Push-Location $repoRoot
try {
    & $python (Join-Path $scriptDir "phonetic_tester.py") @args
} finally {
    Pop-Location
}
