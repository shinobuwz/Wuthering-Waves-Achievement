using Wuwa.Core;

namespace Wuwa.Tests;

[TestClass]
public sealed class OcrPagingTests
{
    [TestMethod]
    public void PagingOptions_RoundTripAndNormalizeUserValues()
    {
        var source = new OcrPagingOptions(
            Method: OcrPagingMethod.Wheel,
            WheelDistance: -3600,
            DragDistance: -620,
            WheelEventIntervalMilliseconds: 140,
            AutoCalibrate: false,
            MinimumSettleMilliseconds: 240,
            MaximumSettleMilliseconds: 3600);

        var settings = new Dictionary<string, string>
        {
            [OcrPagingOptions.SettingKey] = source.ToSettingValue()
        };
        var parsed = OcrPagingOptions.FromSettings(settings);

        Assert.AreEqual(OcrPagingMethod.Wheel, parsed.Method);
        Assert.AreEqual(3600, parsed.WheelDistance);
        Assert.AreEqual(620, parsed.DragDistance);
        Assert.AreEqual(140, parsed.WheelEventIntervalMilliseconds);
        Assert.IsFalse(parsed.AutoCalibrate);
        Assert.AreEqual(240, parsed.MinimumSettleMilliseconds);
        Assert.AreEqual(3600, parsed.MaximumSettleMilliseconds);
    }

    [TestMethod]
    public void PagingOptions_InvalidPersistedValueFallsBackToDefaults()
    {
        var parsed = OcrPagingOptions.FromSettings(new Dictionary<string, string>
        {
            [OcrPagingOptions.SettingKey] = "not-json"
        });

        Assert.AreEqual(OcrPagingMethod.Drag, parsed.Method);
        Assert.AreEqual(OcrPagingOptions.DefaultWheelDistance, parsed.WheelDistance);
        Assert.AreEqual(OcrPagingOptions.DefaultDragDistance, parsed.DragDistance);
    }

    [TestMethod]
    public void FrameAnalysis_RecognizesStableAndTranslatedFrames()
    {
        var before = CreateTexturedFrame(320, 240);
        var identical = Clone(before);
        var translatedUp = TranslateVertically(before, -46);
        var translatedDown = TranslateVertically(translatedUp, 46);

        var stable = OcrFrameAnalysis.MeasureDifference(before, identical, new OcrFrameRegion(0.05, 0.05, 0.95, 0.95));
        var changed = OcrFrameAnalysis.MeasureDifference(before, translatedUp, new OcrFrameRegion(0.05, 0.05, 0.95, 0.95));
        var forward = OcrFrameAnalysis.EstimateVerticalMotion(before, translatedUp, new OcrFrameRegion(0.05, 0.05, 0.95, 0.95), 100);
        var reverse = OcrFrameAnalysis.EstimateVerticalMotion(translatedUp, translatedDown, new OcrFrameRegion(0.05, 0.05, 0.95, 0.95), 100);

        Assert.IsTrue(stable.IsStable);
        Assert.IsFalse(stable.HasVisualChange);
        Assert.IsTrue(changed.HasVisualChange);
        Assert.IsTrue(forward.IsReliable, $"Forward confidence was {forward.Confidence:0.000}, offset {forward.OffsetPixels}.");
        Assert.IsTrue(Math.Abs(forward.OffsetPixels + 46) <= 4, $"Expected about -46, got {forward.OffsetPixels}.");
        Assert.IsTrue(reverse.IsReliable, $"Reverse confidence was {reverse.Confidence:0.000}, offset {reverse.OffsetPixels}.");
        Assert.IsTrue(Math.Abs(reverse.OffsetPixels - 46) <= 4, $"Expected about 46, got {reverse.OffsetPixels}.");
    }

    private static OcrImageFrame CreateTexturedFrame(int width, int height)
    {
        var stride = width * 3;
        var pixels = new byte[stride * height];
        var random = new Random(1729);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var value = (byte)((random.Next(0, 80) + x * 3 + y * 5 + (y / 17 % 2) * 90) % 256);
                var index = y * stride + x * 3;
                pixels[index] = value;
                pixels[index + 1] = (byte)(value ^ 0x5a);
                pixels[index + 2] = (byte)(255 - value);
            }
        }
        return new OcrImageFrame(pixels, width, height, stride);
    }

    private static OcrImageFrame Clone(OcrImageFrame source) =>
        new((byte[])source.BgrPixels.Clone(), source.Width, source.Height, source.Stride);

    private static OcrImageFrame TranslateVertically(OcrImageFrame source, int offset)
    {
        var pixels = new byte[source.BgrPixels.Length];
        for (var y = 0; y < source.Height; y++)
        {
            var sourceY = y - offset;
            if (sourceY < 0 || sourceY >= source.Height) continue;
            Buffer.BlockCopy(
                source.BgrPixels,
                sourceY * source.Stride,
                pixels,
                y * source.Stride,
                source.Stride);
        }
        return new OcrImageFrame(pixels, source.Width, source.Height, source.Stride);
    }
}
