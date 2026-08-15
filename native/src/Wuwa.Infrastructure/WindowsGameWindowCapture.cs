using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Wuwa.Core;

namespace Wuwa.Infrastructure;

/// <summary>Finds visible game windows by process name and captures their client area as packed top-down BGR.</summary>
public sealed partial class WindowsGameWindowCapture : IGameWindowCapture
{
    private const int Srccopy = 0x00CC0020;
    private const int DibRgbColors = 0;

    public Task<GameWindowCandidate> FindGameWindowAsync(
        IReadOnlyCollection<string> processNames,
        int minimumWidth = 800,
        int minimumHeight = 600,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(processNames);
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Game-window capture currently supports Windows only.");
        if (minimumWidth <= 0 || minimumHeight <= 0) throw new ArgumentOutOfRangeException(nameof(minimumWidth));
        var normalized = processNames.Select(NormalizeProcessName).Where(name => name.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (normalized.Count == 0) throw new ArgumentException("At least one game process name is required.", nameof(processNames));
        return Task.Run(() => FindGameWindow(normalized, minimumWidth, minimumHeight, cancellationToken), cancellationToken);
    }

    public Task<OcrImageFrame> CaptureClientAsync(
        GameWindowCandidate window,
        int? expectedWidth = null,
        int? expectedHeight = null,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Game-window capture currently supports Windows only.");
        if (window.Handle == 0) throw new ArgumentException("A valid window handle is required.", nameof(window));
        return Task.Run(() => CaptureClient(window, expectedWidth, expectedHeight, cancellationToken), cancellationToken);
    }

    public Task<bool> ScrollAsync(
        GameWindowCandidate window,
        int wheelNotches = -8,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Game-window input currently supports Windows only.");
        if (window.Handle == 0) throw new ArgumentException("A valid window handle is required.", nameof(window));
        if (wheelNotches == 0) throw new ArgumentOutOfRangeException(nameof(wheelNotches));
        return Task.Run(() => ScrollWindow(window, wheelNotches, cancellationToken), cancellationToken);
    }

    private static GameWindowCandidate FindGameWindow(
        IReadOnlySet<string> processNames,
        int minimumWidth,
        int minimumHeight,
        CancellationToken cancellationToken)
    {
        var processMap = new Dictionary<int, string>();
        foreach (var requestedName in processNames)
        {
            foreach (var process in Process.GetProcessesByName(requestedName))
            {
                using (process)
                {
                    try
                    {
                        processMap.TryAdd(process.Id, process.ProcessName);
                    }
                    catch (InvalidOperationException)
                    {
                        // Process exited while discovery was running.
                    }
                    catch (Win32Exception)
                    {
                        // The process is not accessible to the current user.
                    }
                }
            }
        }
        if (processMap.Count == 0) throw new GameWindowNotFoundException($"No running game process was found for: {string.Join(", ", processNames)}.");

        var candidates = new List<GameWindowCandidate>();
        NativeMethods.EnumWindows((handle, _) =>
        {
            if (cancellationToken.IsCancellationRequested) return false;
            if (!NativeMethods.IsWindowVisible(handle) || NativeMethods.IsIconic(handle)) return true;
            NativeMethods.GetWindowThreadProcessId(handle, out var processId);
            if (processId == 0 || !processMap.TryGetValue(checked((int)processId), out var processName)) return true;
            if (!NativeMethods.GetClientRect(handle, out var rectangle)) return true;
            var width = rectangle.Right - rectangle.Left;
            var height = rectangle.Bottom - rectangle.Top;
            if (width < minimumWidth || height < minimumHeight) return true;
            candidates.Add(new GameWindowCandidate(handle, checked((int)processId), processName, GetTitle(handle), width, height));
            return true;
        }, IntPtr.Zero);
        cancellationToken.ThrowIfCancellationRequested();
        return candidates.OrderByDescending(candidate => (long)candidate.ClientWidth * candidate.ClientHeight).FirstOrDefault()
            ?? throw new GameWindowNotFoundException($"No visible game window was found for: {string.Join(", ", processNames)}.");
    }

    private static bool ScrollWindow(
        GameWindowCandidate window,
        int wheelNotches,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!NativeMethods.IsWindow(window.Handle)) return false;

        // Prefer SendInput: it moves the real cursor and emits real wheel input,
        // without requiring administrator privileges when both processes have the
        // same integrity level. If Windows blocks it, use the older fallbacks.
        NativeMethods.ShowWindow(window.Handle, NativeMethods.ShowWindowRestore);
        NativeMethods.BringWindowToTop(window.Handle);
        NativeMethods.SetForegroundWindow(window.Handle);
        var hasClient = NativeMethods.GetClientRect(window.Handle, out var client);
        var clientCenter = new NativePoint
        {
            X = hasClient ? (client.Right - client.Left) / 2 : window.ClientWidth / 2,
            Y = hasClient ? (client.Bottom - client.Top) / 2 : window.ClientHeight / 2
        };
        var screenCenter = clientCenter;
        var canUseScreenInput = hasClient && NativeMethods.ClientToScreen(window.Handle, ref screenCenter);
        if (canUseScreenInput && TrySendInputScroll(screenCenter, wheelNotches, cancellationToken)) return true;

        var canUseMouseInput = canUseScreenInput && NativeMethods.SetCursorPos(screenCenter.X, screenCenter.Y);
        var direction = Math.Sign(wheelNotches);
        var fallbackSucceeded = true;
        for (var index = 0; index < Math.Abs(wheelNotches); index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (canUseMouseInput)
            {
                NativeMethods.MouseEvent(NativeMethods.MouseEventWheel, 0, 0, direction * NativeMethods.WheelDelta, UIntPtr.Zero);
            }
            else
            {
                var wheelData = unchecked((UIntPtr)((uint)(direction * NativeMethods.WheelDelta) << 16));
                var point = new IntPtr((clientCenter.Y << 16) | (clientCenter.X & 0xffff));
                fallbackSucceeded &= NativeMethods.PostMessage(window.Handle, NativeMethods.MouseWheelMessage, wheelData, point);
            }
            Thread.Sleep(12);
        }

        return canUseMouseInput || fallbackSucceeded;
    }

    private static bool TrySendInputScroll(NativePoint screenCenter, int wheelNotches, CancellationToken cancellationToken)
    {
        var virtualLeft = NativeMethods.GetSystemMetrics(NativeMethods.VirtualScreenLeft);
        var virtualTop = NativeMethods.GetSystemMetrics(NativeMethods.VirtualScreenTop);
        var virtualWidth = NativeMethods.GetSystemMetrics(NativeMethods.VirtualScreenWidth);
        var virtualHeight = NativeMethods.GetSystemMetrics(NativeMethods.VirtualScreenHeight);
        if (virtualWidth <= 1 || virtualHeight <= 1) return false;

        var absoluteX = Math.Clamp((screenCenter.X - virtualLeft) * 65535 / (virtualWidth - 1), 0, 65535);
        var absoluteY = Math.Clamp((screenCenter.Y - virtualTop) * 65535 / (virtualHeight - 1), 0, 65535);
        var direction = Math.Sign(wheelNotches);
        var inputs = new NativeInput[Math.Abs(wheelNotches) + 1];
        inputs[0] = new NativeInput
        {
            Type = NativeMethods.InputMouse,
            Mouse = new NativeMouseInput
            {
                Dx = absoluteX,
                Dy = absoluteY,
                Flags = NativeMethods.MouseEventMove | NativeMethods.MouseEventAbsolute | NativeMethods.MouseEventVirtualDesk
            }
        };
        for (var index = 1; index < inputs.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            inputs[index] = new NativeInput
            {
                Type = NativeMethods.InputMouse,
                Mouse = new NativeMouseInput
                {
                    MouseData = unchecked((uint)(direction * NativeMethods.WheelDelta)),
                    Flags = NativeMethods.MouseEventWheel
                }
            };
        }

        return NativeMethods.SendInput(
            (uint)inputs.Length,
            inputs,
            Marshal.SizeOf<NativeInput>()) == (uint)inputs.Length;
    }

    private static OcrImageFrame CaptureClient(
        GameWindowCandidate window,
        int? expectedWidth,
        int? expectedHeight,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!NativeMethods.IsWindow(window.Handle)) throw new GameWindowCaptureException("The selected game window is no longer valid.");
        if (!NativeMethods.GetClientRect(window.Handle, out var client)) throw LastCaptureError("Unable to read the game client rectangle.");
        var width = client.Right - client.Left;
        var height = client.Bottom - client.Top;
        if (width <= 0 || height <= 0) throw new GameWindowCaptureException("The game client area is empty or minimized.");
        if ((expectedWidth is not null && width != expectedWidth) || (expectedHeight is not null && height != expectedHeight))
        {
            throw new GameWindowCaptureException($"Game client resolution is {width}x{height}; expected {expectedWidth?.ToString() ?? "any"}x{expectedHeight?.ToString() ?? "any"}.");
        }
        var screenOrigin = new NativePoint();
        if (!NativeMethods.ClientToScreen(window.Handle, ref screenOrigin)) throw LastCaptureError("Unable to locate the game client on screen.");
        NativeMethods.SetForegroundWindow(window.Handle);
        var screenDc = NativeMethods.GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero) throw LastCaptureError("Unable to acquire the desktop device context.");
        var memoryDc = IntPtr.Zero;
        var bitmap = IntPtr.Zero;
        var previous = IntPtr.Zero;
        try
        {
            memoryDc = NativeMethods.CreateCompatibleDC(screenDc);
            if (memoryDc == IntPtr.Zero) throw LastCaptureError("Unable to create the capture device context.");
            bitmap = NativeMethods.CreateCompatibleBitmap(screenDc, width, height);
            if (bitmap == IntPtr.Zero) throw LastCaptureError("Unable to allocate the capture bitmap.");
            previous = NativeMethods.SelectObject(memoryDc, bitmap);
            if (previous == IntPtr.Zero || previous == new IntPtr(-1)) throw LastCaptureError("Unable to select the capture bitmap.");
            if (!NativeMethods.BitBlt(memoryDc, 0, 0, width, height, screenDc, screenOrigin.X, screenOrigin.Y, Srccopy)) throw LastCaptureError("Unable to capture the game client pixels.");
            cancellationToken.ThrowIfCancellationRequested();

            var stride = checked(((width * 3 + 3) / 4) * 4);
            var pixels = new byte[checked(stride * height)];
            var bitmapInfo = new BitmapInfo
            {
                Header = new BitmapInfoHeader
                {
                    Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                    Width = width,
                    Height = -height,
                    Planes = 1,
                    BitCount = 24,
                    Compression = 0,
                    SizeImage = (uint)pixels.Length
                }
            };
            if (NativeMethods.GetDIBits(memoryDc, bitmap, 0, (uint)height, pixels, ref bitmapInfo, DibRgbColors) != height)
            {
                throw LastCaptureError("Unable to copy the captured game pixels.");
            }
            return new OcrImageFrame(pixels, width, height, stride);
        }
        finally
        {
            if (previous != IntPtr.Zero && memoryDc != IntPtr.Zero) NativeMethods.SelectObject(memoryDc, previous);
            if (bitmap != IntPtr.Zero) NativeMethods.DeleteObject(bitmap);
            if (memoryDc != IntPtr.Zero) NativeMethods.DeleteDC(memoryDc);
            NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private static string NormalizeProcessName(string value)
    {
        var trimmed = value.Trim();
        return trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? trimmed[..^4] : trimmed;
    }

    private static string GetTitle(IntPtr handle)
    {
        var length = NativeMethods.GetWindowTextLength(handle);
        if (length <= 0) return string.Empty;
        var title = new StringBuilder(length + 1);
        NativeMethods.GetWindowText(handle, title, title.Capacity);
        return title.ToString();
    }

    private static GameWindowCaptureException LastCaptureError(string message)
    {
        var error = Marshal.GetLastWin32Error();
        return error == 0 ? new GameWindowCaptureException(message) : new GameWindowCaptureException(message, new Win32Exception(error));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeInput
    {
        public uint Type;
        public uint Padding;
        public NativeMouseInput Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMouseInput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ColorsUsed;
        public uint ColorsImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public BitmapInfoHeader Header;
        public uint Colors;
    }

    private delegate bool EnumWindowsCallback(IntPtr handle, IntPtr parameter);

    private static partial class NativeMethods
    {
        [LibraryImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static partial bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);
        [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static partial bool IsWindowVisible(IntPtr handle);
        [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static partial bool IsIconic(IntPtr handle);
        [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static partial bool IsWindow(IntPtr handle);
        [LibraryImport("user32.dll", SetLastError = true)] internal static partial uint GetWindowThreadProcessId(IntPtr handle, out uint processId);
        internal const uint InputMouse = 0;
        internal const uint MouseEventMove = 0x0001;
        internal const uint MouseEventAbsolute = 0x8000;
        internal const uint MouseEventVirtualDesk = 0x4000;
        internal const uint MouseEventWheel = 0x0800;
        internal const uint MouseWheelMessage = 0x020A;
        internal const int WheelDelta = 120;
        internal const int ShowWindowRestore = 9;
        internal const int VirtualScreenLeft = 76;
        internal const int VirtualScreenTop = 77;
        internal const int VirtualScreenWidth = 78;
        internal const int VirtualScreenHeight = 79;

        [LibraryImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static partial bool GetClientRect(IntPtr handle, out NativeRect rectangle);
        [LibraryImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static partial bool ClientToScreen(IntPtr handle, ref NativePoint point);
        [LibraryImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static partial bool SetForegroundWindow(IntPtr handle);
        [LibraryImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static partial bool BringWindowToTop(IntPtr handle);
        [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static partial bool ShowWindow(IntPtr handle, int command);
        [LibraryImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static partial bool SetCursorPos(int x, int y);
        [LibraryImport("user32.dll")] internal static partial int GetSystemMetrics(int index);
        [DllImport("user32.dll", SetLastError = true)] internal static extern void MouseEvent(uint flags, uint dx, uint dy, int data, UIntPtr extraInfo);
        [DllImport("user32.dll", SetLastError = true)] internal static extern uint SendInput(uint count, [In] NativeInput[] inputs, int size);
        [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool PostMessage(IntPtr handle, uint message, UIntPtr wParam, IntPtr lParam);
        [LibraryImport("user32.dll", EntryPoint = "GetWindowTextLengthW")] internal static partial int GetWindowTextLength(IntPtr handle);
        [DllImport("user32.dll", EntryPoint = "GetWindowTextW", CharSet = CharSet.Unicode)] internal static extern int GetWindowText(IntPtr handle, StringBuilder text, int maximumCount);
        [LibraryImport("user32.dll", SetLastError = true)] internal static partial IntPtr GetDC(IntPtr handle);
        [LibraryImport("user32.dll")] internal static partial int ReleaseDC(IntPtr handle, IntPtr deviceContext);
        [LibraryImport("gdi32.dll", SetLastError = true)] internal static partial IntPtr CreateCompatibleDC(IntPtr deviceContext);
        [LibraryImport("gdi32.dll", SetLastError = true)] internal static partial IntPtr CreateCompatibleBitmap(IntPtr deviceContext, int width, int height);
        [LibraryImport("gdi32.dll", SetLastError = true)] internal static partial IntPtr SelectObject(IntPtr deviceContext, IntPtr value);
        [LibraryImport("gdi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static partial bool BitBlt(IntPtr destination, int x, int y, int width, int height, IntPtr source, int sourceX, int sourceY, int operation);
        [LibraryImport("gdi32.dll", SetLastError = true)] internal static partial int GetDIBits(IntPtr deviceContext, IntPtr bitmap, uint start, uint lines, byte[] pixels, ref BitmapInfo info, uint usage);
        [LibraryImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static partial bool DeleteObject(IntPtr value);
        [LibraryImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static partial bool DeleteDC(IntPtr deviceContext);
    }
}
