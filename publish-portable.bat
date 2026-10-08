@echo off
rem ============================================================
rem  Network Monitor - publish portable (framework-dependent)
rem  Usage: publish-portable.bat [x64|x86]     (default: x64)
rem  Output: publish\portable-x64\  or  publish\portable-x86\
rem  Requires .NET 10 Desktop Runtime on the target machine
rem ============================================================
setlocal
cd /d "%~dp0"

set ARCH=%1
if "%ARCH%"=="" set ARCH=x64
if /i not "%ARCH%"=="x64" if /i not "%ARCH%"=="x86" (
  echo ERROR: unknown arch "%ARCH%" - use x64 or x86
  exit /b 1
)

echo Publishing NetworkMonitor (portable win-%ARCH%, requires .NET 10 Desktop Runtime)...
dotnet publish "src\NetworkMonitor\NetworkMonitor.csproj" -c Release -r win-%ARCH% --self-contained false -o "publish\portable-%ARCH%" --nologo
if errorlevel 1 (
  echo.
  echo PUBLISH FAILED
  exit /b 1
)

echo.
echo PUBLISH OK
echo Output: publish\portable-%ARCH%\NetworkMonitor.exe
endlocal
