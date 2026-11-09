using Bathos.Platform.Desktop;
using Bathos.Platform.Windows;
using Microsoft.Win32;
using Xunit;

namespace Bathos.Platform.Tests;

[Trait("Validation", "Native")]
[Collection("Windows Shell registrations")]
public sealed class WindowsFileApplicationDiscoveryTests
{
    [Theory]
    [InlineData("")]
    [InlineData(".bathos-unknown-extension")]
    [InlineData(".png")]
    public async Task LookupUsesExtensionContextAndRejectsInvalidFiles(string extension)
    {
        if (!OperatingSystem.IsWindows()) { Assert.Skip("Windows Shell required."); return; }
        var folder = Directory.CreateTempSubdirectory("bathos-types-");
        try
        {
            var path = Path.Combine(folder.FullName, "document" + extension);
            File.WriteAllText(path, "Plain text, including when the extension is PNG.");
            var discovery = new WindowsFileApplicationDiscovery();
            var results = await discovery.FindForFileAsync(path, TestContext.Current.CancellationToken);
            Assert.All(results, c => Assert.Equal(extension, c.Target.AssociationExtension));
            Assert.Equal(results.Count, results.Select(c => c.Target.Value).Distinct(StringComparer.OrdinalIgnoreCase).Count());
            await Assert.ThrowsAnyAsync<IOException>(() => discovery.FindForFileAsync(folder.FullName, TestContext.Current.CancellationToken));
            File.Delete(path);
            await Assert.ThrowsAnyAsync<IOException>(() => discovery.FindForFileAsync(path, TestContext.Current.CancellationToken));
        }
        finally { folder.Delete(true); }
    }

    [Fact]
    public async Task InstalledIndirectAssociationIconsRenderWithoutLaunchingApplications()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Skip("Windows Shell required."); return; }
        var targets = await WindowsAssociationHandlers.Run(() => WindowsAssociationHandlers.WithHandlers(".png",
            entries => entries.Where(e => WindowsAssociationHandlers.IconLocation(e.Handle).Location?.StartsWith("@{", StringComparison.Ordinal) == true)
                .Select(e => ApplicationTarget.WindowsAssociation(e.Name, ".png")).ToArray(), TestContext.Current.CancellationToken));
        if (targets.Length == 0) { Assert.Skip("No installed indirect PNG handlers on this host."); return; }
        foreach (var target in targets)
        {
            var icon = await new WindowsApplicationIconProvider().GetIconAsync(target, 64, TestContext.Current.CancellationToken);
            Assert.NotNull(icon);
            Assert.Equal(64, icon.PixelWidth);
            Assert.Equal(64 * 64 * 4, icon.Data.Length);
            Assert.Contains(icon.Data.ToArray(), value => value != 0);
        }
    }

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
            using (var key = Registry.CurrentUser.CreateSubKey(keyPath + "Duplicate.exe" + @"\shell\open\command")) key.SetValue(null, $"\"{executable}\" --alternate \"%1\"");
            using (var key = Registry.CurrentUser.CreateSubKey(keyPath + "Duplicate.exe" + @"\SupportedTypes")) key.SetValue("." + id, "");
            var discovery = new WindowsFileApplicationDiscovery();
            var results = await discovery.FindForFileAsync(file, TestContext.Current.CancellationToken);
            var candidate = Assert.Single(results, c => string.Equals(c.Target.Value, executable, StringComparison.OrdinalIgnoreCase));
            Assert.Equal(ApplicationTargetKind.WindowsAssociation, candidate.Target.Kind);
            Assert.Equal("." + id, candidate.Target.AssociationExtension);
            var icon = await new WindowsApplicationIconProvider().GetIconAsync(candidate.Target, 32, TestContext.Current.CancellationToken);
            Assert.NotNull(icon);
            Assert.Equal(32 * 32 * 4, icon.Data.Length);
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => discovery.FindForFileAsync(file, canceled.Token));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new WindowsFileInvoker().OpenAsync(file, candidate.Target, canceled.Token));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new WindowsApplicationIconProvider().GetIconAsync(candidate.Target, 32, canceled.Token));
            Registry.CurrentUser.DeleteSubKeyTree(keyPath);
            Registry.CurrentUser.DeleteSubKeyTree(keyPath + "Duplicate.exe");
            Assert.DoesNotContain(await discovery.FindForFileAsync(file, TestContext.Current.CancellationToken), c => c.Target.Value == executable);
            await Assert.ThrowsAsync<FileNotFoundException>(() => new WindowsFileInvoker().OpenAsync(file, candidate.Target, TestContext.Current.CancellationToken));
            await Assert.ThrowsAsync<FileNotFoundException>(() => new WindowsApplicationIconProvider().GetIconAsync(candidate.Target, 32, TestContext.Current.CancellationToken));
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(keyPath, false);
            Registry.CurrentUser.DeleteSubKeyTree(keyPath + "Duplicate.exe", false);
            folder.Delete(true);
        }
    }
}
