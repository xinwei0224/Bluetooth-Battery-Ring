@echo off
chcp 65001 >nul
echo ==================================================
echo  Bluetooth Battery - install (Chrome extension + tray)
echo ==================================================
echo.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0setup.ps1"
echo.
echo One more time in Chrome (only first time):
echo   chrome://extensions  ->  enable Developer mode  ->  Load unpacked  ->  pick the "extension" folder
echo.
pause
