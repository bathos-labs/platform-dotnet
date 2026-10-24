using Bathos.Platform.Directories;
using Bathos.Platform.Linux;
using Bathos.Platform.MacOS;
using Bathos.Platform.Paths;
using Bathos.Platform.Windows;
using Xunit;

namespace Bathos.Platform.Tests;

public sealed class StandardDirectoryTests
{
    private static NativeFilePaths Paths => OperatingSystem.IsWindows() ? NativeFilePaths.Windows : NativeFilePaths.Posix;
    private static string Root => Path.Combine(Path.GetTempPath(), "directory-policy-fixture");

    [Fact]
    [Trait("Validation", "Portable")]
    public void LayoutsSeparateDiscoveryFromApplicationIdentityWithoutCreatingDirectories()
    {
        var home = Path.Combine(Root, Guid.NewGuid().ToString("N"));
        string Folder(Environment.SpecialFolder folder) => folder switch
        {
            Environment.SpecialFolder.UserProfile => home,
            Environment.SpecialFolder.LocalApplicationData => Path.Combine(home, "local"),
            Environment.SpecialFolder.ApplicationData => Path.Combine(home, "roaming"),
            _ => "",
        };
        var identity = new ApplicationDirectoryIdentity("Product", "Publisher");
        foreach (var host in new[] { PlatformOperatingSystem.Windows, PlatformOperatingSystem.MacOS, PlatformOperatingSystem.Linux })
        {
            var roots = DirectoryDiscovery.Resolve(host, Paths, Folder, _ => null, Path.GetTempPath(), AppContext.BaseDirectory);
            var application = roots.ForApplication(identity, Paths);
            Assert.NotNull(application.Data);
            Assert.NotNull(application.Cache);
            Assert.Null(roots.Documents);
            Assert.Equal(Paths.NormalizeAbsolute(AppContext.BaseDirectory), roots.ApplicationBase);
            var expectedCache = host switch
            {
                PlatformOperatingSystem.Windows => Path.Combine(application.Data, "Cache"),
                PlatformOperatingSystem.MacOS => Path.Combine(home, "Library", "Caches", "Publisher", "Product"),
                _ => Path.Combine(home, ".cache", "Publisher", "Product"),
            };
            Assert.Equal(expectedCache, application.Cache);
            Assert.Equal(host == PlatformOperatingSystem.Windows ? Path.Combine(home, "roaming", "Publisher", "Product")
                : host == PlatformOperatingSystem.Linux ? Path.Combine(home, ".config", "Publisher", "Product") : application.Data,
                application.Configuration);
            Assert.False(Directory.Exists(home));
        }
    }

    [Fact]
    [Trait("Validation", "Portable")]
    public void XdgUsesAbsoluteOverridesAndIgnoresRelativeValuesWithoutChangingProcessEnvironment()
    {
        var home = Path.Combine(Root, "home");
        var configured = Path.Combine(Root, "custom # data");
        var roots = DirectoryDiscovery.Resolve(PlatformOperatingSystem.Linux, Paths,
            folder => folder == Environment.SpecialFolder.UserProfile ? home : "",
            key => key switch { "XDG_DATA_HOME" => configured, "XDG_CACHE_HOME" => "relative", "XDG_CONFIG_HOME" => "", "XDG_STATE_HOME" => "bad\0root", _ => null },
            "relative-temp", AppContext.BaseDirectory);
        Assert.Equal(configured, roots.Data);
        Assert.Equal(Path.Combine(home, ".cache"), roots.Cache);
        Assert.Equal(Path.Combine(home, ".config"), roots.Configuration);
        Assert.Equal(Path.Combine(home, ".local", "state"), roots.State);
        Assert.Null(roots.Temporary);
        var absent = DirectoryDiscovery.Resolve(PlatformOperatingSystem.Linux, Paths, _ => "", _ => null, "", "");
        Assert.Null(absent.Home);
        Assert.Null(absent.Data);
        Assert.Null(absent.ForApplication(new("Product"), Paths).Cache);
    }

    [Fact]
    [Trait("Validation", "Portable")]
    public void ExplicitRootsAndIdentityAreValidatedWithoutSanitizingOrUsingTheWorkingDirectory()
    {
        var roots = new StandardDirectoryRoots(PlatformOperatingSystem.Linux) { Data = Root };
        Assert.Equal(Path.Combine(Root, "Product"), roots.ForApplication(new("Product"), Paths).Data);
        Assert.Throws<ArgumentException>(() => (roots with { Data = "relative" }).ForApplication(new("Product"), Paths));
        Assert.Throws<ArgumentException>(() => new ApplicationDirectoryIdentity("../escape"));
        Assert.Throws<ArgumentException>(() => new ApplicationDirectoryIdentity("CON"));
        Assert.Throws<ArgumentException>(() => new ApplicationDirectoryIdentity("Product", ""));
        Assert.Throws<ArgumentException>(() => new ApplicationDirectoryIdentity("Product", "publisher/escape"));
        Assert.Throws<InvalidOperationException>(() => new StandardDirectoryRoots(PlatformOperatingSystem.Unknown).ForApplication(new("Product"), Paths));
        Assert.Null(((IPlatformProvider)new Minimal()).Directories);
    }

    [Fact]
    [Trait("Validation", "Native")]
    public void CurrentProviderDiscoversAbsoluteRootsAndForeignProvidersRejectInvocation()
    {
        IPlatformProvider[] providers = [new WindowsPlatformProvider(), new MacOSPlatformProvider(), new LinuxPlatformProvider()];
        foreach (var provider in providers)
        {
            var directories = provider.Directories;
            Assert.NotNull(directories);
            if (provider.OperatingSystem != PlatformHost.Current)
            {
                Assert.Throws<PlatformNotSupportedException>(() => directories.Discover());
                continue;
            }
            var roots = directories.Discover();
            Assert.Equal(PlatformHost.Current, roots.OperatingSystem);
            Assert.Equal(Paths.NormalizeAbsolute(AppContext.BaseDirectory), roots.ApplicationBase);
            foreach (var root in new[] { roots.Home, roots.Data, roots.Configuration, roots.Cache, roots.State, roots.Temporary, roots.Documents, roots.Desktop })
                if (root is not null) Assert.Equal(root, Paths.NormalizeAbsolute(root));
        }
    }

    private sealed class Minimal : IPlatformProvider
    {
        public PlatformOperatingSystem OperatingSystem => PlatformOperatingSystem.Unknown;
    }
}
