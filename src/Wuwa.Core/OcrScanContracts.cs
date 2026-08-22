namespace Wuwa.Core;

public enum OcrScanMode
{
    CurrentCategory,
    FullScan
}

public enum OcrScanPhase
{
    Preparing,
    FindingGameWindow,
    ScanningCurrentCategory,
    SwitchingPrimaryCategory,
    DiscoveringSecondaryCategories,
    ScanningCategory,
    ScrollingCategory,
    Completed,
    Cancelling,
    Failed
}

public sealed record OcrImageFrame(
    byte[] BgrPixels,
    int Width,
    int Height,
    int Stride)
{
    public void Validate()
    {
        if (Width <= 0 || Height <= 0 || Stride < checked(Width * 3)) throw new InvalidDataException("OCR frame dimensions or stride are invalid.");
        if (BgrPixels.Length < checked(Stride * Height)) throw new InvalidDataException("OCR frame buffer is smaller than stride × height.");
    }
}

public sealed record GameWindowCandidate(
    nint Handle,
    int ProcessId,
    string ProcessName,
    string Title,
    int ClientWidth,
    int ClientHeight);

public sealed record OcrPoint(float X, float Y);

public sealed record OcrTextLine(
    IReadOnlyList<OcrPoint> Points,
    string Text,
    float Score);

public interface IGameWindowCapture
{
    Task<GameWindowCandidate> FindGameWindowAsync(
        IReadOnlyCollection<string> processNames,
        int minimumWidth = 800,
        int minimumHeight = 600,
        CancellationToken cancellationToken = default);

    Task<OcrImageFrame> CaptureClientAsync(
        GameWindowCandidate window,
        int? expectedWidth = null,
        int? expectedHeight = null,
        CancellationToken cancellationToken = default);
}

public interface IOcrTextReader
{
    Task<IReadOnlyList<OcrTextLine>> ReadPageAsync(
        OcrImageFrame frame,
        CancellationToken cancellationToken = default);
}

public enum OcrScanErrorCode
{
    Cancelled,
    WindowNotFound,
    CaptureFailed,
    RecognitionFailed
}

public sealed record OcrScanError(OcrScanErrorCode Code, string Message);

public sealed record SinglePageOcrScanResult(
    bool IsSuccess,
    GameWindowCandidate? Window,
    IReadOnlyList<OcrTextLine> Lines,
    TimeSpan Elapsed,
    OcrScanError? Error = null);

public sealed class SinglePageOcrScanService
{
    private readonly IGameWindowCapture _capture;
    private readonly IOcrTextReader _reader;

    public SinglePageOcrScanService(IGameWindowCapture capture, IOcrTextReader reader)
    {
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
    }

    public async Task<SinglePageOcrScanResult> ScanAsync(
        IReadOnlyCollection<string> processNames,
        int? expectedWidth = null,
        int? expectedHeight = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(processNames);
        if (processNames.Count == 0 || processNames.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("At least one non-empty game process name is required.", nameof(processNames));
        }

        var started = System.Diagnostics.Stopwatch.StartNew();
        GameWindowCandidate? window = null;
        try
        {
            window = await _capture.FindGameWindowAsync(processNames, cancellationToken: cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var frame = await _capture.CaptureClientAsync(window, expectedWidth, expectedHeight, cancellationToken).ConfigureAwait(false);
            frame.Validate();
            cancellationToken.ThrowIfCancellationRequested();
            var lines = await _reader.ReadPageAsync(frame, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return new SinglePageOcrScanResult(true, window, Array.AsReadOnly(lines.ToArray()), started.Elapsed);
        }
        catch (OperationCanceledException)
        {
            return Failure(OcrScanErrorCode.Cancelled, "OCR scan was cancelled.", window, started.Elapsed);
        }
        catch (GameWindowNotFoundException exception)
        {
            return Failure(OcrScanErrorCode.WindowNotFound, exception.Message, window, started.Elapsed);
        }
        catch (GameWindowCaptureException exception)
        {
            return Failure(OcrScanErrorCode.CaptureFailed, exception.Message, window, started.Elapsed);
        }
        catch (Exception exception)
        {
            return Failure(OcrScanErrorCode.RecognitionFailed, exception.Message, window, started.Elapsed);
        }
    }

    private static SinglePageOcrScanResult Failure(
        OcrScanErrorCode code,
        string message,
        GameWindowCandidate? window,
        TimeSpan elapsed) =>
        new(false, window, Array.Empty<OcrTextLine>(), elapsed, new OcrScanError(code, message));
}

public sealed class GameWindowNotFoundException : Exception
{
    public GameWindowNotFoundException(string message) : base(message) { }
}

public sealed class GameWindowCaptureException : Exception
{
    public GameWindowCaptureException(string message) : base(message) { }
    public GameWindowCaptureException(string message, Exception innerException) : base(message, innerException) { }
}
