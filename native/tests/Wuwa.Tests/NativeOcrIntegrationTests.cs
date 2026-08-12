using Wuwa.Infrastructure;

namespace Wuwa.Tests;

[TestClass]
public sealed class NativeOcrIntegrationTests
{
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
        var page = client.DetectAndRecognizeBgr(image, 160, 48, 160 * 3);

        Assert.IsTrue(float.IsFinite(result.Score));
        Assert.IsNotNull(result.Text);
        Assert.AreEqual(0, page.Count);
    }
}
