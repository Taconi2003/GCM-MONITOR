#ifndef PublishRoot
  #error Informe PublishRoot com a publicação self-contained validada.
#endif
#ifndef ProductVersion
  #error Informe ProductVersion.
#endif

#ifdef LabRoot
  #define ProductName "GCM Monitor Laboratorio"
#else
  #define ProductName "GCM Monitor"
#endif

[Setup]
#ifdef LabRoot
AppId={{204540CC-B3ED-4A10-8C50-B3FCB628CE57}
DefaultDirName={#LabRoot}\default-install
OutputBaseFilename=GCMMonitor-LabSetup
#else
AppId={{EF081865-9263-4D99-AF40-279AE0F9B55F}
DefaultDirName={localappdata}\Programs\GCM Monitor
OutputBaseFilename=GCMMonitor-Setup
#endif
AppName={#ProductName}
AppVersion={#ProductVersion}
AppPublisher=Gimenes Coleta e Monitoramento
AppPublisherURL=https://github.com/Taconi2003/GCM-MONITOR
DefaultGroupName={#ProductName}
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
Compression=lzma2/fast
SolidCompression=yes
WizardStyle=modern dark polar includetitlebar

[Languages]
Name: "ptbr"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Files]
Source: "{#PublishRoot}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\{#ProductName}"; Filename: "{app}\GcmMonitor.App.exe"; WorkingDir: "{app}"

[Code]
var
  RemoveLocalData: Boolean;
  CleanupIncomplete: Boolean;

function ReadAttributes(FileName: String): LongWord;
  external 'GetFileAttributesW@kernel32.dll stdcall';

function BusyMutexName(Param: String): String;
begin
#ifdef LabRoot
  Result := 'Local\GcmMonitor.InUse.Laboratorio';
#else
  Result := 'Global\GcmMonitor.InUse.' + Uppercase(GetSHA256OfUnicodeString(RemoveBackslashUnlessRoot(ExpandConstant('{localappdata}'))));
#endif
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
  if ReadAttributes(Path) <> $FFFFFFFF then
  begin
    if not PlainDirectory(Path) then CleanupIncomplete := True
    else if not RemoveDir(Path) then CleanupIncomplete := True;
  end;
end;

function IsTemporaryPreference(Name: String): Boolean;
var
  Index: Integer;
begin
  Result := False;
  if (Length(Name) <> 53) or (Copy(Name, 1, 17) <> 'preferences.json.') or
    (Copy(Name, 50, 4) <> '.tmp') then Exit;
  for Index := 18 to 49 do
    if Pos(Name[Index], '0123456789abcdef') = 0 then Exit;
  Result := True;
end;

procedure RemovePreferences(Directory: String);
var
  Entry: TFindRec;
begin
  DeleteKnownFile(Directory, 'preferences.json');
  if FindFirst(Directory + '\preferences.json.*.tmp', Entry) then
  begin
    try
      repeat
        if IsTemporaryPreference(Entry.Name) then DeleteKnownFile(Directory, Entry.Name);
      until not FindNext(Entry);
    finally
      FindClose(Entry);
    end;
  end;
end;

function InitializeUninstall(): Boolean;
#ifdef LabRoot
var
  Index: Integer;
#endif
begin
  Result := True;
  RemoveLocalData := False;
  if UninstallSilent then
  begin
#ifdef LabRoot
    for Index := 1 to ParamCount do
      if ParamStr(Index) = '/LabRemoveData' then RemoveLocalData := True;
#endif
    Exit;
  end;
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
#ifdef LabRoot
  Root := '{#LabRoot}\profile';
#else
  Root := ExpandConstant('{localappdata}\GCM Monitor');
#endif
  if ReadAttributes(Root) = $FFFFFFFF then Exit;
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
  if PlainDirectory(SettingsDir) then RemovePreferences(SettingsDir);
  RemoveIfEmpty(DataDir);
  RemoveIfEmpty(SettingsDir);
  RemoveIfEmpty(Root + '\Logs');
  RemoveIfEmpty(Root + '\Sessions');
  RemoveIfEmpty(Root);
  if CleanupIncomplete then SuppressibleMsgBox('Alguns dados locais foram preservados. Confira a pasta de dados do GCM Monitor em AppData Local.', mbInformation, MB_OK, IDOK);
end;
