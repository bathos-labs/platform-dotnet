using Bathos.Platform;

#if MacOS
IPlatformProvider provider = new Bathos.Platform.MacOS.MacOSPlatformProvider();
#elif Windows
IPlatformProvider provider = new Bathos.Platform.Windows.WindowsPlatformProvider();
#elif Linux
IPlatformProvider provider = new Bathos.Platform.Linux.LinuxPlatformProvider();
#else
#error Select a provider.
#endif
if (!ReferenceEquals(provider, PlatformProviders.SelectCurrent(provider))) throw new InvalidOperationException("Provider selection failed.");
if (provider.FileRevealer is null || provider.FileInvoker is null) throw new InvalidOperationException("No desktop handoff capabilities.");
var folder = Directory.CreateTempSubdirectory("bathos-consumer-");
try
{
    var file = Path.Combine(folder.FullName, "sample.txt");
    File.WriteAllText(file, "native package consumer");
    (provider.FileEntries ?? throw new InvalidOperationException("No file inspection capability.")).RequireRegularFile(file);
    var otherProviders = new[] { "MacOS", "Windows", "Linux" }.Where(name => name != provider.OperatingSystem.ToString());
    foreach (var other in otherProviders)
        if (File.Exists(Path.Combine(AppContext.BaseDirectory, $"Bathos.Platform.{other}.dll")))
            throw new InvalidOperationException($"Unselected provider {other} was shipped.");
}
finally { folder.Delete(true); }
Console.WriteLine($"Native {provider.OperatingSystem} package consumer passed without sibling providers.");
