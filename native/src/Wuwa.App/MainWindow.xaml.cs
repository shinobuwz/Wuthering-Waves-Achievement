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
#if DEBUG
        OcrNavigationDebugButton.Visibility = Visibility.Visible;
#endif
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
        HintText.Text = $"显示 {_view.Rows.Count} 条 · 双击切换完成状态 · 右键设置状态";
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
            OcrFullScanButton.IsEnabled = false;
            HintText.Text = "正在取消 OCR 扫描…";
            return;
        }

        var ocrAssets = FindOcrAssets();
        if (ocrAssets is null)
        {
            NativeOcrDiagnostics.Write("OCR start failed: assets not found");
            ShowOcrError("原生 OCR 组件尚未部署。开发环境请先运行 native/scripts/build-native-ocr.ps1；发布环境请安装包含 ocr/ 资产的发布包。", "OCR 组件缺失");
            return;
        }

        var modelRoot = ocrAssets.ModelRoot;
        var templateDirectory = FindOcrTemplateDirectory();
        if (templateDirectory is null)
        {
            NativeOcrDiagnostics.Write("OCR start failed: icon templates not found");
            ShowOcrError("找不到 OCR 成就图标模板，请检查 resources/ocr_templates 目录。", "OCR 模板缺失");
            return;
        }
        NativeOcrDiagnostics.Write($"OCR start root={ocrAssets.Root} modelRoot={modelRoot} templates={templateDirectory}");
        var recognitionModel = Path.Combine(modelRoot, "rec", "rec.onnx");
        var dictionary = Path.Combine(modelRoot, "ppocrv5_dict.txt");
        var gameProcessNames = new[] { "Client-Win64-Shipping.exe", "Wuthering Waves.exe" };
        if (!IsAnyProcessRunning(gameProcessNames))
        {
            NativeOcrDiagnostics.Write($"OCR start failed: no process among [{string.Join(",", gameProcessNames)}]");
            ShowOcrError("未检测到《鸣潮》游戏进程，请先启动游戏后再进行 OCR 扫描。", "未检测到游戏");
            return;
        }

        _ocrCancellation = new CancellationTokenSource();
        OcrScanButton.Content = "取消 OCR";
        OcrFullScanButton.IsEnabled = false;
        HintText.Text = "正在检测游戏窗口并扫描当前页面…";
        ErrorText.Text = string.Empty;
        var previousState = WindowState;
        try
        {
            using var client = new NativeOcrClient(new NativeOcrOptions(recognitionModel, dictionary, MinimumScore: 0.0f));
            var reader = new NativeOcrTemplateTextReader(client, templateDirectory);
            var capture = new WindowsGameWindowCapture();
            var initialWindow = await capture.TryFindGameWindowAsync(gameProcessNames, minimumWidth: 800, minimumHeight: 600, cancellationToken: _ocrCancellation.Token);
            if (initialWindow is null)
            {
                NativeOcrDiagnostics.Write("OCR start aborted: no visible game window passed preflight");
                ShowOcrError("找到了游戏进程，但没有找到可见且分辨率至少为 800×600 的游戏窗口。请退出最小化状态，并确认游戏窗口可见。", "找不到游戏窗口");
                return;
            }
            var service = new SinglePageOcrScanService(capture, reader);
            var rows = _workspace.Query().Rows;
            var mergedCandidates = new Dictionary<AchievementId, OcrAchievementCandidate>();
            var mergedUnmatched = new Dictionary<string, OcrUnmatchedText>(StringComparer.Ordinal);
            var seenIds = new HashSet<AchievementId>();
            var scannedPages = 0;
            var detectedLineCount = 0;
            OcrScanPreview? preview = null;

            WindowState = WindowState.Minimized;
            await Task.Delay(350, _ocrCancellation.Token);
            for (var page = 1; page <= 80; page++)
            {
                NativeOcrDiagnostics.Write($"OCR page={page} mergedCandidates={mergedCandidates.Count} seenIds={seenIds.Count}");
                HintText.Text = $"正在扫描当前分类第 {page} 页，已识别 {mergedCandidates.Count} 条…请勿操作游戏窗口。";
                var scan = await service.ScanAsync(
                    gameProcessNames,
                    expectedWidth: 1920,
                    expectedHeight: 1080,
                    cancellationToken: _ocrCancellation.Token);
                if (!scan.IsSuccess)
                {
                    if (scan.Error?.Code == OcrScanErrorCode.Cancelled) HintText.Text = "OCR 扫描已取消。";
                    else ShowOcrError(scan.Error?.Message ?? "OCR 扫描失败。", "OCR 扫描失败");
                    return;
                }

                scannedPages = page;
                detectedLineCount += scan.Lines.Count;
                preview = AchievementOcrMatcher.CreatePreview(scan.Lines, rows);
                var pageIds = preview.Candidates.Select(candidate => candidate.AchievementId).ToHashSet();
                NativeOcrDiagnostics.Write($"OCR page={page} lines={scan.Lines.Count} candidates={preview.Candidates.Count} unmatched={preview.Unmatched.Count} ids=[{string.Join(",", preview.Candidates.Select(candidate => candidate.LegacyCode))}]");
                if (page > 1 && (pageIds.Count == 0 || pageIds.IsSubsetOf(seenIds)))
                {
                    NativeOcrDiagnostics.Write($"OCR stop page={page} reason={(pageIds.Count == 0 ? "empty-page" : "repeated-page")}");
                    break;
                }

                foreach (var candidate in preview.Candidates)
                {
                    if (!mergedCandidates.TryGetValue(candidate.AchievementId, out var existing) || PreferOcrCandidate(candidate, existing))
                    {
                        mergedCandidates[candidate.AchievementId] = candidate;
                    }
                }
                foreach (var unmatched in preview.Unmatched)
                {
                    var key = $"{unmatched.Text}\u001f{unmatched.Reason}";
                    mergedUnmatched.TryAdd(key, unmatched);
                }
                seenIds.UnionWith(pageIds);

                if (scan.Window is null || page == 80) break;
                var scrollAccepted = await capture.ScrollAsync(scan.Window, scrollLength: -160, scrollTimes: 15, cancellationToken: _ocrCancellation.Token);
                NativeOcrDiagnostics.Write($"OCR page={page} scrollAccepted={scrollAccepted}");
                if (!scrollAccepted)
                {
                    ShowOcrError("Windows 拒绝了模拟鼠标输入。请确保游戏和工具使用相同权限运行，且当前桌面未被锁定。", "无法控制游戏");
                    return;
                }
                await Task.Delay(800, _ocrCancellation.Token);
            }

            WindowState = previousState;
            Activate();
            var mergedPreview = MergeOcrPreviews(mergedCandidates, mergedUnmatched, rows);
            NativeOcrDiagnostics.Write($"OCR finished pages={scannedPages} lines={detectedLineCount} candidates={mergedPreview.Candidates.Count} unmatched={mergedPreview.Unmatched.Count}");
            if (mergedPreview.Candidates.Count == 0)
            {
                ShowOcrError($"OCR 扫描完成，但没有匹配到成就。扫描 {scannedPages} 页，检测到 {detectedLineCount} 条文字，未匹配 {mergedPreview.Unmatched.Count} 条。请确认游戏当前打开的是成就列表。", "OCR 扫描结果");
                return;
            }
            var previewWindow = new OcrPreviewWindow(mergedPreview) { Owner = this };
            if (previewWindow.ShowDialog() != true || previewWindow.AcceptedPreview is null)
            {
                HintText.Text = "OCR 结果未应用，当前进度保持不变。";
                return;
            }
            var applied = await _workspace.ApplyOcrPreviewAsync(previewWindow.AcceptedPreview, confirm: true, cancellationToken: _ocrCancellation.Token);
            if (!applied.IsSuccess)
            {
                ShowOcrError(applied.Error?.Message ?? "OCR 结果应用失败。", "OCR 结果应用失败");
                return;
            }
            RefreshView();
            HintText.Text = $"OCR 已应用 {applied.Updated} 条 · 防止降级 {applied.PreventedDowngrades} 条 · 未变化 {applied.Unchanged} 条";
        }
        catch (OperationCanceledException)
        {
            HintText.Text = "OCR 扫描已取消。";
        }
        catch (GameWindowNotFoundException exception)
        {
            NativeOcrDiagnostics.Write($"OCR exception GameWindowNotFound: {exception}");
            ShowOcrError($"未找到可捕获的游戏窗口：{exception.Message}", "未找到游戏窗口");
        }
        catch (GameWindowCaptureException exception)
        {
            NativeOcrDiagnostics.Write($"OCR exception GameWindowCapture: {exception}");
            ShowOcrError($"无法捕获游戏画面：{exception.Message}", "游戏画面捕获失败");
        }
        catch (Exception exception)
        {
            NativeOcrDiagnostics.Write($"OCR exception: {exception}");
            ShowOcrError($"OCR 扫描失败：{exception.Message}", "OCR 扫描失败");
        }
        finally
        {
            WindowState = previousState;
            _ocrCancellation.Dispose();
            _ocrCancellation = null;
            OcrScanButton.Content = "OCR 自动扫描当前分类";
            OcrFullScanButton.IsEnabled = true;
            OcrScanButton.IsEnabled = true;
        }
    }

    private async void OcrNavigationDebug_OnClick(object sender, RoutedEventArgs e)
    {
        if (_ocrCancellation is not null)
        {
            _ocrCancellation.Cancel();
            OcrNavigationDebugButton.IsEnabled = false;
            HintText.Text = "正在取消分类切换测试…";
            return;
        }

        var ocrAssets = FindOcrAssets();
        if (ocrAssets is null)
        {
            ShowOcrError("原生 OCR 组件尚未部署。开发环境请先运行 native/scripts/build-native-ocr.ps1。", "OCR 组件缺失");
            return;
        }

        var gameProcessNames = new[] { "Client-Win64-Shipping.exe", "Wuthering Waves.exe" };
        if (!IsAnyProcessRunning(gameProcessNames))
        {
            ShowOcrError("未检测到《鸣潮》游戏进程，请先启动游戏后再进行分类切换测试。", "未检测到游戏");
            return;
        }

        _ocrCancellation = new CancellationTokenSource();
        OcrNavigationDebugButton.Content = "取消分类测试";
        OcrScanButton.IsEnabled = false;
        OcrFullScanButton.IsEnabled = false;
        HintText.Text = "正在准备分类切换测试…";
        ErrorText.Text = string.Empty;
        var previousState = WindowState;
        try
        {
            var modelRoot = ocrAssets.ModelRoot;
            var recognitionModel = Path.Combine(modelRoot, "rec", "rec.onnx");
            var detectionModel = Path.Combine(modelRoot, "det", "det.onnx");
            var classifierModel = Path.Combine(modelRoot, "cls", "cls.onnx");
            var dictionary = Path.Combine(modelRoot, "ppocrv5_dict.txt");
            var navigationClient = new NativeOcrClient(new NativeOcrOptions(recognitionModel, dictionary, MinimumScore: 0.0f));
            navigationClient.EnableDetection(detectionModel);
            navigationClient.EnableClassifier(classifierModel);
            using var navigationReader = new NativeOcrTextReader(navigationClient);
            var capture = new WindowsGameWindowCapture();
            var navigationService = new SinglePageOcrScanService(capture, navigationReader);
            var initialWindow = await capture.TryFindGameWindowAsync(gameProcessNames, minimumWidth: 800, minimumHeight: 600, cancellationToken: _ocrCancellation.Token);
            if (initialWindow is null)
            {
                ShowOcrError("找到了游戏进程，但没有找到可见且分辨率至少为 800×600 的游戏窗口。", "找不到游戏窗口");
                return;
            }

            var rows = _workspace.Query(new AchievementQuery()).Rows;
            var primaryNames = new[] { "索拉漫行", "长路留迹", "铿锵刃鸣", "诸音声轨" };
            var primaryYPercentages = new[] { 0.1778, 0.2981, 0.4343, 0.5537 };
            var secondaryMap = BuildSecondaryCategoryMap(rows, primaryNames);
            var currentWindow = initialWindow;
            var primarySucceeded = 0;
            var primarySkipped = 0;
            var secondaryClicked = 0;
            var secondaryFailed = 0;
            var failures = new List<string>();

            WindowState = WindowState.Minimized;
            await Task.Delay(350, _ocrCancellation.Token);

            for (var primaryIndex = 0; primaryIndex < primaryNames.Length; primaryIndex++)
            {
                _ocrCancellation.Token.ThrowIfCancellationRequested();
                var primaryName = primaryNames[primaryIndex];
                var primaryX = (int)(currentWindow.ClientWidth * 0.0417);
                var primaryY = (int)(currentWindow.ClientHeight * primaryYPercentages[primaryIndex]);
                var primarySwitched = false;
                for (var attempt = 1; attempt <= 3 && !primarySwitched; attempt++)
                {
                    HintText.Text = $"DEBUG：切换一级分类 {primaryName}（{attempt}/3）";
                    var clicked = await capture.ClickAsync(currentWindow, primaryX, primaryY, _ocrCancellation.Token);
                    NativeOcrDiagnostics.Write($"OCR navigation debug primary={primaryName} attempt={attempt} clicked={clicked} client={primaryX},{primaryY}");
                    if (!clicked) break;
                    await Task.Delay(800, _ocrCancellation.Token);
                    var verification = await navigationService.ScanAsync(gameProcessNames, 1920, 1080, _ocrCancellation.Token);
                    if (!verification.IsSuccess)
                    {
                        failures.Add($"一级 {primaryName}：OCR 验证失败（{verification.Error?.Message ?? "未知错误"}）");
                        break;
                    }
                    currentWindow = verification.Window ?? currentWindow;
                    primarySwitched = IsPrimaryTabRecognized(verification.Lines, primaryName, currentWindow.ClientWidth, currentWindow.ClientHeight);
                    if (!primarySwitched && attempt == 3)
                    {
                        failures.Add($"一级 {primaryName}：切换后 OCR 未识别到目标名称");
                    }
                }

                if (!primarySwitched)
                {
                    primarySkipped++;
                    continue;
                }

                primarySucceeded++;
                var secondaryNames = secondaryMap.GetValueOrDefault(primaryName, []).ToArray();
                var visited = new HashSet<string>(StringComparer.Ordinal);
                var noNewTabRounds = 0;
                while (noNewTabRounds < 3 && visited.Count < secondaryNames.Length)
                {
                    _ocrCancellation.Token.ThrowIfCancellationRequested();
                    var tabScan = await navigationService.ScanAsync(gameProcessNames, 1920, 1080, _ocrCancellation.Token);
                    if (!tabScan.IsSuccess)
                    {
                        failures.Add($"一级 {primaryName}：二级分类 OCR 失败（{tabScan.Error?.Message ?? "未知错误"}）");
                        break;
                    }
                    currentWindow = tabScan.Window ?? currentWindow;
                    var visibleTabs = FindVisibleSecondaryTabs(tabScan.Lines, secondaryNames, currentWindow.ClientWidth, currentWindow.ClientHeight);
                    var foundNew = false;
                    foreach (var tab in visibleTabs)
                    {
                        _ocrCancellation.Token.ThrowIfCancellationRequested();
                        if (!visited.Add(tab.Name)) continue;
                        foundNew = true;
                        var secondaryX = (int)(currentWindow.ClientWidth * ((0.1005 + 0.3479) / 2));
                        HintText.Text = $"DEBUG：点击二级分类 {primaryName} / {tab.Name}（{visited.Count}/{secondaryNames.Length}）";
                        var clicked = await capture.ClickAsync(currentWindow, secondaryX, tab.ClientY, _ocrCancellation.Token);
                        NativeOcrDiagnostics.Write($"OCR navigation debug secondary={primaryName}/{tab.Name} clicked={clicked} client={secondaryX},{tab.ClientY}");
                        if (!clicked)
                        {
                            secondaryFailed++;
                            failures.Add($"二级 {primaryName} / {tab.Name}：点击失败");
                            continue;
                        }
                        secondaryClicked++;
                        await Task.Delay(500, _ocrCancellation.Token);
                    }

                    if (visited.Count >= secondaryNames.Length) break;
                    noNewTabRounds = foundNew ? 0 : noNewTabRounds + 1;
                    var scrollX = (int)(currentWindow.ClientWidth * ((0.1005 + 0.3479) / 2));
                    var scrollY = (int)(currentWindow.ClientHeight * ((0.1796 + 1.0) / 2));
                    HintText.Text = $"DEBUG：滚动二级分类列表 {primaryName}（无新分类 {noNewTabRounds}/3）";
                    if (!await capture.ScrollAtAsync(currentWindow, scrollX, scrollY, -160, 16, _ocrCancellation.Token))
                    {
                        failures.Add($"一级 {primaryName}：二级分类列表滚动失败");
                        break;
                    }
                    await Task.Delay(800, _ocrCancellation.Token);
                }

                foreach (var missing in secondaryNames.Where(name => !visited.Contains(name)))
                {
                    failures.Add($"二级 {primaryName} / {missing}：未找到或未点击");
                }
            }

            WindowState = previousState;
            Activate();
            var summary = $"一级分类：成功 {primarySucceeded}，跳过 {primarySkipped}\n" +
                          $"二级分类：点击成功 {secondaryClicked}，点击失败 {secondaryFailed}\n" +
                          (failures.Count == 0 ? "\n未发现切换错误。" : $"\n问题 {failures.Count} 个：\n{string.Join("\n", failures.Take(12))}");
            NativeOcrDiagnostics.Write($"OCR navigation debug finished primarySucceeded={primarySucceeded} primarySkipped={primarySkipped} secondaryClicked={secondaryClicked} secondaryFailed={secondaryFailed} failures={failures.Count}");
            MessageBox.Show(this, summary, "DEBUG 分类切换测试完成", MessageBoxButton.OK, failures.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
            HintText.Text = $"DEBUG 分类切换测试完成 · 一级成功 {primarySucceeded}/{primaryNames.Length} · 二级点击成功 {secondaryClicked}";
        }
        catch (OperationCanceledException)
        {
            HintText.Text = "DEBUG 分类切换测试已取消。";
        }
        catch (GameWindowNotFoundException exception)
        {
            NativeOcrDiagnostics.Write($"OCR navigation debug exception GameWindowNotFound: {exception}");
            ShowOcrError($"未找到可捕获的游戏窗口：{exception.Message}", "未找到游戏窗口");
        }
        catch (GameWindowCaptureException exception)
        {
            NativeOcrDiagnostics.Write($"OCR navigation debug exception GameWindowCapture: {exception}");
            ShowOcrError($"无法捕获游戏画面：{exception.Message}", "游戏画面捕获失败");
        }
        catch (Exception exception)
        {
            NativeOcrDiagnostics.Write($"OCR navigation debug exception: {exception}");
            ShowOcrError($"分类切换测试失败：{exception.Message}", "DEBUG 分类切换失败");
        }
        finally
        {
            WindowState = previousState;
            _ocrCancellation?.Dispose();
            _ocrCancellation = null;
            OcrNavigationDebugButton.Content = "DEBUG：测试分类切换";
            OcrNavigationDebugButton.IsEnabled = true;
            OcrFullScanButton.IsEnabled = true;
            OcrScanButton.IsEnabled = true;
        }
    }

    private async void OcrFullScan_OnClick(object sender, RoutedEventArgs e)
    {
        if (_ocrCancellation is not null)
        {
            _ocrCancellation.Cancel();
            OcrFullScanButton.IsEnabled = false;
            HintText.Text = "正在取消 OCR 全量扫描…";
            return;
        }

        var ocrAssets = FindOcrAssets();
        var templateDirectory = FindOcrTemplateDirectory();
        if (ocrAssets is null || templateDirectory is null)
        {
            ShowOcrError("原生 OCR 组件或成就图标模板尚未部署。请先构建 native OCR 资源。", "OCR 组件缺失");
            return;
        }

        var gameProcessNames = new[] { "Client-Win64-Shipping.exe", "Wuthering Waves.exe" };
        if (!IsAnyProcessRunning(gameProcessNames))
        {
            ShowOcrError("未检测到《鸣潮》游戏进程，请先启动游戏后再进行 OCR 扫描。", "未检测到游戏");
            return;
        }

        _ocrCancellation = new CancellationTokenSource();
        OcrFullScanButton.Content = "取消 OCR";
        OcrScanButton.IsEnabled = false;
        HintText.Text = "正在准备 OCR 全量扫描…";
        ErrorText.Text = string.Empty;
        var previousState = WindowState;
        try
        {
            var modelRoot = ocrAssets.ModelRoot;
            var recognitionModel = Path.Combine(modelRoot, "rec", "rec.onnx");
            var detectionModel = Path.Combine(modelRoot, "det", "det.onnx");
            var classifierModel = Path.Combine(modelRoot, "cls", "cls.onnx");
            var dictionary = Path.Combine(modelRoot, "ppocrv5_dict.txt");
            using var rowClient = new NativeOcrClient(new NativeOcrOptions(recognitionModel, dictionary, MinimumScore: 0.0f));
            var rowReader = new NativeOcrTemplateTextReader(rowClient, templateDirectory);
            var navigationClient = new NativeOcrClient(new NativeOcrOptions(recognitionModel, dictionary, MinimumScore: 0.0f));
            navigationClient.EnableDetection(detectionModel);
            navigationClient.EnableClassifier(classifierModel);
            using var navigationReader = new NativeOcrTextReader(navigationClient);
            var capture = new WindowsGameWindowCapture();
            var navigationService = new SinglePageOcrScanService(capture, navigationReader);
            var achievementService = new SinglePageOcrScanService(capture, rowReader);
            var initialWindow = await capture.TryFindGameWindowAsync(gameProcessNames, minimumWidth: 800, minimumHeight: 600, cancellationToken: _ocrCancellation.Token);
            if (initialWindow is null)
            {
                ShowOcrError("找到了游戏进程，但没有找到可见且分辨率至少为 800×600 的游戏窗口。", "找不到游戏窗口");
                return;
            }

            var rows = _workspace.Query(new AchievementQuery()).Rows;
            var primaryNames = new[] { "索拉漫行", "长路留迹", "铿锵刃鸣", "诸音声轨" };
            var secondaryMap = BuildSecondaryCategoryMap(rows, primaryNames);
            var mergedCandidates = new Dictionary<AchievementId, OcrAchievementCandidate>();
            var mergedUnmatched = new Dictionary<string, OcrUnmatchedText>(StringComparer.Ordinal);
            var currentWindow = initialWindow;

            WindowState = WindowState.Minimized;
            await Task.Delay(350, _ocrCancellation.Token);

            for (var primaryIndex = 0; primaryIndex < primaryNames.Length; primaryIndex++)
            {
                _ocrCancellation.Token.ThrowIfCancellationRequested();
                var primaryName = primaryNames[primaryIndex];
                var primaryX = (int)(currentWindow.ClientWidth * 0.0417);
                var primaryY = (int)(currentWindow.ClientHeight * new[] { 0.1778, 0.2981, 0.4343, 0.5537 }[primaryIndex]);
                var switched = false;
                for (var attempt = 0; attempt < 3 && !switched; attempt++)
                {
                    HintText.Text = $"正在切换一级分类：{primaryName}（{attempt + 1}/3）";
                    if (!await capture.ClickAsync(currentWindow, primaryX, primaryY, _ocrCancellation.Token)) break;
                    await Task.Delay(800, _ocrCancellation.Token);
                    var verification = await navigationService.ScanAsync(gameProcessNames, 1920, 1080, _ocrCancellation.Token);
                    if (!verification.IsSuccess)
                    {
                        AddScanWarning(mergedUnmatched, primaryName, verification.Error?.Message ?? "一级分类验证失败");
                        break;
                    }
                    currentWindow = verification.Window ?? currentWindow;
                    switched = IsPrimaryTabRecognized(verification.Lines, primaryName, currentWindow.ClientWidth, currentWindow.ClientHeight);
                    NativeOcrDiagnostics.Write($"OCR full primary={primaryName} attempt={attempt + 1} recognized={switched}");
                }

                if (!switched)
                {
                    AddScanWarning(mergedUnmatched, primaryName, "一级分类切换或 OCR 验证失败，已跳过");
                    continue;
                }

                var secondaryNames = secondaryMap.GetValueOrDefault(primaryName, []).ToArray();
                var visited = new HashSet<string>(StringComparer.Ordinal);
                var noNewTabRounds = 0;
                while (noNewTabRounds < 3 && visited.Count < secondaryNames.Length)
                {
                    _ocrCancellation.Token.ThrowIfCancellationRequested();
                    var tabScan = await navigationService.ScanAsync(gameProcessNames, 1920, 1080, _ocrCancellation.Token);
                    if (!tabScan.IsSuccess)
                    {
                        AddScanWarning(mergedUnmatched, primaryName, tabScan.Error?.Message ?? "二级分类识别失败");
                        break;
                    }
                    currentWindow = tabScan.Window ?? currentWindow;
                    var visibleTabs = FindVisibleSecondaryTabs(tabScan.Lines, secondaryNames, currentWindow.ClientWidth, currentWindow.ClientHeight);
                    var foundNew = false;
                    foreach (var tab in visibleTabs)
                    {
                        _ocrCancellation.Token.ThrowIfCancellationRequested();
                        if (!visited.Add(tab.Name)) continue;
                        foundNew = true;
                        var secondaryX = (int)(currentWindow.ClientWidth * ((0.1005 + 0.3479) / 2));
                        HintText.Text = $"正在扫描：{primaryName} / {tab.Name}（{visited.Count}/{secondaryNames.Length}）";
                        if (!await capture.ClickAsync(currentWindow, secondaryX, tab.ClientY, _ocrCancellation.Token))
                        {
                            AddScanWarning(mergedUnmatched, $"{primaryName}/{tab.Name}", "二级分类点击失败");
                            continue;
                        }
                        await Task.Delay(500, _ocrCancellation.Token);
                        await ScanAchievementCategoryAsync(
                            capture,
                            achievementService,
                            gameProcessNames,
                            rows,
                            mergedCandidates,
                            mergedUnmatched,
                            primaryName,
                            tab.Name,
                            _ocrCancellation.Token);
                    }

                    if (visited.Count >= secondaryNames.Length) break;
                    noNewTabRounds = foundNew ? 0 : noNewTabRounds + 1;
                    var scrollX = (int)(currentWindow.ClientWidth * ((0.1005 + 0.3479) / 2));
                    var scrollY = (int)(currentWindow.ClientHeight * ((0.1796 + 1.0) / 2));
                    if (!await capture.ScrollAtAsync(currentWindow, scrollX, scrollY, -160, 16, _ocrCancellation.Token))
                    {
                        AddScanWarning(mergedUnmatched, primaryName, "二级分类列表滚动失败");
                        break;
                    }
                    await Task.Delay(800, _ocrCancellation.Token);
                }

                foreach (var missing in secondaryNames.Where(name => !visited.Contains(name)))
                {
                    AddScanWarning(mergedUnmatched, $"{primaryName}/{missing}", "二级分类未访问");
                }
            }

            WindowState = previousState;
            Activate();
            var mergedPreview = MergeOcrPreviews(mergedCandidates, mergedUnmatched, rows);
            NativeOcrDiagnostics.Write($"OCR full finished candidates={mergedPreview.Candidates.Count} unmatched={mergedPreview.Unmatched.Count}");
            if (mergedPreview.Candidates.Count == 0)
            {
                ShowOcrError("OCR 全量扫描没有匹配到成就。请确认游戏处于成就页面，并检查 native-ocr.log。", "OCR 扫描结果");
                return;
            }
            var previewWindow = new OcrPreviewWindow(mergedPreview) { Owner = this };
            if (previewWindow.ShowDialog() != true || previewWindow.AcceptedPreview is null)
            {
                HintText.Text = "OCR 全量扫描结果未应用，当前进度保持不变。";
                return;
            }
            var applied = await _workspace.ApplyOcrPreviewAsync(previewWindow.AcceptedPreview, confirm: true, cancellationToken: _ocrCancellation.Token);
            if (!applied.IsSuccess)
            {
                ShowOcrError(applied.Error?.Message ?? "OCR 结果应用失败。", "OCR 结果应用失败");
                return;
            }
            RefreshView();
            HintText.Text = $"OCR 全量扫描已应用 {applied.Updated} 条 · 防止降级 {applied.PreventedDowngrades} 条 · 未变化 {applied.Unchanged} 条";
        }
        catch (OperationCanceledException)
        {
            HintText.Text = "OCR 全量扫描已取消。";
        }
        catch (GameWindowNotFoundException exception)
        {
            NativeOcrDiagnostics.Write($"OCR full exception GameWindowNotFound: {exception}");
            ShowOcrError($"未找到可捕获的游戏窗口：{exception.Message}", "未找到游戏窗口");
        }
        catch (GameWindowCaptureException exception)
        {
            NativeOcrDiagnostics.Write($"OCR full exception GameWindowCapture: {exception}");
            ShowOcrError($"无法捕获游戏画面：{exception.Message}", "游戏画面捕获失败");
        }
        catch (Exception exception)
        {
            NativeOcrDiagnostics.Write($"OCR full exception: {exception}");
            ShowOcrError($"OCR 全量扫描失败：{exception.Message}", "OCR 全量扫描失败");
        }
        finally
        {
            WindowState = previousState;
            _ocrCancellation?.Dispose();
            _ocrCancellation = null;
            OcrFullScanButton.Content = "OCR 全量扫描所有分类";
            OcrFullScanButton.IsEnabled = true;
            OcrScanButton.IsEnabled = true;
        }
    }

    private async Task<OcrCategoryScanStats> ScanAchievementCategoryAsync(
        WindowsGameWindowCapture capture,
        SinglePageOcrScanService service,
        IReadOnlyCollection<string> gameProcessNames,
        IReadOnlyList<AchievementRow> rows,
        IDictionary<AchievementId, OcrAchievementCandidate> mergedCandidates,
        IDictionary<string, OcrUnmatchedText> mergedUnmatched,
        string primaryName,
        string secondaryName,
        CancellationToken cancellationToken)
    {
        var seenIds = new HashSet<AchievementId>();
        var pages = 0;
        var lines = 0;
        for (var page = 1; page <= 80; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            HintText.Text = $"正在扫描：{primaryName} / {secondaryName} · 第 {page} 页 · 已识别 {mergedCandidates.Count} 条";
            var scan = await service.ScanAsync(gameProcessNames, 1920, 1080, cancellationToken);
            if (!scan.IsSuccess)
            {
                AddScanWarning(mergedUnmatched, $"{primaryName}/{secondaryName}", scan.Error?.Message ?? "成就页面扫描失败");
                return new OcrCategoryScanStats(pages, lines, false);
            }

            pages = page;
            lines += scan.Lines.Count;
            var preview = AchievementOcrMatcher.CreatePreview(scan.Lines, rows);
            var pageIds = preview.Candidates.Select(candidate => candidate.AchievementId).ToHashSet();
            if (page > 1 && (pageIds.Count == 0 || pageIds.IsSubsetOf(seenIds))) break;
            foreach (var candidate in preview.Candidates)
            {
                if (!mergedCandidates.TryGetValue(candidate.AchievementId, out var existing) || PreferOcrCandidate(candidate, existing))
                {
                    mergedCandidates[candidate.AchievementId] = candidate;
                }
            }
            foreach (var unmatched in preview.Unmatched)
            {
                mergedUnmatched.TryAdd($"{primaryName}/{secondaryName}/{unmatched.Text}\u001f{unmatched.Reason}", unmatched);
            }
            seenIds.UnionWith(pageIds);
            if (scan.Window is null || page == 80) break;
            if (!await capture.ScrollAsync(scan.Window, -160, 15, cancellationToken))
            {
                AddScanWarning(mergedUnmatched, $"{primaryName}/{secondaryName}", "成就列表滚动失败");
                return new OcrCategoryScanStats(pages, lines, false);
            }
            await Task.Delay(800, cancellationToken);
        }
        return new OcrCategoryScanStats(pages, lines, true);
    }

    private static Dictionary<string, IReadOnlyList<string>> BuildSecondaryCategoryMap(
        IReadOnlyList<AchievementRow> rows,
        IReadOnlyList<string> primaryNames) =>
        primaryNames.ToDictionary(
            primary => primary,
            primary => (IReadOnlyList<string>)rows
                .Where(row => string.Equals(row.FirstCategory, primary, StringComparison.Ordinal))
                .OrderBy(row => row.AbsoluteOrder)
                .Select(row => row.SecondCategory)
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            StringComparer.Ordinal);

    private static bool IsPrimaryTabRecognized(
        IReadOnlyList<OcrTextLine> lines,
        string expectedName,
        int width,
        int height)
    {
        var x1 = width * 0.053;
        var y1 = height * 0.047;
        var x2 = width * 0.114;
        var y2 = height * 0.083;
        var text = string.Concat(lines
            .Where(line => IsLineInRegion(line, x1, y1, x2, y2))
            .OrderBy(line => LineCenterY(line))
            .Select(line => line.Text.Trim()));
        var matched = AchievementOcrMatcher.MatchKnownText(text, [expectedName], out var confidence) is not null;
        NativeOcrDiagnostics.Write($"OCR primary expected={expectedName} text={text} matched={matched} confidence={confidence:F3}");
        return matched;
    }

    private static IReadOnlyList<NavigationTab> FindVisibleSecondaryTabs(
        IReadOnlyList<OcrTextLine> lines,
        IReadOnlyList<string> knownNames,
        int width,
        int height)
    {
        var x1 = width * 0.1005;
        var y1 = height * 0.1796;
        var x2 = width * 0.3479;
        var y2 = height;
        var matches = new Dictionary<string, NavigationTab>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            if (!IsLineInRegion(line, x1, y1, x2, y2)) continue;
            var matched = AchievementOcrMatcher.MatchKnownText(line.Text, knownNames, out _);
            if (matched is null) continue;
            var tab = new NavigationTab(matched, (int)Math.Round(LineCenterY(line)));
            if (!matches.TryGetValue(matched, out var existing) || tab.ClientY < existing.ClientY)
            {
                matches[matched] = tab;
            }
        }
        var result = matches.Values.OrderBy(tab => tab.ClientY).ToArray();
        NativeOcrDiagnostics.Write($"OCR secondary visible={result.Length} names=[{string.Join(",", result.Select(tab => tab.Name))}]");
        return result;
    }

    private static bool IsLineInRegion(OcrTextLine line, double x1, double y1, double x2, double y2)
    {
        if (line.Points.Count == 0) return false;
        var centerX = line.Points.Average(point => point.X);
        var centerY = line.Points.Average(point => point.Y);
        return centerX >= x1 && centerX <= x2 && centerY >= y1 && centerY <= y2;
    }

    private static double LineCenterY(OcrTextLine line) =>
        line.Points.Count == 0 ? double.MaxValue : line.Points.Average(point => point.Y);

    private static void AddScanWarning(IDictionary<string, OcrUnmatchedText> warnings, string scope, string reason) =>
        warnings.TryAdd($"warning:{scope}:{reason}", new OcrUnmatchedText($"[{scope}]", reason, 0));

    private sealed record NavigationTab(string Name, int ClientY);

    private sealed record OcrCategoryScanStats(int Pages, int Lines, bool IsSuccess);

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

    private static bool PreferOcrCandidate(OcrAchievementCandidate candidate, OcrAchievementCandidate existing)
    {
        if (candidate.ProposedStatus == ProgressStatus.Completed && existing.ProposedStatus != ProgressStatus.Completed) return true;
        if (existing.ProposedStatus == ProgressStatus.Completed && candidate.ProposedStatus != ProgressStatus.Completed) return false;
        return candidate.MatchConfidence > existing.MatchConfidence;
    }

    private static OcrScanPreview MergeOcrPreviews(
        IReadOnlyDictionary<AchievementId, OcrAchievementCandidate> candidates,
        IReadOnlyDictionary<string, OcrUnmatchedText> unmatched,
        IReadOnlyList<AchievementRow> rows)
    {
        var order = rows.ToDictionary(row => row.Id, row => row.AbsoluteOrder);
        var orderedCandidates = candidates.Values
            .OrderBy(candidate => order.GetValueOrDefault(candidate.AchievementId, int.MaxValue))
            .ToArray();
        return new OcrScanPreview(
            Array.AsReadOnly(orderedCandidates),
            Array.AsReadOnly(unmatched.Values.ToArray()),
            orderedCandidates.Count(candidate => candidate.ProposedStatus == ProgressStatus.Completed),
            orderedCandidates.Count(candidate => candidate.ProposedStatus == ProgressStatus.Incomplete),
            orderedCandidates.Count(candidate => candidate.ProposedStatus is null));
    }

    private static string? FindOcrTemplateDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidates = new[]
            {
                Path.Combine(directory.FullName, "resources", "ocr_templates"),
                Path.Combine(directory.FullName, "ocr_templates")
            };
            foreach (var candidate in candidates)
            {
                if (File.Exists(Path.Combine(candidate, "icon_1star.png")) &&
                    File.Exists(Path.Combine(candidate, "icon_2star.png")) &&
                    File.Exists(Path.Combine(candidate, "icon_3star.png")))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static bool IsAnyProcessRunning(IEnumerable<string> processNames)
    {
        foreach (var processName in processNames)
        {
            var normalizedName = Path.GetFileNameWithoutExtension(processName);
            try
            {
                var processes = Process.GetProcessesByName(normalizedName);
                try
                {
                    if (processes.Length > 0) return true;
                }
                finally
                {
                    foreach (var process in processes) process.Dispose();
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Ignore processes that are not accessible to the current user.
            }
        }

        return false;
    }

    private static OcrAssets? FindOcrAssets()
    {
        var configuredRoot = Environment.GetEnvironmentVariable("WUWA_NATIVE_OCR_ROOT");
        var configuredModelRoot = Environment.GetEnvironmentVariable("WUWA_NATIVE_OCR_MODEL_ROOT");
        var repositoryRoot = FindRepositoryRoot();
        var roots = new List<string>();
        if (!string.IsNullOrWhiteSpace(configuredRoot)) roots.Add(Path.GetFullPath(configuredRoot));
        roots.Add(Path.Combine(AppContext.BaseDirectory, "ocr"));
        if (repositoryRoot is not null)
        {
            roots.Add(Path.Combine(repositoryRoot, "native", "ocr", "build", "Debug"));
            roots.Add(Path.Combine(repositoryRoot, "native", "ocr", "build", "Release"));
        }

        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var modelRoots = new List<string>();
            if (!string.IsNullOrWhiteSpace(configuredModelRoot)) modelRoots.Add(Path.GetFullPath(configuredModelRoot));
            modelRoots.Add(Path.Combine(root, "models", "ppocrv5"));
            if (repositoryRoot is not null) modelRoots.Add(Path.Combine(repositoryRoot, "onnxocr", "models", "ppocrv5"));

            foreach (var modelRoot in modelRoots.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (File.Exists(Path.Combine(root, "Wuwa.Ocr.Native.dll")) &&
                    File.Exists(Path.Combine(modelRoot, "rec", "rec.onnx")) &&
                    File.Exists(Path.Combine(modelRoot, "det", "det.onnx")) &&
                    File.Exists(Path.Combine(modelRoot, "cls", "cls.onnx")) &&
                    File.Exists(Path.Combine(modelRoot, "ppocrv5_dict.txt")))
                {
                    return new OcrAssets(root, modelRoot);
                }
            }
        }

        return null;
    }

    private static string? FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "native", "ocr")) &&
                Directory.Exists(Path.Combine(directory.FullName, "onnxocr", "models", "ppocrv5")))
            {
                return directory.FullName;
            }
        }

        return null;
    }

    private sealed record OcrAssets(string Root, string ModelRoot);

    private void ShowOcrError(string message, string title)
    {
        ShowError(message);
        MessageBox.Show(this, message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        HintText.Text = _view is null ? "工作区加载失败；请检查 shipped 资源文件。" : "操作失败；请查看上方错误信息。";
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
