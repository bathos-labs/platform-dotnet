using System.Runtime.InteropServices;
using Bathos.Platform.Desktop;

namespace Bathos.Platform.MacOS;

/// <summary>Obtains the native file icon through NSWorkspace and copies an sRGB raster, without launching the target.</summary>
public sealed partial class MacOSApplicationIconProvider : IApplicationIconProvider
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    private const string Graphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

    /// <include file="../Bathos.Platform/Documentation/Contracts.xml" path="contracts/GetIconAsync/*" />
    public Task<ApplicationIcon?> GetIconAsync(ApplicationTarget application, int pixelSize, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(application); ApplicationIcon.ValidatePixelSize(pixelSize);
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("macOS application icons require macOS.");
        if (application.Kind is not (ApplicationTargetKind.MacOSBundle or ApplicationTargetKind.Executable))
            throw new NotSupportedException("macOS icons require a bundle or executable target.");
        return Task.Run(() => Read(application, pixelSize, cancellationToken), cancellationToken);
    }

    private static unsafe ApplicationIcon? Read(ApplicationTarget application, int size, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (application.Kind == ApplicationTargetKind.MacOSBundle)
        {
            DesktopPaths.RequireEntry(application.Value);
            if ((File.GetAttributes(application.Value) & FileAttributes.Directory) == 0) throw new IOException("An application-bundle directory is required.");
        }
        else DesktopPaths.RequireFile(application.Value);
        var framework = NativeLibrary.Load("/System/Library/Frameworks/AppKit.framework/AppKit");
        nint pool = 0, color = 0, context = 0;
        try
        {
            pool = Send(Send(GetClass("NSAutoreleasePool"), Sel("alloc")), Sel("init"));
            if (pool == 0) throw new IOException("Could not establish the native icon operation's autorelease pool.");
            var workspace = Send(GetClass("NSWorkspace"), Sel("sharedWorkspace"));
            var path = SendString(GetClass("NSString"), Sel("stringWithUTF8String:"), application.Value);
            if (workspace == 0 || path == 0) throw new IOException("The native application-icon lookup could not be prepared.");
            var image = SendObject(workspace, Sel("iconForFile:"), path);
            if (image == 0) { token.ThrowIfCancellationRequested(); return null; }
            var cgImage = Image(image, Sel("CGImageForProposedRect:context:hints:"), 0, 0, 0);
            if (cgImage == 0) throw new IOException("The native application icon could not be rasterized.");
            var width = ImageWidth(cgImage); var height = ImageHeight(cgImage);
            if (width == 0 || height == 0) throw new IOException("The native icon has no usable extent.");
            var pixels = new byte[size * size * 4];
            fixed (byte* data = pixels)
            {
                color = CreateColorSpace("kCGColorSpaceSRGB");
                if (color == 0) throw new IOException("The native sRGB color space is unavailable.");
                context = CreateContext((nint)data, (nuint)size, (nuint)size, 8, (nuint)size * 4, color, 0x4001); // Big-endian RGBA, premultiplied alpha.
                if (context == 0) throw new IOException("The native icon bitmap could not be created.");
                try
                {
                    var scale = Math.Min((double)size / width, (double)size / height);
                    Draw(context, new Rect { X = (size - width * scale) / 2, Y = (size - height * scale) / 2, Width = width * scale, Height = height * scale }, cgImage);
                }
                finally { ReleaseContext(context); context = 0; } // The bitmap may retain the pinned buffer until released.
            }
            for (var index = 0; index < pixels.Length; index += 4)
                for (var channel = 0; channel < 3; channel++)
                    pixels[index + channel] = pixels[index + 3] == 0 ? (byte)0 : (byte)Math.Min(255, (pixels[index + channel] * 255 + pixels[index + 3] / 2) / pixels[index + 3]);
            token.ThrowIfCancellationRequested();
            return ApplicationIcon.TakeOwnership(size, size, pixels);
        }
        finally
        {
            if (context != 0) ReleaseContext(context);
            if (color != 0) ReleaseColorSpace(color);
            if (pool != 0) SendVoid(pool, Sel("drain"));
            NativeLibrary.Free(framework);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public double X, Y, Width, Height; }
    [LibraryImport(ObjC, EntryPoint = "objc_getClass", StringMarshalling = StringMarshalling.Utf8)] private static partial nint GetClass(string name);
    [LibraryImport(ObjC, EntryPoint = "sel_registerName", StringMarshalling = StringMarshalling.Utf8)] private static partial nint Sel(string name);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")] private static partial nint Send(nint receiver, nint selector);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")] private static partial void SendVoid(nint receiver, nint selector);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")] private static partial nint SendObject(nint receiver, nint selector, nint value);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend", StringMarshalling = StringMarshalling.Utf8)] private static partial nint SendString(nint receiver, nint selector, string value);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")] private static partial nint Image(nint receiver, nint selector, nint rect, nint context, nint hints);
    [LibraryImport(Graphics, EntryPoint = "CGColorSpaceCreateWithName")] private static partial nint CreateColorSpaceNative(nint name);
    private static nint CreateColorSpace(string name)
    {
        // CoreGraphics exports CFString constants, not UTF-8 names.
        var library = NativeLibrary.Load(Graphics);
        try { return CreateColorSpaceNative(Marshal.ReadIntPtr(NativeLibrary.GetExport(library, name))); }
        finally { NativeLibrary.Free(library); }
    }
    [LibraryImport(Graphics, EntryPoint = "CGColorSpaceRelease")] private static partial void ReleaseColorSpace(nint color);
    [LibraryImport(Graphics, EntryPoint = "CGBitmapContextCreate")] private static partial nint CreateContext(nint data, nuint width, nuint height, nuint bits, nuint stride, nint color, uint info);
    [LibraryImport(Graphics, EntryPoint = "CGContextDrawImage")] private static partial void Draw(nint context, Rect rect, nint image);
    [LibraryImport(Graphics, EntryPoint = "CGContextRelease")] private static partial void ReleaseContext(nint context);
    [LibraryImport(Graphics, EntryPoint = "CGImageGetWidth")] private static partial nuint ImageWidth(nint image);
    [LibraryImport(Graphics, EntryPoint = "CGImageGetHeight")] private static partial nuint ImageHeight(nint image);
}
