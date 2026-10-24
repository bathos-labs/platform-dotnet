using System.Diagnostics;
using System.Text.Json;
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
            Environment.SpecialFolder.InternetCache => Path.Combine(home, "native-cache"),
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
                PlatformOperatingSystem.MacOS => Path.Combine(home, "native-cache", "Publisher", "Product"),
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
    public void UnavailableHomeSuppressesDerivedRootsButPreservesIndependentOverrides()
    {
        var configured = Path.Combine(Root, "explicit-data");
        string Folder(Environment.SpecialFolder folder) => folder == Environment.SpecialFolder.UserProfile ? "" : "/runtime-fallback";
        var roots = DirectoryDiscovery.Resolve(PlatformOperatingSystem.Linux, Paths, Folder,
            key => key == "XDG_DATA_HOME" ? configured : null, "", "");
        Assert.Null(roots.Home);
        Assert.Equal(configured, roots.Data);
        Assert.Null(roots.Configuration);
        Assert.Null(roots.Cache);
        Assert.Null(roots.State);
        Assert.Null(roots.Documents);
        Assert.Null(roots.Desktop);
        var documents = DirectoryDiscovery.Resolve(PlatformOperatingSystem.Linux, Paths, Folder,
            key => key == "XDG_DOCUMENTS_DIR" ? configured : null, "", "");
        Assert.Equal(configured, documents.Documents);
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

    [Fact]
    [Trait("Validation", "Native")]
    public async Task MacCacheUsesNativeLocationEvenWhenHomeIsOverridden()
    {
        if (!OperatingSystem.IsMacOS()) Assert.Skip("Native cache selection requires macOS.");
        var folder = Directory.CreateTempSubdirectory("bathos-directories-");
        try
        {
            var home = Path.Combine(folder.FullName, "uncreated-home");
            var observed = await DiscoverInChildAsync(folder.FullName, home);
            Assert.Equal(home, observed.Roots.Home);
            if (observed.NativeCache is { Length: > 0 } nativeCache)
            {
                try { Assert.Equal(Paths.NormalizeAbsolute(nativeCache), observed.Roots.Cache); }
                // An invalid native result is unavailable under the path contract, even when nonempty.
                catch (ArgumentException) { Assert.Null(observed.Roots.Cache); }
            }
            else Assert.Null(observed.Roots.Cache);
            Assert.False(Directory.Exists(home));
        }
        finally { folder.Delete(true); }
    }

    [Fact]
    [Trait("Validation", "Native")]
    public async Task UnixHomeUsesTheEffectiveAccountWhenTheHomeEnvironmentIsUnset()
    {
        if (OperatingSystem.IsWindows()) Assert.Skip("Effective Unix account lookup requires macOS or Linux.");
        var folder = Directory.CreateTempSubdirectory("bathos-account-home-");
        try
        {
            var observed = await DiscoverInChildAsync(folder.FullName, null);
            if (observed.RuntimeHome is "" or "/") Assert.Skip("This host has no unambiguous independent runtime home to compare.");
            Assert.Equal(Paths.NormalizeAbsolute(observed.RuntimeHome), observed.Roots.Home);
            // Exercise the production ABI in this process too; coverage from the owned child is not collected here.
            var accountHome = UnixHomeDirectory.Resolve(null, OperatingSystem.IsMacOS()
                ? MacOSStandardDirectories.LookupAccount : LinuxStandardDirectories.LookupAccount);
            Assert.Equal(Paths.NormalizeAbsolute(observed.RuntimeHome), accountHome);
        }
        finally { folder.Delete(true); }
    }

    private static async Task<DirectoryObservation> DiscoverInChildAsync(string folder, string? home)
    {
        var configuration = Path.Combine(folder, "directories.json");
        await File.WriteAllTextAsync(configuration, JsonSerializer.Serialize(new { Mode = "standard-directories", PipeName = "" }), TestContext.Current.CancellationToken);
        var executable = Path.Combine(AppContext.BaseDirectory, "TestApplication", "Bathos.Platform.TestApplication" + (OperatingSystem.IsWindows() ? ".exe" : ""));
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(configuration);
        // Only this owned child observes the override; the test runner's process environment stays unchanged.
        if (home is null) start.Environment.Remove("HOME");
        else start.Environment["HOME"] = home;
        using var child = Process.Start(start) ?? throw new InvalidOperationException("Directory probe did not start.");
        var output = child.StandardOutput.ReadToEndAsync();
        var error = child.StandardError.ReadToEndAsync();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            await child.WaitForExitAsync(deadline.Token);
            Assert.True(child.ExitCode == 0, await error);
            return JsonSerializer.Deserialize<DirectoryObservation>(await output) ?? throw new InvalidDataException("Missing directory observation.");
        }
        finally
        {
            if (!child.HasExited) child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(output, error);
        }
    }

    private sealed record DirectoryObservation(StandardDirectoryRoots Roots, string RuntimeHome, string? NativeCache);
}
