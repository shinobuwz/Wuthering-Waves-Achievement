using System.Windows;
using Wuwa.Core;

namespace Wuwa.App;

public partial class OcrWorkbenchWindow : Window
{
    private readonly Action _scanCurrent;
    private readonly Action _scanFull;
    private readonly Action _searchSync;
    private readonly Action _openPagingSettings;
    private readonly Func<bool, Task<bool>> _setSkipPreviouslyScanned;
    private readonly Func<Task<bool>> _clearHistory;
    private bool _updating;

    public OcrWorkbenchWindow(
        OcrScanHistory history,
        Action scanCurrent,
        Action scanFull,
        Action searchSync,
        Action openPagingSettings,
        Func<bool, Task<bool>> setSkipPreviouslyScanned,
        Func<Task<bool>> clearHistory)
    {
        InitializeComponent();
        _scanCurrent = scanCurrent ?? throw new ArgumentNullException(nameof(scanCurrent));
        _scanFull = scanFull ?? throw new ArgumentNullException(nameof(scanFull));
        _searchSync = searchSync ?? throw new ArgumentNullException(nameof(searchSync));
        _openPagingSettings = openPagingSettings ?? throw new ArgumentNullException(nameof(openPagingSettings));
        _setSkipPreviouslyScanned = setSkipPreviouslyScanned ?? throw new ArgumentNullException(nameof(setSkipPreviouslyScanned));
        _clearHistory = clearHistory ?? throw new ArgumentNullException(nameof(clearHistory));
        ApplyHistory(history);
    }

    public void ApplyHistory(OcrScanHistory history)
    {
        ArgumentNullException.ThrowIfNull(history);
        _updating = true;
        try
        {
            SkipScannedCheckBox.IsChecked = history.SkipPreviouslyScanned;
            var items = history.EffectiveCategories
                .OrderByDescending(item => item.ScannedAtUtc)
                .ThenBy(item => item.PrimaryName, StringComparer.Ordinal)
                .ThenBy(item => item.SecondaryName, StringComparer.Ordinal)
                .Select(item => new HistoryRow(
                    $"{item.PrimaryName} / {item.SecondaryName}",
                    item.ScannedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                    item.Pages > 0 ? $"{item.Pages} 页" : "已扫描"))
                .ToArray();
            HistoryList.ItemsSource = items;
            HistorySummaryText.Text = $"已记录一级分类 {history.PrimaryCategoryCount} 个，一级 / 二级组合 {items.Length} 个。";
            StatusText.Text = items.Length == 0 ? "还没有扫描记录。" : "默认跳过只影响全量扫描，可随时取消勾选后重新扫描。";
        }
        finally
        {
            _updating = false;
        }
    }

    private void ScanCurrent_OnClick(object sender, RoutedEventArgs e) => Launch(_scanCurrent);

    private void ScanFull_OnClick(object sender, RoutedEventArgs e) => Launch(_scanFull);

    private void SearchSync_OnClick(object sender, RoutedEventArgs e) => Launch(_searchSync);

    private void PagingSettings_OnClick(object sender, RoutedEventArgs e) => Launch(_openPagingSettings);

    private async void SkipScanned_OnClick(object sender, RoutedEventArgs e)
    {
        if (_updating) return;
        var value = SkipScannedCheckBox.IsChecked == true;
        SkipScannedCheckBox.IsEnabled = false;
        try
        {
            if (!await _setSkipPreviouslyScanned(value))
            {
                _updating = true;
                SkipScannedCheckBox.IsChecked = !value;
                _updating = false;
                StatusText.Text = "跳过设置保存失败。";
                return;
            }
            StatusText.Text = value ? "全量扫描将跳过已有记录。" : "本次及后续全量扫描不会跳过已有记录。";
        }
        finally
        {
            SkipScannedCheckBox.IsEnabled = true;
        }
    }

    private async void ClearHistory_OnClick(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(
                this,
                "确定清空 OCR 已扫描分类记录吗？这不会修改任何成就完成状态。",
                "清空扫描记录",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        if (!await _clearHistory())
        {
            StatusText.Text = "扫描记录清空失败。";
        }
    }

    private void Close_OnClick(object sender, RoutedEventArgs e) => Close();

    private void Launch(Action action)
    {
        Hide();
        action();
    }

    private sealed record HistoryRow(string CategoryText, string ScanText, string PagesText);
}
