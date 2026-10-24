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
public sealed class NativeFileFailureTests
{
    private static IPlatformProvider Current() => PlatformProviders.SelectCurrent(
        new MacOSPlatformProvider(), new WindowsPlatformProvider(), new LinuxPlatformProvider());

    [Fact]
    public void InspectionReleasesFileHandlesBeforeReturning()
    {
        var probe = Assert.IsAssignableFrom<IFileEntryProbe>(Current().FileEntries);
        var folder = Directory.CreateTempSubdirectory("bathos-inspection-");
        try
        {
            var path = Path.Combine(folder.FullName, "file.txt");
            File.WriteAllText(path, "keep");
            for (var index = 0; index < 32; index++) probe.RequireRegularFile(path);
            using (var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Assert.Equal(4, exclusive.Length); // Detect a leaked Windows handle, including its sharing mode.
            File.Move(path, path + ".moved");
            Assert.Equal("keep", File.ReadAllText(path + ".moved"));
        }
        finally { folder.Delete(true); }
    }

    [Theory]
    [InlineData("file")]
    [InlineData("directory")]
    [InlineData("dangling")]
    public async Task InspectionRejectsLinksWithoutFollowingOrChangingTheirTargets(string kind)
    {
        var folder = Directory.CreateTempSubdirectory("bathos-links-");
        try
        {
            var target = Path.Combine(folder.FullName, "target");
            var link = Path.Combine(folder.FullName, "link");
            if (kind == "file") File.WriteAllText(target, "target owner");
            if (kind == "directory") Directory.CreateDirectory(target);
            try
            {
                if (kind == "directory") Directory.CreateSymbolicLink(link, target);
                else File.CreateSymbolicLink(link, target);
            }
            catch (UnauthorizedAccessException) when (OperatingSystem.IsWindows())
            {
                Assert.Skip("Windows symlink fixture requires developer mode or symlink privilege; validate links manually on this host.");
            }
            await AssertRejectedInOwnedProcessAsync(folder.FullName, link);
            Assert.Equal(target, kind == "directory" ? new DirectoryInfo(link).LinkTarget : new FileInfo(link).LinkTarget);
            if (kind == "file") Assert.Equal("target owner", File.ReadAllText(target));
            if (kind == "dangling") Assert.False(File.Exists(target));
        }
        finally { folder.Delete(true); }
    }

    [Theory]
    [InlineData("fifo")]
    [InlineData("socket")]
    public async Task PosixSpecialEntriesAreRejectedWithoutOpeningOrBlocking(string kind)
    {
        if (OperatingSystem.IsWindows()) Assert.Skip("POSIX FIFO and Unix socket filesystem entries require macOS or Linux.");
        // Keep socket paths short enough for Darwin's sockaddr_un; the system temp path can be long.
        var folder = Directory.CreateDirectory(Path.Combine("/tmp", "bathos-" + Guid.NewGuid().ToString("N")));
        Socket? socket = null;
        try
        {
            var path = Path.Combine(folder.FullName, "special");
            if (kind == "fifo")
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
            await AssertRejectedInOwnedProcessAsync(folder.FullName, path);
            Assert.True(File.Exists(path));
        }
        finally { socket?.Dispose(); folder.Delete(true); }
    }

    [Fact]
    public void NativeRenameFailuresPreserveBothOwnersAndAllowANewOperation()
    {
        var rename = Current().FileRename;
        if (rename is null) Assert.Skip("Linux exposes no native rename capability.");
        var folder = Directory.CreateTempSubdirectory("bathos-rename-failure-");
        try
        {
            var source = Path.Combine(folder.FullName, "source.txt");
            var output = Path.Combine(folder.FullName, "output.txt");
            File.WriteAllText(source, "source owner");
            var occupied = Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(occupied.FullName, "child.txt"), "destination owner");
            Assert.NotEqual(FileRenameStatus.Success, rename.Move(source, output, out var error, replaceExisting: true));
            Assert.NotEqual(0, error);
            Assert.Equal("source owner", File.ReadAllText(source));
            Assert.Equal("destination owner", File.ReadAllText(Path.Combine(output, "child.txt")));
            var missingParent = Path.Combine(folder.FullName, "missing", "output.txt");
            Assert.Equal(FileRenameStatus.Unavailable, rename.Move(source, missingParent, out error));
            Assert.NotEqual(0, error);
            Assert.Equal("source owner", File.ReadAllText(source));
            var recovered = Path.Combine(folder.FullName, "recovered.txt");
            Assert.Equal(FileRenameStatus.Success, rename.Move(source, recovered, out error));
            Assert.Equal(0, error);
            Assert.False(File.Exists(source));
            Assert.Equal("source owner", File.ReadAllText(recovered));
        }
        finally { folder.Delete(true); }
    }

    private static async Task AssertRejectedInOwnedProcessAsync(string folder, string entry)
    {
        var configuration = Path.Combine(folder, "inspect.json");
        File.WriteAllText(configuration, JsonSerializer.Serialize(new { Mode = "reject-special", PipeName = "", Document = entry }));
        var executable = Path.Combine(AppContext.BaseDirectory, "TestApplication", "Bathos.Platform.TestApplication" + (OperatingSystem.IsWindows() ? ".exe" : ""));
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardError = true };
        start.ArgumentList.Add(configuration);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start native inspection probe.");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var errors = process.StandardError.ReadToEndAsync(CancellationToken.None);
        try
        {
            // A regression that opens a FIFO cannot strand the test runner or an owned worker.
            await process.WaitForExitAsync(deadline.Token);
            Assert.True(process.ExitCode == 0, await errors);
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            await errors;
        }
    }
}
