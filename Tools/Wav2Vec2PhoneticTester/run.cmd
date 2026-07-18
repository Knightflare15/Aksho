@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
set "REPO_ROOT=%SCRIPT_DIR%..\.."
set "BUNDLED_PYTHON=%USERPROFILE%\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe"

if defined PYTHON (
    if exist "%PYTHON%" (
        set "PYTHON_EXE=%PYTHON%"
    )
)

if not defined PYTHON_EXE (
    if exist "%BUNDLED_PYTHON%" (
        set "PYTHON_EXE=%BUNDLED_PYTHON%"
    ) else (
        set "PYTHON_EXE=python"
    )
)

pushd "%REPO_ROOT%"
"%PYTHON_EXE%" "%SCRIPT_DIR%phonetic_tester.py" %*
set "EXIT_CODE=%ERRORLEVEL%"
popd
exit /b %EXIT_CODE%
