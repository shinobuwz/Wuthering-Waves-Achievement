using System.Windows;
using System.Windows.Controls;

namespace Wuwa.App;

public partial class GameToolsView : UserControl
{
    private Action? _openConveneLink;
    private Action? _showConveneHelp;
    private Func<Task>? _toggleMap;
    private Action? _captureSceneMarker;

    public GameToolsView() => InitializeComponent();

    public void Configure(
        Action openConveneLink,
        Action showConveneHelp,
        Func<Task> toggleMap,
        Action captureSceneMarker)
    {
        _openConveneLink = openConveneLink;
        _showConveneHelp = showConveneHelp;
        _toggleMap = toggleMap;
        _captureSceneMarker = captureSceneMarker;
    }

    public void SetMapShortcut(string label) => MapShortcutText.Text = $"地图快捷键：{label}";
    public void SetMapButtonContent(string content) => MapOverlayButton.Content = content;
    public void SetMapButtonEnabled(bool enabled) => MapOverlayButton.IsEnabled = enabled;
    public void SetSceneMarkerVisibility(Visibility visibility) => SceneMarkerLabCard.Visibility = visibility;
    public void SetSceneMarkerButtonEnabled(bool enabled) => SceneMarkerLabButton.IsEnabled = enabled;

    private void ConveneLink_OnClick(object sender, RoutedEventArgs e) => _openConveneLink?.Invoke();
    private void ConveneHelp_OnClick(object sender, RoutedEventArgs e) => _showConveneHelp?.Invoke();
    private async void MapOverlay_OnClick(object sender, RoutedEventArgs e)
    {
        if (_toggleMap is not null) await _toggleMap();
    }

    private void SceneMarkerLab_OnClick(object sender, RoutedEventArgs e) => _captureSceneMarker?.Invoke();
}
