using SmartLayoutSwitcher.App.Diagnostics;
using SmartLayoutSwitcher.App.Popup;
using SmartLayoutSwitcher.App.Settings;
using SmartLayoutSwitcher.Core;
using SmartLayoutSwitcher.Native;

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
    private readonly object _gate = new();
    private readonly System.Threading.Timer _pollTimer;

    private IReadOnlyList<LayoutInfo> _installed = Array.Empty<LayoutInfo>();

    private IntPtr _foregroundHwnd;
    private LayoutId _reportedLayout = LayoutId.Empty;

    private System.Threading.Timer? _longPressTimer;
    private bool _longPressRaised;

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
        var current = _layouts.GetForegroundLayoutInfo();
        if (current is not null)
        {
            _history.Initialize(current.Id);
            _reportedLayout = current.Id;
            _log.Info($"Initial layout: {current.Id} {current.LanguageName}");
        }
        else
        {
            _log.Warn("Could not determine the initial foreground layout.");
        }

        _foreground.ForegroundChanged += OnForegroundChanged;
        _foreground.Install();
        _log.Info($"Foreground tracker installed: {_foreground.IsInstalled}");

        _hook.KeyEvent += OnKey;
        _hook.Install();
        _log.Info($"Keyboard hook installed: {_hook.IsInstalled}");

        _pollTimer = new System.Threading.Timer(_ => OnPoll(), null, 300, 300);
    }

    public void PublishStatus() => RaiseStatus();

    // ---------------------------------------------------------------- key handling

    private KeyReaction OnKey(HookKeyEventArgs args)
    {
        // Disabled means fully transparent to normal typing and the user's own
        // Windows language shortcut.
        if (!_settings.Enabled && !_popup.IsOpen)
            return KeyReaction.PassThrough;

        var now = Environment.TickCount64;
        var usesWinSpace = _settings.Hotkey == HotkeyMode.WinSpace;
        var isPrimary = usesWinSpace
            ? args.VkCode is NativeMethods.VK_LWIN or NativeMethods.VK_RWIN
            : args.VkCode == NativeMethods.VK_LMENU;
        var isSecondary = usesWinSpace
            ? args.VkCode == NativeMethods.VK_SPACE
            : args.VkCode == NativeMethods.VK_LSHIFT;

        KeyReaction reaction;
        if (isPrimary)
            reaction = args.IsKeyUp ? _reactor.PrimaryUp(now) : _reactor.PrimaryDown(now);
        else if (isSecondary)
            reaction = args.IsKeyUp ? _reactor.SecondaryUp(now) : _reactor.SecondaryDown(now);
        else if (_popup.IsOpen)
            reaction = RoutePopupKey(args);
        else
            reaction = args.IsKeyUp ? _reactor.OtherKeyUp() : _reactor.OtherKeyDown();

        if (args.VkCode is NativeMethods.VK_RMENU or NativeMethods.VK_RSHIFT)
            _log.Debug($"Right modifier 0x{args.VkCode:X2} passed through (AltGr safe).");

        return reaction;
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

            _log.Debug("Combo engaged (LAlt+LShift).");
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
                var current = _history.Current;
                _reactor.EnterCycleMode();
                _popup.Show(_installed, current);
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
        if (!_history.TryGetToggleTarget(out var target))
        {
            _log.Info($"Short press ({durationMs} ms) but no switch pair available.");
            return;
        }

        var hwnd = CurrentHwnd();
        if (hwnd == IntPtr.Zero)
            return;

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

            _history.ApplyUserSelection(chosen);
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

    // ------------------------------------------------------------- foreground / poll

    private void OnForegroundChanged(IntPtr hwnd)
    {
        lock (_gate)
        {
            if (_disposed)
                return;

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
            if (_isInternalSwitch && now > _internalDeadline)
                _isInternalSwitch = false;

            var hwnd = _foregroundHwnd != IntPtr.Zero ? _foregroundHwnd : NativeMethods.GetForegroundWindow();
            var layout = QueryLayout(hwnd);
            if (layout.IsEmpty || layout == _reportedLayout)
                return;

            _reportedLayout = layout;

            if (_isInternalSwitch && layout == _internalTarget)
            {
                _isInternalSwitch = false;
                _memory.Remember(hwnd, layout);
                _log.Debug($"Internal switch completed -> {layout}");
            }
            else
            {
                var reformed = _history.ApplyUserSelection(layout);
                _log.Info(reformed
                    ? $"User selected a new layout {layout}; pair reformed."
                    : $"Layout changed to {layout}.");
                if (hwnd != IntPtr.Zero)
                    _memory.Remember(hwnd, layout);
            }

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

    private void BeginInternalSwitch(LayoutId target)
    {
        _isInternalSwitch = true;
        _internalTarget = target;
        _internalDeadline = Environment.TickCount64 + 1500;
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

            StopLongPressTimer();
            _pollTimer?.Dispose();
            _hook.Dispose();
            _foreground.Dispose();
            _popup.Hide();
            _log.Info("Service disposed.");
        }
    }
}
