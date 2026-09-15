using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace SmartLayoutSwitcher.App.Tray;

internal static class TrayIconFactory
{
    private const string ResourceName = "SmartLayoutSwitcher.App.Resources.LangIcon.ico";

    /// <summary>
    /// Loads the app icon (Lang-icon.ico, embedded as a resource and also set
    /// as the exe ApplicationIcon) into a tray-compatible System.Drawing.Icon.
    /// </summary>
    public static Icon Create(bool updateAvailable = false)
    {
        var assembly = typeof(TrayIconFactory).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' not found.");
        using var source = new Icon(stream);

        if (!updateAvailable)
            return (Icon)source.Clone();

        using var canvas = new Bitmap(32, 32);
        using (var graphics = Graphics.FromImage(canvas))
        {
            graphics.DrawIcon(source, new Rectangle(0, 0, 32, 32));
            using var border = new SolidBrush(Color.FromArgb(245, 255, 255, 255));
            using var dot = new SolidBrush(Color.FromArgb(0x0B, 0xE8, 0xBC));
            graphics.FillEllipse(border, 20, 0, 12, 12);
            graphics.FillEllipse(dot, 22, 2, 8, 8);
        }

        var handle = canvas.GetHicon();
        try
        {
            using var temporaryIcon = Icon.FromHandle(handle);
            return (Icon)temporaryIcon.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);
}
