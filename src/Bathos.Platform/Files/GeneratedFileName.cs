using System.Globalization;
using System.Text;

namespace Bathos.Platform.Files;

/// <summary>
/// Portable generated filename components, independent of application naming policy.
/// The caller supplies a base name without an extension and an optional origin/kind.
/// Publication assigns the numeric suffix; it is not an identity or a reserved sequence.
/// </summary>
public sealed class GeneratedFileName
{
    public GeneratedFileName(string baseName, string extension, string? kind = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseName);
        ArgumentException.ThrowIfNullOrWhiteSpace(extension);
        if (extension.Length is < 2 or > 17 || extension[0] != '.' || extension.Skip(1).Any(character => !char.IsAsciiLetterOrDigit(character)))
            throw new ArgumentException("Use a dot followed by 1–16 ASCII letters or digits for the extension.", nameof(extension));
        var primary = Normalize(baseName, 96);
        var secondary = string.IsNullOrWhiteSpace(kind) ? string.Empty : Normalize(kind, 48);
        Stem = secondary.Length == 0 ? primary : primary + " - " + secondary;
        // Windows reserves device names even when an extension follows them.
        var device = Stem.Split('.')[0].TrimEnd().ToUpperInvariant();
        if (device is "CON" or "PRN" or "AUX" or "NUL" ||
            (device.Length == 4 && (device.StartsWith("COM", StringComparison.Ordinal) || device.StartsWith("LPT", StringComparison.Ordinal)) &&
             device[3] is >= '1' and <= '9' or '¹' or '²' or '³')) Stem = "_" + Stem;
        Extension = extension;
    }

    private string Stem { get; }
    private string Extension { get; }

    public string Candidate(int sequence)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sequence, 1);
        return Stem + (sequence == 1 ? string.Empty : " (" + sequence.ToString(CultureInfo.InvariantCulture) + ")") + Extension;
    }

    private static string Normalize(string value, int maximumBytes)
    {
        var text = new StringBuilder();
        var bytes = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            var allowed = Rune.IsLetterOrDigit(rune) || Rune.GetUnicodeCategory(rune) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark ||
                rune.Value is ' ' or '-' or '_' or '.' or '(' or ')';
            var part = allowed ? rune.ToString() : "-";
            var length = Encoding.UTF8.GetByteCount(part);
            if (bytes + length > maximumBytes) break;
            text.Append(part);
            bytes += length;
        }
        var result = text.ToString().Trim(' ', '.', '-');
        return result.Length == 0 ? "File" : result;
    }
}
