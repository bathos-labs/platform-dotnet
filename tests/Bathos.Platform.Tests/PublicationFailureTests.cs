using System.ComponentModel;
using Bathos.Platform.Files;
using Xunit;

namespace Bathos.Platform.Tests;

[Trait("Validation", "Portable")]
public sealed class PublicationFailureTests
{
    [Fact]
    public async Task StagingRecognitionUsesTheActualPublisherNameWithoutInferringLiveness()
    {
        var folder = Directory.CreateTempSubdirectory("bathos-staging-name-");
        try
        {
            string staging;
            await using (var publication = new FilePublication(folder.FullName, new Rename((_, _, _) => FileRenameStatus.Unavailable)))
            {
                staging = publication.Stream.Name;
                Assert.True(FilePublication.IsStagingFileName(Path.GetFileName(staging)));
                Assert.False(FilePublication.IsStagingFileName(staging));
            }
            Assert.False(File.Exists(staging));
            Assert.True(FilePublication.IsStagingFileName(Path.GetFileName(staging)));
        }
        finally { folder.Delete(true); }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unrelated.tmp")]
    [InlineData("0123456789abcdef0123456789abcdef.tmp")]
    [InlineData(".bathos-not-a-guid.tmp")]
    [InlineData(".bathos-0123456789abcdef0123456789abcdef.txt")]
    public void StagingRecognitionRejectsUnrelatedNames(string? name) => Assert.False(FilePublication.IsStagingFileName(name));

    [Theory]
    [InlineData(FileRenameStatus.CrossVolumeUnsupported)]
    [InlineData(FileRenameStatus.Unavailable)]
    public async Task NoncollisionFailureIsNotRetriedAndKeepsStagingOwnedUntilDisposal(FileRenameStatus status)
    {
        var folder = Directory.CreateTempSubdirectory("bathos-publication-failure-");
        try
        {
            var unrelated = Path.Combine(folder.FullName, ".bathos-unrelated.tmp");
            File.WriteAllText(unrelated, "other owner");
            var calls = 0;
            var rename = new Rename((_, _, _) => { calls++; return status; });
            await using var publication = new FilePublicationFactory(rename).Create(folder.FullName);
            var staging = publication.Stream.Name;
            await publication.Stream.WriteAsync("complete payload"u8.ToArray(), TestContext.Current.CancellationToken);
            var error = await Assert.ThrowsAsync<IOException>(() => publication.PublishAsync(new("output", ".txt"), CancellationToken.None));
            Assert.Equal(123, Assert.IsType<Win32Exception>(error.InnerException).NativeErrorCode);
            Assert.Equal(1, calls);
            Assert.False(publication.Stream.CanWrite);
            Assert.Equal("complete payload", File.ReadAllText(staging));
            await Assert.ThrowsAsync<InvalidOperationException>(() => publication.PublishExactAsync("retry.txt", CancellationToken.None));
            Assert.False(File.Exists(Path.Combine(folder.FullName, "output.txt")));
            await publication.DisposeAsync();
            Assert.Equal(unrelated, Assert.Single(Directory.GetFiles(folder.FullName)));
            Assert.Equal("other owner", File.ReadAllText(unrelated));
        }
        finally { folder.Delete(true); }
    }

    [Fact]
    public async Task CancellationBetweenCollisionsStopsBeforeAnotherRenameAndPreservesTheOccupiedFile()
    {
        var folder = Directory.CreateTempSubdirectory("bathos-publication-cancel-");
        using var cancellation = new CancellationTokenSource();
        try
        {
            var occupied = Path.Combine(folder.FullName, "output.txt");
            File.WriteAllText(occupied, "existing owner");
            var calls = 0;
            var rename = new Rename((source, destination, replace) =>
            {
                calls++;
                Assert.Equal(occupied, destination);
                Assert.False(replace);
                Assert.Equal("payload", File.ReadAllText(source)); // Staging is flushed and closed before native submission.
                cancellation.Cancel();
                return FileRenameStatus.DestinationExists;
            });
            await using var publication = new FilePublication(folder.FullName, rename);
            await publication.Stream.WriteAsync("payload"u8.ToArray(), TestContext.Current.CancellationToken);
            var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => publication.PublishAsync(new("output", ".txt"), cancellation.Token));
            Assert.Equal(cancellation.Token, error.CancellationToken);
            Assert.Equal(1, calls);
            Assert.True(File.Exists(publication.Stream.Name));
            await publication.DisposeAsync();
            Assert.Equal(occupied, Assert.Single(Directory.GetFiles(folder.FullName)));
            Assert.Equal("existing owner", File.ReadAllText(occupied));
        }
        finally { folder.Delete(true); }
    }

    [Fact]
    public async Task CancellationAfterRenameCannotReclaimPublishedOrReusedStagingPaths()
    {
        var folder = Directory.CreateTempSubdirectory("bathos-publication-commit-");
        using var cancellation = new CancellationTokenSource();
        try
        {
            var rename = new Rename((source, destination, replace) =>
            {
                File.Move(source, destination, replace); // Portable orchestration probe; native atomicity is tested separately.
                cancellation.Cancel();
                return FileRenameStatus.Success;
            });
            await using var publication = new FilePublication(folder.FullName, rename);
            var staging = publication.Stream.Name;
            await publication.Stream.WriteAsync("committed"u8.ToArray(), TestContext.Current.CancellationToken);
            var output = await publication.PublishExactAsync("output.txt", cancellation.Token);
            Assert.True(cancellation.IsCancellationRequested);
            File.WriteAllText(staging, "new staging owner");
            await publication.DisposeAsync();
            Assert.Equal("committed", File.ReadAllText(output));
            Assert.Equal("new staging owner", File.ReadAllText(staging));
        }
        finally { folder.Delete(true); }
    }

    [Fact]
    public async Task ThrownNativeFailureRetainsItsIdentityAndDoesNotPreventANewPublication()
    {
        var folder = Directory.CreateTempSubdirectory("bathos-publication-recovery-");
        try
        {
            var failure = new IOException("Native boundary failed.");
            var calls = 0;
            var rename = new Rename((source, destination, replace) =>
            {
                if (++calls == 1) throw failure;
                File.Move(source, destination, replace);
                return FileRenameStatus.Success;
            });
            var factory = new FilePublicationFactory(rename);
            await using (var failed = factory.Create(folder.FullName))
                Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => failed.PublishExactAsync("output.txt", CancellationToken.None)));
            Assert.Empty(Directory.GetFiles(folder.FullName));
            await using (var next = factory.Create(folder.FullName))
            {
                await next.Stream.WriteAsync("recovered"u8.ToArray(), TestContext.Current.CancellationToken);
                var output = await next.PublishExactAsync("output.txt", CancellationToken.None);
                Assert.Equal("recovered", File.ReadAllText(output));
            }
            Assert.Equal(2, calls);
            Assert.Single(Directory.GetFiles(folder.FullName));
        }
        finally { folder.Delete(true); }
    }

    [Fact]
    public async Task FlushFailureNeverSubmitsRenameAndDisposalDoesNotRemoveUnownedDirectories()
    {
        var folder = Directory.CreateTempSubdirectory("bathos-publication-flush-");
        try
        {
            await using var publication = new FilePublication(folder.FullName,
                new Rename((_, _, _) => throw new InvalidOperationException("Rename must not follow a flush failure.")));
            var staging = publication.Stream.Name;
            await publication.Stream.DisposeAsync();
            await Assert.ThrowsAsync<ObjectDisposedException>(() => publication.PublishExactAsync("output.txt", CancellationToken.None));
            File.Delete(staging);
            Directory.CreateDirectory(staging);
            File.WriteAllText(Path.Combine(staging, "other-owner.txt"), "keep");
            await publication.DisposeAsync(); // Cleanup cannot mask the original failure or recursively delete an entry.
            Assert.Equal("keep", File.ReadAllText(Path.Combine(staging, "other-owner.txt")));
        }
        finally { folder.Delete(true); }
    }

    private sealed class Rename(Func<string, string, bool, FileRenameStatus> move) : INativeFileRename
    {
        public FileRenameStatus Move(string source, string destination, out int errorCode, bool replaceExisting = false)
        {
            errorCode = 123;
            return move(source, destination, replaceExisting);
        }
    }
}
