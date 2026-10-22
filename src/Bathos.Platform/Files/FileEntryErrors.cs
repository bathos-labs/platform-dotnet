using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Bathos.Platform.Files;

internal static class FileEntryErrors
{
    internal static void ValidatePath(string path, string parameterName)
    {
        ArgumentException.ThrowIfNullOrEmpty(path, parameterName);
        if (path.Contains('\0', StringComparison.Ordinal))
            throw new ArgumentException("A filesystem path cannot contain a null character.", parameterName);
    }

    internal static IOException Native() => new("The file kind could not be inspected.", new Win32Exception(Marshal.GetLastPInvokeError()));
    internal static void RequireRegular(ushort mode)
    {
        if ((mode & 0xf000) != 0x8000) throw new IOException("An output file is occupied by a nonregular file or link.");
    }
}
