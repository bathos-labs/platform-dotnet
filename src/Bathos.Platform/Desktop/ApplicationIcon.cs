namespace Bathos.Platform.Desktop;

/// <summary>Owned RGBA bitmap with no native handles or decoder lifetime. Public construction copies supplied bytes.</summary>
public sealed class ApplicationIcon
{
    /// <summary>Largest admitted raster dimension and requested size, in physical pixels.</summary>
    public const int MaximumPixelSize = 1024;
    private readonly byte[] _data;

    private ApplicationIcon(int width, int height, byte[] data)
    { PixelWidth = width; PixelHeight = height; _data = data; }

    /// <summary>Gets the bitmap width in physical pixels.</summary>
    public int PixelWidth { get; }
    /// <summary>Gets the bitmap height in physical pixels.</summary>
    public int PixelHeight { get; }
    /// <summary>Gets a read-only view of top-down, tightly packed eight-bit sRGB RGBA pixels with straight alpha. Access does not copy bytes; memory outlives the provider and application installation.</summary>
    public ReadOnlyMemory<byte> Data => _data;

    /// <summary>Copies top-down, tightly packed RGBA pixels with straight alpha and an sRGB interpretation.</summary>
    /// <param name="width">Width from one through MaximumPixelSize.</param>
    /// <param name="height">Height from one through MaximumPixelSize.</param>
    /// <param name="pixels">Exactly width times height times four bytes.</param>
    /// <returns>An independent raster icon.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A dimension is outside the supported bounds.</exception>
    /// <exception cref="ArgumentException">The pixel length does not match the dimensions.</exception>
    public static ApplicationIcon FromRgba32(int width, int height, ReadOnlySpan<byte> pixels)
    {
        ValidatePixelSize(width); ValidatePixelSize(height);
        if (pixels.Length != width * height * 4) throw new ArgumentException("RGBA length must match the dimensions.", nameof(pixels));
        return new(width, height, pixels.ToArray());
    }

    // Providers transfer exclusive ownership; they must never mutate the buffer after this call.
    internal static ApplicationIcon TakeOwnership(int width, int height, byte[] pixels)
    {
        ValidatePixelSize(width); ValidatePixelSize(height);
        if (pixels.Length != width * height * 4) throw new ArgumentException("Invalid RGBA buffer.", nameof(pixels));
        return new(width, height, pixels);
    }

    internal static void ValidatePixelSize(int pixelSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pixelSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pixelSize, MaximumPixelSize);
    }
}
