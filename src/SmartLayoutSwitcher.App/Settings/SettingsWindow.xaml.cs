using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace SmartLayoutSwitcher.App.Settings;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;

    public event Action? SettingsSaved;

    public SettingsWindow(AppSettings settings)
    {
        _settings = settings;
        InitializeComponent();

        EnabledCheckBox.IsChecked = settings.Enabled;
        RememberWindowCheckBox.IsChecked = settings.RememberPerWindow;
        PopupCheckBox.IsChecked = settings.ShowPopupOnLongPress;
        StartWithWindowsCheckBox.IsChecked = settings.StartWithWindows;
        GitHubUrlTextBox.Text = settings.GitHubProjectUrl;

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
        _settings.GitHubProjectUrl = GitHubUrlTextBox.Text.Trim();
        if (HotkeyComboBox.SelectedItem is ComboBoxItem { Tag: HotkeyMode mode })
            _settings.Hotkey = mode;

        _settings.Save();
        SettingsSaved?.Invoke();
        Close();
    }

    private void OpenGitHub_Click(object sender, RoutedEventArgs e)
    {
        if (!Uri.TryCreate(GitHubUrlTextBox.Text.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            MessageBox.Show(this, "Enter a valid GitHub project URL first.", "GitHub project", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }
}
