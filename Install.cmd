@echo off
rem Double-click launcher: runs Install.ps1 for the current user and keeps the window
rem open so the result can be read. Optional argument: Lively, Classic or Static.
setlocal
if "%~1"=="" (
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install.ps1"
) else (
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install.ps1" -Style %1
)
echo.
pause
