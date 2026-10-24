using System.Text;
using Bathos.Platform.Files;
using Bathos.Platform.Linux;
using Bathos.Platform.MacOS;
using Bathos.Platform.Windows;
using Xunit;

namespace Bathos.Platform.Tests;

[Trait("Validation", "Native")]
public sealed class SnapshotPublicationTests
{
    private static ISnapshotFileRename Rename => Assert.IsAssignableFrom<ISnapshotFileRename>(PlatformProviders.SelectCurrent(
        new WindowsPlatformProvider(), new MacOSPlatformProvider(), new LinuxPlatformProvider()).SnapshotFileRename);

    [Fact]
    public async Task FailedOpenPreservesBothFilesAndReleasesItsHandleForRetry()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Requires Windows file-sharing enforcement.");
        var folder = Directory.CreateTempSubdirectory("bathos-snapshot-");
        try
        {
            var source = Path.Combine(folder.FullName, "source");
            var destination = Path.Combine(folder.FullName, "destination");
            await File.WriteAllTextAsync(source, "new", TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(destination, "old", TestContext.Current.CancellationToken);
            using (var held = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Assert.Equal(FileRenameStatus.Unavailable, Rename.Move(source, destination, out _, replaceExisting: true));
                Assert.Equal("new", await File.ReadAllTextAsync(source, TestContext.Current.CancellationToken));
                Assert.Equal("old", await File.ReadAllTextAsync(destination, TestContext.Current.CancellationToken));
            }
            Assert.Equal(FileRenameStatus.Success, Rename.Move(source, destination, out _, replaceExisting: true));
            using var exclusive = new FileStream(destination, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Assert.Equal(3, exclusive.Length);
        }
        finally { folder.Delete(true); }
    }

    [Fact]
    public async Task LongPathsAndRelativeRenameArgumentsPreserveThePublishedBytes()
    {
        var folder = Directory.CreateTempSubdirectory("bathos-snapshot-");
        try
        {
            var directory = Directory.CreateDirectory(Path.Combine(folder.FullName, new string('a', 120), new string('b', 120)));
            var source = Path.Combine(directory.FullName, "source");
            var destination = Path.Combine(directory.FullName, "destination");
            await File.WriteAllTextAsync(source, "new", TestContext.Current.CancellationToken);
            Assert.Equal(FileRenameStatus.Success, Rename.Move(Path.GetRelativePath(Environment.CurrentDirectory, source),
                Path.GetRelativePath(Environment.CurrentDirectory, destination), out _));
            Assert.Equal("new", await File.ReadAllTextAsync(destination, TestContext.Current.CancellationToken));
        }
        finally { folder.Delete(true); }
    }

    [Fact]
    public async Task RetainedReaderFinishesOldSnapshotAndLaterReadersSeeReplacement()
    {
        var folder = Directory.CreateTempSubdirectory("bathos-snapshot-");
        var token = TestContext.Current.CancellationToken;
        try
        {
            var path = Path.Combine(folder.FullName, "snapshot-桜.json");
            var files = new FilePublicationFactory(Rename);
            await files.WriteAsync(path, "old"u8.ToArray(), false, false, token);
            await using (var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            {
                await files.WriteAsync(path, "new"u8.ToArray(), true, false, token);
                using var text = new StreamReader(reader, Encoding.UTF8, leaveOpen: true);
                Assert.Equal("old", await text.ReadToEndAsync(token));
                Assert.Equal("new", await File.ReadAllTextAsync(path, token));
            }
            Assert.Equal([path], Directory.GetFiles(folder.FullName));
        }
        finally { folder.Delete(true); }
    }

    [Fact]
    public async Task ExclusiveCollisionAndCancellationPreserveOriginalAndCleanStaging()
    {
        var folder = Directory.CreateTempSubdirectory("bathos-snapshot-");
        var token = TestContext.Current.CancellationToken;
        try
        {
            var path = Path.Combine(folder.FullName, "snapshot.json");
            var files = new FilePublicationFactory(Rename);
            await files.WriteAsync(path, "old"u8.ToArray(), true, false, token);
            await Assert.ThrowsAsync<IOException>(() => files.WriteAsync(path, "new"u8.ToArray(), false, false, token).AsTask());
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => files.WriteAsync(path, "new"u8.ToArray(), true, false, canceled.Token).AsTask());
            Assert.Equal("old", await File.ReadAllTextAsync(path, token));
            Assert.Equal([path], Directory.GetFiles(folder.FullName));
        }
        finally { folder.Delete(true); }
    }

    [Fact]
    public async Task MissingDirectoryRequiresExplicitCreationAndFailedPublicationCleansStaging()
    {
        var folder = Directory.CreateTempSubdirectory("bathos-snapshot-");
        var token = TestContext.Current.CancellationToken;
        try
        {
            var directory = Path.Combine(folder.FullName, "missing");
            var path = Path.Combine(directory, "snapshot.json");
            var files = new FilePublicationFactory(Rename);
            await Assert.ThrowsAsync<DirectoryNotFoundException>(() => files.WriteAsync(path, "new"u8.ToArray(), false, false, token).AsTask());
            Assert.False(Directory.Exists(directory));
            await files.WriteAsync(path, "new"u8.ToArray(), false, true, token);
            Assert.Equal("new", await File.ReadAllTextAsync(path, token));
            var occupied = Directory.CreateDirectory(Path.Combine(directory, "occupied"));
            await Assert.ThrowsAsync<IOException>(() => files.WriteAsync(occupied.FullName, "new"u8.ToArray(), true, false, token).AsTask());
            Assert.Empty(occupied.EnumerateFileSystemInfos());
            Assert.Equal([path], Directory.GetFiles(directory));
        }
        finally { folder.Delete(true); }
    }
}
