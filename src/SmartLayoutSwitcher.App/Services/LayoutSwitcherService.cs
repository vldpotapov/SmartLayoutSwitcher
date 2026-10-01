using Microsoft.Win32;
using SmartLayoutSwitcher.App.Diagnostics;
using SmartLayoutSwitcher.App.Popup;
using SmartLayoutSwitcher.App.Settings;
using SmartLayoutSwitcher.Core;
using SmartLayoutSwitcher.Native;
using System.Diagnostics;
using System.Windows.Threading;

namespace SmartLayoutSwitcher.App.Services;

public sealed record SwitcherStatus(string CurrentCode, string PairText, bool Enabled);

/// <summary>
/// Wires the raw Win32 pieces (keyboard hook, foreground tracker, layout service)
/// into the Core state machines. Single-threaded expectations: hook + window events
/// arrive on the UI (Dispatcher) thread; the 300 ms layout poll runs on the thread pool.
/// All state transitions are guarded by <see cref="_gate"/>.
/// </summary>
public sealed class LayoutSwitcherService : IDisposable
{
    private const long StaleShortcutTimeoutMs = 2000;

    private readonly AppSettings _settings;
    private readonly Logger _log;
    private readonly KeyboardLayoutService _layouts = new();
    private readonly KeyboardHook _hook = new();
    private readonly ForegroundWindowTracker _foreground = new();
    private readonly HotkeyReactor _reactor = new();
    private readonly LayoutHistory _history = new();
    private readonly LongPressResolver _longPress;
    private readonly WindowLayoutMemory _memory = new();
    private readonly PopupController _popup;
    private readonly SelectedTextConversionService _selectedTextConverter = new();
    private readonly object _gate = new();
    private readonly System.Threading.Timer _pollTimer;
    private readonly DispatcherTimer _diagnosticTimer;

    private IReadOnlyList<LayoutInfo> _installed = Array.Empty<LayoutInfo>();

    private IntPtr _foregroundHwnd;
    private LayoutId _reportedLayout = LayoutId.Empty;

    private System.Threading.Timer? _longPressTimer;
    private bool _longPressRaised;

    // The first modifier can reach Windows before the second key completes our
    // shortcut. Mask only its final release when the pair was actually handled,
    // otherwise an ordinary Alt opens the app menu and an ordinary Win opens Start.
    private bool _maskPrimaryRelease;
    private bool _primaryDownPassedThrough;
    private long _lastShortcutEventMs;
    private long _diagnosticEventSequence;

    // Win + Left Alt + Space is intentionally handled separately from the
    // layout-switching reactor: it must take precedence over Win + Space.
    private bool _textConversionActive;
    private bool _textConversionWinDown;
    private bool _textConversionAltDown;
    private bool _textConversionSpaceDown;
    private bool _textConversionScheduled;

    private bool _isInternalSwitch;
    private LayoutId _internalTarget = LayoutId.Empty;
    private long _internalDeadline;

    private volatile bool _disposed;

    public event Action<SwitcherStatus>? StatusChanged;

    public bool Enabled
    {
        get => _settings.Enabled;
        set
        {
            if (_settings.Enabled == value)
                return;
            _settings.Enabled = value;
            _log.Info(value ? "Enabled." : "Disabled.");
            RaiseStatus();
        }
    }

    public LayoutSwitcherService(AppSettings settings, Logger log)
    {
        _settings = settings;
        _log = log;
        _longPress = new LongPressResolver(settings.ShortPressMs > 0 ? settings.ShortPressMs : 600);
        _popup = new PopupController();
        _popup.LayoutChosen += OnPopupLayoutChosen;

        _reactor.ComboEngaged += _ => OnComboEngaged();
        _reactor.ComboReleased += d => OnComboReleased(d);
        _reactor.CycleStep += OnCycleStep;

        RefreshCatalog();
        var staleLayoutsRemoved = _layouts.UnloadRedundantBaseLayouts();
        if (staleLayoutsRemoved > 0)
            _log.Info($"Removed {staleLayoutsRemoved} redundant base keyboard layout(s).");
        // The cleanup above can change the set of loaded handles. Keep the
        // catalog used by QueryLayout and the initial-pair logic in sync.
        RefreshCatalog();
        var current = _layouts.GetForegroundLayoutInfo();
        if (current is not null)
        {
            EnsurePair(current.Id);
            _reportedLayout = current.Id;
            _log.Info($"Initial layout: {current.Id} {current.LanguageName}");
        }
        else
        {
            _log.Warn("Could not determine the initial foreground layout.");
            EnsurePair(LayoutId.Empty);
        }

        _foreground.ForegroundChanged += OnForegroundChanged;
        _foreground.Install();
        _log.Info($"Foreground tracker installed: {_foreground.IsInstalled}");

        _hook.KeyEvent += OnKey;
        _hook.Install();
        _log.Info($"Keyboard hook installed: {_hook.IsInstalled}");
        _log.Diagnostic($"Hook installation: installed={_hook.IsInstalled}, installThread={_hook.InstallThreadId}.");

        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        _pollTimer = new System.Threading.Timer(_ => OnPoll(), null, 300, 300);

        _diagnosticTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMinutes(1),
        };
        _diagnosticTimer.Tick += OnDiagnosticHeartbeat;
        _diagnosticTimer.Start();
        WriteDiagnosticSnapshot("service-ready");
    }

    public void PublishStatus() => RaiseStatus();

    // ---------------------------------------------------------------- key handling

    private KeyReaction OnKey(HookKeyEventArgs args)
    {
        // The synthetic menu-mask event must flow to Windows unchanged.
        if (args.IsInjected)
            return KeyReaction.PassThrough;

        var now = Environment.TickCount64;
        var usesWinSpace = _settings.Hotkey == HotkeyMode.WinSpace;
        var isPrimary = usesWinSpace
            ? args.VkCode is NativeMethods.VK_LWIN or NativeMethods.VK_RWIN
            : IsPhysicalLeftAlt(args);
        var isSecondary = usesWinSpace
            ? args.VkCode == NativeMethods.VK_SPACE
            : args.VkCode == NativeMethods.VK_LSHIFT;
        var shouldDiagnose = ShouldDiagnoseHotkeyEvent(usesWinSpace, isPrimary, isSecondary);
        var diagnosticId = shouldDiagnose ? ++_diagnosticEventSequence : 0;
        var diagnosticStarted = shouldDiagnose ? Stopwatch.GetTimestamp() : 0;
        if (shouldDiagnose)
            LogHotkeyEvent(diagnosticId, "input", args, KeyReaction.PassThrough, now);

        // Disabled means fully transparent to normal typing and the user's own
        // Windows language shortcut.
        if (!_settings.Enabled && !_popup.IsOpen)
        {
            if (shouldDiagnose)
                LogHotkeyEvent(diagnosticId, "disabled-pass", args, KeyReaction.PassThrough, now,
                    ElapsedMicroseconds(diagnosticStarted));
            return KeyReaction.PassThrough;
        }

        if (TryHandleSelectedTextConversionShortcut(args, out var textConversionReaction))
        {
            if (shouldDiagnose)
                LogHotkeyEvent(diagnosticId, "text-conversion", args, textConversionReaction, now,
                    ElapsedMicroseconds(diagnosticStarted));
            return textConversionReaction;
        }

        var timeSinceLastShortcutEvent = now - _lastShortcutEventMs;
        if (isPrimary || isSecondary)
            _lastShortcutEventMs = now;

        RecoverStaleShortcutState(
            args, usesWinSpace, isPrimary, isSecondary, timeSinceLastShortcutEvent);

        KeyReaction reaction;
        if (isPrimary)
        {
            if (args.IsKeyUp)
            {
                if (_maskPrimaryRelease && _primaryDownPassedThrough)
                    SendMenuMask("before primary release");

                reaction = _reactor.PrimaryUp(now);
                _maskPrimaryRelease = false;
                _primaryDownPassedThrough = false;
            }
            else
            {
                reaction = _reactor.PrimaryDown(now);
                if (reaction == KeyReaction.PassThrough)
                    _primaryDownPassedThrough = true;
            }
        }
        else if (isSecondary)
            reaction = args.IsKeyUp ? _reactor.SecondaryUp(now) : _reactor.SecondaryDown(now);
        else if (_popup.IsOpen)
            reaction = RoutePopupKey(args);
        else
            reaction = args.IsKeyUp ? _reactor.OtherKeyUp() : _reactor.OtherKeyDown();

        if (IsPhysicalRightAlt(args) || args.VkCode == NativeMethods.VK_RSHIFT)
            _log.Debug($"Right modifier 0x{args.VkCode:X2} passed through.");

        if (shouldDiagnose)
            LogHotkeyEvent(diagnosticId, "result", args, reaction, now,
                ElapsedMicroseconds(diagnosticStarted));

        return reaction;
    }

    private bool ShouldDiagnoseHotkeyEvent(bool usesWinSpace, bool isPrimary, bool isSecondary)
    {
        if (isPrimary)
            return true;
        if (!isSecondary)
            return false;

        return _reactor.HasPendingState ||
               (usesWinSpace ? NativeMethods.IsWinDown() : NativeMethods.IsLeftAltDown());
    }

    private void LogHotkeyEvent(
        long id,
        string phase,
        HookKeyEventArgs args,
        KeyReaction reaction,
        long now,
        long processingUs = -1)
    {
        var hwnd = CurrentHwnd();
        var layout = QueryLayout(hwnd);
        var engagedAge = _reactor.EngagedSinceMs is long engagedAt ? now - engagedAt : -1;
        _log.Diagnostic(
            $"Hotkey#{id} {phase}: mode={_settings.Hotkey}, key={DiagnosticKeyName(args.VkCode)}, " +
            $"event={(args.IsKeyUp ? "up" : "down")}, sys={args.IsSysKey}, ext={args.IsExtended}, " +
            $"reaction={reaction}, processingUs={processingUs}, enabled={_settings.Enabled}, hook={_hook.IsInstalled}, " +
            $"reactor[pending={_reactor.HasPendingState},engaged={_reactor.IsEngaged},ageMs={engagedAge}], " +
            $"physical[win={NativeMethods.IsWinDown()},space={NativeMethods.IsSpaceDown()}," +
            $"leftAlt={NativeMethods.IsLeftAltDown()},leftShift={NativeMethods.IsLeftShiftDown()}], " +
            $"popup={_popup.IsOpen}, mask={_maskPrimaryRelease}, foreground=0x{hwnd.ToInt64():X}, layout={layout}.");
    }

    private static long ElapsedMicroseconds(long startedTimestamp) =>
        (long)(Stopwatch.GetElapsedTime(startedTimestamp).TotalMilliseconds * 1000);

    private static string DiagnosticKeyName(int vkCode) => vkCode switch
    {
        NativeMethods.VK_LWIN => "LWin",
        NativeMethods.VK_RWIN => "RWin",
        NativeMethods.VK_SPACE => "Space",
        NativeMethods.VK_LMENU or NativeMethods.VK_MENU => "LAlt",
        NativeMethods.VK_RMENU => "RAlt",
        NativeMethods.VK_LSHIFT => "LShift",
        NativeMethods.VK_RSHIFT => "RShift",
        _ => $"0x{vkCode:X2}",
    };

    private void RecoverStaleShortcutState(
        HookKeyEventArgs args,
        bool usesWinSpace,
        bool isPrimary,
        bool isSecondary,
        long timeSinceLastShortcutEvent)
    {
        if (args.IsKeyUp || (!isPrimary && !isSecondary) || _popup.IsOpen ||
            !_reactor.HasPendingState || timeSinceLastShortcutEvent < StaleShortcutTimeoutMs)
            return;

        // At a fresh key-down, the other shortcut key must be physically down
        // only when this is still the same combo. If it is up while the reactor
        // still has pending state, Windows dropped a key-up (commonly across
        // lock/sleep), so recover before processing this new first key.
        var otherKeyDown = usesWinSpace
            ? isPrimary ? NativeMethods.IsSpaceDown() : NativeMethods.IsWinDown()
            : isPrimary ? NativeMethods.IsLeftShiftDown() : NativeMethods.IsLeftAltDown();

        if (!otherKeyDown)
            ResetShortcutState("stale key state detected");
    }

    private bool TryHandleSelectedTextConversionShortcut(HookKeyEventArgs args, out KeyReaction reaction)
    {
        if (_textConversionActive)
        {
            if (args.VkCode == NativeMethods.VK_SPACE)
            {
                if (args.IsKeyUp)
                {
                    _textConversionSpaceDown = false;
                    ScheduleSelectedTextConversionIfReleased();
                }

                reaction = KeyReaction.Suppress;
                return true;
            }

            if (args.VkCode is NativeMethods.VK_LWIN or NativeMethods.VK_RWIN)
            {
                if (args.IsKeyUp)
                {
                    _textConversionWinDown = false;
                    ScheduleSelectedTextConversionIfReleased();
                }

                // Win-down reached Windows before Space completed the shortcut;
                // its matching key-up must remain visible to avoid a stuck key.
                reaction = KeyReaction.PassThrough;
                return true;
            }

            if (IsPhysicalLeftAlt(args))
            {
                if (args.IsKeyUp)
                {
                    _textConversionAltDown = false;
                    ScheduleSelectedTextConversionIfReleased();
                }

                // The initial Alt-down was also delivered, so keep it balanced.
                reaction = KeyReaction.PassThrough;
                return true;
            }

            reaction = KeyReaction.PassThrough;
            return false;
        }

        if (!_settings.EnableSelectedTextConversion || args.IsKeyUp ||
            args.VkCode != NativeMethods.VK_SPACE || _popup.IsOpen || _reactor.IsEngaged ||
            !NativeMethods.IsWinDown() || !NativeMethods.IsLeftAltDown() ||
            NativeMethods.IsControlDown() || NativeMethods.IsShiftDown())
        {
            reaction = KeyReaction.PassThrough;
            return false;
        }

        // Both modifier downs have already reached Windows. Mark them as used
        // before they are released, reset a pending Win+Space sequence, and only
        // swallow Space (whose down never reached the foreground application).
        _reactor.Reset();
        StopLongPressTimer();
        _longPressRaised = false;
        _maskPrimaryRelease = false;
        _primaryDownPassedThrough = false;
        _textConversionActive = true;
        _textConversionWinDown = true;
        _textConversionAltDown = true;
        _textConversionSpaceDown = true;
        SendMenuMask("text conversion shortcut");
        _log.Info("Selected-text conversion shortcut engaged.");

        reaction = KeyReaction.Suppress;
        return true;
    }

    private void ScheduleSelectedTextConversionIfReleased()
    {
        if (_textConversionWinDown || _textConversionAltDown || _textConversionSpaceDown || _textConversionScheduled)
            return;

        _textConversionActive = false;
        _textConversionScheduled = true;
        Prepare(() => _ = ConvertSelectedTextAsync());
    }

    private async Task ConvertSelectedTextAsync()
    {
        // Let the final modifier key-up finish in the foreground application
        // before injecting Ctrl+C / Ctrl+V.
        await Task.Delay(35);

        IntPtr sourceHkl;
        IntPtr targetHkl;
        IntPtr hwnd;
        LayoutId target;
        lock (_gate)
        {
            _textConversionScheduled = false;
            if (_disposed || !_settings.EnableSelectedTextConversion)
                return;

            hwnd = CurrentHwnd();
            var current = QueryLayout(hwnd);
            if (hwnd == IntPtr.Zero || current.IsEmpty)
            {
                _log.Warn("Selected-text conversion skipped: the foreground layout is unavailable.");
                return;
            }

            if (_history.ApplyUserSelection(current))
                PersistPair();
            if (!_history.TryGetToggleTarget(out target) || !_layouts.TryGetLoadedLayoutHandle(target, out targetHkl))
            {
                _log.Info("Selected-text conversion skipped: choose two layouts first.");
                return;
            }

            sourceHkl = _layouts.GetLayoutHandle(hwnd);
            if (sourceHkl == IntPtr.Zero)
            {
                _log.Warn("Selected-text conversion skipped: source keyboard layout is unavailable.");
                return;
            }
        }

        var result = await _selectedTextConverter.ConvertAsync(sourceHkl, targetHkl);
        if (!result.Succeeded)
        {
            _log.Info($"Selected-text conversion skipped: {result.Message}");
            return;
        }

        var switched = SwitchToConvertedTextLayout(hwnd, target);
        _log.Info(switched
            ? $"Selected text converted ({result.ChangedCharacters} character(s) changed) and layout switched to {target}."
            : $"Selected text converted ({result.ChangedCharacters} character(s) changed), but the layout could not be switched.");
    }

    private bool SwitchToConvertedTextLayout(IntPtr hwnd, LayoutId target)
    {
        lock (_gate)
        {
            if (_disposed || hwnd == IntPtr.Zero || target.IsEmpty)
                return false;

            // The text converter works in the foreground window. Do not change
            // a layout in a window the user has already left while it was pasting.
            if (CurrentHwnd() != hwnd)
                return false;

            if (!_layouts.SwitchTo(hwnd, target))
                return false;

            _history.ApplyInternalToggle();
            _memory.Remember(hwnd, target);
            BeginInternalSwitch(target);
            RaiseStatus();
            return true;
        }
    }

    private static bool IsPhysicalLeftAlt(HookKeyEventArgs args) =>
        !args.IsExtended && args.VkCode is (NativeMethods.VK_LMENU or NativeMethods.VK_MENU);

    private static bool IsPhysicalRightAlt(HookKeyEventArgs args) =>
        args.IsExtended && args.VkCode is (NativeMethods.VK_RMENU or NativeMethods.VK_LMENU or NativeMethods.VK_MENU);

    private void SendMenuMask(string phase)
    {
        // A real Ctrl held by the user already prevents Alt's menu activation,
        // so sending a synthetic Ctrl in that case could only disturb it.
        if (NativeMethods.IsControlDown())
        {
            _log.Debug($"Menu mask skipped ({phase}): Ctrl is physically held.");
            return;
        }

        NativeMethods.SendMenuMaskKeyStroke();
        _log.Debug($"Menu mask sent ({phase}).");
    }

    private KeyReaction RoutePopupKey(HookKeyEventArgs args)
    {
        if (args.IsKeyUp)
            return KeyReaction.PassThrough;

        switch (args.VkCode)
        {
            case 0x1B: // ESC -> cancel the popup without applying anything
                OnPopupCancel();
                return KeyReaction.Suppress;
            default:
                return KeyReaction.PassThrough;
        }
    }

    private void OnComboEngaged()
    {
        lock (_gate)
        {
            if (_disposed || _popup.IsOpen)
                return;

            _log.Diagnostic("Layout shortcut engaged.");
            _maskPrimaryRelease = true;
            // Send the mask while the primary Alt/Win key is still physically
            // down. Sending it only at key-up is too late for some Windows 11
            // shell and menu implementations.
            SendMenuMask("combo engaged");
            _longPressRaised = false;

            if (_settings.ShowPopupOnLongPress)
            {
                StopLongPressTimer();
                _longPressTimer = new System.Threading.Timer(
                    _ => OnLongPressTick(), null, _longPress.ThresholdMs, System.Threading.Timeout.Infinite);
            }
        }
    }

    private void OnLongPressTick()
    {
        if (_disposed)
            return;

        // Timer callbacks use a worker thread. Popup and hook state belong to
        // the Dispatcher thread, so perform this transition there.
        Prepare(() =>
        {
            lock (_gate)
            {
                if (_disposed || _longPressRaised || !_reactor.IsEngaged || _popup.IsOpen)
                    return;

                _longPressRaised = true;
                _log.Info("Long press detected -> showing layout popup (hold LAlt, tap LShift to cycle, release LAlt to apply).");
                RefreshCatalog();
                EnsurePair(QueryLayout(CurrentHwnd()));
                var current = _history.Current;
                var paired = _history.TryGetToggleTarget(out var toggleTarget)
                    ? toggleTarget
                    : LayoutId.Empty;
                _reactor.EnterCycleMode();
                _popup.Show(_installed, current, paired);
            }
        });
    }

    private void OnComboReleased(long durationMs)
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            StopLongPressTimer();
            _log.Diagnostic($"Layout shortcut released after {durationMs} ms; longPress={_longPressRaised}, popup={_popup.IsOpen}.");

            if (_longPressRaised || _popup.IsOpen)
            {
                // Long-press finished by releasing the modifiers: apply the layout the
                // selection stopped on. No Enter, no arrows.
                _longPressRaised = false;
                _reactor.ExitCycleMode();
                if (_popup.IsOpen)
                    _popup.Confirm();
                return;
            }

            if (!_settings.Enabled)
            {
                _log.Debug("Combo released but the service is disabled.");
                return;
            }

            PerformShortPressToggle(durationMs);
        }
    }

    private void PerformShortPressToggle(long durationMs)
    {
        var hwnd = CurrentHwnd();
        if (hwnd == IntPtr.Zero)
        {
            _log.Warn("Short press could not switch: no foreground window.");
            return;
        }

        var actual = QueryLayout(hwnd);
        EnsurePair(actual);
        SynchronizeBeforeToggle(hwnd, actual);

        if (!_history.TryGetToggleTarget(out var target))
        {
            _log.Info($"Short press ({durationMs} ms) but no switch pair available.");
            return;
        }

        _log.Info($"Short press ({durationMs} ms): switching {_history.Current} -> {target}");
        if (!_layouts.SwitchTo(hwnd, target))
        {
            _log.Warn($"Could not request switch to {target}; state was left unchanged.");
            return;
        }

        _history.ApplyInternalToggle();
        BeginInternalSwitch(target);
        RaiseStatus();
    }

    private void OnPopupLayoutChosen(LayoutId chosen)
    {
        lock (_gate)
        {
            if (_disposed || chosen.IsEmpty)
                return;

            _log.Info($"Popup selection: {chosen}");

            var hwnd = CurrentHwnd();
            if (hwnd == IntPtr.Zero)
            {
                _log.Warn("Could not apply popup selection: no foreground window.");
                return;
            }

            if (!_layouts.SwitchTo(hwnd, chosen))
            {
                _log.Warn($"Could not request popup switch to {chosen}; state was left unchanged.");
                return;
            }

            _history.ApplyPopupSelection(chosen);
            PersistPair();
            _memory.Remember(hwnd, chosen);
            BeginInternalSwitch(chosen);
            RaiseStatus();
        }
    }

    private void OnCycleStep()
    {
        lock (_gate)
        {
            if (_disposed || !_popup.IsOpen)
                return;

            _log.Debug("Popup cycle step: next layout highlighted.");
            _popup.Move(+1);
        }
    }

    private void OnPopupCancel()
    {
        _reactor.ExitCycleMode();
        _popup.Hide();
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        _log.Diagnostic($"Session event: {e.Reason}.");
        Prepare(() => ResetShortcutState($"session {e.Reason}"));
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        _log.Diagnostic($"Power event: {e.Mode}.");
        if (e.Mode == PowerModes.Resume)
            Prepare(() => ResetShortcutState("system resume"));
    }

    private void ResetShortcutState(string reason)
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _reactor.Reset();
            StopLongPressTimer();
            _longPressRaised = false;
            _maskPrimaryRelease = false;
            _primaryDownPassedThrough = false;

            if (_popup.IsOpen)
                _popup.Hide();

            _log.Info($"Shortcut state reset: {reason}.");
        }
    }

    // ------------------------------------------------------------- foreground / poll

    private void OnForegroundChanged(IntPtr hwnd)
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            // WinEvent callbacks are delivered out of process and can arrive
            // after another window has already taken focus. Never replace the
            // cached foreground with such a stale HWND.
            var actualForeground = CurrentHwnd();
            if (actualForeground != IntPtr.Zero && hwnd != actualForeground)
            {
                _log.Diagnostic($"Stale foreground event ignored: event=0x{hwnd.ToInt64():X}, " +
                                $"actual=0x{actualForeground.ToInt64():X}.");
                hwnd = actualForeground;
            }

            if (hwnd == _foregroundHwnd)
                return;

            var previous = QueryLayout(_foregroundHwnd);
            if (_foregroundHwnd != IntPtr.Zero && !previous.IsEmpty)
                _memory.Remember(_foregroundHwnd, previous);

            _foregroundHwnd = hwnd;
            _log.Info($"Foreground changed: {_layouts.GetForegroundProcessName()} (0x{hwnd.ToInt64():X})");

            if (_settings.RememberPerWindow && _memory.TryGet(hwnd, out var remembered))
            {
                var actual = QueryLayout(hwnd);
                if (remembered != actual)
                {
                    _log.Info($"Restoring remembered layout {remembered}.");
                    BeginInternalSwitch(remembered);
                    _layouts.SwitchTo(hwnd, remembered);
                }
            }

            RaiseStatus();
        }
    }

    private void OnPoll()
    {
        if (_disposed)
            return;

        lock (_gate)
        {
            if (_disposed)
                return;

            var now = Environment.TickCount64;
            var hwnd = CurrentHwnd();
            if (hwnd == IntPtr.Zero)
                hwnd = _foregroundHwnd;
            var layout = QueryLayout(hwnd);
            if (layout.IsEmpty)
                return;

            EnsurePair(layout);

            var completedInternalSwitch = _isInternalSwitch && layout == _internalTarget;
            if (completedInternalSwitch)
            {
                _isInternalSwitch = false;
                _memory.Remember(hwnd, layout);
                _log.Debug($"Internal switch completed -> {layout}");
            }
            else if (_isInternalSwitch && now > _internalDeadline)
            {
                _log.Warn($"Layout switch confirmation timed out: target={_internalTarget}, " +
                          $"reported={_reportedLayout}, actual={layout}, foreground=0x{hwnd.ToInt64():X}.");
                _isInternalSwitch = false;
            }
            else if (_isInternalSwitch)
            {
                return;
            }

            var reportedChanged = layout != _reportedLayout;
            var historyChanged = layout != _history.Current;
            _reportedLayout = layout;

            if (completedInternalSwitch && historyChanged)
            {
                _history.ApplyInternalSelection(layout);
            }
            else if (historyChanged)
            {
                var reformed = _history.ApplyUserSelection(layout);
                if (reformed)
                    PersistPair();
                _log.Info(reformed
                    ? $"User selected a new layout {layout}; pair reformed."
                    : $"Layout changed to {layout}.");
                if (hwnd != IntPtr.Zero)
                    _memory.Remember(hwnd, layout);
            }

            if (reportedChanged || historyChanged || completedInternalSwitch)
                RaiseStatus();
        }
    }

    // --------------------------------------------------------------------- helpers

    private static IntPtr CurrentHwnd() => NativeMethods.GetForegroundWindow();

    private LayoutId QueryLayout(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return LayoutId.Empty;

        var tid = NativeMethods.GetWindowThreadProcessId(hwnd, out _);
        var hkl = tid == 0 ? IntPtr.Zero : NativeMethods.GetKeyboardLayout(tid);
        if (hkl == IntPtr.Zero)
            return LayoutId.Empty;

        var preloadId = KeyboardLayoutService.MatchToCatalog((uint)hkl.ToInt64(), _installed);
        return new LayoutId(preloadId.ToString("X8"));
    }

    private void RefreshCatalog() => _installed = _layouts.GetInstalledLayouts();

    /// <summary>
    /// Restores the user's last valid pair. On a first run, or after the user
    /// removes one of those layouts in Windows, the first two layouts in the
    /// Windows input-language order become the new pair.
    /// </summary>
    private bool EnsurePair(LayoutId current)
    {
        if (_history.HasPair)
            return true;

        var installed = _installed
            .Select(layout => layout.Id)
            .Where(layout => !layout.IsEmpty)
            .Distinct()
            .ToArray();

        if (TryGetSavedPair(installed, out var savedPrimary, out var savedSecondary))
        {
            _history.RestorePair(savedPrimary, savedSecondary, current);
            _log.Info($"Restored saved layout pair: {savedPrimary} <-> {savedSecondary}.");
            return true;
        }

        if (installed.Length >= 2)
        {
            _history.RestorePair(installed[0], installed[1], current);
            PersistPair();
            _log.Info($"Initialized layout pair from Windows order: {installed[0]} <-> {installed[1]}.");
            return true;
        }

        if (!current.IsEmpty && _history.Primary != current)
            _history.Initialize(current);

        if (_history.Primary.IsEmpty)
            _log.Warn("A layout pair is unavailable because Windows has fewer than two installed layouts.");
        return false;
    }

    private bool TryGetSavedPair(
        IReadOnlyCollection<LayoutId> installed,
        out LayoutId primary,
        out LayoutId secondary)
    {
        primary = LayoutId.Empty;
        secondary = LayoutId.Empty;

        if (!TryNormalizeLayoutId(_settings.PrimaryLayoutId, out primary) ||
            !TryNormalizeLayoutId(_settings.SecondaryLayoutId, out secondary) ||
            primary == secondary ||
            !installed.Contains(primary) ||
            !installed.Contains(secondary))
        {
            primary = LayoutId.Empty;
            secondary = LayoutId.Empty;
            return false;
        }

        return true;
    }

    private static bool TryNormalizeLayoutId(string? value, out LayoutId layout)
    {
        layout = LayoutId.Empty;
        if (!KeyboardLayoutService.IsValidLayoutId(value ?? string.Empty) ||
            !uint.TryParse(value, System.Globalization.NumberStyles.HexNumber, null, out var parsed))
        {
            return false;
        }

        layout = new LayoutId(parsed.ToString("X8"));
        return true;
    }

    private void PersistPair()
    {
        if (!_history.HasPair)
            return;

        _settings.PrimaryLayoutId = _history.Primary.Id;
        _settings.SecondaryLayoutId = _history.Secondary.Id;
        _settings.Save();
    }

    private void BeginInternalSwitch(LayoutId target)
    {
        _isInternalSwitch = true;
        _internalTarget = target;
        _internalDeadline = Environment.TickCount64 + 1500;
        _log.Diagnostic($"Waiting for layout switch confirmation: target={target}.");
    }

    private void SynchronizeBeforeToggle(IntPtr hwnd, LayoutId actual)
    {
        if (actual.IsEmpty)
            return;

        var confirmedPendingSwitch = _isInternalSwitch && actual == _internalTarget;
        if (_isInternalSwitch)
        {
            _log.Diagnostic(confirmedPendingSwitch
                ? $"Pending switch confirmed before next hotkey -> {actual}."
                : $"Pending switch superseded before next hotkey: target={_internalTarget}, actual={actual}.");
            _isInternalSwitch = false;
        }

        if (_history.Current != actual)
        {
            var previous = _history.Current;
            var reformed = false;
            if (confirmedPendingSwitch)
                _history.ApplyInternalSelection(actual);
            else
                reformed = _history.ApplyUserSelection(actual);
            if (reformed)
                PersistPair();
            _log.Info($"Synchronized active layout before toggle: {previous} -> {actual}" +
                      (reformed ? "; pair reformed." : "."));
        }

        _reportedLayout = actual;
        _memory.Remember(hwnd, actual);
    }

    private void OnDiagnosticHeartbeat(object? sender, EventArgs e) =>
        WriteDiagnosticSnapshot("heartbeat");

    private void WriteDiagnosticSnapshot(string reason)
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            var now = Environment.TickCount64;
            var lastHookEvent = _hook.LastEventTickMs;
            var hookIdleMs = lastHookEvent == 0 ? -1 : Math.Max(0, now - lastHookEvent);
            var hwnd = CurrentHwnd();
            _log.Diagnostic(
                $"Snapshot ({reason}): hook[installed={_hook.IsInstalled},events={_hook.EventCount}," +
                $"idleMs={hookIdleMs},installThread={_hook.InstallThreadId},callbackThread={_hook.CallbackThreadId}], " +
                $"hotkey={_settings.Hotkey}, enabled={_settings.Enabled}, " +
                $"reactor[pending={_reactor.HasPendingState},engaged={_reactor.IsEngaged}], " +
                $"physical[win={NativeMethods.IsWinDown()},space={NativeMethods.IsSpaceDown()}," +
                $"leftAlt={NativeMethods.IsLeftAltDown()},leftShift={NativeMethods.IsLeftShiftDown()}], " +
                $"pair={_history.Primary}<->{_history.Secondary}, current={_history.Current}, " +
                $"foreground=0x{hwnd.ToInt64():X}, layout={QueryLayout(hwnd)}, internalSwitch={_isInternalSwitch}.");
        }
    }

    private void StopLongPressTimer()
    {
        _longPressTimer?.Dispose();
        _longPressTimer = null;
    }

    private void RaiseStatus()
    {
        string current, pair;
        lock (_gate)
        {
            var cur = _history.Current.IsEmpty ? _reportedLayout : _history.Current;
            current = DisplayCode(cur);
            pair = _history.HasPair
                ? $"{DisplayCode(_history.Primary)} <-> {DisplayCode(_history.Secondary)}"
                : "-";
        }

        StatusChanged?.Invoke(new SwitcherStatus(current, pair, _settings.Enabled));
    }

    private string DisplayCode(LayoutId id)
    {
        foreach (var l in _installed)
        {
            if (l.Id == id)
                return SampleCharResolver.ThreeLetterCode(l.Lcid);
        }

        var hex = id.Id;
        return hex.Length >= 4 ? hex[^4..] : hex;
    }

    private void Prepare(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            action();
        else
            dispatcher.BeginInvoke(action);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;

            SystemEvents.SessionSwitch -= OnSessionSwitch;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            StopLongPressTimer();
            _diagnosticTimer.Stop();
            _pollTimer?.Dispose();
            _hook.Dispose();
            _foreground.Dispose();
            _popup.Hide();
            _log.Info("Service disposed.");
        }
    }
}
