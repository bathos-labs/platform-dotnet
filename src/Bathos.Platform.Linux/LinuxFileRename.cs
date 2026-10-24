using System.Runtime.InteropServices;
using Bathos.Platform.Files;

namespace Bathos.Platform.Linux;

/// <summary>Linux atomic rename with exclusive creation or explicitly authorized replacement and no copy fallback.</summary>
/// <remarks>Requires libc renameat2 and filesystem support for RENAME_NOREPLACE. Unsupported operations fail explicitly.</remarks>
public sealed partial class LinuxFileRename : INativeFileRename
{
    /// <include file="../Bathos.Platform/Documentation/Contracts.xml" path="contracts/Move/*" />
    public FileRenameStatus Move(string source, string destination, out int errorCode, bool replaceExisting = false)
    {
        FileEntryErrors.ValidatePath(source, nameof(source));
        FileEntryErrors.ValidatePath(destination, nameof(destination));
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
        // Absolute paths ignore AT_FDCWD; RENAME_NOREPLACE checks and claims in one operation.
        if (Rename(-100, source, -100, destination, replaceExisting ? 0u : 1u) == 0)
        {
            errorCode = 0;
            return FileRenameStatus.Success;
        }
        errorCode = Marshal.GetLastPInvokeError();
        return errorCode switch
        {
            17 => FileRenameStatus.DestinationExists,
            18 => FileRenameStatus.CrossVolumeUnsupported,
            _ => FileRenameStatus.Unavailable,
        };
    }

    [LibraryImport("libc", EntryPoint = "renameat2", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int Rename(int sourceDirectory, string source, int destinationDirectory, string destination, uint flags);
}
