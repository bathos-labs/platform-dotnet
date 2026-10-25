using Bathos.Platform;
using Bathos.Platform.Files;
using Bathos.Platform.Linux;
using Bathos.Platform.MacOS;
using Bathos.Platform.Windows;

// Keep this source on the pinned stable API. Both its callers and implementations are compiled against that release.
public static class ReleasedConsumer
{
    public static IPlatformProvider CreateMinimalProvider() => new MinimalProvider();

    public static async Task ExerciseAsync()
    {
        IPlatformProvider[] providers = [new WindowsPlatformProvider(), new MacOSPlatformProvider(), new LinuxPlatformProvider()];
        var current = PlatformProviders.SelectCurrent(providers: providers);
        var minimal = CreateMinimalProvider();
        if (minimal.FileRename is not null || minimal.FileEntries is not null || minimal.ScreenColorSampler is not null ||
            minimal.FileRevealer is not null || minimal.FileInvoker is not null || minimal.SnapshotFileRename is not null ||
            minimal.FilePaths is not null || minimal.Directories is not null || minimal.FileLeases is not null ||
            minimal.ColorProfiles is not null)
            throw new InvalidOperationException("Released provider defaults changed.");
        if (!ReferenceEquals(minimal, PlatformProviders.Select(host: PlatformHost.Current, providers: [minimal])))
            throw new InvalidOperationException("Released provider selection changed.");

        var folder = Directory.CreateTempSubdirectory("bathos-released-consumer-");
        try
        {
            var original = Path.Combine(folder.FullName, "original.txt");
            await File.WriteAllTextAsync(original, "original");
            (current.FileEntries ?? throw new InvalidOperationException("No native inspection capability.")).RequireRegularFile(original);
            var nativeRename = current.FileRename ?? throw new InvalidOperationException("No native rename capability.");
            var rename = new DelegatingRename(nativeRename);
            await using (var publication = new FilePublication(directory: folder.FullName, rename: rename))
            {
                await publication.Stream.WriteAsync("first"u8.ToArray());
                var path = await publication.PublishAsync(name: new GeneratedFileName(baseName: "first", extension: ".txt"), token: CancellationToken.None);
                if (await File.ReadAllTextAsync(path) != "first") throw new InvalidOperationException("Released publication failed.");
            }
            var factory = new FilePublicationFactory(rename: rename);
            await using (var publication = factory.Create(directory: folder.FullName))
            {
                await publication.Stream.WriteAsync("second"u8.ToArray());
                var path = await publication.PublishExactAsync(fileName: "second.txt", token: CancellationToken.None, replaceExisting: false);
                if (await File.ReadAllTextAsync(path) != "second") throw new InvalidOperationException("Released factory failed.");
            }
        }
        finally { folder.Delete(recursive: true); }
    }

    private sealed class MinimalProvider : IPlatformProvider
    {
        public PlatformOperatingSystem OperatingSystem => PlatformHost.Current;
    }

    private sealed class DelegatingRename(INativeFileRename inner) : INativeFileRename
    {
        public FileRenameStatus Move(string source, string destination, out int errorCode, bool replaceExisting = false) =>
            inner.Move(source, destination, out errorCode, replaceExisting);
    }
}
