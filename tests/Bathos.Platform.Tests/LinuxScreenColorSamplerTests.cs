using System.Diagnostics;
using Bathos.Platform.Linux;
using Xunit;

namespace Bathos.Platform.Tests;

public sealed class LinuxScreenColorSamplerTests
{
    [Fact]
    [Trait("Validation", "Portable")]
    public async Task RequestsRejectOverlapAcrossInstancesAndReleaseOwnershipAfterFailure()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var first = new LinuxScreenColorSampler(() =>
        {
            entered.SetResult();
            release.Wait();
            throw new IOException("Portal failed.");
        });
        var second = new LinuxScreenColorSampler(() => new ScreenColor(1, 2, 3));
        var operation = first.SampleAsync();
        try
        {
            await entered.Task;
            Assert.Throws<InvalidOperationException>(() => { _ = first.SampleAsync(); });
            Assert.Throws<InvalidOperationException>(() => { _ = second.SampleAsync(); });
        }
        finally
        {
            release.Set();
            await Assert.ThrowsAsync<IOException>(() => operation);
        }
        Assert.Equal(new ScreenColor(1, 2, 3), await second.SampleAsync());
        Assert.Null(await new LinuxScreenColorSampler(() => null).SampleAsync());
        Assert.Equal(new ScreenColor(1, 2, 3), await second.SampleAsync());
    }

    [Theory]
    [InlineData("success")]
    [InlineData("cancel")]
    [InlineData("failure")]
    [InlineData("missing")]
    [InlineData("nan")]
    [InlineData("range")]
    [InlineData("type")]
    [InlineData("signature")]
    [InlineData("disconnect")]
    [InlineData("unrelated")]
    [InlineData("method-error")]
    [InlineData("reply-signature")]
    [InlineData("reply-path")]
    [InlineData("late")]
    [InlineData("owner-loss")]
    [InlineData("infinity")]
    [InlineData("upper-range")]
    [InlineData("short")]
    [InlineData("long")]
    [InlineData("component-type")]
    [Trait("Validation", "Native")]
    public async Task NativePortalProtocolAgainstPrivateHeadlessService(string scenario)
    {
        if (!OperatingSystem.IsLinux()) Assert.Skip("Native libdbus protocol tests require Linux; no desktop is used.");
        var start = new ProcessStartInfo("/usr/bin/dbus-run-session")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add("--");
        start.ArgumentList.Add("/usr/bin/python3");
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "PortalTestService.py"));
        start.ArgumentList.Add(scenario);
        using var service = Process.Start(start) ?? throw new InvalidOperationException("Could not start the isolated test portal.");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var errors = service.StandardError.ReadToEndAsync(deadline.Token);
        Task<ScreenColor?>? operation = null;
        try
        {
            var address = await service.StandardOutput.ReadLineAsync(deadline.Token);
            Assert.NotNull(address);
            Assert.StartsWith("unix:", address, StringComparison.Ordinal);
            var sampler = new LinuxScreenColorSampler(() => OperatingSystem.IsLinux() ? DesktopBus.PickColor("", address)
                : throw new PlatformNotSupportedException());
            operation = sampler.SampleAsync();
            if (scenario is "success" or "unrelated" or "late") Assert.Equal(new ScreenColor(0, 128, 255), await operation.WaitAsync(deadline.Token));
            else if (scenario == "cancel") Assert.Null(await operation.WaitAsync(deadline.Token));
            else if (scenario is "disconnect" or "owner-loss") await Assert.ThrowsAsync<IOException>(() => operation.WaitAsync(deadline.Token));
            else await Assert.ThrowsAsync<InvalidOperationException>(() => operation.WaitAsync(deadline.Token));
            await service.WaitForExitAsync(deadline.Token);
            Assert.True(service.ExitCode == 0, await errors);
            // Service exit waits for our unique bus name to vanish: the connection was closed, even after failure.
            // The real transport must also release the sampler's process-wide slot before completion.
            Assert.Equal(new ScreenColor(1, 2, 3), await new LinuxScreenColorSampler(() => new ScreenColor(1, 2, 3)).SampleAsync());
        }
        finally
        {
            if (!service.HasExited) service.Kill(entireProcessTree: true);
            await service.WaitForExitAsync(CancellationToken.None);
            if (operation is not null)
            {
                // Killing the private bus also retires the native wait on a failed test. Never leave owned work behind.
                try { await operation; }
                catch (Exception error) when (error is IOException or InvalidOperationException) { }
            }
        }
    }
}
