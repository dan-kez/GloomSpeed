@echo off
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0windows.ps1" -Action uninstall
echo.
pause
