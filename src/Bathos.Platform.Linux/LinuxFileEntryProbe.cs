using System.Runtime.InteropServices;
using Bathos.Platform.Files;

namespace Bathos.Platform.Linux;

/// <summary>Linux statx inspection that rejects links and nonregular files without opening them.</summary>
public sealed partial class LinuxFileEntryProbe : IFileEntryProbe
{
    public void RequireRegularFile(string path)
    {
        FileEntryErrors.ValidatePath(path, nameof(path));
        // AT_FDCWD, AT_SYMLINK_NOFOLLOW, STATX_TYPE; statx has a fixed architecture-independent layout.
        if (StatX(-100, path, 0x100, 1, out var info) != 0) throw FileEntryErrors.Native();
        if ((info.Mask & 1) == 0) throw new IOException("The filesystem did not identify the file kind.");
        FileEntryErrors.RequireRegular(info.Mode);
    }

    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct Stat { [FieldOffset(0)] internal uint Mask; [FieldOffset(28)] internal ushort Mode; }

    [LibraryImport("libc", EntryPoint = "statx", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int StatX(int directory, string path, int flags, uint mask, out Stat info);
}
