using System.Text;

namespace Bathos.Platform.Paths;

/// <summary>Explicit Bathos portability policy for a single new filename, independent of native path admission.</summary>
/// <remarks>Validation never edits existing names. The policy excludes Windows reserved characters/device stems,
/// controls, trailing dots/spaces, malformed Unicode, and names exceeding 255 UTF-8 bytes or UTF-16 code units.
/// It does not promise support on every filesystem or uniqueness under Unicode/case equivalence.
/// Use Files.GeneratedFileName for opt-in generated-name sanitization and publication suffixes.</remarks>
public static class PortableFileNames
{
    /// <summary>Returns whether a complete filename meets the explicit portable policy; null and empty are invalid.</summary>
    public static bool IsValid(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 255 || name.EndsWith(' ') || name.EndsWith('.') ||
            name.Any(c => char.IsControl(c) || "<>:\"/\\|?*".Contains(c)) || IsDeviceName(name)) return false;
        try { return new UTF8Encoding(false, true).GetByteCount(name) <= 255; }
        catch (EncoderFallbackException) { return false; }
    }

    internal static bool IsDeviceName(string name)
    {
        var stem = name.Split('.')[0].TrimEnd().ToUpperInvariant();
        return stem is "CON" or "PRN" or "AUX" or "NUL" ||
            (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) &&
             stem[3] is >= '1' and <= '9' or '¹' or '²' or '³');
    }
}
