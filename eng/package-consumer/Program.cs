using Bathos.Platform;
using Bathos.Platform.Files;
using System.Runtime.CompilerServices;

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
if (args is ["--native-aot"] && RuntimeFeature.IsDynamicCodeSupported)
    throw new InvalidOperationException("Expected a Native AOT executable.");
var folder = Directory.CreateTempSubdirectory("bathos-consumer-");
try
{
    var rename = provider.FileRename ?? throw new InvalidOperationException("No native rename capability.");
    string file;
    await using (var publication = new FilePublication(folder.FullName, rename, createDirectory: false))
    {
        await publication.Stream.WriteAsync("native package consumer"u8.ToArray());
        file = await publication.PublishExactAsync("sample.txt", CancellationToken.None);
    }
    (provider.FileEntries ?? throw new InvalidOperationException("No file inspection capability.")).RequireRegularFile(file);
    using (var reader = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
    {
        var snapshot = provider.SnapshotFileRename ?? throw new InvalidOperationException("No snapshot capability.");
        await using (var publication = new FilePublication(folder.FullName, snapshot, createDirectory: false))
        {
            await publication.Stream.WriteAsync("replacement"u8.ToArray());
            await publication.PublishExactAsync("sample.txt", CancellationToken.None, replaceExisting: true);
        }
        using var text = new StreamReader(reader);
        if (await text.ReadToEndAsync() != "native package consumer" || File.ReadAllText(file) != "replacement")
            throw new InvalidOperationException("Snapshot publication did not preserve old and new bytes.");
    }
    var leases = provider.FileLeases ?? throw new InvalidOperationException("No lease capability.");
    var lockPath = Path.Combine(folder.FullName, "consumer.lock");
    using (var lease = leases.TryAcquire(lockPath, FileLeaseMode.Exclusive)
        ?? throw new InvalidOperationException("Could not acquire the native lease."))
    {
        using var contender = leases.TryAcquire(lockPath, FileLeaseMode.Exclusive);
        if (contender is not null) throw new InvalidOperationException("An exclusive lease admitted a contender.");
    }
    using var reacquired = leases.TryAcquire(lockPath, FileLeaseMode.Exclusive)
        ?? throw new InvalidOperationException("Native lease disposal did not release ownership.");
#if MacOS
    // ColorSync exercises managed delegates called by a native framework without opening a picker.
    var profiles = provider.ColorProfiles ?? throw new InvalidOperationException("No profile capability.");
    var inventory = await profiles.EnumerateAsync();
    if (inventory.Locations.Count == 0) throw new InvalidOperationException($"No ColorSync profiles observed: {inventory.Finding}");
    if ((await profiles.ReadAsync(inventory.Locations[0].Key)).IsEmpty)
        throw new InvalidOperationException("The native profile read returned no bytes.");
    Console.WriteLine($"ColorSync callback inventory: {inventory.Locations.Count} profiles; complete: {inventory.IsComplete}.");
#endif
    var otherProviders = new[] { "MacOS", "Windows", "Linux" }.Where(name => name != provider.OperatingSystem.ToString());
    foreach (var other in otherProviders)
        if (File.Exists(Path.Combine(AppContext.BaseDirectory, $"Bathos.Platform.{other}.dll")))
            throw new InvalidOperationException($"Unselected provider {other} was shipped.");
}
finally { folder.Delete(true); }
Console.WriteLine($"Native {provider.OperatingSystem} package consumer passed without sibling providers.");
