@echo off
rem ============================================================
rem  Network Monitor - build installers (x64 + x86)
rem  Requires: .NET SDK 10 + Inno Setup 6 (ISCC.exe)
rem  Usage: build-installer.bat [-SkipPublish] [-Arch x64^|x86^|both]
rem  Output: publish\installer\NetworkMonitor-1.0.0-x64-setup.exe
rem          publish\installer\NetworkMonitor-1.0.0-x86-setup.exe
rem ============================================================
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-installer.ps1" %*
exit /b %ERRORLEVEL%
