using System.Runtime.InteropServices;
using Bathos.Platform.Files;

namespace Bathos.Platform.MacOS;

/// <summary>Uses the Darwin 64-bit inode stat ABI to distinguish regular files from pipes, sockets, and devices.</summary>
public sealed partial class MacFileEntryProbe : IFileEntryProbe
{
    public void RequireRegularFile(string path)
    {
        if (LStat(path, out var info) != 0) throw FileEntryErrors.Native();
        FileEntryErrors.RequireRegular(info.Mode);
    }

    // Darwin struct stat: mode_t follows dev_t. Retain the complete native allocation, including reserved fields.
    [StructLayout(LayoutKind.Explicit, Size = 144)]
    private struct Stat { [FieldOffset(4)] internal ushort Mode; }

    [LibraryImport("libSystem.B.dylib", EntryPoint = "lstat", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int LStat(string path, out Stat info);
}
