using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Wuwa.Core;
using Wuwa.Infrastructure;

namespace Wuwa.App;

public partial class SceneMarkerOverlayWindow : Window
{
    private const uint SwpShowWindow = 0x0040;
    private static readonly IntPtr HwndTopmost = new(-1);

    private readonly GameWindowCandidate _gameWindow;
    private readonly GameWindowClientBounds _clientBounds;
    private readonly OcrImageFrame _sourceFrame;
    private readonly DateTimeOffset _capturedAtUtc;
    private readonly SceneMarkerStorage _storage;
    private Point _dragStart;
    private bool _isDragging;
    private bool _saveInProgress;
    private bool _allowClose;
    private SceneMarkerPixelRegion? _selectedRegion;
    private OcrImageFrame? _markerFrame;

    public SceneMarkerOverlayWindow(
        GameWindowCandidate gameWindow,
        GameWindowClientBounds clientBounds,
        OcrImageFrame sourceFrame,
        DateTimeOffset capturedAtUtc,
        SceneMarkerStorage? storage = null)
    {
        _gameWindow = gameWindow ?? throw new ArgumentNullException(nameof(gameWindow));
        _clientBounds = clientBounds ?? throw new ArgumentNullException(nameof(clientBounds));
        ArgumentNullException.ThrowIfNull(sourceFrame);
        sourceFrame.Validate();
        if (sourceFrame.Width != clientBounds.Width || sourceFrame.Height != clientBounds.Height)
        {
            throw new ArgumentException("The frozen frame dimensions must match the game client bounds.", nameof(sourceFrame));
        }

        var usedLength = checked(sourceFrame.Stride * sourceFrame.Height);
        _sourceFrame = new OcrImageFrame(
            sourceFrame.BgrPixels.AsSpan(0, usedLength).ToArray(),
            sourceFrame.Width,
            sourceFrame.Height,
            sourceFrame.Stride);
        _capturedAtUtc = capturedAtUtc.ToUniversalTime();
        _storage = storage ?? new SceneMarkerStorage();

        InitializeComponent();
        FrozenFrameImage.Source = CreateBitmapSource(_sourceFrame);
        SaveLocationText.Text = $"默认保存到：{SceneMarkerStorage.DefaultDirectory}";
        SourceInitialized += (_, _) => ApplyClientBounds();
        Closing += SceneMarkerOverlayWindow_OnClosing;
    }

    public SceneMarkerSaveResult? SavedResult { get; private set; }

    private void ApplyClientBounds()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        if (!SetWindowPos(
                handle,
                HwndTopmost,
                _clientBounds.Left,
                _clientBounds.Top,
                _clientBounds.Width,
                _clientBounds.Height,
                SwpShowWindow))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法将场景标记 Overlay 对齐到游戏客户区。");
        }
    }

    private void SelectionCanvas_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_saveInProgress) return;
        _dragStart = ClampToCanvas(e.GetPosition(SelectionCanvas));
        _isDragging = true;
        _selectedRegion = null;
        _markerFrame = null;
        EditorPanel.Visibility = Visibility.Collapsed;
        EditorErrorText.Text = string.Empty;
        OverlayStatusText.Text = "拖拽到标记右下角后松开鼠标";
        SelectionRectangle.Visibility = Visibility.Visible;
        UpdateSelectionRectangle(_dragStart, _dragStart);
        SelectionCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void SelectionCanvas_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging) return;
        UpdateSelectionRectangle(_dragStart, ClampToCanvas(e.GetPosition(SelectionCanvas)));
    }

    private void SelectionCanvas_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;
        SelectionCanvas.ReleaseMouseCapture();
        var dragEnd = ClampToCanvas(e.GetPosition(SelectionCanvas));
        UpdateSelectionRectangle(_dragStart, dragEnd);

        SceneMarkerPixelRegion region;
        try
        {
            region = SceneMarkerFrameTools.MapDisplaySelection(
                _dragStart.X,
                _dragStart.Y,
                dragEnd.X,
                dragEnd.Y,
                SelectionCanvas.ActualWidth,
                SelectionCanvas.ActualHeight,
                _sourceFrame.Width,
                _sourceFrame.Height);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            RejectSelection(exception.Message);
            return;
        }

        if (!SceneMarkerFrameTools.IsLargeEnough(region))
        {
            RejectSelection($"选区至少需要 {SceneMarkerFrameTools.MinimumMarkerSize}×{SceneMarkerFrameTools.MinimumMarkerSize} 个源像素，请重新框选。");
            return;
        }

        _selectedRegion = region;
        _markerFrame = SceneMarkerFrameTools.Crop(_sourceFrame, region);
        MarkerPreviewImage.Source = CreateBitmapSource(_markerFrame);
        var normalized = SceneMarkerFrameTools.Normalize(region, _sourceFrame.Width, _sourceFrame.Height);
        SelectionInfoText.Text = string.Format(
            CultureInfo.InvariantCulture,
            "源画面：{0}×{1}  stride={2}\n像素 ROI：x={3}, y={4}, w={5}, h={6}\n归一化 ROI：left={7:F6}, top={8:F6}, width={9:F6}, height={10:F6}",
            _sourceFrame.Width,
            _sourceFrame.Height,
            _sourceFrame.Stride,
            region.X,
            region.Y,
            region.Width,
            region.Height,
            normalized.Left,
            normalized.Top,
            normalized.Width,
            normalized.Height);
        OverlayStatusText.Text = "已冻结选区；填写标识后保存，或重新拖拽调整";
        EditorErrorText.Text = string.Empty;
        EditorPanel.Visibility = Visibility.Visible;
        if (string.IsNullOrWhiteSpace(SceneIdBox.Text))
        {
            SceneIdBox.Focus();
        }
        else
        {
            MarkerNameBox.Focus();
        }
        e.Handled = true;
    }

    private void RejectSelection(string message)
    {
        _selectedRegion = null;
        _markerFrame = null;
        SelectionRectangle.Visibility = Visibility.Collapsed;
        EditorPanel.Visibility = Visibility.Collapsed;
        OverlayStatusText.Text = message;
    }

    private void Redraw_OnClick(object sender, RoutedEventArgs e)
    {
        if (_saveInProgress) return;
        _selectedRegion = null;
        _markerFrame = null;
        MarkerPreviewImage.Source = null;
        SelectionRectangle.Visibility = Visibility.Collapsed;
        EditorPanel.Visibility = Visibility.Collapsed;
        OverlayStatusText.Text = "在冻结游戏画面上重新拖拽框选 · Esc 取消";
        SelectionCanvas.Focus();
    }

    private async void Save_OnClick(object sender, RoutedEventArgs e)
    {
        if (_saveInProgress || _selectedRegion is null || _markerFrame is null) return;
        if (!SceneMarkerIdentifier.TryValidate(SceneIdBox.Text, out var sceneId, out var sceneError))
        {
            ShowEditorError($"Scene ID：{sceneError}", SceneIdBox);
            return;
        }
        if (!SceneMarkerIdentifier.TryValidate(MarkerNameBox.Text, out var markerName, out var markerError))
        {
            ShowEditorError($"Marker 名称：{markerError}", MarkerNameBox);
            return;
        }

        var outputDirectory = SceneMarkerStorage.DefaultDirectory;
        if (!SceneMarkerStorage.TryPrepareSceneDirectory(outputDirectory, sceneId, out _, out var defaultDirectoryError))
        {
            var continueSelection = MessageBox.Show(
                this,
                $"Exe 旁的默认场景目录不可写：\n{Path.Combine(outputDirectory, sceneId)}\n\n原因：{defaultDirectoryError}\n\n请选择另一个保存目录。",
                "场景标记目录不可写",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);
            if (continueSelection != MessageBoxResult.OK) return;

            var picker = new OpenFolderDialog
            {
                Title = "选择场景标记保存目录",
                InitialDirectory = AppPaths.ApplicationDirectory,
                Multiselect = false
            };
            if (picker.ShowDialog(this) != true) return;
            outputDirectory = picker.FolderName;
            if (!SceneMarkerStorage.TryPrepareSceneDirectory(outputDirectory, sceneId, out _, out var selectedDirectoryError))
            {
                EditorErrorText.Text = $"选择的场景目录仍不可写：{selectedDirectoryError}";
                return;
            }
        }

        _saveInProgress = true;
        SaveButton.IsEnabled = false;
        SaveButton.Content = "正在保存…";
        EditorErrorText.Text = string.Empty;
        try
        {
            var png = EncodePng(_markerFrame);
            SavedResult = await _storage.SaveAsync(
                outputDirectory,
                new SceneMarkerSaveRequest(
                    sceneId,
                    markerName,
                    _capturedAtUtc,
                    _gameWindow,
                    _clientBounds,
                    _sourceFrame,
                    _selectedRegion,
                    png));
            NativeOcrDiagnostics.Write(
                $"Scene marker saved scene={sceneId} marker={markerName} roi={_selectedRegion.X},{_selectedRegion.Y},{_selectedRegion.Width}x{_selectedRegion.Height} image={SavedResult.ImagePath}");
            _allowClose = true;
            DialogResult = true;
        }
        catch (Exception exception)
        {
            NativeOcrDiagnostics.Write($"Scene marker save failed: {exception}");
            EditorErrorText.Text = $"保存失败：{exception.Message}";
            SaveButton.IsEnabled = true;
            SaveButton.Content = "保存 PNG + JSON";
            _saveInProgress = false;
        }
    }

    private void ShowEditorError(string message, TextBox focusTarget)
    {
        EditorErrorText.Text = message;
        focusTarget.Focus();
        focusTarget.SelectAll();
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e)
    {
        if (_saveInProgress) return;
        DialogResult = false;
    }

    private void SceneMarkerOverlayWindow_OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_saveInProgress || _allowClose) return;
        e.Cancel = true;
        EditorErrorText.Text = "标记正在保存，请等待写入完成后再关闭。";
    }

    private void Window_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || _saveInProgress) return;
        e.Handled = true;
        DialogResult = false;
    }

    private Point ClampToCanvas(Point point) =>
        new(
            Math.Clamp(point.X, 0, Math.Max(0, SelectionCanvas.ActualWidth)),
            Math.Clamp(point.Y, 0, Math.Max(0, SelectionCanvas.ActualHeight)));

    private void UpdateSelectionRectangle(Point start, Point end)
    {
        var left = Math.Min(start.X, end.X);
        var top = Math.Min(start.Y, end.Y);
        SelectionRectangle.Width = Math.Abs(end.X - start.X);
        SelectionRectangle.Height = Math.Abs(end.Y - start.Y);
        Canvas.SetLeft(SelectionRectangle, left);
        Canvas.SetTop(SelectionRectangle, top);
    }

    private static BitmapSource CreateBitmapSource(OcrImageFrame frame)
    {
        frame.Validate();
        var bitmap = BitmapSource.Create(
            frame.Width,
            frame.Height,
            96,
            96,
            PixelFormats.Bgr24,
            null,
            frame.BgrPixels,
            frame.Stride);
        bitmap.Freeze();
        return bitmap;
    }

    private static byte[] EncodePng(OcrImageFrame frame)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(CreateBitmapSource(frame)));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);
}
