using System.Windows;
using System.Windows.Controls;
using Wuwa.Core;

namespace Wuwa.App;

public partial class DashboardView : UserControl
{
    private Action? _openAchievements;
    private Action? _openRotation;
    private Action? _openGameTools;

    public DashboardView() => InitializeComponent();

    public void Configure(Action openAchievements, Action openRotation, Action openGameTools)
    {
        _openAchievements = openAchievements;
        _openRotation = openRotation;
        _openGameTools = openGameTools;
    }

    public void ApplySnapshot(WorkspaceSnapshot snapshot, string? selectedRotationName)
    {
        var statistics = snapshot.Statistics;
        TotalText.Text = statistics.Total.ToString();
        CompletedText.Text = statistics.Completed.ToString();
        RateText.Text = $"{statistics.CompletionRatePercent:0.0}%";
        TrackedText.Text = snapshot.Metadata.EffectiveTrackedAchievementIds.Count.ToString();
        RotationSummaryText.Text = string.IsNullOrWhiteSpace(selectedRotationName) ? "尚未选择连招流程。" : $"当前流程：{selectedRotationName}";
    }

    private void OpenAchievements_OnClick(object sender, RoutedEventArgs e) => _openAchievements?.Invoke();
    private void OpenRotation_OnClick(object sender, RoutedEventArgs e) => _openRotation?.Invoke();
    private void OpenGameTools_OnClick(object sender, RoutedEventArgs e) => _openGameTools?.Invoke();
}
