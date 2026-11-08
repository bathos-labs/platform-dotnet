using Bathos.Platform.Desktop;
using Bathos.Platform.Windows;
using Microsoft.Win32;
using Xunit;

namespace Bathos.Platform.Tests;

[Trait("Validation", "Native")]
public sealed class WindowsFileApplicationDiscoveryTests
{
    [Fact]
    public async Task OwnedApplicationRegistrationIsDiscoveredWithoutAnIdentityAndRemovalIsObserved()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Skip("Windows Shell registration required."); return; }
        var id = "BathosDiscovery" + Guid.NewGuid().ToString("N");
        var keyPath = @"Software\Classes\Applications\" + id + ".exe";
        var folder = Directory.CreateTempSubdirectory("bathos-discovery-");
        try
        {
            var executable = Path.Combine(folder.FullName, id + ".exe");
            File.Copy(Environment.ProcessPath ?? throw new InvalidOperationException("Missing process path."), executable);
            var file = Path.Combine(folder.FullName, "document." + id);
            File.WriteAllText(file, "owned fixture");
            using (var key = Registry.CurrentUser.CreateSubKey(keyPath + @"\shell\open\command")) key.SetValue(null, $"\"{executable}\" \"%1\"");
            using (var key = Registry.CurrentUser.CreateSubKey(keyPath + @"\SupportedTypes")) key.SetValue("." + id, "");
            var discovery = new WindowsFileApplicationDiscovery();
            var results = await discovery.FindForFileAsync(file, TestContext.Current.CancellationToken);
            var candidate = Assert.Single(results, c => string.Equals(c.Target.Value, executable, StringComparison.OrdinalIgnoreCase));
            Assert.Equal(ApplicationTargetKind.WindowsAssociation, candidate.Target.Kind);
            Assert.Equal("." + id, candidate.Target.AssociationExtension);
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => discovery.FindForFileAsync(file, canceled.Token));
            Registry.CurrentUser.DeleteSubKeyTree(keyPath);
            Assert.DoesNotContain(await discovery.FindForFileAsync(file, TestContext.Current.CancellationToken), c => c.Target.Value == executable);
            await Assert.ThrowsAsync<FileNotFoundException>(() => new WindowsFileInvoker().OpenAsync(file, candidate.Target, TestContext.Current.CancellationToken));
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(keyPath, false);
            folder.Delete(true);
        }
    }
}
