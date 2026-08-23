using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Wuwa.Core;

namespace Wuwa.App;

public partial class OcrStopOverlayWindow : Window
{
    private const uint SetWindowPosNoSize = 0x0001;
    private const uint SetWindowPosNoActivate = 0x0010;
    private const uint SetWindowPosShowWindow = 0x0040;
    private const int WindowMessageMouseActivate = 0x0021;
    private const int MouseActivateNoActivate = 3;
    private static readonly IntPtr HwndTopmost = new(-1);
    private readonly Action _requestStop;
    private bool _stopRequested;

    public OcrStopOverlayWindow(Action requestStop)
    {
        _requestStop = requestStop ?? throw new ArgumentNullException(nameof(requestStop));
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            EnsureTopmost();
            HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WindowHook);
        };
    }

    public void ResetStopState()
    {
        _stopRequested = false;
        StopButton.IsEnabled = true;
        StopButton.Content = "停止扫描";
    }

    public void PositionAt(GameWindowScreenBounds gameBounds)
    {
        ArgumentNullException.ThrowIfNull(gameBounds);
        if (!IsVisible)
        {
            return;
        }

        UpdateLayout();
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;

        var dpi = VisualTreeHelper.GetDpi(this);
        var overlayWidth = (int)Math.Ceiling(Math.Max(ActualWidth, Width) * dpi.DpiScaleX);
        var margin = (int)Math.Ceiling(16 * dpi.DpiScaleX);
        var x = gameBounds.X + gameBounds.Width - overlayWidth - margin;
        var y = gameBounds.Y + margin;
        NativeMethods.SetWindowPos(
            handle,
            HwndTopmost,
            x,
            y,
            0,
            0,
            SetWindowPosNoSize | SetWindowPosNoActivate | SetWindowPosShowWindow);
    }

    private void EnsureTopmost()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        NativeMethods.SetWindowPos(
            handle,
            HwndTopmost,
            0,
            0,
            0,
            0,
            SetWindowPosNoSize | SetWindowPosNoActivate | SetWindowPosShowWindow);
    }

    private void Stop_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (_stopRequested) return;
        _stopRequested = true;
        StopButton.IsEnabled = false;
        StopButton.Content = "正在停止…";
        _requestStop();
    }

    private static IntPtr WindowHook(
        IntPtr hwnd,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if (message != WindowMessageMouseActivate) return IntPtr.Zero;
        // Keep the game focused without discarding the mouse-down message. This lets
        // the overlay button react reliably even though the window is shown inactive.
        handled = true;
        return new IntPtr(MouseActivateNoActivate);
    }

    private static partial class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetWindowPos(
            IntPtr window,
            IntPtr insertAfter,
            int x,
            int y,
            int width,
            int height,
            uint flags);
    }
}
