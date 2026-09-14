using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SmartLayoutSwitcher.Core;
using SmartLayoutSwitcher.Native;
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingGraphics = System.Drawing.Graphics;
using DrawingPixelFormat = System.Drawing.Imaging.PixelFormat;
using DrawingRectangle = System.Drawing.Rectangle;
using DrawingSize = System.Drawing.Size;

namespace SmartLayoutSwitcher.App.Popup;

/// <summary>
/// Language popup built to match the Figma "Language popup" frame: a rounded,
/// blurred glass container holding one chip per installed layout. The selected
/// chip is highlighted; keyboard cycling comes from the global hook (see
/// RoutePopupKey), so the window itself never steals focus.
/// </summary>
public partial class LayoutPopupWindow : Window
{
    // Figma: active chip fill #000000 @20%, inactive fully transparent.
    private static readonly SolidColorBrush ActiveChipBrush = Frozen(Color.FromArgb(0x33, 0x00, 0x00, 0x00));
    private static readonly SolidColorBrush InactiveChipBrush = Frozen(Color.FromArgb(0x00, 0x00, 0x00, 0x00));

    // Figma: label fill #000000 @90%, Segoe UI Semibold 17.
    private static readonly SolidColorBrush LabelBrush = Frozen(Color.FromArgb(0xE5, 0x00, 0x00, 0x00));

    private readonly List<LayoutInfo> _layouts;
    private readonly List<Border> _chips = new();
    private int _selectedIndex;

    public event Action<LayoutId>? LayoutChosen;

    public LayoutPopupWindow(IEnumerable<LayoutInfo> layouts, LayoutId current)
    {
        InitializeComponent();

        _layouts = layouts.ToList();
        _selectedIndex = Math.Max(0, _layouts.FindIndex(l => l.Id == current));

        BuildChips();

        Loaded += (_, _) =>
        {
            UpdateLayout();
            CenterOnWorkArea();
        };
    }

    public void MoveSelection(int delta)
    {
        if (_layouts.Count == 0)
            return;

        _selectedIndex = (_selectedIndex + delta + _layouts.Count) % _layouts.Count;
        ApplySelectionHighlight();
    }

    public void ConfirmAndClose()
    {
        if (_selectedIndex >= 0 && _selectedIndex < _layouts.Count)
            LayoutChosen?.Invoke(_layouts[_selectedIndex].Id);
        Close();
    }

    private void BuildChips()
    {
        for (var i = 0; i < _layouts.Count; i++)
        {
            var chip = new Border
            {
                Height = 40,
                CornerRadius = new CornerRadius(10),
                Background = InactiveChipBrush,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Cursor = Cursors.Hand,
                Child = new TextBlock
                {
                    Text = ShortName(_layouts[i]),
                    FontFamily = new FontFamily("Segoe UI"),
                    FontSize = 17,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = LabelBrush,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };

            var index = i;
            chip.MouseLeftButtonUp += (_, _) =>
            {
                _selectedIndex = index;
                ConfirmAndClose();
            };

            ChipsPanel.Children.Add(chip);
            _chips.Add(chip);
        }

        ApplySelectionHighlight();
    }

    /// <summary>
    /// Figma labels: "U.S." for English (United States), otherwise the language
    /// name without its region, e.g. "Russian", "Czech".
    /// </summary>
    private static string ShortName(LayoutInfo layout)
    {
        var name = layout.LanguageName;
        if (string.IsNullOrWhiteSpace(name))
            return layout.LayoutName;

        var open = name.IndexOf('(');
        if (open <= 0 || !name.EndsWith(')'))
            return name;

        var region = name[(open + 1)..^1].Trim();
        return string.Equals(region, "United States", StringComparison.OrdinalIgnoreCase)
            ? "U.S."
            : name[..open].Trim();
    }

    private void ApplySelectionHighlight()
    {
        for (var i = 0; i < _chips.Count; i++)
            _chips[i].Background = i == _selectedIndex ? ActiveChipBrush : InactiveChipBrush;
    }

    private void CenterOnWorkArea()
    {
        var wa = SystemParameters.WorkArea;
        Left = wa.Left + (wa.Width - ActualWidth) / 2;
        Top = wa.Top + (wa.Height - ActualHeight) / 2;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var hwnd = new WindowInteropHelper(this).Handle;

        var style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        style |= NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW;
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, style);

        // The WPF layer provides the translucent pixels required for its
        // rounded shape. The native region clips both that layer and acrylic,
        // so neither tint nor blur can leak beyond the 20px corners.
        SizeChanged += (_, _) => ApplyRoundedBackdrop(hwnd);
        Loaded += (_, _) => ApplyRoundedBackdrop(hwnd);
    }

    private void ApplyRoundedBackdrop(IntPtr hwnd)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        NativeMethods.SetRoundedRegion(
            hwnd,
            (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX),
            (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY),
            radius: (int)Math.Ceiling(20 * Math.Min(dpi.DpiScaleX, dpi.DpiScaleY)));
    }

    /// <summary>
    /// Creates a stable background blur from the pixels that are actually
    /// behind the popup. Windows may silently decline the undocumented acrylic
    /// composition mode used by many WPF examples; this fallback is consistent
    /// across the supported Windows versions and keeps the blur inside the
    /// rounded WPF Border.
    /// </summary>
    public void SetBlurredDesktopBackground()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0)
            return;

        try
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            var topLeft = PointToScreen(new System.Windows.Point(0, 0));
            var width = Math.Max(1, (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX));
            var height = Math.Max(1, (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY));

            using var desktop = new DrawingBitmap(width, height, DrawingPixelFormat.Format32bppPArgb);
            using (var graphics = DrawingGraphics.FromImage(desktop))
            {
                graphics.CopyFromScreen(
                    (int)Math.Round(topLeft.X),
                    (int)Math.Round(topLeft.Y),
                    0,
                    0,
                    new DrawingSize(width, height),
                    System.Drawing.CopyPixelOperation.SourceCopy);
            }

            var rectangle = new DrawingRectangle(0, 0, width, height);
            var bits = desktop.LockBits(rectangle, System.Drawing.Imaging.ImageLockMode.ReadOnly, DrawingPixelFormat.Format32bppPArgb);
            try
            {
                var stride = width * 4;
                var pixels = new byte[stride * height];
                for (var row = 0; row < height; row++)
                {
                    Marshal.Copy(IntPtr.Add(bits.Scan0, row * bits.Stride), pixels, row * stride, stride);
                }

                Blur(pixels, width, height, radius: 15);
                Tint(pixels, tint: 0xF2, opacity: 0.60);

                var image = BitmapSource.Create(
                    width,
                    height,
                    dpi.PixelsPerInchX,
                    dpi.PixelsPerInchY,
                    PixelFormats.Bgra32,
                    null,
                    pixels,
                    stride);
                image.Freeze();
                RootContainer.Background = new ImageBrush(image) { Stretch = Stretch.Fill };
            }
            finally
            {
                desktop.UnlockBits(bits);
            }
        }
        catch (ExternalException)
        {
            // Screen capture can be unavailable on a protected desktop. Keep
            // the ordinary translucent Figma fill rather than blocking input.
        }
    }

    private static void Blur(byte[] pixels, int width, int height, int radius)
    {
        var stride = width * 4;
        var buffer = new byte[pixels.Length];

        // Three inexpensive box passes approximate a smooth Gaussian blur
        // while staying instantaneous for this small popup.
        for (var pass = 0; pass < 3; pass++)
        {
            BoxBlurHorizontal(pixels, buffer, width, height, radius);
            BoxBlurVertical(buffer, pixels, width, height, radius);
        }

        for (var index = 3; index < pixels.Length; index += 4)
            pixels[index] = 255;
    }

    private static void BoxBlurHorizontal(byte[] source, byte[] destination, int width, int height, int radius)
    {
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var from = Math.Max(0, x - radius);
                var to = Math.Min(width - 1, x + radius);
                var count = to - from + 1;
                var blue = 0;
                var green = 0;
                var red = 0;
                for (var sample = from; sample <= to; sample++)
                {
                    var index = (y * width + sample) * 4;
                    blue += source[index];
                    green += source[index + 1];
                    red += source[index + 2];
                }

                var target = (y * width + x) * 4;
                destination[target] = (byte)(blue / count);
                destination[target + 1] = (byte)(green / count);
                destination[target + 2] = (byte)(red / count);
                destination[target + 3] = 255;
            }
        }
    }

    private static void BoxBlurVertical(byte[] source, byte[] destination, int width, int height, int radius)
    {
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var from = Math.Max(0, y - radius);
                var to = Math.Min(height - 1, y + radius);
                var count = to - from + 1;
                var blue = 0;
                var green = 0;
                var red = 0;
                for (var sample = from; sample <= to; sample++)
                {
                    var index = (sample * width + x) * 4;
                    blue += source[index];
                    green += source[index + 1];
                    red += source[index + 2];
                }

                var target = (y * width + x) * 4;
                destination[target] = (byte)(blue / count);
                destination[target + 1] = (byte)(green / count);
                destination[target + 2] = (byte)(red / count);
                destination[target + 3] = 255;
            }
        }
    }

    private static void Tint(byte[] pixels, byte tint, double opacity)
    {
        for (var index = 0; index < pixels.Length; index += 4)
        {
            pixels[index] = Blend(pixels[index], tint, opacity);
            pixels[index + 1] = Blend(pixels[index + 1], tint, opacity);
            pixels[index + 2] = Blend(pixels[index + 2], tint, opacity);
        }
    }

    private static byte Blend(byte value, byte tint, double opacity)
        => (byte)Math.Round(value * (1 - opacity) + tint * opacity);

    private static SolidColorBrush Frozen(System.Windows.Media.Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
