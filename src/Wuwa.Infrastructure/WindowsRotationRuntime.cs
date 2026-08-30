using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Wuwa.Core;

namespace Wuwa.Infrastructure;

/// <summary>Read-only game-window discovery and state adapter for Rotation.</summary>
public sealed partial class WindowsRotationGameMonitor : IRotationGameMonitor
{
    public Task<RotationGameWindow?> TryFindAsync(
        IReadOnlyCollection<string> processNames,
        int minimumWidth = 800,
        int minimumHeight = 600,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var names = processNames.Select(Path.GetFileNameWithoutExtension).Where(name => !string.IsNullOrWhiteSpace(name)).Select(name => name!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Task.Run(() => Find(names, minimumWidth, minimumHeight, cancellationToken), cancellationToken);
    }

    public RotationGameWindowState ReadState(RotationGameWindow window)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var exists = Native.IsWindow(window.Handle);
        if (!exists) return new(false, false, false, false, null);
        var visible = Native.IsWindowVisible(window.Handle);
        var minimized = Native.IsIconic(window.Handle);
        RotationWindowBounds? bounds = null;
        if (visible && !minimized && Native.GetClientRect(window.Handle, out var rectangle))
        {
            var origin = new Point();
            var width = rectangle.Right - rectangle.Left;
            var height = rectangle.Bottom - rectangle.Top;
            if (width > 0 && height > 0 && Native.ClientToScreen(window.Handle, ref origin))
                bounds = new(window.Handle, origin.X, origin.Y, width, height);
        }
        return new(true, visible, minimized, Native.GetForegroundWindow() == window.Handle, bounds);
    }

    public bool TryActivate(RotationGameWindow window)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        if (!Native.IsWindow(window.Handle)) return false;
        Native.ShowWindow(window.Handle, 9);
        return Native.SetForegroundWindow(window.Handle);
    }

    private static RotationGameWindow? Find(IReadOnlySet<string> names, int minimumWidth, int minimumHeight, CancellationToken cancellationToken)
    {
        var processes = new Dictionary<int, string>();
        foreach (var name in names)
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                using (process)
                {
                    try { processes.TryAdd(process.Id, process.ProcessName); }
                    catch (Exception exception) when (exception is InvalidOperationException or Win32Exception) { }
                }
            }
        }
        var candidates = new List<(RotationGameWindow Window, long Area)>();
        Native.EnumWindows((handle, _) =>
        {
            if (cancellationToken.IsCancellationRequested) return false;
            Native.GetWindowThreadProcessId(handle, out var processId);
            if (!processes.TryGetValue(checked((int)processId), out var processName) || !Native.IsWindowVisible(handle) || Native.IsIconic(handle)) return true;
            if (!Native.GetClientRect(handle, out var rectangle)) return true;
            var width = rectangle.Right - rectangle.Left;
            var height = rectangle.Bottom - rectangle.Top;
            if (width < minimumWidth || height < minimumHeight) return true;
            candidates.Add((new(handle, checked((int)processId), processName, GetTitle(handle)), (long)width * height));
            return true;
        }, nint.Zero);
        cancellationToken.ThrowIfCancellationRequested();
        return candidates.OrderByDescending(item => item.Area).Select(item => item.Window).FirstOrDefault();
    }

    private static string GetTitle(nint handle)
    {
        var length = Native.GetWindowTextLength(handle);
        if (length <= 0) return string.Empty;
        var buffer = new char[length + 1];
        return Native.GetWindowText(handle, buffer, buffer.Length) > 0 ? new string(buffer, 0, length) : string.Empty;
    }

    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    private delegate bool EnumWindowsProc(nint hwnd, nint lParam);

    private static partial class Native
    {
        [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static partial bool EnumWindows(EnumWindowsProc callback, nint lParam);
        [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static partial bool IsWindow(nint hwnd);
        [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static partial bool IsWindowVisible(nint hwnd);
        [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static partial bool IsIconic(nint hwnd);
        [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static partial bool GetClientRect(nint hwnd, out Rect rect);
        [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static partial bool ClientToScreen(nint hwnd, ref Point point);
        [LibraryImport("user32.dll")] internal static partial nint GetForegroundWindow();
        [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static partial bool SetForegroundWindow(nint hwnd);
        [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static partial bool ShowWindow(nint hwnd, int command);
        [LibraryImport("user32.dll")] internal static partial uint GetWindowThreadProcessId(nint hwnd, out uint processId);
        [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW", StringMarshalling = StringMarshalling.Utf16)] internal static partial int GetWindowText(nint hwnd, [Out] char[] text, int maxCount);
        [LibraryImport("user32.dll", EntryPoint = "GetWindowTextLengthW")] internal static partial int GetWindowTextLength(nint hwnd);
    }
}

/// <summary>Observes physical keyboard/mouse transitions and always forwards them to the next hook.</summary>
public sealed partial class WindowsRotationInputSource : IRotationInputSource
{
    private const int WhKeyboardLl = 13;
    private const int WhMouseLl = 14;
    private const int WmQuit = 0x0012;
    private const uint KeyboardInjected = 0x10;
    private const uint KeyboardLowerIntegrityInjected = 0x02;
    private const uint MouseInjected = 0x01;
    private readonly object _gate = new();
    private readonly Func<int, bool> _allowQuitPostAttempt;
    private Thread? _thread;
    private uint _threadId;
    private nint _keyboardHook;
    private nint _mouseHook;
    private HookProc? _keyboardProc;
    private HookProc? _mouseProc;
    private ManualResetEventSlim? _started;
    private Exception? _startError;
    private bool _disposed;

    public WindowsRotationInputSource() : this(_ => true) { }

    internal WindowsRotationInputSource(Func<int, bool> allowQuitPostAttempt) =>
        _allowQuitPostAttempt = allowQuitPostAttempt ?? throw new ArgumentNullException(nameof(allowQuitPostAttempt));

    public event EventHandler<RotationObservedInput>? InputObserved;
    public bool IsRunning => _thread is { IsAlive: true } && _keyboardHook != 0 && _mouseHook != 0;

    public void Start()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_thread is { IsAlive: true }) return;
            _started = new(false);
            _startError = null;
            _thread = new Thread(MessageLoop) { IsBackground = true, Name = "Wuwa Rotation input observer" };
            _thread.Start();
        }
        if (!_started!.Wait(TimeSpan.FromSeconds(5)))
        {
            Stop(TimeSpan.FromSeconds(3));
            throw new TimeoutException("Rotation input observer did not start.");
        }
        if (_startError is not null)
        {
            Stop(TimeSpan.FromSeconds(3));
            throw new InvalidOperationException("Unable to install Rotation input observer.", _startError);
        }
        if (!IsRunning)
        {
            Stop(TimeSpan.FromSeconds(3));
            throw new InvalidOperationException("Rotation input observer did not reach a running state.");
        }
    }

    private void MessageLoop()
    {
        _threadId = Native.GetCurrentThreadId();
        _keyboardProc = KeyboardHook;
        _mouseProc = MouseHook;
        try
        {
            // Force creation of this thread's message queue before Start can return.
            Native.PeekMessage(out _, nint.Zero, 0, 0, 0);
            _keyboardHook = Native.SetWindowsHookEx(WhKeyboardLl, _keyboardProc, nint.Zero, 0);
            if (_keyboardHook == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            _mouseHook = Native.SetWindowsHookEx(WhMouseLl, _mouseProc, nint.Zero, 0);
            if (_mouseHook == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            _started?.Set();
            while (Native.GetMessage(out var message, nint.Zero, 0, 0) > 0) { }
        }
        catch (Exception exception)
        {
            _startError = exception;
            _started?.Set();
        }
        finally
        {
            UnhookAll();
            _threadId = 0;
        }
    }

    private nint KeyboardHook(int code, nint wParam, nint lParam)
    {
        nint next = 0;
        try
        {
            if (code >= 0)
            {
                var data = Marshal.PtrToStructure<KeyboardData>(lParam);
                if ((data.Flags & (KeyboardInjected | KeyboardLowerIntegrityInjected)) == 0)
                {
                    var message = wParam.ToInt32();
                    if (message is 0x0100 or 0x0104) Publish(new(new(RotationInputDevice.Keyboard, checked((int)data.VirtualKey)), true));
                    else if (message is 0x0101 or 0x0105) Publish(new(new(RotationInputDevice.Keyboard, checked((int)data.VirtualKey)), false));
                }
            }
        }
        catch { }
        finally { next = Native.CallNextHookEx(_keyboardHook, code, wParam, lParam); }
        return next;
    }

    private nint MouseHook(int code, nint wParam, nint lParam)
    {
        nint next = 0;
        try
        {
            if (code >= 0)
            {
                var data = Marshal.PtrToStructure<MouseData>(lParam);
                if ((data.Flags & MouseInjected) == 0 && TryMapMouse(wParam.ToInt32(), data.ButtonData, out var button, out var down))
                    Publish(new(new(RotationInputDevice.Mouse, button), down));
            }
        }
        catch { }
        finally { next = Native.CallNextHookEx(_mouseHook, code, wParam, lParam); }
        return next;
    }

    private void Publish(RotationInputEvent input)
    {
        var observed = new RotationObservedInput(input, Native.GetForegroundWindow());
        try { InputObserved?.Invoke(this, observed); }
        catch { }
    }

    private static bool TryMapMouse(int message, uint mouseData, out int button, out bool down)
    {
        (button, down) = message switch
        {
            0x0201 => (1, true), 0x0202 => (1, false),
            0x0204 => (2, true), 0x0205 => (2, false),
            0x0207 => (3, true), 0x0208 => (3, false),
            0x020B => ((((mouseData >> 16) & 0xffff) == 1 ? 4 : 5), true),
            0x020C => ((((mouseData >> 16) & 0xffff) == 1 ? 4 : 5), false),
            _ => (0, false)
        };
        return button != 0;
    }

    public bool Stop(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        Thread? thread;
        uint threadId;
        lock (_gate)
        {
            thread = _thread;
            threadId = _threadId;
        }
        if (thread is null || !thread.IsAlive)
        {
            UnhookAll();
            return _keyboardHook == 0 && _mouseHook == 0;
        }
        if (Thread.CurrentThread == thread) return false;

        var posted = TryPostQuit(threadId, 1);
        if (!posted)
        {
            UnhookAll();
            posted = TryPostQuit(threadId, 2);
        }
        var exited = thread.Join(timeout);
        if (!exited)
        {
            // Never leave global hooks active even if the message thread cannot be joined.
            UnhookAll();
            TryPostQuit(threadId, 3);
            exited = thread.Join(TimeSpan.FromMilliseconds(500));
        }
        return exited && _keyboardHook == 0 && _mouseHook == 0;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        var stopped = Stop(TimeSpan.FromSeconds(3));
        if (stopped) _started?.Dispose();
    }

    private bool TryPostQuit(uint threadId, int attempt) =>
        threadId != 0 && _allowQuitPostAttempt(attempt) && Native.PostThreadMessage(threadId, WmQuit, nint.Zero, nint.Zero);

    private void UnhookAll()
    {
        var mouse = Interlocked.Exchange(ref _mouseHook, nint.Zero);
        if (mouse != 0) Native.UnhookWindowsHookEx(mouse);
        var keyboard = Interlocked.Exchange(ref _keyboardHook, nint.Zero);
        if (keyboard != 0) Native.UnhookWindowsHookEx(keyboard);
    }

    private delegate nint HookProc(int code, nint wParam, nint lParam);
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardData { public uint VirtualKey, ScanCode, Flags, Time; public nuint ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseData { public int X, Y; public uint ButtonData, Flags, Time; public nuint ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct Message { public nint Hwnd; public uint MessageId; public nuint WParam; public nint LParam; public uint Time; public int X, Y; public uint Private; }

    private static partial class Native
    {
        [LibraryImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)] internal static partial nint SetWindowsHookEx(int hookId, HookProc callback, nint module, uint threadId);
        [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static partial bool UnhookWindowsHookEx(nint hook);
        [LibraryImport("user32.dll")] internal static partial nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);
        [LibraryImport("user32.dll", EntryPoint = "GetMessageW")] internal static partial int GetMessage(out Message message, nint hwnd, uint min, uint max);
        [LibraryImport("user32.dll", EntryPoint = "PeekMessageW")] [return: MarshalAs(UnmanagedType.Bool)] internal static partial bool PeekMessage(out Message message, nint hwnd, uint min, uint max, uint removeMessage);
        [LibraryImport("user32.dll", EntryPoint = "PostThreadMessageW")] [return: MarshalAs(UnmanagedType.Bool)] internal static partial bool PostThreadMessage(uint threadId, uint message, nint wParam, nint lParam);
        [LibraryImport("user32.dll")] internal static partial nint GetForegroundWindow();
        [LibraryImport("kernel32.dll")] internal static partial uint GetCurrentThreadId();
    }
}
