#define MyAppName "贴边 EdgeBar"
#define MyAppVersion "2.2.0"
#define MyAppPublisher "CodeCat"
#define MyAppExeName "EdgeBar.exe"

[Setup]
AppId={{B3C1F5A2-7E4D-4A6B-9C21-5D8F0E3A7B61}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName=D:\Programs\EdgeBar
DisableDirPage=no
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=Output
OutputBaseFilename=EdgeBar-setup-2.2.0
SetupIconFile=..\Assets\app.ico
UninstallDisplayIcon={app}\EdgeBar.exe
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
MinVersion=10.0
CloseApplications=no
RestartApplications=no
UsePreviousAppDir=no
AllowNoIcons=yes
VersionInfoVersion=2.2.0.0
VersionInfoProductName={#MyAppName}
VersionInfoCompany={#MyAppPublisher}

[Languages]
Name: "chinesesimplified"; MessagesFile: "ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加图标："; Flags: checkedonce

[Files]
Source: "..\publish\EdgeBar.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\贴边 EdgeBar"; Filename: "{app}\EdgeBar.exe"
Name: "{autodesktop}\贴边 EdgeBar"; Filename: "{app}\EdgeBar.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\EdgeBar.exe"; Description: "立即运行 贴边 EdgeBar"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
function InitializeSetup(): Boolean;
var
  ResultCode: Integer;
begin
  { 覆盖升级时先结束正在运行的旧版本，避免文件被占用 }
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM EdgeBar.exe /F', '', SW_HIDE,
    ewWaitUntilTerminated, ResultCode);
  Result := True;
end;
