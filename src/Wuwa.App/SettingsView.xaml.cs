using System.Windows;
using System.Windows.Controls;

namespace Wuwa.App;

public partial class SettingsView : UserControl
{
    private Action? _toggleTheme;
    private Action? _checkUpdate;
    private Action? _showSystemHelp;

    public SettingsView() => InitializeComponent();

    public void Configure(Action toggleTheme, Action checkUpdate, Action showSystemHelp)
    {
        _toggleTheme = toggleTheme;
        _checkUpdate = checkUpdate;
        _showSystemHelp = showSystemHelp;
    }

    public void SetThemeButtonContent(string content) => ThemeButton.Content = content;

    private void Theme_OnClick(object sender, RoutedEventArgs e) => _toggleTheme?.Invoke();
    private void Update_OnClick(object sender, RoutedEventArgs e) => _checkUpdate?.Invoke();
    private void SystemHelp_OnClick(object sender, RoutedEventArgs e) => _showSystemHelp?.Invoke();
}
