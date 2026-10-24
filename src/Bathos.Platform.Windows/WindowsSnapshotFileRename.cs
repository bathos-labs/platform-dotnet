using System.Runtime.InteropServices;
using Bathos.Platform.Files;
using Microsoft.Win32.SafeHandles;

namespace Bathos.Platform.Windows;

/// <summary>Publishes regular staged files through a handle-based atomic rename that preserves retained readers.</summary>
/// <remarks>Requires Windows 10 version 1607 or later and filesystem support for FileRenameInfoEx/POSIX replacement. No weaker fallback is used.</remarks>
public sealed partial class WindowsSnapshotFileRename : ISnapshotFileRename
{
    /// <include file="../Bathos.Platform/Documentation/Contracts.xml" path="contracts/Move/*" />
    public unsafe FileRenameStatus Move(string source, string destination, out int errorCode, bool replaceExisting = false)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 14393)) throw new PlatformNotSupportedException();
        FileEntryErrors.ValidatePath(source, nameof(source));
        FileEntryErrors.ValidatePath(destination, nameof(destination));
        source = Path.GetFullPath(source);
        destination = Path.GetFullPath(destination);
        // DELETE access, all sharing, OPEN_EXISTING, OPEN_REPARSE_POINT. No directory or link traversal at the leaf.
        using var handle = Open(Extended(source), 0x00010000, 7, 0, 3, 0x00200000, 0);
        if (handle.IsInvalid) return Failure(out errorCode);
        if (!GetFileInformationByHandleEx(handle, 9, out var attributes, 8)) return Failure(out errorCode);
        if ((attributes.Attributes & (0x10u | 0x400u | 0x40u)) != 0)
        {
            errorCode = 87; // ERROR_INVALID_PARAMETER: only regular staging files are admitted.
            return FileRenameStatus.Unavailable;
        }
        var name = Extended(destination);
        var nameBytes = checked(name.Length * sizeof(char));
        var nameOffset = (int)Marshal.OffsetOf<RenameInformation>(nameof(RenameInformation.FileName));
        var buffer = new byte[checked(nameOffset + nameBytes + sizeof(char))];
        fixed (byte* pointer = buffer)
        {
            var information = (RenameInformation*)pointer;
            information->Flags = replaceExisting ? 3u : 0u; // REPLACE_IF_EXISTS | POSIX_SEMANTICS
            information->FileNameLength = (uint)nameBytes;
            name.AsSpan().CopyTo(new Span<char>(pointer + nameOffset, name.Length));
            if (!SetFileInformationByHandle(handle, 22, pointer, (uint)buffer.Length)) return Failure(out errorCode);
        }
        errorCode = 0;
        return FileRenameStatus.Success;
    }

    private static string Extended(string path) => path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path
        : path.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + path[2..] : @"\\?\" + path;

    private static FileRenameStatus Failure(out int errorCode)
    {
        errorCode = Marshal.GetLastPInvokeError();
        return errorCode switch
        {
            80 or 183 => FileRenameStatus.DestinationExists,
            17 => FileRenameStatus.CrossVolumeUnsupported,
            _ => FileRenameStatus.Unavailable,
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RenameInformation { internal uint Flags; internal nint RootDirectory; internal uint FileNameLength; internal char FileName; }
    [StructLayout(LayoutKind.Sequential)]
    private struct AttributeInformation { internal uint Attributes; internal uint ReparseTag; }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial SafeFileHandle Open(string path, uint access, uint sharing, nint security, uint creation, uint flags, nint template);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileInformationByHandleEx(SafeFileHandle handle, int informationClass, out AttributeInformation information, uint size);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool SetFileInformationByHandle(SafeFileHandle handle, int informationClass, void* information, uint size);
}
