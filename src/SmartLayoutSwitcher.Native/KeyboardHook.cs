using System.Runtime.InteropServices;
using SmartLayoutSwitcher.Core;

namespace SmartLayoutSwitcher.Native;

public sealed record HookKeyEventArgs(
    int VkCode,
    uint ScanCode,
    uint Flags,
    bool IsKeyUp,
    bool IsSysKey,
    bool IsInjected)
{
    public bool IsExtended => (Flags & NativeMethods.LLKHF_EXTENDED) != 0;
    public bool IsAltDownNow => (Flags & NativeMethods.LLKHF_ALTDOWN) != 0;
}

/// <summary>
/// Handlers return a <see cref="KeyReaction"/>; Suppress swallows the event so
/// Windows' own hotkey handling cannot fire a second switch.
/// </summary>
public delegate KeyReaction KeyHookHandler(HookKeyEventArgs args);

/// <summary>
/// Thin wrapper over WH_KEYBOARD_LL. Runs entirely in this process; no DLL injection.
/// </summary>
public sealed class KeyboardHook : IDisposable
{
    private readonly NativeMethods.LowLevelKeyboardProc _callback;
    private IntPtr _hookId;

    public KeyboardHook()
    {
        _callback = HookCallback;
    }

    /// <summary>First non-null handler that returns Suppress wins; handlers run in order.</summary>
    public event KeyHookHandler? KeyEvent;

    public bool IsInstalled => _hookId != IntPtr.Zero;

    public bool Install()
    {
        if (_hookId != IntPtr.Zero)
            return true;

        var hMod = NativeMethods.GetModuleHandle(null);
        _hookId = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _callback, hMod, 0);
        return _hookId != IntPtr.Zero;
    }

    public void Uninstall()
    {
        if (_hookId == IntPtr.Zero)
            return;

        NativeMethods.UnhookWindowsHookEx(_hookId);
        _hookId = IntPtr.Zero;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var data = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
            var msg = (uint)wParam.ToInt64();
            var args = new HookKeyEventArgs(
                VkCode: (int)data.vkCode,
                ScanCode: data.scanCode,
                Flags: data.flags,
                IsKeyUp: msg is NativeMethods.WM_KEYUP or NativeMethods.WM_SYSKEYUP,
                IsSysKey: msg is NativeMethods.WM_SYSKEYDOWN or NativeMethods.WM_SYSKEYUP,
                IsInjected: (data.flags & NativeMethods.LLKHF_INJECTED) != 0);

            var handler = KeyEvent;
            if (handler is not null)
            {
                foreach (var h in handler.GetInvocationList().Cast<KeyHookHandler>())
                {
                    if (h(args) == KeyReaction.Suppress)
                        return (IntPtr)1; // swallow the event before Windows sees it
                }
            }
        }

        return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void Dispose() => Uninstall();
}
