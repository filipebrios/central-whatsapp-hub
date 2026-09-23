#define MyAppName "MODUX"
#define MyAppVersion "1.5.1"
#define MyAppPublisher "MODUX"
#define MyAppExeName "MODUX.exe"

[Setup]
AppId={{B1A14D26-5498-48D7-BABD-9FB9B4AD739E}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\Central WhatsApp
DefaultGroupName=MODUX
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=installer-output
OutputBaseFilename=MODUX-Setup-{#MyAppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
RestartApplications=no
UninstallDisplayName=MODUX
SetupLogging=yes
SetupIconFile=assets\\modux.ico
UninstallDisplayIcon={app}\\{#MyAppExeName}

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "desktopicon"; Description: "Criar atalho na Área de Trabalho"; GroupDescription: "Atalhos:"; Flags: checkedonce
Name: "autostart"; Description: "Iniciar o MODUX com o Windows"; GroupDescription: "Inicialização:"; Flags: checkedonce

[InstallDelete]
Type: files; Name: "{app}\\CentralWhatsApp.WebView2.exe"
Type: files; Name: "{autodesktop}\\Central WhatsApp.lnk"
Type: files; Name: "{group}\\Central WhatsApp.lnk"

[Files]
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Central WhatsApp"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\Central WhatsApp"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\\Microsoft\\Windows\\CurrentVersion\\Run"; ValueType: none; ValueName: "Central WhatsApp"; Flags: deletevalue uninsdeletevalue
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "MODUX"; ValueData: """{app}\{#MyAppExeName}"" --autostart"; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Abrir o MODUX"; Flags: nowait postinstall skipifsilent
