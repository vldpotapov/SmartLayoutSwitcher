using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Navigation;
using SmartLayoutSwitcher.App.Services;

namespace SmartLayoutSwitcher.App.Settings;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly UpdateService _updateService = new();
    private UpdateInfo? _availableUpdate;

    public event Action? SettingsSaved;

    public SettingsWindow(AppSettings settings)
    {
        _settings = settings;
        InitializeComponent();

        EnabledCheckBox.IsChecked = settings.Enabled;
        RememberWindowCheckBox.IsChecked = settings.RememberPerWindow;
        PopupCheckBox.IsChecked = settings.ShowPopupOnLongPress;
        StartWithWindowsCheckBox.IsChecked = settings.StartWithWindows;
        GitHubLink.NavigateUri = new Uri(AppSettings.DefaultGitHubProjectUrl);
        GitHubLink.Inlines.Add(new Run(AppSettings.DefaultGitHubProjectUrl));
        var version = typeof(SettingsWindow).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), inherit: false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()?.InformationalVersion
            ?? typeof(SettingsWindow).Assembly.GetName().Version?.ToString(3)
            ?? "—";
        version = version.Split('+', 2)[0];
        VersionText.Text = $"Version {version}";

        for (var i = 0; i < HotkeyComboBox.Items.Count; i++)
        {
            if (HotkeyComboBox.Items[i] is ComboBoxItem item && item.Tag is HotkeyMode mode && mode == settings.Hotkey)
            {
                HotkeyComboBox.SelectedIndex = i;
                break;
            }
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _settings.Enabled = EnabledCheckBox.IsChecked == true;
        _settings.RememberPerWindow = RememberWindowCheckBox.IsChecked == true;
        _settings.ShowPopupOnLongPress = PopupCheckBox.IsChecked == true;
        _settings.StartWithWindows = StartWithWindowsCheckBox.IsChecked == true;
        if (HotkeyComboBox.SelectedItem is ComboBoxItem { Tag: HotkeyMode mode })
            _settings.Hotkey = mode;

        _settings.Save();
        SettingsSaved?.Invoke();
        Close();
    }

    private void GitHubLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    private async void CheckForUpdates_Click(object sender, RoutedEventArgs e)
    {
        if (_availableUpdate is not null)
        {
            await DownloadAvailableUpdateAsync(_availableUpdate);
            return;
        }

        CheckForUpdatesButton.IsEnabled = false;
        UpdateStatusText.Text = "Checking…";

        var installedVersion = NormalizeVersion(typeof(SettingsWindow).Assembly.GetName().Version);
        var result = await _updateService.CheckAsync(installedVersion);

        switch (result.Status)
        {
            case UpdateCheckStatus.UpToDate:
                UpdateStatusText.Text = "You’re up to date.";
                break;
            case UpdateCheckStatus.UpdateAvailable when result.Update is not null:
                _availableUpdate = result.Update;
                CheckForUpdatesButton.Content = $"Download {result.Update.Version}";
                UpdateStatusText.Text = "A new version is available.";
                break;
            default:
                UpdateStatusText.Text = "Couldn’t check for updates.";
                break;
        }

        CheckForUpdatesButton.IsEnabled = true;
    }

    private async Task DownloadAvailableUpdateAsync(UpdateInfo update)
    {
        CheckForUpdatesButton.IsEnabled = false;
        UpdateStatusText.Text = "Downloading installer…";

        try
        {
            var installerPath = await _updateService.DownloadInstallerAsync(update);
            UpdateStatusText.Text = "Installer downloaded to Downloads.";
            CheckForUpdatesButton.Content = "Download complete";
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{installerPath}\"") { UseShellExecute = true });
        }
        catch (Exception)
        {
            UpdateStatusText.Text = "Download failed. Try again.";
            CheckForUpdatesButton.Content = $"Download {update.Version}";
            CheckForUpdatesButton.IsEnabled = true;
        }
    }

    private static Version NormalizeVersion(Version? version) =>
        version is null
            ? new Version(0, 0, 0)
            : new Version(version.Major, version.Minor, Math.Max(0, version.Build));

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
