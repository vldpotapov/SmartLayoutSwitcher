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

    public IntPtr GetLayoutHandle(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return IntPtr.Zero;

        var threadId = NativeMethods.GetWindowThreadProcessId(hwnd, out _);
        return threadId == 0 ? IntPtr.Zero : NativeMethods.GetKeyboardLayout(threadId);
    }

    public bool TryGetLoadedLayoutHandle(LayoutId layout, out IntPtr hkl)
    {
        hkl = FindLoadedHkl(layout);
        return hkl != IntPtr.Zero;
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

    /// <summary>
    /// Removes a base layout that Windows kept loaded alongside the user's
    /// preferred substitute. For example, a stale 04050405 Czech HKL can remain
    /// after an older app loaded it even though the user configured only Czech
    /// QWERTY (00010405). Keeping both makes Windows show two "CES" entries.
    /// </summary>
    public int UnloadRedundantBaseLayouts()
    {
        var substitutes = GetConfiguredSubstitutes();
        if (substitutes.Count == 0)
            return 0;

        var preloaded = GetPreloadedIds();
        var loaded = GetLoadedHkls();
        var removed = 0;
        foreach (var baseId in substitutes.Keys.Where(preloaded.Contains))
        {
            var languageId = baseId & 0xFFFF;
            var hasPreferredVariant = loaded.Any(hkl =>
            {
                var raw = (uint)hkl.ToInt64();
                return (raw & 0xFFFF) == languageId && (raw & 0xF0000000) == 0xF0000000;
            });

            if (!hasPreferredVariant)
                continue;

            foreach (var hkl in loaded)
            {
                var raw = (uint)hkl.ToInt64();
                var isBaseLayout = (raw & 0xFFFF) == languageId && (raw >> 16) == languageId;
                if (isBaseLayout && NativeMethods.UnloadKeyboardLayout(hkl))
                    removed++;
            }
        }

        return removed;
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

        // WM_INPUTLANGCHANGEREQUEST expects a real, loaded HKL. A registry
        // KLID is not necessarily that handle: e.g. the Czech QWERTY
        // substitute 00010405 is loaded by Windows as F0050405. Posting the
        // plain KLID is accepted by PostMessage but does not activate Czech,
        // leaving the switcher's history out of sync with Windows.
        var hkl = FindLoadedHkl(layout);
        if (hkl == IntPtr.Zero)
            return false;

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

    private static Dictionary<uint, uint> GetConfiguredSubstitutes()
    {
        var result = new Dictionary<uint, uint>();
        try
        {
            using var substitutes = Registry.CurrentUser.OpenSubKey(SubstitutesKey);
            if (substitutes is null)
                return result;

            foreach (var name in substitutes.GetValueNames())
            {
                if (substitutes.GetValue(name) is not string value ||
                    !uint.TryParse(name, System.Globalization.NumberStyles.HexNumber, null, out var baseId) ||
                    !uint.TryParse(value, System.Globalization.NumberStyles.HexNumber, null, out var effectiveId))
                    continue;

                result[baseId] = effectiveId;
            }
        }
        catch
        {
            // A missing or unreadable registry key simply means no cleanup.
        }

        return result;
    }

    private static HashSet<uint> GetPreloadedIds()
    {
        var result = new HashSet<uint>();
        try
        {
            using var preload = Registry.CurrentUser.OpenSubKey(PreloadKey);
            if (preload is null)
                return result;

            foreach (var name in preload.GetValueNames())
            {
                if (preload.GetValue(name) is string value &&
                    uint.TryParse(value, System.Globalization.NumberStyles.HexNumber, null, out var preloadId))
                    result.Add(preloadId);
            }
        }
        catch
        {
            // A missing or unreadable registry key simply means no cleanup.
        }

        return result;
    }

    private static IReadOnlyList<IntPtr> GetLoadedHkls()
    {
        var count = checked((int)NativeMethods.GetKeyboardLayoutList(0, null));
        if (count <= 0)
            return Array.Empty<IntPtr>();

        var loaded = new IntPtr[count];
        var actual = checked((int)NativeMethods.GetKeyboardLayoutList(loaded.Length, loaded));
        return actual == loaded.Length ? loaded : loaded.Take(Math.Max(0, actual)).ToArray();
    }

    private static IntPtr FindLoadedHkl(LayoutId target)
    {
        if (!uint.TryParse(target.Id, System.Globalization.NumberStyles.HexNumber, null, out var targetId))
            return IntPtr.Zero;

        var loaded = GetLoadedHkls();
        if (loaded.Count == 0)
            return IntPtr.Zero;

        // Most layouts have an HKL that exactly contains their KLID.
        foreach (var hkl in loaded)
            if ((uint)hkl.ToInt64() == targetId)
                return hkl;

        var languageId = targetId & 0xFFFF;
        var highWord = targetId >> 16;
        var isSubstitute = highWord != 0 && highWord != languageId;

        // Preferred variants are represented by transient FxxxLLLL HKLs.
        // Prefer that handle for a substituted KLID, rather than the base
        // LLLL LLLL layout that can coexist in Windows' loaded list.
        if (isSubstitute)
        {
            foreach (var hkl in loaded)
            {
                var raw = (uint)hkl.ToInt64();
                if ((raw & 0xFFFF) == languageId && (raw & 0xF0000000) == 0xF0000000)
                    return hkl;
            }
        }

        // Standard language layouts use the familiar 04090409 form. The
        // fallback still lets Windows resolve ordinary user-installed layouts
        // while only substituted variants use the Fxxx preference above.
        foreach (var hkl in loaded)
            if (((uint)hkl.ToInt64() & 0xFFFF) == languageId)
                return hkl;

        return IntPtr.Zero;
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
