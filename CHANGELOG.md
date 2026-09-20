# Changelog

All notable changes to Smart Layout Switcher are documented in this file.

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
