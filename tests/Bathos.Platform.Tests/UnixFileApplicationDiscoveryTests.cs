using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Bathos.Platform.Desktop;
using Bathos.Platform.Linux;
using Bathos.Platform.MacOS;
using Xunit;

namespace Bathos.Platform.Tests;

public sealed partial class UnixFileApplicationDiscoveryTests
{
    [Fact]
    [Trait("Validation", "Native")]
    public async Task ActualFileDiscoveryRejectsInvalidFilesAndCancellation()
    {
        if (OperatingSystem.IsWindows()) Assert.Skip("Unix adapters required.");
        IPlatformProvider platform = OperatingSystem.IsMacOS() ? new MacOSPlatformProvider() : new LinuxPlatformProvider();
        var discovery = Assert.IsAssignableFrom<IApplicationDiscovery>(platform.ApplicationDiscovery);
        var directory = Directory.CreateTempSubdirectory("bathos-discovery-");
        var token = TestContext.Current.CancellationToken;
        try
        {
            var path = Path.Combine(directory.FullName, "sample.txt");
            await File.WriteAllTextAsync(path, "plain text", token);
            var candidates = await discovery.FindForFileAsync(path, token);
            Assert.All(candidates, candidate => Assert.False(string.IsNullOrWhiteSpace(candidate.DisplayName)));
            Assert.Equal(candidates.Count, candidates.Select(candidate => candidate.Target).Distinct().Count());
            foreach (var name in new[] { "unknown.bathosunregistered", "extensionless" })
            {
                var other = Path.Combine(directory.FullName, name);
                await File.WriteAllTextAsync(other, "plain text", token);
                Assert.NotNull(await discovery.FindForFileAsync(other, token));
            }
            await Assert.ThrowsAnyAsync<IOException>(() => discovery.FindForFileAsync(directory.FullName, token));
            await Assert.ThrowsAnyAsync<IOException>(() => discovery.FindForFileAsync(path + ".missing", token));
            using var canceled = new CancellationTokenSource(); canceled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => discovery.FindForFileAsync(path, canceled.Token));
        }
        finally { directory.Delete(true); }
    }

    [Fact]
    [Trait("Validation", "Native")]
    public async Task MacOwnedBundlesPreserveDistinctIdentitiesAndComposeWithIcons()
    {
        if (!OperatingSystem.IsMacOS()) { Assert.Skip("Native NSWorkspace required."); return; }
        var directory = Directory.CreateDirectory(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "BathosDiscoveryTest-" + Guid.NewGuid().ToString("N")));
        var registered = new List<string>();
        var extension = "bathos" + Guid.NewGuid().ToString("N");
        var token = TestContext.Current.CancellationToken;
        try
        {
            foreach (var name in new[] { "First", "Second" })
            {
                var bundle = Path.Combine(directory.FullName, name + ".app");
                var contents = Directory.CreateDirectory(Path.Combine(bundle, "Contents"));
                var executable = Path.Combine(Directory.CreateDirectory(Path.Combine(contents.FullName, "MacOS")).FullName, "Owned");
                await File.WriteAllTextAsync(executable, "#!/bin/sh\nexit 1\n", token);
                File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                await File.WriteAllTextAsync(Path.Combine(contents.FullName, "Info.plist"),
                    $"<?xml version=\"1.0\"?><plist version=\"1.0\"><dict><key>CFBundleIdentifier</key><string>org.bathos.{extension}.{name}</string><key>CFBundleName</key><string>Owned Editor</string><key>CFBundleDisplayName</key><string>Owned Editor</string><key>CFBundleExecutable</key><string>Owned</string><key>CFBundleVersion</key><string>1</string><key>CFBundlePackageType</key><string>APPL</string><key>UTExportedTypeDeclarations</key><array><dict><key>UTTypeIdentifier</key><string>org.bathos.{extension}</string><key>UTTypeConformsTo</key><array><string>public.data</string></array><key>UTTypeTagSpecification</key><dict><key>public.filename-extension</key><array><string>{extension}</string></array></dict></dict></array><key>CFBundleDocumentTypes</key><array><dict><key>LSItemContentTypes</key><array><string>org.bathos.{extension}</string></array><key>CFBundleTypeName</key><string>Owned</string><key>CFBundleTypeRole</key><string>Editor</string><key>CFBundleTypeExtensions</key><array><string>{extension}</string></array></dict></array></dict></plist>", token);
                registered.Add(bundle);
                await Register("-f", bundle);
                Assert.NotEmpty(MacOSApplicationDiscovery.FindByBundleIdentifier($"org.bathos.{extension}.{name}"));
            }
            var document = Path.Combine(directory.FullName, "drawing." + extension);
            await File.WriteAllBytesAsync(document, [], token);
            var provider = new MacOSPlatformProvider();
            var candidates = await provider.ApplicationDiscovery.FindForFileAsync(document, token);
            var owned = candidates.Where(candidate => registered.Contains(candidate.Target.Value, StringComparer.Ordinal)).ToArray();
            Assert.True(owned.Length == 2, "Expected: " + string.Join(", ", registered) + "; Found: " + string.Join(", ", candidates.Select(c => c.Target.Value)));
            foreach (var candidate in owned)
            {
                Assert.Equal(ApplicationTargetKind.MacOSBundle, candidate.Target.Kind);
                var icon = Assert.IsType<ApplicationIcon>(await provider.ApplicationIcons.GetIconAsync(candidate.Target, 16, token));
                Assert.Equal(16 * 16 * 4, icon.Data.Length);
            }
            // Removing an owned bundle invalidates the explicit selection; it must not select another app.
            Directory.Delete(registered[0], true);
            await Assert.ThrowsAnyAsync<IOException>(() => provider.FileInvoker.OpenAsync(document,
                owned.Single(candidate => candidate.Target.Value == registered[0]).Target, token));
        }
        finally
        {
            foreach (var bundle in registered) await Register("-u", bundle);
            directory.Delete(true);
        }
    }

    private static async Task Register(string operation, string bundle)
    {
        if (operation == "-f")
        {
            var bytes = Encoding.UTF8.GetBytes(bundle);
            var url = CreateUrl(0, bytes, bytes.Length, 1);
            Assert.NotEqual(0, url);
            try { Assert.Equal(0, RegisterUrl(url, 1)); }
            finally { Release(url); }
            return;
        }
        var start = new ProcessStartInfo("/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister")
        { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
        start.ArgumentList.Add("-v"); start.ArgumentList.Add("-lint"); start.ArgumentList.Add(operation); start.ArgumentList.Add(bundle);
        using var process = Process.Start(start) ?? throw new IOException("Could not start owned registration helper.");
        var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var detail = await output;
        Assert.DoesNotContain("failed", detail, StringComparison.OrdinalIgnoreCase);
        Assert.True(process.ExitCode == 0, await errors);
    }
    [LibraryImport("/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/LaunchServices", EntryPoint = "LSRegisterURL")]
    private static partial int RegisterUrl(nint url, byte update);
    [LibraryImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation", EntryPoint = "CFURLCreateFromFileSystemRepresentation")]
    private static partial nint CreateUrl(nint allocator, byte[] bytes, nint length, byte directory);
    [LibraryImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation", EntryPoint = "CFRelease")]
    private static partial void Release(nint value);
}
