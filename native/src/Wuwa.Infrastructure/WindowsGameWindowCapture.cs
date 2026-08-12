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
        var screenDc = NativeMethods.GetDC(window.Handle);
        if (screenDc == IntPtr.Zero) throw LastCaptureError("Unable to acquire the game client device context.");
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
            if (!NativeMethods.BitBlt(memoryDc, 0, 0, width, height, screenDc, 0, 0, Srccopy)) throw LastCaptureError("Unable to capture the game client pixels.");
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
            NativeMethods.ReleaseDC(window.Handle, screenDc);
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
        [LibraryImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static partial bool GetClientRect(IntPtr handle, out NativeRect rectangle);
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
