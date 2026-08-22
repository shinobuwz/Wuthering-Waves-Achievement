using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Microsoft.Win32;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Diagnostics;
using System.ComponentModel;
using System.IO;
using System.Security;
using System.Runtime.InteropServices;
using Wuwa.Core;
using Wuwa.Infrastructure;

namespace Wuwa.App;

public partial class MainWindow : Window
{
    // A negative drag moves the list content upward, advancing to lower rows.
    // Keep the movement below the client height so each OCR pass can overlap rows.
    private const int OcrListDragPixels = -600;
    private const int SecondaryNavigationDragPixels = OcrListDragPixels;
    private const int OcrCancelHotKeyId = 0x5741;
    private const uint OcrCancelHotKeyModifiers = 0x0002 | 0x0004; // CTRL + SHIFT
    private const int MapHotKeyId = 0x5742;
    private const uint MapHotKeyAltModifiers = 0x0001 | 0x4000; // ALT + MOD_NOREPEAT
    private const uint MapHotKeyFallbackModifiers = 0x0001 | 0x0002 | 0x4000; // CTRL + ALT + MOD_NOREPEAT
    private const uint VirtualKeyF12 = 0x7B;
    private const uint VirtualKeyM = 0x4D;
    private const int WmHotKey = 0x0312;
    private static readonly string[] MapGameProcessNames = ["Client-Win64-Shipping.exe", "Wuthering Waves.exe"];

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly AchievementWorkspace _workspace;
    private WorkspaceView? _view;
    private bool _initializingFilters;
    private CancellationTokenSource? _ocrCancellation;
    private OcrScanMode? _activeOcrMode;
    private HwndSource? _windowSource;
    private bool _ocrCancelHotKeyRegistered;
    private bool _mapHotKeyRegistered;
    private string _mapHotKeyLabel = "未注册";
    private MapOverlayWindow? _mapOverlay;
    private WindowsGameWindowCapture? _mapCapture;
    private GameWindowCandidate? _mapGameWindow;
    private DispatcherTimer? _mapTrackingTimer;
    private bool _mapToggleInProgress;
    private bool _isLightTheme;

    public MainWindow(AchievementWorkspace workspace)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        InitializeComponent();
        SourceInitialized += MainWindow_OnSourceInitialized;
        Closed += MainWindow_OnClosed;
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
        FilterSummaryText.Text = $"显示 {_view.Rows.Count} 条";
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

    private void SearchBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
        RefreshView();
    }

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
        StatusCombo.SelectedIndex = 0;
        GroupCombo.SelectedIndex = 0;
        SortCombo.SelectedIndex = 0;
        RefreshView();
    }

    private void ConveneLink_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var logPath = FindConveneLogPath();
            if (logPath is null)
            {
                var dialog = new OpenFileDialog
                {
                    Title = "选择鸣潮 Client.log",
                    Filter = "Client.log|Client.log|日志文件 (*.log)|*.log|所有文件 (*.*)|*.*",
                    CheckFileExists = true,
                    Multiselect = false,
                    FileName = "Client.log"
                };
                if (dialog.ShowDialog(this) != true)
                {
                    return;
                }

                logPath = dialog.FileName;
            }

            var link = ConveneLinkExtractor.Extract(ReadSharedFile(logPath));
            if (string.IsNullOrWhiteSpace(link))
            {
                const string message = "未找到唤取记录链接。\n\n请先在游戏内手动打开“唤取记录”，点击翻页或切换到历史记录，等待记录加载完成后再重试。";
                ShowError(message.Replace("\n\n", " "));
                MessageBox.Show(this, message, "无法获取唤取链接", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SetClipboardWithRetry(link);
            ErrorText.Text = string.Empty;
            HintText.Text = "已获取并复制唤取链接，有效期约 1 小时。";
            MessageBox.Show(this, "唤取链接已复制到剪贴板。\n\n链接通常有效期约 1 小时。", "获取成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            var message = $"获取唤取链接失败：{exception.Message}";
            ShowError(message);
            MessageBox.Show(this, message, "获取唤取链接失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ConveneHelp_OnClick(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            this,
            "获取唤取链接前，请先在游戏内完成以下操作：\n\n1. 打开“唤取记录”界面。\n2. 点击翻页，或切换到任意历史记录页。\n3. 等待记录加载完成后，回到这里点击“获取唤取链接”。\n\n链接会从 Client.log 中读取，并复制到剪贴板。",
            "获取唤取链接说明",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private static void SetClipboardWithRetry(string text)
    {
        ExternalException? lastException = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                Clipboard.SetText(text);
                return;
            }
            catch (ExternalException exception)
            {
                lastException = exception;
                Thread.Sleep(80);
            }
        }

        // WPF's clipboard wrapper can lose a race with overlays, game clients, or
        // clipboard managers. clip.exe performs the same operation through the
        // Windows shell and is a reliable fallback for this short-lived text.
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = Path.Combine(Environment.SystemDirectory, "clip.exe"),
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    CreateNoWindow = true
                }
            };
            if (!process.Start())
            {
                throw new InvalidOperationException("无法启动 Windows 剪贴板工具。");
            }

            process.StandardInput.Write(text);
            process.StandardInput.Close();
            if (!process.WaitForExit(2000) || process.ExitCode != 0)
            {
                throw new InvalidOperationException("Windows 剪贴板工具执行失败。");
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or ExternalException)
        {
            throw lastException ?? new ExternalException($"无法写入系统剪贴板：{exception.Message}");
        }
    }

    private static byte[] ReadSharedFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static string? FindConveneLogPath()
    {
        const string uninstallKey = @"SOFTWARE\\WOW6432Node\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\KRInstall Wuthering Waves";
        var registryCandidates = new List<string>();

        foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var key = baseKey.OpenSubKey(uninstallKey);
                if (key is null)
                {
                    continue;
                }

                foreach (var valueName in new[] { "wwGamePath", "InstallPath" })
                {
                    if (key.GetValue(valueName) is string value && !string.IsNullOrWhiteSpace(value))
                    {
                        registryCandidates.Add(value);
                    }
                }
            }
            catch (SecurityException)
            {
                // Registry access is optional; the file picker remains available.
            }
            catch (IOException)
            {
                // Registry access is optional; the file picker remains available.
            }
        }

        foreach (var candidate in registryCandidates)
        {
            var logPath = ResolveConveneLogPath(candidate);
            if (logPath is not null)
            {
                return logPath;
            }
        }

        foreach (var drive in DriveInfo.GetDrives().Where(drive => drive.IsReady))
        {
            foreach (var candidate in new[]
            {
                Path.Combine(drive.RootDirectory.FullName, "Wuthering Waves Game"),
                Path.Combine(drive.RootDirectory.FullName, "Wuthering Waves", "Wuthering Waves Game"),
                Path.Combine(drive.RootDirectory.FullName, "Program Files", "Epic Games", "WutheringWavesj3oFh"),
                Path.Combine(drive.RootDirectory.FullName, "Program Files", "Epic Games", "WutheringWavesj3oFh", "Wuthering Waves Game")
            })
            {
                var logPath = ResolveConveneLogPath(candidate);
                if (logPath is not null)
                {
                    return logPath;
                }
            }
        }

        return null;
    }

    private static string? ResolveConveneLogPath(string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        var normalized = candidate.Trim().Trim('"');
        var candidates = new[]
        {
            normalized,
            Path.Combine(normalized, "Wuthering Waves Game")
        };

        foreach (var root in candidates)
        {
            var logPath = Path.Combine(root, "Client", "Saved", "Logs", "Client.log");
            if (File.Exists(logPath))
            {
                return logPath;
            }
        }

        return null;
    }

    private void MainWindow_OnSourceInitialized(object? sender, EventArgs e)
    {
        _windowSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        if (_windowSource is null) return;
        _windowSource.AddHook(MainWindowMessageHook);
        _ocrCancelHotKeyRegistered = RegisterHotKey(
            _windowSource.Handle,
            OcrCancelHotKeyId,
            OcrCancelHotKeyModifiers,
            VirtualKeyF12);
        NativeOcrDiagnostics.Write($"OCR cancel hotkey registered={_ocrCancelHotKeyRegistered} shortcut=Ctrl+Shift+F12");

        _mapHotKeyRegistered = RegisterHotKey(
            _windowSource.Handle,
            MapHotKeyId,
            MapHotKeyAltModifiers,
            VirtualKeyM);
        if (_mapHotKeyRegistered)
        {
            _mapHotKeyLabel = "Alt+M";
        }
        else
        {
            _mapHotKeyRegistered = RegisterHotKey(
                _windowSource.Handle,
                MapHotKeyId,
                MapHotKeyFallbackModifiers,
                VirtualKeyM);
            _mapHotKeyLabel = _mapHotKeyRegistered ? "Ctrl+Alt+M" : "不可用";
        }
        MapShortcutText.Text = $"地图快捷键：{_mapHotKeyLabel}";
        NativeOcrDiagnostics.Write($"Map hotkey registered={_mapHotKeyRegistered} shortcut={_mapHotKeyLabel}");
    }

    private void MainWindow_OnClosed(object? sender, EventArgs e)
    {
        _mapTrackingTimer?.Stop();
        _mapTrackingTimer = null;
        if (_mapOverlay is not null)
        {
            _mapOverlay.HideRequested -= MapOverlay_OnHideRequested;
            _mapOverlay.Close();
            _mapOverlay = null;
        }
        _mapGameWindow = null;
        _mapCapture = null;

        if (_windowSource is null) return;
        if (_ocrCancelHotKeyRegistered)
        {
            UnregisterHotKey(_windowSource.Handle, OcrCancelHotKeyId);
            _ocrCancelHotKeyRegistered = false;
        }
        if (_mapHotKeyRegistered)
        {
            UnregisterHotKey(_windowSource.Handle, MapHotKeyId);
            _mapHotKeyRegistered = false;
        }
        _windowSource.RemoveHook(MainWindowMessageHook);
        _windowSource = null;
    }

    private IntPtr MainWindowMessageHook(
        IntPtr hwnd,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if (message != WmHotKey)
        {
            return IntPtr.Zero;
        }

        if (wParam.ToInt32() == OcrCancelHotKeyId)
        {
            var messageText = _activeOcrMode == OcrScanMode.FullScan
                ? "正在取消 OCR 全量扫描…（Ctrl+Shift+F12）"
                : "正在取消 OCR 扫描…（Ctrl+Shift+F12）";
            RequestOcrCancellation(messageText);
            handled = true;
        }
        else if (wParam.ToInt32() == MapHotKeyId)
        {
            _ = ToggleMapOverlayAsync();
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void RequestOcrCancellation(string message)
    {
        if (_ocrCancellation is null) return;
        _ocrCancellation.Cancel();
        OcrScanButton.IsEnabled = false;
        OcrFullScanButton.IsEnabled = false;
        OcrNavigationDebugButton.IsEnabled = false;
        HintText.Text = message;
        NativeOcrDiagnostics.Write("OCR cancellation requested");
    }

    private async void MapOverlay_OnClick(object sender, RoutedEventArgs e)
    {
        await ToggleMapOverlayAsync();
    }

    private async Task ToggleMapOverlayAsync()
    {
        if (_mapToggleInProgress)
        {
            return;
        }

        _mapToggleInProgress = true;
        try
        {
            if (_mapOverlay?.IsVisible == true)
            {
                HideMapOverlay(restoreGameFocus: true);
                return;
            }

            var capture = _mapCapture ??= new WindowsGameWindowCapture();
            HintText.Text = "正在查找《鸣潮》游戏窗口…";
            var gameWindow = await capture.TryFindGameWindowAsync(
                MapGameProcessNames,
                minimumWidth: 800,
                minimumHeight: 600);
            if (gameWindow is null)
            {
                ShowMapError("未找到可覆盖的《鸣潮》窗口。请先启动游戏，并确认游戏处于可见、未最小化状态。", "找不到游戏窗口");
                return;
            }

            if (!capture.TryGetClientBounds(gameWindow, out var bounds))
            {
                ShowMapError("无法取得游戏客户区位置。请将《鸣潮》切换到无边框/窗口化全屏后重试。", "无法定位游戏窗口");
                return;
            }

            _mapGameWindow = gameWindow;
            var overlay = _mapOverlay;
            if (overlay is null)
            {
                overlay = new MapOverlayWindow();
                overlay.HideRequested += MapOverlay_OnHideRequested;
                overlay.Closed += MapOverlay_OnClosed;
                _mapOverlay = overlay;
            }

            overlay.ApplyClientBounds(bounds);
            if (!overlay.IsVisible)
            {
                overlay.Show();
            }

            try
            {
                await overlay.InitializeBrowserAsync();
            }
            catch (MapOverlayUnavailableException exception)
            {
                NativeOcrDiagnostics.Write($"Map overlay WebView2 initialization failed: {exception}");
                overlay.Close();
                ShowMapError(exception.Message, "地图浏览器不可用");
                return;
            }

            overlay.ApplyClientBounds(bounds);
            StartMapTracking();
            MapOverlayButton.Content = "隐藏游戏地图";
            HintText.Text = $"游戏地图已打开 · {_mapHotKeyLabel} 或 Esc 隐藏";
            ErrorText.Text = string.Empty;
            NativeOcrDiagnostics.Write($"Map overlay shown handle=0x{gameWindow.Handle.ToInt64():X} client={bounds.Left},{bounds.Top},{bounds.Width}x{bounds.Height}");
        }
        catch (Exception exception)
        {
            NativeOcrDiagnostics.Write($"Map overlay failed: {exception}");
            ShowMapError($"打开游戏地图失败：{exception.Message}", "打开游戏地图失败");
        }
        finally
        {
            _mapToggleInProgress = false;
        }
    }

    private void StartMapTracking()
    {
        if (_mapTrackingTimer is null)
        {
            _mapTrackingTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(200)
            };
            _mapTrackingTimer.Tick += MapTrackingTimer_OnTick;
        }
        _mapTrackingTimer.Start();
    }

    private void MapTrackingTimer_OnTick(object? sender, EventArgs e)
    {
        if (_mapOverlay?.IsVisible != true || _mapCapture is null || _mapGameWindow is null)
        {
            _mapTrackingTimer?.Stop();
            return;
        }

        if (!_mapCapture.TryGetClientBounds(_mapGameWindow, out var bounds))
        {
            HideMapOverlay(restoreGameFocus: false);
            return;
        }

        var overlayHandle = new WindowInteropHelper(_mapOverlay).Handle;
        var gameIsForeground = _mapCapture.IsForegroundWindow(_mapGameWindow.Handle);
        var overlayIsForeground = overlayHandle != IntPtr.Zero && _mapCapture.IsForegroundWindow(overlayHandle);
        if (!gameIsForeground && !overlayIsForeground)
        {
            HideMapOverlay(restoreGameFocus: false);
            return;
        }

        _mapOverlay.ApplyClientBounds(bounds);
    }

    private void MapOverlay_OnHideRequested(object? sender, EventArgs e)
    {
        HideMapOverlay(restoreGameFocus: true);
    }

    private void MapOverlay_OnClosed(object? sender, EventArgs e)
    {
        if (sender is MapOverlayWindow overlay)
        {
            overlay.HideRequested -= MapOverlay_OnHideRequested;
            overlay.Closed -= MapOverlay_OnClosed;
        }
        _mapTrackingTimer?.Stop();
        _mapOverlay = null;
        _mapGameWindow = null;
        MapOverlayButton.Content = "打开游戏地图";
    }

    private void HideMapOverlay(bool restoreGameFocus)
    {
        _mapTrackingTimer?.Stop();
        if (_mapOverlay?.IsVisible == true)
        {
            _mapOverlay.Hide();
        }
        MapOverlayButton.Content = "打开游戏地图";
        HintText.Text = "游戏地图已隐藏。";
        if (restoreGameFocus && _mapCapture is not null && _mapGameWindow is not null)
        {
            _mapCapture.TryActivateWindow(_mapGameWindow);
        }
    }

    private void ShowMapError(string message, string title)
    {
        ShowError(message);
        MessageBox.Show(this, message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private async void OcrScan_OnClick(object sender, RoutedEventArgs e)
    {
        if (_ocrCancellation is not null)
        {
            RequestOcrCancellation("正在取消 OCR 扫描…");
            return;
        }

        var ocrAssets = FindOcrAssets();
        if (ocrAssets is null)
        {
            NativeOcrDiagnostics.Write("OCR start failed: assets not found");
            ShowOcrError("当前程序目录未找到内置 OCR 组件。请使用包含 ocr/ 目录的 Native 发布包运行；如果是源码开发环境，请先构建 Native OCR 资源后重新生成 Release 输出。", "OCR 组件缺失");
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
        _activeOcrMode = OcrScanMode.CurrentCategory;
        OcrScanButton.Content = "取消 OCR";
        OcrFullScanButton.IsEnabled = false;
        ReportOcrProgress(OcrScanMode.CurrentCategory, OcrScanPhase.Preparing, "正在检测游戏窗口并扫描当前页面…");
        ErrorText.Text = string.Empty;
        var previousState = WindowState;
        try
        {
            using var client = new NativeOcrClient(new NativeOcrOptions(recognitionModel, dictionary, MinimumScore: 0.0f));
            var reader = new NativeOcrTemplateTextReader(client, templateDirectory);
            var capture = new WindowsGameWindowCapture();
            ReportOcrProgress(OcrScanMode.CurrentCategory, OcrScanPhase.FindingGameWindow, "正在检测游戏窗口…");
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
                ReportOcrProgress(OcrScanMode.CurrentCategory, OcrScanPhase.ScanningCurrentCategory,
                    $"正在扫描当前分类第 {page} 页，已识别 {mergedCandidates.Count} 条…请勿操作游戏窗口。",
                    page: page, matchedCount: mergedCandidates.Count, unmatchedCount: mergedUnmatched.Count);
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
                ReportOcrProgress(OcrScanMode.CurrentCategory, OcrScanPhase.ScanningCurrentCategory,
                    $"第 {page} 页识别完成，已识别 {mergedCandidates.Count} 条。",
                    page: page, matchedCount: mergedCandidates.Count, unmatchedCount: mergedUnmatched.Count);

                if (scan.Window is null || page == 80) break;
                ReportOcrProgress(OcrScanMode.CurrentCategory, OcrScanPhase.ScrollingCategory,
                    $"正在拖动成就列表…", page: page, matchedCount: mergedCandidates.Count, unmatchedCount: mergedUnmatched.Count);
                var dragAccepted = await capture.DragScrollAsync(scan.Window, dragPixels: OcrListDragPixels, cancellationToken: _ocrCancellation.Token);
                NativeOcrDiagnostics.Write($"OCR page={page} dragAccepted={dragAccepted}");
                if (!dragAccepted)
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
            _ocrCancellation?.Dispose();
            _ocrCancellation = null;
            _activeOcrMode = null;
            OcrScanButton.Content = "OCR 自动扫描当前分类";
            OcrFullScanButton.IsEnabled = true;
            OcrScanButton.IsEnabled = true;
        }
    }

    private async void OcrNavigationDebug_OnClick(object sender, RoutedEventArgs e)
    {
        if (_ocrCancellation is not null)
        {
            RequestOcrCancellation("正在取消分类切换测试…");
            return;
        }

        var ocrAssets = FindOcrAssets();
        if (ocrAssets is null)
        {
            ShowOcrError("原生 OCR 组件尚未部署。开发环境请先运行 scripts/build-native-ocr.ps1。", "OCR 组件缺失");
            return;
        }

        var gameProcessNames = new[] { "Client-Win64-Shipping.exe", "Wuthering Waves.exe" };
        if (!IsAnyProcessRunning(gameProcessNames))
        {
            ShowOcrError("未检测到《鸣潮》游戏进程，请先启动游戏后再进行分类切换测试。", "未检测到游戏");
            return;
        }

        _ocrCancellation = new CancellationTokenSource();
        _activeOcrMode = OcrScanMode.CurrentCategory;
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
            var secondaryNavigationReader = new RegionOcrTextReader(navigationReader, 0.145, 0.18, 0.31, 0.95);
            var capture = new WindowsGameWindowCapture();
            var navigationService = new SinglePageOcrScanService(capture, navigationReader);
            var secondaryNavigationService = new SinglePageOcrScanService(capture, secondaryNavigationReader);
            var initialWindow = await capture.TryFindGameWindowAsync(gameProcessNames, minimumWidth: 800, minimumHeight: 600, cancellationToken: _ocrCancellation.Token);
            if (initialWindow is null)
            {
                ShowOcrError("找到了游戏进程，但没有找到可见且分辨率至少为 800×600 的游戏窗口。", "找不到游戏窗口");
                return;
            }

            var primaryYPercentages = new[] { 0.1778, 0.2981, 0.4343, 0.5537 };
            var workspaceSnapshot = _workspace.GetSnapshot();
            var rows = workspaceSnapshot.Rows;
            var categories = workspaceSnapshot.Categories;
            var currentWindow = initialWindow;
            var primarySucceeded = 0;
            var primarySkipped = 0;
            var secondaryClicked = 0;
            var secondaryFailed = 0;
            var failures = new List<string>();

            WindowState = WindowState.Minimized;
            await Task.Delay(350, _ocrCancellation.Token);

            for (var primaryIndex = 0; primaryIndex < primaryYPercentages.Length; primaryIndex++)
            {
                _ocrCancellation.Token.ThrowIfCancellationRequested();
                var primarySlotName = $"一级图标 {primaryIndex + 1}";
                var primaryName = primarySlotName;
                var primaryX = (int)(currentWindow.ClientWidth * 0.0417);
                var primaryY = (int)(currentWindow.ClientHeight * primaryYPercentages[primaryIndex]);
                var primarySwitched = false;
                for (var attempt = 1; attempt <= 3 && !primarySwitched; attempt++)
                {
                    HintText.Text = $"DEBUG：切换{primarySlotName}（{attempt}/3）";
                    var clicked = await capture.ClickAsync(currentWindow, primaryX, primaryY, _ocrCancellation.Token);
                    NativeOcrDiagnostics.Write($"OCR navigation debug primary={primarySlotName} attempt={attempt} clicked={clicked} client={primaryX},{primaryY}");
                    if (!clicked) break;
                    await Task.Delay(800, _ocrCancellation.Token);
                    var verification = await navigationService.ScanAsync(gameProcessNames, 1920, 1080, _ocrCancellation.Token);
                    if (!verification.IsSuccess)
                    {
                        failures.Add($"{primarySlotName}：OCR 验证失败（{verification.Error?.Message ?? "未知错误"}）");
                        break;
                    }
                    currentWindow = verification.Window ?? currentWindow;
                    var recognizedPrimary = ReadPrimaryTabText(verification.Lines, currentWindow.ClientWidth, currentWindow.ClientHeight);
                    primaryName = string.IsNullOrWhiteSpace(recognizedPrimary) ? primarySlotName : recognizedPrimary;
                    primarySwitched = true;
                    NativeOcrDiagnostics.Write($"OCR navigation debug primary={primarySlotName} recognizedText={recognizedPrimary}");
                }

                if (!primarySwitched)
                {
                    primarySkipped++;
                    continue;
                }

                primarySucceeded++;
                var knownSecondaryNames = FindKnownSecondaryNames(rows, primaryName, categories);
                var visited = new HashSet<string>(StringComparer.Ordinal);
                for (var secondaryRound = 0; secondaryRound < 64; secondaryRound++)
                {
                    _ocrCancellation.Token.ThrowIfCancellationRequested();
                    var tabScan = await secondaryNavigationService.ScanAsync(gameProcessNames, 1920, 1080, _ocrCancellation.Token);
                    if (!tabScan.IsSuccess)
                    {
                        failures.Add($"一级 {primaryName}：二级分类 OCR 失败（{tabScan.Error?.Message ?? "未知错误"}）");
                        break;
                    }
                    currentWindow = tabScan.Window ?? currentWindow;
                    var visibleTabs = FindVisibleSecondaryTabs(
                        tabScan.Lines,
                        currentWindow.ClientWidth,
                        currentWindow.ClientHeight,
                        knownSecondaryNames);
                    var visibleSignature = BuildNavigationSignature(visibleTabs);
                    var foundNew = false;
                    foreach (var tab in visibleTabs)
                    {
                        _ocrCancellation.Token.ThrowIfCancellationRequested();
                        var key = AchievementOcrMatcher.NormalizeName(tab.Name);
                        if (!visited.Add(key)) continue;
                        foundNew = true;
                        var secondaryX = tab.ClientX;
                        HintText.Text = $"DEBUG：点击二级分类 {primaryName} / {tab.Name}（已发现 {visited.Count} 个）";
                        var clicked = await capture.ClickAsync(currentWindow, secondaryX, tab.ClientY, _ocrCancellation.Token);
                        NativeOcrDiagnostics.Write($"OCR navigation debug secondary={primaryName}/{tab.Name} clicked={clicked} client={secondaryX},{tab.ClientY} textCenter={tab.ClientX},{tab.ClientY}");
                        if (!clicked)
                        {
                            secondaryFailed++;
                            failures.Add($"二级 {primaryName} / {tab.Name}：点击失败");
                            continue;
                        }
                        secondaryClicked++;
                        await Task.Delay(500, _ocrCancellation.Token);
                    }

                    NativeOcrDiagnostics.Write($"OCR navigation debug secondary-round={secondaryRound + 1} visible={visibleTabs.Count} foundNew={foundNew} visited={visited.Count} signature={visibleSignature}");
                    if (visibleTabs.Count == 0 || !foundNew)
                    {
                        NativeOcrDiagnostics.Write($"OCR navigation debug secondary-stop reason=no-new-tabs round={secondaryRound + 1} visited={visited.Count}");
                        break;
                    }

                    var scrollX = (int)(currentWindow.ClientWidth * ((0.1005 + 0.3479) / 2));
                    var scrollY = (int)(currentWindow.ClientHeight * 0.78);
                    HintText.Text = $"DEBUG：拖动二级分类列表 {primaryName}（已发现 {visited.Count} 个）";
                    if (!await capture.DragScrollAtAsync(currentWindow, scrollX, scrollY, SecondaryNavigationDragPixels, _ocrCancellation.Token))
                    {
                        failures.Add($"一级 {primaryName}：二级分类列表滚动失败");
                        break;
                    }
                    await Task.Delay(800, _ocrCancellation.Token);
                }
            }

            WindowState = previousState;
            Activate();
            var summary = $"一级分类：成功 {primarySucceeded}，跳过 {primarySkipped}\n" +
                          $"二级分类：点击成功 {secondaryClicked}，点击失败 {secondaryFailed}\n" +
                          (failures.Count == 0 ? "\n未发现切换错误。" : $"\n问题 {failures.Count} 个：\n{string.Join("\n", failures.Take(12))}");
            NativeOcrDiagnostics.Write($"OCR navigation debug finished primarySucceeded={primarySucceeded} primarySkipped={primarySkipped} secondaryClicked={secondaryClicked} secondaryFailed={secondaryFailed} failures={failures.Count}");
            MessageBox.Show(this, summary, "DEBUG 分类切换测试完成", MessageBoxButton.OK, failures.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
            HintText.Text = $"DEBUG 分类切换测试完成 · 一级成功 {primarySucceeded}/{primaryYPercentages.Length} · 二级点击成功 {secondaryClicked}";
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
            _activeOcrMode = null;
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
            RequestOcrCancellation("正在取消 OCR 全量扫描…");
            return;
        }

        var ocrAssets = FindOcrAssets();
        var templateDirectory = FindOcrTemplateDirectory();
        if (ocrAssets is null || templateDirectory is null)
        {
            ShowOcrError("当前程序目录未找到内置 OCR 组件或成就图标模板。请使用包含 ocr/ 和 resources/ocr_templates/ 的 Native 发布包运行。", "OCR 组件缺失");
            return;
        }

        var gameProcessNames = new[] { "Client-Win64-Shipping.exe", "Wuthering Waves.exe" };
        if (!IsAnyProcessRunning(gameProcessNames))
        {
            ShowOcrError("未检测到《鸣潮》游戏进程，请先启动游戏后再进行 OCR 扫描。", "未检测到游戏");
            return;
        }

        _ocrCancellation = new CancellationTokenSource();
        _activeOcrMode = OcrScanMode.FullScan;
        OcrFullScanButton.Content = "取消 OCR";
        OcrScanButton.IsEnabled = false;
        ReportOcrProgress(OcrScanMode.FullScan, OcrScanPhase.Preparing, "正在准备 OCR 全量扫描…");
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
            var secondaryNavigationReader = new RegionOcrTextReader(navigationReader, 0.145, 0.18, 0.31, 0.95);
            var capture = new WindowsGameWindowCapture();
            var navigationService = new SinglePageOcrScanService(capture, navigationReader);
            var secondaryNavigationService = new SinglePageOcrScanService(capture, secondaryNavigationReader);
            var achievementService = new SinglePageOcrScanService(capture, rowReader);
            ReportOcrProgress(OcrScanMode.FullScan, OcrScanPhase.FindingGameWindow, "正在检测游戏窗口…");
            var initialWindow = await capture.TryFindGameWindowAsync(gameProcessNames, minimumWidth: 800, minimumHeight: 600, cancellationToken: _ocrCancellation.Token);
            if (initialWindow is null)
            {
                ShowOcrError("找到了游戏进程，但没有找到可见且分辨率至少为 800×600 的游戏窗口。", "找不到游戏窗口");
                return;
            }

            var workspaceSnapshot = _workspace.GetSnapshot();
            var rows = workspaceSnapshot.Rows;
            var categories = workspaceSnapshot.Categories;
            var primaryYPercentages = new[] { 0.1778, 0.2981, 0.4343, 0.5537 };
            var mergedCandidates = new Dictionary<AchievementId, OcrAchievementCandidate>();
            var mergedUnmatched = new Dictionary<string, OcrUnmatchedText>(StringComparer.Ordinal);
            var currentWindow = initialWindow;

            WindowState = WindowState.Minimized;
            await Task.Delay(350, _ocrCancellation.Token);

            for (var primaryIndex = 0; primaryIndex < primaryYPercentages.Length; primaryIndex++)
            {
                _ocrCancellation.Token.ThrowIfCancellationRequested();
                var primarySlotName = $"一级图标 {primaryIndex + 1}";
                var primaryName = primarySlotName;
                var primaryX = (int)(currentWindow.ClientWidth * 0.0417);
                var primaryY = (int)(currentWindow.ClientHeight * primaryYPercentages[primaryIndex]);
                var switched = false;
                for (var attempt = 0; attempt < 3 && !switched; attempt++)
                {
                    ReportOcrProgress(OcrScanMode.FullScan, OcrScanPhase.SwitchingPrimaryCategory,
                        $"正在切换{primarySlotName}（{attempt + 1}/3）",
                        primaryName: primarySlotName, visitedCount: primaryIndex, totalCount: primaryYPercentages.Length,
                        matchedCount: mergedCandidates.Count, unmatchedCount: mergedUnmatched.Count);
                    HintText.Text = $"正在切换{primarySlotName}（{attempt + 1}/3）";
                    if (!await capture.ClickAsync(currentWindow, primaryX, primaryY, _ocrCancellation.Token)) break;
                    await Task.Delay(800, _ocrCancellation.Token);
                    var verification = await navigationService.ScanAsync(gameProcessNames, 1920, 1080, _ocrCancellation.Token);
                    if (!verification.IsSuccess)
                    {
                        AddScanWarning(mergedUnmatched, primarySlotName, verification.Error?.Message ?? "一级分类验证失败");
                        break;
                    }
                    currentWindow = verification.Window ?? currentWindow;
                    var recognizedPrimary = ReadPrimaryTabText(verification.Lines, currentWindow.ClientWidth, currentWindow.ClientHeight);
                    primaryName = string.IsNullOrWhiteSpace(recognizedPrimary) ? primarySlotName : recognizedPrimary;
                    switched = true;
                    NativeOcrDiagnostics.Write($"OCR full primary={primarySlotName} recognizedText={recognizedPrimary}");
                }

                if (!switched)
                {
                    AddScanWarning(mergedUnmatched, primarySlotName, "一级分类切换或 OCR 验证失败，已跳过");
                    continue;
                }

                var knownSecondaryNames = FindKnownSecondaryNames(rows, primaryName, categories);
                var visited = new HashSet<string>(StringComparer.Ordinal);
                for (var secondaryRound = 0; secondaryRound < 64; secondaryRound++)
                {
                    _ocrCancellation.Token.ThrowIfCancellationRequested();
                    ReportOcrProgress(OcrScanMode.FullScan, OcrScanPhase.DiscoveringSecondaryCategories,
                        $"正在发现{primaryName}下的二级分类…",
                        primaryName: primaryName, visitedCount: primaryIndex, totalCount: primaryYPercentages.Length,
                        matchedCount: mergedCandidates.Count, unmatchedCount: mergedUnmatched.Count);
                    var tabScan = await secondaryNavigationService.ScanAsync(gameProcessNames, 1920, 1080, _ocrCancellation.Token);
                    if (!tabScan.IsSuccess)
                    {
                        AddScanWarning(mergedUnmatched, primaryName, tabScan.Error?.Message ?? "二级分类识别失败");
                        break;
                    }
                    currentWindow = tabScan.Window ?? currentWindow;
                    var visibleTabs = FindVisibleSecondaryTabs(
                        tabScan.Lines,
                        currentWindow.ClientWidth,
                        currentWindow.ClientHeight,
                        knownSecondaryNames);
                    var visibleSignature = BuildNavigationSignature(visibleTabs);
                    var foundNew = false;
                    foreach (var tab in visibleTabs)
                    {
                        _ocrCancellation.Token.ThrowIfCancellationRequested();
                        var key = AchievementOcrMatcher.NormalizeName(tab.Name);
                        if (!visited.Add(key)) continue;
                        foundNew = true;
                        var secondaryX = tab.ClientX;
                        ReportOcrProgress(OcrScanMode.FullScan, OcrScanPhase.ScanningCategory,
                            $"正在扫描：{primaryName} / {tab.Name}（已发现 {visited.Count} 个）",
                            primaryName: primaryName, secondaryName: tab.Name, visitedCount: primaryIndex,
                            totalCount: primaryYPercentages.Length, matchedCount: mergedCandidates.Count,
                            unmatchedCount: mergedUnmatched.Count);
                        HintText.Text = $"正在扫描：{primaryName} / {tab.Name}（已发现 {visited.Count} 个）";
                        var clicked = await capture.ClickAsync(currentWindow, secondaryX, tab.ClientY, _ocrCancellation.Token);
                        NativeOcrDiagnostics.Write($"OCR full secondary={primaryName}/{tab.Name} clicked={clicked} client={secondaryX},{tab.ClientY} textCenter={tab.ClientX},{tab.ClientY}");
                        if (!clicked)
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

                    NativeOcrDiagnostics.Write($"OCR full secondary-round={secondaryRound + 1} visible={visibleTabs.Count} foundNew={foundNew} visited={visited.Count} signature={visibleSignature}");
                    if (visibleTabs.Count == 0 || !foundNew)
                    {
                        NativeOcrDiagnostics.Write($"OCR full secondary-stop reason=no-new-tabs round={secondaryRound + 1} visited={visited.Count}");
                        break;
                    }

                    var scrollX = (int)(currentWindow.ClientWidth * ((0.1005 + 0.3479) / 2));
                    var scrollY = (int)(currentWindow.ClientHeight * 0.78);
                    ReportOcrProgress(OcrScanMode.FullScan, OcrScanPhase.ScrollingCategory,
                        $"正在拖动二级分类列表：{primaryName}（已发现 {visited.Count} 个）",
                        primaryName: primaryName, visitedCount: primaryIndex, totalCount: primaryYPercentages.Length,
                        matchedCount: mergedCandidates.Count, unmatchedCount: mergedUnmatched.Count);
                    HintText.Text = $"正在拖动二级分类列表：{primaryName}（已发现 {visited.Count} 个）";
                    if (!await capture.DragScrollAtAsync(currentWindow, scrollX, scrollY, SecondaryNavigationDragPixels, _ocrCancellation.Token))
                    {
                        AddScanWarning(mergedUnmatched, primaryName, "二级分类列表滚动失败");
                        break;
                    }
                    await Task.Delay(800, _ocrCancellation.Token);
                }
            }

            WindowState = previousState;
            Activate();
            var mergedPreview = MergeOcrPreviews(mergedCandidates, mergedUnmatched, rows);
            NativeOcrDiagnostics.Write($"OCR full finished candidates={mergedPreview.Candidates.Count} unmatched={mergedPreview.Unmatched.Count}");
            if (mergedPreview.Candidates.Count == 0)
            {
                ShowOcrError("OCR 全量扫描没有匹配到成就。请确认游戏处于成就页面，并检查 log 目录中的 native-ocr-YYYY-MM-DD.log。", "OCR 扫描结果");
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
            _activeOcrMode = null;
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
            ReportOcrProgress(OcrScanMode.FullScan, OcrScanPhase.ScanningCategory,
                $"正在扫描：{primaryName} / {secondaryName} · 第 {page} 页 · 已识别 {mergedCandidates.Count} 条",
                primaryName: primaryName, secondaryName: secondaryName, page: page,
                matchedCount: mergedCandidates.Count, unmatchedCount: mergedUnmatched.Count);
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
            ReportOcrProgress(OcrScanMode.FullScan, OcrScanPhase.ScanningCategory,
                $"{primaryName} / {secondaryName} 第 {page} 页识别完成。",
                primaryName: primaryName, secondaryName: secondaryName, page: page,
                matchedCount: mergedCandidates.Count, unmatchedCount: mergedUnmatched.Count);
            if (scan.Window is null || page == 80) break;
            ReportOcrProgress(OcrScanMode.FullScan, OcrScanPhase.ScrollingCategory,
                $"正在拖动：{primaryName} / {secondaryName}…",
                primaryName: primaryName, secondaryName: secondaryName, page: page,
                matchedCount: mergedCandidates.Count, unmatchedCount: mergedUnmatched.Count);
            if (!await capture.DragScrollAsync(scan.Window, dragPixels: OcrListDragPixels, cancellationToken))
            {
                AddScanWarning(mergedUnmatched, $"{primaryName}/{secondaryName}", "成就列表滚动失败");
                return new OcrCategoryScanStats(pages, lines, false);
            }
            await Task.Delay(800, cancellationToken);
        }
        return new OcrCategoryScanStats(pages, lines, true);
    }

    private static string ReadPrimaryTabText(
        IReadOnlyList<OcrTextLine> lines,
        int width,
        int height)
    {
        var x1 = width * 0.053;
        var y1 = height * 0.047;
        var x2 = width * 0.114;
        var y2 = height * 0.083;
        return string.Concat(lines
            .Where(line => IsLineInRegion(line, x1, y1, x2, y2))
            .OrderBy(line => LineCenterY(line))
            .Select(line => line.Text.Trim()));
    }

    private static IReadOnlySet<string> FindKnownSecondaryNames(
        IReadOnlyList<AchievementRow> rows,
        string primaryName,
        CategoryCatalog? categories = null)
    {
        var firstNames = rows
            .Select(row => row.FirstCategory)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var canonicalPrimary = AchievementOcrMatcher.MatchKnownText(primaryName, firstNames, out _);
        if (canonicalPrimary is null) return new HashSet<string>(StringComparer.Ordinal);
        var normalizedPrimary = AchievementOcrMatcher.NormalizeName(canonicalPrimary);
        var names = rows
            .Where(row => string.Equals(
                AchievementOcrMatcher.NormalizeName(row.FirstCategory),
                normalizedPrimary,
                StringComparison.Ordinal))
            .Select(row => row.SecondCategory)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.Ordinal);
        if (categories?.SecondCategories.TryGetValue(canonicalPrimary, out var configured) == true)
        {
            names.UnionWith(configured.Keys);
        }
        return names;
    }

    private static IReadOnlyList<NavigationTab> FindVisibleSecondaryTabs(
        IReadOnlyList<OcrTextLine> lines,
        int width,
        int height,
        IReadOnlySet<string>? knownNames = null)
    {
        // The tab labels occupy the inner text column in the left panel. Keep
        // icons, completion percentages, the panel edge, and the lower HUD out
        // of the navigation OCR region.
        var x1 = width * 0.145;
        var y1 = height * 0.18;
        var x2 = width * 0.31;
        var y2 = height * 0.95;
        var matches = new Dictionary<string, NavigationTab>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            if (!IsLineInRegion(line, x1, y1, x2, y2)) continue;
            var centerX = line.Points.Average(point => point.X);
            var text = line.Text.Trim();
            if (IsCompletionPercentage(text)) continue;
            var canonicalText = text;
            if (knownNames is { Count: > 0 })
            {
                canonicalText = CanonicalizeSecondaryTabName(text, knownNames) ?? string.Empty;
                if (canonicalText.Length == 0) continue;
            }
            var normalized = AchievementOcrMatcher.NormalizeName(canonicalText);
            if (normalized.Length < 2) continue;
            NativeOcrDiagnostics.Write($"OCR secondary line raw={text} canonical={canonicalText} center={centerX:0},{LineCenterY(line):0}");
            var tab = new NavigationTab(canonicalText, (int)Math.Round(centerX), (int)Math.Round(LineCenterY(line)));
            if (!matches.TryGetValue(normalized, out var existing) || tab.ClientY < existing.ClientY)
            {
                matches[normalized] = tab;
            }
        }
        var result = matches.Values.OrderBy(tab => tab.ClientY).ToArray();
        NativeOcrDiagnostics.Write($"OCR secondary visible={result.Length} names=[{string.Join(",", result.Select(tab => tab.Name))}]");
        return result;
    }

    private static string? CanonicalizeSecondaryTabName(
        string text,
        IReadOnlySet<string> knownNames)
    {
        var normalizedText = AchievementOcrMatcher.NormalizeName(text);
        var exact = knownNames.FirstOrDefault(name =>
            string.Equals(AchievementOcrMatcher.NormalizeName(name), normalizedText, StringComparison.Ordinal));
        if (exact is not null) return exact;

        var matched = AchievementOcrMatcher.MatchKnownText(text, knownNames, out _);
        if (matched is null) return null;

        if (TryGetTrailingChineseOrdinal(normalizedText, out var rawOrdinal) &&
            TryGetTrailingChineseOrdinal(AchievementOcrMatcher.NormalizeName(matched), out var matchedOrdinal) &&
            rawOrdinal != matchedOrdinal)
        {
            // OCR can confuse the one-stroke 一 and two-stroke 二. Do not turn
            // an explicit ordinal into a different known tab: preserving the
            // raw label keeps its row coordinate aligned with the actual UI.
            NativeOcrDiagnostics.Write($"OCR secondary preserve-ordinal raw={text} fuzzy={matched}");
            return text;
        }

        return matched;
    }

    private static bool TryGetTrailingChineseOrdinal(string value, out char ordinal)
    {
        ordinal = value.Length == 0 ? '\0' : value[^1];
        return ordinal is '一' or '二' or '三' or '四' or '五' or '六' or '七' or '八' or '九' or '十';
    }

    private static string BuildNavigationSignature(IReadOnlyList<NavigationTab> tabs) =>
        string.Join("\u001f", tabs.Select(tab => AchievementOcrMatcher.NormalizeName(tab.Name)));

    private static bool IsCompletionPercentage(string text)
    {
        var value = text.Trim();
        return value.EndsWith('%') && value[..^1].Trim().All(char.IsDigit);
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

    private void ReportOcrProgress(
        OcrScanMode mode,
        OcrScanPhase phase,
        string message,
        string primaryName = "",
        string secondaryName = "",
        int page = 0,
        int visitedCount = 0,
        int? totalCount = null,
        int matchedCount = 0,
        int unmatchedCount = 0)
    {
        HintText.Text = message;
        NativeOcrDiagnostics.Write($"OCR progress mode={mode} phase={phase} page={page} primary={primaryName} secondary={secondaryName} matched={matchedCount} unmatched={unmatchedCount} message={message}");
    }

    private void AddScanWarning(IDictionary<string, OcrUnmatchedText> warnings, string scope, string reason)
    {
        if (!warnings.TryAdd($"warning:{scope}:{reason}", new OcrUnmatchedText($"[{scope}]", reason, 0))) return;
        HintText.Text = $"{scope}：{reason}";
        NativeOcrDiagnostics.Write($"OCR warning scope={scope} reason={reason}");
    }

    private sealed record NavigationTab(string Name, int ClientX, int ClientY);

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
            ? new Dictionary<string, string>
            {
                ["WindowBrush"] = "#F5F8F7", ["PanelBrush"] = "#FFFFFF", ["PanelAltBrush"] = "#E8F0EE",
                ["BorderBrush"] = "#C3D4D0", ["TextBrush"] = "#19302D", ["MutedTextBrush"] = "#5A7470",
                ["InputBrush"] = "#FFFFFF", ["RowBorderBrush"] = "#D7E2DF", ["SelectionBrush"] = "#B8E8DF",
                ["ErrorBrush"] = "#A52714", ["CompletedStatusBrush"] = "#087F6A", ["IncompleteStatusBrush"] = "#9A6B00",
                ["UnavailableStatusBrush"] = "#B13B2F", ["OccupiedStatusBrush"] = "#6D4BC0"
            }
            : new Dictionary<string, string>
            {
                ["WindowBrush"] = "#182124", ["PanelBrush"] = "#222E32", ["PanelAltBrush"] = "#29383D",
                ["BorderBrush"] = "#3A5055", ["TextBrush"] = "#E8F2F0", ["MutedTextBrush"] = "#9BB3AF",
                ["InputBrush"] = "#172225", ["RowBorderBrush"] = "#304247", ["SelectionBrush"] = "#285A5A",
                ["ErrorBrush"] = "#FF9A8D", ["CompletedStatusBrush"] = "#42D8C2", ["IncompleteStatusBrush"] = "#E8C56C",
                ["UnavailableStatusBrush"] = "#FF9A8D", ["OccupiedStatusBrush"] = "#B9A3FF"
            };
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
            roots.Add(Path.Combine(repositoryRoot, "ocr", "build", "Debug"));
            roots.Add(Path.Combine(repositoryRoot, "ocr", "build", "Release"));
        }

        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var modelRoots = new List<string>();
            if (!string.IsNullOrWhiteSpace(configuredModelRoot)) modelRoots.Add(Path.GetFullPath(configuredModelRoot));
            modelRoots.Add(Path.Combine(root, "models", "ppocrv5"));
            if (repositoryRoot is not null) modelRoots.Add(Path.Combine(repositoryRoot, "models", "ppocrv5"));

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
            if (Directory.Exists(Path.Combine(directory.FullName, "ocr")) &&
                Directory.Exists(Path.Combine(directory.FullName, "models", "ppocrv5")))
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
