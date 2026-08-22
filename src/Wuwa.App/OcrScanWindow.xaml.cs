using System.Collections.ObjectModel;
using System.Windows;
using Wuwa.Core;

namespace Wuwa.App;

public partial class OcrScanWindow : Window
{
    private readonly OcrScanMode _mode;
    private bool _scanFinished;
    private bool _closeAfterCancellation;
    private bool _cancelRequested;

    public OcrScanWindow(OcrScanMode mode)
    {
        _mode = mode;
        InitializeComponent();
        DataContext = this;
        ModeText.Text = mode == OcrScanMode.FullScan
            ? "全量扫描：自动遍历全部一级/二级分类"
            : "当前分类：扫描当前二级分类的全部成就页面";
        PhaseText.Text = "准备 OCR 扫描…";
        StateText.Text = "准备中";
    }

    public ObservableCollection<string> Warnings { get; } = [];

    public event EventHandler? CancelRequested;

    public bool IsCancelRequested => _cancelRequested;

    public bool IsFinished => _scanFinished;

    public void Report(OcrScanProgress progress)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => Report(progress));
            return;
        }

        PhaseText.Text = PhaseTitle(progress.Phase);
        LocationText.Text = FormatLocation(progress);
        LatestMessageText.Text = progress.Message;
        MatchedText.Text = progress.MatchedCount.ToString();
        UnmatchedText.Text = progress.UnmatchedCount.ToString();
        PageText.Text = progress.Page > 0 ? progress.Page.ToString() : "—";
        WarningText.Text = progress.WarningCount.ToString();
        StateText.Text = progress.Phase switch
        {
            OcrScanPhase.Cancelling => "正在取消…",
            OcrScanPhase.Failed => "扫描失败",
            OcrScanPhase.Completed => "扫描完成",
            _ => "扫描中"
        };
        Title = $"OCR 扫描 · {progress.Message}";
    }

    public void AddWarning(string warning)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => AddWarning(warning));
            return;
        }

        if (string.IsNullOrWhiteSpace(warning) || Warnings.Contains(warning)) return;
        Warnings.Add(warning);
        while (Warnings.Count > 100) Warnings.RemoveAt(0);
        WarningText.Text = Warnings.Count.ToString();
    }

    public void PrepareForGame()
    {
        if (_scanFinished) return;
        StateText.Text = "扫描进行中 · 游戏窗口会自动置前，请勿操作工具或游戏";
        Title = "OCR 扫描进行中";
        Show();
    }

    public void MarkCompleted(string summary)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => MarkCompleted(summary));
            return;
        }

        _scanFinished = true;
        CancelButton.IsEnabled = false;
        CloseButton.IsEnabled = true;
        ScanProgressBar.IsIndeterminate = false;
        ScanProgressBar.Value = 100;
        StateText.Text = "扫描完成";
        PhaseText.Text = "扫描结果已准备好";
        LatestMessageText.Text = summary;
        Title = "OCR 扫描完成";
        RestoreForResult();
    }

    public void MarkCancelled(string message = "扫描已取消，当前进度未改变。")
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => MarkCancelled(message));
            return;
        }

        _scanFinished = true;
        CancelButton.IsEnabled = false;
        CloseButton.IsEnabled = true;
        ScanProgressBar.IsIndeterminate = false;
        ScanProgressBar.Value = 0;
        StateText.Text = "已取消";
        PhaseText.Text = "扫描已取消";
        LatestMessageText.Text = message;
        Title = "OCR 扫描已取消";
        RestoreForResult();
        CloseIfRequested();
    }

    public void MarkFailed(string message)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => MarkFailed(message));
            return;
        }

        _scanFinished = true;
        CancelButton.IsEnabled = false;
        CloseButton.IsEnabled = true;
        ScanProgressBar.IsIndeterminate = false;
        ScanProgressBar.Value = 0;
        StateText.Text = "扫描失败";
        PhaseText.Text = "扫描未完成";
        LatestMessageText.Text = message;
        Title = "OCR 扫描失败";
        RestoreForResult();
        CloseIfRequested();
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e)
    {
        RequestCancel(closeAfterCancellation: false);
    }

    private void Close_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_scanFinished)
        {
            e.Cancel = true;
            RequestCancel(closeAfterCancellation: true);
            return;
        }

        base.OnClosing(e);
    }

    private void RequestCancel(bool closeAfterCancellation)
    {
        if (_scanFinished) return;
        _closeAfterCancellation |= closeAfterCancellation;
        if (_cancelRequested) return;
        _cancelRequested = true;
        CancelButton.IsEnabled = false;
        StateText.Text = "正在取消…";
        PhaseText.Text = "正在请求取消 OCR";
        Title = "正在取消 OCR 扫描…";
        CancelRequested?.Invoke(this, EventArgs.Empty);
    }

    private void RestoreForResult()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Show();
        Activate();
    }

    private void CloseIfRequested()
    {
        if (_closeAfterCancellation)
        {
            Dispatcher.BeginInvoke(Close);
        }
    }

    private static string PhaseTitle(OcrScanPhase phase) => phase switch
    {
        OcrScanPhase.Preparing => "准备 OCR 组件",
        OcrScanPhase.FindingGameWindow => "检测游戏窗口",
        OcrScanPhase.ScanningCurrentCategory => "扫描当前分类",
        OcrScanPhase.SwitchingPrimaryCategory => "切换一级分类",
        OcrScanPhase.DiscoveringSecondaryCategories => "发现二级分类",
        OcrScanPhase.ScanningCategory => "扫描分类成就",
        OcrScanPhase.ScrollingCategory => "滚动分类列表",
        OcrScanPhase.Cancelling => "正在取消",
        OcrScanPhase.Completed => "扫描完成",
        OcrScanPhase.Failed => "扫描失败",
        _ => "OCR 扫描"
    };

    private static string FormatLocation(OcrScanProgress progress)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(progress.PrimaryCategory)) parts.Add(progress.PrimaryCategory);
        if (!string.IsNullOrWhiteSpace(progress.SecondaryCategory)) parts.Add(progress.SecondaryCategory);
        var location = parts.Count == 0 ? "当前游戏成就页面" : string.Join(" / ", parts);
        if (progress.Page > 0) location += $" · 第 {progress.Page} 页";
        if (progress.TotalCategoryCount is not null && progress.Mode == OcrScanMode.FullScan)
        {
            location += $" · 分类 {progress.VisitedCategoryCount}/{progress.TotalCategoryCount}";
        }
        return location;
    }
}
