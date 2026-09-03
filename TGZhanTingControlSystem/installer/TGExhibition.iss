#ifndef SourceRoot
  #error SourceRoot must point to a complete Phase 9G production package.
#endif
#ifndef OutputDir
  #define OutputDir "."
#endif
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

[Setup]
AppId={{83E146D0-EB44-4964-B77E-58D18BBD7694}
AppName=TG智慧展厅智能中控系统
AppVersion={#AppVersion}
AppPublisher=TG Exhibition
DefaultDirName={autopf}\TG Exhibition
DefaultGroupName=TG智慧展厅智能中控系统
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=TG智慧展厅智能中控系统_Setup
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
UninstallDisplayIcon={app}\Launcher\TG.Control.Launcher.exe
AppMutex=Global\TG.Exhibition.RuntimeLauncher
VersionInfoVersion={#AppVersion}
VersionInfoDescription=TG智慧展厅智能中控系统离线安装程序
VersionInfoCompany=TG Exhibition
VersionInfoProductName=TG智慧展厅智能中控系统

[Languages]
Name: "chinesesimp"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Files]
Source: "{#SourceRoot}\Server\*"; DestDir: "{app}\Server"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#SourceRoot}\TouchClient\*"; DestDir: "{app}\TouchClient"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#SourceRoot}\LedPlayer\*"; DestDir: "{app}\LedPlayer"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#SourceRoot}\TtsWorker\*"; DestDir: "{app}\TtsWorker"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#SourceRoot}\Launcher\*"; DestDir: "{app}\Launcher"; Flags: ignoreversion recursesubdirs createallsubdirs
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
Name: "{group}\运行管理"; Filename: "{app}\Launcher\TG.Control.Launcher.exe"
Name: "{group}\管理端"; Filename: "http://127.0.0.1:5080/"
Name: "{group}\部署健康检查"; Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\Tools\Test-DeploymentHealth.ps1"""
Name: "{group}\卸载"; Filename: "{uninstallexe}"
Name: "{autodesktop}\TG智慧展厅"; Filename: "{app}\Launcher\TG.Control.Launcher.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加选项："; Flags: checkedonce

[Run]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\Tools\Install-TGExhibition.ps1"" -InstallRoot ""{app}"" -DataRoot ""{commonappdata}\TG Exhibition"""; StatusMsg: "正在注册系统服务、现场配置和防火墙规则……"; Flags: runhidden waituntilterminated
Filename: "{app}\Launcher\TG.Control.Launcher.exe"; Description: "启动TG智慧展厅运行管理"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\Tools\Uninstall-TGExhibition.ps1"" -DataRoot ""{commonappdata}\TG Exhibition"" {code:GetRemoveDataSwitch}"; Flags: runhidden waituntilterminated; RunOnceId: "TGExhibitionRuntimeCleanup"

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
    { Stop the existing service before [Files] replaces immutable binaries during an upgrade. }
    Exec(ExpandConstant('{sys}\sc.exe'), 'stop "TG Exhibition Control Server"', '',
      SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Sleep(5000);
  end;
end;

function InitializeUninstall(): Boolean;
begin
  Result := True;
  RemoveCustomerData := False;
  if not UninstallSilent then
    RemoveCustomerData := SuppressibleMsgBox(
      '是否同时永久删除 ProgramData 中的现场配置、内容、媒体、缓存、日志和历史版本？' + #13#10 + #13#10 +
      '默认建议选择“否”，以便重新安装或数据恢复。',
      mbConfirmation, MB_YESNO or MB_DEFBUTTON2, IDNO) = IDYES;
end;

function GetRemoveDataSwitch(Param: String): String;
begin
  if RemoveCustomerData then
    Result := '-RemoveData'
  else
    Result := '';
end;
