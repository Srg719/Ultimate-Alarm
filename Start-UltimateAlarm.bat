@echo off
setlocal
set ROOT=%~dp0
if exist "%ROOT%UltimateAlarm.exe" (
    start "" "%ROOT%UltimateAlarm.exe"
    exit /b 0
)
if exist "%ROOT%bin\UltimateAlarm.exe" (
    start "" "%ROOT%bin\UltimateAlarm.exe"
    exit /b 0
)
powershell -NoProfile -ExecutionPolicy Bypass -File "%ROOT%install.ps1"
