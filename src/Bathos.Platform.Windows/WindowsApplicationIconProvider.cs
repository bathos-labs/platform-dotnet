using System.ComponentModel;
using System.Runtime.InteropServices;
using Bathos.Platform.Desktop;
using Microsoft.Win32.SafeHandles;

namespace Bathos.Platform.Windows;

/// <summary>Extracts an executable's first icon resource and copies its native rendering without retaining GDI handles.</summary>
public sealed partial class WindowsApplicationIconProvider : IApplicationIconProvider
{
    /// <include file="../Bathos.Platform/Documentation/Contracts.xml" path="contracts/GetIconAsync/*" />
    public Task<ApplicationIcon?> GetIconAsync(ApplicationTarget application, int pixelSize, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(application); ApplicationIcon.ValidatePixelSize(pixelSize);
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows application icons require Windows.");
        if (application.Kind != ApplicationTargetKind.Executable) throw new NotSupportedException("Windows icons require an executable target.");
        return Task.Run(() => Read(application.Value, pixelSize, cancellationToken), cancellationToken);
    }

    private static ApplicationIcon? Read(string path, int size, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); DesktopPaths.RequireFile(path);
        var result = Extract(path, 0, 0, out var nativeIcon, 0, (uint)size);
        using var icon = new IconHandle(nativeIcon);
        Marshal.ThrowExceptionForHR(result);
        if (result == 1) { token.ThrowIfCancellationRequested(); return null; } // S_FALSE: no icon resource.
        if (icon.IsInvalid) throw new IOException("Native extraction returned no icon handle.");
        var black = Render(icon.DangerousGetHandle(), size, 0); var white = Render(icon.DangerousGetHandle(), size, 255);
        token.ThrowIfCancellationRequested();
        return ApplicationIcon.TakeOwnership(size, size, CompositeToRgba(black, white));
    }

    // Rendering on two opaque backgrounds also recovers transparency for legacy masked icons whose alpha is absent.
    internal static byte[] CompositeToRgba(byte[] black, byte[] white)
    {
        var rgba = new byte[black.Length];
        for (var index = 0; index < black.Length; index += 4)
        {
            var alpha = 255 - Math.Clamp(Math.Max(white[index] - black[index], Math.Max(white[index + 1] - black[index + 1], white[index + 2] - black[index + 2])), 0, 255);
            rgba[index + 3] = (byte)alpha;
            for (var channel = 0; channel < 3; channel++)
                rgba[index + channel] = alpha == 0 ? (byte)0 : (byte)Math.Min(255, (black[index + 2 - channel] * 255 + alpha / 2) / alpha);
        }
        return rgba;
    }

    private static byte[] Render(nint icon, int size, byte background)
    {
        using var ownedDc = new DcHandle(CreateCompatibleDC(0));
        if (ownedDc.IsInvalid) throw new Win32Exception(Marshal.GetLastPInvokeError());
        var dc = ownedDc.DangerousGetHandle();
        var info = new BitmapInfo { HeaderSize = 40, Width = size, Height = -size, Planes = 1, Bits = 32 };
        using var bitmap = new BitmapHandle(CreateDIBSection(dc, ref info, 0, out var pixels, 0, 0));
        if (bitmap.IsInvalid || pixels == 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
        var previous = SelectObject(dc, bitmap.DangerousGetHandle());
        if (previous == 0 || previous == -1) throw new Win32Exception(Marshal.GetLastPInvokeError());
        try
        {
            var data = new byte[size * size * 4]; Array.Fill(data, background);
            Marshal.Copy(data, 0, pixels, data.Length);
            if (DrawIconEx(dc, 0, 0, icon, size, size, 0, 0, 3) == 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
            if (GdiFlush() == 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
            Marshal.Copy(pixels, data, 0, data.Length);
            return data;
        }
        finally { SelectObject(dc, previous); }
    }

    private sealed class IconHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        internal IconHandle(nint value) : base(true) => SetHandle(value);
        protected override bool ReleaseHandle() => DestroyIcon(handle) != 0;
    }
    private sealed class BitmapHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        internal BitmapHandle(nint value) : base(true) => SetHandle(value);
        protected override bool ReleaseHandle() => DeleteObject(handle) != 0;
    }
    private sealed class DcHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        internal DcHandle(nint value) : base(true) => SetHandle(value);
        protected override bool ReleaseHandle() => DeleteDC(handle) != 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public uint HeaderSize; public int Width, Height; public ushort Planes, Bits;
        public uint Compression, ImageSize; public int XPixels, YPixels; public uint ColorsUsed, ColorsImportant;
    }
    [LibraryImport("shell32.dll", EntryPoint = "SHDefExtractIconW", StringMarshalling = StringMarshalling.Utf16)] private static partial int Extract(string path, int index, uint flags, out nint large, nint small, uint size);
    [LibraryImport("user32.dll", SetLastError = true)] private static partial int DestroyIcon(nint icon);
    [LibraryImport("gdi32.dll", SetLastError = true)] private static partial nint CreateCompatibleDC(nint dc);
    [LibraryImport("gdi32.dll", SetLastError = true)] private static partial nint CreateDIBSection(nint dc, ref BitmapInfo info, uint usage, out nint pixels, nint section, uint offset);
    [LibraryImport("gdi32.dll", SetLastError = true)] private static partial nint SelectObject(nint dc, nint item);
    [LibraryImport("user32.dll", SetLastError = true)] private static partial int DrawIconEx(nint dc, int x, int y, nint icon, int width, int height, uint step, nint brush, uint flags);
    [LibraryImport("gdi32.dll", SetLastError = true)] private static partial int GdiFlush();
    [LibraryImport("gdi32.dll", SetLastError = true)] private static partial int DeleteObject(nint item);
    [LibraryImport("gdi32.dll", SetLastError = true)] private static partial int DeleteDC(nint dc);
}
