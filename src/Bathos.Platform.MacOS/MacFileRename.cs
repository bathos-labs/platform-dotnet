using System.Runtime.InteropServices;
using Bathos.Platform.Files;

namespace Bathos.Platform.MacOS;

/// <summary>macOS exclusive rename, or explicitly authorized replacement, on the same filesystem.</summary>
public sealed partial class MacFileRename : INativeFileRename
{
    public FileRenameStatus Move(string source, string destination, out int errorCode, bool replaceExisting = false)
    {
        FileEntryErrors.ValidatePath(source, nameof(source));
        FileEntryErrors.ValidatePath(destination, nameof(destination));
        // RENAME_EXCL checks and claims the destination in the same filesystem operation.
        if (Rename(source, destination, replaceExisting ? 0u : 0x4u) == 0) { errorCode = 0; return FileRenameStatus.Success; }
        errorCode = Marshal.GetLastPInvokeError();
        return errorCode switch
        {
            17 => FileRenameStatus.DestinationExists,
            18 => FileRenameStatus.CrossVolumeUnsupported,
            _ => FileRenameStatus.Unavailable,
        };
    }

    [LibraryImport("libSystem.B.dylib", EntryPoint = "renamex_np", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int Rename(string source, string destination, uint flags);
}
