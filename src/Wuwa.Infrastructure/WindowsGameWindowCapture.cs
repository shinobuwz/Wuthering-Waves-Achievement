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
    // The achievement list occupies the middle/right part of the 1920×1080 game client.
    // Use client coordinates so window placement and multi-monitor desktop centers do not change the target.
    private const double AchievementListScrollXRatio = 0.62;

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
        NativeOcrDiagnostics.Write($"FindGameWindow requested=[{string.Join(",", normalized)}] minimum={minimumWidth}x{minimumHeight}");
        return Task.Run(() => FindGameWindow(normalized, minimumWidth, minimumHeight, cancellationToken), cancellationToken);
    }

    public Task<GameWindowCandidate?> TryFindGameWindowAsync(
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
        NativeOcrDiagnostics.Write($"TryFindGameWindow requested=[{string.Join(",", normalized)}] minimum={minimumWidth}x{minimumHeight}");
        return Task.Run(() => TryFindGameWindow(normalized, minimumWidth, minimumHeight, cancellationToken), cancellationToken);
    }

    /// <summary>Reads the current visible client rectangle in physical screen pixels.</summary>
    public bool TryGetClientBounds(GameWindowCandidate window, out GameWindowClientBounds bounds)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Game-window capture currently supports Windows only.");
        if (window.Handle == 0) throw new ArgumentException("A valid window handle is required.", nameof(window));

        bounds = default!;
        if (!NativeMethods.IsWindow(window.Handle) ||
            !NativeMethods.IsWindowVisible(window.Handle) ||
            NativeMethods.IsIconic(window.Handle) ||
            !NativeMethods.GetClientRect(window.Handle, out var client))
        {
            return false;
        }

        var width = client.Right - client.Left;
        var height = client.Bottom - client.Top;
        if (width <= 0 || height <= 0)
        {
            return false;
        }

        var origin = new NativePoint { X = 0, Y = 0 };
        if (!NativeMethods.ClientToScreen(window.Handle, ref origin))
        {
            return false;
        }

        bounds = new GameWindowClientBounds(window.Handle, origin.X, origin.Y, width, height);
        return true;
    }

    /// <summary>Returns whether the supplied top-level window is the current foreground window.</summary>
    public bool IsForegroundWindow(nint handle)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Game-window capture currently supports Windows only.");
        return handle != 0 && NativeMethods.GetForegroundWindow() == handle;
    }

    /// <summary>Restores and activates a game window after the interactive overlay is hidden.</summary>
    public bool TryActivateWindow(GameWindowCandidate window)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Game-window capture currently supports Windows only.");
        if (window.Handle == 0 || !NativeMethods.IsWindow(window.Handle)) return false;
        NativeMethods.ShowWindow(window.Handle, NativeMethods.ShowWindowRestore);
        return NativeMethods.SetForegroundWindow(window.Handle);
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

    public Task<bool> DragScrollAsync(
        GameWindowCandidate window,
        int dragPixels = -600,
        CancellationToken cancellationToken = default)
    {
        ValidateDragArguments(window, dragPixels);
        var clientX = (int)Math.Round(window.ClientWidth * AchievementListScrollXRatio);
        var clientY = (int)Math.Round(window.ClientHeight * 0.78);
        NativeOcrDiagnostics.Write($"DragScroll requested handle=0x{window.Handle.ToInt64():X} pid={window.ProcessId} achievement-list client={clientX},{clientY} deltaY={dragPixels}");
        return Task.Run(() => DragWindow(window, clientX, clientY, dragPixels, cancellationToken), cancellationToken);
    }

    public Task<bool> DragScrollAtAsync(
        GameWindowCandidate window,
        int clientX,
        int clientY,
        int dragPixels = -600,
        CancellationToken cancellationToken = default)
    {
        ValidateDragArguments(window, dragPixels);
        NativeOcrDiagnostics.Write($"DragScroll requested handle=0x{window.Handle.ToInt64():X} pid={window.ProcessId} client={clientX},{clientY} deltaY={dragPixels}");
        return Task.Run(() => DragWindow(window, clientX, clientY, dragPixels, cancellationToken), cancellationToken);
    }

    public Task<bool> ClickAsync(
        GameWindowCandidate window,
        int clientX,
        int clientY,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Game-window input currently supports Windows only.");
        if (window.Handle == 0) throw new ArgumentException("A valid window handle is required.", nameof(window));
        NativeOcrDiagnostics.Write($"Click requested handle=0x{window.Handle.ToInt64():X} pid={window.ProcessId} client={clientX},{clientY}");
        return Task.Run(() => ClickWindow(window, clientX, clientY, cancellationToken), cancellationToken);
    }

    private static void ValidateDragArguments(GameWindowCandidate window, int dragPixels)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Game-window input currently supports Windows only.");
        if (window.Handle == 0) throw new ArgumentException("A valid window handle is required.", nameof(window));
        if (dragPixels == 0) throw new ArgumentOutOfRangeException(nameof(dragPixels));
    }

    private static GameWindowCandidate FindGameWindow(
        IReadOnlySet<string> processNames,
        int minimumWidth,
        int minimumHeight,
        CancellationToken cancellationToken) =>
        TryFindGameWindow(processNames, minimumWidth, minimumHeight, cancellationToken)
            ?? throw new GameWindowNotFoundException($"No visible game window was found for: {string.Join(", ", processNames)}.");

    private static GameWindowCandidate? TryFindGameWindow(
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
        if (processMap.Count == 0)
        {
            NativeOcrDiagnostics.Write($"FindGameWindow no matching process for=[{string.Join(",", processNames)}]");
            return null;
        }
        NativeOcrDiagnostics.Write($"FindGameWindow processIds=[{string.Join(",", processMap.Keys)}]");

        var candidates = new List<GameWindowCandidate>();
        NativeMethods.EnumWindows((handle, _) =>
        {
            if (cancellationToken.IsCancellationRequested) return false;
            NativeMethods.GetWindowThreadProcessId(handle, out var processId);
            if (processId == 0 || !processMap.TryGetValue(checked((int)processId), out var processName)) return true;
            var visible = NativeMethods.IsWindowVisible(handle);
            var iconic = NativeMethods.IsIconic(handle);
            var title = GetTitle(handle);
            if (!NativeMethods.GetClientRect(handle, out var rectangle))
            {
                NativeOcrDiagnostics.Write($"FindGameWindow candidate handle=0x{handle.ToInt64():X} pid={processId} visible={visible} iconic={iconic} title={title} rect=unavailable");
                return true;
            }
            var width = rectangle.Right - rectangle.Left;
            var height = rectangle.Bottom - rectangle.Top;
            NativeOcrDiagnostics.Write($"FindGameWindow candidate handle=0x{handle.ToInt64():X} pid={processId} visible={visible} iconic={iconic} title={title} client={width}x{height}");
            if (!visible || iconic || width < minimumWidth || height < minimumHeight) return true;
            candidates.Add(new GameWindowCandidate(handle, checked((int)processId), processName, title, width, height));
            return true;
        }, IntPtr.Zero);
        cancellationToken.ThrowIfCancellationRequested();
        var selected = candidates.OrderByDescending(candidate => (long)candidate.ClientWidth * candidate.ClientHeight).FirstOrDefault();
        if (selected is null)
        {
            NativeOcrDiagnostics.Write("FindGameWindow no visible candidate met the size requirement");
            return null;
        }
        NativeOcrDiagnostics.Write($"FindGameWindow selected handle=0x{selected.Handle.ToInt64():X} pid={selected.ProcessId} name={selected.ProcessName} title={selected.Title} client={selected.ClientWidth}x{selected.ClientHeight}");
        return selected;
    }

    private static bool ClickWindow(
        GameWindowCandidate window,
        int clientX,
        int clientY,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!NativeMethods.IsWindow(window.Handle))
        {
            NativeOcrDiagnostics.Write("Click aborted: target window is no longer valid");
            return false;
        }

        NativeMethods.ShowWindow(window.Handle, NativeMethods.ShowWindowRestore);
        var foreground = NativeMethods.SetForegroundWindow(window.Handle);
        Thread.Sleep(300);
        var point = new NativePoint
        {
            X = Math.Clamp(clientX, 0, Math.Max(0, window.ClientWidth - 1)),
            Y = Math.Clamp(clientY, 0, Math.Max(0, window.ClientHeight - 1))
        };
        if (!NativeMethods.ClientToScreen(window.Handle, ref point))
        {
            NativeOcrDiagnostics.Write("Click failed: ClientToScreen returned false");
            return false;
        }

        var positioned = NativeMethods.SetCursorPos(point.X, point.Y);
        if (positioned)
        {
            Thread.Sleep(100);
            NativeMethods.MouseEvent(NativeMethods.MouseEventLeftDown, 0, 0, 0, UIntPtr.Zero);
            NativeMethods.MouseEvent(NativeMethods.MouseEventLeftUp, 0, 0, 0, UIntPtr.Zero);
            NativeOcrDiagnostics.Write($"Click method=mouse_event foreground={foreground} positioned=true screen={point.X},{point.Y}");
            return true;
        }

        var focused = TrySendInputFocus(point, cancellationToken);
        NativeOcrDiagnostics.Write($"Click method=SendInput foreground={foreground} positioned=false screen={point.X},{point.Y} result={focused}");
        return focused;
    }

    private static bool DragWindow(
        GameWindowCandidate window,
        int clientX,
        int clientY,
        int dragPixels,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!NativeMethods.IsWindow(window.Handle))
        {
            NativeOcrDiagnostics.Write("DragScroll aborted: target window is no longer valid");
            return false;
        }

        NativeMethods.ShowWindow(window.Handle, NativeMethods.ShowWindowRestore);
        var foreground = NativeMethods.SetForegroundWindow(window.Handle);
        Thread.Sleep(300);

        var startClient = new NativePoint
        {
            X = Math.Clamp(clientX, 0, Math.Max(0, window.ClientWidth - 1)),
            Y = Math.Clamp(clientY, 0, Math.Max(0, window.ClientHeight - 1))
        };
        var endClient = new NativePoint
        {
            X = startClient.X,
            Y = Math.Clamp(startClient.Y + dragPixels, 0, Math.Max(0, window.ClientHeight - 1))
        };
        var startScreen = startClient;
        var endScreen = endClient;
        if (!NativeMethods.ClientToScreen(window.Handle, ref startScreen) ||
            !NativeMethods.ClientToScreen(window.Handle, ref endScreen))
        {
            NativeOcrDiagnostics.Write("DragScroll failed: ClientToScreen returned false");
            return false;
        }

        NativeOcrDiagnostics.Write($"DragScroll focus foreground={foreground} startClient={startClient.X},{startClient.Y} endClient={endClient.X},{endClient.Y} startScreen={startScreen.X},{startScreen.Y} endScreen={endScreen.X},{endScreen.Y}");
        if (TrySendInputDrag(startScreen, endScreen, cancellationToken))
        {
            NativeOcrDiagnostics.Write("DragScroll method=SendInput result=success");
            return true;
        }

        if (TryMouseEventDrag(startScreen, endScreen, cancellationToken))
        {
            NativeOcrDiagnostics.Write("DragScroll method=mouse_event result=success");
            return true;
        }

        NativeOcrDiagnostics.Write("DragScroll result=failure");
        return false;
    }

    private static bool TryMouseEventDrag(NativePoint start, NativePoint end, CancellationToken cancellationToken)
    {
        if (!NativeMethods.SetCursorPos(start.X, start.Y))
        {
            NativeOcrDiagnostics.Write("DragScroll mouse_event SetCursorPos=false");
            return false;
        }

        const int steps = 32;
        var buttonDown = false;
        try
        {
            Thread.Sleep(100);
            NativeMethods.MouseEvent(NativeMethods.MouseEventLeftDown, 0, 0, 0, UIntPtr.Zero);
            buttonDown = true;
            for (var step = 1; step <= steps; step++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var point = Interpolate(start, end, step, steps);
                if (!NativeMethods.SetCursorPos(point.X, point.Y)) return false;
                Thread.Sleep(15);
            }
            return true;
        }
        finally
        {
            if (buttonDown) NativeMethods.MouseEvent(NativeMethods.MouseEventLeftUp, 0, 0, 0, UIntPtr.Zero);
        }
    }

    private static bool TrySendInputFocus(NativePoint screenCenter, CancellationToken cancellationToken)
    {
        var virtualLeft = NativeMethods.GetSystemMetrics(NativeMethods.VirtualScreenLeft);
        var virtualTop = NativeMethods.GetSystemMetrics(NativeMethods.VirtualScreenTop);
        var virtualWidth = NativeMethods.GetSystemMetrics(NativeMethods.VirtualScreenWidth);
        var virtualHeight = NativeMethods.GetSystemMetrics(NativeMethods.VirtualScreenHeight);
        if (virtualWidth <= 1 || virtualHeight <= 1) return false;

        var absoluteX = Math.Clamp((screenCenter.X - virtualLeft) * 65535 / (virtualWidth - 1), 0, 65535);
        var absoluteY = Math.Clamp((screenCenter.Y - virtualTop) * 65535 / (virtualHeight - 1), 0, 65535);
        var focusInputs = new[]
        {
            new NativeInput
            {
                Type = NativeMethods.InputMouse,
                Mouse = new NativeMouseInput
                {
                    Dx = absoluteX,
                    Dy = absoluteY,
                    Flags = NativeMethods.MouseEventMove | NativeMethods.MouseEventAbsolute | NativeMethods.MouseEventVirtualDesk
                }
            },
            new NativeInput
            {
                Type = NativeMethods.InputMouse,
                Mouse = new NativeMouseInput { Flags = NativeMethods.MouseEventLeftDown }
            },
            new NativeInput
            {
                Type = NativeMethods.InputMouse,
                Mouse = new NativeMouseInput { Flags = NativeMethods.MouseEventLeftUp }
            }
        };
        var sent = NativeMethods.SendInput((uint)focusInputs.Length, focusInputs, Marshal.SizeOf<NativeInput>());
        NativeOcrDiagnostics.Write($"SendInput focus requested={focusInputs.Length} sent={sent} size={Marshal.SizeOf<NativeInput>()} absolute={absoluteX},{absoluteY}");
        if (sent != (uint)focusInputs.Length) return false;
        Thread.Sleep(250);
        cancellationToken.ThrowIfCancellationRequested();
        return true;
    }

    private static bool TrySendInputDrag(NativePoint start, NativePoint end, CancellationToken cancellationToken)
    {
        var virtualLeft = NativeMethods.GetSystemMetrics(NativeMethods.VirtualScreenLeft);
        var virtualTop = NativeMethods.GetSystemMetrics(NativeMethods.VirtualScreenTop);
        var virtualWidth = NativeMethods.GetSystemMetrics(NativeMethods.VirtualScreenWidth);
        var virtualHeight = NativeMethods.GetSystemMetrics(NativeMethods.VirtualScreenHeight);
        if (virtualWidth <= 1 || virtualHeight <= 1) return false;

        var size = Marshal.SizeOf<NativeInput>();
        var startMove = CreateAbsoluteMove(start, virtualLeft, virtualTop, virtualWidth, virtualHeight);
        var moveSent = NativeMethods.SendInput(1, new[] { startMove }, size);
        NativeOcrDiagnostics.Write($"SendInput drag move requested=1 sent={moveSent} absolute={startMove.Mouse.Dx},{startMove.Mouse.Dy}");
        if (moveSent != 1) return false;

        Thread.Sleep(100);
        cancellationToken.ThrowIfCancellationRequested();
        var buttonDown = new NativeInput
        {
            Type = NativeMethods.InputMouse,
            Mouse = new NativeMouseInput { Flags = NativeMethods.MouseEventLeftDown }
        };
        var downSent = NativeMethods.SendInput(1, new[] { buttonDown }, size);
        NativeOcrDiagnostics.Write($"SendInput drag left-down requested=1 sent={downSent}");
        if (downSent != 1) return false;

        var buttonIsDown = true;
        try
        {
            const int steps = 32;
            for (var step = 1; step <= steps; step++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var point = Interpolate(start, end, step, steps);
                var move = CreateAbsoluteMove(point, virtualLeft, virtualTop, virtualWidth, virtualHeight);
                var sent = NativeMethods.SendInput(1, new[] { move }, size);
                if (sent != 1)
                {
                    NativeOcrDiagnostics.Write($"SendInput drag move failed step={step}/{steps} sent={sent}");
                    return false;
                }
                Thread.Sleep(15);
            }
            return true;
        }
        finally
        {
            if (buttonIsDown)
            {
                var buttonUp = new NativeInput
                {
                    Type = NativeMethods.InputMouse,
                    Mouse = new NativeMouseInput { Flags = NativeMethods.MouseEventLeftUp }
                };
                var upSent = NativeMethods.SendInput(1, new[] { buttonUp }, size);
                NativeOcrDiagnostics.Write($"SendInput drag left-up requested=1 sent={upSent}");
            }
        }
    }

    private static NativeInput CreateAbsoluteMove(
        NativePoint point,
        int virtualLeft,
        int virtualTop,
        int virtualWidth,
        int virtualHeight) =>
        new()
        {
            Type = NativeMethods.InputMouse,
            Mouse = new NativeMouseInput
            {
                Dx = Math.Clamp((point.X - virtualLeft) * 65535 / (virtualWidth - 1), 0, 65535),
                Dy = Math.Clamp((point.Y - virtualTop) * 65535 / (virtualHeight - 1), 0, 65535),
                Flags = NativeMethods.MouseEventMove | NativeMethods.MouseEventAbsolute | NativeMethods.MouseEventVirtualDesk
            }
        };

    private static NativePoint Interpolate(NativePoint start, NativePoint end, int step, int steps) =>
        new()
        {
            X = start.X + (end.X - start.X) * step / steps,
            Y = start.Y + (end.Y - start.Y) * step / steps
        };

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
        var foreground = NativeMethods.SetForegroundWindow(window.Handle);
        NativeOcrDiagnostics.Write($"Capture handle=0x{window.Handle.ToInt64():X} origin={screenOrigin.X},{screenOrigin.Y} size={width}x{height} foreground={foreground}");
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
        [LibraryImport("user32.dll")] internal static partial IntPtr GetForegroundWindow();
        [LibraryImport("user32.dll", SetLastError = true)] internal static partial uint GetWindowThreadProcessId(IntPtr handle, out uint processId);
        internal const uint InputMouse = 0;
        internal const uint MouseEventMove = 0x0001;
        internal const uint MouseEventLeftDown = 0x0002;
        internal const uint MouseEventLeftUp = 0x0004;
        internal const uint MouseEventAbsolute = 0x8000;
        internal const uint MouseEventVirtualDesk = 0x4000;
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
        [DllImport("user32.dll", EntryPoint = "mouse_event", SetLastError = true)] internal static extern void MouseEvent(uint flags, uint dx, uint dy, int data, UIntPtr extraInfo);
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
