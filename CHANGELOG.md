# Changelog

All notable changes to Smart Layout Switcher are documented in this file.

## [1.0.21-test.1] - 2026-09-17

### Settings window
- Rebuilt the settings window from the new Figma design as a compact 504 × 517 dark interface.
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
