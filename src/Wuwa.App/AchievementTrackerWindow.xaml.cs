using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Wuwa.Core;

namespace Wuwa.App;

public partial class AchievementTrackerWindow : Window
{
    private readonly AchievementWorkspace _workspace;
    private readonly Action _restoreWorkspace;
    private readonly Action<WorkspaceSnapshot> _stateChanged;
    private WorkspaceSnapshot _snapshot;

    public AchievementTrackerWindow(
        AchievementWorkspace workspace,
        WorkspaceSnapshot snapshot,
        Action restoreWorkspace,
        Action<WorkspaceSnapshot> stateChanged)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        _restoreWorkspace = restoreWorkspace ?? throw new ArgumentNullException(nameof(restoreWorkspace));
        _stateChanged = stateChanged ?? throw new ArgumentNullException(nameof(stateChanged));
        InitializeComponent();
        Left = Math.Max(SystemParameters.WorkArea.Left + 16, SystemParameters.WorkArea.Right - Width - 24);
        Top = Math.Max(SystemParameters.WorkArea.Top + 16, SystemParameters.WorkArea.Bottom - Height - 24);
        RefreshItems();
    }

    public void ApplySnapshot(WorkspaceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _snapshot = snapshot;
        if (IsInitialized)
        {
            RefreshItems();
        }
    }

    private void RefreshItems()
    {
        var rowsById = _snapshot.Rows.ToDictionary(row => row.Id);
        var tracked = _snapshot.Metadata.EffectiveTrackedAchievementIds
            .Select(id => rowsById.GetValueOrDefault(id))
            .Where(row => row is not null)
            .Cast<AchievementRow>()
            .Where(row => row.Status == ProgressStatus.Incomplete)
            .Select(row => new TrackerItemViewModel(row))
            .ToArray();

        ExpandedTrackedItems.ItemsSource = tracked.Take(5).ToArray();
        CompactTrackedItems.ItemsSource = tracked.Skip(5).ToArray();
        TrackedCountText.Text = $"追踪 {tracked.Length} 条";
        TrackedHeaderText.Visibility = tracked.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        var searchText = SearchBox?.Text.Trim() ?? string.Empty;
        var searchResults = string.IsNullOrWhiteSpace(searchText)
            ? Array.Empty<TrackerItemViewModel>()
            : _workspace.Query(new AchievementQuery(
                    NameSearchText: searchText,
                    Status: ProgressStatus.Incomplete,
                    Sort: AchievementSort.IncompleteFirst))
                .Rows
                .Select(row => new TrackerItemViewModel(row))
                .ToArray();

        SearchResultItems.ItemsSource = searchResults;
        SearchHeaderText.Visibility = string.IsNullOrWhiteSpace(searchText) ? Visibility.Collapsed : Visibility.Visible;
        SearchHeaderText.Text = $"搜索结果（{searchResults.Length}）";
        EmptyText.Text = tracked.Length == 0 && searchResults.Length == 0
            ? string.IsNullOrWhiteSpace(searchText) ? "暂无追踪成就\n可返回工作区批量加入追踪" : "没有匹配的未完成成就"
            : string.Empty;
        EmptyText.Visibility = tracked.Length == 0 && searchResults.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        ErrorText.Text = string.Empty;
    }

    private void SearchBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        SearchPlaceholder.Visibility = string.IsNullOrWhiteSpace(SearchBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
        RefreshItems();
    }

    private async void Complete_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: AchievementId id } button)
        {
            return;
        }

        button.IsEnabled = false;
        ErrorText.Text = string.Empty;
        try
        {
            var result = await _workspace.ChangeStatusAsync(id, ProgressStatus.Completed);
            if (!result.IsSuccess)
            {
                ErrorText.Text = result.Error?.Message ?? "完成成就失败。";
                return;
            }

            ApplySnapshot(result.Snapshot);
            _stateChanged(result.Snapshot);
        }
        catch (Exception exception)
        {
            ErrorText.Text = $"完成成就失败：{exception.Message}";
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private async void ClearTracked_OnClick(object sender, RoutedEventArgs e)
    {
        var ids = _snapshot.Metadata.EffectiveTrackedAchievementIds.ToArray();
        if (ids.Length == 0)
        {
            ErrorText.Text = "当前没有追踪成就。";
            return;
        }

        var answer = MessageBox.Show(
            this,
            $"确定清空当前 {ids.Length} 条追踪成就吗？\n\n只会清空追踪列表，不会改变成就完成状态。",
            "清空追踪列表",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes || sender is not Button button)
        {
            return;
        }

        button.IsEnabled = false;
        try
        {
            var result = await _workspace.RemoveTrackedAchievementsAsync(ids);
            if (!result.IsSuccess)
            {
                ErrorText.Text = result.Error?.Message ?? "清空追踪列表失败。";
                return;
            }

            ApplySnapshot(result.Snapshot);
            _stateChanged(result.Snapshot);
        }
        catch (Exception exception)
        {
            ErrorText.Text = $"清空追踪列表失败：{exception.Message}";
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is Button || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // The window may be closing while the title bar receives its final mouse event.
        }
    }

    private void ExpandWorkspace_OnClick(object sender, RoutedEventArgs e) => _restoreWorkspace();

    private void Close_OnClick(object sender, RoutedEventArgs e) => Close();

    private void ReturnToGame_OnClick(object sender, RoutedEventArgs e) => _restoreWorkspace();
}
