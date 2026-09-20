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
            // The icon is inset so the notification dot can render fully, sticking
            // out beyond the icon's corner without being clipped by the bitmap edge.
            graphics.DrawIcon(source, new Rectangle(2, 2, 28, 28));
            using var dot = new SolidBrush(Color.FromArgb(0xFF, 0x6F, 0x00));
            graphics.FillEllipse(dot, 20, 0, 12, 12);
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
