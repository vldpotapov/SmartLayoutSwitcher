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
    private readonly UpdateCoordinator _updates;
    private UpdateInfo? _availableUpdate;

    public event Action? SettingsSaved;

    public SettingsWindow(AppSettings settings, UpdateCoordinator updates)
    {
        _settings = settings;
        _updates = updates;
        InitializeComponent();

        EnabledCheckBox.IsChecked = settings.Enabled;
        RememberWindowCheckBox.IsChecked = settings.RememberPerWindow;
        PopupCheckBox.IsChecked = settings.ShowPopupOnLongPress;
        SelectedTextConversionCheckBox.IsChecked = settings.EnableSelectedTextConversion;
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

        SetAvailableUpdate(_updates.AvailableUpdate);

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
        _settings.EnableSelectedTextConversion = SelectedTextConversionCheckBox.IsChecked == true;
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
            OpenReleasePage(_availableUpdate);
            return;
        }

        CheckForUpdatesButton.IsEnabled = false;
        UpdateStatusText.Text = "Checking…";

        var result = await _updates.CheckAsync(force: true);

        switch (result.Status)
        {
            case UpdateCheckStatus.UpToDate:
                UpdateStatusText.Text = "You’re up to date.";
                break;
            case UpdateCheckStatus.UpdateAvailable when result.Update is not null:
                SetAvailableUpdate(result.Update);
                break;
            default:
                UpdateStatusText.Text = "Couldn’t check for updates.";
                break;
        }

        CheckForUpdatesButton.IsEnabled = true;
    }

    public void SetAvailableUpdate(UpdateInfo? update)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => SetAvailableUpdate(update));
            return;
        }

        _availableUpdate = update;
        if (update is not null)
        {
            CheckForUpdatesButton.Content = "Open download page";
            UpdateStatusText.Text = $"Version {update.Version} is available.";
            return;
        }

        CheckForUpdatesButton.Content = "Check for updates";
        UpdateStatusText.Text = _updates.IsUsingFreshCache && !_updates.LastCheckFailed
            ? "You’re up to date."
            : string.Empty;
    }

    private void OpenReleasePage(UpdateInfo update)
    {
        try
        {
            Process.Start(new ProcessStartInfo(update.ReleasePageUri.AbsoluteUri) { UseShellExecute = true });
            UpdateStatusText.Text = "The release page was opened in your browser.";
        }
        catch (Exception)
        {
            UpdateStatusText.Text = "Couldn’t open the release page.";
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
