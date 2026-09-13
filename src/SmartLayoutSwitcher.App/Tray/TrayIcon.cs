using System.Windows;
using System.Windows.Controls;
using Hardcodet.Wpf.TaskbarNotification;

namespace SmartLayoutSwitcher.App.Tray;

/// <summary>
/// System tray icon + context menu (spec §21). Pure WPF via Hardcodet.NotifyIcon.Wpf,
/// so no extra WinForms message loop is needed.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly TaskbarIcon _icon = new();
    private readonly MenuItem _current;
    private readonly MenuItem _pair;

    public event Action? ExitRequested;
    public event Action? SettingsRequested;

    public TrayIcon()
    {
        _icon.Icon = TrayIconFactory.Create();
        _icon.ToolTipText = "Smart Layout Switcher";

        var menu = new ContextMenu();

        var header = new MenuItem { Header = "Smart Layout Switcher", IsEnabled = false };
        _current = CreateHeaderItem("Current: —");
        _pair = CreateHeaderItem("Pair: —");

        var exit = new MenuItem { Header = "Exit" };
        exit.Click += (_, _) => ExitRequested?.Invoke();

        var settings = new MenuItem { Header = "Settings…" };
        settings.Click += (_, _) => SettingsRequested?.Invoke();

        menu.Items.Add(header);
        menu.Items.Add(new Separator());
        menu.Items.Add(_current);
        menu.Items.Add(_pair);
        menu.Items.Add(new Separator());
        menu.Items.Add(settings);
        menu.Items.Add(new Separator());
        menu.Items.Add(exit);

        _icon.ContextMenu = menu;
    }

    public void UpdateStatus(string current, string pair)
    {
        var invoke = () =>
        {
            _current.Header = $"Current: {current}";
            _pair.Header = $"Pair: {pair}";
        };

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            invoke();
        else
            dispatcher.BeginInvoke(invoke);
    }

    private static MenuItem CreateHeaderItem(string header) =>
        new() { Header = header, IsEnabled = false };

    public void Dispose() => _icon.Dispose();
}
