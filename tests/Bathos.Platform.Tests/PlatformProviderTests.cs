using Bathos.Platform.Desktop;
using Bathos.Platform.Linux;
using Bathos.Platform.MacOS;
using Bathos.Platform.Windows;
using Xunit;

namespace Bathos.Platform.Tests;

[Trait("Validation", "Portable")]
public sealed class PlatformProviderTests
{
    [Fact]
    public void ProviderWithOnlyOsIdentitySelectsWithAllCapabilitiesAbsent()
    {
        var supplied = new MinimalProvider();
        var selected = PlatformProviders.Select(PlatformOperatingSystem.Windows, supplied);
        Assert.Same(supplied, selected);
        Assert.Null(selected.FileRename);
        Assert.Null(selected.FilePaths);
        Assert.Null(selected.FileEntries);
        Assert.Null(selected.ScreenColorSampler);
        Assert.Null(selected.FileRevealer);
        Assert.Null(selected.FileInvoker);
    }

    [Fact]
    public void ProviderCanSupplyOneCapabilityWhileInheritingDefaultsForTheOthers()
    {
        var revealer = new WindowsFileRevealer();
        var selected = PlatformProviders.Select(PlatformOperatingSystem.Windows, new RevealOnlyProvider(revealer));
        Assert.Same(revealer, selected.FileRevealer);
        Assert.Null(selected.FileRename);
        Assert.Null(selected.FilePaths);
        Assert.Null(selected.FileEntries);
        Assert.Null(selected.ScreenColorSampler);
        Assert.Null(selected.FileInvoker);
    }

    private sealed class MinimalProvider : IPlatformProvider
    {
        public PlatformOperatingSystem OperatingSystem => PlatformOperatingSystem.Windows;
    }

    private sealed class RevealOnlyProvider(IFileRevealer revealer) : IPlatformProvider
    {
        public PlatformOperatingSystem OperatingSystem => PlatformOperatingSystem.Windows;
        public IFileRevealer FileRevealer => revealer;
    }

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
    public void SamplersAreOptInAndDoNotProbeNativeServicesDuringConstruction()
    {
        var dispatcher = new RejectingDispatcher();
        var mac = new MacOSPlatformProvider(dispatcher).ScreenColorSampler;
        var windows = new WindowsPlatformProvider(dispatcher).ScreenColorSampler;
        var linux = new LinuxPlatformProvider(enableScreenColorSampler: true).ScreenColorSampler;
        Assert.Equal(OperatingSystem.IsMacOSVersionAtLeast(10, 15), mac is not null);
        Assert.Equal(OperatingSystem.IsWindows(), windows is not null);
        Assert.Equal(OperatingSystem.IsLinux(), linux is not null);
        if (mac is not null) Assert.Throws<InvalidOperationException>(() => { _ = mac.SampleAsync(); });
        if (windows is not null) Assert.Throws<InvalidOperationException>(() => { _ = windows.SampleAsync(); });
        Assert.Throws<ArgumentException>(() => new LinuxPlatformProvider(true, "invalid"));
        Assert.Throws<ArgumentException>(() => new LinuxPlatformProvider(true, "x11:abc\0suffix"));
    }

    private sealed class RejectingDispatcher : INativeDispatcher
    {
        public void VerifyAccess() => throw new InvalidOperationException("No native UI access.");
        public void Post(Action action) => throw new InvalidOperationException("No native UI access.");
    }

    [Fact]
    public void CapabilityAbsenceIsExplicitAndConstructionDoesNotRequireNativeLibraries()
    {
        IPlatformProvider[] providers = [new MacOSPlatformProvider(), new WindowsPlatformProvider(), new LinuxPlatformProvider()];
        foreach (var provider in providers)
        {
            Assert.NotNull(provider.FileEntries);
            Assert.NotNull(provider.FilePaths);
            Assert.Null(provider.ScreenColorSampler);
            Assert.NotNull(provider.FileRevealer);
            Assert.NotNull(provider.FileInvoker);
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
                var runtime = name == "System" || name.StartsWith("System.", StringComparison.Ordinal) ||
                    name is "netstandard" or "Microsoft.Win32.Primitives" or "Microsoft.Win32.Registry";
                var sharedCore = assembly != core && name == core.GetName().Name;
                Assert.True(runtime || sharedCore, $"Unexpected dependency from {assembly.GetName().Name}: {name}");
            }
    }
}
