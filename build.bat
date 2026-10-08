@echo off
rem ============================================================
rem  Network Monitor - build (Release)
rem  Requires: .NET SDK 10.0 or newer
rem ============================================================
setlocal
cd /d "%~dp0"

echo Building NetworkMonitor (Release)...
dotnet build "src\NetworkMonitor\NetworkMonitor.csproj" -c Release --nologo
if errorlevel 1 (
  echo.
  echo BUILD FAILED
  exit /b 1
)

echo.
echo BUILD OK
echo Output: src\NetworkMonitor\bin\Release\net10.0-windows10.0.19041\NetworkMonitor.exe
endlocal
