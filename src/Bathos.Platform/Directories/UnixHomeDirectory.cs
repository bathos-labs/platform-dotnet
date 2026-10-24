using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace Bathos.Platform.Directories;

internal static class UnixHomeDirectory
{
    private const int RangeError = 34; // ERANGE on the supported Darwin and Linux ABIs.
    private const int MaximumBufferSize = 1024 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal readonly record struct Result(int Error, bool Found, nint Home);

    internal static string? Resolve(string? configuredHome, Func<nint, int, Result> lookup)
    {
        if (!string.IsNullOrEmpty(configuredHome)) return configuredHome;
        for (var size = 1024; size <= MaximumBufferSize; size *= 2)
        {
            var buffer = new byte[size];
            var pinned = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                var address = pinned.AddrOfPinnedObject();
                var result = lookup(address, size);
                if (result.Error == RangeError) continue;
                if (result.Error != 0)
                    throw new IOException("The account home directory could not be read.", new Win32Exception(result.Error));
                if (!result.Found || result.Home == 0) return null;
                var start = (nuint)address;
                var home = (nuint)result.Home;
                if (home < start || home - start >= (nuint)buffer.Length)
                    throw new InvalidDataException("The account home directory is outside its owned buffer.");
                var offset = (int)(home - start);
                var end = Array.IndexOf(buffer, (byte)0, offset);
                if (end < 0) throw new InvalidDataException("The account home directory is not terminated.");
                if (end == offset) return null;
                try { return StrictUtf8.GetString(buffer, offset, end - offset); }
                catch (DecoderFallbackException error) { throw new InvalidDataException("The account home directory is not valid UTF-8.", error); }
            }
            finally { pinned.Free(); }
        }
        throw new IOException("The account entry exceeds the one-megabyte lookup buffer limit.");
    }
}
