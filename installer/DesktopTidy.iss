#ifndef AppVersion
  #define AppVersion "1.0.2"
#endif
#ifndef BuildDir
  #error Pass /DBuildDir with the absolute application build directory.
#endif
#ifndef Bootstrapper
  #error Pass /DBootstrapper with the verified WebView2 bootstrapper path.
#endif
#ifndef ReleaseDir
  #define ReleaseDir "..\dist"
#endif

[Setup]
; Keep this identity stable across releases so upgrades reuse the same installation.
AppId={{82DAB9E9-9675-46A3-9391-6B9AEDE8C8EC}
AppName=桌面整理
AppVersion={#AppVersion}
AppPublisher=KellonWww
AppPublisherURL=https://github.com/KellonWww/desktop-tidy
DefaultDirName={localappdata}\Programs\DesktopTidy
DisableProgramGroupPage=yes
DisableDirPage=no
PrivilegesRequired=lowest
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0
OutputDir={#ReleaseDir}
OutputBaseFilename=DesktopTidy-Setup-{#AppVersion}-win-x64
SetupIconFile=..\src\DesktopTidy\Assets\desktop-tidy.ico
UninstallDisplayIcon={app}\DesktopTidy.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
AppMutex=Local\DesktopTidy.Prototype.C6E185EF
SetupMutex=Local\DesktopTidy.Setup.82DAB9E9
; Do not forcibly terminate a process that may be persisting drafts or restoring desktop icons.
CloseApplications=no
RestartApplications=no
Uninstallable=yes

[Languages]
Name: "zhcn"; MessagesFile: "compiler:Default.isl,ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; Flags: unchecked
Name: "startup"; Description: "登录 Windows 时启动（驻留托盘）"; Flags: unchecked

[Files]
; Explicit application files exclude diagnostic tools, user profiles and local data.
Source: "{#BuildDir}\DesktopTidy.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildDir}\DesktopTidy.exe.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildDir}\Microsoft.Web.WebView2.Core.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildDir}\Microsoft.Web.WebView2.WinForms.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildDir}\WebView2Loader.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildDir}\WebView2-LICENSE.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildDir}\Web\*"; DestDir: "{app}\Web"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Bootstrapper}"; DestName: "MicrosoftEdgeWebview2Setup.exe"; Flags: dontcopy

[Icons]
Name: "{userprograms}\桌面整理"; Filename: "{app}\DesktopTidy.exe"; WorkingDir: "{app}"
Name: "{userdesktop}\桌面整理"; Filename: "{app}\DesktopTidy.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "DesktopTidy"; ValueData: """{app}\DesktopTidy.exe"" --tray"; Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\DesktopTidy.exe"; Description: "启动桌面整理"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent unchecked

[Code]
const
  RuntimeKey = 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';

// Check both machine-wide and current-user Evergreen registrations, not the Edge browser.
function HasWebView2: Boolean;
var
  Version: String;
begin
  Result := RegQueryStringValue(HKLM32, RuntimeKey, 'pv', Version);
  Result := Result and (Version <> '') and (Version <> '0.0.0.0');
  if not Result then
  begin
    Result := RegQueryStringValue(HKCU, RuntimeKey, 'pv', Version);
    Result := Result and (Version <> '') and (Version <> '0.0.0.0');
  end;
end;

// Fail before installing application files when the required .NET Framework is missing.
function InitializeSetup: Boolean;
var
  Release: Cardinal;
  ErrorCode: Integer;
begin
  Result := RegQueryDWordValue(HKLM32,
    'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release);
  Result := Result and (Release >= 528040);
  if not Result then
    if SuppressibleMsgBox('需要先安装 .NET Framework 4.8 或更高兼容版本，再运行本安装包。' + #13#10 +
      '是否打开微软官方下载页面？', mbConfirmation, MB_YESNO, IDNO) = IDYES then
      ShellExec('open', 'https://dotnet.microsoft.com/download/dotnet-framework/net48',
        '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
end;

// Install the bundled, build-time signature-verified online bootstrapper only when needed.
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ExitCode: Integer;
begin
  Result := '';
  if HasWebView2 then exit;
  WizardForm.StatusLabel.Caption := '正在准备 WebView2 运行环境（需要网络连接）…';
  ExtractTemporaryFile('MicrosoftEdgeWebview2Setup.exe');
  if not Exec(ExpandConstant('{tmp}\MicrosoftEdgeWebview2Setup.exe'), '/silent /install',
    '', SW_HIDE, ewWaitUntilTerminated, ExitCode) then
  begin
    Result := '无法启动 WebView2 安装程序，请先安装微软 WebView2 Runtime 后重试。';
    exit;
  end;
  if (ExitCode <> 0) and (ExitCode <> 3010) then
  begin
    Result := 'WebView2 安装失败（代码 ' + IntToStr(ExitCode) + '），请检查网络或手动安装后重试。';
    exit;
  end;
  NeedsRestart := ExitCode = 3010;
  if not HasWebView2 then
    Result := '尚未检测到 WebView2 Runtime，请完成运行环境安装（如有需要请重启）后重试。';
end;

// Remove only this application's optional launch entries when deselected during an upgrade.
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep <> ssPostInstall then exit;
  if not WizardIsTaskSelected('startup') then
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'DesktopTidy');
  if not WizardIsTaskSelected('desktopicon') then
    DeleteFile(ExpandConstant('{userdesktop}\桌面整理.lnk'));
end;

// No UninstallDelete rules: notes, drafts, layout, backups and preferences remain outside {app}.
