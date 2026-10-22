using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Bathos.Platform.Files;

internal static class FileEntryErrors
{
    internal static IOException Native() => new("The file kind could not be inspected.", new Win32Exception(Marshal.GetLastPInvokeError()));
    internal static void RequireRegular(ushort mode)
    {
        if ((mode & 0xf000) != 0x8000) throw new IOException("An output file is occupied by a nonregular file or link.");
    }
}
