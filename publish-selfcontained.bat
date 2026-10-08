@echo off
rem ============================================================
rem  Network Monitor - publish self-contained (no .NET needed)
rem  Usage: publish-selfcontained.bat [x64|x86]   (default: x64)
rem  Output: publish\self-contained-x64\  or  publish\self-contained-x86\
rem  Runs on any Windows 10 (19041+) / 11 without installing .NET
rem ============================================================
setlocal
cd /d "%~dp0"

set ARCH=%1
if "%ARCH%"=="" set ARCH=x64
if /i not "%ARCH%"=="x64" if /i not "%ARCH%"=="x86" (
  echo ERROR: unknown arch "%ARCH%" - use x64 or x86
  exit /b 1
)

echo Publishing NetworkMonitor (self-contained win-%ARCH%, no runtime needed)...
dotnet publish "src\NetworkMonitor\NetworkMonitor.csproj" -c Release -r win-%ARCH% --self-contained true -o "publish\self-contained-%ARCH%" --nologo
if errorlevel 1 (
  echo.
  echo PUBLISH FAILED
  exit /b 1
)

echo.
echo PUBLISH OK
echo Output: publish\self-contained-%ARCH%\NetworkMonitor.exe
endlocal
