using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmartLayoutSwitcher.App.Settings;

public enum HotkeyMode
{
    LeftAltLeftShift,
    WinSpace,
}

/// <summary>
/// JSON settings at %APPDATA%\SmartLayoutSwitcher\settings.json (spec §23).
/// </summary>
public sealed class AppSettings
{
    public const string DefaultGitHubProjectUrl = "https://github.com/vldpotapov/SmartLayoutSwitcher";

    public bool Enabled { get; set; } = true;
    public int ShortPressMs { get; set; } = 600;
    public bool RememberPerWindow { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public bool ShowPopupOnLongPress { get; set; } = true;
    public bool EnableSelectedTextConversion { get; set; } = true;
    public bool DebugLogEnabled { get; set; }
    public HotkeyMode Hotkey { get; set; } = HotkeyMode.WinSpace;
    public string GitHubProjectUrl { get; set; } = DefaultGitHubProjectUrl;

    // The current two-layout switch pair. Values are normalized Windows KLIDs
    // (for example, 00000409), not transient HKL handles.
    public string? PrimaryLayoutId { get; set; }
    public string? SecondaryLayoutId { get; set; }

    // Cached update state keeps normal launches offline and lets the tray show an
    // already-known update immediately.
    public DateTimeOffset? LastUpdateCheckUtc { get; set; }
    public bool UpdateCheckFailed { get; set; }
    public string? AvailableUpdateVersion { get; set; }
    public string? AvailableUpdateDownloadUrl { get; set; }
    public string? AvailableUpdateFileName { get; set; }

    public static string DirectoryPath =>
        System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SmartLayoutSwitcher");

    public static string FilePath => System.IO.Path.Combine(DirectoryPath, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static AppSettings Load()
    {
        try
        {
            if (!System.IO.File.Exists(FilePath))
                return new AppSettings();

            var json = System.IO.File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            System.IO.File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch
        {
            // Settings persistence is best-effort; never crash on it.
        }
    }
}
