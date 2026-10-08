; Instalador do VideoStreaming (Inno Setup 6)
; Gere com: publicar.bat  (ou ISCC.exe installer\VideoStreaming.iss depois de publicar o modo portátil)

#define AppName "VideoStreaming"
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#define AppExe "VideoStreaming.exe"
#define FirewallRule "VideoStreaming"

[Setup]
AppId={{B6C5F2F4-3A0E-4F64-9D61-5E2C7A1D9B11}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppName}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
SetupIconFile=..\src\Host\Assets\app.ico
OutputDir=..\dist\installer
OutputBaseFilename=VideoStreaming-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
AppMutex=VideoStreaming.SingleInstance
CloseApplications=yes
DisableProgramGroupPage=yes
VersionInfoVersion={#AppVersion}
VersionInfoProductName={#AppName}

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "desktopicon"; Description: "Criar atalho na Área de Trabalho"; GroupDescription: "Atalhos:"
Name: "firewall"; Description: "Liberar o VideoStreaming no Firewall do Windows (recomendado)"; GroupDescription: "Rede:"

[Files]
Source: "..\dist\portable\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\Desinstalar {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""{#FirewallRule}"" program=""{app}\{#AppExe}"""; Flags: runhidden; Tasks: firewall
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""{#FirewallRule}"" dir=in action=allow program=""{app}\{#AppExe}"" enable=yes profile=any"; Flags: runhidden; Tasks: firewall
Filename: "{app}\{#AppExe}"; Description: "Abrir o {#AppName}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""{#FirewallRule}"" program=""{app}\{#AppExe}"""; Flags: runhidden; RunOnceId: "RemoveFirewallRule"

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    DataDir := ExpandConstant('{userappdata}\VideoStreaming');
    if DirExists(DataDir) then
      if MsgBox('Deseja remover também as configurações, a lista de banidos e os arquivos salvos do VideoStreaming?' + #13#10 + DataDir,
                mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
        DelTree(DataDir, True, True, True);
  end;
end;
