@echo off
cd /d "%~dp0"

set "ISCC=%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe"
if not exist "%ISCC%" set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if not exist "%ISCC%" set "ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe"
if not exist "%ISCC%" (
  echo Inno Setup wurde nicht gefunden.
  pause
  exit /b 1
)

echo Veröffentliche Easy Tagger ...
dotnet publish "src\EasyTagger.App\EasyTagger.App.csproj" -c Release -r win-x64 --self-contained true -o "publish\win-x64" -p:DebugType=None -p:DebugSymbols=false
if errorlevel 1 (
  echo Publish fehlgeschlagen.
  pause
  exit /b 1
)

echo Baue Setup ...
"%ISCC%" "installer\EasyTagger.iss"
if errorlevel 1 (
  echo Setup-Build fehlgeschlagen.
  pause
  exit /b 1
)

echo.
echo Fertig: %~dp0dist\EasyTagger-Setup.exe
pause
