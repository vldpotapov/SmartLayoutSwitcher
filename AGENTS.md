# Repository instructions

## Project overview

- This repository contains Smart Layout Switcher, a Windows-only .NET 8 application.
- `src/SmartLayoutSwitcher.App` is the WPF tray application and UI.
- `src/SmartLayoutSwitcher.Core` contains platform-independent hotkey and layout-selection logic.
- `src/SmartLayoutSwitcher.Native` contains Windows keyboard and layout interop.
- `tests/SmartLayoutSwitcher.Core.Tests` contains the xUnit test suite for the core logic.

## Working rules

- Keep changes limited to the files required by the current task.
- Read the relevant implementation and tests before editing.
- Preserve existing behavior unless the task explicitly changes it.
- Do not modify `Modelfile` or `opencode.json` unless the task explicitly concerns local-model configuration; they may contain uncommitted user experiments.
- Do not change application versions, `CHANGELOG.md`, installer files, release artifacts, signing configuration, startup/registry behavior, or dependencies unless explicitly requested.
- Do not commit, push, reset, clean, or discard user changes.
- Do not launch, install, or uninstall the application unless explicitly requested.
- Avoid unrelated formatting and broad refactors.

## Verification

- For core logic changes, run:
  `dotnet test .\tests\SmartLayoutSwitcher.Core.Tests\SmartLayoutSwitcher.Core.Tests.csproj -c Release`
- For application, native interop, project-file, or XAML changes, also run:
  `dotnet build .\SmartLayoutSwitcher.sln -c Release`
- Run `git diff --check` before reporting completion.
- If a required command cannot run, report the exact command and error. Do not claim success from code inspection alone.

## Result format

Report:

1. a concise summary of the change;
2. files modified;
3. verification commands and results;
4. remaining issues, assumptions, or manual checks.
