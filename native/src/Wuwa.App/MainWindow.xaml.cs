using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Diagnostics;
using System.IO;
using Wuwa.Core;
using Wuwa.Infrastructure;

namespace Wuwa.App;

public partial class MainWindow : Window
{
    private readonly AchievementWorkspace _workspace;
    private WorkspaceView? _view;
    private bool _initializingFilters;
    private CancellationTokenSource? _ocrCancellation;
    private bool _isLightTheme;

    public MainWindow(AchievementWorkspace workspace)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        InitializeComponent();
        Loaded += MainWindow_OnLoaded;
    }

    private async void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= MainWindow_OnLoaded;
        var opened = await _workspace.OpenAsync();
        if (!opened.IsSuccess)
        {
            ShowError(opened.Error?.Message ?? "无法打开工作区。");
            return;
        }

        if (opened.Snapshot.Metadata.EffectiveSettings.TryGetValue("theme", out var theme))
        {
            SetTheme(string.Equals(theme, "light", StringComparison.OrdinalIgnoreCase));
        }
        PopulateFilters(opened.Snapshot);
        RefreshView();
        var captureDirectory = Environment.GetEnvironmentVariable("WUWA_NATIVE_UI_CAPTURE_DIR");
        if (!string.IsNullOrWhiteSpace(captureDirectory))
        {
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            await Task.Delay(1500);
            await RunVisualVerificationAsync(Path.GetFullPath(captureDirectory));
        }
    }

    private void PopulateFilters(WorkspaceSnapshot snapshot)
    {
        _initializingFilters = true;
        try
        {
            SetItems(VersionCombo, ["全部版本", .. snapshot.Rows.Select(row => row.Version).Distinct().OrderBy(VersionSortKey)]);
            SetItems(FirstCategoryCombo, ["全部一级分类", .. snapshot.Categories.FirstCategories.OrderBy(pair => pair.Value).Select(pair => pair.Key)]);
            SetItems(SecondCategoryCombo, ["全部二级分类", .. snapshot.Rows.Select(row => row.SecondCategory).Distinct().OrderBy(item => item, StringComparer.Ordinal)]);
            SetItems(HiddenCombo, ["全部隐藏状态", "仅显示可见", "仅显示隐藏"]);
            SetItems(ObtainabilityCombo, ["全部可获取状态", "可获取", "暂不可获取"]);
            SetItems(CompletionCombo, ["全部完成状态", "未完成", "已完成"]);
            SetItems(StatusCombo, ["全部状态", "未完成", "已完成", "暂不可获取", "已占用"]);
            SetItems(GroupCombo, ["全部成就", "仅成就组"]);
            SetItems(SortCombo, ["默认排序", "未完成优先"]);
        }
        finally
        {
            _initializingFilters = false;
        }
    }

    private void RefreshView()
    {
        if (_initializingFilters)
        {
            return;
        }

        _view = _workspace.Query(BuildQuery());
        AchievementGrid.ItemsSource = _view.Rows;
        RevisionText.Text = _view.Revision.ToString();
        TotalText.Text = _view.Statistics.Total.ToString();
        CompletedText.Text = _view.Statistics.Completed.ToString();
        IncompleteText.Text = _view.Statistics.Incomplete.ToString();
        UnavailableText.Text = _view.Statistics.Unavailable.ToString();
        HiddenText.Text = _view.Statistics.Hidden.ToString();
        RateText.Text = $"{_view.Statistics.CompletionRatePercent:0.0}%";
        HintText.Text = $"显示 {_view.Rows.Count} 条 · 双击一行可切换完成状态 · 右键菜单可标记暂不可获取";
        ErrorText.Text = string.Empty;
    }

    private AchievementQuery BuildQuery() => new(
        SearchText: SearchBox.Text,
        Version: SelectedValue(VersionCombo, "全部版本"),
        FirstCategory: SelectedValue(FirstCategoryCombo, "全部一级分类"),
        SecondCategory: SelectedValue(SecondCategoryCombo, "全部二级分类"),
        Hidden: HiddenCombo.SelectedIndex switch
        {
            1 => HiddenFilter.VisibleOnly,
            2 => HiddenFilter.HiddenOnly,
            _ => HiddenFilter.All
        },
        Obtainability: ObtainabilityCombo.SelectedIndex switch
        {
            1 => ObtainabilityFilter.ObtainableOnly,
            2 => ObtainabilityFilter.UnavailableOnly,
            _ => ObtainabilityFilter.All
        },
        Completion: CompletionCombo.SelectedIndex switch
        {
            1 => CompletionFilter.IncompleteOnly,
            2 => CompletionFilter.CompletedOnly,
            _ => CompletionFilter.All
        },
        Status: StatusCombo.SelectedIndex switch
        {
            1 => ProgressStatus.Incomplete,
            2 => ProgressStatus.Completed,
            3 => ProgressStatus.Unavailable,
            4 => ProgressStatus.Occupied,
            _ => null
        },
        GroupsOnly: GroupCombo.SelectedIndex == 1,
        Sort: SortCombo.SelectedIndex == 1 ? AchievementSort.IncompleteFirst : AchievementSort.Default);

    private async void AchievementGrid_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (AchievementGrid.SelectedItem is not AchievementRow row)
        {
            return;
        }

        var requestedStatus = row.Status == ProgressStatus.Completed
            ? ProgressStatus.Incomplete
            : ProgressStatus.Completed;
        await ApplyStatusAsync(row.Id, requestedStatus);
    }

    private async void MarkUnavailable_OnClick(object sender, RoutedEventArgs e)
    {
        if (AchievementGrid.SelectedItem is AchievementRow row)
        {
            await ApplyStatusAsync(row.Id, ProgressStatus.Unavailable);
        }
    }

    private void AchievementGrid_OnPreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (AchievementGrid.SelectedItem is not AchievementRow row)
        {
            return;
        }

        var menu = new ContextMenu();
        var unavailable = new MenuItem { Header = "标记为暂不可获取" };
        unavailable.Click += async (_, _) => await ApplyStatusAsync(row.Id, ProgressStatus.Unavailable);
        menu.Items.Add(unavailable);
        var incomplete = new MenuItem { Header = "重置为未完成" };
        incomplete.Click += async (_, _) => await ApplyStatusAsync(row.Id, ProgressStatus.Incomplete);
        menu.Items.Add(incomplete);
        menu.IsOpen = true;
        e.Handled = true;
    }

    private async Task ApplyStatusAsync(AchievementId id, ProgressStatus status)
    {
        var result = await _workspace.ChangeStatusAsync(id, status);
        if (!result.IsSuccess)
        {
            ShowError(result.Error?.Message ?? "状态变更失败。");
            return;
        }

        RefreshView();
    }

    private void SearchBox_OnTextChanged(object sender, TextChangedEventArgs e) => RefreshView();

    private void Filter_OnChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded)
        {
            RefreshView();
        }
    }

    private void ClearFilters_OnClick(object sender, RoutedEventArgs e)
    {
        SearchBox.Clear();
        VersionCombo.SelectedIndex = 0;
        FirstCategoryCombo.SelectedIndex = 0;
        SecondCategoryCombo.SelectedIndex = 0;
        HiddenCombo.SelectedIndex = 0;
        ObtainabilityCombo.SelectedIndex = 0;
        CompletionCombo.SelectedIndex = 0;
        StatusCombo.SelectedIndex = 0;
        GroupCombo.SelectedIndex = 0;
        SortCombo.SelectedIndex = 0;
        RefreshView();
    }

    private async void OcrScan_OnClick(object sender, RoutedEventArgs e)
    {
        if (_ocrCancellation is not null)
        {
            _ocrCancellation.Cancel();
            OcrScanButton.IsEnabled = false;
            HintText.Text = "正在取消 OCR 扫描…";
            return;
        }

        var ocrRoot = Environment.GetEnvironmentVariable("WUWA_NATIVE_OCR_ROOT") ?? Path.Combine(AppContext.BaseDirectory, "ocr");
        var modelRoot = Environment.GetEnvironmentVariable("WUWA_NATIVE_OCR_MODEL_ROOT") ?? Path.Combine(ocrRoot, "models", "ppocrv5");
        var recognitionModel = Path.Combine(modelRoot, "rec", "rec.onnx");
        var detectionModel = Path.Combine(modelRoot, "det", "det.onnx");
        var classifierModel = Path.Combine(modelRoot, "cls", "cls.onnx");
        var dictionary = Path.Combine(modelRoot, "ppocrv5_dict.txt");
        if (!File.Exists(Path.Combine(ocrRoot, "Wuwa.Ocr.Native.dll")) || !File.Exists(recognitionModel) || !File.Exists(detectionModel) || !File.Exists(classifierModel) || !File.Exists(dictionary))
        {
            ShowError("原生 OCR 组件尚未部署。请先运行 native/scripts/build-native-ocr.ps1，或安装包含 ocr/ 资产的发布包。");
            return;
        }

        _ocrCancellation = new CancellationTokenSource();
        OcrScanButton.Content = "取消 OCR";
        HintText.Text = "正在检测游戏窗口并扫描当前页面…";
        ErrorText.Text = string.Empty;
        var previousState = WindowState;
        try
        {
            using var client = new NativeOcrClient(new NativeOcrOptions(recognitionModel, dictionary, MinimumScore: 0.5f));
            client.EnableDetection(detectionModel);
            client.EnableClassifier(classifierModel);
            using var reader = new NativeOcrTextReader(client);
            var service = new SinglePageOcrScanService(new WindowsGameWindowCapture(), reader);
            WindowState = WindowState.Minimized;
            await Task.Delay(350, _ocrCancellation.Token);
            var scan = await service.ScanAsync(
                ["Client-Win64-Shipping.exe", "Wuthering Waves.exe"],
                expectedWidth: 1920,
                expectedHeight: 1080,
                cancellationToken: _ocrCancellation.Token);
            WindowState = previousState;
            Activate();
            if (!scan.IsSuccess)
            {
                if (scan.Error?.Code == OcrScanErrorCode.Cancelled) HintText.Text = "OCR 扫描已取消。";
                else ShowError(scan.Error?.Message ?? "OCR 扫描失败。");
                return;
            }
            var preview = AchievementOcrMatcher.CreatePreview(scan.Lines, _workspace.Query().Rows);
            if (preview.Candidates.Count == 0)
            {
                ShowError($"OCR 扫描完成，但没有匹配到成就。检测到 {scan.Lines.Count} 条文字，未匹配 {preview.Unmatched.Count} 条。");
                return;
            }
            var previewWindow = new OcrPreviewWindow(preview) { Owner = this };
            if (previewWindow.ShowDialog() != true || previewWindow.AcceptedPreview is null)
            {
                HintText.Text = "OCR 结果未应用，当前进度保持不变。";
                return;
            }
            var applied = await _workspace.ApplyOcrPreviewAsync(previewWindow.AcceptedPreview, confirm: true, cancellationToken: _ocrCancellation.Token);
            if (!applied.IsSuccess)
            {
                ShowError(applied.Error?.Message ?? "OCR 结果应用失败。");
                return;
            }
            RefreshView();
            HintText.Text = $"OCR 已应用 {applied.Updated} 条 · 防止降级 {applied.PreventedDowngrades} 条 · 未变化 {applied.Unchanged} 条";
        }
        catch (OperationCanceledException)
        {
            HintText.Text = "OCR 扫描已取消。";
        }
        catch (Exception exception)
        {
            ShowError($"OCR 扫描失败：{exception.Message}");
        }
        finally
        {
            WindowState = previousState;
            _ocrCancellation.Dispose();
            _ocrCancellation = null;
            OcrScanButton.Content = "OCR 单页扫描";
            OcrScanButton.IsEnabled = true;
        }
    }

    private async void ImportLegacy_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "旧版配置 (config.json)|config.json|JSON files (*.json)|*.json", FileName = "config.json" };
        if (dialog.ShowDialog(this) != true) return;
        var source = new JsonLegacyProfileSource();
        var discovery = await _workspace.DiscoverLegacyProfilesAsync(source, dialog.FileName);
        if (discovery.Candidates.Count == 0)
        {
            ShowError(discovery.Error?.Message ?? "没有找到可导入的旧版进度。");
            return;
        }
        var candidate = SelectLegacyCandidate(discovery.Candidates);
        if (candidate is null) return;
        var confirm = MessageBox.Show(
            $"将用以下旧版进度替换当前 native 进度：\n\n用户名：{candidate.Username}\n昵称：{candidate.Nickname}\nUID：{candidate.Uid}\n来源：{candidate.ProgressPath}\n进度条目：{candidate.ProgressCount}\n\n当前 generation 会保留。是否继续？",
            "导入旧版进度", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;
        var imported = await _workspace.ImportLegacyProfileAsync(source, new LegacyImportOptions(candidate, ConfirmReplace: true));
        if (!imported.IsSuccess) { ShowError(imported.Error?.Message ?? "旧版进度导入失败。"); return; }
        PopulateFilters(imported.Snapshot);
        RefreshView();
    }

    private LegacyProfileCandidate? SelectLegacyCandidate(IReadOnlyList<LegacyProfileCandidate> candidates)
    {
        if (candidates.Count == 1) return candidates[0];
        var choices = candidates.Select(candidate => $"{candidate.Nickname} · {candidate.Username} · UID {candidate.Uid} · {candidate.ProgressCount} 条 · {candidate.ProgressPath}").ToArray();
        var window = new Window { Owner = this, Title = "选择旧版进度", Width = 760, Height = 360, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new DockPanel { Margin = new Thickness(16) };
        var list = new ListBox { ItemsSource = choices, SelectedIndex = 0 };
        DockPanel.SetDock(list, Dock.Top);
        panel.Children.Add(list);
        var confirm = new Button { Content = "选择", Width = 100, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        confirm.Click += (_, _) => window.DialogResult = true;
        panel.Children.Add(confirm);
        window.Content = panel;
        return window.ShowDialog() == true && list.SelectedIndex >= 0 ? candidates[list.SelectedIndex] : null;
    }

    private async void Import_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "JSON files (*.json)|*.json|Excel workbook (*.xlsx)|*.xlsx|TSV files (*.tsv;*.txt)|*.tsv;*.txt" };
        if (dialog.ShowDialog(this) != true) return;
        var progressJson = false;
        if (Path.GetExtension(dialog.FileName).Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var document = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(dialog.FileName));
                progressJson = document.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object;
            }
            catch (Exception exception) when (exception is IOException or System.Text.Json.JsonException)
            {
                ShowError($"无法读取导入文件：{exception.Message}");
                return;
            }
        }
        if (!progressJson)
        {
            var result = MessageBox.Show("导入将替换当前 native 成就数据，当前 generation 会保留。是否继续？", "确认导入", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;
        }
        IAchievementImportSource source;
        try { source = AchievementExchangeFactory.CreateImport(dialog.FileName); }
        catch (NotSupportedException exception) { ShowError(exception.Message); return; }
        var imported = await _workspace.ImportExchangeAsync(source, replace: !progressJson, confirmReplace: !progressJson);
        if (!imported.IsSuccess)
        {
            ShowError(imported.Error?.Message ?? "导入失败。");
            return;
        }
        PopulateFilters(imported.Snapshot);
        RefreshView();
    }

    private async void Export_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "JSON files (*.json)|*.json|Excel workbook (*.xlsx)|*.xlsx|TSV files (*.tsv)|*.tsv", FileName = "wuthering-waves-achievements.json" };
        if (dialog.ShowDialog(this) != true) return;
        IAchievementExportSink sink;
        try { sink = AchievementExchangeFactory.CreateExport(dialog.FileName); }
        catch (NotSupportedException exception) { ShowError(exception.Message); return; }
        var exported = await _workspace.ExportAsync(sink);
        if (!exported.IsSuccess) ShowError(exported.Error?.Message ?? "导出失败。");
    }

    private async void Theme_OnClick(object sender, RoutedEventArgs e)
    {
        var useLight = !_isLightTheme;
        SetTheme(useLight);
        var result = await _workspace.SetSettingAsync("theme", useLight ? "light" : "dark");
        if (!result.IsSuccess) ShowError(result.Error?.Message ?? "主题偏好保存失败。");
    }

    private async void Sync_OnClick(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show("将匿名请求 Wiki 并把有效内容写入新的 native generation。继续？", "同步 Wiki", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;
        var sync = await _workspace.SyncWikiAsync(new KuroWikiAchievementSource());
        if (!sync.IsSuccess)
        {
            ShowError(sync.Error?.Message ?? "Wiki 同步失败，当前数据未改变。");
            return;
        }
        PopulateFilters(sync.Snapshot);
        RefreshView();
    }

    private async void Update_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var checker = new GitHubUpdateChecker();
            var update = await checker.CheckAsync();
            if (update.Status == UpdateCheckStatus.Unavailable || update.Release is null)
            {
                ShowError(update.Error ?? "暂时无法取得 GitHub 最新版本。");
                return;
            }
            if (update.Status == UpdateCheckStatus.Current)
            {
                MessageBox.Show($"当前已是最新版本：{update.Release.TagName}", "检查更新", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (update.Status == UpdateCheckStatus.DevelopmentBuild)
            {
                MessageBox.Show($"当前版本不低于公开版本 {update.Release.TagName}，无需更新。", "检查更新", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show($"发现新版本：{update.Release.TagName}\n\n是否打开发布页面？", "检查更新", MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (result == MessageBoxResult.Yes && checker.IsTrustedReleaseUrl(update.Release.HtmlUrl))
                Process.Start(new ProcessStartInfo(update.Release.HtmlUrl) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            ShowError($"检查更新失败：{exception.Message}");
        }
    }

    private void SetTheme(bool light)
    {
        _isLightTheme = light;
        ThemeButton.Content = light ? "深色主题" : "浅色主题";
        var colors = light
            ? new Dictionary<string, string> { ["WindowBrush"] = "#F5F8F7", ["PanelBrush"] = "#FFFFFF", ["PanelAltBrush"] = "#E8F0EE", ["BorderBrush"] = "#C3D4D0", ["TextBrush"] = "#19302D", ["MutedTextBrush"] = "#5A7470", ["InputBrush"] = "#FFFFFF", ["RowBorderBrush"] = "#D7E2DF", ["SelectionBrush"] = "#B8E8DF", ["ErrorBrush"] = "#A52714" }
            : new Dictionary<string, string> { ["WindowBrush"] = "#182124", ["PanelBrush"] = "#222E32", ["PanelAltBrush"] = "#29383D", ["BorderBrush"] = "#3A5055", ["TextBrush"] = "#E8F2F0", ["MutedTextBrush"] = "#9BB3AF", ["InputBrush"] = "#172225", ["RowBorderBrush"] = "#304247", ["SelectionBrush"] = "#285A5A", ["ErrorBrush"] = "#FF9A8D" };
        foreach (var pair in colors)
        {
            Application.Current.Resources[pair.Key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(pair.Value));
        }
    }

    private async Task RunVisualVerificationAsync(string outputDirectory)
    {
        try
        {
            Directory.CreateDirectory(outputDirectory);
            foreach (var light in new[] { false, true })
            {
                SetTheme(light);
                foreach (var size in new[] { (Width: 1080d, Height: 700d), (Width: 1440d, Height: 900d) })
                {
                    Width = size.Width;
                    Height = size.Height;
                    WindowState = WindowState.Normal;
                    UpdateLayout();
                    await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                    UpdateLayout();
                    var pixelWidth = Math.Max(1, (int)Math.Ceiling(ActualWidth));
                    var pixelHeight = Math.Max(1, (int)Math.Ceiling(ActualHeight));
                    var bitmap = new RenderTargetBitmap(pixelWidth, pixelHeight, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(this);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    var theme = light ? "light" : "dark";
                    await using var stream = File.Create(Path.Combine(outputDirectory, $"{(int)size.Width}x{(int)size.Height}-{theme}.png"));
                    encoder.Save(stream);
                }
            }
            File.WriteAllText(Path.Combine(outputDirectory, "completed.txt"), DateTimeOffset.UtcNow.ToString("O"));
            Application.Current.Shutdown();
        }
        catch (Exception exception)
        {
            Directory.CreateDirectory(outputDirectory);
            File.WriteAllText(Path.Combine(outputDirectory, "error.txt"), exception.ToString());
            Application.Current.Shutdown(2);
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        HintText.Text = "工作区加载失败；请检查 shipped 资源文件。";
    }

    private static void SetItems(ComboBox comboBox, IEnumerable<string> values)
    {
        comboBox.ItemsSource = values.ToArray();
        comboBox.SelectedIndex = 0;
    }

    private static string? SelectedValue(ComboBox comboBox, string allLabel) =>
        comboBox.SelectedItem is string value && !string.Equals(value, allLabel, StringComparison.Ordinal)
            ? value
            : null;

    private static double VersionSortKey(string value) =>
        double.TryParse(value, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var number)
            ? number
            : double.MaxValue;
}
