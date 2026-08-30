using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Interop;
using Wuwa.Core;

namespace Wuwa.App;

public partial class RotationOverlayWindow : Window
{
    private const int GwlExStyle = -20;
    private const long WsExTransparent = 0x20;
    private const long WsExToolWindow = 0x80;
    private const long WsExNoActivate = 0x08000000;
    private const int WmNcHitTest = 0x0084;
    private const int WmMouseActivate = 0x0021;
    private const int HtTransparent = -1;
    private const int MaNoActivate = 3;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private static readonly nint HwndTopmost = new(-1);
    private nint _handle;

    public RotationOverlayWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => InitializeNativeWindow();
    }

    public void ApplySnapshot(RotationRunSnapshot snapshot, RotationProfile profile, RotationBindingSet bindings)
    {
        AutomationProperties.SetName(this, string.Join(" | ", snapshot.Preview.Select(item => item.Description)));
        PreviewItems.ItemsSource = snapshot.Preview.Select(item =>
        {
            var character = profile.Team.FirstOrDefault(slot => slot.Slot == item.CharacterSlot)?.DisplayName ?? $"槽位 {item.CharacterSlot}";
            var binding = item.BindingAction is { } action && bindings.TryGet(action, out var input) ? FormatInput(input) : "未绑定";
            return new PreviewDisplay(item.IsStart ? "START" : item.Action?.ToString() ?? "—", item.Description, $"{character} · {binding}");
        }).ToArray();
    }

    public void PositionWithin(RotationWindowBounds bounds)
    {
        if (_handle == 0) _handle = new WindowInteropHelper(this).Handle;
        var width = Math.Min(720, Math.Max(420, bounds.Width - 40));
        const int height = 140;
        var left = bounds.Left + (bounds.Width - width) / 2;
        var top = bounds.Top + bounds.Height - height - 28;
        Native.SetWindowPos(_handle, HwndTopmost, left, top, width, height, SwpNoActivate | SwpShowWindow);
    }

    private void InitializeNativeWindow()
    {
        _handle = new WindowInteropHelper(this).Handle;
        var styles = Native.GetWindowLongPtr(_handle, GwlExStyle).ToInt64();
        Native.SetWindowLongPtr(_handle, GwlExStyle, new(styles | WsExTransparent | WsExToolWindow | WsExNoActivate));
        HwndSource.FromHwnd(_handle)?.AddHook(WindowHook);
    }

    private nint WindowHook(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == WmNcHitTest) { handled = true; return new(HtTransparent); }
        if (message == WmMouseActivate) { handled = true; return new(MaNoActivate); }
        return nint.Zero;
    }

    private static string FormatInput(RotationPhysicalInput input) => input.Device switch
    {
        RotationInputDevice.Mouse => input.Code switch
        {
            1 => "鼠标左键",
            2 => "鼠标右键",
            3 => "鼠标中键",
            4 => "鼠标 X1",
            5 => "鼠标 X2",
            _ => $"鼠标 {input.Code}"
        },
        _ => KeyInterop.KeyFromVirtualKey(input.Code).ToString()
    };

    private sealed record PreviewDisplay(string Badge, string Description, string Detail);

    private static class Native
    {
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] internal static extern nint GetWindowLongPtr(nint hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] internal static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int width, int height, uint flags);
    }
}
