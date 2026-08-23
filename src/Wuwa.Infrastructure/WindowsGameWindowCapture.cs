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
    private const int WheelChunkDistance = 160;
    private const int TextFieldFocusSettleMilliseconds = 220;
    private const int ModifierSettleMilliseconds = 40;
    private const int KeyPressMilliseconds = 30;
    private const int SelectAllSettleMilliseconds = 160;
    private const int ClipboardPasteSettleMilliseconds = 320;

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

    public bool TryGetClientScreenBounds(
        GameWindowCandidate window,
        out GameWindowScreenBounds bounds)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Game-window capture currently supports Windows only.");
        if (window.Handle == 0) throw new ArgumentException("A valid window handle is required.", nameof(window));
        bounds = default!;
        if (!NativeMethods.IsWindow(window.Handle) || !NativeMethods.GetClientRect(window.Handle, out var client))
        {
            NativeOcrDiagnostics.Write($"ClientScreenBounds failed handle=0x{window.Handle.ToInt64():X}");
            return false;
        }

        var origin = new NativePoint();
        if (!NativeMethods.ClientToScreen(window.Handle, ref origin))
        {
            NativeOcrDiagnostics.Write($"ClientScreenBounds ClientToScreen failed handle=0x{window.Handle.ToInt64():X}");
            return false;
        }

        var width = client.Right - client.Left;
        var height = client.Bottom - client.Top;
        if (width <= 0 || height <= 0) return false;
        bounds = new GameWindowScreenBounds(origin.X, origin.Y, width, height);
        NativeOcrDiagnostics.Write($"ClientScreenBounds handle=0x{window.Handle.ToInt64():X} bounds={origin.X},{origin.Y},{width}x{height}");
        return true;
    }

    public Task<bool> ScrollAsync(
        GameWindowCandidate window,
        int scrollDistance = -2400,
        int eventIntervalMilliseconds = 100,
        CancellationToken cancellationToken = default)
    {
        ValidateScrollArguments(window, scrollDistance, eventIntervalMilliseconds);
        var clientX = (int)Math.Round(window.ClientWidth * AchievementListScrollXRatio);
        var clientY = (int)Math.Round(window.ClientHeight * 0.62);
        NativeOcrDiagnostics.Write($"Scroll requested handle=0x{window.Handle.ToInt64():X} pid={window.ProcessId} achievement-list client={clientX},{clientY} distance={scrollDistance} interval={eventIntervalMilliseconds}ms");
        return Task.Run(() => ScrollWindow(window, clientX, clientY, scrollDistance, eventIntervalMilliseconds, cancellationToken), cancellationToken);
    }

    public Task<bool> ScrollAtAsync(
        GameWindowCandidate window,
        int clientX,
        int clientY,
        int scrollDistance = -2400,
        int eventIntervalMilliseconds = 100,
        CancellationToken cancellationToken = default)
    {
        ValidateScrollArguments(window, scrollDistance, eventIntervalMilliseconds);
        NativeOcrDiagnostics.Write($"Scroll requested handle=0x{window.Handle.ToInt64():X} pid={window.ProcessId} client={clientX},{clientY} distance={scrollDistance} interval={eventIntervalMilliseconds}ms");
        return Task.Run(() => ScrollWindow(window, clientX, clientY, scrollDistance, eventIntervalMilliseconds, cancellationToken), cancellationToken);
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

    /// <summary>
    /// Focuses a game text field, selects its current contents, and pastes Unicode
    /// text from the Windows clipboard. This keeps text entry inside the same
    /// foreground-window and integrity-level checks as the existing mouse automation.
    /// </summary>
    public Task<bool> ReplaceTextAsync(
        GameWindowCandidate window,
        int clientX,
        int clientY,
        string text,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Game-window input currently supports Windows only.");
        if (window.Handle == 0) throw new ArgumentException("A valid window handle is required.", nameof(window));
        if (text is null) throw new ArgumentNullException(nameof(text));
        NativeOcrDiagnostics.Write($"ReplaceText requested handle=0x{window.Handle.ToInt64():X} pid={window.ProcessId} client={clientX},{clientY} length={text.Length} method=clipboard-paste");
        return Task.Run(() => ReplaceText(window, clientX, clientY, text, cancellationToken), cancellationToken);
    }

    private static void ValidateScrollArguments(GameWindowCandidate window, int scrollDistance, int eventIntervalMilliseconds)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Game-window input currently supports Windows only.");
        if (window.Handle == 0) throw new ArgumentException("A valid window handle is required.", nameof(window));
        if (scrollDistance == 0) throw new ArgumentOutOfRangeException(nameof(scrollDistance));
        if (eventIntervalMilliseconds is < 20 or > 1000) throw new ArgumentOutOfRangeException(nameof(eventIntervalMilliseconds));
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

    private static bool ReplaceText(
        GameWindowCandidate window,
        int clientX,
        int clientY,
        string text,
        CancellationToken cancellationToken)
    {
        // The game's text field accepts clipboard paste reliably, while
        // KEYEVENTF_UNICODE input is reported as sent by Windows but is ignored
        // by the game's Slate/IME text widget for Chinese achievement names.
        if (!SetClipboardUnicodeText(text)) return false;
        if (!ClickWindow(window, clientX, clientY, cancellationToken)) return false;
        WaitForInput(TextFieldFocusSettleMilliseconds, cancellationToken);

        if (!SendPacedKeyChord(NativeMethods.VirtualKeyA, cancellationToken)) return false;
        WaitForInput(SelectAllSettleMilliseconds, cancellationToken);
        if (!SendPacedKeyChord(NativeMethods.VirtualKeyV, cancellationToken)) return false;
        WaitForInput(ClipboardPasteSettleMilliseconds, cancellationToken);
        NativeOcrDiagnostics.Write($"ReplaceText paste completed text={text}");
        return true;
    }

    private static bool SendPacedKeyChord(ushort key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var controlPressed = false;
        var keyPressed = false;
        try
        {
            if (!SendKeyEvent(NativeMethods.VirtualKeyControl, keyUp: false)) return false;
            controlPressed = true;
            Thread.Sleep(ModifierSettleMilliseconds);

            if (!SendKeyEvent(key, keyUp: false)) return false;
            keyPressed = true;
            Thread.Sleep(KeyPressMilliseconds);

            if (!SendKeyEvent(key, keyUp: true)) return false;
            keyPressed = false;
            Thread.Sleep(ModifierSettleMilliseconds);

            if (!SendKeyEvent(NativeMethods.VirtualKeyControl, keyUp: true)) return false;
            controlPressed = false;
            NativeOcrDiagnostics.Write($"ReplaceText paced-keychord key=0x{key:X2} result=success");
            return true;
        }
        finally
        {
            if (keyPressed) _ = SendKeyEvent(key, keyUp: true);
            if (controlPressed) _ = SendKeyEvent(NativeMethods.VirtualKeyControl, keyUp: true);
        }
    }

    private static bool SendKeyEvent(ushort key, bool keyUp)
    {
        var inputs = new[]
        {
            CreateKeyInput(key, keyUp ? NativeMethods.KeyboardEventKeyUp : 0)
        };
        var sent = NativeMethods.SendKeyboardInput(
            1,
            inputs,
            Marshal.SizeOf<NativeKeyboardInput>());
        if (sent != 1)
        {
            NativeOcrDiagnostics.Write($"ReplaceText key-event key=0x{key:X2} keyUp={keyUp} requested=1 sent={sent}");
        }
        return sent == 1;
    }

    private static void WaitForInput(int milliseconds, CancellationToken cancellationToken)
    {
        if (cancellationToken.WaitHandle.WaitOne(milliseconds))
        {
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    private static bool SetClipboardUnicodeText(string text)
    {
        var data = Encoding.Unicode.GetBytes(text + '\0');
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            if (NativeMethods.OpenClipboard(IntPtr.Zero))
            {
                IntPtr allocation = IntPtr.Zero;
                try
                {
                    if (!NativeMethods.EmptyClipboard())
                    {
                        NativeOcrDiagnostics.Write($"Clipboard EmptyClipboard failed attempt={attempt}");
                        return false;
                    }

                    allocation = NativeMethods.GlobalAlloc(NativeMethods.GlobalMemoryMoveable, (UIntPtr)data.Length);
                    if (allocation == IntPtr.Zero) return false;
                    var target = NativeMethods.GlobalLock(allocation);
                    if (target == IntPtr.Zero) return false;
                    try
                    {
                        Marshal.Copy(data, 0, target, data.Length);
                    }
                    finally
                    {
                        NativeMethods.GlobalUnlock(allocation);
                    }

                    if (NativeMethods.SetClipboardData(NativeMethods.ClipboardUnicodeText, allocation) == IntPtr.Zero)
                    {
                        NativeOcrDiagnostics.Write($"Clipboard SetClipboardData failed attempt={attempt}");
                        return false;
                    }

                    allocation = IntPtr.Zero;
                    NativeOcrDiagnostics.Write($"Clipboard Unicode text set length={text.Length} attempt={attempt}");
                    return true;
                }
                finally
                {
                    if (allocation != IntPtr.Zero) NativeMethods.GlobalFree(allocation);
                    NativeMethods.CloseClipboard();
                }
            }

            Thread.Sleep(60);
        }

        NativeOcrDiagnostics.Write("Clipboard OpenClipboard failed after retries");
        return false;
    }

    private static NativeKeyboardInput CreateKeyInput(ushort virtualKey, uint flags) =>
        new()
        {
            Type = NativeMethods.InputKeyboard,
            Keyboard = new NativeKeyboardInputData
            {
                VirtualKey = virtualKey,
                Flags = flags
            }
        };

    private static bool ScrollWindow(
        GameWindowCandidate window,
        int clientX,
        int clientY,
        int scrollDistance,
        int eventIntervalMilliseconds,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!NativeMethods.IsWindow(window.Handle))
        {
            NativeOcrDiagnostics.Write("Scroll aborted: target window is no longer valid");
            return false;
        }

        NativeMethods.ShowWindow(window.Handle, NativeMethods.ShowWindowRestore);
        var foreground = NativeMethods.SetForegroundWindow(window.Handle);
        Thread.Sleep(300);
        var screenPoint = new NativePoint
        {
            X = Math.Clamp(clientX, 0, Math.Max(0, window.ClientWidth - 1)),
            Y = Math.Clamp(clientY, 0, Math.Max(0, window.ClientHeight - 1))
        };
        if (!NativeMethods.ClientToScreen(window.Handle, ref screenPoint))
        {
            NativeOcrDiagnostics.Write("Scroll failed: ClientToScreen returned false");
            return false;
        }

        var wheelDeltas = BuildWheelDeltas(scrollDistance);
        NativeOcrDiagnostics.Write($"Scroll focus foreground={foreground} screen={screenPoint.X},{screenPoint.Y} events={wheelDeltas.Count} distance={scrollDistance}");
        if (TryMouseEventScroll(screenPoint, wheelDeltas, eventIntervalMilliseconds, cancellationToken))
        {
            NativeOcrDiagnostics.Write("Scroll method=mouse_event result=success");
            return true;
        }
        if (TrySendInputScroll(screenPoint, wheelDeltas, eventIntervalMilliseconds, cancellationToken))
        {
            NativeOcrDiagnostics.Write("Scroll method=SendInput result=success");
            return true;
        }

        var fallbackSucceeded = true;
        foreach (var wheelDelta in wheelDeltas)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var messageData = unchecked((UIntPtr)((uint)wheelDelta << 16));
            var point = new IntPtr((screenPoint.Y << 16) | (screenPoint.X & 0xffff));
            fallbackSucceeded &= NativeMethods.PostMessage(window.Handle, NativeMethods.MouseWheelMessage, messageData, point);
            WaitForInputInterval(eventIntervalMilliseconds, cancellationToken);
        }
        NativeOcrDiagnostics.Write($"Scroll method=PostMessage result={fallbackSucceeded}");
        return fallbackSucceeded;
    }

    private static bool TryMouseEventScroll(
        NativePoint screenPoint,
        IReadOnlyList<int> wheelDeltas,
        int eventIntervalMilliseconds,
        CancellationToken cancellationToken)
    {
        if (!NativeMethods.SetCursorPos(screenPoint.X, screenPoint.Y))
        {
            NativeOcrDiagnostics.Write("Scroll mouse_event SetCursorPos=false");
            return false;
        }

        Thread.Sleep(100);
        foreach (var wheelDelta in wheelDeltas)
        {
            cancellationToken.ThrowIfCancellationRequested();
            NativeMethods.MouseEvent(NativeMethods.MouseEventWheel, 0, 0, wheelDelta, UIntPtr.Zero);
            WaitForInputInterval(eventIntervalMilliseconds, cancellationToken);
        }
        NativeOcrDiagnostics.Write($"Scroll method=mouse_event positioned=true events={wheelDeltas.Count} interval={eventIntervalMilliseconds}ms");
        return true;
    }

    private static bool TrySendInputScroll(
        NativePoint screenPoint,
        IReadOnlyList<int> wheelDeltas,
        int eventIntervalMilliseconds,
        CancellationToken cancellationToken)
    {
        var virtualLeft = NativeMethods.GetSystemMetrics(NativeMethods.VirtualScreenLeft);
        var virtualTop = NativeMethods.GetSystemMetrics(NativeMethods.VirtualScreenTop);
        var virtualWidth = NativeMethods.GetSystemMetrics(NativeMethods.VirtualScreenWidth);
        var virtualHeight = NativeMethods.GetSystemMetrics(NativeMethods.VirtualScreenHeight);
        if (virtualWidth <= 1 || virtualHeight <= 1) return false;

        var size = Marshal.SizeOf<NativeInput>();
        var moveInput = CreateAbsoluteMove(screenPoint, virtualLeft, virtualTop, virtualWidth, virtualHeight);
        var moveSent = NativeMethods.SendInput(1, new[] { moveInput }, size);
        NativeOcrDiagnostics.Write($"SendInput scroll pointer requested=1 sent={moveSent} absolute={moveInput.Mouse.Dx},{moveInput.Mouse.Dy}");
        if (moveSent != 1) return false;

        Thread.Sleep(250);
        foreach (var wheelDelta in wheelDeltas)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var wheelInput = new NativeInput
            {
                Type = NativeMethods.InputMouse,
                Mouse = new NativeMouseInput
                {
                    MouseData = unchecked((uint)wheelDelta),
                    Flags = NativeMethods.MouseEventWheel
                }
            };
            if (NativeMethods.SendInput(1, new[] { wheelInput }, size) != 1) return false;
            WaitForInputInterval(eventIntervalMilliseconds, cancellationToken);
        }
        NativeOcrDiagnostics.Write($"SendInput wheel requested={wheelDeltas.Count} sent={wheelDeltas.Count} interval={eventIntervalMilliseconds}ms");
        return true;
    }

    private static IReadOnlyList<int> BuildWheelDeltas(int scrollDistance)
    {
        var direction = Math.Sign(scrollDistance);
        var remaining = Math.Abs(scrollDistance);
        var result = new List<int>((remaining + WheelChunkDistance - 1) / WheelChunkDistance);
        while (remaining > 0)
        {
            var current = Math.Min(WheelChunkDistance, remaining);
            result.Add(direction * current);
            remaining -= current;
        }
        return result;
    }

    private static void WaitForInputInterval(int milliseconds, CancellationToken cancellationToken)
    {
        if (cancellationToken.WaitHandle.WaitOne(milliseconds))
        {
            throw new OperationCanceledException(cancellationToken);
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

    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct NativeKeyboardInput
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(8)] public NativeKeyboardInputData Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeKeyboardInputData
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
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
        internal const uint InputKeyboard = 1;
        internal const uint KeyboardEventKeyUp = 0x0002;
        internal const ushort VirtualKeyControl = 0x11;
        internal const ushort VirtualKeyA = 0x41;
        internal const ushort VirtualKeyV = 0x56;
        internal const uint GlobalMemoryMoveable = 0x0002;
        internal const uint ClipboardUnicodeText = 13;
        internal const uint MouseEventMove = 0x0001;
        internal const uint MouseEventLeftDown = 0x0002;
        internal const uint MouseEventLeftUp = 0x0004;
        internal const uint MouseEventWheel = 0x0800;
        internal const uint MouseEventAbsolute = 0x8000;
        internal const uint MouseEventVirtualDesk = 0x4000;
        internal const int ShowWindowRestore = 9;
        internal const int VirtualScreenLeft = 76;
        internal const int VirtualScreenTop = 77;
        internal const int VirtualScreenWidth = 78;
        internal const int VirtualScreenHeight = 79;
        internal const uint MouseWheelMessage = 0x020A;

        [LibraryImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static partial bool GetClientRect(IntPtr handle, out NativeRect rectangle);
        [LibraryImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static partial bool ClientToScreen(IntPtr handle, ref NativePoint point);
        [LibraryImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static partial bool SetForegroundWindow(IntPtr handle);
        [LibraryImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static partial bool BringWindowToTop(IntPtr handle);
        [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static partial bool ShowWindow(IntPtr handle, int command);
        [LibraryImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static partial bool SetCursorPos(int x, int y);
        [LibraryImport("user32.dll")] internal static partial int GetSystemMetrics(int index);
        [DllImport("user32.dll", EntryPoint = "mouse_event", SetLastError = true)] internal static extern void MouseEvent(uint flags, uint dx, uint dy, int data, UIntPtr extraInfo);
        [DllImport("user32.dll", SetLastError = true)] internal static extern uint SendInput(uint count, [In] NativeInput[] inputs, int size);
        [DllImport("user32.dll", SetLastError = true, EntryPoint = "SendInput")] internal static extern uint SendKeyboardInput(uint count, [In] NativeKeyboardInput[] inputs, int size);
        [LibraryImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static partial bool OpenClipboard(IntPtr owner);
        [LibraryImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static partial bool CloseClipboard();
        [LibraryImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static partial bool EmptyClipboard();
        [LibraryImport("user32.dll", SetLastError = true)] internal static partial IntPtr SetClipboardData(uint format, IntPtr memory);
        [LibraryImport("kernel32.dll", SetLastError = true)] internal static partial IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
        [LibraryImport("kernel32.dll", SetLastError = true)] internal static partial IntPtr GlobalLock(IntPtr memory);
        [LibraryImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static partial bool GlobalUnlock(IntPtr memory);
        [LibraryImport("kernel32.dll", SetLastError = true)] internal static partial IntPtr GlobalFree(IntPtr memory);
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
