@echo off
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install-ffmpeg.ps1"
if errorlevel 1 echo Installation fehlgeschlagen.
pause
