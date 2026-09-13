using System.Diagnostics;
using System.Text;
using Microsoft.Win32;
using SmartLayoutSwitcher.Core;

namespace SmartLayoutSwitcher.Native;

/// <summary>
/// Keyboard layout queries and switching around the foreground window / its input thread
/// (spec §13: layout on Windows is per-thread, not a single global value).
///
/// Note on encoding: an HKL (e.g. 0x04190419) is a compound of language id and layout
/// id, while the registry "Preload" entries (e.g. 00000419) use the plain input-language
/// identifier. We normalize HKL → catalog id by matching the LCID (low 16 bits).
/// </summary>
public sealed class KeyboardLayoutService
{
    private const string PreloadKey = @"Keyboard Layout\Preload";
    private const string SubstitutesKey = @"Keyboard Layout\Substitutes";
    private const string LayoutsKey = @"SYSTEM\CurrentControlSet\Control\Keyboard Layouts";

    public LayoutInfo? GetForegroundLayoutInfo()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
            return null;

        var tid = NativeMethods.GetWindowThreadProcessId(hwnd, out _);
        var hkl = tid == 0 ? IntPtr.Zero : NativeMethods.GetKeyboardLayout(tid);
        if (hkl == IntPtr.Zero)
            return null;

        var catalog = GetInstalledLayouts();
        var preloadId = MatchToCatalog((uint)hkl.ToInt64(), catalog);
        var info = BuildLayoutInfo(preloadId, loaded: true);
        return info with { Hkl = hkl };
    }

    public string? GetForegroundProcessName()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
            return null;

        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0)
            return null;

        try
        {
            return Process.GetProcessById((int)pid).ProcessName;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Ordered list of the layouts the user can switch to (registry "Preload").
    /// </summary>
    public IReadOnlyList<LayoutInfo> GetInstalledLayouts()
    {
        var result = new List<LayoutInfo>();
        using var preload = Registry.CurrentUser.OpenSubKey(PreloadKey);
        if (preload is null)
            return result;

        var names = preload.GetValueNames().ToList();
        names.Sort((a, b) =>
            int.TryParse(a, out var ia) && int.TryParse(b, out var ib) ? ia.CompareTo(ib)
            : string.CompareOrdinal(a, b));

        var order = 0;
        foreach (var name in names)
        {
            if (preload.GetValue(name) is not string hex)
                continue;

            if (!uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var preloadId))
                continue;

            // Windows stores preferred variants (such as Czech QWERTY) as a
            // substitute for the base language layout. The effective ID is the
            // only one this app should display or request; otherwise calling the
            // layout-loading API can make Windows show a duplicate Czech layout.
            var effectiveId = ResolveSubstitute(preloadId);
            result.Add(BuildLayoutInfo(effectiveId, loaded: false) with { SortOrder = order++ });
        }

        return result;
    }

    public LayoutInfo BuildLayoutInfo(uint preloadId, bool loaded)
    {
        var lcid = (ushort)(preloadId & 0xFFFF);
        var languageName = LcidToEnglishName(lcid);
        var layoutName = ReadLayoutText(preloadId);
        return new LayoutInfo(
            Hkl: new IntPtr(preloadId),
            PreloadId: preloadId,
            Lcid: lcid,
            LanguageName: languageName,
            LayoutName: layoutName,
            SortOrder: 0);
    }

    public static uint MatchToCatalog(uint rawHkl, IReadOnlyList<LayoutInfo> catalog)
    {
        if (catalog.Count == 0)
            return rawHkl & 0xFFFF;

        foreach (var l in catalog)
            if (l.PreloadId == rawHkl)
                return l.PreloadId;

        foreach (var l in catalog)
            if ((l.PreloadId & 0xFFFF) == (rawHkl & 0xFFFF))
                return l.PreloadId;

        return rawHkl & 0xFFFF;
    }

    /// <summary>
    /// Requests the foreground window's input thread to activate an already
    /// installed input locale. This deliberately does not call LoadKeyboardLayout:
    /// on Windows 8+ that API can load an additional system-wide layout.
    /// </summary>
    public bool SwitchTo(IntPtr hwnd, LayoutId layout)
    {
        if (hwnd == IntPtr.Zero || layout.IsEmpty || !IsValidLayoutId(layout.Id))
            return false;

        var hkl = new IntPtr(long.Parse(layout.Id, System.Globalization.NumberStyles.HexNumber));
        return NativeMethods.PostMessage(hwnd, NativeMethods.WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, hkl);
    }

    public static bool IsValidLayoutId(string id) =>
        !string.IsNullOrWhiteSpace(id) &&
        uint.TryParse(id, System.Globalization.NumberStyles.HexNumber, null, out _);

    private static uint ResolveSubstitute(uint preloadId)
    {
        try
        {
            using var substitutes = Registry.CurrentUser.OpenSubKey(SubstitutesKey);
            if (substitutes?.GetValue(preloadId.ToString("X8")) is not string hex ||
                !uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var substituteId))
                return preloadId;

            return substituteId;
        }
        catch
        {
            return preloadId;
        }
    }

    private static string ReadLayoutText(uint preloadId)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"{LayoutsKey}\{preloadId:X8}");
            if (key is null)
                return string.Empty;

            var text = key.GetValue("Layout Text") as string;
            if (!string.IsNullOrWhiteSpace(text))
                return text;

            var display = key.GetValue("Layout Display Name") as string;
            if (!string.IsNullOrWhiteSpace(display))
                return ResolveIndirectString(display);

            return key.GetValue("Layout File") as string ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string ResolveIndirectString(string value)
    {
        // "@%SystemRoot%\system32\input.dll,-xxxx" — resolve via SHLoadIndirectString when starts with '@'.
        if (!value.StartsWith('@'))
            return value;

        var buf = new StringBuilder(512);
        if (NativeMethods.SHLoadIndirectString(value, buf, buf.Capacity, IntPtr.Zero) != NativeMethods.ERROR_SUCCESS)
            return value;
        return buf.ToString();
    }

    private static string LcidToEnglishName(ushort lcid)
    {
        var sb = new StringBuilder(256);
        if (NativeMethods.GetLocaleInfoW(lcid, NativeMethods.LOCALE_SLANGUAGE, sb, sb.Capacity) > 0)
            return sb.ToString();
        return $"LCID {lcid:X4}";
    }
}
