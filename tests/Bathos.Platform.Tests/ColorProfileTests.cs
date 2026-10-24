using System.Text;
using Bathos.Platform.ColorProfiles;
using Bathos.Platform.Linux;
using Bathos.Platform.MacOS;
using Bathos.Platform.Windows;
using Xunit;

namespace Bathos.Platform.Tests;

public sealed class ColorProfileTests
{
    private static readonly string[] Names = ["one.icc", "two.icm"];
    [Fact]
    [Trait("Validation", "Portable")]
    public void NativeNameBuffersRequireCompleteMatchingCountAndValidEncoding()
    {
        Assert.Equal(Names, WindowsColorProfiles.ParseNames(Encoding.Unicode.GetBytes("one.icc\0two.icm\0\0"), 2, 10));
        Assert.Throws<InvalidDataException>(() => WindowsColorProfiles.ParseNames(Encoding.Unicode.GetBytes("one.icc\0"), 2, 10));
        Assert.Throws<InvalidDataException>(() => WindowsColorProfiles.ParseNames(Encoding.Unicode.GetBytes("one.icc"), 1, 10));
        Assert.Throws<InvalidDataException>(() => WindowsColorProfiles.ParseNames([0], 1, 10));
        Assert.Throws<InvalidDataException>(() => WindowsColorProfiles.ParseNames([0x00, 0xD8, 0, 0], 1, 10));
        Assert.Empty(WindowsColorProfiles.ParseNames([0, 0], 0, 10));
        Assert.Throws<InvalidDataException>(() => WindowsColorProfiles.ParseNames(Encoding.Unicode.GetBytes("one.icc\0"), 1, 0));
    }

    [Fact]
    [Trait("Validation", "Portable")]
    public async Task CapabilityAbsenceAndCanceledCallsDoNotLoadNativeLibraries()
    {
        Assert.Null(((IPlatformProvider)new LinuxPlatformProvider()).ColorProfiles);
        Assert.Null(((IPlatformProvider)new PartialProvider()).ColorProfiles);
        Assert.Throws<ArgumentOutOfRangeException>(() => new ColorProfileAccessLimits(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ColorProfileAccessLimits(maximumBytes: 0));
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        IColorProfileProvider[] providers = [new MacColorProfileProvider(), new WindowsColorProfileProvider()];
        foreach (var provider in providers)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.EnumerateAsync(canceled.Token).AsTask());
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.ReadAsync("profile", canceled.Token).AsTask());
            await Assert.ThrowsAsync<ArgumentException>(() => provider.ReadAsync("bad\0key", TestContext.Current.CancellationToken).AsTask());
        }
    }

    [Fact]
    [Trait("Validation", "Native")]
    public async Task NativeInventoryReadsObservedBytesAndHonorsResourceLimits()
    {
        if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsWindows()) Assert.Skip("Requires ColorSync or WCS.");
        var token = TestContext.Current.CancellationToken;
        var provider = PlatformProviders.SelectCurrent(new MacOSPlatformProvider(), new WindowsPlatformProvider()).ColorProfiles;
        Assert.NotNull(provider);
        var inventory = await provider.EnumerateAsync(token);
        Assert.True(inventory.IsComplete, inventory.Finding);
        Assert.NotEmpty(inventory.Locations);
        var entry = inventory.Locations[0];
        var bytes = await provider.ReadAsync(entry.Key, token);
        Assert.NotEmpty(bytes.ToArray());
        IColorProfileProvider limited = OperatingSystem.IsMacOS()
            ? new MacColorProfileProvider(new(maximumEntries: 1, maximumBytes: 1))
            : new WindowsColorProfileProvider(new(maximumEntries: 1, maximumBytes: 1));
        await Assert.ThrowsAsync<InvalidDataException>(() => limited.ReadAsync(entry.Key, token).AsTask());
        Assert.Equal(bytes.ToArray(), (await provider.ReadAsync(entry.Key, token)).ToArray());
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".icc");
        await Assert.ThrowsAnyAsync<IOException>(() => provider.ReadAsync(missing, token).AsTask());
        Assert.Equal(bytes.ToArray(), (await provider.ReadAsync(entry.Key, token)).ToArray());
        var partial = await limited.EnumerateAsync(token);
        Assert.InRange(partial.Locations.Count, 0, 1);
        if (inventory.Locations.Count > 1) Assert.False(partial.IsComplete);
    }

    [Fact]
    [Trait("Validation", "Native")]
    public async Task MacReadPreservesBytesAtLimitAndReleasesFilesAfterFailure()
    {
        if (!OperatingSystem.IsMacOS()) Assert.Skip("Requires the macOS file-backed profile provider.");
        var token = TestContext.Current.CancellationToken;
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".icc");
        var provider = new MacColorProfileProvider(new(maximumBytes: 3));
        try
        {
            // Readback deliberately does not interpret ICC content.
            await File.WriteAllBytesAsync(path, new byte[] { 1, 2, 3 }, token);
            Assert.Equal(new byte[] { 1, 2, 3 }, (await provider.ReadAsync(path, token)).ToArray());
            await File.WriteAllBytesAsync(path, new byte[] { 1, 2, 3, 4 }, token);
            await Assert.ThrowsAsync<InvalidDataException>(() => provider.ReadAsync(path, token).AsTask());
            using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            await File.WriteAllBytesAsync(path, Array.Empty<byte>(), token);
            Assert.True((await provider.ReadAsync(path, token)).IsEmpty);
        }
        finally { File.Delete(path); }
    }

    private sealed class PartialProvider : IPlatformProvider
    {
        public PlatformOperatingSystem OperatingSystem => PlatformOperatingSystem.Unknown;
    }
}
