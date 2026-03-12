@echo off
set SCRIPT_DIR=%~dp0
powershell -ExecutionPolicy Bypass -File "%SCRIPT_DIR%RUN-WINDOWS-TESTS.ps1"
pause
