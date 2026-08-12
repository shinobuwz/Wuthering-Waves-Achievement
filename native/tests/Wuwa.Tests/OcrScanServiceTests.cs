using Wuwa.Core;

namespace Wuwa.Tests;

[TestClass]
public sealed class OcrScanServiceTests
{
    [TestMethod]
    public async Task ScanAsync_CapturesAndRecognizesOneImmutablePreview()
    {
        var window = new GameWindowCandidate((nint)42, 7, "Wuthering Waves", "鸣潮", 1920, 1080);
        var frame = new OcrImageFrame(new byte[12 * 8], 4, 8, 12);
        var capture = new StubCapture(window, frame);
        var reader = new StubReader([new OcrTextLine(
            [new OcrPoint(1, 2), new OcrPoint(3, 2), new OcrPoint(3, 4), new OcrPoint(1, 4)],
            "测试成就",
            0.95f)]);
        var service = new SinglePageOcrScanService(capture, reader);

        var result = await service.ScanAsync(["Wuthering Waves.exe"], 1920, 1080);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(window, result.Window);
        Assert.AreEqual(1, result.Lines.Count);
        Assert.AreEqual("测试成就", result.Lines[0].Text);
        Assert.AreEqual(1920, capture.ExpectedWidth);
        Assert.AreEqual(1080, capture.ExpectedHeight);
        Assert.AreSame(frame, reader.Frame);
    }

    [TestMethod]
    public async Task ScanAsync_CancellationReturnsNoPartialLines()
    {
        var capture = new StubCapture(
            new GameWindowCandidate((nint)42, 7, "Client-Win64-Shipping", "鸣潮", 1920, 1080),
            new OcrImageFrame(new byte[12 * 8], 4, 8, 12));
        var reader = new StubReader([new OcrTextLine([], "不应返回", 1)]);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await new SinglePageOcrScanService(capture, reader).ScanAsync(["Client-Win64-Shipping"], cancellationToken: cancellation.Token);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(OcrScanErrorCode.Cancelled, result.Error?.Code);
        Assert.AreEqual(0, result.Lines.Count);
        Assert.IsNull(reader.Frame);
    }

    [TestMethod]
    public async Task ScanAsync_WindowAndCaptureFailuresAreStructured()
    {
        var reader = new StubReader([]);
        var missing = await new SinglePageOcrScanService(new ThrowingCapture(findFailure: true), reader)
            .ScanAsync(["Wuthering Waves"]);
        var failedCapture = await new SinglePageOcrScanService(new ThrowingCapture(findFailure: false), reader)
            .ScanAsync(["Wuthering Waves"]);

        Assert.AreEqual(OcrScanErrorCode.WindowNotFound, missing.Error?.Code);
        Assert.AreEqual(OcrScanErrorCode.CaptureFailed, failedCapture.Error?.Code);
        Assert.AreEqual(0, missing.Lines.Count);
        Assert.AreEqual(0, failedCapture.Lines.Count);
    }

    private sealed class StubCapture(GameWindowCandidate window, OcrImageFrame frame) : IGameWindowCapture
    {
        public int? ExpectedWidth { get; private set; }
        public int? ExpectedHeight { get; private set; }

        public Task<GameWindowCandidate> FindGameWindowAsync(IReadOnlyCollection<string> processNames, int minimumWidth = 800, int minimumHeight = 600, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(window);
        }

        public Task<OcrImageFrame> CaptureClientAsync(GameWindowCandidate selected, int? expectedWidth = null, int? expectedHeight = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ExpectedWidth = expectedWidth;
            ExpectedHeight = expectedHeight;
            return Task.FromResult(frame);
        }
    }

    private sealed class StubReader(IReadOnlyList<OcrTextLine> lines) : IOcrTextReader
    {
        public OcrImageFrame? Frame { get; private set; }

        public Task<IReadOnlyList<OcrTextLine>> ReadPageAsync(OcrImageFrame frame, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Frame = frame;
            return Task.FromResult(lines);
        }
    }

    private sealed class ThrowingCapture(bool findFailure) : IGameWindowCapture
    {
        public Task<GameWindowCandidate> FindGameWindowAsync(IReadOnlyCollection<string> processNames, int minimumWidth = 800, int minimumHeight = 600, CancellationToken cancellationToken = default) =>
            findFailure
                ? Task.FromException<GameWindowCandidate>(new GameWindowNotFoundException("missing"))
                : Task.FromResult(new GameWindowCandidate((nint)42, 7, "Wuthering Waves", "鸣潮", 1920, 1080));

        public Task<OcrImageFrame> CaptureClientAsync(GameWindowCandidate window, int? expectedWidth = null, int? expectedHeight = null, CancellationToken cancellationToken = default) =>
            Task.FromException<OcrImageFrame>(new GameWindowCaptureException("capture failed"));
    }
}
