using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using Bathos.Platform.Windows;
using Microsoft.Win32;
using Xunit;

namespace Bathos.Platform.Tests;

[Trait("Validation", "Native")]
[Collection("Windows Shell registrations")]
public sealed class WindowsAssociationActivationTests
{
    [Fact]
    public async Task DiscoveryIconAndInvocationUseCurrentRegistrationAndPreserveArguments()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Skip("Windows Shell required."); return; }
        var folder = Directory.CreateTempSubdirectory("bathos-activation-");
        var id = "Bathos" + Guid.NewGuid().ToString("N");
        var keyPath = @"Software\Classes\Applications\" + id + ".exe";
        try
        {
            foreach (var source in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "TestApplication")))
                File.Copy(source, Path.Combine(folder.FullName, Path.GetFileName(source)));
            var executable = Path.Combine(folder.FullName, "Bathos.Platform.TestApplication.exe");
            var document = Path.Combine(folder.FullName, "-space # % & \u03a9." + id);
            File.WriteAllText(document, "owned document");
            using (var types = Registry.CurrentUser.CreateSubKey(keyPath + @"\SupportedTypes")) types.SetValue("." + id, "");
            using (var command = Registry.CurrentUser.CreateSubKey(keyPath + @"\shell\open\command"))
                command.SetValue(null, $"\"{executable}\" --association-held-marker unused \"%1\"");
            var provider = new WindowsPlatformProvider();
            var candidate = Assert.Single(await provider.ApplicationDiscovery.FindForFileAsync(document, TestContext.Current.CancellationToken), c => c.Target.Value == executable);
            Assert.NotNull(await provider.ApplicationIcons.GetIconAsync(candidate.Target, 32, TestContext.Current.CancellationToken));
            for (var iteration = 0; iteration < 2; iteration++)
            {
                var pipeName = id + iteration;
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
                deadline.CancelAfter(TimeSpan.FromSeconds(25));
                await using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                using (var command = Registry.CurrentUser.CreateSubKey(keyPath + @"\shell\open\command"))
                    command.SetValue(null, $"\"{executable}\" --association-held-marker {pipeName} \"%1\"");
                var connected = pipe.WaitForConnectionAsync(deadline.Token);
                Process? child = null;
                try
                {
                    await provider.FileInvoker.OpenAsync(document, candidate.Target, deadline.Token);
                    await connected;
                    using var reader = new StreamReader(pipe, leaveOpen: true);
                    using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
                    using var response = JsonDocument.Parse(await reader.ReadLineAsync(deadline.Token) ?? throw new IOException("No acknowledgment."));
                    child = Process.GetProcessById(response.RootElement.GetProperty("ProcessId").GetInt32());
                    Assert.Equal(new[] { "--association-held-marker", pipeName, document }, response.RootElement.GetProperty("Arguments").EnumerateArray().Select(v => v.GetString()));
                    Assert.False(child.HasExited);
                    await writer.WriteLineAsync("finish".AsMemory(), deadline.Token);
                    await child.WaitForExitAsync(deadline.Token);
                    Assert.Equal(0, child.ExitCode);
                }
                finally
                {
                    await deadline.CancelAsync();
                    try { await connected; } catch (OperationCanceledException) { }
                    if (child is not null)
                    {
                        if (!child.HasExited) child.Kill();
                        await child.WaitForExitAsync(CancellationToken.None);
                        child.Dispose();
                    }
                }
            }
            Assert.Equal("owned document", File.ReadAllText(document));
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(keyPath, false);
            folder.Delete(true);
        }
    }
}
