namespace Wuwa.Infrastructure;

/// <summary>Describes a visible game client rectangle in physical screen pixels.</summary>
public sealed record GameWindowClientBounds(
    nint Handle,
    int Left,
    int Top,
    int Width,
    int Height)
{
    public int Right => checked(Left + Width);
    public int Bottom => checked(Top + Height);
}
