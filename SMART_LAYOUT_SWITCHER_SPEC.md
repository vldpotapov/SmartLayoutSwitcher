# Windows Smart Layout Switcher — Product & Technical Specification

## 1. Назначение документа

Этот документ описывает небольшую Windows-утилиту для переключения языковых раскладок клавиатуры.

Документ должен быть достаточным контекстом для Codex, Claude Code, OpenCode или другого AI coding agent, чтобы он мог:

1. понять пользовательскую задачу;
2. выбрать подходящую архитектуру;
3. создать рабочий MVP;
4. протестировать поведение;
5. постепенно добавить дополнительные функции без изменения основной логики.

Главный приоритет проекта — **предсказуемое и быстрое переключение между двумя последними использованными раскладками**, максимально близкое по ощущению к macOS.

---

# 2. Контекст пользователя

На Windows установлены три языка / раскладки:

- English — EN
- Russian — RU
- Czech — CZ

Сейчас Windows по стандартному хоткею циклически перебирает все доступные языки:

`EN → RU → CZ → EN`

Такое поведение неудобно.

Основной сценарий пользователя почти всегда связан только с двумя языками одновременно, например:

`RU ↔ EN`

При этом третий язык, Czech, нужен периодически.

На macOS переключение ощущается удобнее: пользователь может быстро работать между последними используемыми языками, не проходя полный цикл всех установленных раскладок.

Цель проекта — реализовать похожее поведение на Windows.

---

# 3. Главная цель

Создать маленькую Windows tray utility, которая:

- отслеживает используемые языковые раскладки;
- запоминает **две последние реально использованные раскладки**;
- по короткому нажатию `Left Alt + Left Shift` переключает только между ними;
- позволяет при необходимости быстро выбрать третий язык;
- корректно работает с Czech layout и `AltGr`;
- не конфликтует со стандартным Windows language switcher;
- работает в фоне с минимальным потреблением ресурсов.

---

# 4. Ключевой UX

## 4.1 Основное переключение

Пусть последние использованные раскладки:

- RU
- EN

Тогда:

`Left Alt + Left Shift`

должно работать так:

`RU → EN → RU → EN → RU`

CZ при этом в цикл **не входит**.

---

## 4.2 Изменение пары последних языков

Если пользователь вручную или через popup выбирает CZ, например:

`RU → CZ`

то две последние реально использованные раскладки становятся:

- CZ
- RU

После этого:

`Left Alt + Left Shift`

должно работать:

`CZ ↔ RU`

Если затем пользователь выбирает EN:

`CZ → EN`

новая пара:

`EN ↔ CZ`

Важно:

**не должно существовать жестко заданной пары RU/EN.<br>
Всегда используются именно две последние реально выбранные раскладки.**

---

# 5. Требования к горячей клавише

Основной хоткей:

`Left Alt + Left Shift`

Требования:

- учитывать именно левый Alt;
- учитывать именно левый Shift;
- `Right Alt` не должен вызывать переключение;
- `Right Alt + Shift` также не должен вызывать переключение;
- `AltGr` должен продолжать нормально работать в Czech layout;
- не должно происходить двойного переключения;
- нажатие должно отрабатываться только один раз до отпускания клавиш.

Особенно важно:

в чешской раскладке `Right Alt` используется как `AltGr`, поэтому глобальный перехват любого Alt недопустим.

---

# 6. Short press и Long press

Желаемое финальное поведение:

## Short press

Короткое нажатие:

`Left Alt + Left Shift`

переключает:

`lastLayout ↔ previousLayout`

Целевое время короткого нажатия:

примерно `< 500–700 ms`

Точный threshold должен быть вынесен в настройку / константу.

Рекомендуемое значение по умолчанию:

`600 ms`

---

## Long press

Удержание:

`Left Alt + Left Shift`

дольше threshold:

показывает небольшой overlay / popup со всеми доступными раскладками.

Пример:

```text
┌────────────────────────┐
│   EN     RU     CZ     │
│          ●             │
└────────────────────────┘
```

Требования к popup:

- появляется рядом с центром экрана или около текущего фокуса;
- находится поверх остальных окон;
- не крадет фокус у текущего приложения без необходимости;
- закрывается после выбора;
- закрывается по `Esc`;
- желательно позволять выбрать язык клавишами;
- после выбора новая раскладка становится `lastLayout`;
- предыдущая активная раскладка становится `previousLayout`.

Popup относится к следующему этапу после MVP.

---

# 7. Поведение при переключении приложений

Желательная функция:

**per-window layout memory**

Программа должна запоминать последнюю раскладку для каждого окна.

Пример:

Chrome:

`EN`

Telegram:

`RU`

Figma:

`EN`

Если пользователь:

1. работает в Chrome на EN;
2. переходит в Telegram и использует RU;
3. возвращается в Chrome;

программа может автоматически восстановить EN.

---

# 8. Что считать окном

Для первой реализации рекомендуется привязывать layout memory к:

- HWND активного top-level окна;

или, если это окажется нестабильно:

- executable/process name + HWND.

Не следует сразу создавать сложную систему профилей приложений.

Основной принцип:

**сначала надежность, потом дополнительные возможности.**

---

# 9. Приоритет функций

## P0 — обязательно для MVP

1. Приложение запускается на Windows.
2. Определяет текущую раскладку.
3. Получает список доступных раскладок.
4. Отслеживает изменения активной раскладки.
5. Хранит две последние реально использованные раскладки.
6. `Left Alt + Left Shift` переключает только между этими двумя.
7. Right Alt / AltGr не ломается.
8. Не возникает двойного переключения.
9. Стандартный Windows Alt+Shift не вмешивается.
10. Приложение может работать продолжительное время без утечки hooks/resources.

---

## P1

1. System tray icon.
2. Простое контекстное меню:
   - Enabled / Disabled
   - Current layout
   - Exit
3. Autostart with Windows.
4. Per-window layout memory.
5. Logging / diagnostics.

---

## P2

1. Long press.
2. Popup выбора раскладки.
3. Настраиваемый threshold long press.
4. Настраиваемый hotkey.
5. UI настроек.

---

## P3 — optional

1. Profiles per application.
2. Ignore-list приложений.
3. Экспорт / импорт настроек.
4. Portable mode.
5. Поддержка arbitrary количества языков.
6. Кастомные правила, например:
   - Browser → EN
   - Messenger → RU

---

# 10. Что НЕ нужно делать

На первом этапе НЕ нужно:

- создавать сложный красивый UI;
- использовать Electron;
- использовать web stack;
- делать cloud sync;
- делать аккаунты;
- добавлять telemetry;
- добавлять installer до рабочего MVP;
- hardcode конкретные Windows language IDs;
- ограничивать приложение только RU / EN / CZ.

Хотя пользователь сейчас использует RU, EN и CZ, архитектура должна работать с любыми установленными Windows keyboard layouts.

---

# 11. Предпочтительный стек

Предпочтительная реализация:

- C#
- .NET 8 или актуальный стабильный .NET desktop runtime
- Win32 API / Windows hooks
- WinForms или WPF только там, где нужен tray / popup

Предпочтение для MVP:

**C# + .NET + минимальный WinForms host + Win32 interop**

Причины:

- маленький executable;
- хороший доступ к Win32;
- удобно реализовать tray;
- удобно работать с keyboard hooks;
- удобно поддерживать;
- не нужен Node.js runtime.

Если AI-разработчик видит более простой и надежный native Windows подход, он может его предложить, но должен объяснить причину.

---

# 12. Windows API — ориентиры

AI-разработчик должен проверить актуальную документацию Microsoft перед окончательной реализацией.

Вероятно понадобятся API из следующих областей:

## Keyboard hooks

Для отслеживания нажатий:

- `SetWindowsHookEx`
- `WH_KEYBOARD_LL`
- `KBDLLHOOKSTRUCT`
- `CallNextHookEx`
- `UnhookWindowsHookEx`

Нужно различать:

- `VK_LMENU`
- `VK_RMENU`
- `VK_LSHIFT`
- `VK_RSHIFT`

Нельзя считать обычный `VK_MENU` достаточным для определения AltGr.

---

## Foreground window

Для отслеживания активного окна:

- `GetForegroundWindow`
- `GetWindowThreadProcessId`

Опционально:

- `SetWinEventHook`
- `EVENT_SYSTEM_FOREGROUND`

Предпочтительнее event-driven подход, чем постоянный polling, если он стабилен.

---

## Keyboard layout

Для определения активной раскладки:

- `GetKeyboardLayout`
- `GetKeyboardLayoutList`
- `GetWindowThreadProcessId`

Для изменения раскладки нужно исследовать надежный механизм, например:

- `PostMessage`
- `WM_INPUTLANGCHANGEREQUEST`

или другой корректный способ для активного foreground thread.

Не следует менять раскладку глобально методом, который дает непредсказуемое поведение между приложениями.

---

# 13. Важная техническая особенность Windows

Keyboard layout на Windows может быть связан не просто со всей системой, а с thread / foreground window.

Поэтому AI-разработчик не должен предполагать:

> существует одна глобальная текущая раскладка для всех приложений.

Нужно корректно получать layout именно для foreground window / thread.

---

# 14. Модель состояния

Минимальная модель:

```csharp
LayoutId? CurrentLayout;
LayoutId? LastLayout;
LayoutId? PreviousLayout;
```

При первом запуске:

```text
CurrentLayout = actual current Windows layout
LastLayout = CurrentLayout
PreviousLayout = null
```

После обнаружения новой реально активной раскладки:

```text
if newLayout != LastLayout:
    PreviousLayout = LastLayout
    LastLayout = newLayout
```

При переключении хоткеем:

если `CurrentLayout == LastLayout`:

```text
target = PreviousLayout
```

иначе:

```text
target = LastLayout
```

Однако AI должен внимательно продумать состояние, чтобы программно инициированная смена раскладки не разрушала историю.

---

# 15. Важное различие: user switch vs programmatic switch

Нужно различать:

1. пользователь самостоятельно выбрал новый язык;
2. программа переключила язык между двумя сохраненными.

Пример:

история:

`RU, EN`

Хоткей переключает:

`RU → EN → RU`

Это НЕ должно приводить к постоянной перестановке истории таким образом, чтобы она ломалась.

История должна оставаться парой:

`RU / EN`

Пока пользователь не выберет третий язык, например CZ.

Поэтому желательно иметь примерно такую модель:

```text
PrimaryLayout
SecondaryLayout
CurrentLayout
IsInternalSwitch
```

или другую надежную state-machine.

AI должен обязательно написать тесты на это поведение.

---

# 16. Алгоритм обновления двух последних языков

Ожидаемая логика:

Пусть сохранена пара:

```text
A = RU
B = EN
```

Программное переключение между A и B:

```text
RU → EN
EN → RU
```

НЕ изменяет состав пары.

Если Windows сообщает новый layout:

```text
CZ
```

который не равен A и B:

новая пара формируется из:

```text
previous actual layout + CZ
```

Например:

```text
RU → CZ
```

результат:

```text
A = RU
B = CZ
```

Если было:

```text
EN → CZ
```

результат:

```text
A = EN
B = CZ
```

Порядок названий A/B неважен, если toggle остается корректным.

---

# 17. Дебаунс хоткея

Нужно обязательно защититься от:

- key repeat;
- нескольких hook events;
- нажатия modifier keys в разном порядке;
- повторной обработки до отпускания.

Пример состояния:

```text
LeftAltDown
LeftShiftDown
ComboTriggered
ComboStartTime
```

После первого trigger:

```text
ComboTriggered = true
```

Новое переключение разрешается только после отпускания комбинации.

---

# 18. Порядок нажатия клавиш

Должны работать оба варианта:

```text
Left Alt down
Left Shift down
```

и

```text
Left Shift down
Left Alt down
```

Комбинация считается активной, когда одновременно нажаты обе нужные клавиши.

---

# 19. Конфликт с Windows Alt+Shift

Перед тестированием приложения желательно отключить системный Windows shortcut для переключения языка через `Alt + Shift`.

Но приложение также должно по возможности перехватывать комбинацию так, чтобы Windows не выполняла второе переключение.

AI-разработчик должен явно проверить:

- нужно ли возвращать non-zero из low-level keyboard hook;
- какие события нужно suppress;
- не ломает ли suppression обычные Alt/Shift сценарии.

Нельзя блокировать:

- одиночный Left Alt;
- одиночный Left Shift;
- Right Alt / AltGr;
- другие комбинации с Alt и Shift.

---

# 20. AltGr

Это критически важный edge case.

В Czech layout:

`Right Alt`

используется как `AltGr`.

Windows иногда внутренне представляет AltGr как сочетание Ctrl + Alt.

Поэтому:

- нельзя реагировать на любой Alt;
- основной trigger только `VK_LMENU`;
- нужно протестировать набор чешских символов через Right Alt;
- программа не должна менять язык при использовании AltGr.

---

# 21. Tray application

После MVP добавить system tray.

Минимальное меню:

```text
Smart Layout Switcher
---------------------
Enabled ✓
Current: EN
Pair: EN ↔ RU
---------------------
Start with Windows
Settings
Exit
```

До появления settings screen пункты могут быть минимальными.

---

# 22. Autostart

Предпочтительно использовать один из стандартных Windows способов:

- Startup folder;
- Registry Run key;
- MSIX startup task — только если позже появится packaging.

Для простого desktop app допустим Registry Run key.

Autostart должен быть:

- опциональным;
- легко отключаемым;
- без admin privileges.

---

# 23. Конфигурация

В перспективе использовать простой JSON.

Пример:

```json
{
  "enabled": true,
  "shortPressMs": 600,
  "rememberPerWindow": true,
  "startWithWindows": true,
  "showPopupOnLongPress": true
}
```

Хранение:

```text
%APPDATA%/SmartLayoutSwitcher/settings.json
```

Не хранить настройки в директории приложения, если это установленная версия.

---

# 24. Logging

Добавить простой debug log.

Например:

```text
2026-09-12 20:15:01 Foreground changed: chrome.exe
2026-09-12 20:15:01 Current layout: EN-US
2026-09-12 20:15:04 Hotkey: LAlt + LShift
2026-09-12 20:15:04 Switching EN-US -> RU
```

Debug logging должен отключаться.

Нельзя писать в лог пользовательский текст или нажатые буквенные клавиши.

Программа не должна превращаться в keylogger.

---

# 25. Privacy & security

Программа может использовать keyboard hook только для определения управляющей комбинации.

Она НЕ должна:

- записывать пользовательский ввод;
- хранить нажатия клавиш;
- отправлять данные в интернет;
- иметь telemetry по умолчанию;
- требовать admin privileges без реальной необходимости.

Все данные остаются локально.

---

# 26. MVP — рекомендуемая структура проекта

```text
SmartLayoutSwitcher/
│
├── README.md
├── SPEC.md
│
├── src/
│   └── SmartLayoutSwitcher/
│       ├── Program.cs
│       ├── AppContext.cs
│       │
│       ├── Input/
│       │   ├── KeyboardHook.cs
│       │   └── HotkeyStateMachine.cs
│       │
│       ├── Layout/
│       │   ├── KeyboardLayoutService.cs
│       │   ├── LayoutHistory.cs
│       │   └── LayoutSwitcher.cs
│       │
│       ├── Windows/
│       │   └── ForegroundWindowTracker.cs
│       │
│       ├── Native/
│       │   └── NativeMethods.cs
│       │
│       └── Diagnostics/
│           └── Logger.cs
│
└── tests/
    └── SmartLayoutSwitcher.Tests/
        ├── LayoutHistoryTests.cs
        └── HotkeyStateMachineTests.cs
```

Названия могут отличаться.

Не следует создавать чрезмерно сложную архитектуру.

---

# 27. State-machine для hotkey

Желательно вынести обработку комбинации отдельно от Win32 hook.

Например:

```text
Idle
 ↓
FirstModifierDown
 ↓
ComboDown
 ↓
Triggered
 ↓
WaitForRelease
 ↓
Idle
```

Это позволит unit-test без Windows keyboard hook.

---

# 28. Unit tests

Обязательные тесты логики.

## Последние две раскладки

```text
Start EN
User chooses RU
Pair = EN/RU

Toggle
Current = EN

Toggle
Current = RU
```

---

## Третий язык

```text
Pair = EN/RU
Current = RU

User chooses CZ

Pair = RU/CZ

Toggle -> RU
Toggle -> CZ
```

---

## Повторная раскладка

```text
Current = EN
Windows reports EN again

history must not change
```

---

## Right Alt

```text
RAlt down
LShift down

No toggle
```

---

## Left Alt

```text
LAlt + LShift

Exactly one toggle
```

---

## Holding keys

```text
Hold LAlt + LShift for 2 seconds

MVP without long press:
only one toggle

Final version:
long press event once
short press must not also fire
```

---

# 29. Manual test checklist

Проверить:

### EN ↔ RU

- [ ] EN → RU
- [ ] RU → EN
- [ ] 20 быстрых переключений подряд

### RU ↔ CZ

- [ ] вручную выбрать CZ
- [ ] проверить формирование новой пары
- [ ] переключить 10 раз

### EN ↔ CZ

- [ ] вручную сформировать пару
- [ ] проверить toggle

### AltGr

В Czech layout:

- [ ] использовать Right Alt для чешских символов
- [ ] язык не переключается
- [ ] символы вводятся нормально

### Applications

Проверить минимум:

- [ ] Chrome
- [ ] Windows Explorer
- [ ] VS Code
- [ ] Figma Desktop / browser
- [ ] Telegram / messenger
- [ ] Notepad

### Rapid application switching

- [ ] Alt+Tab между приложениями
- [ ] hotkey сразу после Alt+Tab
- [ ] отсутствие случайного переключения

---

# 30. Performance

Приложение должно:

- использовать минимальный CPU в idle;
- не использовать постоянный aggressive polling;
- не создавать десятки background threads;
- корректно освобождать keyboard hooks;
- нормально завершаться через tray → Exit.

Целевой idle CPU:

практически `0%`.

---

# 31. Startup behavior

После запуска:

1. определить foreground window;
2. определить его current layout;
3. получить installed layouts;
4. инициализировать history.

Если сохраненная предыдущая пара существует в settings/state и обе раскладки все еще установлены, ее можно восстановить.

Но persistence истории не является обязательной частью MVP.

---

# 32. Поведение при наличии только одной раскладки

Если установлена одна раскладка:

- hotkey ничего не делает;
- приложение не падает.

Если установлено две:

- работает обычный toggle.

Если установлено три и больше:

- работает логика двух последних.

---

# 33. Удаление раскладки во время работы

Если layout из текущей пары больше недоступен:

- удалить его из state;
- выбрать доступный fallback;
- не падать.

Это edge case и может быть реализовано после MVP.

---

# 34. UI popup — будущий этап

Минималистичный дизайн:

```text
┌─────────────────────────────┐
│   ENG      RUS      CES     │
│    A        Б        Č      │
└─────────────────────────────┘
```

Визуальные требования:

- dark / neutral UI;
- компактный;
- без title bar;
- rounded corners допустимы;
- Windows 11 style;
- текущий язык визуально выделен;
- popup появляется быстро, без анимаций длиннее ~100–150 ms.

UI не является главным приоритетом.

---

# 35. Возможная логика long press

Вариант:

```text
LAlt down
LShift down
start timer

if released before 600ms:
    toggle last two

if still held after 600ms:
    show layout selector
    DO NOT perform short toggle
```

Это предпочтительнее, чем сначала сделать toggle, а потом открыть popup.

---

# 36. Главное UX-правило

Пользователь никогда не должен думать:

> сколько раз мне сейчас нажать Alt+Shift, чтобы дойти до нужного языка?

Для обычной работы должно быть:

```text
один hotkey = другой из двух текущих рабочих языков
```

Третий и последующие языки доступны отдельно.

---

# 37. Definition of Done — MVP

MVP считается готовым только если:

1. Собирается из чистого clone по инструкции.
2. Запускается на Windows 10/11.
3. Видит установленные keyboard layouts.
4. Видит current layout foreground application.
5. `Left Alt + Left Shift` стабильно переключает между двумя последними.
6. CZ не входит в цикл, если не является одним из двух последних языков.
7. После ручного выбора CZ пара корректно обновляется.
8. `Right Alt / AltGr` полностью работоспособен.
9. Нет двойных переключений.
10. Нет заметной задержки при переключении.
11. Приложение не требует admin rights.
12. Есть README с build/run instructions.
13. Есть unit tests state logic.
14. Есть debug logging.
15. После 15–30 минут тестирования приложение не показывает нестабильного поведения.

---

# 38. Этапы реализации

## Phase 1 — Research / prototype

Проверить:

- получение текущего keyboard layout;
- переключение layout foreground window;
- low-level keyboard hook;
- корректное определение Left/Right modifiers.

Создать минимальный console prototype.

---

## Phase 2 — Core MVP

Реализовать:

- keyboard hook;
- hotkey state machine;
- layout tracking;
- two-last-layout history;
- switching;
- tests.

Без popup.

---

## Phase 3 — Windows integration

Добавить:

- foreground window events;
- tray;
- graceful shutdown;
- logging;
- autostart.

---

## Phase 4 — Per-window memory

Добавить:

- map `window/application → layout`;
- restoration on focus change;
- option enable/disable.

---

## Phase 5 — Long press UI

Добавить:

- long press detection;
- layout popup;
- keyboard selection;
- configurable threshold.

---

## Phase 6 — Packaging

Добавить:

- release build;
- single-file executable, если это надежно;
- version info;
- icon;
- optional installer.

---

# 39. Инструкция AI coding agent

Перед написанием большого количества кода:

1. Прочитай весь этот документ.
2. Изучи существующий repository.
3. Не меняй требования без необходимости.
4. Если Windows API имеет несколько вариантов реализации, выбери самый надежный и минимальный.
5. Проверь актуальную Microsoft documentation.
6. Сначала реализуй маленький working prototype.
7. После проверки prototype переходи к архитектуре приложения.
8. Не создавай unnecessary abstractions.
9. Core logic должна быть unit-testable без Win32 hooks.
10. После каждого этапа:
    - build;
    - run tests;
    - summarize result.
11. Не добавляй новые зависимости без причины.
12. Не используй AutoHotkey.
13. Не используй Electron.
14. Не требуй administrator privileges.
15. Не добавляй network functionality.

---

# 40. Первая задача для Codex / AI

Используй этот prompt после передачи документа:

```text
Read SPEC.md completely before changing anything.

We are starting Phase 1 of the project.

Your task is to build the smallest possible Windows prototype that proves three things:

1. We can correctly detect the keyboard layout used by the current foreground window.
2. We can switch that foreground window to another installed keyboard layout.
3. We can reliably detect only Left Alt + Left Shift without triggering on Right Alt / AltGr.

Use C#/.NET and native Windows APIs.

Do not build the tray UI, settings UI, long-press popup, autostart, or per-window memory yet.

Keep the code small and diagnostic.

Print/log:
- foreground HWND/process
- current layout
- installed layouts
- detected LAlt/LShift state
- target layout after a test switch

Add a short README section explaining exactly how to run and manually test the prototype.

Build the project and fix all compile errors before finishing.

After implementation, report:
- files created/changed
- Windows APIs used
- how layout detection works
- how switching works
- how AltGr is protected
- known limitations
- exact manual test steps
```

---

# 41. Финальное описание продукта в одной фразе

**Smart Layout Switcher — маленькая Windows-утилита, которая превращает переключение нескольких языков в быстрый toggle между двумя последними использованными раскладками, оставляя остальные языки доступными по отдельному действию.**
