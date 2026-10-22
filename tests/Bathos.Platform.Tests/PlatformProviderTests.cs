using Bathos.Platform.Linux;
using Bathos.Platform.MacOS;
using Bathos.Platform.Windows;
using Xunit;

namespace Bathos.Platform.Tests;

public sealed class PlatformProviderTests
{
    [Fact]
    public void SelectionUsesOnlySuppliedProvidersAndRejectsAmbiguity()
    {
        var windows = new WindowsPlatformProvider();
        var mac = new MacOSPlatformProvider();
        var linux = new LinuxPlatformProvider();
        Assert.Same(windows, PlatformProviders.Select(PlatformOperatingSystem.Windows, linux, windows, mac));
        Assert.Same(mac, PlatformProviders.Select(PlatformOperatingSystem.MacOS, mac));
        Assert.Same(linux, PlatformProviders.Select(PlatformOperatingSystem.Linux, linux));
        Assert.Throws<PlatformNotSupportedException>(() => PlatformProviders.Select(PlatformOperatingSystem.MacOS, windows));
        Assert.Throws<PlatformNotSupportedException>(() => PlatformProviders.Select(PlatformOperatingSystem.Unknown, mac, windows, linux));
        Assert.Throws<InvalidOperationException>(() => PlatformProviders.Select(PlatformOperatingSystem.Windows, windows, new WindowsPlatformProvider()));
        Assert.Equal(PlatformHost.Current, PlatformProviders.SelectCurrent(mac, windows, linux).OperatingSystem);
    }

    [Fact]
    public void CapabilityAbsenceIsExplicitAndConstructionDoesNotRequireNativeLibraries()
    {
        IPlatformProvider[] providers = [new MacOSPlatformProvider(), new WindowsPlatformProvider(), new LinuxPlatformProvider()];
        foreach (var provider in providers)
        {
            Assert.NotNull(provider.FileEntries);
            Assert.Null(provider.ScreenColorSampler);
        }
        Assert.NotNull(providers[0].FileRename);
        Assert.NotNull(providers[1].FileRename);
        Assert.Null(providers[2].FileRename);
    }

    [Fact]
    public void NativePackagesCannotDependOnToolkitsProductsOrSiblingProviders()
    {
        var core = typeof(IPlatformProvider).Assembly;
        Assert.DoesNotContain(core.GetReferencedAssemblies(), name => name.Name?.StartsWith("Bathos.Platform.", StringComparison.Ordinal) == true);
        foreach (var assembly in new[] { core, typeof(MacOSPlatformProvider).Assembly, typeof(WindowsPlatformProvider).Assembly, typeof(LinuxPlatformProvider).Assembly })
            foreach (var reference in assembly.GetReferencedAssemblies())
            {
                var name = reference.Name ?? string.Empty;
                Assert.False(name.StartsWith("Avalonia", StringComparison.Ordinal) || name.StartsWith("Makura", StringComparison.Ordinal) ||
                    name.StartsWith("Microsoft.Extensions", StringComparison.Ordinal) || name.StartsWith("Bathos.Platform.", StringComparison.Ordinal), name);
            }
    }
}
