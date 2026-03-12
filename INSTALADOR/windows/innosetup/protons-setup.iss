; === VC-02: Versao centralizada ===
; A versao e passada via linha de comando: /DAppVersion=X.Y.Z
; Fallback para 1.0.0 se nao for definida
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef AppPublisher
  #define AppPublisher "Protons Team"
#endif

[Setup]
AppName=Protons
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\Protons
DefaultGroupName=Protons
OutputDir=..\..\saida\windows
OutputBaseFilename=ProtonsSetup-{#AppVersion}
SetupIconFile=..\ativos\logo_protons.ico
Compression=lzma
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=admin
UninstallDisplayIcon={app}\Protons.UI.exe
WizardStyle=modern dynamic hidebevels excludelightcontrols
ShowLanguageDialog=no
WizardBackImageFile=..\ativos\installer\wizard_back_light.png
WizardBackImageFileDynamicDark=..\ativos\installer\wizard_back_dark.png
WizardImageStretch=no
WizardImageAlphaFormat=defined
WizardSmallImageFile=..\ativos\installer\wizard_small_logo_light.png
WizardSmallImageFileDynamicDark=..\ativos\installer\wizard_small_logo_dark.png
WizardBackColor=$F6F8FB
WizardBackColorDynamicDark=$12161F
SetupLogging=yes
LanguageDetectionMethod=uilanguage

; === SS-05: Assinatura digital ===
; Requer: Variaveis de ambiente PROTONS_CERT_PATH e PROTONS_CERT_PASS
; Para compilar com assinatura:
;   $env:PROTONS_CERT_PATH = "C:\caminho\certificado.pfx"
;   $env:PROTONS_CERT_PASS = "senha"
;   & "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" protons-setup.iss
;
; SignTool deve estar no PATH ou usar caminho completo
; Descomentar as linhas abaixo quando o certificado estiver configurado:
; SignTool=signtool sign /f "%PROTONS_CERT_PATH%" /p "%PROTONS_CERT_PASS%" /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /v $f
; SignedUninstaller=yes

[Languages]
Name: "ptbr"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Files]
; SS-02: Excluir *.pdb (simbolos de debug)
; SS-03: Excluir *.json e incluir apenas os necessarios (whitelist)
Source: "..\..\..\Login\Protons.UI\bin\Release\net8.0\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs; Excludes: "*.pdb,*.json"
; SS-03: Whitelist de arquivos JSON necessarios
Source: "..\..\..\Login\Protons.UI\bin\Release\net8.0\win-x64\publish\appsettings.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\..\Login\Protons.UI\bin\Release\net8.0\win-x64\publish\Protons.UI.runtimeconfig.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\..\Login\Protons.UI\bin\Release\net8.0\win-x64\publish\Protons.UI.deps.json"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{commondesktop}\Protons Login"; Filename: "{app}\Protons.UI.exe"; WorkingDir: "{app}"
Name: "{commonprograms}\Protons\Protons Login"; Filename: "{app}\Protons.UI.exe"; WorkingDir: "{app}"
Name: "{commonprograms}\Protons\Desinstalar Protons"; Filename: "{uninstallexe}"

[Registry]
Root: HKLM64; Subkey: "Software\Protons"; ValueType: string; ValueName: "InstallPath"; ValueData: "{app}"; Flags: uninsdeletekey
Root: HKLM64; Subkey: "Software\Protons"; ValueType: string; ValueName: "Version"; ValueData: "{#AppVersion}"
Root: HKLM64; Subkey: "Software\Protons"; ValueType: string; ValueName: "InstallScope"; ValueData: "perMachine"

[Dirs]
Name: "{userappdata}\Protons"; Flags: uninsneveruninstall

[Run]
Filename: "{app}\Protons.UI.exe"; Description: "Iniciar Protons"; Flags: nowait postinstall skipifsilent

[Messages]
WelcomeLabel1=Bem-vindo ao instalador profissional do Protons
WelcomeLabel2=Este assistente vai preparar o Protons Login com seguranca e rapidez neste computador.
FinishedLabel=A instalacao do Protons Login foi concluida com sucesso.%n%nClique em Concluir para fechar este assistente.
ExitSetupMessage=A instalacao do Protons nao foi concluida.%n%nDeseja cancelar a instalacao?
SelectDirDesc=Escolha o local de instalacao do Protons
SelectDirLabel3=O Protons sera instalado na pasta abaixo. Clique em Avancar para continuar.
DiskSpaceWarning=Para instalar o Protons sao necessarios pelo menos %1 KB de espaco em disco. Voce tem apenas %2 KB disponiveis. Deseja continuar mesmo assim?

[CustomMessages]
ptbr.ProgressStageLabel=Etapa atual:
ptbr.EtaLabel=Tempo estimado restante:
ptbr.EtaCalculating=calculando...
ptbr.EtaSecondsSuffix=s
ptbr.CancelConfirmMessage=Deseja realmente cancelar a instalacao do Protons?
ptbr.StagePreparing=Preparando instalacao
ptbr.StageInstalling=Instalando arquivos
ptbr.StageFinalizing=Finalizando configuracao
ptbr.FailpointMessage=Falha simulada para validacao de rollback.
ptbr.CancelTestMessage=Cancelamento simulado para validacao.

[Code]
var
  ProgressStageText: TNewStaticText;
  ProgressEtaText: TNewStaticText;
  InstallStartedAt: Cardinal;
  CancelRequested: Boolean;

function GetTickCount: Cardinal;
  external 'GetTickCount@kernel32 stdcall';

function HasSwitch(const SwitchText: String): Boolean;
begin
  Result := Pos(UpperCase(SwitchText), UpperCase(GetCmdTail)) > 0;
end;

function RollbackTestEnabled: Boolean;
begin
  Result := HasSwitch('/PROTONS_ROLLBACK_TEST=1');
end;

function SlowInstallEnabled: Boolean;
begin
  Result := HasSwitch('/PROTONS_SLOW_INSTALL=1');
end;

function CancelTestEnabled: Boolean;
begin
  Result := HasSwitch('/PROTONS_CANCEL_TEST=1');
end;

procedure SafeDeleteFile(const FileName: String);
begin
  if FileExists(FileName) then
    DeleteFile(FileName);
end;

procedure CleanupFailedInstall;
var
  InstallPath: String;
begin
  InstallPath := ExpandConstant('{app}');
  SafeDeleteFile(ExpandConstant('{commondesktop}\Protons Login.lnk'));
  DelTree(ExpandConstant('{commonprograms}\Protons'), True, True, True);
  RegDeleteKeyIncludingSubkeys(HKLM64, 'Software\Protons');
  RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Protons');
  if DirExists(InstallPath) then
    DelTree(InstallPath, True, True, True);
end;

procedure UpdateProgressUi(const StageText: String; const EtaText: String);
begin
  if Assigned(ProgressStageText) then
    ProgressStageText.Caption := ExpandConstant('{cm:ProgressStageLabel}') + ' ' + StageText;
  if Assigned(ProgressEtaText) then
    ProgressEtaText.Caption := ExpandConstant('{cm:EtaLabel}') + ' ' + EtaText;
end;

function InitializeSetup: Boolean;
var
  FreeMB: Cardinal;
  TotalMB: Cardinal;
  MinMB: Cardinal;
begin
  MinMB := 150;
  FreeMB := 0;
  TotalMB := 0;
  if GetSpaceOnDisk(ExpandConstant('{autopf}'), True, FreeMB, TotalMB) then
  begin
    if FreeMB < MinMB then
    begin
      MsgBox(
        'Espaco em disco insuficiente.' + #13#10 +
        'O Protons requer pelo menos ' + IntToStr(MinMB) + ' MB livres.' + #13#10 +
        'Espaco disponivel: ' + IntToStr(FreeMB) + ' MB.',
        mbError, MB_OK
      );
      Result := False;
      Exit;
    end;
  end;
  Result := True;
end;

procedure InitializeWizard;
begin
  InstallStartedAt := GetTickCount;
  CancelRequested := False;

  ProgressStageText := TNewStaticText.Create(WizardForm);
  ProgressStageText.Parent := WizardForm.InstallingPage;
  ProgressStageText.Left := WizardForm.ProgressGauge.Left;
  ProgressStageText.Top := WizardForm.ProgressGauge.Top - ScaleY(22);
  ProgressStageText.Width := WizardForm.ProgressGauge.Width;
  ProgressStageText.Caption := ExpandConstant('{cm:ProgressStageLabel}') + ' ' + ExpandConstant('{cm:StagePreparing}');

  ProgressEtaText := TNewStaticText.Create(WizardForm);
  ProgressEtaText.Parent := WizardForm.InstallingPage;
  ProgressEtaText.Left := WizardForm.ProgressGauge.Left;
  ProgressEtaText.Top := WizardForm.ProgressGauge.Top + WizardForm.ProgressGauge.Height + ScaleY(8);
  ProgressEtaText.Width := WizardForm.ProgressGauge.Width;
  ProgressEtaText.Caption := ExpandConstant('{cm:EtaLabel}') + ' ' + ExpandConstant('{cm:EtaCalculating}');
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = wpInstalling then
  begin
    InstallStartedAt := GetTickCount;
    UpdateProgressUi(ExpandConstant('{cm:StageInstalling}'), ExpandConstant('{cm:EtaCalculating}'));
  end;
end;

function FormatEta(Seconds: Integer): String;
var
  Mins, Secs: Integer;
begin
  if Seconds < 3 then
    Result := ExpandConstant('{cm:EtaCalculating}')
  else if Seconds < 60 then
    Result := IntToStr(Seconds) + ExpandConstant('{cm:EtaSecondsSuffix}')
  else
  begin
    Mins := Seconds div 60;
    Secs := Seconds mod 60;
    if Secs < 10 then
      Result := IntToStr(Mins) + 'm 0' + IntToStr(Secs) + 's'
    else
      Result := IntToStr(Mins) + 'm ' + IntToStr(Secs) + 's';
  end;
end;

procedure CurInstallProgressChanged(CurProgress, MaxProgress: Integer);
var
  ElapsedSec: Integer;
  RemainingSec: Integer;
  EtaText: String;
begin
  if MaxProgress <= 0 then
    Exit;

  ElapsedSec := (GetTickCount - InstallStartedAt) div 1000;
  if (CurProgress > 0) and (ElapsedSec > 0) then
  begin
    RemainingSec := ((MaxProgress - CurProgress) * ElapsedSec) div CurProgress;
    EtaText := FormatEta(RemainingSec);
  end
  else
    EtaText := ExpandConstant('{cm:EtaCalculating}');

  UpdateProgressUi(ExpandConstant('{cm:StageInstalling}'), EtaText);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
  begin
    UpdateProgressUi(ExpandConstant('{cm:StageInstalling}'), ExpandConstant('{cm:EtaCalculating}'));
    if RollbackTestEnabled then
    begin
      CleanupFailedInstall;
      RaiseException(ExpandConstant('{cm:FailpointMessage}'));
    end;
    if CancelTestEnabled then
    begin
      CancelRequested := True;
      CleanupFailedInstall;
      RaiseException(ExpandConstant('{cm:CancelTestMessage}'));
    end;
    if SlowInstallEnabled then
      Sleep(15000);
  end;

  if CurStep = ssPostInstall then
  begin
    UpdateProgressUi(ExpandConstant('{cm:StageFinalizing}'), '0' + ExpandConstant('{cm:EtaSecondsSuffix}'));
  end;
end;

procedure DeinitializeSetup;
begin
  if CancelRequested then
    CleanupFailedInstall;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Protons');
  end;
end;
