#define MyAppName "Smart Layout Switcher"
#define MyAppVersion "1.0.19-test.1"
#define MyAppPublisher "Vladimir Potapov"
#define MyAppURL "https://github.com/vldpotapov/SmartLayoutSwitcher"
#define MyAppExeName "SmartLayoutSwitcher.App.exe"

[Setup]
AppId={{99A6A9FA-2D42-47B9-A4EB-2B154CEF0163}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\artifacts\installer
OutputBaseFilename=SmartLayoutSwitcher-Setup-{#MyAppVersion}
SetupIconFile=..\assets\installer\Lang-icon.ico
WizardImageFile=..\assets\installer\setup-image@2x.png
WizardSmallImageFile=..\assets\installer\Lang-icon-wizard.png
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "autostart"; Description: "Start Smart Layout Switcher with Windows"
Name: "desktopicon"; Description: "Create a desktop icon"; Flags: unchecked

[CustomMessages]
english.HotkeyPageCaption=Keyboard shortcut
english.HotkeyPageDescription=Choose the shortcut for switching layouts
english.HotkeyPageInstructions=You can change this later in Settings.
english.WinSpaceHotkey=Win + Space (recommended)
english.AltShiftHotkey=Left Alt + Left Shift
russian.HotkeyPageCaption=Сочетание клавиш
russian.HotkeyPageDescription=Выберите сочетание для переключения раскладок
russian.HotkeyPageInstructions=Его можно изменить позже в настройках приложения.
russian.WinSpaceHotkey=Win + Space (рекомендуется)
russian.AltShiftHotkey=Левый Alt + Левый Shift

[Files]
Source: "..\artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "SmartLayoutSwitcher"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: autostart; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\SmartLayoutSwitcher"; ValueType: string; ValueName: "InstallerHotkey"; ValueData: "{code:GetSelectedHotkey}"; Flags: uninsdeletevalue

[Code]
var
  HotkeyPage: TInputOptionWizardPage;

procedure StopRunningApplication;
var
  AppPath: String;
  ResultCode: Integer;
begin
  AppPath := ExpandConstant('{app}\{#MyAppExeName}');
  if not FileExists(AppPath) then
    exit;

  { Newer versions close cleanly through the named shutdown request. }
  Exec(AppPath, '--shutdown', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(750);

  { Older test builds do not understand the shutdown request. The process has no
    unsaved document state, so Setup can safely end only this application's process. }
  Exec(ExpandConstant('{sys}\taskkill.exe'),
    '/F /IM "{#MyAppExeName}"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
    StopRunningApplication;
end;

procedure InitializeWizard;
begin
  HotkeyPage := CreateInputOptionPage(wpSelectTasks,
    ExpandConstant('{cm:HotkeyPageCaption}'),
    ExpandConstant('{cm:HotkeyPageDescription}'),
    ExpandConstant('{cm:HotkeyPageInstructions}'),
    True, False);
  HotkeyPage.Add(ExpandConstant('{cm:WinSpaceHotkey}'));
  HotkeyPage.Add(ExpandConstant('{cm:AltShiftHotkey}'));
  HotkeyPage.SelectedValueIndex := 0;
end;

function GetSelectedHotkey(Param: String): String;
begin
  if HotkeyPage.SelectedValueIndex = 1 then
    Result := 'LeftAltLeftShift'
  else
    Result := 'WinSpace';
end;
