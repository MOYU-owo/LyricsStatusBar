#define AppName "网易云任务栏歌词"
#define AppVersion "1.1.2"
#define AppPublisher "MOYU-owo"
#define AppExeName "LyricsStatusBar.exe"

[Setup]
AppId={{DC9C84B6-8FCB-4F16-AD49-A47C1F63D3D2}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL=https://github.com/MOYU-owo/LyricsStatusBar
AppSupportURL=https://github.com/MOYU-owo/LyricsStatusBar/issues
AppUpdatesURL=https://github.com/MOYU-owo/LyricsStatusBar/releases
DefaultDirName={localappdata}\Programs\LyricsStatusBar
DefaultGroupName={#AppName}
PrivilegesRequired=lowest
OutputDir=..\artifacts\installer
OutputBaseFilename=LyricsStatusBar-Win11-x64-Setup
SetupIconFile=..\assets\LyricsStatusBar.ico
VersionInfoCompany={#AppPublisher}
VersionInfoDescription={#AppName} 安装程序
VersionInfoProductName={#AppName}
VersionInfoProductVersion={#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExeName}
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "chinesesimp"; MessagesFile: "ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "autostart"; Description: "随 Windows 自动启动"; Flags: checkedonce

[Files]
Source: "..\artifacts\app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\artifacts\plugin\manifest.json"; DestDir: "{code:GetPluginDirectory}"; Flags: ignoreversion; Check: ShouldDeployPlugin
Source: "..\artifacts\plugin\plugin.js"; DestDir: "{code:GetPluginDirectory}"; Flags: ignoreversion; Check: ShouldDeployPlugin
Source: "..\artifacts\plugin\native.dll"; DestDir: "{code:GetPluginDirectory}"; Flags: ignoreversion; Check: ShouldDeployPlugin
Source: "..\artifacts\LyricsStatusBarBridge.plugin"; DestDir: "{code:GetPluginPackageDirectory}"; Flags: ignoreversion; Check: ShouldDeployPlugin

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\安装或修复 BetterNCM 桥接"; Filename: "{app}\{#AppExeName}"; Parameters: "--repair-plugin"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "LyricsStatusBar"; ValueData: """{app}\{#AppExeName}"" --autostart"; Tasks: autostart; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\LyricsStatusBar"; ValueType: string; ValueName: "BetterNCMDataDir"; ValueData: "{code:GetBetterNCMDataDirectory}"; Check: ShouldDeployPlugin; Flags: uninsdeletevalue uninsdeletekeyifempty

[Run]
Filename: "{app}\{#AppExeName}"; Description: "启动 {#AppName}"; Flags: nowait postinstall skipifsilent
Filename: "https://github.com/std-microblock/BetterNCM-Installer/releases"; Description: "尚未安装 BetterNCM，打开官方安装页面"; Flags: shellexec postinstall unchecked; Check: ShouldOfferBetterNCM

[UninstallRun]
Filename: "taskkill"; Parameters: "/IM {#AppExeName} /F"; Flags: runhidden; RunOnceId: "StopLyricsStatusBar"

[UninstallDelete]
Type: files; Name: "{code:GetStoredPluginDirectory}\manifest.json"; Check: HasStoredBetterNCMDirectory
Type: files; Name: "{code:GetStoredPluginDirectory}\plugin.js"; Check: HasStoredBetterNCMDirectory
Type: files; Name: "{code:GetStoredPluginDirectory}\native.dll"; Check: HasStoredBetterNCMDirectory
Type: dirifempty; Name: "{code:GetStoredPluginDirectory}"; Check: HasStoredBetterNCMDirectory
Type: files; Name: "{code:GetStoredPluginPackageDirectory}\LyricsStatusBarBridge.plugin"; Check: HasStoredBetterNCMDirectory

[Code]
var
  BetterNCMPage: TInputDirWizardPage;
  DetectedBetterNCMPath: String;

function IsBetterNCMDataDirectory(Path: String): Boolean;
begin
  Result :=
    (Trim(Path) <> '') and DirExists(Path) and
    (FileExists(AddBackslash(Path) + 'betterncm.dll') or
     DirExists(AddBackslash(Path) + 'plugins') or
     DirExists(AddBackslash(Path) + 'plugins_runtime'));
end;

function CandidateDataPath(): String;
var
  DriveCode: Integer;
  Candidate: String;
begin
  Result := '';
  if IsBetterNCMDataDirectory('C:\betterncm') then
  begin
    Result := 'C:\betterncm';
    Exit;
  end;

  Candidate := ExpandConstant('{userappdata}\BetterNCM');
  if IsBetterNCMDataDirectory(Candidate) then
  begin
    Result := Candidate;
    Exit;
  end;

  Candidate := ExpandConstant('{localappdata}\BetterNCM');
  if IsBetterNCMDataDirectory(Candidate) then
  begin
    Result := Candidate;
    Exit;
  end;

  for DriveCode := Ord('D') to Ord('Z') do
  begin
    Candidate := Chr(DriveCode) + ':\betterncm';
    if IsBetterNCMDataDirectory(Candidate) then
    begin
      Result := Candidate;
      Exit;
    end;
  end;
end;

procedure InitializeWizard();
var
  Description: String;
begin
  DetectedBetterNCMPath := CandidateDataPath();
  if DetectedBetterNCMPath = '' then
    Description := '当前没有检测到 BetterNCM。可以直接继续安装；以后安装 BetterNCM 后，本工具会自动补装桥接插件。'
  else
    Description := '已检测到 BetterNCM。安装程序会同时部署歌词桥接插件；如位置不正确，可在下方修改。';

  BetterNCMPage := CreateInputDirPage(
    wpSelectDir,
    'BetterNCM 检测',
    '选择 BetterNCM 数据目录（可留空）',
    Description,
    False,
    '');
  BetterNCMPage.Add('BetterNCM 数据目录：');
  BetterNCMPage.Values[0] := DetectedBetterNCMPath;
end;

function GetBetterNCMDataDirectory(Param: String): String;
begin
  Result := Trim(BetterNCMPage.Values[0]);
end;

function ShouldDeployPlugin(): Boolean;
begin
  Result := IsBetterNCMDataDirectory(GetBetterNCMDataDirectory(''));
end;

function ShouldOfferBetterNCM(): Boolean;
begin
  Result := not ShouldDeployPlugin();
end;

function GetPluginDirectory(Param: String): String;
begin
  Result := AddBackslash(GetBetterNCMDataDirectory('')) + 'plugins_runtime\lyrics_statusbar_bridge';
end;

function GetPluginPackageDirectory(Param: String): String;
begin
  Result := AddBackslash(GetBetterNCMDataDirectory('')) + 'plugins';
end;

function HasStoredBetterNCMDirectory(): Boolean;
var
  StoredPath: String;
begin
  Result :=
    RegQueryStringValue(HKCU, 'Software\LyricsStatusBar', 'BetterNCMDataDir', StoredPath) and
    (Trim(StoredPath) <> '');
end;

function GetStoredBetterNCMDirectory(): String;
begin
  if not RegQueryStringValue(HKCU, 'Software\LyricsStatusBar', 'BetterNCMDataDir', Result) then
    Result := '';
end;

function GetStoredPluginDirectory(Param: String): String;
begin
  Result := AddBackslash(GetStoredBetterNCMDirectory()) + 'plugins_runtime\lyrics_statusbar_bridge';
end;

function GetStoredPluginPackageDirectory(Param: String): String;
begin
  Result := AddBackslash(GetStoredBetterNCMDirectory()) + 'plugins';
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  SelectedPath: String;
begin
  Result := True;
  if CurPageID = BetterNCMPage.ID then
  begin
    SelectedPath := Trim(BetterNCMPage.Values[0]);
    if (SelectedPath <> '') and not IsBetterNCMDataDirectory(SelectedPath) then
    begin
      if MsgBox(
        '这个目录不像 BetterNCM 数据目录。选择“是”将清空该路径并只安装主程序，之后检测到 BetterNCM 时再自动补装插件；选择“否”返回修改。',
        mbConfirmation,
        MB_YESNO) = IDYES then
        BetterNCMPage.Values[0] := ''
      else
        Result := False;
    end;
  end;
end;