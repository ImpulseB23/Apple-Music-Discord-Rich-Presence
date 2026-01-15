[Setup]
AppName=Apple Music Discord RPC
AppVersion=1.0.0
AppPublisher=Impulse
AppPublisherURL=https://github.com/impulseb23/Apple-Music-Discord-Rich-Presence
DefaultDirName={autopf}\AppleMusicRPC
DefaultGroupName=Apple Music Discord RPC
OutputDir=.
OutputBaseFilename=AppleMusicRPC-Setup
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\AppleMusicRpc.exe

[Files]
Source: "release\*"; DestDir: "{app}"; Flags: recursesubdirs

[Icons]
Name: "{group}\Apple Music Discord RPC"; Filename: "{app}\AppleMusicRpc.exe"
Name: "{autodesktop}\Apple Music Discord RPC"; Filename: "{app}\AppleMusicRpc.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create desktop shortcut"; GroupDescription: "Additional icons:"

[Run]
Filename: "{app}\AppleMusicRpc.exe"; Description: "Launch Apple Music Discord RPC"; Flags: postinstall nowait skipifsilent
