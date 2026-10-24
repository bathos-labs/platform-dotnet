using System.Diagnostics;
using Bathos.Platform.Linux;
using Xunit;

namespace Bathos.Platform.Tests;

[Trait("Validation", "Native")]
public sealed class LinuxFileRevealTests
{
    [Theory]
    [InlineData("success")]
    [InlineData("error")]
    [InlineData("malformed")]
    [InlineData("held-success")]
    [InlineData("held-error")]
    public async Task RevealUsesEncodedFileUrisAndPreservesNativeErrors(string scenario)
    {
        if (!OperatingSystem.IsLinux()) Assert.Skip("Native D-Bus reveal tests require Linux; no desktop is used.");
        const string path = "/tmp/drawing #100% & café.png";
        const string uri = "file:///tmp/drawing%20%23100%25%20&%20caf%C3%A9.png";
        var start = new ProcessStartInfo("/usr/bin/dbus-run-session") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true };
        foreach (var argument in new[] { "--", "/usr/bin/python3", Path.Combine(AppContext.BaseDirectory, "FileManagerTestService.py"), scenario, uri })
            start.ArgumentList.Add(argument);
        using var service = Process.Start(start) ?? throw new InvalidOperationException("Could not start the private file-manager service.");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var errors = service.StandardError.ReadToEndAsync(deadline.Token);
        using var cancellation = new CancellationTokenSource();
        Task? operation = null;
        try
        {
            var address = await service.StandardOutput.ReadLineAsync(deadline.Token);
            Assert.NotNull(address);
            operation = Task.Run(() =>
            {
                if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
                DesktopBus.Reveal(path, cancellation.Token, address);
            }, TestContext.Current.CancellationToken);
            if (scenario.StartsWith("held-", StringComparison.Ordinal))
            {
                Assert.Equal("submitted", await service.StandardOutput.ReadLineAsync(deadline.Token));
                await cancellation.CancelAsync();
                Assert.False(operation.IsCompleted); // Native reply and connection cleanup are still owned.
                await service.StandardInput.WriteLineAsync("finish".AsMemory(), deadline.Token);
            }
            if (scenario is "error" or "held-error") await Assert.ThrowsAsync<InvalidOperationException>(() => operation.WaitAsync(deadline.Token));
            else if (scenario == "malformed") await Assert.ThrowsAsync<IOException>(() => operation.WaitAsync(deadline.Token));
            else await operation.WaitAsync(deadline.Token);
            await service.WaitForExitAsync(deadline.Token);
            Assert.True(service.ExitCode == 0, await errors);
            // The fixture exits only after seeing our unique bus name disappear, including native errors.

        }
        finally
        {
            if (!service.HasExited) service.Kill(entireProcessTree: true);
            await service.WaitForExitAsync(CancellationToken.None);
            if (operation is not null)
            {
                try { await operation; }
                catch (Exception error) when (error is IOException or InvalidOperationException) { }
            }
        }
    }
}
