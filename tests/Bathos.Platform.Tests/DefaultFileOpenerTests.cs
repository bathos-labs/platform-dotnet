using Bathos.Platform.Desktop;
using Bathos.Platform.Linux;
using Bathos.Platform.MacOS;
using Bathos.Platform.Windows;
using Xunit;

namespace Bathos.Platform.Tests;

public sealed class DefaultFileOpenerTests
{
    [Fact]
    [Trait("Validation", "Portable")]
    public void CapabilityIsAdditiveAndConstructionDoesNotLoadNativeServices()
    {
        IPlatformProvider minimal = new MinimalProvider();
        Assert.Null(minimal.DefaultFileOpener);
        Assert.IsType<MacOSDefaultFileOpener>(new MacOSPlatformProvider().DefaultFileOpener);
        Assert.IsType<WindowsDefaultFileOpener>(new WindowsPlatformProvider().DefaultFileOpener);
        Assert.IsType<LinuxDefaultFileOpener>(new LinuxPlatformProvider().DefaultFileOpener);
    }

    [Fact]
    [Trait("Validation", "Native")]
    public async Task RejectsInvalidFilesAndPreCancellationWithoutLaunchingApplications()
    {
        var platform = PlatformProviders.SelectCurrent(new MacOSPlatformProvider(), new WindowsPlatformProvider(), new LinuxPlatformProvider());
        var opener = Assert.IsAssignableFrom<IDefaultFileOpener>(platform.DefaultFileOpener);
        var token = TestContext.Current.CancellationToken;
        var directory = Directory.CreateTempSubdirectory("bathos-default-open-");
        try
        {
            var path = Path.Combine(directory.FullName, "日本語 file # & %.txt");
            await File.WriteAllTextAsync(path, "owned test document", token);
            await Assert.ThrowsAsync<ArgumentNullException>(() => opener.OpenAsync(null!, token));
            await Assert.ThrowsAsync<ArgumentException>(() => opener.OpenAsync("relative.txt", token));
            await Assert.ThrowsAsync<ArgumentException>(() => opener.OpenAsync(path + '\0', token));
            await Assert.ThrowsAnyAsync<IOException>(() => opener.OpenAsync(directory.FullName, token));
            await Assert.ThrowsAnyAsync<IOException>(() => opener.OpenAsync(path + ".missing", token));
            if (!OperatingSystem.IsWindows())
            {
                var link = Path.Combine(directory.FullName, "link.txt");
                File.CreateSymbolicLink(link, path);
                await Assert.ThrowsAnyAsync<IOException>(() => opener.OpenAsync(link, token));
                var dangling = Path.Combine(directory.FullName, "dangling.txt");
                File.CreateSymbolicLink(dangling, path + ".missing");
                await Assert.ThrowsAnyAsync<IOException>(() => opener.OpenAsync(dangling, token));
            }
            using var canceled = new CancellationTokenSource(); canceled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => opener.OpenAsync(path, canceled.Token));
            Assert.Equal("owned test document", await File.ReadAllTextAsync(path, token));
        }
        finally { directory.Delete(true); }
    }

    [Fact]
    [Trait("Validation", "Portable")]
    public async Task ForeignProvidersRejectTheHostBeforeLoadingNativeServices()
    {
        var path = Path.GetFullPath("unused-file");
        var openers = new (bool IsHost, IDefaultFileOpener Opener)[]
        {
            (OperatingSystem.IsMacOS(), new MacOSDefaultFileOpener()),
            (OperatingSystem.IsWindows(), new WindowsDefaultFileOpener()),
            (OperatingSystem.IsLinux(), new LinuxDefaultFileOpener())
        };
        foreach (var (isHost, opener) in openers)
            if (!isHost) await Assert.ThrowsAsync<PlatformNotSupportedException>(() => opener.OpenAsync(path, TestContext.Current.CancellationToken));
    }

    private sealed class MinimalProvider : IPlatformProvider
    {
        public PlatformOperatingSystem OperatingSystem => PlatformOperatingSystem.MacOS;
    }
}
