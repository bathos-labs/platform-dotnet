using System.Diagnostics;
using System.Text.Json;
using Bathos.Platform.Desktop;
using Bathos.Platform.Linux;
using Xunit;

namespace Bathos.Platform.Tests;

public sealed class LinuxApplicationIconTests
{
    [Fact]
    [Trait("Validation", "Native")]
    public async Task MissingRegistrationCancellationAndUnsupportedTargetsRemainDistinct()
    {
        if (!OperatingSystem.IsLinux()) Assert.Skip("Native GIO icon retrieval requires Linux.");
        var provider = new LinuxApplicationIconProvider();
        var target = ApplicationTarget.LinuxDesktopEntry("org.bathos.missing" + Guid.NewGuid().ToString("N") + ".desktop");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(25));
        var token = deadline.Token;
        await Assert.ThrowsAsync<FileNotFoundException>(() => provider.GetIconAsync(target, 32, token));
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.GetIconAsync(target, 32, canceled.Token));
        await Assert.ThrowsAsync<NotSupportedException>(() => provider.GetIconAsync(ApplicationTarget.Executable("/bin/false"), 32, token));
    }
    [Fact]
    [Trait("Validation", "Native")]
    public async Task GioDesktopEntryResolvesThemedAndAbsoluteIconsWithoutLaunchingTheApplication()
    {
        if (!OperatingSystem.IsLinux()) Assert.Skip("Native GIO icon retrieval requires Linux; no desktop is used.");
        var folder = Directory.CreateTempSubdirectory("bathos-gio-icon-");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(25));
        var token = deadline.Token;
        try
        {
            var apps = Directory.CreateDirectory(Path.Combine(folder.FullName, "applications"));
            var theme = Directory.CreateDirectory(Path.Combine(folder.FullName, "icons", "hicolor"));
            var icons = Directory.CreateDirectory(Path.Combine(theme.FullName, "32x32", "apps"));
            var resource = Path.Combine(icons.FullName, "owned.svg");
            const string content = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"32\" height=\"32\"/>";
            await File.WriteAllTextAsync(resource, content, token);
            await File.WriteAllTextAsync(Path.Combine(theme.FullName, "index.theme"), "[Icon Theme]\nName=Owned fixture\nDirectories=32x32/apps\n[32x32/apps]\nSize=32\nType=Fixed\n", token);
            foreach (var iconValue in new[] { "owned", resource })
            {
                const string id = "org.bathos.IconProbe.desktop";
                await File.WriteAllTextAsync(Path.Combine(apps.FullName, id), $"[Desktop Entry]\nType=Application\nName=Owned icon probe\nExec=/bin/false\nIcon={iconValue}\n", token);
                var config = Path.Combine(folder.FullName, "config.json");
                await File.WriteAllTextAsync(config, JsonSerializer.Serialize(new { Mode = "application-icon", PipeName = "unused", DesktopId = id }), token);
                var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "TestApplication", "Bathos.Platform.TestApplication"))
                { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
                start.ArgumentList.Add(config); start.Environment["XDG_DATA_HOME"] = folder.FullName; start.Environment["XDG_DATA_DIRS"] = folder.FullName;
                start.Environment["HOME"] = folder.FullName;
                using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the owned GIO probe.");
                var output = process.StandardOutput.ReadToEndAsync(token); var errors = process.StandardError.ReadToEndAsync(token);
                try
                {
                    await process.WaitForExitAsync(token);
                    Assert.True(process.ExitCode == 0, await errors);
                    using var json = JsonDocument.Parse(await output);
                    Assert.Equal((int)ApplicationIconFormat.Svg, json.RootElement.GetProperty("Format").GetInt32());
                    Assert.Equal(System.Text.Encoding.UTF8.GetBytes(content), Convert.FromBase64String(json.RootElement.GetProperty("Data").GetString() ?? throw new InvalidDataException()));
                }
                finally
                {
                    if (!process.HasExited) process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(CancellationToken.None);
                    await Task.WhenAll(output, errors);
                }
            }
        }
        finally { folder.Delete(true); }
    }
}
