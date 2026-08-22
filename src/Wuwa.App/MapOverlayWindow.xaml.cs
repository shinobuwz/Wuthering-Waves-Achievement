using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Wuwa.Infrastructure;

namespace Wuwa.App;

public partial class MapOverlayWindow : Window
{
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private static readonly IntPtr HwndTopmost = new(-1);

    // Do not persist a player's current map viewport or marker selection in the app.
    // The map site owns the default state, which differs between users and updates.
    private const string MapUrl = "https://www.kurobbs.com/mc/map/";

    private bool _browserInitialized;

    public MapOverlayWindow()
    {
        InitializeComponent();
        PreviewKeyDown += MapOverlayWindow_OnPreviewKeyDown;
    }

    /// <summary>Raised when the user requests hiding the overlay with Esc.</summary>
    public event EventHandler? HideRequested;

    public async Task InitializeBrowserAsync()
    {
        if (_browserInitialized)
        {
            return;
        }

        try
        {
            await MapBrowser.EnsureCoreWebView2Async();
            if (MapBrowser.CoreWebView2 is null)
            {
                throw new InvalidOperationException("WebView2 初始化后没有返回 CoreWebView2 实例。");
            }

            MapBrowser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            MapBrowser.CoreWebView2.Settings.AreDevToolsEnabled = false;
            MapBrowser.CoreWebView2.Settings.IsStatusBarEnabled = false;
            MapBrowser.CoreWebView2.Navigate(MapUrl);
            _browserInitialized = true;
        }
        catch (Exception exception)
        {
            throw new MapOverlayUnavailableException(
                "无法初始化地图浏览器。请安装或修复 Microsoft Edge WebView2 Runtime 后重试。\n\n下载地址：https://developer.microsoft.com/microsoft-edge/webview2/",
                exception);
        }
    }

    /// <summary>Moves the native overlay window to a client rectangle in physical screen pixels.</summary>
    public void ApplyClientBounds(GameWindowClientBounds bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            // This is only a pre-Show fallback. The native SetWindowPos path below
            // is used once the HWND exists and avoids WPF DIP rounding on high-DPI
            // or secondary monitors.
            Left = bounds.Left;
            Top = bounds.Top;
            Width = bounds.Width;
            Height = bounds.Height;
            return;
        }

        SetWindowPos(
            handle,
            HwndTopmost,
            bounds.Left,
            bounds.Top,
            bounds.Width,
            bounds.Height,
            SwpNoActivate | SwpShowWindow);
    }

    protected override void OnClosed(EventArgs e)
    {
        MapBrowser.Dispose();
        base.OnClosed(e);
    }

    private void MapOverlayWindow_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        e.Handled = true;
        HideRequested?.Invoke(this, EventArgs.Empty);
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

public sealed class MapOverlayUnavailableException : Exception
{
    public MapOverlayUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
