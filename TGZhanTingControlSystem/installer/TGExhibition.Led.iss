#ifndef SourceRoot
  #error SourceRoot must point to a complete Led package.
#endif
#ifndef OutputDir
  #define OutputDir "."
#endif
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

[Setup]
AppId={{A86DCA4B-2351-4A40-B2C0-C5572359515D}
AppName=TG智慧展厅播放端
AppVersion={#AppVersion}
AppPublisher=TG Exhibition
DefaultDirName={autopf}\TG Exhibition Led
DefaultGroupName=TG智慧展厅播放端
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=TG智慧展厅播放端_Setup
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
UninstallDisplayIcon={app}\LedPlayer\LedPlayer.exe
AppMutex=Global\TG.Exhibition.RuntimeLauncher
VersionInfoVersion={#AppVersion}
VersionInfoDescription=TG智慧展厅播放端离线安装程序
VersionInfoCompany=TG Exhibition
VersionInfoProductName=TG智慧展厅播放端

[Languages]
Name: "chinesesimp"; MessagesFile: "ChineseSimplified.isl"

[Files]
Source: "{#SourceRoot}\LedPlayer\*"; DestDir: "{app}\LedPlayer"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#SourceRoot}\Launcher\*"; DestDir: "{app}\Launcher"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#SourceRoot}\Tools\*"; DestDir: "{app}\Tools"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#SourceRoot}\ThirdParty\*"; DestDir: "{app}\ThirdParty"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#SourceRoot}\package-manifest.json"; DestDir: "{app}"; Flags: ignoreversion

[Dirs]
Name: "{commonappdata}\TG Exhibition"
Name: "{commonappdata}\TG Exhibition\Config"
Name: "{commonappdata}\TG Exhibition\Cache\LedPlayer\Content"
Name: "{commonappdata}\TG Exhibition\Logs"

[Icons]
Name: "{group}\启动播放端"; Filename: "{app}\Launcher\TG.Control.Launcher.exe"
Name: "{group}\卸载"; Filename: "{uninstallexe}"
Name: "{autodesktop}\TG智慧展厅播放端"; Filename: "{app}\Launcher\TG.Control.Launcher.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加选项："; Flags: checkedonce

[Run]
Filename: "{app}\Launcher\TG.Control.Launcher.exe"; Description: "启动播放端"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\Tools\Uninstall-TGTerminal.ps1"" -Component Led -DataRoot ""{commonappdata}\TG Exhibition"" {code:GetRemoveDataSwitch}"; Flags: runhidden waituntilterminated; RunOnceId: "TGExhibitionLedCleanup"

[Code]
var
  RemoveCustomerData: Boolean;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
  PowerShellPath: String;
  Parameters: String;
  SiteConfigPath: String;
begin
  if CurStep <> ssPostInstall then Exit;
  SiteConfigPath := ExpandConstant('{src}\led.site-install.json');
  if not FileExists(SiteConfigPath) then
    RaiseException('安装程序旁缺少 led.site-install.json，请先填写服务端地址和终端接入密钥。');
  PowerShellPath := ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe');
  Parameters := '-NoProfile -ExecutionPolicy Bypass -File "' +
    ExpandConstant('{app}\Tools\Install-TGTerminal.ps1') + '" -Component Led -InstallRoot "' +
    ExpandConstant('{app}') + '" -DataRoot "' +
    ExpandConstant('{commonappdata}\TG Exhibition') + '" -SiteConfig "' + SiteConfigPath + '"';
  if (not Exec(PowerShellPath, Parameters, '', SW_HIDE, ewWaitUntilTerminated, ResultCode)) or
     (ResultCode <> 0) then
    RaiseException('播放端配置失败，错误代码：' + IntToStr(ResultCode));
end;

function InitializeUninstall(): Boolean;
begin
  Result := True;
  RemoveCustomerData := False;
  if not UninstallSilent then
    RemoveCustomerData := SuppressibleMsgBox(
      '是否同时永久删除本机播放端配置、缓存和日志？默认建议选择“否”。',
      mbConfirmation, MB_YESNO or MB_DEFBUTTON2, IDNO) = IDYES;
end;

function GetRemoveDataSwitch(Param: String): String;
begin
  if RemoveCustomerData then Result := '-RemoveData' else Result := '';
end;
