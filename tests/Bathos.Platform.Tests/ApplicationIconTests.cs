using Bathos.Platform.Desktop;
using Bathos.Platform.Linux;
using Bathos.Platform.MacOS;
using Bathos.Platform.Windows;
using Xunit;

namespace Bathos.Platform.Tests;

public sealed class ApplicationIconTests
{
    [Fact]
    [Trait("Validation", "Portable")]
    public void OwnedDataPreservesItsRepresentationAndRejectsInvalidShapes()
    {
        byte[] pixels = [255, 0, 0, 128];
        var raster = ApplicationIcon.FromRgba32(1, 1, pixels); pixels[0] = 0;
        Assert.Equal(1, raster.PixelWidth); Assert.Equal(1, raster.PixelHeight);
        Assert.Equal([255, 0, 0, 128], raster.Data.ToArray());
        Assert.Throws<ArgumentOutOfRangeException>(() => ApplicationIcon.FromRgba32(0, 1, []));
        Assert.Throws<ArgumentOutOfRangeException>(() => ApplicationIcon.FromRgba32(1025, 1, []));
        Assert.Throws<ArgumentException>(() => ApplicationIcon.FromRgba32(1, 1, [0]));
    }

    [Fact]
    [Trait("Validation", "Portable")]
    public void WindowsCompositesRecoverStraightRgbaForTransparentAndLegacyOpaquePixels()
    {
        var pixels = WindowsApplicationIconProvider.CompositeToRgba(
            [0, 0, 128, 0, 0, 0, 0, 0, 20, 30, 40, 0],
            [127, 127, 255, 0, 255, 255, 255, 0, 20, 30, 40, 0]);
        Assert.Equal([255, 0, 0, 128, 0, 0, 0, 0, 40, 30, 20, 255], pixels);
    }

    [Fact]
    [Trait("Validation", "Native")]
    public async Task NativeIconsHaveOwnedPixelsAndCancellationNeverRequiresDesktopInteraction()
    {
        var token = TestContext.Current.CancellationToken;
        IPlatformProvider provider = PlatformProviders.SelectCurrent(new MacOSPlatformProvider(), new WindowsPlatformProvider(), new LinuxPlatformProvider());
        var icons = Assert.IsAssignableFrom<IApplicationIconProvider>(provider.ApplicationIcons);
        if (OperatingSystem.IsLinux()) Assert.Skip("Linux native desktop-entry resources are exercised in the isolated GIO probe.");
        var target = OperatingSystem.IsMacOS()
            ? MacOSApplicationDiscovery.FindByBundleIdentifier("com.apple.finder")[0]
            : ApplicationTarget.Executable(Environment.ProcessPath ?? throw new InvalidOperationException("No process executable."));
        // Arbitrary executables, including the test runner, need not contain an icon resource.
        // Successful Windows extraction is verified using the owned ICO fixture in NativeIconResourceTests.
        if (OperatingSystem.IsMacOS())
        {
            var icon = Assert.IsType<ApplicationIcon>(await icons.GetIconAsync(target, 32, token));
            Assert.Equal(32, icon.PixelWidth); Assert.Equal(32, icon.PixelHeight);
            Assert.Equal(32 * 32 * 4, icon.Data.Length);
            Assert.Contains(icon.Data.ToArray().Where((_, index) => index % 4 == 3), alpha => alpha > 0);
        }
        using var canceled = CancellationTokenSource.CreateLinkedTokenSource(token); canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => icons.GetIconAsync(target, 32, canceled.Token));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => icons.GetIconAsync(target, 0, token));
        var missing = Path.Combine(Path.GetTempPath(), "bathos-missing-icon-" + Guid.NewGuid().ToString("N"));
        await Assert.ThrowsAnyAsync<IOException>(() => icons.GetIconAsync(ApplicationTarget.Executable(missing), 32, token));
        var unsupported = ApplicationTarget.LinuxDesktopEntry("org.bathos.unsupported.desktop");
        await Assert.ThrowsAsync<NotSupportedException>(() => icons.GetIconAsync(unsupported, 32, token));
    }
}
