using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Bathos.Platform.Desktop;
using Bathos.Platform.MacOS;
using Bathos.Platform.Windows;
using Xunit;

namespace Bathos.Platform.Tests;

public sealed class NativeIconResourceTests
{
    [Fact]
    [Trait("Validation", "Native")]
    public async Task OwnedNativeResourcePreservesTopDownChannelsAndStraightAlpha()
    {
        if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsWindows()) Assert.Skip("Native raster extraction requires macOS or Windows.");
        var folder = Directory.CreateTempSubdirectory("bathos-native-icon-");
        try
        {
            var png = CreatePng(OperatingSystem.IsMacOS() ? 128 : 32);
            ApplicationTarget target;
            IApplicationIconProvider provider;
            if (OperatingSystem.IsMacOS())
            {
                var bundle = Directory.CreateDirectory(Path.Combine(folder.FullName, "Owned.app"));
                var resources = Directory.CreateDirectory(Path.Combine(bundle.FullName, "Contents", "Resources"));
                File.WriteAllText(Path.Combine(bundle.FullName, "Contents", "Info.plist"),
                    $"<?xml version=\"1.0\"?><plist version=\"1.0\"><dict><key>CFBundleIdentifier</key><string>org.bathos.Icon{Guid.NewGuid():N}</string><key>CFBundlePackageType</key><string>APPL</string><key>CFBundleExecutable</key><string>Owned</string><key>CFBundleIconFile</key><string>Owned.icns</string></dict></plist>");
                var executable = Path.Combine(Directory.CreateDirectory(Path.Combine(bundle.FullName, "Contents", "MacOS")).FullName, "Owned");
                File.WriteAllText(executable, "#!/bin/sh\nexit 1\n");
                File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                using (var stream = File.Create(Path.Combine(resources.FullName, "Owned.icns")))
                {
                    stream.Write("icns"u8); WriteBig(stream, (uint)png.Length + 16);
                    stream.Write("ic07"u8); WriteBig(stream, (uint)png.Length + 8); stream.Write(png);
                }
                target = ApplicationTarget.MacOSBundle(bundle.FullName); provider = new MacOSApplicationIconProvider();
            }
            else
            {
                // A private ICO resource exercises the same native extraction path without depending on a system executable's artwork.
                var path = Path.Combine(folder.FullName, "Owned.ico");
                using (var stream = File.Create(path))
                using (var writer = new BinaryWriter(stream))
                {
                    writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)1);
                    writer.Write((byte)32); writer.Write((byte)32); writer.Write((byte)0); writer.Write((byte)0);
                    writer.Write((ushort)1); writer.Write((ushort)32); writer.Write((uint)png.Length); writer.Write(22u); writer.Write(png);
                }
                target = ApplicationTarget.Executable(path); provider = new WindowsApplicationIconProvider();
            }
            var icon = Assert.IsType<ApplicationIcon>(await provider.GetIconAsync(target, 32, TestContext.Current.CancellationToken));
            var data = icon.Data.ToArray();
            Pixel(8, 8, [255, 0, 0, 255]); Pixel(8, 24, [0, 0, 255, 255]);
            if (OperatingSystem.IsWindows())
            {
                Pixel(24, 8, [0, 255, 0, 128]); Pixel(24, 24, [0, 0, 0, 0]);
            }
            else
            {
                // IconServices may composite translucent artwork over a native app-icon plate.
                // Its appearance is part of the returned native icon, not a source-alpha preservation promise.
                var green = (8 * 32 + 24) * 4;
                Assert.True(data[green + 1] > data[green] && data[green + 1] > data[green + 2]);
            }
            folder.Delete(true);
            Assert.Equal(data, icon.Data.ToArray());
            void Pixel(int x, int y, byte[] expected)
            {
                for (var channel = 0; channel < 4; channel++) Assert.True(Math.Abs(data[(y * 32 + x) * 4 + channel] - expected[channel]) <= 2,
                    "Native quadrant samples: " + string.Join("; ", new[] { (8, 8), (24, 8), (8, 24), (24, 24) }.Select(point => string.Join(',', data.AsSpan((point.Item2 * 32 + point.Item1) * 4, 4).ToArray()))));
            }
        }
        finally { if (folder.Exists) folder.Delete(true); }
    }

    private static byte[] CreatePng(int size)
    {
        using var output = new MemoryStream(); output.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        byte[] header = new byte[13]; BinaryPrimitives.WriteUInt32BigEndian(header, (uint)size); BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), (uint)size);
        header[8] = 8; header[9] = 6; Chunk(output, "IHDR", header);
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
            for (var y = 0; y < size; y++)
            {
                zlib.WriteByte(0);
                for (var x = 0; x < size; x++) zlib.Write(y < size / 2 ? x < size / 2 ? [255, 0, 0, 255] : [0, 255, 0, 128] : x < size / 2 ? [0, 0, 255, 255] : [0, 0, 0, 0]);
            }
        Chunk(output, "IDAT", compressed.ToArray()); Chunk(output, "IEND", []); return output.ToArray();
    }
    private static void Chunk(Stream stream, string name, byte[] data)
    {
        WriteBig(stream, (uint)data.Length); var type = Encoding.ASCII.GetBytes(name); stream.Write(type); stream.Write(data);
        uint crc = uint.MaxValue;
        foreach (var value in type.Concat(data))
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xedb88320u);
        }
        WriteBig(stream, ~crc);
    }
    private static void WriteBig(Stream stream, uint value)
    { Span<byte> bytes = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(bytes, value); stream.Write(bytes); }
}
