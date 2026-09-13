using SmartLayoutSwitcher.Core;

namespace SmartLayoutSwitcher.App.Services;

/// <summary>
/// Per-window layout memory keyed by top-level HWND (first implementation, spec §8).
/// Priority is reliability over fancy application profiles.
/// </summary>
public sealed class WindowLayoutMemory
{
    private readonly Dictionary<IntPtr, LayoutId> _map = new();

    public int Count => _map.Count;

    public bool TryGet(IntPtr hwnd, out LayoutId layout)
    {
        if (hwnd == IntPtr.Zero)
        {
            layout = LayoutId.Empty;
            return false;
        }

        return _map.TryGetValue(hwnd, out layout!);
    }

    public void Remember(IntPtr hwnd, LayoutId layout)
    {
        if (hwnd == IntPtr.Zero || layout.IsEmpty)
            return;
        _map[hwnd] = layout;
    }

    public void Forget(IntPtr hwnd)
    {
        if (hwnd != IntPtr.Zero)
            _map.Remove(hwnd);
    }

    /// <summary>Prunes entries whose window no longer exists (lives are cheap, run periodically).</summary>
    public void Prune(Func<IntPtr, bool> windowExists)
    {
        foreach (var hwnd in _map.Keys.ToList())
        {
            if (!windowExists(hwnd))
                _map.Remove(hwnd);
        }
    }
}