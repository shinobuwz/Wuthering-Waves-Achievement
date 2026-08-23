using System.Text.Json;

namespace Wuwa.Core;

public sealed record OcrPagingOptions(
    int WheelDistance = 2400,
    int SecondaryWheelDistance = 640,
    int WheelEventIntervalMilliseconds = 100,
    bool AutoCalibrate = true,
    int MinimumSettleMilliseconds = 150,
    int MaximumSettleMilliseconds = 2800,
    int? CalibratedWidth = null,
    int? CalibratedHeight = null,
    int? LastForwardPixels = null,
    int? LastReversePixels = null,
    int? LastSecondaryPixels = null,
    DateTimeOffset? CalibratedAtUtc = null)
{
    public const string SettingKey = "ocr.paging";
    // Historical native wheel values were -160 × 15 for achievement pages and
    // -160 × 4 for the narrower secondary-Tag list. Keep those proven totals as defaults.
    public const int DefaultWheelDistance = 2400;
    public const int DefaultSecondaryWheelDistance = 640;
    public const int MaximumSecondaryWheelDistance = 8000;
    public const int DefaultWheelEventIntervalMilliseconds = 100;
    public const int DefaultMinimumSettleMilliseconds = 150;
    public const int DefaultMaximumSettleMilliseconds = 2800;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static OcrPagingOptions FromSettings(IReadOnlyDictionary<string, string> settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.TryGetValue(SettingKey, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return new OcrPagingOptions();
        }

        try
        {
            return (JsonSerializer.Deserialize<OcrPagingOptions>(value, JsonOptions) ?? new OcrPagingOptions()).Normalize();
        }
        catch (JsonException)
        {
            return new OcrPagingOptions();
        }
    }

    public string ToSettingValue() => JsonSerializer.Serialize(Normalize(), JsonOptions);

    public OcrPagingOptions Normalize() => this with
    {
        WheelDistance = Math.Clamp(Math.Abs(WheelDistance), 120, 12000),
        SecondaryWheelDistance = Math.Clamp(Math.Abs(SecondaryWheelDistance), 120, MaximumSecondaryWheelDistance),
        WheelEventIntervalMilliseconds = Math.Clamp(WheelEventIntervalMilliseconds, 20, 300),
        MinimumSettleMilliseconds = Math.Clamp(MinimumSettleMilliseconds, 50, 1500),
        MaximumSettleMilliseconds = Math.Clamp(
            MaximumSettleMilliseconds,
            Math.Max(800, Math.Clamp(MinimumSettleMilliseconds, 50, 1500) + 300),
            6000)
    };

    public bool NeedsCalibration(int width, int height) =>
        AutoCalibrate &&
        (CalibratedAtUtc is null || CalibratedWidth != width || CalibratedHeight != height);

    public OcrPagingOptions ClearCalibration() => this with
    {
        CalibratedWidth = null,
        CalibratedHeight = null,
        LastForwardPixels = null,
        LastReversePixels = null,
        LastSecondaryPixels = null,
        CalibratedAtUtc = null
    };
}

public readonly record struct OcrFrameRegion(double Left, double Top, double Right, double Bottom)
{
    public static OcrFrameRegion AchievementList { get; } = new(0.38, 0.20, 0.94, 0.96);
    public static OcrFrameRegion SecondaryNavigation { get; } = new(0.10, 0.18, 0.35, 0.95);

    public void Validate()
    {
        if (Left < 0 || Top < 0 || Right > 1 || Bottom > 1 || Left >= Right || Top >= Bottom)
        {
            throw new ArgumentOutOfRangeException(nameof(OcrFrameRegion), "OCR frame region ratios must form a non-empty rectangle inside the frame.");
        }
    }
}

public sealed record OcrFrameDifference(double MeanAbsoluteDifference, double ChangedSampleRatio)
{
    public bool IsStable => MeanAbsoluteDifference <= 3.0 && ChangedSampleRatio <= 0.045;
    public bool HasVisualChange => MeanAbsoluteDifference >= 4.0 || ChangedSampleRatio >= 0.08;
}

public sealed record OcrVerticalMotion(
    int OffsetPixels,
    double Confidence,
    double BestScore,
    double ZeroOffsetScore)
{
    public bool IsReliable => OffsetPixels != 0 && Confidence >= 0.08;
}

public static class OcrFrameAnalysis
{
    public static OcrFrameDifference MeasureDifference(
        OcrImageFrame first,
        OcrImageFrame second,
        OcrFrameRegion region)
    {
        ValidateComparable(first, second, region);
        var bounds = GetBounds(first, region);
        const int sampleStep = 4;
        long totalDifference = 0;
        var changed = 0;
        var samples = 0;
        for (var y = bounds.Top; y < bounds.Bottom; y += sampleStep)
        {
            for (var x = bounds.Left; x < bounds.Right; x += sampleStep)
            {
                var difference = Math.Abs(Luminance(first, x, y) - Luminance(second, x, y));
                totalDifference += difference;
                if (difference >= 12) changed++;
                samples++;
            }
        }

        return samples == 0
            ? new OcrFrameDifference(0, 0)
            : new OcrFrameDifference(totalDifference / (double)samples, changed / (double)samples);
    }

    public static OcrVerticalMotion EstimateVerticalMotion(
        OcrImageFrame before,
        OcrImageFrame after,
        OcrFrameRegion region,
        int maximumOffsetPixels)
    {
        ValidateComparable(before, after, region);
        if (maximumOffsetPixels <= 0) throw new ArgumentOutOfRangeException(nameof(maximumOffsetPixels));

        var bounds = GetBounds(before, region);
        const int sampleStepX = 12;
        const int sampleStepY = 4;
        var sampleWidth = Math.Max(1, (bounds.Right - bounds.Left) / sampleStepX);
        var sampleHeight = Math.Max(1, (bounds.Bottom - bounds.Top) / sampleStepY);
        if (sampleWidth < 4 || sampleHeight < 12)
        {
            return new OcrVerticalMotion(0, 0, double.MaxValue, double.MaxValue);
        }

        var beforeEdges = BuildEdgeMap(before, bounds, sampleWidth, sampleHeight);
        var afterEdges = BuildEdgeMap(after, bounds, sampleWidth, sampleHeight);
        var maximumShift = Math.Min(maximumOffsetPixels / sampleStepY, sampleHeight * 3 / 4);
        if (maximumShift < 1)
        {
            return new OcrVerticalMotion(0, 0, double.MaxValue, double.MaxValue);
        }

        var zeroScore = ScoreShift(beforeEdges, afterEdges, sampleWidth, sampleHeight, 0);
        var bestShift = 0;
        var bestScore = zeroScore;
        for (var shift = -maximumShift; shift <= maximumShift; shift++)
        {
            if (shift == 0) continue;
            var score = ScoreShift(beforeEdges, afterEdges, sampleWidth, sampleHeight, shift);
            if (score < bestScore)
            {
                bestScore = score;
                bestShift = shift;
            }
        }

        var confidence = zeroScore <= 0.001
            ? 0
            : Math.Clamp((zeroScore - bestScore) / zeroScore, 0, 1);
        if (Math.Abs(bestShift) <= 1 || confidence < 0.08)
        {
            bestShift = 0;
        }

        return new OcrVerticalMotion(bestShift * sampleStepY, confidence, bestScore, zeroScore);
    }

    private static byte[] BuildEdgeMap(
        OcrImageFrame frame,
        FrameBounds bounds,
        int sampleWidth,
        int sampleHeight)
    {
        var luminance = new byte[sampleWidth * sampleHeight];
        for (var y = 0; y < sampleHeight; y++)
        {
            var sourceY = Math.Min(bounds.Bottom - 1, bounds.Top + y * (bounds.Bottom - bounds.Top) / sampleHeight);
            for (var x = 0; x < sampleWidth; x++)
            {
                var sourceX = Math.Min(bounds.Right - 1, bounds.Left + x * (bounds.Right - bounds.Left) / sampleWidth);
                luminance[y * sampleWidth + x] = (byte)Luminance(frame, sourceX, sourceY);
            }
        }

        var edges = new byte[luminance.Length];
        for (var y = 1; y < sampleHeight - 1; y++)
        {
            for (var x = 1; x < sampleWidth - 1; x++)
            {
                var index = y * sampleWidth + x;
                var horizontal = Math.Abs(luminance[index + 1] - luminance[index - 1]);
                var vertical = Math.Abs(luminance[index + sampleWidth] - luminance[index - sampleWidth]);
                edges[index] = (byte)Math.Min(255, horizontal + vertical);
            }
        }
        return edges;
    }

    private static double ScoreShift(
        IReadOnlyList<byte> before,
        IReadOnlyList<byte> after,
        int width,
        int height,
        int shift)
    {
        var afterStart = Math.Max(1, shift + 1);
        var afterEnd = Math.Min(height - 1, height + shift - 1);
        if (afterEnd - afterStart < height / 4) return double.MaxValue;

        long difference = 0;
        var informative = 0;
        for (var afterY = afterStart; afterY < afterEnd; afterY++)
        {
            var beforeY = afterY - shift;
            for (var x = 1; x < width - 1; x++)
            {
                var beforeValue = before[beforeY * width + x];
                var afterValue = after[afterY * width + x];
                if (beforeValue < 8 && afterValue < 8) continue;
                difference += Math.Abs(beforeValue - afterValue);
                informative++;
            }
        }

        if (informative < width * 4) return double.MaxValue;
        var score = difference / (double)informative;
        return score * (1 + Math.Abs(shift) / (double)height * 0.015);
    }

    private static void ValidateComparable(OcrImageFrame first, OcrImageFrame second, OcrFrameRegion region)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        first.Validate();
        second.Validate();
        region.Validate();
        if (first.Width != second.Width || first.Height != second.Height)
        {
            throw new ArgumentException("OCR frames must have matching dimensions.");
        }
    }

    private static FrameBounds GetBounds(OcrImageFrame frame, OcrFrameRegion region)
    {
        var left = Math.Clamp((int)Math.Floor(frame.Width * region.Left), 0, frame.Width - 1);
        var top = Math.Clamp((int)Math.Floor(frame.Height * region.Top), 0, frame.Height - 1);
        var right = Math.Clamp((int)Math.Ceiling(frame.Width * region.Right), left + 1, frame.Width);
        var bottom = Math.Clamp((int)Math.Ceiling(frame.Height * region.Bottom), top + 1, frame.Height);
        return new FrameBounds(left, top, right, bottom);
    }

    private static int Luminance(OcrImageFrame frame, int x, int y)
    {
        var index = checked(y * frame.Stride + x * 3);
        var blue = frame.BgrPixels[index];
        var green = frame.BgrPixels[index + 1];
        var red = frame.BgrPixels[index + 2];
        return (blue * 29 + green * 150 + red * 77) >> 8;
    }

    private readonly record struct FrameBounds(int Left, int Top, int Right, int Bottom);
}
