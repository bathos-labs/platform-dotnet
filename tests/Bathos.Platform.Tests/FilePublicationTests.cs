using System.Text;
using Bathos.Platform.Files;
using Bathos.Platform.Paths;
using Xunit;

namespace Bathos.Platform.Tests;

public sealed class FilePublicationTests
{
    private static INativeFileRename NativeRename => PlatformProviders.SelectCurrent(
        new Bathos.Platform.MacOS.MacOSPlatformProvider(), new Bathos.Platform.Windows.WindowsPlatformProvider(),
        new Bathos.Platform.Linux.LinuxPlatformProvider()).FileRename ?? throw new PlatformNotSupportedException();
    [Theory]
    [InlineData("Spring drawing", null, "Spring drawing.png")]
    [InlineData("Spring drawing", "Clipboard", "Spring drawing - Clipboard.png")]
    [InlineData("Spring drawing", "Mask", "Spring drawing - Mask.png")]
    [InlineData("Spring drawing", " ", "Spring drawing.png")]
    [InlineData("../Drawing: front/back?", "Paste", "Drawing- front-back - Paste.png")]
    [InlineData("front\\back", null, "front-back.png")]
    [InlineData("CON", null, "_CON.png")]
    [InlineData("lpt1.notes", null, "_lpt1.notes.png")]
    [InlineData("...", null, "File.png")]
    [InlineData("桜 café", "Derived", "桜 café - Derived.png")]
    [Trait("Validation", "Portable")]
    public void NamesKeepReadableComponentsAndNormalizePortableStems(string baseName, string? kind, string expected)
    {
        var name = new GeneratedFileName(baseName, ".png", kind);
        Assert.Equal(expected, name.Candidate(1));
        Assert.Equal(Path.GetFileNameWithoutExtension(expected) + " (2).png", name.Candidate(2));
        foreach (var sequence in new[] { 1, 2, int.MaxValue })
        {
            var candidate = name.Candidate(sequence);
            Assert.True(PortableFileNames.IsValid(candidate), $"Generated candidate is not portable: {candidate}");
        }
    }

    [Fact]
    [Trait("Validation", "Portable")]
    public void NamesRequireABaseAndSimpleExtensionAndBoundUnicodeLength()
    {
        Assert.Throws<ArgumentException>(() => new GeneratedFileName(" ", ".png"));
        Assert.Throws<ArgumentException>(() => new GeneratedFileName("Drawing", "png"));
        Assert.Throws<ArgumentException>(() => new GeneratedFileName("Drawing", ".png/other"));
        const string extension = ".abcdefghijklmnop";
        var name = new GeneratedFileName(new string('桜', 300), extension, new string('é', 300));
        var candidate = name.Candidate(int.MaxValue);
        Assert.True(PortableFileNames.IsValid(candidate), $"Generated candidate is not portable: {candidate}");
        Assert.True(Encoding.UTF8.GetByteCount(candidate) < 255);
        Assert.EndsWith(" (2147483647)" + extension, candidate, StringComparison.Ordinal);
        Assert.DoesNotContain('�', candidate);
    }

    [Fact]
    [Trait("Validation", "Native")]
    public async Task ConcurrentPublishersClaimDistinctNamesAndPreserveEveryPayload()
    {
        var token = TestContext.Current.CancellationToken;
        var folder = Directory.CreateTempSubdirectory("bathos-file-publication-");
        try
        {
            const int count = 24;
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var staged = 0;
            var tasks = Enumerable.Range(0, count).Select(index => Task.Run(async () =>
            {
                await using var publication = new FilePublication(folder.FullName, NativeRename);
                var bytes = Encoding.UTF8.GetBytes("Complete payload " + index);
                await publication.Stream.WriteAsync(bytes, token);
                if (Interlocked.Increment(ref staged) == count) ready.SetResult();
                await start.Task.WaitAsync(token);
                var path = await publication.PublishAsync(new("Drawing", ".png", "Clipboard"), token);
                return (Path: path, Bytes: bytes);
            }, token)).ToArray();
            await ready.Task.WaitAsync(token);
            // None of the final names exists while producers are still staging bytes.
            Assert.Empty(Directory.EnumerateFiles(folder.FullName, "*.png"));
            start.SetResult();
            var results = await Task.WhenAll(tasks);
            Assert.Equal(count, results.Select(result => result.Path).Distinct(StringComparer.Ordinal).Count());
            Assert.Equal(count, Directory.GetFileSystemEntries(folder.FullName).Length);
            Assert.True(File.Exists(Path.Combine(folder.FullName, "Drawing - Clipboard.png")));
            for (var index = 2; index <= count; index++)
                Assert.True(File.Exists(Path.Combine(folder.FullName, $"Drawing - Clipboard ({index}).png")));
            foreach (var result in results) Assert.Equal(result.Bytes, await File.ReadAllBytesAsync(result.Path, token));
        }
        finally { folder.Delete(true); }
    }

    [Fact]
    [Trait("Validation", "Native")]
    public async Task PublicationSkipsOccupiedCandidatesAndReusesTheFirstGap()
    {
        var token = TestContext.Current.CancellationToken;
        var folder = Directory.CreateTempSubdirectory("bathos-file-publication-");
        try
        {
            foreach (var name in new[] { "Drawing.png", "Drawing (2).png", "Drawing (4).png" })
                await File.WriteAllTextAsync(Path.Combine(folder.FullName, name), "existing", token);
            await using (var publication = new FilePublication(folder.FullName, NativeRename))
            {
                await publication.Stream.WriteAsync(new byte[] { 1, 2, 3 }, token);
                var path = await publication.PublishAsync(new("Drawing", ".png"), token);
                Assert.Equal("Drawing (3).png", Path.GetFileName(path));
                Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(path, token));
            }
            foreach (var name in new[] { "Drawing.png", "Drawing (2).png", "Drawing (4).png" })
                Assert.Equal("existing", await File.ReadAllTextAsync(Path.Combine(folder.FullName, name), token));
            Assert.Equal(4, Directory.GetFileSystemEntries(folder.FullName).Length);
        }
        finally { folder.Delete(true); }
    }

    [Fact]
    [Trait("Validation", "Native")]
    public async Task PosixPublicationTreatsDirectoriesAndDanglingLinksAsOccupiedNames()
    {
        if (OperatingSystem.IsWindows()) Assert.Skip("This symlink scenario requires a POSIX host.");
        var token = TestContext.Current.CancellationToken;
        var folder = Directory.CreateTempSubdirectory("bathos-file-publication-");
        try
        {
            var occupiedDirectory = Directory.CreateDirectory(Path.Combine(folder.FullName, "Drawing.png"));
            var missingTarget = Path.Combine(folder.FullName, "missing.png");
            var link = File.CreateSymbolicLink(Path.Combine(folder.FullName, "Drawing (2).png"), missingTarget);
            await using (var publication = new FilePublication(folder.FullName, NativeRename))
            {
                await publication.Stream.WriteAsync(new byte[] { 1, 2, 3 }, token);
                Assert.Equal(Path.Combine(folder.FullName, "Drawing (3).png"), await publication.PublishAsync(new("Drawing", ".png"), token));
            }
            Assert.True(occupiedDirectory.Exists);
            Assert.Equal(missingTarget, new FileInfo(link.FullName).LinkTarget);
            Assert.False(File.Exists(missingTarget));
            Assert.Equal(3, Directory.GetFileSystemEntries(folder.FullName).Length);
        }
        finally { folder.Delete(true); }
    }

    [Fact]
    [Trait("Validation", "Native")]
    public async Task CompletedPublicationRetainsItsFileAndCannotPublishAgain()
    {
        var token = TestContext.Current.CancellationToken;
        var folder = Directory.CreateTempSubdirectory("bathos-file-publication-");
        try
        {
            string path;
            await using (var publication = new FilePublication(folder.FullName, NativeRename))
            {
                await publication.Stream.WriteAsync(new byte[] { 1, 2, 3 }, token);
                path = await publication.PublishAsync(new("Drawing", ".png"), token);
                await Assert.ThrowsAsync<InvalidOperationException>(() => publication.PublishAsync(new("Other", ".png"), token));
            }
            Assert.Equal(path, Assert.Single(Directory.GetFileSystemEntries(folder.FullName)));
            Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(path, token));
        }
        finally { folder.Delete(true); }
    }

    [Fact]
    [Trait("Validation", "Native")]
    public async Task ExactPublicationRejectsADestinationCreatedWhileEncoding()
    {
        var token = TestContext.Current.CancellationToken;
        var folder = Directory.CreateTempSubdirectory("bathos-file-publication-");
        try
        {
            var destination = Path.Combine(folder.FullName, "Drawing.png");
            await using (var publication = new FilePublication(folder.FullName, NativeRename))
            {
                await publication.Stream.WriteAsync(new byte[] { 1, 2, 3 }, token);
                await File.WriteAllTextAsync(destination, "another publisher", token);
                await Assert.ThrowsAsync<IOException>(() => publication.PublishExactAsync("Drawing.png", token));
            }
            Assert.Equal(destination, Assert.Single(Directory.GetFileSystemEntries(folder.FullName)));
            Assert.Equal("another publisher", await File.ReadAllTextAsync(destination, token));
        }
        finally { folder.Delete(true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Validation", "Native")]
    public async Task AuthorizedReplacementPreservesDestinationUntilPublication(bool cancel)
    {
        var token = TestContext.Current.CancellationToken;
        var folder = Directory.CreateTempSubdirectory("bathos-replacement-");
        try
        {
            var destination = Path.Combine(folder.FullName, "Drawing.png");
            await File.WriteAllTextAsync(destination, "original", token);
            await using (var publication = new FilePublication(folder.FullName, NativeRename))
            {
                await publication.Stream.WriteAsync("replacement"u8.ToArray(), token);
                Assert.Equal("original", await File.ReadAllTextAsync(destination, token));
                using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
                if (cancel) cancellation.Cancel();
                if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                    publication.PublishExactAsync("Drawing.png", cancellation.Token, replaceExisting: true));
                else Assert.Equal(destination, await publication.PublishExactAsync("Drawing.png", token, replaceExisting: true));
            }
            Assert.Equal(destination, Assert.Single(Directory.GetFileSystemEntries(folder.FullName)));
            Assert.Equal(cancel ? "original" : "replacement", await File.ReadAllTextAsync(destination, token));
        }
        finally { folder.Delete(true); }
    }

    [Fact]
    [Trait("Validation", "Portable")]
    public async Task AbandonedAndCanceledPublicationsRemoveOnlyTheirTemporaryFiles()
    {
        var token = TestContext.Current.CancellationToken;
        var folder = Directory.CreateTempSubdirectory("bathos-file-publication-");
        try
        {
            var unrelated = Path.Combine(folder.FullName, ".bathos-unrelated.tmp");
            await File.WriteAllTextAsync(unrelated, "keep", token);
            await using (var abandoned = new FilePublication(folder.FullName, new UnexpectedRename()))
                await abandoned.Stream.WriteAsync(new byte[] { 1 }, token);
            await using (var canceled = new FilePublication(folder.FullName, new UnexpectedRename()))
            {
                await canceled.Stream.WriteAsync(new byte[] { 2 }, token);
                using var cancellation = new CancellationTokenSource();
                cancellation.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled.PublishAsync(new("Drawing", ".png"), cancellation.Token));
            }
            Assert.Equal(unrelated, Assert.Single(Directory.GetFileSystemEntries(folder.FullName)));
            Assert.Equal("keep", await File.ReadAllTextAsync(unrelated, token));
        }
        finally { folder.Delete(true); }
    }

    [Fact]
    [Trait("Validation", "Native")]
    public async Task NonCollisionFailuresSurfaceAndExactNamesCannotEscapeTheDirectory()
    {
        var token = TestContext.Current.CancellationToken;
        var folder = Directory.CreateTempSubdirectory("bathos-file-publication-");
        try
        {
            await using var publication = new FilePublication(folder.FullName, NativeRename);
            await Assert.ThrowsAsync<ArgumentException>(() => publication.PublishExactAsync("../escape.png", token));
            if (OperatingSystem.IsWindows())
                await Assert.ThrowsAsync<ArgumentException>(() => publication.PublishExactAsync("..\\escape.png", token));
            await Assert.ThrowsAsync<ArgumentException>(() => publication.PublishExactAsync("bad\0name.png", token));
            await Assert.ThrowsAsync<IOException>(() => publication.PublishExactAsync(new string('x', 300) + ".png", token));
            Assert.Empty(Directory.EnumerateFiles(folder.FullName, "*.png"));
        }
        finally { folder.Delete(true); }
    }

    [Theory]
    [InlineData("literal\\name.txt")]
    [InlineData("literal:name.txt")]
    [Trait("Validation", "Native")]
    public async Task ExactNamesPreserveNativeCharactersWithoutApplyingPortableNamingRules(string name)
    {
        var token = TestContext.Current.CancellationToken;
        var folder = Directory.CreateTempSubdirectory("bathos-native-name-");
        try
        {
            await using var publication = new FilePublication(folder.FullName, NativeRename);
            await publication.Stream.WriteAsync("complete"u8.ToArray(), token);
            if (OperatingSystem.IsWindows())
            {
                await Assert.ThrowsAsync<ArgumentException>(() => publication.PublishExactAsync(name, token));
                Assert.True(publication.Stream.CanWrite);
            }
            else
            {
                var destination = Path.Combine(folder.FullName, name);
                Assert.Equal(destination, await publication.PublishExactAsync(name, token));
                Assert.Equal("complete", await File.ReadAllTextAsync(destination, token));
                Assert.Equal(destination, Assert.Single(Directory.EnumerateFiles(folder.FullName)));
            }
        }
        finally { folder.Delete(true); }
    }
    [Fact]
    [Trait("Validation", "Native")]
    public async Task SuccessfulPublicationRelinquishesTheStagingPath()
    {
        var folder = Directory.CreateTempSubdirectory("bathos-ownership-");
        try
        {
            await using var publication = new FilePublication(folder.FullName, NativeRename);
            var staging = publication.Stream.Name;
            await publication.Stream.WriteAsync("published"u8.ToArray(), TestContext.Current.CancellationToken);
            var output = await publication.PublishExactAsync("output.txt", TestContext.Current.CancellationToken);
            File.WriteAllText(staging, "another owner");
            await publication.DisposeAsync();
            Assert.Equal("another owner", File.ReadAllText(staging));
            Assert.Equal("published", File.ReadAllText(output));
        }
        finally { folder.Delete(true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Validation", "Portable")]
    public async Task StagingNameCannotBePublishedEvenWithReplacement(bool upperCase)
    {
        var folder = Directory.CreateTempSubdirectory("bathos-ownership-");
        try
        {
            await using var publication = new FilePublication(folder.FullName, new UnexpectedRename());
            var name = Path.GetFileName(publication.Stream.Name);
            if (upperCase) name = name.ToUpperInvariant();
            await Assert.ThrowsAsync<ArgumentException>(() => publication.PublishExactAsync(name,
                TestContext.Current.CancellationToken, replaceExisting: true));
            Assert.True(publication.Stream.CanWrite);
        }
        finally { folder.Delete(true); }
    }

    [Fact]
    [Trait("Validation", "Portable")]
    public async Task RepeatedDisposalDoesNotReclaimAnAbandonedStagingPath()
    {
        var folder = Directory.CreateTempSubdirectory("bathos-ownership-");
        try
        {
            await using var publication = new FilePublication(folder.FullName, new UnexpectedRename());
            var staging = publication.Stream.Name;
            await publication.DisposeAsync();
            Assert.False(File.Exists(staging));
            File.WriteAllText(staging, "another owner");
            await publication.DisposeAsync();
            Assert.Equal("another owner", File.ReadAllText(staging));
        }
        finally { folder.Delete(true); }
    }

    private sealed class UnexpectedRename : INativeFileRename
    {
        public FileRenameStatus Move(string source, string destination, out int errorCode, bool replaceExisting = false) =>
            throw new InvalidOperationException("Canceled or abandoned staging must not invoke native rename.");
    }
}
