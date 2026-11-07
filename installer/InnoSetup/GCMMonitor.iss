#ifndef PublishRoot
  #error Informe PublishRoot com a publicação self-contained validada.
#endif
#ifndef ProductVersion
  #error Informe ProductVersion.
#endif

[Setup]
AppId={{EF081865-9263-4D99-AF40-279AE0F9B55F}
AppName=GCM Monitor
AppVersion={#ProductVersion}
AppPublisher=Gimenes Coleta e Monitoramento
AppPublisherURL=https://github.com/Taconi2003/GCM-MONITOR
DefaultDirName={localappdata}\Programs\GCM Monitor
DefaultGroupName=GCM Monitor
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
MinVersion=10.0.19045
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
SetupArchitecture=x64
AppMutex={code:BusyMutexName}
CloseApplications=no
RestartApplications=no
UninstallDisplayIcon={app}\GcmMonitor.App.exe
OutputBaseFilename=GCMMonitor-Setup
Compression=lzma2/fast
SolidCompression=yes
WizardStyle=modern dark polar includetitlebar

[Languages]
Name: "ptbr"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Files]
Source: "{#PublishRoot}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\GCM Monitor"; Filename: "{app}\GcmMonitor.App.exe"; WorkingDir: "{app}"

[Code]
var
  RemoveLocalData: Boolean;
  CleanupIncomplete: Boolean;

function ReadAttributes(FileName: String): LongWord;
  external 'GetFileAttributesW@kernel32.dll stdcall';

function BusyMutexName(Param: String): String;
begin
  Result := 'Global\GcmMonitor.InUse.' + Uppercase(GetSHA256OfUnicodeString(RemoveBackslashUnlessRoot(ExpandConstant('{localappdata}'))));
end;

function PlainDirectory(Path: String): Boolean;
var
  Parent: String;
  Attributes: LongWord;
begin
  Attributes := ReadAttributes(Path);
  Result := (Attributes <> $FFFFFFFF) and ((Attributes and $10) <> 0) and ((Attributes and $400) = 0);
  if not Result then Exit;
  Parent := ExtractFileDir(Path);
  if (Parent <> '') and (Parent <> Path) then Result := PlainDirectory(Parent);
end;

procedure DeleteKnownFile(Directory, Name: String);
var
  FileName: String;
  Attributes: LongWord;
begin
  FileName := AddBackslash(Directory) + Name;
  Attributes := ReadAttributes(FileName);
  if Attributes = $FFFFFFFF then Exit;
  if ((Attributes and $410) <> 0) or not DeleteFile(FileName) then
  begin
    CleanupIncomplete := True;
    Log('Arquivo de dados preservado durante a limpeza: ' + Name);
  end;
end;

procedure RemoveIfEmpty(Path: String);
begin
  if DirExists(Path) then
  begin
    if not PlainDirectory(Path) then CleanupIncomplete := True
    else if not RemoveDir(Path) then CleanupIncomplete := True;
  end;
end;

function InitializeUninstall(): Boolean;
begin
  Result := True;
  RemoveLocalData := False;
  if UninstallSilent then Exit;
  Result := MsgBox('Exporte os relatórios que deseja manter antes de remover o GCM Monitor. Continuar com a desinstalação?',
    mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES;
  if Result then
    RemoveLocalData := MsgBox('Remover também o histórico e as preferências locais do GCM Monitor? Escolha Não para preservá-los. PDFs exportados serão preservados.',
      mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Root, DataDir, SettingsDir: String;
begin
  if (CurUninstallStep <> usPostUninstall) or not RemoveLocalData then Exit;
  Root := ExpandConstant('{localappdata}\GCM Monitor');
  if not DirExists(Root) then Exit;
  if CheckForMutexes(BusyMutexName('')) or not PlainDirectory(Root) then
  begin
    MsgBox('Os dados locais foram preservados porque o perfil está em uso ou a pasta não pôde ser validada.', mbError, MB_OK);
    Exit;
  end;
  DataDir := Root + '\Data';
  SettingsDir := Root + '\Settings';
  if PlainDirectory(DataDir) then
  begin
    DeleteKnownFile(DataDir, 'gcm-monitor.db');
    DeleteKnownFile(DataDir, 'gcm-monitor.db-wal');
    DeleteKnownFile(DataDir, 'gcm-monitor.db-shm');
    DeleteKnownFile(DataDir, 'gcm-monitor.db-journal');
  end;
  if PlainDirectory(SettingsDir) then DeleteKnownFile(SettingsDir, 'preferences.json');
  RemoveIfEmpty(DataDir);
  RemoveIfEmpty(SettingsDir);
  RemoveIfEmpty(Root + '\Logs');
  RemoveIfEmpty(Root + '\Sessions');
  RemoveIfEmpty(Root);
  if CleanupIncomplete then MsgBox('Alguns dados locais foram preservados. Confira a pasta de dados do GCM Monitor em AppData Local.', mbInformation, MB_OK);
end;
