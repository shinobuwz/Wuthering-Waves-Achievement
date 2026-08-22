using Wuwa.Core;

namespace Wuwa.Infrastructure;

public sealed class NativeOcrTextReader : IOcrTextReader, IDisposable
{
    private readonly NativeOcrClient _client;
    private bool _disposed;

    public NativeOcrTextReader(NativeOcrClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public Task<IReadOnlyList<OcrTextLine>> ReadPageAsync(
        OcrImageFrame frame,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(frame);
        frame.Validate();
        return Task.Run<IReadOnlyList<OcrTextLine>>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var nativeLines = _client.DetectAndRecognizeBgr(frame.BgrPixels, frame.Width, frame.Height, frame.Stride);
            NativeOcrDiagnostics.Write($"NativeOcrTextReader frame={frame.Width}x{frame.Height} lines={nativeLines.Count}");
            cancellationToken.ThrowIfCancellationRequested();
            return Array.AsReadOnly(nativeLines.Select(line => new OcrTextLine(
                Array.AsReadOnly(line.Points.Select(point => new OcrPoint(point.X, point.Y)).ToArray()),
                line.Text,
                line.Score)).ToArray());
        }, cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _client.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
