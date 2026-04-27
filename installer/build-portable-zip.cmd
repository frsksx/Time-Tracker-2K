@echo off
setlocal

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-portable-zip.ps1"
exit /b %ERRORLEVEL%
