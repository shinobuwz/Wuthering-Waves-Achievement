using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using Wuwa.Core;

namespace Wuwa.App;

public partial class OcrPreviewWindow : Window
{
    private readonly OcrScanPreview _preview;

    public OcrPreviewWindow(OcrScanPreview preview)
    {
        _preview = preview ?? throw new ArgumentNullException(nameof(preview));
        Rows = new ObservableCollection<OcrPreviewRow>(preview.Candidates.Select(candidate => new OcrPreviewRow(candidate)));
        DataContext = this;
        InitializeComponent();
        SummaryText.Text = $"匹配 {preview.Candidates.Count} 条 · 已完成 {preview.CompletedCount} · 未完成 {preview.IncompleteCount} · 状态未知 {preview.UnknownStatusCount}";
        UnmatchedText.Text = preview.Unmatched.Count == 0
            ? "没有未匹配文字。"
            : $"未匹配 {preview.Unmatched.Count} 条：{string.Join("；", preview.Unmatched.Take(8).Select(item => item.Text))}";
    }

    public ObservableCollection<OcrPreviewRow> Rows { get; }

    public OcrScanPreview? AcceptedPreview { get; private set; }

    private void Apply_OnClick(object sender, RoutedEventArgs e)
    {
        var selected = Rows
            .Where(row => row.Apply && row.Candidate.CanApply)
            .Select(row => row.Candidate)
            .ToArray();
        if (selected.Length == 0)
        {
            MessageBox.Show(this, "请至少选择一条具有明确状态的候选。", "OCR 扫描预览", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        AcceptedPreview = _preview with
        {
            Candidates = Array.AsReadOnly(selected),
            CompletedCount = selected.Count(candidate => candidate.ProposedStatus == ProgressStatus.Completed),
            IncompleteCount = selected.Count(candidate => candidate.ProposedStatus == ProgressStatus.Incomplete),
            UnknownStatusCount = selected.Count(candidate => candidate.ProposedStatus is null)
        };
        DialogResult = true;
        Close();
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

public sealed class OcrPreviewRow : INotifyPropertyChanged
{
    private bool _apply;

    public OcrPreviewRow(OcrAchievementCandidate candidate)
    {
        Candidate = candidate;
        _apply = candidate.CanApply;
    }

    public OcrAchievementCandidate Candidate { get; }

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

    public string StatusText => Candidate.ProposedStatus?.ToChinese() ?? "未知";

    public string ConfidenceText => $"{Candidate.MatchConfidence:P0}";

    public event PropertyChangedEventHandler? PropertyChanged;
}
