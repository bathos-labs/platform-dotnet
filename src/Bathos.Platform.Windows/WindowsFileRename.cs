using System.Runtime.InteropServices;
using Bathos.Platform.Files;

namespace Bathos.Platform.Windows;

/// <summary>Windows exclusive rename, or explicitly authorized replacement, without COPY_ALLOWED.</summary>
public sealed partial class WindowsFileRename : INativeFileRename
{
    public FileRenameStatus Move(string source, string destination, out int errorCode, bool replaceExisting = false)
    {
        FileEntryErrors.ValidatePath(source, nameof(source));
        FileEntryErrors.ValidatePath(destination, nameof(destination));
        if (Rename(source, destination, replaceExisting ? 0x1u : 0u)) { errorCode = 0; return FileRenameStatus.Success; }
        errorCode = Marshal.GetLastPInvokeError();
        return errorCode switch
        {
            80 or 183 => FileRenameStatus.DestinationExists,
            17 => FileRenameStatus.CrossVolumeUnsupported,
            _ => FileRenameStatus.Unavailable,
        };
    }

    [LibraryImport("kernel32.dll", EntryPoint = "MoveFileExW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool Rename(string source, string destination, uint flags);
}
