#define MyAppName "Smart Layout Switcher"
#define MyAppVersion "1.0.12"
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
SetupIconFile=..\assets\branding\smart-layout-switcher.ico
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
english.WinSpaceHotkey=Win + Space (recommended)
english.AltShiftHotkey=Left Alt + Left Shift
russian.HotkeyPageCaption=Сочетание клавиш
russian.HotkeyPageDescription=Выберите сочетание для переключения раскладок
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
  HotkeyPage: TWizardPage;
  WinSpaceRadio: TNewRadioButton;
  AltShiftRadio: TNewRadioButton;

procedure InitializeWizard;
begin
  HotkeyPage := CreateCustomPage(wpSelectTasks,
    ExpandConstant('{cm:HotkeyPageCaption}'),
    ExpandConstant('{cm:HotkeyPageDescription}'));

  WinSpaceRadio := TNewRadioButton.Create(HotkeyPage.Surface);
  WinSpaceRadio.Parent := HotkeyPage.Surface;
  WinSpaceRadio.Caption := ExpandConstant('{cm:WinSpaceHotkey}');
  WinSpaceRadio.Left := ScaleX(8);
  WinSpaceRadio.Top := ScaleY(12);
  WinSpaceRadio.Width := HotkeyPage.SurfaceWidth - ScaleX(16);
  WinSpaceRadio.Checked := True;

  AltShiftRadio := TNewRadioButton.Create(HotkeyPage.Surface);
  AltShiftRadio.Parent := HotkeyPage.Surface;
  AltShiftRadio.Caption := ExpandConstant('{cm:AltShiftHotkey}');
  AltShiftRadio.Left := ScaleX(8);
  AltShiftRadio.Top := WinSpaceRadio.Top + ScaleY(28);
  AltShiftRadio.Width := HotkeyPage.SurfaceWidth - ScaleX(16);
end;

function GetSelectedHotkey(Param: String): String;
begin
  if AltShiftRadio.Checked then
    Result := 'LeftAltLeftShift'
  else
    Result := 'WinSpace';
end;
