namespace Wuwa.Core;

/// <summary>A rectangular marker region expressed in source-frame pixels.</summary>
public sealed record SceneMarkerPixelRegion(int X, int Y, int Width, int Height)
{
    public int Right => checked(X + Width);
    public int Bottom => checked(Y + Height);

    public void ValidateForFrame(int frameWidth, int frameHeight)
    {
        if (frameWidth <= 0 || frameHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frameWidth), "Frame dimensions must be positive.");
        }

        if (X < 0 || Y < 0 || Width <= 0 || Height <= 0 || Right > frameWidth || Bottom > frameHeight)
        {
            throw new ArgumentOutOfRangeException(nameof(SceneMarkerPixelRegion), "The marker region must be inside the source frame.");
        }
    }
}

/// <summary>A rectangular marker region normalized to the source-frame width and height.</summary>
public sealed record SceneMarkerNormalizedRegion(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;
    public double Bottom => Top + Height;
}

/// <summary>Pure display-coordinate mapping and BGR cropping used by the WPF marker overlay.</summary>
public static class SceneMarkerFrameTools
{
    public const int MinimumMarkerSize = 3;

    public static SceneMarkerPixelRegion MapDisplaySelection(
        double startX,
        double startY,
        double endX,
        double endY,
        double displayWidth,
        double displayHeight,
        int sourceWidth,
        int sourceHeight)
    {
        ValidateFinite(startX, nameof(startX));
        ValidateFinite(startY, nameof(startY));
        ValidateFinite(endX, nameof(endX));
        ValidateFinite(endY, nameof(endY));
        ValidateFinite(displayWidth, nameof(displayWidth));
        ValidateFinite(displayHeight, nameof(displayHeight));
        if (displayWidth <= 0 || displayHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(displayWidth), "Display dimensions must be positive.");
        }
        if (sourceWidth <= 0 || sourceHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceWidth), "Source dimensions must be positive.");
        }

        var left = Math.Clamp(Math.Min(startX, endX), 0, displayWidth);
        var top = Math.Clamp(Math.Min(startY, endY), 0, displayHeight);
        var right = Math.Clamp(Math.Max(startX, endX), 0, displayWidth);
        var bottom = Math.Clamp(Math.Max(startY, endY), 0, displayHeight);

        var pixelLeft = Math.Clamp((int)Math.Floor(left / displayWidth * sourceWidth), 0, sourceWidth);
        var pixelTop = Math.Clamp((int)Math.Floor(top / displayHeight * sourceHeight), 0, sourceHeight);
        var pixelRight = Math.Clamp((int)Math.Ceiling(right / displayWidth * sourceWidth), 0, sourceWidth);
        var pixelBottom = Math.Clamp((int)Math.Ceiling(bottom / displayHeight * sourceHeight), 0, sourceHeight);

        return new SceneMarkerPixelRegion(
            pixelLeft,
            pixelTop,
            pixelRight - pixelLeft,
            pixelBottom - pixelTop);
    }

    public static bool IsLargeEnough(
        SceneMarkerPixelRegion region,
        int minimumWidth = MinimumMarkerSize,
        int minimumHeight = MinimumMarkerSize)
    {
        ArgumentNullException.ThrowIfNull(region);
        if (minimumWidth <= 0 || minimumHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumWidth), "Minimum dimensions must be positive.");
        }
        return region.Width >= minimumWidth && region.Height >= minimumHeight;
    }

    public static SceneMarkerNormalizedRegion Normalize(
        SceneMarkerPixelRegion region,
        int sourceWidth,
        int sourceHeight)
    {
        ArgumentNullException.ThrowIfNull(region);
        region.ValidateForFrame(sourceWidth, sourceHeight);
        return new SceneMarkerNormalizedRegion(
            region.X / (double)sourceWidth,
            region.Y / (double)sourceHeight,
            region.Width / (double)sourceWidth,
            region.Height / (double)sourceHeight);
    }

    public static OcrImageFrame Crop(OcrImageFrame frame, SceneMarkerPixelRegion region)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(region);
        frame.Validate();
        region.ValidateForFrame(frame.Width, frame.Height);

        var croppedStride = checked(region.Width * 3);
        var croppedPixels = new byte[checked(croppedStride * region.Height)];
        for (var row = 0; row < region.Height; row++)
        {
            Buffer.BlockCopy(
                frame.BgrPixels,
                checked((region.Y + row) * frame.Stride + region.X * 3),
                croppedPixels,
                row * croppedStride,
                croppedStride);
        }

        return new OcrImageFrame(croppedPixels, region.Width, region.Height, croppedStride);
    }

    private static void ValidateFinite(double value, string parameterName)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(parameterName, "Coordinates and dimensions must be finite.");
        }
    }
}

/// <summary>Validation for stable scene and marker identifiers used in capture metadata and filenames.</summary>
public static class SceneMarkerIdentifier
{
    public const int MaximumLength = 64;

    public static bool TryValidate(string? value, out string identifier, out string? error)
    {
        identifier = value?.Trim() ?? string.Empty;
        if (identifier.Length == 0)
        {
            error = "标识不能为空。";
            return false;
        }
        if (identifier.Length > MaximumLength)
        {
            error = $"标识不能超过 {MaximumLength} 个字符。";
            return false;
        }
        if (!IsAsciiLowerOrDigit(identifier[0]))
        {
            error = "标识必须以小写英文字母或数字开头。";
            return false;
        }
        if (identifier.Any(character => !IsAsciiLowerOrDigit(character) && character is not '.' and not '_' and not '-'))
        {
            error = "标识只能包含小写英文字母、数字、点、下划线和连字符。";
            return false;
        }

        error = null;
        return true;
    }

    private static bool IsAsciiLowerOrDigit(char character) =>
        character is >= 'a' and <= 'z' or >= '0' and <= '9';
}
