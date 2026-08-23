using Wuwa.Core;

namespace Wuwa.Infrastructure;

/// <summary>
/// Matches the Native OCR pipeline: locate achievement star icons first, use
/// PP-OCR detection for each name region, then recognize the fixed status crop.
/// </summary>
public sealed class NativeOcrTemplateTextReader : IOcrTextReader
{
    private const int NameDx = 122;
    private const int NameDy = -39;
    private const int NameWidth = 503;
    private const int NameHeight = 40;
    private const int DescriptionDx = 122;
    private const int DescriptionDy = 3;
    private const int DescriptionWidth = 700;
    private const int DescriptionHeight = 40;
    private const int StatusDx = 878;
    private const int StatusDy = 15;
    private const int StatusWidth = 163;
    private const int StatusHeight = 47;

    private readonly NativeOcrClient _client;
    private readonly string _templateDirectory;

    public NativeOcrTemplateTextReader(NativeOcrClient client, string templateDirectory, string detectionModelPath)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _templateDirectory = string.IsNullOrWhiteSpace(templateDirectory)
            ? throw new ArgumentException("Template directory is required.", nameof(templateDirectory))
            : Path.GetFullPath(templateDirectory);
        if (string.IsNullOrWhiteSpace(detectionModelPath))
        {
            throw new ArgumentException("Detection model path is required.", nameof(detectionModelPath));
        }
        _client.EnableDetection(Path.GetFullPath(detectionModelPath));
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

        var lines = new List<OcrTextLine>(icons.Count * 3);
        foreach (var icon in icons)
        {
            cancellationToken.ThrowIfCancellationRequested();
            NativeOcrDiagnostics.Write($"TemplateReader icon x={icon.X} y={icon.Y} label={icon.Label} confidence={icon.Confidence:F3}");
            var nameLine = AddDetectedNameCrop(lines, frame, icon.X, icon.Y);
            if (nameLine is not null && BuiltInAchievementRules.IsWangRiJinzhouOcrName(nameLine.Text))
            {
                NativeOcrDiagnostics.Write($"TemplateReader special-name text={nameLine.Text}");
                AddRecognizedCrop(lines, frame, icon.X, icon.Y, DescriptionDx, DescriptionDy, DescriptionWidth, DescriptionHeight, "description", OcrTextKind.AchievementDescription);
            }
            AddRecognizedCrop(lines, frame, icon.X, icon.Y, StatusDx, StatusDy, StatusWidth, StatusHeight, "status", OcrTextKind.AchievementStatus);
        }

        NativeOcrDiagnostics.Write($"TemplateReader lines={lines.Count}");
        return lines;
    }

    private OcrTextLine? AddDetectedNameCrop(
        ICollection<OcrTextLine> lines,
        OcrImageFrame frame,
        int iconX,
        int iconY)
    {
        var crop = Crop(frame, iconX + NameDx, iconY + NameDy, NameWidth, NameHeight, out var x, out var y, out var actualWidth, out var actualHeight);
        if (crop is null) return null;

        var detected = _client.DetectAndRecognizeBgr(crop, actualWidth, actualHeight, actualWidth * 3)
            .Where(result => !string.IsNullOrWhiteSpace(result.Text) && result.Points.Count > 0)
            .ToArray();
        var selected = detected
            .OrderByDescending(result => Math.Max(1, result.Text.Length) * Math.Max(0.0f, result.Score))
            .ThenBy(result => result.Points.Min(point => point.X))
            .FirstOrDefault();
        if (selected is null)
        {
            NativeOcrDiagnostics.Write($"TemplateReader crop=name preprocess=det+color x={x} y={y} size={actualWidth}x{actualHeight} lines=0");
            return null;
        }

        var text = selected.Text.Trim();
        var score = selected.Score;
        var left = x + selected.Points.Min(point => point.X);
        var top = y + selected.Points.Min(point => point.Y);
        var right = x + selected.Points.Max(point => point.X);
        var bottom = y + selected.Points.Max(point => point.Y);
        NativeOcrDiagnostics.Write($"TemplateReader crop=name preprocess=det+color x={x} y={y} size={actualWidth}x{actualHeight} detected={detected.Length} box={left:F0},{top:F0}-{right:F0},{bottom:F0} text={text} score={score:F3}");

        var line = new OcrTextLine(
            [
                new OcrPoint(left, top),
                new OcrPoint(right, top),
                new OcrPoint(right, bottom),
                new OcrPoint(left, bottom)
            ],
            text,
            score,
            OcrTextKind.AchievementName);
        lines.Add(line);
        return line;
    }

    private OcrTextLine? AddRecognizedCrop(
        ICollection<OcrTextLine> lines,
        OcrImageFrame frame,
        int iconX,
        int iconY,
        int offsetX,
        int offsetY,
        int cropWidth,
        int cropHeight,
        string cropKind,
        OcrTextKind textKind)
    {
        var crop = Crop(frame, iconX + offsetX, iconY + offsetY, cropWidth, cropHeight, out var x, out var y, out var actualWidth, out var actualHeight);
        if (crop is null) return null;

        var result = _client.RecognizeBgrClahe(crop, actualWidth, actualHeight, actualWidth * 3);
        NativeOcrDiagnostics.Write($"TemplateReader crop={cropKind} preprocess=clahe x={x} y={y} size={actualWidth}x{actualHeight} text={result.Text} score={result.Score:F3}");
        if (string.IsNullOrWhiteSpace(result.Text)) return null;
        var line = new OcrTextLine(
            [
                new OcrPoint(x, y),
                new OcrPoint(x + actualWidth, y),
                new OcrPoint(x + actualWidth, y + actualHeight),
                new OcrPoint(x, y + actualHeight)
            ],
            result.Text.Trim(),
            result.Score,
            textKind);
        lines.Add(line);
        return line;
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
