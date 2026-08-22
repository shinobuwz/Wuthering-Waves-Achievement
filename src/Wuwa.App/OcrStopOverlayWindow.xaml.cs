using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Wuwa.Core;

namespace Wuwa.App;

public partial class OcrStopOverlayWindow : Window
{
    private const uint SetWindowPosNoSize = 0x0001;
    private const uint SetWindowPosNoActivate = 0x0010;
    private const uint SetWindowPosShowWindow = 0x0040;
    private static readonly IntPtr HwndTopmost = new(-1);
    private readonly Action _forceStop;

    public OcrStopOverlayWindow(Action forceStop)
    {
        _forceStop = forceStop ?? throw new ArgumentNullException(nameof(forceStop));
        InitializeComponent();
        SourceInitialized += (_, _) => EnsureTopmost();
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

    private void Stop_OnClick(object sender, RoutedEventArgs e) => _forceStop();

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
