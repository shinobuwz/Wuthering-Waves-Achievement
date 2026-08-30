using System.Buffers.Binary;
using System.Security;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Wuwa.Core;

namespace Wuwa.Infrastructure;

public sealed record SceneMarkerSaveRequest(
    string SceneId,
    string MarkerName,
    DateTimeOffset CapturedAtUtc,
    GameWindowCandidate GameWindow,
    GameWindowClientBounds ClientBounds,
    OcrImageFrame SourceFrame,
    SceneMarkerPixelRegion PixelRegion,
    ReadOnlyMemory<byte> MarkerPng);

public sealed record SceneMarkerSourceMetadata(
    int ProcessId,
    string ProcessName,
    string WindowTitle,
    int FrameWidth,
    int FrameHeight,
    int FrameStride,
    int ScreenLeft,
    int ScreenTop,
    int ScreenWidth,
    int ScreenHeight);

public sealed record SceneMarkerCaptureMetadata(
    int SchemaVersion,
    string SceneId,
    string MarkerName,
    string ImageFile,
    DateTimeOffset CapturedAtUtc,
    SceneMarkerSourceMetadata Source,
    SceneMarkerPixelRegion PixelRegion,
    SceneMarkerNormalizedRegion NormalizedRegion,
    string SourceFrameSha256,
    string MarkerBgrSha256,
    string MarkerPngSha256);

public sealed record SceneMarkerSaveResult(
    string ImagePath,
    string MetadataPath,
    SceneMarkerCaptureMetadata Metadata);

/// <summary>Persists marker-lab PNG/JSON pairs below the portable application directory.</summary>
public sealed class SceneMarkerStorage
{
    public const string DirectoryName = "scene-marker-lab";
    private const int SchemaVersion = 1;
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];
    private static readonly byte[] IhdrChunkType = [(byte)'I', (byte)'H', (byte)'D', (byte)'R'];
    private static readonly byte[] IdatChunkType = [(byte)'I', (byte)'D', (byte)'A', (byte)'T'];
    private static readonly byte[] IendChunkType = [(byte)'I', (byte)'E', (byte)'N', (byte)'D'];
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static string DefaultDirectory => Path.Combine(AppPaths.ApplicationDirectory, DirectoryName);

    public static bool TryPrepareDirectory(string directory, out string? error)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            error = "保存目录不能为空。";
            return false;
        }

        string probePath = string.Empty;
        try
        {
            var fullPath = Path.GetFullPath(directory);
            Directory.CreateDirectory(fullPath);
            probePath = Path.Combine(fullPath, $".wuwa-marker-write-{Guid.NewGuid():N}.tmp");
            using (new FileStream(probePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
            }
            File.Delete(probePath);
            error = null;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException or ArgumentException or NotSupportedException)
        {
            TryDelete(probePath);
            error = exception.Message;
            return false;
        }
    }

    public static bool TryPrepareSceneDirectory(
        string rootDirectory,
        string sceneId,
        out string sceneDirectory,
        out string? error)
    {
        sceneDirectory = string.Empty;
        if (!SceneMarkerIdentifier.TryValidate(sceneId, out var validatedSceneId, out error))
        {
            return false;
        }

        try
        {
            sceneDirectory = Path.Combine(Path.GetFullPath(rootDirectory), validatedSceneId);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or SecurityException)
        {
            error = exception.Message;
            return false;
        }
        return TryPrepareDirectory(sceneDirectory, out error);
    }

    public async Task<SceneMarkerSaveResult> SaveAsync(
        string directory,
        SceneMarkerSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!SceneMarkerIdentifier.TryValidate(request.SceneId, out var sceneId, out var sceneError))
        {
            throw new ArgumentException(sceneError, nameof(request));
        }
        if (!SceneMarkerIdentifier.TryValidate(request.MarkerName, out var markerName, out var markerError))
        {
            throw new ArgumentException(markerError, nameof(request));
        }

        request.SourceFrame.Validate();
        request.PixelRegion.ValidateForFrame(request.SourceFrame.Width, request.SourceFrame.Height);
        var markerFrame = SceneMarkerFrameTools.Crop(request.SourceFrame, request.PixelRegion);
        ValidatePng(request.MarkerPng.Span, markerFrame.Width, markerFrame.Height);
        if (!TryPrepareSceneDirectory(directory, sceneId, out var sceneDirectory, out var directoryError))
        {
            throw new IOException($"无法写入场景标记目录：{directoryError}");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var markerBgrHash = ComputeFrameHash(markerFrame);
        var markerPngHash = Convert.ToHexString(SHA256.HashData(request.MarkerPng.Span)).ToLowerInvariant();
        var sourceHash = ComputeFrameHash(request.SourceFrame);
        var capturedAtUtc = request.CapturedAtUtc.ToUniversalTime();
        var uniquePart = Guid.NewGuid().ToString("N")[..8];
        var baseName = $"{capturedAtUtc:yyyyMMdd-HHmmssfff}-{markerName}-{markerBgrHash[..8]}-{uniquePart}";
        var imagePath = Path.Combine(sceneDirectory, baseName + ".png");
        var metadataPath = Path.Combine(sceneDirectory, baseName + ".json");
        var metadata = new SceneMarkerCaptureMetadata(
            SchemaVersion,
            sceneId,
            markerName,
            Path.GetFileName(imagePath),
            capturedAtUtc,
            new SceneMarkerSourceMetadata(
                request.GameWindow.ProcessId,
                request.GameWindow.ProcessName,
                request.GameWindow.Title,
                request.SourceFrame.Width,
                request.SourceFrame.Height,
                request.SourceFrame.Stride,
                request.ClientBounds.Left,
                request.ClientBounds.Top,
                request.ClientBounds.Width,
                request.ClientBounds.Height),
            request.PixelRegion,
            SceneMarkerFrameTools.Normalize(request.PixelRegion, request.SourceFrame.Width, request.SourceFrame.Height),
            sourceHash,
            markerBgrHash,
            markerPngHash);

        var temporarySuffix = $".tmp-{Guid.NewGuid():N}";
        var temporaryImagePath = imagePath + temporarySuffix;
        var temporaryMetadataPath = metadataPath + temporarySuffix;
        var imageCommitted = false;
        try
        {
            await File.WriteAllBytesAsync(temporaryImagePath, request.MarkerPng.ToArray(), cancellationToken).ConfigureAwait(false);
            var json = JsonSerializer.Serialize(metadata, JsonOptions);
            await File.WriteAllTextAsync(temporaryMetadataPath, json, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryImagePath, imagePath, overwrite: false);
            imageCommitted = true;
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryMetadataPath, metadataPath, overwrite: false);
            return new SceneMarkerSaveResult(imagePath, metadataPath, metadata);
        }
        catch
        {
            if (imageCommitted)
            {
                TryDelete(imagePath);
            }
            throw;
        }
        finally
        {
            TryDelete(temporaryImagePath);
            TryDelete(temporaryMetadataPath);
        }
    }

    private static void ValidatePng(ReadOnlySpan<byte> png, int expectedWidth, int expectedHeight)
    {
        if (png.Length < PngSignature.Length || !png[..PngSignature.Length].SequenceEqual(PngSignature))
        {
            throw new ArgumentException("Marker image bytes must contain a valid PNG image.", nameof(png));
        }

        var offset = PngSignature.Length;
        var sawHeader = false;
        var sawImageData = false;
        var sawEnd = false;
        while (offset < png.Length)
        {
            if (png.Length - offset < 12)
            {
                throw new ArgumentException("Marker PNG contains a truncated chunk.", nameof(png));
            }

            var unsignedLength = BinaryPrimitives.ReadUInt32BigEndian(png.Slice(offset, 4));
            if (unsignedLength > int.MaxValue)
            {
                throw new ArgumentException("Marker PNG chunk is too large.", nameof(png));
            }
            var dataLength = (int)unsignedLength;
            if (dataLength > png.Length - offset - 12)
            {
                throw new ArgumentException("Marker PNG contains a truncated chunk.", nameof(png));
            }

            var type = png.Slice(offset + 4, 4);
            var data = png.Slice(offset + 8, dataLength);
            var storedCrc = BinaryPrimitives.ReadUInt32BigEndian(png.Slice(offset + 8 + dataLength, 4));
            if (storedCrc != ComputePngCrc(type, data))
            {
                throw new ArgumentException("Marker PNG chunk checksum is invalid.", nameof(png));
            }

            if (!sawHeader)
            {
                if (!type.SequenceEqual(IhdrChunkType) || dataLength != 13)
                {
                    throw new ArgumentException("Marker PNG must begin with an IHDR chunk.", nameof(png));
                }
                var width = BinaryPrimitives.ReadUInt32BigEndian(data[..4]);
                var height = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(4, 4));
                if (width != expectedWidth || height != expectedHeight)
                {
                    throw new ArgumentException("Marker PNG dimensions must match the selected region.", nameof(png));
                }
                if (data[10] != 0 || data[11] != 0 || data[12] > 1)
                {
                    throw new ArgumentException("Marker PNG uses unsupported compression, filtering, or interlacing metadata.", nameof(png));
                }
                sawHeader = true;
            }
            else if (type.SequenceEqual(IhdrChunkType))
            {
                throw new ArgumentException("Marker PNG contains more than one IHDR chunk.", nameof(png));
            }

            if (type.SequenceEqual(IdatChunkType))
            {
                sawImageData = true;
            }
            if (type.SequenceEqual(IendChunkType))
            {
                if (dataLength != 0)
                {
                    throw new ArgumentException("Marker PNG IEND chunk must be empty.", nameof(png));
                }
                sawEnd = true;
                offset += 12;
                if (offset != png.Length)
                {
                    throw new ArgumentException("Marker PNG contains data after IEND.", nameof(png));
                }
                break;
            }

            offset += checked(dataLength + 12);
        }

        if (!sawHeader || !sawImageData || !sawEnd)
        {
            throw new ArgumentException("Marker image bytes contain an incomplete PNG image.", nameof(png));
        }
    }

    private static uint ComputePngCrc(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        var crc = uint.MaxValue;
        foreach (var value in type)
        {
            crc = UpdateCrc(crc, value);
        }
        foreach (var value in data)
        {
            crc = UpdateCrc(crc, value);
        }
        return ~crc;
    }

    private static uint UpdateCrc(uint crc, byte value)
    {
        crc ^= value;
        for (var bit = 0; bit < 8; bit++)
        {
            crc = (crc & 1) != 0 ? 0xEDB88320U ^ (crc >> 1) : crc >> 1;
        }
        return crc;
    }

    private static string ComputeFrameHash(OcrImageFrame frame)
    {
        var usedLength = checked(frame.Stride * frame.Height);
        return Convert.ToHexString(SHA256.HashData(frame.BgrPixels.AsSpan(0, usedLength))).ToLowerInvariant();
    }

    private static void TryDelete(string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
