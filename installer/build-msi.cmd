@echo off
setlocal

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-msi.ps1"
exit /b %ERRORLEVEL%
