#define MyAppName "Central WhatsApp"
#define MyAppVersion "0.7.0"
#define MyAppPublisher "Central WhatsApp"
#define MyAppExeName "CentralWhatsApp.WebView2.exe"

[Setup]
AppId={{B1A14D26-5498-48D7-BABD-9FB9B4AD739E}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\Central WhatsApp
DefaultGroupName=Central WhatsApp
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=installer-output
OutputBaseFilename=Central-WhatsApp-Setup-{#MyAppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
RestartApplications=no
UninstallDisplayName=Central WhatsApp
SetupLogging=yes

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "desktopicon"; Description: "Criar atalho na Área de Trabalho"; GroupDescription: "Atalhos:"; Flags: checkedonce
Name: "autostart"; Description: "Iniciar o Central WhatsApp com o Windows"; GroupDescription: "Inicialização:"; Flags: checkedonce

[Files]
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Central WhatsApp"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\Central WhatsApp"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "CentralWhatsApp"; ValueData: """{app}\{#MyAppExeName}"" --autostart"; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Abrir o Central WhatsApp"; Flags: nowait postinstall skipifsilent
