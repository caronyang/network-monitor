; ============================================================
; Network Monitor - Inno Setup 6 installer script (TEMPLATE)
;
; DO NOT compile this file directly: it contains @-delimited placeholder
; tokens that are rendered per-architecture by build-installer.ps1, which
; writes the result to publish\installer\NetworkMonitor-x64.iss
; (or -x86.iss) and compiles that with ISCC.exe.
;
; Manual flow:
;   powershell -NoProfile -ExecutionPolicy Bypass -File build-installer.ps1
; ============================================================

[Setup]
AppId={{53AEA82F-91D3-4471-8D02-E223B3D5C883}
AppName=Network Monitor
AppVersion=@APP_VERSION@
AppVerName=Network Monitor @APP_VERSION@
AppPublisher=Caron Yang
DefaultDirName={autopf}\NetworkMonitor
DefaultGroupName=Network Monitor
DisableProgramGroupPage=yes
OutputDir=@OUTPUT_DIR@
OutputBaseFilename=NetworkMonitor-@APP_VERSION@-@APP_ARCH@-setup
SetupIconFile=@ICON_FILE@
UninstallDisplayIcon={app}\NetworkMonitor.exe
UninstallDisplayName=Network Monitor
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
; Intended: the optional autostart task writes a per-user HKCU Run value for the
; installing user - this is by design, so silence the admin+HKCU advisory.
UsedUserAreasWarning=no
VersionInfoVersion=@APP_VERSION@
VersionInfoProductTextVersion=@APP_VERSION@
; Detect a running instance (single-instance mutex of the app)
AppMutex=Local\NetworkMonitor_SingleInstance
ArchitecturesAllowed=@ARCH_ALLOWED@
@ARCH_64BIT_MODE@

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "chinesetraditional"; MessagesFile: "compiler:Languages\ChineseTrad.isl"

[CustomMessages]
english.startup=Launch automatically at Windows sign-in
english.startupgroup=Startup
chinesetraditional.startup=開機時自動啟動
chinesetraditional.startupgroup=開機啟動

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startup"; Description: "{cm:startup}"; GroupDescription: "{cm:startupgroup}"; Flags: unchecked

[Files]
Source: "@SOURCE_DIR@\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "@LICENSE_FILE@"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion

[Icons]
Name: "{group}\Network Monitor"; Filename: "{app}\NetworkMonitor.exe"; WorkingDir: "{app}"
Name: "{group}\Uninstall Network Monitor"; Filename: "{uninstallexe}"
Name: "{autodesktop}\Network Monitor"; Filename: "{app}\NetworkMonitor.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Registry]
; Same value the app's own "autostart" toggle manages (Autostart.cs):
;   HKCU\...\Run  "NetworkMonitor" = "<exe>" --minimized
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "NetworkMonitor"; ValueData: """{app}\NetworkMonitor.exe"" --minimized"; Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\NetworkMonitor.exe"; Description: "{cm:LaunchProgram,Network Monitor}"; Flags: nowait postinstall skipifsilent

; Note: uninstalling the app keeps the user's data
; (%LOCALAPPDATA%\NetworkMonitor - SQLite database and settings).
