using System.Runtime.InteropServices;
using Bathos.Platform.Desktop;

namespace Bathos.Platform.Linux;

internal static partial class LinuxIconRenderer
{
    internal const int MaximumResourceBytes = 16 * 1024 * 1024;
    private const string Pixbuf = "libgdk_pixbuf-2.0.so.0";
    private const string Rsvg = "librsvg-2.so.2";
    private const string Cairo = "libcairo.so.2";

    internal static ApplicationIcon Read(string path, int size)
    {
        ApplicationIcon.ValidatePixelSize(size);
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is not (".png" or ".xpm" or ".svg")) throw new NotSupportedException("Application icons require PNG, XPM, or SVG resources.");
        var bytes = LinuxIconTheme.ReadBounded(path, MaximumResourceBytes);
        try { return extension == ".svg" ? RenderSvg(bytes, size) : RenderRaster(bytes, size, extension == ".png" ? "png" : "xpm"); }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            var dependencies = extension == ".svg" ? $"{Rsvg}, {Cairo}, libgobject-2.0.so.0, libglib-2.0.so.0" : $"{Pixbuf}, libgio-2.0.so.0, libgobject-2.0.so.0, libglib-2.0.so.0";
            throw new PlatformNotSupportedException($"Linux application-icon rendering requires compatible native libraries: {dependencies}. Native loading failure: {error.Message}", error);
        }
    }

    private static unsafe ApplicationIcon RenderRaster(byte[] bytes, int size, string format)
    {
        fixed (byte* source = bytes)
        {
            var loader = NewLoader(format, out var error);
            if (loader == 0) throw Failure(error, "Could not create the icon decoder.");
            try
            {
                // Validate the callback's native entry point before entering unmanaged code: exceptions
                // must never escape a reverse P/Invoke callback.
                var library = NativeLibrary.Load(Pixbuf);
                try { NativeLibrary.GetExport(library, "gdk_pixbuf_loader_set_size"); }
                finally { NativeLibrary.Free(library); }
                var requested = size;
                if (Connect(loader, "size-prepared", (nint)(delegate* unmanaged[Cdecl]<nint, int, int, nint, void>)&PrepareSize, (nint)(&requested), 0, 0) == 0)
                    throw new IOException("Could not configure the icon decoder.");
                var written = Write(loader, (nint)source, (nuint)bytes.Length, out error);
                if (written == 0)
                {
                    var failure = Failure(error, "Could not decode the icon.");
                    Close(loader, out var closeError);
                    if (closeError != 0) FreeError(closeError);
                    throw failure;
                }
                if (Close(loader, out error) == 0) throw Failure(error, "Could not finish decoding the icon.");
                var pixbuf = GetPixbuf(loader); // Borrowed until loader disposal.
                if (pixbuf == 0) throw new IOException("The icon decoder returned no bitmap.");
                var width = Width(pixbuf); var height = Height(pixbuf); var channels = Channels(pixbuf); var stride = Stride(pixbuf);
                var data = Pixels(pixbuf);
                if (width < 1 || height < 1 || width > size || height > size || channels is not (3 or 4) || Bits(pixbuf) != 8 || stride < width * channels || data == 0)
                    throw new IOException("The icon decoder returned an unsupported bitmap layout.");
                var result = new byte[size * size * 4];
                for (var y = 0; y < height; y++)
                    for (var x = 0; x < width; x++)
                    {
                        var input = (byte*)data + (long)y * stride + x * channels;
                        var offset = (((size - height) / 2 + y) * size + (size - width) / 2 + x) * 4;
                        result[offset] = input[0]; result[offset + 1] = input[1]; result[offset + 2] = input[2];
                        result[offset + 3] = channels == 4 ? input[3] : (byte)255;
                    }
                return ApplicationIcon.TakeOwnership(size, size, result);
            }
            finally { Unref(loader); }
        }
    }

    private static unsafe ApplicationIcon RenderSvg(byte[] bytes, int size)
    {
        fixed (byte* source = bytes)
        {
            // No base URI: external files/network resources cannot be resolved. Keep librsvg's default parser limits.
            var svg = NewSvg((nint)source, (nuint)bytes.Length, out var error);
            if (svg == 0) throw Failure(error, "Could not parse the SVG icon.");
            try
            {
                var result = new byte[size * size * 4];
                fixed (byte* pixels = result)
                {
                    var surface = Surface((nint)pixels, 0, size, size, size * 4); // CAIRO_FORMAT_ARGB32
                    if (surface == 0) throw new IOException("Could not allocate the SVG surface.");
                    try
                    {
                        if (SurfaceStatus(surface) != 0) throw new IOException("Could not create the SVG surface.");
                        var context = Create(surface);
                        if (context == 0) throw new IOException("Could not allocate the SVG context.");
                        try
                        {
                            var viewport = new Rectangle { Width = size, Height = size };
                            if (Render(svg, context, ref viewport, out error) == 0) throw Failure(error, "Could not render the SVG icon.");
                            if (Status(context) != 0) throw new IOException("SVG rendering failed.");
                            Flush(surface);
                            if (SurfaceStatus(surface) != 0) throw new IOException("SVG surface rendering failed.");
                        }
                        finally { Destroy(context); }
                    }
                    finally { DestroySurface(surface); }
                }
                for (var index = 0; index < result.Length; index += 4)
                {
                    var value = BitConverter.ToUInt32(result, index);
                    var alpha = (byte)(value >> 24);
                    result[index] = Straight((byte)(value >> 16), alpha);
                    result[index + 1] = Straight((byte)(value >> 8), alpha);
                    result[index + 2] = Straight((byte)value, alpha);
                    result[index + 3] = alpha;
                }
                return ApplicationIcon.TakeOwnership(size, size, result);
            }
            finally { Unref(svg); }
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static unsafe void PrepareSize(nint loader, int width, int height, nint data)
    {
        var size = *(int*)data;
        var scale = Math.Min((double)size / Math.Max(1, width), (double)size / Math.Max(1, height));
        SetSize(loader, Math.Max(1, (int)(width * scale)), Math.Max(1, (int)(height * scale)));
    }

    private static byte Straight(byte value, byte alpha) => alpha == 0 ? (byte)0 : (byte)Math.Min(255, (value * 255 + alpha / 2) / alpha);
    private static IOException Failure(nint error, string message)
    {
        if (error == 0) return new IOException(message);
        try { return new IOException($"{message} {Marshal.PtrToStringUTF8(Marshal.PtrToStructure<GError>(error).Message)}"); }
        finally { FreeError(error); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct GError { public uint Domain; public int Code; public nint Message; }
    [StructLayout(LayoutKind.Sequential)] private struct Rectangle { public double X, Y, Width, Height; }
    [LibraryImport("libglib-2.0.so.0", EntryPoint = "g_error_free")] private static partial void FreeError(nint error);
    [LibraryImport("libgobject-2.0.so.0", EntryPoint = "g_object_unref")] private static partial void Unref(nint value);
    [LibraryImport(Pixbuf, EntryPoint = "gdk_pixbuf_loader_new_with_type", StringMarshalling = StringMarshalling.Utf8)] private static partial nint NewLoader(string format, out nint error);
    [LibraryImport(Pixbuf, EntryPoint = "gdk_pixbuf_loader_write")] private static partial int Write(nint loader, nint data, nuint length, out nint error);
    [LibraryImport(Pixbuf, EntryPoint = "gdk_pixbuf_loader_close")] private static partial int Close(nint loader, out nint error);
    [LibraryImport(Pixbuf, EntryPoint = "gdk_pixbuf_loader_get_pixbuf")] private static partial nint GetPixbuf(nint loader);
    [LibraryImport(Pixbuf, EntryPoint = "gdk_pixbuf_loader_set_size")] private static partial void SetSize(nint loader, int width, int height);
    [LibraryImport("libgobject-2.0.so.0", EntryPoint = "g_signal_connect_data", StringMarshalling = StringMarshalling.Utf8)] private static partial nuint Connect(nint instance, string signal, nint callback, nint data, nint destroy, int flags);
    [LibraryImport(Pixbuf, EntryPoint = "gdk_pixbuf_get_width")] private static partial int Width(nint pixbuf);
    [LibraryImport(Pixbuf, EntryPoint = "gdk_pixbuf_get_height")] private static partial int Height(nint pixbuf);
    [LibraryImport(Pixbuf, EntryPoint = "gdk_pixbuf_get_n_channels")] private static partial int Channels(nint pixbuf);
    [LibraryImport(Pixbuf, EntryPoint = "gdk_pixbuf_get_bits_per_sample")] private static partial int Bits(nint pixbuf);
    [LibraryImport(Pixbuf, EntryPoint = "gdk_pixbuf_get_rowstride")] private static partial int Stride(nint pixbuf);
    [LibraryImport(Pixbuf, EntryPoint = "gdk_pixbuf_get_pixels")] private static partial nint Pixels(nint pixbuf);
    [LibraryImport(Rsvg, EntryPoint = "rsvg_handle_new_from_data")] private static partial nint NewSvg(nint data, nuint length, out nint error);
    [LibraryImport(Rsvg, EntryPoint = "rsvg_handle_render_document")] private static partial int Render(nint svg, nint context, ref Rectangle viewport, out nint error);
    [LibraryImport(Cairo, EntryPoint = "cairo_image_surface_create_for_data")] private static partial nint Surface(nint data, int format, int width, int height, int stride);
    [LibraryImport(Cairo, EntryPoint = "cairo_surface_status")] private static partial int SurfaceStatus(nint surface);
    [LibraryImport(Cairo, EntryPoint = "cairo_surface_flush")] private static partial void Flush(nint surface);
    [LibraryImport(Cairo, EntryPoint = "cairo_surface_destroy")] private static partial void DestroySurface(nint surface);
    [LibraryImport(Cairo, EntryPoint = "cairo_create")] private static partial nint Create(nint surface);
    [LibraryImport(Cairo, EntryPoint = "cairo_status")] private static partial int Status(nint context);
    [LibraryImport(Cairo, EntryPoint = "cairo_destroy")] private static partial void Destroy(nint context);
}
