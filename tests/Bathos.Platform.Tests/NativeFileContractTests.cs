using Bathos.Platform.Files;
using Bathos.Platform.Linux;
using Bathos.Platform.MacOS;
using Bathos.Platform.Windows;
using Xunit;

namespace Bathos.Platform.Tests;

/// <summary>Runs real native operations on the current host using disposable files; safe for unattended native runners.</summary>
public sealed class NativeFileContractTests
{
    private static IPlatformProvider Current() => PlatformProviders.SelectCurrent(
        new MacOSPlatformProvider(), new WindowsPlatformProvider(), new LinuxPlatformProvider());

    [Fact]
    public void FileInspectionAdmitsRegularFilesAndRejectsDirectoriesAndMissingEntries()
    {
        var probe = Current().FileEntries;
        Assert.NotNull(probe);
        var directory = Directory.CreateTempSubdirectory("bathos-native-");
        try
        {
            var file = Path.Combine(directory.FullName, "source.txt");
            File.WriteAllText(file, "source");
            probe.RequireRegularFile(file);
            Assert.ThrowsAny<IOException>(() => probe.RequireRegularFile(directory.FullName));
            Assert.ThrowsAny<IOException>(() => probe.RequireRegularFile(Path.Combine(directory.FullName, "missing")));
        }
        finally { directory.Delete(true); }
    }

    [Fact]
    public void EmbeddedNullPathsAreRejectedWithoutInspectingOrRenamingTheirPrefix()
    {
        var provider = Current();
        var probe = provider.FileEntries;
        Assert.NotNull(probe);
        var directory = Directory.CreateTempSubdirectory("bathos-native-");
        try
        {
            var source = Path.Combine(directory.FullName, "source.txt");
            var destination = Path.Combine(directory.FullName, "destination.txt");
            File.WriteAllText(source, "source");
            Assert.Throws<ArgumentException>(() => probe.RequireRegularFile(source + "\0suffix"));
            if (provider.FileRename is { } rename)
            {
                Assert.Throws<ArgumentException>(() => rename.Move(source + "\0suffix", destination, out _));
                Assert.Throws<ArgumentException>(() => rename.Move(source, destination + "\0suffix", out _));
                Assert.Equal("source", File.ReadAllText(source));
                Assert.False(File.Exists(destination));
            }
        }
        finally { directory.Delete(true); }
    }

    [Fact]
    public void ExclusiveRenamePreservesBothFilesUntilReplacementIsExplicit()
    {
        var rename = Current().FileRename;
        if (rename is null) Assert.Skip("This host provider does not implement exclusive rename.");
        var directory = Directory.CreateTempSubdirectory("bathos-native-");
        try
        {
            var source = Path.Combine(directory.FullName, "source.txt");
            var destination = Path.Combine(directory.FullName, "destination.txt");
            File.WriteAllText(source, "new");
            File.WriteAllText(destination, "original");
            Assert.Equal(FileRenameStatus.DestinationExists, rename.Move(source, destination, out _));
            Assert.Equal("new", File.ReadAllText(source));
            Assert.Equal("original", File.ReadAllText(destination));
            Assert.Equal(FileRenameStatus.Success, rename.Move(source, destination, out var error, replaceExisting: true));
            Assert.Equal(0, error);
            Assert.False(File.Exists(source));
            Assert.Equal("new", File.ReadAllText(destination));
        }
        finally { directory.Delete(true); }
    }

    [Fact]
    public async Task PublicationCleansOnlyOwnedStagingAndRetainsPublishedBytes()
    {
        var rename = Current().FileRename;
        if (rename is null) Assert.Skip("This host provider does not implement native publication.");
        var directory = Directory.CreateTempSubdirectory("bathos-native-");
        try
        {
            var unrelated = Path.Combine(directory.FullName, "unrelated.txt");
            File.WriteAllText(unrelated, "keep");
            await using (var abandoned = new FilePublication(directory.FullName, rename))
                await abandoned.Stream.WriteAsync(new byte[] { 1, 2, 3 }, TestContext.Current.CancellationToken);
            Assert.Equal([unrelated], Directory.GetFiles(directory.FullName));
            string published;
            await using (var publication = new FilePublication(directory.FullName, rename))
            {
                await publication.Stream.WriteAsync(new byte[] { 4, 5, 6 }, TestContext.Current.CancellationToken);
                published = await publication.PublishExactAsync("published.bin", TestContext.Current.CancellationToken);
            }
            Assert.Equal(new byte[] { 4, 5, 6 }, await File.ReadAllBytesAsync(published, TestContext.Current.CancellationToken));
            Assert.Equal("keep", File.ReadAllText(unrelated));
            Assert.Equal(2, Directory.GetFiles(directory.FullName).Length);
        }
        finally { directory.Delete(true); }
    }
}
