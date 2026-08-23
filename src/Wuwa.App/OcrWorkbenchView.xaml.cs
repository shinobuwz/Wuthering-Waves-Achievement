using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Wuwa.Core;

namespace Wuwa.App;

public partial class OcrWorkbenchView : UserControl
{
    private Action? _scanCurrent;
    private Action? _scanFull;
    private Action? _searchSync;
    private Action? _openPagingSettings;
    private Action? _back;
    private Func<bool, Task<bool>>? _setSkipPreviouslyScanned;
    private Func<Task<bool>>? _clearHistory;
    private Func<IReadOnlyList<OcrTagSelection>, bool, Task<bool>>? _setTagsMarked;
    private Func<IReadOnlyList<OcrAchievementCandidate>, Task<bool>>? _applyResults;
    private IReadOnlyList<OcrResultViewRow> _allResultRows = Array.Empty<OcrResultViewRow>();
    private IReadOnlyList<OcrTagViewRow> _allTagRows = Array.Empty<OcrTagViewRow>();
    private OcrScanPreview? _pendingPreview;
    private bool _updating;

    public OcrWorkbenchView()
    {
        InitializeComponent();
        _updating = true;
        try
        {
            ResultStatusCombo.ItemsSource = new[] { "全部状态", "已完成", "未完成", "未知", "歧义" };
            ResultStatusCombo.SelectedIndex = 0;
            TagMarkedCombo.ItemsSource = new[] { "全部 Tag", "已标记跳过", "未标记" };
            TagMarkedCombo.SelectedIndex = 0;
            ResultPrimaryCombo.ItemsSource = new[] { "全部一级分类" };
            ResultSecondaryCombo.ItemsSource = new[] { "全部二级分类" };
            ResultPrimaryCombo.SelectedIndex = 0;
            ResultSecondaryCombo.SelectedIndex = 0;
            TagPrimaryCombo.ItemsSource = new[] { "全部一级 Tag" };
            TagPrimaryCombo.SelectedIndex = 0;
        }
        finally
        {
            _updating = false;
        }
        RefreshResultFilter();
        RefreshTagFilter();
        ShowWorkbenchPage(OcrWorkbenchSection.Results);
    }

    public void Configure(
        Action scanCurrent,
        Action scanFull,
        Action searchSync,
        Action openPagingSettings,
        Action back,
        Func<bool, Task<bool>> setSkipPreviouslyScanned,
        Func<Task<bool>> clearHistory,
        Func<IReadOnlyList<OcrTagSelection>, bool, Task<bool>> setTagsMarked,
        Func<IReadOnlyList<OcrAchievementCandidate>, Task<bool>> applyResults)
    {
        _scanCurrent = scanCurrent ?? throw new ArgumentNullException(nameof(scanCurrent));
        _scanFull = scanFull ?? throw new ArgumentNullException(nameof(scanFull));
        _searchSync = searchSync ?? throw new ArgumentNullException(nameof(searchSync));
        _openPagingSettings = openPagingSettings ?? throw new ArgumentNullException(nameof(openPagingSettings));
        _back = back ?? throw new ArgumentNullException(nameof(back));
        _setSkipPreviouslyScanned = setSkipPreviouslyScanned ?? throw new ArgumentNullException(nameof(setSkipPreviouslyScanned));
        _clearHistory = clearHistory ?? throw new ArgumentNullException(nameof(clearHistory));
        _setTagsMarked = setTagsMarked ?? throw new ArgumentNullException(nameof(setTagsMarked));
        _applyResults = applyResults ?? throw new ArgumentNullException(nameof(applyResults));
    }

    public void ApplyWorkspaceState(OcrScanHistory history, IReadOnlyList<AchievementRow> rows)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(rows);
        _updating = true;
        try
        {
            SkipScannedCheckBox.IsChecked = history.SkipPreviouslyScanned;
            var historyByKey = history.EffectiveCategories.ToDictionary(
                item => OcrTagSelection.BuildKey(item.PrimaryName, item.SecondaryName),
                StringComparer.Ordinal);
            _allTagRows = rows
                .Select(row => new OcrTagSelection(row.FirstCategory, row.SecondCategory))
                .DistinctBy(item => item.Key)
                .OrderBy(item => item.PrimaryName, StringComparer.Ordinal)
                .ThenBy(item => item.SecondaryName, StringComparer.Ordinal)
                .Select(item => historyByKey.TryGetValue(item.Key, out var scanned)
                    ? new OcrTagViewRow(item.PrimaryName, item.SecondaryName, true, scanned.ScannedAtUtc, scanned.Pages)
                    : new OcrTagViewRow(item.PrimaryName, item.SecondaryName, false, null, 0))
                .ToArray();
            var primaryNames = _allTagRows.Select(row => row.PrimaryName).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal);
            TagPrimaryCombo.ItemsSource = new[] { "全部一级 Tag" }.Concat(primaryNames).ToArray();
            if (TagPrimaryCombo.SelectedIndex < 0) TagPrimaryCombo.SelectedIndex = 0;
            HistorySummaryText.Text = $"已标记一级分类 {history.PrimaryCategoryCount} 个，一级 / 二级组合 {history.EffectiveCategories.Count} 个。";
        }
        finally
        {
            _updating = false;
        }
        RefreshTagFilter();
    }

    public void SetScanResults(
        OcrScanPreview preview,
        IReadOnlyList<AchievementRow> rows,
        string sourceLabel)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(rows);
        _pendingPreview = preview;
        foreach (var existingRow in _allResultRows) existingRow.PropertyChanged -= ResultRow_OnPropertyChanged;
        var byId = rows.ToDictionary(row => row.Id);
        _allResultRows = preview.Candidates
            .Select(candidate =>
            {
                byId.TryGetValue(candidate.AchievementId, out var row);
                return new OcrResultViewRow(candidate, row?.FirstCategory ?? string.Empty, row?.SecondCategory ?? string.Empty);
            })
            .OrderBy(row => row.FirstCategory, StringComparer.Ordinal)
            .ThenBy(row => row.SecondCategory, StringComparer.Ordinal)
            .ThenBy(row => row.Candidate.LegacyCode, StringComparer.Ordinal)
            .ToArray();
        foreach (var resultRow in _allResultRows) resultRow.PropertyChanged += ResultRow_OnPropertyChanged;

        _updating = true;
        try
        {
            ResultPrimaryCombo.ItemsSource = new[] { "全部一级分类" }
                .Concat(_allResultRows.Select(row => row.FirstCategory).Where(value => value.Length > 0).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal))
                .ToArray();
            ResultSecondaryCombo.ItemsSource = new[] { "全部二级分类" }
                .Concat(_allResultRows.Select(row => row.SecondCategory).Where(value => value.Length > 0).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal))
                .ToArray();
            ResultPrimaryCombo.SelectedIndex = 0;
            ResultSecondaryCombo.SelectedIndex = 0;
            ResultStatusCombo.SelectedIndex = 0;
            ResultSearchBox.Text = string.Empty;
        }
        finally
        {
            _updating = false;
        }

        UnmatchedSummaryText.Text = preview.Unmatched.Count == 0
            ? "没有未匹配文字。"
            : $"未匹配 {preview.Unmatched.Count} 条：{string.Join("；", preview.Unmatched.Take(6).Select(item => item.Text))}";
        StatusText.Text = $"{sourceLabel}，结果尚未写入。请筛选、勾选后点击“应用勾选结果”。";
        ResultsViewButton.IsChecked = true;
        ShowWorkbenchPage(OcrWorkbenchSection.Results);
        RefreshResultFilter();
    }

    public void SetStatus(string message)
    {
        StatusText.Text = message;
    }

    private void AuthorBilibili_OnClick(object sender, RoutedEventArgs e) =>
        App.OpenAuthorBilibili(Window.GetWindow(this));

    private void ScanCurrent_OnClick(object sender, RoutedEventArgs e) => _scanCurrent?.Invoke();
    private void ScanFull_OnClick(object sender, RoutedEventArgs e) => _scanFull?.Invoke();
    private void SearchSync_OnClick(object sender, RoutedEventArgs e) => _searchSync?.Invoke();
    private void PagingSettings_OnClick(object sender, RoutedEventArgs e) => _openPagingSettings?.Invoke();
    private void Back_OnClick(object sender, RoutedEventArgs e) => _back?.Invoke();
    private void ResultsView_OnChecked(object sender, RoutedEventArgs e) => ShowWorkbenchPage(OcrWorkbenchSection.Results);
    private void TagsView_OnChecked(object sender, RoutedEventArgs e) => ShowWorkbenchPage(OcrWorkbenchSection.Tags);
    private void HelpView_OnClick(object sender, RoutedEventArgs e) => ShowWorkbenchPage(OcrWorkbenchSection.Help);

    private void ShowWorkbenchPage(OcrWorkbenchSection section)
    {
        if (ResultsPage is null || TagsPage is null || HelpPage is null) return;
        if (section == OcrWorkbenchSection.Help)
        {
            ResultsViewButton.IsChecked = false;
            TagsViewButton.IsChecked = false;
        }
        ResultsPage.Visibility = section == OcrWorkbenchSection.Results ? Visibility.Visible : Visibility.Collapsed;
        TagsPage.Visibility = section == OcrWorkbenchSection.Tags ? Visibility.Visible : Visibility.Collapsed;
        HelpPage.Visibility = section == OcrWorkbenchSection.Help ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void SkipScanned_OnClick(object sender, RoutedEventArgs e)
    {
        if (_updating || _setSkipPreviouslyScanned is null) return;
        var requested = SkipScannedCheckBox.IsChecked == true;
        SkipScannedCheckBox.IsEnabled = false;
        try
        {
            if (!await _setSkipPreviouslyScanned(requested))
            {
                _updating = true;
                SkipScannedCheckBox.IsChecked = !requested;
                _updating = false;
                StatusText.Text = "跳过设置保存失败。";
            }
            else
            {
                StatusText.Text = requested ? "全量扫描将跳过已标记 Tag。" : "全量扫描将重新扫描已有标记。";
            }
        }
        finally
        {
            SkipScannedCheckBox.IsEnabled = true;
        }
    }

    private async void ClearHistory_OnClick(object sender, RoutedEventArgs e)
    {
        if (_clearHistory is null) return;
        if (MessageBox.Show(
                Window.GetWindow(this),
                "确定清空全部 OCR Tag 跳过标记吗？这不会修改成就完成状态。",
                "清空 Tag 标记",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }
        if (!await _clearHistory()) StatusText.Text = "Tag 标记清空失败。";
    }

    private async void MarkSelectedTags_OnClick(object sender, RoutedEventArgs e) => await SetSelectedTagsMarkedAsync(true);
    private async void UnmarkSelectedTags_OnClick(object sender, RoutedEventArgs e) => await SetSelectedTagsMarkedAsync(false);

    private async Task SetSelectedTagsMarkedAsync(bool marked)
    {
        if (_setTagsMarked is null) return;
        var selected = TagGrid.SelectedItems
            .OfType<OcrTagViewRow>()
            .Select(row => new OcrTagSelection(row.PrimaryName, row.SecondaryName))
            .DistinctBy(item => item.Key)
            .ToArray();
        if (selected.Length == 0)
        {
            StatusText.Text = "请先使用 Ctrl / Shift 选择至少一个 Tag。";
            return;
        }
        var saved = await _setTagsMarked(selected, marked);
        if (saved)
        {
            StatusText.Text = marked
                ? $"DEBUG：已标记 {selected.Length} 个 Tag，后续全量扫描默认跳过。"
                : $"DEBUG：已取消 {selected.Length} 个 Tag 的跳过标记。";
        }
    }

    private async void ApplyResults_OnClick(object sender, RoutedEventArgs e)
    {
        if (_applyResults is null || _pendingPreview is null) return;
        var selected = _allResultRows
            .Where(row => row.Apply && row.Candidate.CanApply)
            .Select(row => row.Candidate)
            .ToArray();
        if (selected.Length == 0)
        {
            StatusText.Text = "请至少勾选一条状态明确且不歧义的扫描结果。";
            return;
        }

        ApplyResultsButton.IsEnabled = false;
        try
        {
            if (await _applyResults(selected))
            {
                foreach (var row in _allResultRows.Where(row => selected.Contains(row.Candidate))) row.Apply = false;
                StatusText.Text = $"已应用 {selected.Length} 条扫描结果。";
                RefreshResultFilter();
            }
        }
        finally
        {
            ApplyResultsButton.IsEnabled = true;
        }
    }

    private void ResultFilter_OnChanged(object sender, RoutedEventArgs e)
    {
        if (!_updating) RefreshResultFilter();
    }

    private void ResultRow_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OcrResultViewRow.Apply))
        {
            Dispatcher.BeginInvoke(new Action(RefreshResultFilter));
        }
    }

    private void TagFilter_OnChanged(object sender, RoutedEventArgs e)
    {
        if (!_updating) RefreshTagFilter();
    }

    private void RefreshResultFilter()
    {
        var search = ResultSearchBox?.Text.Trim() ?? string.Empty;
        var status = ResultStatusCombo?.SelectedItem?.ToString() ?? "全部状态";
        var primary = ResultPrimaryCombo?.SelectedItem?.ToString() ?? "全部一级分类";
        var secondary = ResultSecondaryCombo?.SelectedItem?.ToString() ?? "全部二级分类";
        var filtered = _allResultRows.Where(row =>
                (search.Length == 0 ||
                 row.Candidate.MatchedName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                 row.Candidate.OcrText.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                 row.Candidate.LegacyCode.Contains(search, StringComparison.OrdinalIgnoreCase)) &&
                (primary == "全部一级分类" || string.Equals(row.FirstCategory, primary, StringComparison.Ordinal)) &&
                (secondary == "全部二级分类" || string.Equals(row.SecondCategory, secondary, StringComparison.Ordinal)) &&
                status switch
                {
                    "已完成" => row.Candidate.ProposedStatus == ProgressStatus.Completed,
                    "未完成" => row.Candidate.ProposedStatus == ProgressStatus.Incomplete,
                    "未知" => row.Candidate.ProposedStatus is null,
                    "歧义" => row.Candidate.IsAmbiguous,
                    _ => true
                })
            .ToArray();
        ResultGrid.ItemsSource = filtered;
        var selected = _allResultRows.Count(row => row.Apply);
        ResultSearchPlaceholder.Visibility = search.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        ResultSummaryText.Text = _pendingPreview is null
            ? "还没有扫描结果。"
            : $"显示 {filtered.Length}/{_allResultRows.Count} 条 · 已勾选 {selected} 条 · 完成 {_pendingPreview.CompletedCount} · 未完成 {_pendingPreview.IncompleteCount} · 未知 {_pendingPreview.UnknownStatusCount}";
        ResultCountMetricText.Text = _allResultRows.Count.ToString();
        SelectedMetricText.Text = selected.ToString();
        CompletedMetricText.Text = (_pendingPreview?.CompletedCount ?? 0).ToString();
        IncompleteMetricText.Text = (_pendingPreview?.IncompleteCount ?? 0).ToString();
        UnmatchedMetricText.Text = (_pendingPreview?.Unmatched.Count ?? 0).ToString();
        ApplyResultsButton.IsEnabled = _pendingPreview is not null && _allResultRows.Count > 0;
    }

    private void RefreshTagFilter()
    {
        var search = TagSearchBox?.Text.Trim() ?? string.Empty;
        var marked = TagMarkedCombo?.SelectedItem?.ToString() ?? "全部 Tag";
        var primary = TagPrimaryCombo?.SelectedItem?.ToString() ?? "全部一级 Tag";
        var filtered = _allTagRows.Where(row =>
                (search.Length == 0 || row.PrimaryName.Contains(search, StringComparison.OrdinalIgnoreCase) || row.SecondaryName.Contains(search, StringComparison.OrdinalIgnoreCase)) &&
                (primary == "全部一级 Tag" || string.Equals(row.PrimaryName, primary, StringComparison.Ordinal)) &&
                marked switch
                {
                    "已标记跳过" => row.IsMarked,
                    "未标记" => !row.IsMarked,
                    _ => true
                })
            .ToArray();
        TagGrid.ItemsSource = filtered;
        var markedCount = _allTagRows.Count(row => row.IsMarked);
        TagSearchPlaceholder.Visibility = search.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        TagSummaryText.Text = $"显示 {filtered.Length}/{_allTagRows.Count} 个 Tag · 已标记 {markedCount} 个";
        MarkedTagMetricText.Text = markedCount.ToString();
    }

    private void SelectFilteredResults_OnClick(object sender, RoutedEventArgs e)
    {
        foreach (var row in ResultGrid.Items.OfType<OcrResultViewRow>()) row.Apply = true;
        RefreshResultFilter();
    }

    private void UnselectFilteredResults_OnClick(object sender, RoutedEventArgs e)
    {
        foreach (var row in ResultGrid.Items.OfType<OcrResultViewRow>()) row.Apply = false;
        RefreshResultFilter();
    }

    private void ResultGrid_OnPreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        var gridRow = FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject);
        if (gridRow?.Item is not OcrResultViewRow clickedRow)
        {
            return;
        }

        if (!gridRow.IsSelected)
        {
            ResultGrid.SelectedItems.Clear();
            gridRow.IsSelected = true;
        }

        var selectedRows = ResultGrid.SelectedItems
            .OfType<OcrResultViewRow>()
            .ToArray();
        if (selectedRows.Length == 0)
        {
            selectedRows = [clickedRow];
        }

        var menu = new ContextMenu();
        var select = new MenuItem { Header = "批量勾选" };
        select.Click += (_, _) => SetResultRowsApply(selectedRows, apply: true);
        menu.Items.Add(select);
        var unselect = new MenuItem { Header = "批量取消勾选" };
        unselect.Click += (_, _) => SetResultRowsApply(selectedRows, apply: false);
        menu.Items.Add(unselect);
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void SetResultRowsApply(IReadOnlyList<OcrResultViewRow> rows, bool apply)
    {
        foreach (var row in rows)
        {
            row.Apply = apply;
        }
        StatusText.Text = apply
            ? $"已勾选选中的 {rows.Count} 条扫描结果。"
            : $"已取消勾选选中的 {rows.Count} 条扫描结果。";
        RefreshResultFilter();
    }

    private static T? FindVisualParent<T>(DependencyObject? source) where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T target)
            {
                return target;
            }
            source = source is FrameworkContentElement contentElement
                ? contentElement.Parent
                : VisualTreeHelper.GetParent(source);
        }
        return null;
    }

    private enum OcrWorkbenchSection
    {
        Results,
        Tags,
        Help
    }
}

public sealed record OcrTagSelection(string PrimaryName, string SecondaryName)
{
    public string Key => BuildKey(PrimaryName, SecondaryName);

    public static string BuildKey(string primaryName, string secondaryName) =>
        $"{AchievementOcrMatcher.NormalizeName(primaryName)}\u001f{AchievementOcrMatcher.NormalizeName(secondaryName)}";
}

public sealed record OcrTagViewRow(
    string PrimaryName,
    string SecondaryName,
    bool IsMarked,
    DateTimeOffset? ScannedAtUtc,
    int Pages)
{
    public string MarkedText => IsMarked ? "跳过" : string.Empty;
    public string ScannedAtText => ScannedAtUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? string.Empty;
    public string PagesText => Pages > 0 ? $"{Pages} 页" : IsMarked ? "手动" : string.Empty;
}

public sealed class OcrResultViewRow : INotifyPropertyChanged
{
    private bool _apply;

    public OcrResultViewRow(OcrAchievementCandidate candidate, string firstCategory, string secondCategory)
    {
        Candidate = candidate;
        FirstCategory = firstCategory;
        SecondCategory = secondCategory;
        _apply = candidate.ShouldApplyByDefault;
    }

    public OcrAchievementCandidate Candidate { get; }
    public string FirstCategory { get; }
    public string SecondCategory { get; }
    public string StatusText => Candidate.IsAmbiguous ? "歧义" : Candidate.ProposedStatus?.ToChinese() ?? "未知";
    public double MatchConfidence => Candidate.MatchConfidence;
    public string ConfidenceText => $"{MatchConfidence:P0}";

    public bool Apply
    {
        get => _apply;
        set
        {
            var applicableValue = Candidate.CanApply && value;
            if (_apply == applicableValue) return;
            _apply = applicableValue;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Apply)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
