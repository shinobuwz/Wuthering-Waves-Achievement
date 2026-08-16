using Wuwa.Core;

namespace Wuwa.Infrastructure;

/// <summary>
/// Matches the Python OCR pipeline: locate achievement star icons first, then
/// recognize the name/status crops at fixed offsets from each icon.
/// </summary>
public sealed class NativeOcrTemplateTextReader : IOcrTextReader
{
    private const int NameDx = 122;
    private const int NameDy = -39;
    private const int NameWidth = 503;
    private const int NameHeight = 40;
    private const int StatusDx = 878;
    private const int StatusDy = 15;
    private const int StatusWidth = 163;
    private const int StatusHeight = 47;

    private readonly NativeOcrClient _client;
    private readonly string _templateDirectory;

    public NativeOcrTemplateTextReader(NativeOcrClient client, string templateDirectory)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _templateDirectory = string.IsNullOrWhiteSpace(templateDirectory)
            ? throw new ArgumentException("Template directory is required.", nameof(templateDirectory))
            : Path.GetFullPath(templateDirectory);
    }

    public Task<IReadOnlyList<OcrTextLine>> ReadPageAsync(
        OcrImageFrame frame,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        frame.Validate();
        return Task.Run<IReadOnlyList<OcrTextLine>>(() => ReadPage(frame, cancellationToken), cancellationToken);
    }

    private IReadOnlyList<OcrTextLine> ReadPage(OcrImageFrame frame, CancellationToken cancellationToken)
    {
        var icons = _client.FindAchievementIcons(
            frame.BgrPixels,
            frame.Width,
            frame.Height,
            frame.Stride,
            _templateDirectory);
        NativeOcrDiagnostics.Write($"TemplateReader icons={icons.Count} templateDir={_templateDirectory}");

        var lines = new List<OcrTextLine>(icons.Count * 2);
        foreach (var icon in icons)
        {
            cancellationToken.ThrowIfCancellationRequested();
            NativeOcrDiagnostics.Write($"TemplateReader icon x={icon.X} y={icon.Y} label={icon.Label} confidence={icon.Confidence:F3}");
            AddRecognizedCrop(lines, frame, icon.X, icon.Y, NameDx, NameDy, NameWidth, NameHeight, "name");
            AddRecognizedCrop(lines, frame, icon.X, icon.Y, StatusDx, StatusDy, StatusWidth, StatusHeight, "status");
        }

        NativeOcrDiagnostics.Write($"TemplateReader lines={lines.Count}");
        return lines;
    }

    private void AddRecognizedCrop(
        ICollection<OcrTextLine> lines,
        OcrImageFrame frame,
        int iconX,
        int iconY,
        int offsetX,
        int offsetY,
        int cropWidth,
        int cropHeight,
        string cropKind)
    {
        var crop = Crop(frame, iconX + offsetX, iconY + offsetY, cropWidth, cropHeight, out var x, out var y, out var actualWidth, out var actualHeight);
        if (crop is null) return;

        var result = _client.RecognizeBgrClahe(crop, actualWidth, actualHeight, actualWidth * 3);
        NativeOcrDiagnostics.Write($"TemplateReader crop={cropKind} x={x} y={y} size={actualWidth}x{actualHeight} text={result.Text} score={result.Score:F3}");
        if (string.IsNullOrWhiteSpace(result.Text)) return;
        lines.Add(new OcrTextLine(
            [
                new OcrPoint(x, y),
                new OcrPoint(x + actualWidth, y),
                new OcrPoint(x + actualWidth, y + actualHeight),
                new OcrPoint(x, y + actualHeight)
            ],
            result.Text.Trim(),
            result.Score));
    }

    private static byte[]? Crop(
        OcrImageFrame frame,
        int requestedX,
        int requestedY,
        int requestedWidth,
        int requestedHeight,
        out int actualX,
        out int actualY,
        out int actualWidth,
        out int actualHeight)
    {
        actualX = Math.Max(0, requestedX);
        actualY = Math.Max(0, requestedY);
        var right = Math.Min(frame.Width, actualX + requestedWidth);
        var bottom = Math.Min(frame.Height, actualY + requestedHeight);
        actualWidth = right - actualX;
        actualHeight = bottom - actualY;
        if (actualWidth < 20 || actualHeight < 10)
        {
            return null;
        }

        var pixels = new byte[checked(actualWidth * actualHeight * 3)];
        for (var row = 0; row < actualHeight; row++)
        {
            Buffer.BlockCopy(
                frame.BgrPixels,
                checked((actualY + row) * frame.Stride + actualX * 3),
                pixels,
                row * actualWidth * 3,
                actualWidth * 3);
        }
        return pixels;
    }
}
