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

        var styles = new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/SmartLayoutSwitcher.App;component/Tray/TrayMenuStyles.xaml"),
        };

        var menu = new ContextMenu { Style = (Style)styles["TrayContextMenuStyle"] };

        var header = CreateItem("Smart Layout Switcher", CreateBrandIcon(), styles, interactive: false);
        (_current, _currentText, _pairText) = CreateStatusItem(styles);

        var settings = CreateItem("Settings", CreateSvgIcon("/SmartLayoutSwitcher.App;component/Resources/Settings/general-dark.svg"), styles);
        settings.Click += (_, _) => SettingsRequested?.Invoke();

        var exit = CreateItem("Quit", CreateSvgIcon("/SmartLayoutSwitcher.App;component/Resources/Tray/quit-dark.svg"), styles);
        exit.Click += (_, _) => ExitRequested?.Invoke();

        menu.Items.Add(header);
        menu.Items.Add(_current);
        menu.Items.Add(settings);
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

    private static MenuItem CreateItem(object header, object icon, ResourceDictionary styles, bool interactive = true) => new()
    {
        Header = header,
        Icon = icon,
        Style = (Style)styles["TrayMenuItemStyle"],
        IsHitTestVisible = interactive,
        Focusable = interactive,
    };

    private static (MenuItem Item, TextBlock Current, TextBlock Pair) CreateStatusItem(ResourceDictionary styles)
    {
        var current = new TextBlock
        {
            Text = "Current: —",
        };
        var pair = new TextBlock
        {
            Text = "Pair: —",
        };
        var header = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        header.Children.Add(current);
        header.Children.Add(pair);

        var item = CreateItem(header, CreateSvgIcon("/SmartLayoutSwitcher.App;component/Resources/Settings/switch-dark.svg"), styles, interactive: false);
        return (item, current, pair);
    }

    private static Image CreateSvgIcon(string source) => new()
    {
        Width = 18,
        Height = 18,
        Source = SvgImageSource.Load(source),
    };

    private static Image CreateBrandIcon() => new()
    {
        Width = 18,
        Height = 18,
        Stretch = System.Windows.Media.Stretch.Uniform,
        Source = new BitmapImage(new Uri("pack://application:,,,/SmartLayoutSwitcher.App;component/Resources/Lang-icon.png")),
    };

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
