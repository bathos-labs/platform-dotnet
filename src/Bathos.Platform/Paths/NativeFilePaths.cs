using System.Diagnostics.CodeAnalysis;

namespace Bathos.Platform.Paths;

/// <summary>Host-native lexical path operations without filesystem access or alias resolution.</summary>
/// <remarks>Comparison and lease identity belong to the caller. Normalization does not establish that aliases,
/// network locations, or alternate URI representations share ownership. Critical hosts must supply consistent
/// locations and keep lease locations and their containing directories stable throughout ownership.</remarks>
public interface INativeFilePaths
{
    /// <include file="../Documentation/Contracts.xml" path="contracts/NormalizeAbsolute/*" />
    string NormalizeAbsolute(string path);
    /// <include file="../Documentation/Contracts.xml" path="contracts/ToFileUri/*" />
    string ToFileUri(string path, bool directory = false);
    /// <include file="../Documentation/Contracts.xml" path="contracts/TryFromFileUri/*" />
    bool TryFromFileUri(string fileUri, [NotNullWhen(true)] out string? path);
}

/// <summary>Explicit Windows or POSIX lexical operations backed by the matching runtime's path parser.</summary>
/// <remarks>Construction is inert. Windows operations require Windows; POSIX operations require macOS or Linux.
/// No current directory, filesystem probe, Unicode normalization, case folding, or filename sanitization is applied.</remarks>
public sealed class NativeFilePaths : INativeFilePaths
{
    private readonly bool _windows;
    private NativeFilePaths(bool windows) => _windows = windows;
    /// <summary>Windows drive and UNC syntax. Extended/device namespaces are outside this contract.</summary>
    public static NativeFilePaths Windows { get; } = new(true);
    /// <summary>Local POSIX absolute paths on macOS/Linux; foreign drive and network URI evidence remains unresolved.</summary>
    public static NativeFilePaths Posix { get; } = new(false);

    /// <include file="../Documentation/Contracts.xml" path="contracts/NormalizeAbsolute/*" />
    public string NormalizeAbsolute(string path)
    {
        RequireHost();
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (path.Contains('\0') || !Path.IsPathFullyQualified(path))
            throw new ArgumentException("An absolute native path without NUL is required.", nameof(path));
        if (_windows && (path.Replace('/', '\\').StartsWith(@"\\?\", StringComparison.Ordinal) ||
                         path.Replace('/', '\\').StartsWith(@"\\.\", StringComparison.Ordinal)))
            throw new ArgumentException("Windows device namespaces are not supported.", nameof(path));
        if (!_windows && path.StartsWith("//", StringComparison.Ordinal))
            throw new ArgumentException("Double-slash network roots are not supported.", nameof(path));
        var normalized = Path.GetFullPath(path);
        if (!_windows && (normalized.StartsWith("//", StringComparison.Ordinal) || IsDrivePath(normalized)))
            throw new ArgumentException("Ambiguous network or drive syntax is not a local POSIX reference.", nameof(path));
        return normalized;
    }

    /// <include file="../Documentation/Contracts.xml" path="contracts/ToFileUri/*" />
    public string ToFileUri(string path, bool directory = false)
    {
        var normalized = NormalizeAbsolute(path);
        if (directory) normalized = Path.TrimEndingDirectorySeparator(normalized);
        var portable = _windows ? normalized.Replace('\\', '/') : normalized;
        var uri = !_windows ? "file://" + Escape(portable)
            : portable.StartsWith("//", StringComparison.Ordinal) ? "file:" + Escape(portable)
            : "file:///" + portable[..2] + Escape(portable[2..]);
        return directory && !uri.EndsWith('/') ? uri + "/" : uri;
    }

    /// <include file="../Documentation/Contracts.xml" path="contracts/TryFromFileUri/*" />
    public bool TryFromFileUri(string fileUri, [NotNullWhen(true)] out string? path)
    {
        RequireHost();
        path = null;
        if (string.IsNullOrEmpty(fileUri) || fileUri != fileUri.Trim() || fileUri.Contains('\\') || !ValidEscapes(fileUri) || !fileUri.StartsWith("file://", StringComparison.OrdinalIgnoreCase) ||
            !Uri.TryCreate(fileUri, UriKind.Absolute, out var uri) || !uri.IsFile ||
            uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.UserInfo.Length != 0 || !uri.IsDefaultPort ||
            fileUri.Contains("%2f", StringComparison.OrdinalIgnoreCase) ||
            (_windows && fileUri.Contains("%5c", StringComparison.OrdinalIgnoreCase)) ||
            (!_windows && (uri.Host.Length != 0 || IsDrivePath(uri.AbsolutePath)))) return false;
        try
        {
            path = NormalizeAbsolute(_windows ? uri.LocalPath : Uri.UnescapeDataString(uri.AbsolutePath));
            return true;
        }
        catch (ArgumentException) { return false; }
        catch (NotSupportedException) { return false; }
        catch (PathTooLongException) { return false; }
    }

    private static bool ValidEscapes(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] != '%') continue;
            if (i + 2 >= value.Length || !Uri.IsHexDigit(value[i + 1]) || !Uri.IsHexDigit(value[i + 2])) return false;
            i += 2;
        }
        return true;
    }

    private static bool IsDrivePath(string path) => path.Length >= 3 && path[0] == '/' && char.IsAsciiLetter(path[1]) && path[2] == ':';
    private static string Escape(string path) => string.Join('/', path.Split('/').Select(Uri.EscapeDataString));
    private void RequireHost()
    {
        if (_windows ? !OperatingSystem.IsWindows() : !(OperatingSystem.IsMacOS() || OperatingSystem.IsLinux()))
            throw new PlatformNotSupportedException("Path syntax requires the matching runtime host.");
    }
}
