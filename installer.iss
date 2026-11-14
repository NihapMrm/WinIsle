; WinIsle installer (Inno Setup 6). Build the app first, then:
;   build.cmd
;   iscc /DAppVersion=1.1.0 installer.iss
; Output: dist\WinIsle-Setup.exe. Installs per user, so no administrator rights are needed.

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

[Setup]
AppId={{6F1E0C52-4B7A-4E0B-9C1D-57A2E1F3B0C8}
AppName=WinIsle
AppVersion={#AppVersion}
AppPublisher=NihapMrm
AppPublisherURL=https://github.com/NihapMrm/WinIsle
AppSupportURL=https://github.com/NihapMrm/WinIsle/issues
DefaultDirName={localappdata}\Programs\WinIsle
DefaultGroupName=WinIsle
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=dist
OutputBaseFilename=WinIsle-Setup
SetupIconFile=logo.ico
UninstallDisplayIcon={app}\WinIsle.exe
LicenseFile=LICENSE
WizardStyle=modern
Compression=lzma2
SolidCompression=yes
MinVersion=10.0
CloseApplications=yes

[Tasks]
Name: "startup"; Description: "Start WinIsle with Windows"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; Flags: unchecked

[Files]
Source: "WinIsle.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\WinIsle"; Filename: "{app}\WinIsle.exe"
; Same shortcut the Control Center's "Start with Windows" switch manages.
Name: "{userstartup}\WinIsle"; Filename: "{app}\WinIsle.exe"; WorkingDir: "{app}"; Tasks: startup
Name: "{userdesktop}\WinIsle"; Filename: "{app}\WinIsle.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\WinIsle.exe"; Description: "{cm:LaunchProgram,WinIsle}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Close the app, then give Windows its notification banners back (the app turns them off while it runs).
Filename: "{cmd}"; Parameters: "/c taskkill /f /im WinIsle.exe >nul 2>&1 & ""{app}\WinIsle.exe"" --restore-banners"; Flags: runhidden waituntilterminated; RunOnceId: "RestoreBanners"

[UninstallDelete]
; Created by the in-app switch, so the installer doesn't know about it otherwise.
Type: files; Name: "{userstartup}\WinIsle.lnk"
; Your data in %LOCALAPPDATA%\WinIsle is kept.
