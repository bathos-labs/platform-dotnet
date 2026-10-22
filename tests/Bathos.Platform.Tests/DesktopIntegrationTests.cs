using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using Bathos.Platform.Desktop;
using Bathos.Platform.Linux;
using Bathos.Platform.MacOS;
using Bathos.Platform.Windows;
using Microsoft.Win32;
using Xunit;

namespace Bathos.Platform.Tests;

public sealed class DesktopIntegrationTests
{
    private static IPlatformProvider Current() => PlatformProviders.SelectCurrent(
        new MacOSPlatformProvider(), new WindowsPlatformProvider(), new LinuxPlatformProvider());

    private static string TestApplication => Path.Combine(AppContext.BaseDirectory, "TestApplication",
        "Bathos.Platform.TestApplication" + (OperatingSystem.IsWindows() ? ".exe" : ""));

    [Fact]
    public void TargetFactoriesRejectCommandsRelativePathsAndMalformedIdentifiers()
    {
        Assert.Throws<ArgumentException>(() => ApplicationTarget.Executable("editor --open"));
        Assert.Throws<ArgumentException>(() => ApplicationTarget.Executable(Path.GetFullPath("editor") + "\0suffix"));
        Assert.Throws<ArgumentException>(() => ApplicationTarget.MacOSBundle("Editor.app"));
        Assert.Throws<ArgumentException>(() => ApplicationTarget.LinuxDesktopEntry("/usr/share/applications/editor.desktop"));
        Assert.Throws<ArgumentException>(() => ApplicationTarget.LinuxDesktopEntry("editor"));
        Assert.Equal(ApplicationTargetKind.MacOSBundle, ApplicationTarget.MacOSBundle("/Applications/Editor.app").Kind);
    }

    [Fact]
    public async Task InvalidInputsAndCancellationDoNotLaunchApplicationsOrFileManagers()
    {
        var provider = Current();
        var reveal = Assert.IsAssignableFrom<IFileRevealer>(provider.FileRevealer);
        var invoke = Assert.IsAssignableFrom<IFileInvoker>(provider.FileInvoker);
        var folder = Directory.CreateTempSubdirectory("bathos-desktop-");
        try
        {
            var document = Path.Combine(folder.FullName, "document.txt");
            File.WriteAllText(document, "preserve");
            var target = ApplicationTarget.Executable(TestApplication);
            var unsupported = OperatingSystem.IsMacOS() ? ApplicationTarget.LinuxDesktopEntry("org.bathos.Unsupported.desktop")
                : ApplicationTarget.MacOSBundle("/Applications/Unsupported.app");
            await Assert.ThrowsAsync<NotSupportedException>(() => invoke.OpenAsync(document, unsupported, TestContext.Current.CancellationToken));
            await Assert.ThrowsAnyAsync<ArgumentException>(() => reveal.RevealAsync("relative.txt", TestContext.Current.CancellationToken));
            await Assert.ThrowsAnyAsync<IOException>(() => reveal.RevealAsync(Path.Combine(folder.FullName, "missing"), TestContext.Current.CancellationToken));
            await Assert.ThrowsAnyAsync<IOException>(() => invoke.OpenAsync(folder.FullName, target, TestContext.Current.CancellationToken));
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reveal.RevealAsync(document, canceled.Token));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => invoke.OpenAsync(document, target, canceled.Token));
            await Assert.ThrowsAnyAsync<IOException>(() => invoke.OpenAsync(document, ApplicationTarget.Executable(Path.Combine(folder.FullName, "missing-editor")), TestContext.Current.CancellationToken));
            Assert.Equal("preserve", File.ReadAllText(document));
        }
        finally { folder.Delete(true); }
    }

    [Fact]
    public async Task ExecutableHandoffPreservesArgumentsAndDoesNotWaitForOrKillTheApplication()
    {
        await RunProbeAsync(desktopEntry: false);
    }

    [Fact]
    public async Task LinuxDesktopEntryDiscoveryAndGioActivationPreserveTheFileArgument()
    {
        if (!OperatingSystem.IsLinux()) Assert.Skip("Native GIO activation requires Linux; no desktop is used.");
        await RunProbeAsync(desktopEntry: true);
    }

    private static async Task RunProbeAsync(bool desktopEntry)
    {
        var folder = Directory.CreateTempSubdirectory("bathos-handoff-");
        var pipeName = "bathos_" + Guid.NewGuid().ToString("N");
        await using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        Process? owner = null, child = null;
        StreamWriter? writer = null;
        Task? connected = null;
        try
        {
            var document = Path.Combine(folder.FullName, "-drawing # 100% & café.json");
            File.WriteAllText(document, JsonSerializer.Serialize(new { Mode = "echo", PipeName = pipeName }));
            connected = pipe.WaitForConnectionAsync(deadline.Token);
            var invoke = Assert.IsAssignableFrom<IFileInvoker>(Current().FileInvoker);
            if (desktopEntry)
            {
                var applications = Directory.CreateDirectory(Path.Combine(folder.FullName, "applications"));
                const string id = "org.bathos.HandoffProbe.desktop";
                var executable = TestApplication.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal).Replace("%", "%%", StringComparison.Ordinal);
                File.WriteAllText(Path.Combine(applications.FullName, id), $"[Desktop Entry]\nType=Application\nName=Bathos owned test\nExec=\"{executable}\" %f\nTerminal=false\n");
                var configuration = Path.Combine(folder.FullName, "launch.json");
                File.WriteAllText(configuration, JsonSerializer.Serialize(new { Mode = "desktop", PipeName = pipeName, Document = document, DesktopId = id }));
                var start = new ProcessStartInfo(TestApplication) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
                start.ArgumentList.Add(configuration);
                start.Environment["XDG_DATA_HOME"] = folder.FullName; // Isolated child: never change the test process's cached XDG directories.
                owner = Process.Start(start) ?? throw new InvalidOperationException("Could not start the GIO probe.");
                await owner.WaitForExitAsync(deadline.Token);
                if (owner.ExitCode != 0) Assert.Fail(await owner.StandardError.ReadToEndAsync(deadline.Token));
            }
            else await invoke.OpenAsync(document, ApplicationTarget.Executable(TestApplication)).WaitAsync(deadline.Token);
            // The application is still waiting for this connection/release, so successful handoff did not wait for exit.
            await connected;
            using var reader = new StreamReader(pipe, leaveOpen: true);
            writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            var text = await reader.ReadLineAsync(deadline.Token) ?? throw new InvalidDataException("The probe did not report its arguments.");
            var response = JsonSerializer.Deserialize<ProbeResult>(text) ?? throw new InvalidDataException("Invalid probe response.");
            child = Process.GetProcessById(response.ProcessId);
            Assert.Equal([document], response.Arguments);
            Assert.False(child.HasExited);
            Assert.True(File.Exists(document));
            await writer.WriteLineAsync("finish".AsMemory(), deadline.Token);
            Assert.Equal("finished", await reader.ReadLineAsync(deadline.Token));
            await child.WaitForExitAsync(deadline.Token);
        }
        finally
        {
            await deadline.CancelAsync();
            if (connected is not null)
            {
                try { await connected; }
                catch (OperationCanceledException) { }
            }
            if (child is not null)
            {
                if (!child.HasExited) child.Kill();
                await child.WaitForExitAsync(CancellationToken.None);
                child.Dispose();
            }
            if (owner is not null)
            {
                if (!owner.HasExited) owner.Kill(entireProcessTree: true);
                await owner.WaitForExitAsync(CancellationToken.None);
                owner.Dispose();
            }
            if (writer is not null) await writer.DisposeAsync();
            folder.Delete(true);
        }
    }

    [Fact]
    public void NativeDiscoveryReturnsRegisteredTargetsAndRepresentsAbsenceExplicitly()
    {
        var missing = "bathos-missing-" + Guid.NewGuid().ToString("N");
        if (OperatingSystem.IsLinux()) Assert.Empty(LinuxApplicationDiscovery.FindByDesktopId(missing + ".desktop"));
        else if (OperatingSystem.IsMacOS())
        {
            Assert.Empty(MacOSApplicationDiscovery.FindByBundleIdentifier("org.bathos." + missing));
            Assert.NotEmpty(MacOSApplicationDiscovery.FindByBundleIdentifier("com.apple.finder"));
        }
        else if (OperatingSystem.IsWindows())
        {
            var name = missing + ".exe";
            var key = @"Software\Microsoft\Windows\CurrentVersion\App Paths\" + name;
            using (var registration = Registry.CurrentUser.CreateSubKey(key)) registration.SetValue(null, "\"" + TestApplication + "\"");
            try
            {
                Assert.Equal([ApplicationTarget.Executable(TestApplication)], WindowsApplicationDiscovery.FindByExecutableName(name));
            }
            finally { Registry.CurrentUser.DeleteSubKey(key); }
            Assert.Empty(WindowsApplicationDiscovery.FindByExecutableName(name));
        }
    }

    private sealed record ProbeResult(int ProcessId, string[] Arguments);
}
