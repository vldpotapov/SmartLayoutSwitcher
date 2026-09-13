namespace SmartLayoutSwitcher.Native;

/// <summary>
/// Event-driven foreground window tracking via WinEvent hook (spec §8, §12).
/// Delivered on the installing thread's message loop.
/// </summary>
public sealed class ForegroundWindowTracker : IDisposable
{
    private readonly NativeMethods.WinEventProc _proc;
    private IntPtr _hook;

    public ForegroundWindowTracker() => _proc = OnWinEvent;

    public event Action<IntPtr>? ForegroundChanged;

    public bool IsInstalled => _hook != IntPtr.Zero;

    public bool Install()
    {
        if (_hook != IntPtr.Zero)
            return true;

        _hook = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_SYSTEM_FOREGROUND,
            NativeMethods.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero,
            _proc,
            0,
            0,
            NativeMethods.WINEVENT_OUTOFCONTEXT);

        return _hook != IntPtr.Zero;
    }

    public void Uninstall()
    {
        if (_hook == IntPtr.Zero)
            return;

        NativeMethods.UnhookWinEvent(_hook);
        _hook = IntPtr.Zero;
    }

    public void Dispose() => Uninstall();

    private void OnWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild,
        uint dwEventThread, uint dwmsEventTime)
    {
        if (hwnd != IntPtr.Zero)
            ForegroundChanged?.Invoke(hwnd);
    }
}