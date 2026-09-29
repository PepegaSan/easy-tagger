@echo off
cd /d "%~dp0"
dotnet build "src\EasyTagger.App\EasyTagger.App.csproj" -c Release
if errorlevel 1 (
  echo Build fehlgeschlagen.
  pause
  exit /b 1
)
start "" "%~dp0src\EasyTagger.App\bin\Release\net8.0-windows\EasyTagger.App.exe"
