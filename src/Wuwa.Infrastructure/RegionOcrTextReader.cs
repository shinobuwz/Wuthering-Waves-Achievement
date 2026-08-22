using Wuwa.Core;

namespace Wuwa.Infrastructure;

/// <summary>Runs an OCR reader against a client-area crop and restores full-frame coordinates.</summary>
public sealed class RegionOcrTextReader : IOcrTextReader
{
    private readonly IOcrTextReader _inner;
    private readonly double _x1Ratio;
    private readonly double _y1Ratio;
    private readonly double _x2Ratio;
    private readonly double _y2Ratio;

    public RegionOcrTextReader(
        IOcrTextReader inner,
        double x1Ratio,
        double y1Ratio,
        double x2Ratio,
        double y2Ratio)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        if (x1Ratio < 0 || y1Ratio < 0 || x2Ratio <= x1Ratio || y2Ratio <= y1Ratio || x2Ratio > 1 || y2Ratio > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(x1Ratio), "The OCR crop ratios must define a non-empty region inside the frame.");
        }

        _x1Ratio = x1Ratio;
        _y1Ratio = y1Ratio;
        _x2Ratio = x2Ratio;
        _y2Ratio = y2Ratio;
    }

    public async Task<IReadOnlyList<OcrTextLine>> ReadPageAsync(
        OcrImageFrame frame,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        frame.Validate();
        cancellationToken.ThrowIfCancellationRequested();

        var x1 = Math.Clamp((int)Math.Floor(frame.Width * _x1Ratio), 0, frame.Width - 1);
        var y1 = Math.Clamp((int)Math.Floor(frame.Height * _y1Ratio), 0, frame.Height - 1);
        var x2 = Math.Clamp((int)Math.Ceiling(frame.Width * _x2Ratio), x1 + 1, frame.Width);
        var y2 = Math.Clamp((int)Math.Ceiling(frame.Height * _y2Ratio), y1 + 1, frame.Height);
        var width = x2 - x1;
        var height = y2 - y1;
        var stride = checked(width * 3);
        var pixels = new byte[checked(stride * height)];

        for (var row = 0; row < height; row++)
        {
            var sourceOffset = checked((y1 + row) * frame.Stride + x1 * 3);
            var destinationOffset = row * stride;
            Buffer.BlockCopy(frame.BgrPixels, sourceOffset, pixels, destinationOffset, stride);
        }

        NativeOcrDiagnostics.Write($"OCR region crop source={frame.Width}x{frame.Height} region={x1},{y1}-{x2},{y2} crop={width}x{height}");
        var lines = await _inner.ReadPageAsync(new OcrImageFrame(pixels, width, height, stride), cancellationToken).ConfigureAwait(false);
        return lines
            .Select(line => new OcrTextLine(
                line.Points.Select(point => new OcrPoint(point.X + x1, point.Y + y1)).ToArray(),
                line.Text,
                line.Score))
            .ToArray();
    }
}
