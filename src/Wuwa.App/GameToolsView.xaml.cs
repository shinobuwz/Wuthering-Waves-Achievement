using System.Windows;
using System.Windows.Controls;

namespace Wuwa.App;

public partial class GameToolsView : UserControl
{
    private Action? _openConveneLink;
    private Action? _showConveneHelp;
    private Func<Task>? _toggleMap;

    public GameToolsView() => InitializeComponent();

    public void Configure(Action openConveneLink, Action showConveneHelp, Func<Task> toggleMap)
    {
        _openConveneLink = openConveneLink;
        _showConveneHelp = showConveneHelp;
        _toggleMap = toggleMap;
    }

    public void SetMapShortcut(string label) => MapShortcutText.Text = $"地图快捷键：{label}";
    public void SetMapButtonContent(string content) => MapOverlayButton.Content = content;

    private void ConveneLink_OnClick(object sender, RoutedEventArgs e) => _openConveneLink?.Invoke();
    private void ConveneHelp_OnClick(object sender, RoutedEventArgs e) => _showConveneHelp?.Invoke();
    private async void MapOverlay_OnClick(object sender, RoutedEventArgs e)
    {
        if (_toggleMap is not null) await _toggleMap();
    }
}
