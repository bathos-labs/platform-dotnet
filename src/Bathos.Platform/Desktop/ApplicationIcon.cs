namespace Bathos.Platform.Desktop;

/// <summary>The representation of owned application-icon data. Encoded resources still require host decoding.</summary>
public enum ApplicationIconFormat
{
    /// <summary>Top-down, tightly packed, eight-bit sRGB red/green/blue/straight-alpha pixels.</summary>
    Rgba32,
    /// <summary>An encoded PNG resource, without a promise that a decoder will admit its contents.</summary>
    Png,
    /// <summary>An encoded SVG resource. The host owns safe parsing and rasterization.</summary>
    Svg,
    /// <summary>An encoded XPM resource. The host owns decoding.</summary>
    Xpm,
}

/// <summary>Owned icon data with no native handles or decoder lifetime. Constructors copy the supplied bytes.</summary>
public sealed class ApplicationIcon
{
    /// <summary>Largest admitted raster dimension and requested size, in physical pixels.</summary>
    public const int MaximumPixelSize = 1024;
    /// <summary>Maximum encoded-resource length. Native icon lookup is bounded independently of host decoding.</summary>
    public const int MaximumResourceBytes = 16 * 1024 * 1024;
    private readonly byte[] _data;

    private ApplicationIcon(ApplicationIconFormat format, int? width, int? height, ReadOnlySpan<byte> data)
    { Format = format; PixelWidth = width; PixelHeight = height; _data = data.ToArray(); }

    /// <summary>Gets the pixel layout or declared resource encoding.</summary>
    public ApplicationIconFormat Format { get; }
    /// <summary>Gets the raster width, or null for an encoded resource whose geometry has not been decoded.</summary>
    public int? PixelWidth { get; }
    /// <summary>Gets the raster height, or null for an encoded resource whose geometry has not been decoded.</summary>
    public int? PixelHeight { get; }
    /// <summary>Gets a read-only view of the owned bytes, retained independently of the provider and application installation.</summary>
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
        return new(ApplicationIconFormat.Rgba32, width, height, pixels);
    }

    /// <summary>Copies an encoded icon resource without interpreting or validating its contents.</summary>
    /// <param name="format">PNG, SVG, or XPM; raw RGBA requires FromRgba32.</param>
    /// <param name="data">Nonempty encoded bytes, at most MaximumResourceBytes.</param>
    /// <returns>An independent encoded icon.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The format or length is unsupported.</exception>
    public static ApplicationIcon FromEncoded(ApplicationIconFormat format, ReadOnlySpan<byte> data)
    {
        if (format is not (ApplicationIconFormat.Png or ApplicationIconFormat.Svg or ApplicationIconFormat.Xpm))
            throw new ArgumentOutOfRangeException(nameof(format));
        if (data.IsEmpty || data.Length > MaximumResourceBytes) throw new ArgumentOutOfRangeException(nameof(data));
        return new(format, null, null, data);
    }

    internal static void ValidatePixelSize(int pixelSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pixelSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pixelSize, MaximumPixelSize);
    }
}
