@echo off
setlocal
cd /d "%~dp0.."
dotnet publish "src\KksDllStringExtractor\KksDllStringExtractor.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false -o "dist\win-x64"
if errorlevel 1 (
  echo Publish failed.
  pause
  exit /b 1
)
echo EXE: dist\win-x64\KKS_DLL_String_Extractor.exe
echo Keep LICENSE, THIRD_PARTY_NOTICES.md and the licenses folder with the EXE.
pause
