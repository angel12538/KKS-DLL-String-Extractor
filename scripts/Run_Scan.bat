@echo off
setlocal
chcp 65001 >nul
cd /d "%~dp0.."
where dotnet >nul 2>nul
if errorlevel 1 (
  echo Install the .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0
  pause
  exit /b 1
)
set /p "INPUT=Enter the KKS BepInEx\plugins folder path: "
set "INPUT=%INPUT:"=%"
if not exist "%INPUT%\" (
  echo Input folder not found.
  pause
  exit /b 1
)
rem Use a NEW folder on every scan to protect existing translations.
for /f %%I in ('powershell -NoProfile -Command "Get-Date -Format yyyyMMdd_HHmmss_fff"') do set "STAMP=%%I"
if not defined STAMP (
  echo Cannot determine output timestamp.
  pause
  exit /b 1
)
set "OUTPUT=%~dp0..\Results_TXT\scan_%STAMP%"
dotnet run --project "%~dp0..\src\KksDllStringExtractor\KksDllStringExtractor.csproj" -c Release -- "%INPUT%" "%OUTPUT%"
if errorlevel 1 (
  echo Scan failed. Please read the error above.
  pause
  exit /b 1
)
echo.
echo Finished: "%OUTPUT%"
pause
