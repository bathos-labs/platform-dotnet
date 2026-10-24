using Bathos.Platform.Files;
using Bathos.Platform.Linux;
using Xunit;

namespace Bathos.Platform.Tests;

[Trait("Validation", "Native")]
public sealed class LinuxFileRenameTests
{
    [Fact]
    public void DirectoryRenamePreservesOccupiedTreesAndMovesUnicodeNames()
    {
        if (!OperatingSystem.IsLinux()) Assert.Skip("Requires Linux renameat2.");
        var root = Directory.CreateTempSubdirectory("bathos-rename-");
        try
        {
            var source = Directory.CreateDirectory(Path.Combine(root.FullName, "source-桜"));
            var destination = Directory.CreateDirectory(Path.Combine(root.FullName, "occupied"));
            File.WriteAllText(Path.Combine(source.FullName, "source.txt"), "source");
            File.WriteAllText(Path.Combine(destination.FullName, "keep.txt"), "keep");
            var rename = new LinuxFileRename();
            Assert.Equal(FileRenameStatus.DestinationExists, rename.Move(source.FullName, destination.FullName, out _));
            Assert.Equal("keep", File.ReadAllText(Path.Combine(destination.FullName, "keep.txt")));
            var moved = Path.Combine(root.FullName, "moved-桜");
            Assert.Equal(FileRenameStatus.Success, rename.Move(source.FullName, moved, out _));
            Assert.False(Directory.Exists(source.FullName));
            Assert.Equal("source", File.ReadAllText(Path.Combine(moved, "source.txt")));
        }
        finally { root.Delete(true); }
    }

    [Fact]
    public void CrossVolumeRenameDoesNotCopyOrDeleteEitherFile()
    {
        if (!OperatingSystem.IsLinux() || !Directory.Exists("/dev/shm")) Assert.Skip("Requires Linux with a separate /dev/shm filesystem.");
        var sourceRoot = Directory.CreateTempSubdirectory("bathos-cross-volume-");
        var destinationRoot = Directory.CreateDirectory(Path.Combine("/dev/shm", "bathos-" + Guid.NewGuid().ToString("N")));
        try
        {
            var source = Path.Combine(sourceRoot.FullName, "source");
            var destination = Path.Combine(destinationRoot.FullName, "destination");
            File.WriteAllText(source, "source");
            File.WriteAllText(destination, "destination");
            Assert.Equal(FileRenameStatus.CrossVolumeUnsupported, new LinuxFileRename().Move(source, destination, out _, replaceExisting: true));
            Assert.Equal("source", File.ReadAllText(source));
            Assert.Equal("destination", File.ReadAllText(destination));
        }
        finally { sourceRoot.Delete(true); destinationRoot.Delete(true); }
    }
}
