namespace Wuwa.Core;

public sealed record RotationGameWindow(nint Handle, int ProcessId, string ProcessName, string Title);

public sealed record RotationWindowBounds(
    nint Handle,
    int Left,
    int Top,
    int Width,
    int Height)
{
    public int Right => checked(Left + Width);
    public int Bottom => checked(Top + Height);
}

public sealed record RotationGameWindowState(
    bool Exists,
    bool IsVisible,
    bool IsMinimized,
    bool IsForeground,
    RotationWindowBounds? Bounds);

/// <summary>An observed physical transition plus the foreground HWND at observation time.</summary>
public sealed record RotationObservedInput(RotationInputEvent Input, nint ForegroundWindow);

public static class RotationRuntimeInputGate
{
    public static bool CanAccept(
        RotationObservedInput observed,
        RotationGameWindow gameWindow,
        RotationGameWindowState currentState) =>
        observed.ForegroundWindow == gameWindow.Handle &&
        currentState.Exists &&
        currentState.IsVisible &&
        !currentState.IsMinimized &&
        currentState.IsForeground &&
        currentState.Bounds is not null;
}

public interface IRotationInputSource : IDisposable
{
    event EventHandler<RotationObservedInput>? InputObserved;
    bool IsRunning { get; }
    void Start();
    bool Stop(TimeSpan timeout);
}

public interface IRotationGameMonitor
{
    Task<RotationGameWindow?> TryFindAsync(
        IReadOnlyCollection<string> processNames,
        int minimumWidth = 800,
        int minimumHeight = 600,
        CancellationToken cancellationToken = default);

    RotationGameWindowState ReadState(RotationGameWindow window);
    bool TryActivate(RotationGameWindow window);
}
