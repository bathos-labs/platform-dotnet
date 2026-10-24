using System.Diagnostics;
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
    public async Task IndependentProvidersExcludeOneAnotherAndReleaseWithoutDeletingOrChangingBytes()
    {
        var folder = Directory.CreateTempSubdirectory("bathos-leases-");
        try
        {
            var path = Path.Combine(folder.FullName, "writer.lock");
            await File.WriteAllTextAsync(path, "stale metadata is not ownership", TestContext.Current.CancellationToken);
            var lease = Assert.IsAssignableFrom<IFileLease>(Current().TryAcquire(path, TestContext.Current.CancellationToken));
            try { Assert.Null(Current().TryAcquire(path, TestContext.Current.CancellationToken)); }
            finally { await lease.DisposeAsync(); }
            lease.Dispose();
            Assert.Equal("stale metadata is not ownership", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
            using var next = Current().TryAcquire(path, TestContext.Current.CancellationToken);
            Assert.NotNull(next);
        }
        finally { folder.Delete(true); }
    }

    [Fact]
    public void CancellationAndOperationalFailureAreNotContention()
    {
        var folder = Directory.CreateTempSubdirectory("bathos-leases-");
        try
        {
            var path = Path.Combine(folder.FullName, "writer.lock");
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            Assert.Throws<OperationCanceledException>(() => Current().TryAcquire(path, canceled.Token));
            Assert.False(File.Exists(path));
            Assert.ThrowsAny<IOException>(() => Current().TryAcquire(Path.Combine(folder.FullName, "missing", "writer.lock"), TestContext.Current.CancellationToken));
            Assert.ThrowsAny<IOException>(() => Current().TryAcquire(folder.FullName, TestContext.Current.CancellationToken));
            Assert.Throws<ArgumentException>(() => Current().TryAcquire("relative.lock", TestContext.Current.CancellationToken));
            using var lease = Current().TryAcquire(path, TestContext.Current.CancellationToken);
            Assert.NotNull(lease);
            if (!OperatingSystem.IsWindows())
                Assert.Equal((UnixFileMode)0, File.GetUnixFileMode(path) & ~(UnixFileMode.UserRead | UnixFileMode.UserWrite));
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
            Assert.ThrowsAny<IOException>(() => Current().TryAcquire(link, TestContext.Current.CancellationToken));
            Assert.Equal("preserve", File.ReadAllText(target));
        }
        finally { folder.Delete(true); }
    }

    [Fact]
    public void ForeignProvidersRejectInvocationAndPartialProvidersRemainCompatible()
    {
        IPlatformProvider[] providers = [new MacOSPlatformProvider(), new WindowsPlatformProvider(), new LinuxPlatformProvider()];
        foreach (var provider in providers)
            if (provider.OperatingSystem != PlatformHost.Current)
                Assert.Throws<PlatformNotSupportedException>(() => provider.FileLeases?.TryAcquire("unused", TestContext.Current.CancellationToken));
        Assert.Null(((IPlatformProvider)new Partial()).FileLeases);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChildOwnershipExcludesParentUntilDisposalOrProcessExit(bool kill)
    {
        var folder = Directory.CreateTempSubdirectory("bathos-leases-");
        try
        {
            var path = Path.Combine(folder.FullName, "writer.lock");
            await using (var child = await Child.StartAsync(folder.FullName, path))
            {
                Assert.Equal("acquired", await child.ReadAsync());
                Assert.Null(Current().TryAcquire(path, TestContext.Current.CancellationToken));
                if (kill) child.Process.Kill(entireProcessTree: true);
                else await child.Process.StandardInput.WriteLineAsync("finish");
                await child.WaitAsync();
            }
            Assert.True(File.Exists(path));
            using var next = Current().TryAcquire(path, TestContext.Current.CancellationToken);
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
            using var lease = Current().TryAcquire(path, TestContext.Current.CancellationToken);
            Assert.NotNull(lease);
            await using var child = await Child.StartAsync(folder.FullName, path);
            Assert.Equal("contended", await child.ReadAsync());
            lease.Dispose();
            // The still-running child must not inherit the parent's native ownership handle.
            using var next = Current().TryAcquire(path, TestContext.Current.CancellationToken);
            Assert.NotNull(next);
            await child.Process.StandardInput.WriteLineAsync("finish");
            await child.WaitAsync();
            Assert.Null(Current().TryAcquire(path, TestContext.Current.CancellationToken));
        }
        finally { folder.Delete(true); }
    }

    [Fact]
    public async Task AtomicDirectoryRenameRetainsExclusionAtTheDestination()
    {
        var folder = Directory.CreateTempSubdirectory("bathos-leases-");
        try
        {
            var source = Directory.CreateDirectory(Path.Combine(folder.FullName, "source")).FullName;
            var destination = Path.Combine(folder.FullName, "destination");
            var path = Path.Combine(source, "writer.lock");
            var moved = Path.Combine(destination, "writer.lock");
            using (var lease = Current().TryAcquire(path, TestContext.Current.CancellationToken))
            {
                Assert.NotNull(lease);
                Directory.Move(source, destination);
                Assert.Null(Current().TryAcquire(moved, TestContext.Current.CancellationToken));
                await using var child = await Child.StartAsync(folder.FullName, moved);
                Assert.Equal("contended", await child.ReadAsync());
                await child.Process.StandardInput.WriteLineAsync("finish");
                await child.WaitAsync();
            }
            using var next = Current().TryAcquire(moved, TestContext.Current.CancellationToken);
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
            var path = Path.Combine(folder.FullName, "writer.lock");
            using var first = Current().TryAcquire(path, TestContext.Current.CancellationToken);
            using var second = Current().TryAcquire(path + " ", TestContext.Current.CancellationToken);
            Assert.NotNull(first);
            Assert.NotNull(second);
            Assert.Null(Current().TryAcquire(path + " ", TestContext.Current.CancellationToken));
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
        internal static async Task<Child> StartAsync(string folder, string path, bool reject = false)
        {
            var configuration = Path.Combine(folder, "probe.json");
            await File.WriteAllTextAsync(configuration, JsonSerializer.Serialize(new { Mode = reject ? "reject-file-lease" : "file-lease", PipeName = "", Document = path }), TestContext.Current.CancellationToken);
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
