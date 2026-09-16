using System.Collections.Concurrent;
using System.IO;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SkiaSharp;
using Svg.Skia;

namespace SmartLayoutSwitcher.App.Controls;

/// <summary>
/// Loads the source SVG assets into frozen WPF image sources. WPF has no native
/// SVG image decoder, so this keeps the original vectors in the project while
/// rendering a crisp, cached bitmap for the small application controls.
/// </summary>
public sealed class SvgImageExtension : MarkupExtension
{
    public SvgImageExtension()
    {
    }

    public SvgImageExtension(string source) => Source = source;

    [ConstructorArgument("source")]
    public string Source { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider) => SvgImageSource.Load(Source);
}

public static class SvgImageSource
{
    private const int RenderSize = 96;
    private static readonly ConcurrentDictionary<string, ImageSource> Cache = new(StringComparer.Ordinal);

    public static ImageSource Load(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
            throw new ArgumentException("An SVG resource path is required.", nameof(source));

        return Cache.GetOrAdd(source, LoadCore);
    }

    private static ImageSource LoadCore(string source)
    {
        var uri = new Uri(source, UriKind.RelativeOrAbsolute);
        var resource = Application.GetResourceStream(uri)
            ?? throw new InvalidOperationException($"SVG resource '{source}' was not found.");

        using var stream = resource.Stream;
        using var svg = new SKSvg();
        var picture = svg.Load(stream)
            ?? throw new InvalidOperationException($"SVG resource '{source}' could not be rendered.");

        var bounds = picture.CullRect;
        if (bounds.Width <= 0 || bounds.Height <= 0)
            throw new InvalidOperationException($"SVG resource '{source}' has no visible bounds.");

        var scale = Math.Min(RenderSize / bounds.Width, RenderSize / bounds.Height);
        var width = Math.Max(1, (int)Math.Ceiling(bounds.Width * scale));
        var height = Math.Max(1, (int)Math.Ceiling(bounds.Height * scale));

        using var bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.Scale(scale);
            canvas.Translate(-bounds.Left, -bounds.Top);
            canvas.DrawPicture(picture);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var png = new MemoryStream(data.ToArray());

        var result = new BitmapImage();
        result.BeginInit();
        result.CacheOption = BitmapCacheOption.OnLoad;
        result.StreamSource = png;
        result.EndInit();
        result.Freeze();
        return result;
    }
}
