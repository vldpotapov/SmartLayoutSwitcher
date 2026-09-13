using System.Drawing;
using System.IO;
using System.Reflection;

namespace SmartLayoutSwitcher.App.Tray;

internal static class TrayIconFactory
{
    private const string ResourceName = "SmartLayoutSwitcher.App.Resources.LangIcon.ico";

    /// <summary>
    /// Loads the app icon (Lang-icon.ico, embedded as a resource and also set
    /// as the exe ApplicationIcon) into a tray-compatible System.Drawing.Icon.
    /// </summary>
    public static Icon Create()
    {
        var assembly = typeof(TrayIconFactory).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' not found.");
        return new Icon(stream);
    }
}