using Wuwa.Infrastructure;

namespace Wuwa.Tests;

[TestClass]
public sealed class NativeOcrIntegrationTests
{
    [TestMethod]
    public void SearchResultFixture_DetectionModelFindsAndRecognizesAchievementName()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Native OCR currently supports Windows only.");
        var nativeRoot = Environment.GetEnvironmentVariable("WUWA_NATIVE_OCR_ROOT");
        var modelRoot = Environment.GetEnvironmentVariable("WUWA_NATIVE_OCR_MODEL_ROOT");
        if (string.IsNullOrWhiteSpace(nativeRoot) || string.IsNullOrWhiteSpace(modelRoot))
        {
            Assert.Inconclusive("Build native OCR and set WUWA_NATIVE_OCR_ROOT/WUWA_NATIVE_OCR_MODEL_ROOT to run this fixture regression.");
        }

        const int width = 503;
        const int height = 40;
        var pixels = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "ocr", "search-result-name-region-503x40.bgr"));
        using var client = new NativeOcrClient(new NativeOcrOptions(
            Path.Combine(modelRoot!, "rec", "rec.onnx"),
            Path.Combine(modelRoot!, "ppocrv5_dict.txt")));
        client.EnableDetection(Path.Combine(modelRoot!, "det", "det.onnx"));

        var lines = client.DetectAndRecognizeBgr(pixels, width, height, width * 3);

        Assert.AreEqual(1, lines.Count);
        Assert.AreEqual("打上花火", lines[0].Text);
        Assert.IsTrue(lines[0].Score > 0.95f, $"Unexpected recognition score: {lines[0].Score:F3}");
        var detectedWidth = lines[0].Points.Max(point => point.X) - lines[0].Points.Min(point => point.X);
        Assert.IsTrue(detectedWidth < 200, $"Detection box still includes the separator: {detectedWidth:F1}px");
    }

    [TestMethod]
    public void RecognitionSmoke_CrossesManagedAbiAndRealOnnxRuntimeSession()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Native OCR currently supports Windows only.");
        var nativeRoot = Environment.GetEnvironmentVariable("WUWA_NATIVE_OCR_ROOT");
        var modelRoot = Environment.GetEnvironmentVariable("WUWA_NATIVE_OCR_MODEL_ROOT");
        if (string.IsNullOrWhiteSpace(nativeRoot) || string.IsNullOrWhiteSpace(modelRoot))
        {
            Assert.Inconclusive("Build native OCR and set WUWA_NATIVE_OCR_ROOT/WUWA_NATIVE_OCR_MODEL_ROOT to run this integration smoke.");
        }

        Assert.AreEqual(1U, NativeOcrClient.AbiVersion);
        Assert.IsFalse(string.IsNullOrWhiteSpace(NativeOcrClient.Version));
        using var client = new NativeOcrClient(new NativeOcrOptions(
            Path.Combine(modelRoot!, "rec", "rec.onnx"),
            Path.Combine(modelRoot!, "ppocrv5_dict.txt"),
            RecognitionMinWidth: 320,
            RecognitionMaxWidth: 320));
        var image = Enumerable.Repeat((byte)255, 48 * 160 * 3).ToArray();
        var result = client.RecognizeBgr(image, 160, 48, 160 * 3);
        client.EnableDetection(Path.Combine(modelRoot!, "det", "det.onnx"));
        client.EnableClassifier(Path.Combine(modelRoot!, "cls", "cls.onnx"));
        var page = client.DetectAndRecognizeBgr(image, 160, 48, 160 * 3);

        Assert.IsTrue(float.IsFinite(result.Score));
        Assert.IsNotNull(result.Text);
        Assert.AreEqual(0, page.Count);
    }
}
