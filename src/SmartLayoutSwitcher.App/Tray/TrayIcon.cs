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
    public event Action? CheckForUpdatesRequested;

    public TrayIcon(bool updateAvailable = false)
    {
        _updateAvailable = updateAvailable;
        _currentIcon = TrayIconFactory.Create(updateAvailable);
        _icon.Icon = _currentIcon;
        _icon.ToolTipText = CreateToolTipText(updateAvailable);

        var menu = new ContextMenu
        {
            Style = Resource<Style>("TrayContextMenuStyle"),
        };

        var header = CreateItem("Smart Layout Switcher", CreateBrandIcon(), interactive: false);
        (_current, _currentText, _pairText) = CreateStatusItem();

        var settings = CreateItem("Settings", CreateSvgIcon("/SmartLayoutSwitcher.App;component/Resources/Settings/general.svg"));
        settings.Click += (_, _) => SettingsRequested?.Invoke();

        var checkForUpdates = CreateItem("Check for updates", CreateSvgIcon("/SmartLayoutSwitcher.App;component/Resources/Tray/check-updates.svg"));
        checkForUpdates.Click += (_, _) => CheckForUpdatesRequested?.Invoke();

        var exit = CreateItem("Quit", CreateSvgIcon("/SmartLayoutSwitcher.App;component/Resources/Tray/quit.svg"));
        exit.Click += (_, _) => ExitRequested?.Invoke();

        menu.Items.Add(header);
        menu.Items.Add(CreateSeparator());
        menu.Items.Add(_current);
        menu.Items.Add(CreateSeparator());
        menu.Items.Add(settings);
        menu.Items.Add(checkForUpdates);
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
        Style = Resource<Style>("TrayMenuItemStyle"),
        IsHitTestVisible = interactive,
        Focusable = interactive,
    };

    private static (MenuItem Item, TextBlock Current, TextBlock Pair) CreateStatusItem()
    {
        var current = new TextBlock
        {
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Foreground = System.Windows.Media.Brushes.Black,
            Text = "Current: —",
        };
        var pair = new TextBlock
        {
            Margin = new Thickness(0, 7, 0, 0),
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
            FontSize = 16,
            Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x66, 0x66, 0x66)),
            Text = "Pair: —",
        };
        var header = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        header.Children.Add(current);
        header.Children.Add(pair);

        var item = CreateItem(header, CreateSvgIcon("/SmartLayoutSwitcher.App;component/Resources/Settings/switch.svg"), interactive: false);
        return (item, current, pair);
    }

    private static Separator CreateSeparator() => new()
    {
        Style = Resource<Style>("TraySeparatorStyle"),
    };

    private static Image CreateSvgIcon(string source) => new()
    {
        Width = 24,
        Height = 24,
        Source = SvgImageSource.Load(source),
    };

    private static Border CreateBrandIcon()
    {
        var image = new Image
        {
            Width = 48,
            Height = 48,
            Stretch = System.Windows.Media.Stretch.Uniform,
            Source = new BitmapImage(new Uri("pack://application:,,,/SmartLayoutSwitcher.App;component/Resources/Lang-icon.png")),
        };

        return new Border
        {
            Width = 48,
            Height = 48,
            Background = System.Windows.Media.Brushes.White,
            Child = image,
        };
    }

    private static T Resource<T>(string key) where T : class =>
        Application.Current?.TryFindResource(key) as T
        ?? throw new InvalidOperationException($"Application resource '{key}' was not found.");

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
