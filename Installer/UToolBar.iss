#define MyAppName "UToolBar"
#define MyAppVersion "2.3.2"
#define MyAppPublisher "CodeCat"
#define MyAppExeName "UToolBar.exe"

[Setup]
AppId={{B3C1F5A2-7E4D-4A6B-9C21-5D8F0E3A7B61}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName=D:\Programs\UToolBar
DisableDirPage=no
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=Output
OutputBaseFilename=UToolBar-setup-2.3.2
SetupIconFile=..\Assets\app.ico
UninstallDisplayIcon={app}\UToolBar.exe
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
VersionInfoVersion=2.3.2.0
VersionInfoProductName={#MyAppName}
VersionInfoCompany={#MyAppPublisher}

[Languages]
Name: "chinesesimplified"; MessagesFile: "ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加图标："; Flags: checkedonce

[Files]
Source: "..\publish\UToolBar.exe"; DestDir: "{app}"; Flags: ignoreversion

[InstallDelete]
Type: files; Name: "{autoprograms}\贴边 EdgeBar.lnk"
Type: files; Name: "{autodesktop}\贴边 EdgeBar.lnk"
Type: files; Name: "{autoprograms}\贴边 UToolBar.lnk"
Type: files; Name: "{autodesktop}\贴边 UToolBar.lnk"

[Icons]
Name: "{autoprograms}\UToolBar"; Filename: "{app}\UToolBar.exe"
Name: "{autodesktop}\UToolBar"; Filename: "{app}\UToolBar.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\UToolBar.exe"; Description: "立即运行 UToolBar"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
function InitializeSetup(): Boolean;
var
  ResultCode: Integer;
begin
  { 覆盖升级时先结束正在运行的旧版本，避免文件被占用 }
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM UToolBar.exe /F', '', SW_HIDE,
    ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM EdgeBar.exe /F', '', SW_HIDE,
    ewWaitUntilTerminated, ResultCode);
  Result := True;
end;

