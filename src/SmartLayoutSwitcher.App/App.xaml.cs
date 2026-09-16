using System.IO;
using System.Windows;
using Microsoft.Win32;
using SmartLayoutSwitcher.App.Diagnostics;
using SmartLayoutSwitcher.App.Services;
using SmartLayoutSwitcher.App.Settings;
using SmartLayoutSwitcher.App.Tray;

namespace SmartLayoutSwitcher.App;

public partial class App : Application
{
    private const string ShutdownEventName = "SmartLayoutSwitcher.ShutdownRequested";
    private readonly Mutex _mutex = new(initiallyOwned: true, name: "SmartLayoutSwitcher.SingleInstance");
    private readonly EventWaitHandle _shutdownRequest = new(
        initialState: false,
        mode: EventResetMode.AutoReset,
        name: ShutdownEventName);
    private bool _ownsMutex;
    private bool _shuttingDown;
    private Thread? _shutdownListener;

    private AppSettings? _settings;
    private Logger? _log;
    private LayoutSwitcherService? _service;
    private UpdateCoordinator? _updates;
    private TrayIcon? _tray;
    private SettingsWindow? _settingsWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (!_mutex.WaitOne(0, false))
        {
            if (e.Args.Any(argument => string.Equals(argument, "--shutdown", StringComparison.OrdinalIgnoreCase)))
                _shutdownRequest.Set();

            Shutdown(); // another instance is already running
            return;
        }
        _ownsMutex = true;

        _shutdownListener = new Thread(WaitForShutdownRequest)
        {
            IsBackground = true,
            Name = "SmartLayoutSwitcher shutdown listener"
        };
        _shutdownListener.Start();

        _settings = AppSettings.Load();
        ApplyInstallerHotkey(_settings);
        if (string.IsNullOrWhiteSpace(_settings.GitHubProjectUrl))
        {
            _settings.GitHubProjectUrl = AppSettings.DefaultGitHubProjectUrl;
            _settings.Save();
        }
        _settings.StartWithWindows = Autostart.IsRegistered();

        var logPath = Path.Combine(AppSettings.DirectoryPath, "smart-layout-switcher.log");
        _log = new Logger(_settings.DebugLogEnabled, logPath);
        _log.Info("=== Smart Layout Switcher starting ===");

        var installedVersion = typeof(App).Assembly.GetName().Version ?? new Version(0, 0, 0);
        _updates = new UpdateCoordinator(_settings, installedVersion);
        _updates.UpdateAvailabilityChanged += OnUpdateAvailabilityChanged;

        _service = new LayoutSwitcherService(_settings, _log);
        _service.StatusChanged += s =>
        {
            _log.Debug($"Status: current={s.CurrentCode}, pair={s.PairText}, enabled={s.Enabled}");
            _tray?.UpdateStatus(s.CurrentCode, s.PairText);
        };

        _tray = new TrayIcon(_updates.AvailableUpdate is not null);
        _tray.SettingsRequested += ShowSettings;
        _tray.ExitRequested += RequestExit;

        _service.Enabled = _settings.Enabled;
        _service.PublishStatus();

        _ = _updates.CheckAsync();

        _log.Info("Ready. Short press LAlt+LShift toggles the pair; long press opens the layout popup.");
    }

    private static void ApplyInstallerHotkey(AppSettings settings)
    {
        const string registryPath = @"Software\SmartLayoutSwitcher";

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(registryPath, writable: true);
            var selectedHotkey = key?.GetValue("InstallerHotkey") as string;

            if (selectedHotkey is not ("WinSpace" or "LeftAltLeftShift"))
                return;

            settings.Hotkey = selectedHotkey == "WinSpace"
                ? HotkeyMode.WinSpace
                : HotkeyMode.LeftAltLeftShift;
            settings.Save();
            key!.DeleteValue("InstallerHotkey", throwOnMissingValue: false);
        }
        catch
        {
            // An installer preference is optional; startup must never depend on it.
        }
    }

    private void ShowSettings()
    {
        if (_settings is null || _updates is null)
            return;

        if (_settingsWindow is { IsVisible: true })
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(_settings, _updates);
        _settingsWindow.SettingsSaved += () =>
        {
            if (_service is not null)
                _service.Enabled = _settings.Enabled;
            Autostart.Set(_settings.StartWithWindows);
            _service?.PublishStatus();
        };
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
    }

    private void OnUpdateAvailabilityChanged(UpdateInfo? update)
    {
        _tray?.SetUpdateAvailable(update is not null);
        _settingsWindow?.SetAvailableUpdate(update);
        _log?.Info(update is null
            ? "No update is available."
            : $"Update {update.Version} is available.");
    }

    private void RequestExit()
    {
        if (_shuttingDown)
            return;
        _shuttingDown = true;
        _log?.Info("Exit requested via tray.");
        Shutdown();
    }

    private void WaitForShutdownRequest()
    {
        _shutdownRequest.WaitOne();
        Dispatcher.BeginInvoke(RequestExit);
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

        _shutdownRequest.Dispose();

        base.OnExit(e);
    }
}
