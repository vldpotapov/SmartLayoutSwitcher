using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Navigation;

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

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
