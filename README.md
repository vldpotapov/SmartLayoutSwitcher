# Smart Layout Switcher

<p align="center">
  <img src="icon/Lang-icon.png" width="128" alt="Smart Layout Switcher icon">
</p>

Smart Layout Switcher is a small Windows tray app for switching the input language without breaking normal typing. It works only with the keyboard layouts already installed for the current Windows user.

## Download and install

Download the latest `SmartLayoutSwitcher-Setup.exe` from the [GitHub Releases page](https://github.com/vldpotapov/SmartLayoutSwitcher/releases/latest), run it, and follow the installer.

The installer is self-contained: the .NET runtime is included. Windows 10 or Windows 11, 64-bit, is required.

## How it works

- A short press of the selected shortcut switches to the next installed layout.
- Hold the shortcut to show the language popup.
- While it is open, release the second key and press it again to move through layouts. Release the first key to apply the selected layout and close the popup.
- The popup uses a snapshot of the desktop behind it for a soft background-blur effect. The snapshot is taken only when the popup opens, so it does not consume CPU while the app is idle.
- The app never installs or loads layouts: it uses the layouts configured in Windows, including the user’s selected layout variant such as Czech QWERTY.

## Settings

Open **Settings…** from the tray icon to configure:

- the shortcut: **Left Alt + Left Shift** or **Win + Space**;
- popup display on a long press;
- per-window layout memory;
- launch at Windows sign-in;
- the project link.

The tray menu also lets you enable or disable the switcher without exiting it.

## Build from source

Requirements: Windows 10/11, the .NET 8 SDK, and Inno Setup 6 or later to build the installer.

```powershell
dotnet test .\tests\SmartLayoutSwitcher.Core.Tests\SmartLayoutSwitcher.Core.Tests.csproj -c Release
dotnet publish .\src\SmartLayoutSwitcher.App\SmartLayoutSwitcher.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o .\artifacts\publish
iscc .\installer\SmartLayoutSwitcher.iss
```

The setup executable will be created in `artifacts\installer`.

## Project structure

```text
src/SmartLayoutSwitcher.App       WPF tray application, popup, settings
src/SmartLayoutSwitcher.Core      Hotkey and layout-selection logic
src/SmartLayoutSwitcher.Native    Windows keyboard/layout interop
tests/                            Unit tests for the core logic
installer/                        Inno Setup installer definition
```

## License

[MIT](LICENSE) © 2026 Vladimir Potapov.
