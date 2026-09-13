using Microsoft.Win32;

namespace SmartLayoutSwitcher.App;

/// <summary>
/// Optional autostart via HKCU Run key — no admin rights needed (spec §22).
/// </summary>
public static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SmartLayoutSwitcher";

    public static bool IsRegistered()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            return key?.GetValue(ValueName) is string;
        }
        catch
        {
            return false;
        }
    }

    public static void Set(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (enable)
                key?.SetValue(ValueName, Environment.ProcessPath ?? string.Empty);
            else
                key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch
        {
            // Best-effort.
        }
    }
}