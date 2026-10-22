using System.Runtime.InteropServices;
using Bathos.Platform.Files;
using Microsoft.Win32.SafeHandles;

namespace Bathos.Platform.Windows;

/// <summary>Rejects directories/reparse points and requires an ordinary disk-file handle.</summary>
public sealed partial class WindowsFileEntryProbe : IFileEntryProbe
{
    public void RequireRegularFile(string path)
    {
        if ((File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.Device)) != 0)
            throw new IOException("An output file is occupied by a nonregular file or link.");
        using var handle = Open(path, 0x80, 7, IntPtr.Zero, 3, 0x00200000, IntPtr.Zero);
        if (handle.IsInvalid) throw FileEntryErrors.Native();
        if (GetFileType(handle) != 1) throw new IOException("A regular disk file is required.");
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial SafeFileHandle Open(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial uint GetFileType(SafeFileHandle handle);
}
