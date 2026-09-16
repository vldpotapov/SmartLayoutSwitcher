using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Hardcodet.Wpf.TaskbarNotification;
using SmartLayoutSwitcher.App.Controls;
using Icon = System.Drawing.Icon;

namespace SmartLayoutSwitcher.App.Tray;

/// <summary>
/// System tray icon with a WPF-rendered context menu. Keeping it a ContextMenu
/// preserves Windows' reliable taskbar anchoring while allowing the menu itself
/// to follow the application's visual language.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly TaskbarIcon _icon = new();
    private readonly MenuItem _current;
    private readonly TextBlock _currentText;
    private readonly TextBlock _pairText;
    private Icon? _currentIcon;
    private bool _updateAvailable;

    public event Action? ExitRequested;
    public event Action? SettingsRequested;

    public TrayIcon(bool updateAvailable = false)
    {
        _updateAvailable = updateAvailable;
        _currentIcon = TrayIconFactory.Create(updateAvailable);
        _icon.Icon = _currentIcon;
        _icon.ToolTipText = CreateToolTipText(updateAvailable);

        var menu = new ContextMenu();

        var header = CreateItem("Smart Layout Switcher", CreateBrandIcon(), interactive: false);
        (_current, _currentText, _pairText) = CreateStatusItem();

        var settings = CreateItem("Settings", CreateSvgIcon("/SmartLayoutSwitcher.App;component/Resources/Settings/general.svg"));
        settings.Click += (_, _) => SettingsRequested?.Invoke();

        var exit = CreateItem("Quit", CreateSvgIcon("/SmartLayoutSwitcher.App;component/Resources/Tray/quit.svg"));
        exit.Click += (_, _) => ExitRequested?.Invoke();

        menu.Items.Add(header);
        menu.Items.Add(CreateSeparator());
        menu.Items.Add(_current);
        menu.Items.Add(CreateSeparator());
        menu.Items.Add(settings);
        menu.Items.Add(CreateSeparator());
        menu.Items.Add(exit);

        _icon.ContextMenu = menu;
    }

    public void UpdateStatus(string current, string pair)
    {
        var invoke = () =>
        {
            _currentText.Text = $"Current: {current}";
            _pairText.Text = $"Pair: {pair}";
        };

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            invoke();
        else
            dispatcher.BeginInvoke(invoke);
    }

    public void SetUpdateAvailable(bool updateAvailable)
    {
        var invoke = () =>
        {
            if (_updateAvailable == updateAvailable)
                return;

            _updateAvailable = updateAvailable;
            var oldIcon = _currentIcon;
            _currentIcon = TrayIconFactory.Create(updateAvailable);
            _icon.Icon = _currentIcon;
            _icon.ToolTipText = CreateToolTipText(updateAvailable);
            oldIcon?.Dispose();
        };

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            invoke();
        else
            dispatcher.BeginInvoke(invoke);
    }

    private static MenuItem CreateItem(object header, object icon, bool interactive = true) => new()
    {
        Header = header,
        Icon = icon,
        IsHitTestVisible = interactive,
        Focusable = interactive,
    };

    private static (MenuItem Item, TextBlock Current, TextBlock Pair) CreateStatusItem()
    {
        var current = new TextBlock
        {
            Text = "Current: —",
        };
        var pair = new TextBlock
        {
            Margin = new Thickness(0, 2, 0, 0),
            Text = "Pair: —",
        };
        var header = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        header.Children.Add(current);
        header.Children.Add(pair);

        var item = CreateItem(header, CreateSvgIcon("/SmartLayoutSwitcher.App;component/Resources/Settings/switch.svg"), interactive: false);
        return (item, current, pair);
    }

    private static Separator CreateSeparator() => new();

    private static Image CreateSvgIcon(string source) => new()
    {
        Width = 16,
        Height = 16,
        Source = SvgImageSource.Load(source),
    };

    private static Border CreateBrandIcon()
    {
        var image = new Image
        {
            Stretch = System.Windows.Media.Stretch.Uniform,
            Source = new BitmapImage(new Uri("pack://application:,,,/SmartLayoutSwitcher.App;component/Resources/Lang-icon.png")),
        };

        return new Border
        {
            Width = 16,
            Height = 16,
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x11, 0x11, 0x11)),
            CornerRadius = new System.Windows.CornerRadius(3),
            Padding = new Thickness(2),
            Child = image,
        };
    }

    private static string CreateToolTipText(bool updateAvailable) =>
        updateAvailable
            ? "Smart Layout Switcher — update available"
            : "Smart Layout Switcher";

    public void Dispose()
    {
        _icon.Dispose();
        _currentIcon?.Dispose();
    }
}
