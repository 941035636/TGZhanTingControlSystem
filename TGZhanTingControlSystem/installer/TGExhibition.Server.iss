#ifndef SourceRoot
  #error SourceRoot must point to a complete Server package.
#endif
#ifndef OutputDir
  #define OutputDir "."
#endif
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

[Setup]
AppId={{AAE238B7-9BFA-41A5-B4E9-4A0FC3F8A5C1}
AppName=TG智慧展厅服务端
AppVersion={#AppVersion}
AppPublisher=TG Exhibition
DefaultDirName={autopf}\TG Exhibition Server
DefaultGroupName=TG智慧展厅服务端
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=TG智慧展厅服务端_Setup
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
PrivilegesRequired=admin
WizardStyle=modern dynamic
SetupLogging=yes
CloseApplications=yes
RestartApplications=no
RestartIfNeededByRun=no
UninstallDisplayIcon={app}\Server\TG.Control.Server.exe
VersionInfoVersion={#AppVersion}
VersionInfoDescription=TG智慧展厅服务端离线安装程序
VersionInfoCompany=TG Exhibition
VersionInfoProductName=TG智慧展厅服务端

[Languages]
Name: "chinesesimp"; MessagesFile: "ChineseSimplified.isl"

[Files]
Source: "{#SourceRoot}\Server\*"; DestDir: "{app}\Server"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#SourceRoot}\TtsWorker\*"; DestDir: "{app}\TtsWorker"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#SourceRoot}\Tools\*"; DestDir: "{app}\Tools"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#SourceRoot}\ThirdParty\*"; DestDir: "{app}\ThirdParty"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#SourceRoot}\package-manifest.json"; DestDir: "{app}"; Flags: ignoreversion

[Dirs]
Name: "{commonappdata}\TG Exhibition"
Name: "{commonappdata}\TG Exhibition\Config"
Name: "{commonappdata}\TG Exhibition\Data"
Name: "{commonappdata}\TG Exhibition\Media"
Name: "{commonappdata}\TG Exhibition\Cache"
Name: "{commonappdata}\TG Exhibition\Logs"
Name: "{commonappdata}\TG Exhibition\Backups"
Name: "{commonappdata}\TG Exhibition\Runtime"

[Icons]
Name: "{group}\打开管理端"; Filename: "https://localhost:5443/"
Name: "{group}\部署健康检查"; Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\Tools\Test-DeploymentHealth.ps1"""
Name: "{group}\卸载"; Filename: "{uninstallexe}"

[UninstallRun]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\Tools\Uninstall-TGExhibition.ps1"" -DataRoot ""{commonappdata}\TG Exhibition"" {code:GetRemoveDataSwitch}"; Flags: runhidden waituntilterminated; RunOnceId: "TGExhibitionServerCleanup"

[Code]
var
  RemoveCustomerData: Boolean;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Result := '';
  if RegKeyExists(HKLM64, 'SYSTEM\CurrentControlSet\Services\TG Exhibition Control Server') then
  begin
    Exec(ExpandConstant('{sys}\sc.exe'), 'stop "TG Exhibition Control Server"', '',
      SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Sleep(5000);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
  PowerShellPath: String;
  Parameters: String;
  SiteConfigPath: String;
begin
  if CurStep <> ssPostInstall then
    Exit;
  SiteConfigPath := ExpandConstant('{src}\server.site-install.json');
  PowerShellPath := ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe');
  Parameters := '-NoProfile -ExecutionPolicy Bypass -File "' +
    ExpandConstant('{app}\Tools\Install-TGServer.ps1') + '" -InstallRoot "' +
    ExpandConstant('{app}') + '" -DataRoot "' +
    ExpandConstant('{commonappdata}\TG Exhibition') + '"';
  if FileExists(SiteConfigPath) then
    Parameters := Parameters + ' -SiteConfig "' + SiteConfigPath + '"';
  if (not Exec(PowerShellPath, Parameters, '', SW_HIDE, ewWaitUntilTerminated, ResultCode)) or
     (ResultCode <> 0) then
    RaiseException('服务端注册失败，错误代码：' + IntToStr(ResultCode));
end;

function InitializeUninstall(): Boolean;
begin
  Result := True;
  RemoveCustomerData := False;
  if not UninstallSilent then
    RemoveCustomerData := SuppressibleMsgBox(
      '是否同时永久删除现场配置、内容、媒体、缓存、日志和历史版本？' + #13#10 + #13#10 +
      '默认建议选择“否”，以便重新安装或数据恢复。',
      mbConfirmation, MB_YESNO or MB_DEFBUTTON2, IDNO) = IDYES;
end;

function GetRemoveDataSwitch(Param: String): String;
begin
  if RemoveCustomerData then Result := '-RemoveData' else Result := '';
end;
