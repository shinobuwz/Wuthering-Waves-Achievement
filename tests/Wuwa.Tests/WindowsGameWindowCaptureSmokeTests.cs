using Wuwa.Infrastructure;

namespace Wuwa.Tests;

[TestClass]
public sealed class WindowsGameWindowCaptureSmokeTests
{
    [TestMethod]
    public async Task CaptureVisibleWindow_WhenExplicitlyRequested_ReturnsValidBgrFrame()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Windows-only capture smoke.");
        var processName = Environment.GetEnvironmentVariable("WUWA_CAPTURE_SMOKE_PROCESS");
        if (string.IsNullOrWhiteSpace(processName)) Assert.Inconclusive("Set WUWA_CAPTURE_SMOKE_PROCESS to a visible process with an 800x600 client area.");

        var capture = new WindowsGameWindowCapture();
        var window = await capture.FindGameWindowAsync([processName!]);
        var frame = await capture.CaptureClientAsync(window);

        frame.Validate();
        Assert.IsTrue(frame.Width >= 800);
        Assert.IsTrue(frame.Height >= 600);
        Assert.IsTrue(frame.BgrPixels.Any(value => value != 0));
    }
}
