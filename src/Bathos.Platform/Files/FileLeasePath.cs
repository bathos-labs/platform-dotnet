using System.Text;
using Bathos.Platform.Paths;

namespace Bathos.Platform.Files;

internal static class FileLeasePath
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static string NormalizeUnix(string path)
    {
        path = NativeFilePaths.Posix.NormalizeAbsolute(path);
        // The interop marshaller otherwise replaces unpaired surrogates, selecting a different artifact.
        try { StrictUtf8.GetByteCount(path); }
        catch (EncoderFallbackException error)
        {
            throw new ArgumentException("A Unix lock path requires well-formed Unicode.", nameof(path), error);
        }
        return path;
    }
}
