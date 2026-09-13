using System.IO;
using System.Windows;
using SmartLayoutSwitcher.App.Diagnostics;
using SmartLayoutSwitcher.App.Services;
using SmartLayoutSwitcher.App.Settings;
using SmartLayoutSwitcher.App.Tray;

namespace SmartLayoutSwitcher.App;

public partial class App : Application
{
    private readonly Mutex _mutex = new(initiallyOwned: true, name: "SmartLayoutSwitcher.SingleInstance");
    private bool _ownsMutex;
    private bool _shuttingDown;

    private AppSettings? _settings;
    private Logger? _log;
    private LayoutSwitcherService? _service;
    private TrayIcon? _tray;
    private SettingsWindow? _settingsWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (!_mutex.WaitOne(0, false))
        {
            Shutdown(); // another instance is already running
            return;
        }
        _ownsMutex = true;

        _settings = AppSettings.Load();
        if (string.IsNullOrWhiteSpace(_settings.GitHubProjectUrl))
        {
            _settings.GitHubProjectUrl = AppSettings.DefaultGitHubProjectUrl;
            _settings.Save();
        }
        _settings.StartWithWindows = Autostart.IsRegistered();

        var logPath = Path.Combine(AppSettings.DirectoryPath, "smart-layout-switcher.log");
        _log = new Logger(_settings.DebugLogEnabled, logPath);
        _log.Info("=== Smart Layout Switcher starting ===");

        _service = new LayoutSwitcherService(_settings, _log);
        _service.StatusChanged += s =>
        {
            _log.Debug($"Status: current={s.CurrentCode}, pair={s.PairText}, enabled={s.Enabled}");
            _tray?.UpdateStatus(s.CurrentCode, s.PairText);
        };

        _tray = new TrayIcon(enabled: _settings.Enabled);
        _tray.EnabledChanged += value =>
        {
            _settings.Enabled = value;
            _settings.Save();
            if (_service is not null)
                _service.Enabled = value;
        };
        _tray.SettingsRequested += ShowSettings;
        _tray.ExitRequested += RequestExit;

        _service.Enabled = _settings.Enabled;
        _service.PublishStatus();

        _log.Info("Ready. Short press LAlt+LShift toggles the pair; long press opens the layout popup.");
    }

    private void ShowSettings()
    {
        if (_settings is null)
            return;

        if (_settingsWindow is { IsVisible: true })
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(_settings);
        _settingsWindow.SettingsSaved += () =>
        {
            if (_service is not null)
                _service.Enabled = _settings.Enabled;
            Autostart.Set(_settings.StartWithWindows);
            _tray?.SetEnabled(_settings.Enabled);
            _service?.PublishStatus();
        };
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
    }

    private void RequestExit()
    {
        if (_shuttingDown)
            return;
        _shuttingDown = true;
        _log?.Info("Exit requested via tray.");
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_ownsMutex)
        {
            _service?.Dispose();
            _tray?.Dispose();
            _log?.Info("=== stopped ===");
            _log?.Dispose();
            try
            {
                _mutex.ReleaseMutex();
            }
            catch
            {
                // ignore
            }
        }

        base.OnExit(e);
    }
}
