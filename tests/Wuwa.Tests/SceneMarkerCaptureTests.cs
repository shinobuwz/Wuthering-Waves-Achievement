using System.Buffers.Binary;
using System.IO.Compression;
using System.Text.Json;
using Wuwa.Core;
using Wuwa.Infrastructure;

namespace Wuwa.Tests;

[TestClass]
public sealed class SceneMarkerCaptureTests
{
    [TestMethod]
    public void MapDisplaySelection_MapsScaledCoordinatesToSourcePixels()
    {
        var region = SceneMarkerFrameTools.MapDisplaySelection(
            100,
            50,
            300,
            150,
            displayWidth: 960,
            displayHeight: 540,
            sourceWidth: 1920,
            sourceHeight: 1080);

        Assert.AreEqual(new SceneMarkerPixelRegion(200, 100, 400, 200), region);
    }

    [TestMethod]
    public void MapDisplaySelection_NormalizesReverseDragAndClampsToFrame()
    {
        var region = SceneMarkerFrameTools.MapDisplaySelection(
            1200,
            700,
            -50,
            -20,
            displayWidth: 960,
            displayHeight: 540,
            sourceWidth: 1920,
            sourceHeight: 1080);

        Assert.AreEqual(new SceneMarkerPixelRegion(0, 0, 1920, 1080), region);
    }

    [TestMethod]
    public void MapDisplaySelection_RoundsOutwardSoSelectedPixelsAreNotDropped()
    {
        var region = SceneMarkerFrameTools.MapDisplaySelection(
            0.25,
            0.25,
            1.25,
            1.25,
            displayWidth: 2,
            displayHeight: 2,
            sourceWidth: 4,
            sourceHeight: 4);

        Assert.AreEqual(new SceneMarkerPixelRegion(0, 0, 3, 3), region);
        Assert.IsTrue(SceneMarkerFrameTools.IsLargeEnough(region));
        Assert.IsFalse(SceneMarkerFrameTools.IsLargeEnough(new SceneMarkerPixelRegion(0, 0, 2, 3)));
    }

    [TestMethod]
    public void Normalize_ProducesFrameRelativeRegion()
    {
        var normalized = SceneMarkerFrameTools.Normalize(
            new SceneMarkerPixelRegion(480, 270, 960, 540),
            1920,
            1080);

        Assert.AreEqual(0.25, normalized.Left, 0.000001);
        Assert.AreEqual(0.25, normalized.Top, 0.000001);
        Assert.AreEqual(0.5, normalized.Width, 0.000001);
        Assert.AreEqual(0.5, normalized.Height, 0.000001);
    }

    [TestMethod]
    public void Crop_CopiesOnlySelectedBgrPixelsAndRemovesSourcePadding()
    {
        var pixels = new byte[]
        {
            1, 2, 3, 4, 5, 6, 7, 8, 9, 90, 91, 92,
            11, 12, 13, 14, 15, 16, 17, 18, 19, 93, 94, 95
        };
        var frame = new OcrImageFrame(pixels, Width: 3, Height: 2, Stride: 12);

        var crop = SceneMarkerFrameTools.Crop(frame, new SceneMarkerPixelRegion(1, 0, 2, 2));

        Assert.AreEqual(2, crop.Width);
        Assert.AreEqual(2, crop.Height);
        Assert.AreEqual(6, crop.Stride);
        CollectionAssert.AreEqual(
            new byte[] { 4, 5, 6, 7, 8, 9, 14, 15, 16, 17, 18, 19 },
            crop.BgrPixels);
    }

    [DataTestMethod]
    [DataRow("achievement-list")]
    [DataRow("scene.1_marker")]
    [DataRow("0-loading")]
    public void Identifier_AcceptsStableLowercaseIds(string value)
    {
        var valid = SceneMarkerIdentifier.TryValidate(value, out var identifier, out var error);

        Assert.IsTrue(valid);
        Assert.AreEqual(value, identifier);
        Assert.IsNull(error);
    }

    [DataTestMethod]
    [DataRow("")]
    [DataRow("Achievement-list")]
    [DataRow("-loading")]
    [DataRow("场景")]
    [DataRow("scene marker")]
    public void Identifier_RejectsUnsafeOrUnstableIds(string value)
    {
        Assert.IsFalse(SceneMarkerIdentifier.TryValidate(value, out _, out var error));
        Assert.IsFalse(string.IsNullOrWhiteSpace(error));
    }

    [DataTestMethod]
    [DataRow("1")]
    [DataRow(" TRUE ")]
    [DataRow("yes")]
    [DataRow("On")]
    public void LabSettings_EnableReleaseOnlyForExplicitTruthyValues(string configuredValue)
    {
        Assert.IsTrue(SceneMarkerLabSettings.IsEnabled(isDebugBuild: false, configuredValue));
        Assert.IsTrue(SceneMarkerLabSettings.IsEnabled(isDebugBuild: true, configuredValue: null));
    }

    [DataTestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("0")]
    [DataRow("false")]
    [DataRow("anything")]
    public void LabSettings_KeepReleaseEntryHiddenWithoutExplicitOptIn(string? configuredValue)
    {
        Assert.IsFalse(SceneMarkerLabSettings.IsEnabled(isDebugBuild: false, configuredValue));
    }

    [TestMethod]
    public void Storage_PrepareDirectoryReportsAFilePathAsUnwritable()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var filePath = Path.Combine(root, "not-a-directory");
            File.WriteAllText(filePath, "occupied");

            Assert.IsFalse(SceneMarkerStorage.TryPrepareDirectory(filePath, out var error));
            Assert.IsFalse(string.IsNullOrWhiteSpace(error));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void Storage_PrepareSceneDirectoryReportsAnOccupiedScenePath()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            File.WriteAllText(Path.Combine(root, "achievement-list"), "occupied");

            Assert.IsFalse(SceneMarkerStorage.TryPrepareSceneDirectory(
                root,
                "achievement-list",
                out _,
                out var error));
            Assert.IsFalse(string.IsNullOrWhiteSpace(error));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task Storage_SavesUniquePngAndVersionedJsonBelowSceneDirectory()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var source = CreateFrame();
            var region = new SceneMarkerPixelRegion(1, 1, 2, 2);
            var request = CreateSaveRequest(source, region);
            var storage = new SceneMarkerStorage();

            var first = await storage.SaveAsync(root, request);
            var second = await storage.SaveAsync(root, request);

            Assert.IsTrue(File.Exists(first.ImagePath));
            Assert.IsTrue(File.Exists(first.MetadataPath));
            Assert.AreNotEqual(first.ImagePath, second.ImagePath);
            Assert.AreEqual(Path.Combine(root, "achievement-list"), Path.GetDirectoryName(first.ImagePath));
            CollectionAssert.AreEqual(request.MarkerPng.ToArray(), await File.ReadAllBytesAsync(first.ImagePath));

            using var json = JsonDocument.Parse(await File.ReadAllTextAsync(first.MetadataPath));
            var rootElement = json.RootElement;
            Assert.AreEqual(1, rootElement.GetProperty("schemaVersion").GetInt32());
            Assert.AreEqual("achievement-list", rootElement.GetProperty("sceneId").GetString());
            Assert.AreEqual("title-anchor", rootElement.GetProperty("markerName").GetString());
            Assert.AreEqual(Path.GetFileName(first.ImagePath), rootElement.GetProperty("imageFile").GetString());
            Assert.AreEqual(1, rootElement.GetProperty("pixelRegion").GetProperty("x").GetInt32());
            Assert.AreEqual(0.25, rootElement.GetProperty("normalizedRegion").GetProperty("left").GetDouble(), 0.000001);
            Assert.AreEqual(64, rootElement.GetProperty("sourceFrameSha256").GetString()!.Length);
            Assert.AreEqual(64, rootElement.GetProperty("markerBgrSha256").GetString()!.Length);
            Assert.AreEqual(64, rootElement.GetProperty("markerPngSha256").GetString()!.Length);
            Assert.IsFalse(Directory.EnumerateFiles(root, "*.tmp-*", SearchOption.AllDirectories).Any());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task Storage_RejectsNonPngBytesWithoutCreatingCaptureFiles()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var source = CreateFrame();
            var region = new SceneMarkerPixelRegion(1, 1, 2, 2);
            var request = CreateSaveRequest(source, region) with { MarkerPng = new byte[] { 1, 2, 3 } };
            var storage = new SceneMarkerStorage();

            await Assert.ThrowsExceptionAsync<ArgumentException>(() => storage.SaveAsync(root, request));

            Assert.AreEqual(0, Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Count());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task Storage_RejectsPngWithDimensionsDifferentFromTheSelectedRegion()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var source = CreateFrame();
            var region = new SceneMarkerPixelRegion(1, 1, 2, 2);
            var wrongSizePng = CreatePng(new OcrImageFrame(new byte[] { 1, 2, 3 }, 1, 1, 3));
            var request = CreateSaveRequest(source, region) with { MarkerPng = wrongSizePng };
            var storage = new SceneMarkerStorage();

            await Assert.ThrowsExceptionAsync<ArgumentException>(() => storage.SaveAsync(root, request));

            Assert.AreEqual(0, Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Count());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task Storage_CancelledBeforeWriteLeavesNoCaptureFiles()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var source = CreateFrame();
            var region = new SceneMarkerPixelRegion(1, 1, 2, 2);
            var request = CreateSaveRequest(source, region);
            var storage = new SceneMarkerStorage();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsExceptionAsync<OperationCanceledException>(() =>
                storage.SaveAsync(root, request, cancellation.Token));

            Assert.AreEqual(0, Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Count());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task Storage_RejectsPngWithAnInvalidChunkChecksum()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var source = CreateFrame();
            var region = new SceneMarkerPixelRegion(1, 1, 2, 2);
            var request = CreateSaveRequest(source, region);
            var corruptPng = request.MarkerPng.ToArray();
            corruptPng[^5] ^= 0x7F;
            var storage = new SceneMarkerStorage();

            await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
                storage.SaveAsync(root, request with { MarkerPng = corruptPng }));

            Assert.AreEqual(0, Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Count());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static OcrImageFrame CreateFrame()
    {
        var pixels = Enumerable.Range(0, 36).Select(value => (byte)value).ToArray();
        return new OcrImageFrame(pixels, Width: 4, Height: 3, Stride: 12);
    }

    private static SceneMarkerSaveRequest CreateSaveRequest(
        OcrImageFrame source,
        SceneMarkerPixelRegion region)
    {
        var marker = SceneMarkerFrameTools.Crop(source, region);
        return new SceneMarkerSaveRequest(
            "achievement-list",
            "title-anchor",
            new DateTimeOffset(2026, 8, 30, 12, 34, 56, 789, TimeSpan.Zero),
            new GameWindowCandidate((nint)123, 456, "Client-Win64-Shipping", "Wuthering Waves", 4, 3),
            new GameWindowClientBounds((nint)123, 100, 200, 4, 3),
            source,
            region,
            CreatePng(marker));
    }

    private static byte[] CreatePng(OcrImageFrame frame)
    {
        frame.Validate();
        var raw = new byte[checked((frame.Width * 3 + 1) * frame.Height)];
        for (var y = 0; y < frame.Height; y++)
        {
            var rawRow = y * (frame.Width * 3 + 1);
            raw[rawRow] = 0;
            for (var x = 0; x < frame.Width; x++)
            {
                var source = y * frame.Stride + x * 3;
                var target = rawRow + 1 + x * 3;
                raw[target] = frame.BgrPixels[source + 2];
                raw[target + 1] = frame.BgrPixels[source + 1];
                raw[target + 2] = frame.BgrPixels[source];
            }
        }

        byte[] compressed;
        using (var compressedStream = new MemoryStream())
        {
            using (var zlib = new ZLibStream(compressedStream, CompressionLevel.SmallestSize, leaveOpen: true))
            {
                zlib.Write(raw);
            }
            compressed = compressedStream.ToArray();
        }

        using var png = new MemoryStream();
        png.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0, 4), (uint)frame.Width);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4, 4), (uint)frame.Height);
        header[8] = 8;
        header[9] = 2;
        WritePngChunk(png, "IHDR"u8, header);
        WritePngChunk(png, "IDAT"u8, compressed);
        WritePngChunk(png, "IEND"u8, Array.Empty<byte>());
        return png.ToArray();
    }

    private static void WritePngChunk(Stream stream, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length);
        stream.Write(length);
        stream.Write(type);
        stream.Write(data);
        Span<byte> crc = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, ComputePngCrc(type, data));
        stream.Write(crc);
    }

    private static uint ComputePngCrc(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        var crc = uint.MaxValue;
        foreach (var value in type)
        {
            crc = UpdatePngCrc(crc, value);
        }
        foreach (var value in data)
        {
            crc = UpdatePngCrc(crc, value);
        }
        return ~crc;
    }

    private static uint UpdatePngCrc(uint crc, byte value)
    {
        crc ^= value;
        for (var bit = 0; bit < 8; bit++)
        {
            crc = (crc & 1) != 0 ? 0xEDB88320U ^ (crc >> 1) : crc >> 1;
        }
        return crc;
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "wuwa-scene-marker-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
