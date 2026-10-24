using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Text.Json;
using Bathos.Platform.Files;
using Bathos.Platform.Linux;
using Bathos.Platform.MacOS;
using Bathos.Platform.Windows;
using Xunit;

namespace Bathos.Platform.Tests;

[Trait("Validation", "Native")]
public sealed class FileLeaseTests
{
    private static IFileLeaseProvider Current() => PlatformProviders.SelectCurrent(
        new MacOSPlatformProvider(), new WindowsPlatformProvider(), new LinuxPlatformProvider()).FileLeases
        ?? throw new InvalidOperationException("Missing native lease capability.");

    [Fact]
    public void SharedOwnersCoexistAndExcludeExclusiveOwnersUntilAllHaveReleased()
    {
        var folder = Directory.CreateTempSubdirectory("bathos-shared-leases-");
        try
        {
            var path = Path.Combine(folder.FullName, "writer.lock");
            var provider = Current();
            using (var first = provider.TryAcquire(path, FileLeaseMode.Shared, TestContext.Current.CancellationToken))
            {
                Assert.NotNull(first);
                using (var second = provider.TryAcquire(path, FileLeaseMode.Shared, TestContext.Current.CancellationToken))
                {
                    Assert.NotNull(second);
                    Assert.Null(Current().TryAcquire(path, FileLeaseMode.Exclusive, TestContext.Current.CancellationToken));
                }
                Assert.Null(Current().TryAcquire(path, FileLeaseMode.Exclusive, TestContext.Current.CancellationToken));
            }
            using var exclusive = Current().TryAcquire(path, FileLeaseMode.Exclusive, TestContext.Current.CancellationToken);
            Assert.NotNull(exclusive);
            Assert.Null(provider.TryAcquire(path, FileLeaseMode.Shared, TestContext.Current.CancellationToken));
            Assert.Throws<ArgumentOutOfRangeException>(() => provider.TryAcquire(path, (FileLeaseMode)100, TestContext.Current.CancellationToken));
        }
        finally { folder.Delete(true); }
    }

    [Fact]
    public async Task IndependentProvidersExcludeOneAnotherAndReleaseWithoutDeletingOrChangingBytes()
    {
        var folder = Directory.CreateTempSubdirectory("bathos-leases-");
        try
        {
            var path = Path.Combine(folder.FullName, "writer.lock");
            await File.WriteAllTextAsync(path, "stale metadata is not ownership", TestContext.Current.CancellationToken);
            var lease = Assert.IsAssignableFrom<IFileLease>(Current().TryAcquire(path, FileLeaseMode.Exclusive, TestContext.Current.CancellationToken));
            try { Assert.Null(Current().TryAcquire(path, FileLeaseMode.Exclusive, TestContext.Current.CancellationToken)); }
            finally { await lease.DisposeAsync(); }
            lease.Dispose();
            Assert.Equal("stale metadata is not ownership", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
            using var next = Current().TryAcquire(path, FileLeaseMode.Exclusive, TestContext.Current.CancellationToken);
            Assert.NotNull(next);
        }
        finally { folder.Delete(true); }
    }

    [Theory]
    [InlineData(FileLeaseMode.Shared)]
    [InlineData(FileLeaseMode.Exclusive)]
    public void CancellationAndOperationalFailureAreNotContention(FileLeaseMode mode)
    {
        var folder = Directory.CreateTempSubdirectory("bathos-leases-");
        try
        {
            var path = Path.Combine(folder.FullName, "writer.lock");
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            Assert.Throws<OperationCanceledException>(() => Current().TryAcquire(path, mode, canceled.Token));
            Assert.False(File.Exists(path));
            Assert.ThrowsAny<IOException>(() => Current().TryAcquire(Path.Combine(folder.FullName, "missing", "writer.lock"), mode, TestContext.Current.CancellationToken));
            Assert.ThrowsAny<IOException>(() => Current().TryAcquire(folder.FullName, mode, TestContext.Current.CancellationToken));
            Assert.Throws<ArgumentException>(() => Current().TryAcquire("relative.lock", mode, TestContext.Current.CancellationToken));
            using var lease = Current().TryAcquire(path, mode, TestContext.Current.CancellationToken);
            Assert.NotNull(lease);
            if (!OperatingSystem.IsWindows())
                Assert.Equal((UnixFileMode)0, File.GetUnixFileMode(path) & ~(UnixFileMode.UserRead | UnixFileMode.UserWrite));
        }
        finally { folder.Delete(true); }
    }

    [Theory]
    [InlineData(FileLeaseMode.Exclusive, false)]
    [InlineData(FileLeaseMode.Shared, false)]
    [InlineData(FileLeaseMode.Shared, true)]
    public void CancellationAfterNativeAcquisitionReleasesOwnershipWithoutChangingTheArtifact(FileLeaseMode mode, bool retainSharedOwner)
    {
        var folder = Directory.CreateTempSubdirectory("bathos-lease-cancel-");
        try
        {
            var path = Path.Combine(folder.FullName, "writer.lock");
            File.WriteAllText(path, "preserve stale diagnostics");
            using var retained = retainSharedOwner
                ? Assert.IsAssignableFrom<IFileLease>(Current().TryAcquire(path, FileLeaseMode.Shared, TestContext.Current.CancellationToken))
                : null;
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            var reached = false;
            void BeforeTransfer()
            {
                reached = true;
                using var contender = Current().TryAcquire(path, FileLeaseMode.Exclusive, TestContext.Current.CancellationToken);
                Assert.Null(contender);
                cancellation.Cancel();
            }
            IFileLease? Acquire() => PlatformHost.Current switch
            {
                PlatformOperatingSystem.Windows => WindowsFileLeaseProvider.TryAcquire(path, mode, BeforeTransfer, cancellation.Token),
                PlatformOperatingSystem.MacOS => MacOSFileLeaseProvider.TryAcquire(path, mode, BeforeTransfer, cancellation.Token),
                PlatformOperatingSystem.Linux => LinuxFileLeaseProvider.TryAcquire(path, mode, BeforeTransfer, cancellation.Token),
                _ => throw new PlatformNotSupportedException(),
            };
            var error = Assert.Throws<OperationCanceledException>(() => { using var unexpected = Acquire(); });
            Assert.True(reached);
            Assert.Equal(cancellation.Token, error.CancellationToken);
            if (retained is not null)
            {
                using var contender = Current().TryAcquire(path, FileLeaseMode.Exclusive, TestContext.Current.CancellationToken);
                Assert.Null(contender);
                retained.Dispose();
            }
            Assert.Equal("preserve stale diagnostics", File.ReadAllText(path));
            using var next = Current().TryAcquire(path, FileLeaseMode.Exclusive, TestContext.Current.CancellationToken);
            Assert.NotNull(next);
        }
        finally { folder.Delete(true); }
    }

    [Fact]
    public void DefaultModeExcludesBothSharedAndExclusiveOwners()
    {
        var folder = Directory.CreateTempSubdirectory("bathos-lease-default-");
        try
        {
            var path = Path.Combine(folder.FullName, "writer.lock");
            using var lease = Current().TryAcquire(path, default, TestContext.Current.CancellationToken);
            Assert.NotNull(lease);
            using var shared = Current().TryAcquire(path, FileLeaseMode.Shared, TestContext.Current.CancellationToken);
            Assert.Null(shared);
            using var exclusive = Current().TryAcquire(path, FileLeaseMode.Exclusive, TestContext.Current.CancellationToken);
            Assert.Null(exclusive);
        }
        finally { folder.Delete(true); }
    }

    [Fact]
    public async Task WindowsPendingReleaseObservationCanBeCanceledWithoutRetainingOwnership()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Native asynchronous lock completion requires Windows.");
        var folder = Directory.CreateTempSubdirectory("bathos-lease-wait-");
        try
        {
            var path = Path.Combine(folder.FullName, "writer.lock");
            using var lease = Current().TryAcquire(path, FileLeaseMode.Exclusive, TestContext.Current.CancellationToken);
            Assert.NotNull(lease);
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            var pending = WindowsLeaseRelease.WaitAsync(path, cancellation.Token);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
            Assert.Null(Current().TryAcquire(path, FileLeaseMode.Exclusive, TestContext.Current.CancellationToken));
            lease.Dispose();
            await WindowsLeaseRelease.WaitAsync(path, TestContext.Current.CancellationToken);
            using var next = Current().TryAcquire(path, FileLeaseMode.Exclusive, TestContext.Current.CancellationToken);
            Assert.NotNull(next);
        }
        finally { folder.Delete(true); }
    }

    [Fact]
    public void LinksCannotRedirectLeaseOwnership()
    {
        var folder = Directory.CreateTempSubdirectory("bathos-leases-");
        try
        {
            var target = Path.Combine(folder.FullName, "target");
            File.WriteAllText(target, "preserve");
            var link = Path.Combine(folder.FullName, "link");
            try { File.CreateSymbolicLink(link, target); }
            catch (UnauthorizedAccessException) when (OperatingSystem.IsWindows())
            { Assert.Skip("Symlink creation requires Windows developer mode or privileges."); }
            Assert.ThrowsAny<IOException>(() => Current().TryAcquire(link, FileLeaseMode.Exclusive, TestContext.Current.CancellationToken));
            Assert.Equal("preserve", File.ReadAllText(target));
        }
        finally { folder.Delete(true); }
    }

    [Fact]
    public async Task SharedOwnershipCoordinatesAcrossProcesses()
    {
        var folder = Directory.CreateTempSubdirectory("bathos-shared-leases-");
        try
        {
            var path = Path.Combine(folder.FullName, "shared.lock");
            var provider = Current();
            using (var parent = provider.TryAcquire(path, FileLeaseMode.Shared, TestContext.Current.CancellationToken))
            {
                Assert.NotNull(parent);
                await using (var exclusiveContender = await Child.StartAsync(folder.FullName, path))
                {
                    Assert.Equal("contended", await exclusiveContender.ReadAsync());
                    await exclusiveContender.Process.StandardInput.WriteLineAsync("finish");
                    await exclusiveContender.WaitAsync();
                }
                await using var child = await Child.StartAsync(folder.FullName, path, leaseMode: FileLeaseMode.Shared);
                Assert.Equal("acquired", await child.ReadAsync());
                Assert.Null(provider.TryAcquire(path, FileLeaseMode.Exclusive, TestContext.Current.CancellationToken));
                parent.Dispose();
                Assert.Null(provider.TryAcquire(path, FileLeaseMode.Exclusive, TestContext.Current.CancellationToken));
                await child.Process.StandardInput.WriteLineAsync("finish");
                await child.WaitAsync();
            }
            using var exclusive = provider.TryAcquire(path, FileLeaseMode.Exclusive, TestContext.Current.CancellationToken);
            Assert.NotNull(exclusive);
            await using var contender = await Child.StartAsync(folder.FullName, path, leaseMode: FileLeaseMode.Shared);
            Assert.Equal("contended", await contender.ReadAsync());
            await contender.Process.StandardInput.WriteLineAsync("finish");
            await contender.WaitAsync();
        }
        finally { folder.Delete(true); }
    }

    [Theory]
    [InlineData(0xd800)]
    [InlineData(0xdc00)]
    public void InvalidUnicodeCannotSelectAReplacementArtifact(int surrogate)
    {
        if (OperatingSystem.IsWindows()) Assert.Skip("UTF-8 path marshalling requires macOS or Linux.");
        var folder = Directory.CreateTempSubdirectory("bathos-leases-");
        try
        {
            var path = Path.Combine(folder.FullName, "writer-" + (char)surrogate + ".lock");
            var replacement = Path.Combine(folder.FullName, "writer-\ufffd.lock");
            Assert.Throws<ArgumentException>(() => { using var unexpected = Current().TryAcquire(path, FileLeaseMode.Exclusive, TestContext.Current.CancellationToken); });
            Assert.Empty(folder.EnumerateFileSystemInfos());
            File.WriteAllText(replacement, "preserve");
            Assert.Throws<ArgumentException>(() => { using var unexpected = Current().TryAcquire(path, FileLeaseMode.Exclusive, TestContext.Current.CancellationToken); });
            Assert.Equal("preserve", File.ReadAllText(replacement));
            using var valid = Current().TryAcquire(replacement, FileLeaseMode.Exclusive, TestContext.Current.CancellationToken);
            Assert.NotNull(valid);
        }
        finally { folder.Delete(true); }
    }

    [Fact]
    public async Task RejectingATerminalDoesNotAssignAControllingTerminal()
    {
        if (OperatingSystem.IsWindows()) Assert.Skip("Owned terminal fixtures require macOS or Linux.");
        var folder = Directory.CreateTempSubdirectory("bathos-lease-terminal-");
        try
        {
            await using var fixture = await Child.StartAsync(folder.FullName, "", mode: "lease-terminal-fixture");
            var path = await fixture.ReadAsync() ?? throw new InvalidDataException("Missing owned terminal path.");
            await using (var child = await Child.StartAsync(folder.FullName, path, mode: "reject-terminal-lease"))
            {
                Assert.Equal("rejected-without-terminal", await child.ReadAsync());
                await child.WaitAsync();
                Assert.Equal(0, child.Process.ExitCode);
            }
            await fixture.Process.StandardInput.WriteLineAsync("finish");
            await fixture.WaitAsync();
            Assert.Equal(0, fixture.Process.ExitCode);
        }
        finally { folder.Delete(true); }
    }

    [Theory]
    [InlineData(FileLeaseMode.Shared)]
    [InlineData(FileLeaseMode.Exclusive)]
    public void ForeignProvidersRejectInvocationAndPartialProvidersRemainCompatible(FileLeaseMode mode)
    {
        IPlatformProvider[] providers = [new MacOSPlatformProvider(), new WindowsPlatformProvider(), new LinuxPlatformProvider()];
        foreach (var provider in providers)
            if (provider.OperatingSystem != PlatformHost.Current)
                Assert.Throws<PlatformNotSupportedException>(() => provider.FileLeases?.TryAcquire("unused", mode, TestContext.Current.CancellationToken));
        Assert.Null(((IPlatformProvider)new Partial()).FileLeases);
    }

    [Theory]
    [InlineData(FileLeaseMode.Exclusive, false)]
    [InlineData(FileLeaseMode.Exclusive, true)]
    [InlineData(FileLeaseMode.Shared, false)]
    [InlineData(FileLeaseMode.Shared, true)]
    public async Task ChildOwnershipExcludesParentUntilDisposalOrProcessExit(FileLeaseMode mode, bool kill)
    {
        var folder = Directory.CreateTempSubdirectory("bathos-leases-");
        try
        {
            var path = Path.Combine(folder.FullName, "writer.lock");
            await using (var child = await Child.StartAsync(folder.FullName, path, leaseMode: mode))
            {
                Assert.Equal("acquired", await child.ReadAsync());
                Assert.Null(Current().TryAcquire(path, FileLeaseMode.Exclusive, TestContext.Current.CancellationToken));
                using (var shared = Current().TryAcquire(path, FileLeaseMode.Shared, TestContext.Current.CancellationToken))
                {
                    if (mode == FileLeaseMode.Shared) Assert.NotNull(shared);
                    else Assert.Null(shared);
                }
                if (kill) child.Process.Kill(entireProcessTree: true);
                else await child.Process.StandardInput.WriteLineAsync("finish");
                await child.WaitAsync();
            }
            Assert.True(File.Exists(path));
            if (kill && OperatingSystem.IsWindows()) await WindowsLeaseRelease.WaitAsync(path, TestContext.Current.CancellationToken);
            using var next = Current().TryAcquire(path, FileLeaseMode.Exclusive, TestContext.Current.CancellationToken);
            Assert.NotNull(next);
        }
        finally { folder.Delete(true); }
    }

    [Fact]
    public async Task ChildReportsContentionWithoutChangingParentsOwnership()
    {
        var folder = Directory.CreateTempSubdirectory("bathos-leases-");
        try
        {
            var path = Path.Combine(folder.FullName, "writer.lock");
            using var lease = Current().TryAcquire(path, FileLeaseMode.Exclusive, TestContext.Current.CancellationToken);
            Assert.NotNull(lease);
            await using var child = await Child.StartAsync(folder.FullName, path);
            Assert.Equal("contended", await child.ReadAsync());
            lease.Dispose();
            // Disposal releases ownership while the contending child remains alive.
            using var next = Current().TryAcquire(path, FileLeaseMode.Exclusive, TestContext.Current.CancellationToken);
            Assert.NotNull(next);
            await child.Process.StandardInput.WriteLineAsync("finish");
            await child.WaitAsync();
            Assert.Null(Current().TryAcquire(path, FileLeaseMode.Exclusive, TestContext.Current.CancellationToken));
        }
        finally { folder.Delete(true); }
    }

    [Theory]
    [InlineData(FileLeaseMode.Shared)]
    [InlineData(FileLeaseMode.Exclusive)]
    public async Task OwnerExitReleasesLeaseWhileItsDescendantRemainsAlive(FileLeaseMode mode)
    {
        var folder = Directory.CreateTempSubdirectory("bathos-lease-inheritance-");
        try
        {
            var pipeName = "bl_" + Guid.NewGuid().ToString("N");
            await using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(20));
            var connected = pipe.WaitForConnectionAsync(deadline.Token);
            try
            {
                var path = Path.Combine(folder.FullName, "writer.lock");
                await using var owner = await Child.StartAsync(folder.FullName, path, mode: "file-lease-owner", pipeName: pipeName, leaseMode: mode);
                Process? descendant = null;
                try
                {
                    var processId = int.Parse(await owner.ReadAsync() ?? throw new InvalidDataException("Missing descendant ID."), CultureInfo.InvariantCulture);
                    descendant = Process.GetProcessById(processId);
                    await connected;
                    using var reader = new StreamReader(pipe, leaveOpen: true);
                    using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
                    var message = await reader.ReadLineAsync(deadline.Token) ?? throw new InvalidDataException("Missing descendant readiness.");
                    using var ready = JsonDocument.Parse(message);
                    Assert.Equal(processId, ready.RootElement.GetProperty("ProcessId").GetInt32());
                    Assert.Null(Current().TryAcquire(path, FileLeaseMode.Exclusive, TestContext.Current.CancellationToken));
                    using (var shared = Current().TryAcquire(path, FileLeaseMode.Shared, TestContext.Current.CancellationToken))
                    {
                        if (mode == FileLeaseMode.Shared) Assert.NotNull(shared);
                        else Assert.Null(shared);
                    }
                    // Abrupt exit avoids explicit unlock, which would also release an inherited Unix description's lock.
                    owner.Process.Kill(entireProcessTree: false);
                    await owner.WaitAsync();
                    Assert.False(descendant.HasExited);
                    if (OperatingSystem.IsWindows()) await WindowsLeaseRelease.WaitAsync(path, deadline.Token);
                    using var next = Current().TryAcquire(path, FileLeaseMode.Exclusive, TestContext.Current.CancellationToken);
                    Assert.NotNull(next);
                    await writer.WriteLineAsync("finish".AsMemory(), deadline.Token);
                    Assert.Equal("finished", await reader.ReadLineAsync(deadline.Token));
                    await descendant.WaitForExitAsync(deadline.Token);
                }
                finally
                {
                    if (descendant is not null)
                    {
                        if (!descendant.HasExited) descendant.Kill(entireProcessTree: true);
                        await descendant.WaitForExitAsync(CancellationToken.None);
                        descendant.Dispose();
                    }
                }
            }
            finally
            {
                await deadline.CancelAsync();
                try { await connected; }
                catch (OperationCanceledException) { }
            }
        }
        finally { folder.Delete(true); }
    }

    [Fact]
    public async Task StableLockOutsideTheDataTreeRemainsExclusiveWhenDataMoves()
    {
        var folder = Directory.CreateTempSubdirectory("bathos-leases-");
        try
        {
            var source = Directory.CreateDirectory(Path.Combine(folder.FullName, "source")).FullName;
            var destination = Path.Combine(folder.FullName, "destination");
            var path = Path.Combine(folder.FullName, "writer.lock");
            File.WriteAllText(Path.Combine(source, "document.txt"), "complete document");
            using (var lease = Current().TryAcquire(path, FileLeaseMode.Exclusive, TestContext.Current.CancellationToken))
            {
                Assert.NotNull(lease);
                Directory.Move(source, destination);
                Assert.False(Directory.Exists(source));
                Assert.Equal("complete document", File.ReadAllText(Path.Combine(destination, "document.txt")));
                Assert.True(File.Exists(path));
                Assert.Null(Current().TryAcquire(path, FileLeaseMode.Exclusive, TestContext.Current.CancellationToken));
                await using var child = await Child.StartAsync(folder.FullName, path);
                Assert.Equal("contended", await child.ReadAsync());
                await child.Process.StandardInput.WriteLineAsync("finish");
                await child.WaitAsync();
            }
            using var next = Current().TryAcquire(path, FileLeaseMode.Exclusive, TestContext.Current.CancellationToken);
            Assert.NotNull(next);
        }
        finally { folder.Delete(true); }
    }

    [Fact]
    public void SignificantTrailingCharactersDoNotRetargetAnotherLock()
    {
        var folder = Directory.CreateTempSubdirectory("bathos-leases-");
        try
        {
            var path = Path.Combine(folder.FullName, "writer-日本 \U0001F3A8.lock");
            using var first = Current().TryAcquire(path, FileLeaseMode.Exclusive, TestContext.Current.CancellationToken);
            using var second = Current().TryAcquire(path + " ", FileLeaseMode.Exclusive, TestContext.Current.CancellationToken);
            Assert.NotNull(first);
            Assert.NotNull(second);
            Assert.Null(Current().TryAcquire(path + " ", FileLeaseMode.Exclusive, TestContext.Current.CancellationToken));
        }
        finally { folder.Delete(true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SpecialFilesFailWithoutBlockingAcquisition(bool fifo)
    {
        if (OperatingSystem.IsWindows()) Assert.Skip("POSIX special-file fixtures require macOS or Linux.");
        var folder = Directory.CreateDirectory(Path.Combine("/tmp", "bathos-lease-" + Guid.NewGuid().ToString("N")));
        Socket? socket = null;
        try
        {
            var path = Path.Combine(folder.FullName, "special");
            if (fifo)
            {
                var start = new ProcessStartInfo("/usr/bin/mkfifo") { UseShellExecute = false };
                start.ArgumentList.Add(path);
                using var create = Process.Start(start) ?? throw new InvalidOperationException("Could not create FIFO fixture.");
                await create.WaitForExitAsync(TestContext.Current.CancellationToken);
                Assert.Equal(0, create.ExitCode);
            }
            else
            {
                socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                socket.Bind(new UnixDomainSocketEndPoint(path));
            }
            await using var child = await Child.StartAsync(folder.FullName, path, reject: true);
            Assert.Equal("rejected", await child.ReadAsync());
            await child.Process.StandardInput.WriteLineAsync("finish");
            await child.WaitAsync();
            Assert.True(File.Exists(path));
        }
        finally { socket?.Dispose(); folder.Delete(true); }
    }

    private sealed class Partial : IPlatformProvider { public PlatformOperatingSystem OperatingSystem => PlatformOperatingSystem.Unknown; }

    private sealed class Child(Process process) : IAsyncDisposable
    {
        internal Process Process { get; } = process;
        private readonly Task<string> _error = process.StandardError.ReadToEndAsync();
        private readonly CancellationTokenSource _deadline = CreateDeadline();
        private static CancellationTokenSource CreateDeadline()
        {
            var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            source.CancelAfter(TimeSpan.FromSeconds(20));
            return source;
        }
        internal async Task<string?> ReadAsync() => await Process.StandardOutput.ReadLineAsync(_deadline.Token);
        internal async Task WaitAsync() => await Process.WaitForExitAsync(_deadline.Token);
        internal static async Task<Child> StartAsync(string folder, string path, bool reject = false, string? mode = null, string pipeName = "", FileLeaseMode leaseMode = FileLeaseMode.Exclusive)
        {
            var configuration = Path.Combine(folder, "probe.json");
            await File.WriteAllTextAsync(configuration, JsonSerializer.Serialize(new { Mode = mode ?? (reject ? "reject-file-lease" : "file-lease"), PipeName = pipeName, Document = path, LeaseMode = leaseMode }), TestContext.Current.CancellationToken);
            var executable = Path.Combine(AppContext.BaseDirectory, "TestApplication", "Bathos.Platform.TestApplication" + (OperatingSystem.IsWindows() ? ".exe" : ""));
            var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add(configuration);
            return new(Process.Start(start) ?? throw new InvalidOperationException("Lease probe did not start."));
        }
        public async ValueTask DisposeAsync()
        {
            try
            {
                if (!Process.HasExited) Process.Kill(entireProcessTree: true);
                await Process.WaitForExitAsync(CancellationToken.None);
                var error = await _error;
                Assert.True(string.IsNullOrEmpty(error), error);
            }
            finally { _deadline.Dispose(); Process.Dispose(); }
        }
    }
}
