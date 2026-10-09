@echo off
setlocal
cd /d "%~dp0.."
dotnet run --project "src\KksDllStringExtractor\KksDllStringExtractor.csproj" -c Release -- --self-test
if errorlevel 1 (
  echo Self-test failed.
  pause
  exit /b 1
)
echo PASS
pause
