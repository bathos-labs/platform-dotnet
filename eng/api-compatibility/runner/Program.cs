using System.Reflection;
using System.Security.Cryptography;
using Bathos.Platform;
using Bathos.Platform.Linux;
using Bathos.Platform.MacOS;
using Bathos.Platform.Windows;

Assembly[] assemblies = [typeof(IPlatformProvider).Assembly, typeof(WindowsPlatformProvider).Assembly,
    typeof(MacOSPlatformProvider).Assembly, typeof(LinuxPlatformProvider).Assembly, typeof(ReleasedConsumer).Assembly];
if (args.Length != assemblies.Length) throw new ArgumentException("Expected four candidate assembly hashes and the released consumer hash.");
for (var index = 0; index < assemblies.Length; index++)
{
    var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assemblies[index].Location)));
    if (hash != args[index]) throw new InvalidOperationException($"Loaded unexpected bytes for {assemblies[index].GetName().Name}.");
}
await ReleasedConsumer.ExerciseAsync();
var oldProvider = ReleasedConsumer.CreateMinimalProvider();
if (oldProvider.SnapshotFileRename is not null || oldProvider.FilePaths is not null || oldProvider.Directories is not null ||
    oldProvider.FileLeases is not null || oldProvider.ColorProfiles is not null)
    throw new InvalidOperationException("New capabilities must remain absent on the released provider implementation.");
Console.WriteLine("Unrecompiled released consumer and provider passed against the exact candidate assemblies.");
