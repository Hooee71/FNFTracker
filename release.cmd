@echo off
rem Tags and pushes a new release; GitHub builds and publishes FNFTracker.exe.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0release.ps1"
echo.
pause
