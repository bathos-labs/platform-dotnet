using Bathos.Platform.Linux;
using Xunit;

namespace Bathos.Platform.Tests;

public sealed class LinuxIconRendererTests
{
    [Fact]
    [Trait("Validation", "Native")]
    public void SvgAndXpmProduceOwnedSquareRgbaAndMalformedResourcesFail()
    {
        if (!OperatingSystem.IsLinux()) Assert.Skip("Linux native rendering libraries required.");
        var directory = Directory.CreateTempSubdirectory("bathos-render-");
        try
        {
            var svg = Path.Combine(directory.FullName, "icon.svg");
            File.WriteAllText(svg, "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"64\" height=\"32\"><rect width=\"64\" height=\"32\" fill=\"red\" fill-opacity=\"0.5\"/></svg>");
            var icon = LinuxIconRenderer.Read(svg, 32);
            Assert.Equal(32, icon.PixelWidth); Assert.Equal(32, icon.PixelHeight);
            Assert.Equal(new byte[32 * 8 * 4], icon.Data.Span[..(32 * 8 * 4)].ToArray());
            Assert.Equal(new byte[] { 255, 0, 0, 128 }, icon.Data.Span.Slice(32 * 8 * 4, 4).ToArray());
            var xpm = Path.Combine(directory.FullName, "icon.xpm");
            File.WriteAllText(xpm, "/* XPM */\nstatic char *x[]={\"2 1 2 1\",\"r c #ff0000\",\". c None\",\"r.\"};");
            var raster = LinuxIconRenderer.Read(xpm, 32);
            Assert.Equal(32 * 32 * 4, raster.Data.Length);
            Assert.Equal(new byte[] { 255, 0, 0, 255 }, raster.Data.Span.Slice(32 * 8 * 4, 4).ToArray());
            var png = Path.Combine(directory.FullName, "icon.png");
            File.WriteAllBytes(png, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAMAAAABCAIAAACUgoPjAAAADUlEQVR4nGP4z8AAQQAR+AL+4+XvtgAAAABJRU5ErkJggg=="));
            var rgb = LinuxIconRenderer.Read(png, 3);
            Assert.Equal(new byte[] { 255, 0, 0, 255 }, rgb.Data.Span.Slice(3 * 4, 4).ToArray());
            File.WriteAllText(svg, "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"32\" height=\"32\"><image href=\"" + new Uri(png).AbsoluteUri + "\" width=\"32\" height=\"32\"/></svg>");
            Assert.Equal(new byte[32 * 32 * 4], LinuxIconRenderer.Read(svg, 32).Data.ToArray());
            File.WriteAllText(svg, "<svg broken");
            Assert.Throws<IOException>(() => LinuxIconRenderer.Read(svg, 32));
            File.WriteAllText(xpm, "invalid");
            Assert.Throws<IOException>(() => LinuxIconRenderer.Read(xpm, 32));
            directory.Delete(true);
            Assert.Equal(new byte[] { 255, 0, 0, 128 }, icon.Data.Span.Slice(32 * 8 * 4, 4).ToArray());
        }
        finally { if (directory.Exists) directory.Delete(true); }
    }
}
