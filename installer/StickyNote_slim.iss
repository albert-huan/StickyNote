#define AppName "StickyNote"
#define AppVersion "1.4.0"
#define AppPublisher "StickyNote"
#define AppURL "https://example.invalid/StickyNote"
#define AppExe "StickyNote.exe"

#ifndef Arch
#define Arch "x64"
#endif
#if Arch == 'x64'
  #define BuildDir "..\\publish-slim\\win-x64"
  #define OutputBase "StickyNote_Setup_Slim_x64"
  #define DefaultDir "{pf}\\StickyNote"
  #define ArchitecturesAllowed "x64"
  #define InstallIn64Bit "x64"
#else
  #define BuildDir "..\\publish-slim\\win-x86"
  #define OutputBase "StickyNote_Setup_Slim_x86"
  #define DefaultDir "{pf32}\\StickyNote"
  #define ArchitecturesAllowed "x86"
  #define InstallIn64Bit ""
#endif

[Setup]
AppId={{93D2F5D5-3DAD-4C84-9E3B-8E2B1D6F1A11}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}
AppUpdatesURL={#AppURL}
DefaultDirName={#DefaultDir}
DefaultGroupName={#AppName}
OutputDir=out
OutputBaseFilename={#OutputBase}
DisableWelcomePage=no
DisableDirPage=no
DisableReadyPage=no
DisableFinishedPage=no
AllowNoIcons=yes
Compression=lzma2/ultra
SolidCompression=yes
ArchitecturesAllowed={#ArchitecturesAllowed}
ArchitecturesInstallIn64BitMode={#InstallIn64Bit}
PrivilegesRequired=admin
ChangesEnvironment=yes
WizardStyle=modern
SetupLogging=yes
SignedUninstaller=no



[Languages]
Name: "chs"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "在桌面创建快捷方式"
Name: "startmenuicon"; Description: "在开始菜单创建快捷方式"
Name: "autostart"; Description: "开机自动启动"; Flags: unchecked
Name: "addenv"; Description: "添加系统环境变量(STICKYNOTE_HOME)"; Flags: unchecked

[Components]
Name: "main"; Description: "主程序"; Types: full compact custom; Flags: fixed
Name: "options"; Description: "可选功能"; Types: full custom

[Files]
Source: "{#BuildDir}\\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion; Components: main

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\\{#AppExe}"; Tasks: startmenuicon
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\\{#AppExe}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\\Microsoft\\Windows\\CurrentVersion\\Run"; ValueType: string; ValueName: "{#AppName}"; ValueData: """{app}\\{#AppExe}"""; Flags: uninsdeletevalue; Tasks: autostart
Root: HKLM; Subkey: "SYSTEM\\CurrentControlSet\\Control\\Session Manager\\Environment"; ValueType: expandsz; ValueName: "STICKYNOTE_HOME"; ValueData: "{app}"; Flags: uninsdeletevalue; Tasks: addenv

[Run]
Filename: "{app}\\{#AppExe}"; Description: "运行 {#AppName}"; Flags: postinstall nowait skipifsilent

[UninstallDelete]
Type: files; Name: "{app}\\StickyNote.pdb"

[Code]
function HasDesktopRuntime(): Boolean;
var
  sharedPath: string;
  FindRec: TFindRec;
begin
  if Is64BitInstallMode then
    sharedPath := ExpandConstant('{pf}\\dotnet\\shared\\Microsoft.WindowsDesktop.App')
  else
    sharedPath := ExpandConstant('{pf32}\\dotnet\\shared\\Microsoft.WindowsDesktop.App');
  Result := False;
  if DirExists(sharedPath) then
  begin
    if FindFirst(sharedPath + '\\*', FindRec) then
    begin
      try
        while True do
        begin
          if (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
          begin
            if Copy(FindRec.Name, 1, 2) = '8.' then
            begin
              Result := True;
              break;
            end;
          end;
          if not FindNext(FindRec) then break;
        end;
      finally
        FindClose(FindRec);
      end;
    end;
  end;
end;

function InitializeSetup(): Boolean;
begin
  if not HasDesktopRuntime() then
  begin
    MsgBox('.NET 8 Windows 桌面运行时未检测到，请先安装后再继续。', mbError, MB_OK);
    Result := False;
  end
  else
    Result := True;
end;
