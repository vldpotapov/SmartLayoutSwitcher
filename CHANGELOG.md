# Changelog

All notable changes to Smart Layout Switcher are documented in this file.

## [1.0.1] - 2026-10-03

### Diagnostics
- Disabled file logging by default after reliability testing. Logging can still be explicitly enabled with `DebugLogEnabled` for troubleshooting.
- Disabled the diagnostic heartbeat and extra hotkey diagnostic queries when logging is off; no log writer thread or new log files are created.
- Preserved existing log files and keyboard switching behavior.
- Added regression tests for disabled logging, existing log preservation and opt-in logging.

## [1.0.0] - 2026-10-02

### First public release
- Mac-style keyboard layout switching for Windows with a rounded, blurred layout picker.
- A saved two-layout working pair, per-window layout memory and configurable Win + Space or Left Alt + Left Shift shortcuts.
- Selected-text correction with Win + Left Alt + Space, followed by switching to the corrected layout.
- Update notifications linking to the official release page and a self-contained Windows x64 installer.
- Includes the foreground-layout synchronization fix and bounded local diagnostic logging from development version 1.0.28.
- Reviewed public documentation and removed unused icon variants and personal assistant configuration from the tracked project.

Public versioning starts at 1.0.0. Users of numerically higher development builds must install this release manually. The entries below document the development history, not the public version sequence.

## [1.0.28] - 2026-10-02

### Hotkey reliability
- Reset unfinished shortcut state after Windows session changes and system resume.
- Recover automatically when a lost key-up would otherwise consume the next shortcut press.
- Synchronize the active foreground window and its real keyboard layout before every toggle, preventing the first shortcut after inactivity from targeting an already-active layout.
- Ignore delayed foreground-window events and confirm internal switches even when the reported layout has not changed.

### Diagnostics
- Added privacy-safe, bounded diagnostic logging for hotkey state, foreground layouts, session transitions and switch confirmation without recording typed text or clipboard contents.

## [1.0.27] - 2026-09-21

### Installer
- Fixed the launch checkbox on the final wizard page being clipped on the left edge; it now aligns with the page body text at every DPI scaling.
- Removed the Russian language; the installer is English-only.
- Reduced the installer size to about 53 MB by excluding debug symbols and satellite resource assemblies.

## [1.0.26] - 2026-09-21

### Settings window
- Widened the About section actions so update-related labels fit without clipping.
- Shortened the available-update status to a compact version label.

## [1.0.25] - 2026-09-21

### Installer
- Fixed Setup hanging while replacing an older or partially installed build that could not start because its .NET runtime was unavailable.
- Setup now terminates only the existing Smart Layout Switcher process directly instead of launching the old executable and waiting on a hidden runtime-error dialog.

## [1.0.24] - 2026-09-21

### Reliability
- The layout pair is now saved and restored across restarts. On first launch, the first two layouts in the Windows input-language order are used automatically.
- If a saved layout is no longer installed, the pair safely falls back to the first two available layouts instead of becoming unavailable.

### Interface
- The tray-menu shadow now has an unclipped transparent canvas, so it can render around the full menu rather than only near rounded corners.
- The full top strip of the Settings window can now be dragged, including the empty space around the header.

## [1.0.23] - 2026-09-20

### Tray icon
- Fixed the update dot on the tray icon: it now renders as a full circle sticking out beyond the icon's corner, matching the Figma design, instead of being clipped by the bitmap edge.

## [1.0.22] - 2026-09-20

### Tray menu
- Redesigned the tray menu to match the Figma design: dark surface with rounded corners, a 1px border and a soft drop shadow.
- Menu rows show a #3B3B3B hover state only on interactive items (Settings, Quit); the header and status rows stay inactive.
- Replaced the standard Windows menu appearance with custom dark styling and removed the separators.
- Switched the tray icons to the dark #999999 variants (switch, general, quit) at 18 × 18 px.

## [1.0.21] - 2026-09-18

### Settings window
- Rebuilt the settings window from the new Figma design as a compact 504 × 517 dark interface.
- Section outlines now render as a visible 1px border; the previous 0.5px border disappeared at 100% display scaling.
- Fixed the Cancel and Save buttons being clipped at the bottom, and card content being squeezed by the 1px card borders.
- Added dedicated dark settings icons without changing the icons used by the Windows tray menu.
- Preserved the existing settings, shortcuts, update status, GitHub action, and dynamic version display.

### Local development
- Added repository instructions and a guarded local OpenCode configuration for Qwen3 4B Instruct.

## [1.0.20] - 2026-09-17

### Settings window
- Redesigned the settings window to match the new design: a header with the app icon, title and a close button, five sections (Enable, General, Switch, Text Fix, About), and updated typography and colors.
- The window can now be moved by dragging its header.
- The About section shows the update status, a GitHub link and a "Check for updates" button with the new styling.

### Tray menu
- The tray menu now uses the standard Windows menu appearance with correctly sized icons.

## [1.0.19] - 2026-09-15

- Added selected-text layout correction: select text typed in the wrong layout and press **Win + Left Alt + Space** to remap it to the other layout in the active pair.
- Opening the release page in the browser instead of downloading updates.
- Cached background update checks with a turquoise dot on the tray icon when an update is ready.
- Updated the project description and installer.
