using System.Globalization;
using System.Text;
using Bathos.Platform.Desktop;

namespace Bathos.Platform.Linux;

// Resolves resources only. Parsing/rasterization and the choice of desktop theme stay with the host.
internal static class LinuxIconTheme
{
    private static readonly string[] Extensions = [".png", ".svg", ".xpm"];
    internal static bool IsComponent(string value) => !string.IsNullOrWhiteSpace(value) && value is not ("." or "..") && value.IndexOfAny(['/', '\\', '\0']) < 0;

    internal static string? Find(IReadOnlyList<string> names, string theme, int size, string[] roots, CancellationToken token)
    {
        foreach (var name in names)
        {
            if (!IsComponent(name)) throw new IOException("The native icon name is not a filename component.");
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var found = InTheme(theme);
            if (found is not null) return found;
            if (!visited.Contains("hicolor") && InTheme("hicolor") is { } standard) return standard;
            foreach (var root in roots)
                if (Resource(root, name) is { } standalone) return standalone;

            string? InTheme(string current)
            {
                token.ThrowIfCancellationRequested();
                if (!visited.Add(current)) return null;
                if (visited.Count > 64) throw new IOException("Icon-theme inheritance exceeds the lookup limit.");
                if (!IsComponent(current)) throw new IOException("An inherited icon-theme name is not a directory component.");
                var index = roots.Select(root => Path.Combine(root, current, "index.theme")).FirstOrDefault(File.Exists);
                if (index is null) return null;
                var sections = ReadIndex(index);
                if (!sections.TryGetValue("Icon Theme", out var header)) return null;
                var directories = List(header.GetValueOrDefault("Directories"))
                    .Concat(List(header.GetValueOrDefault("ScaledDirectories"))).Distinct(StringComparer.Ordinal).ToArray();
                if (directories.Length > 4096) throw new IOException("The icon theme declares too many directories.");
                var candidates = new List<(string Directory, int Distance)>();
                foreach (var directory in directories)
                {
                    if (directory.Split('/').Any(part => !IsComponent(part))) throw new IOException("An icon-theme directory escapes its theme.");
                    if (!sections.TryGetValue(directory, out var attributes)) continue;
                    var nominal = Number(attributes, "Size", 0); var scale = Number(attributes, "Scale", 1);
                    if (nominal <= 0 || nominal > 1024 || scale <= 0 || scale > 16) continue;
                    var minimum = nominal; var maximum = nominal;
                    switch (attributes.GetValueOrDefault("Type", "Threshold"))
                    {
                        case "Scalable": minimum = Number(attributes, "MinSize", nominal); maximum = Number(attributes, "MaxSize", nominal); break;
                        case "Threshold":
                            var threshold = Number(attributes, "Threshold", 2);
                            if (threshold < 0 || threshold > 1024) continue;
                            minimum = Math.Max(0, nominal - threshold); maximum = nominal + threshold; break;
                        case "Fixed": break;
                        default: continue;
                    }
                    if (minimum < 0 || maximum < minimum || maximum > 2048) continue;
                    var distance = size < minimum * scale ? minimum * scale - size : Math.Max(0, size - maximum * scale);
                    candidates.Add((directory, distance));
                }
                foreach (var candidate in candidates.OrderBy(candidate => candidate.Distance))
                    foreach (var root in roots)
                    {
                        token.ThrowIfCancellationRequested();
                        if (Resource(Path.Combine(root, current, candidate.Directory), name) is { } resource) return resource;
                    }
                foreach (var inherited in List(header.GetValueOrDefault("Inherits")))
                    if (InTheme(inherited) is { } inheritedIcon) return inheritedIcon;
                return null;
            }
        }
        return null;
    }

    private static string? Resource(string directory, string name)
    {
        if (Extensions.Any(extension => name.EndsWith(extension, StringComparison.OrdinalIgnoreCase)))
            return File.Exists(Path.Combine(directory, name)) ? Path.Combine(directory, name) : null;
        return Extensions.Select(extension => Path.Combine(directory, name + extension)).FirstOrDefault(File.Exists);
    }

    internal static ApplicationIcon ReadIcon(string path)
    {
        var format = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => ApplicationIconFormat.Png,
            ".svg" => ApplicationIconFormat.Svg,
            ".xpm" => ApplicationIconFormat.Xpm,
            _ => throw new NotSupportedException("The application icon is not PNG, SVG, or XPM."),
        };
        return ApplicationIcon.FromEncoded(format, ReadBounded(path, ApplicationIcon.MaximumResourceBytes));
    }

    private static byte[] ReadBounded(string path, int maximum)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length <= 0 || stream.Length > maximum) throw new IOException("The icon resource exceeds its byte bounds.");
        var bytes = new byte[(int)stream.Length]; stream.ReadExactly(bytes); return bytes;
    }

    private static Dictionary<string, Dictionary<string, string>> ReadIndex(string path)
    {
        var text = new UTF8Encoding(false, true).GetString(ReadBounded(path, 1024 * 1024)).TrimStart('\uFEFF');
        var sections = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        Dictionary<string, string>? section = null;
        foreach (var line in text.Split('\n'))
        {
            var value = line.Trim();
            if (value.Length == 0 || value.StartsWith('#')) continue;
            if (value.StartsWith('[') && value.EndsWith(']')) { section = new(StringComparer.Ordinal); sections[value[1..^1]] = section; }
            else if (section is not null && value.IndexOf('=') is var separator && separator > 0) section[value[..separator].Trim()] = value[(separator + 1)..].Trim();
        }
        return sections;
    }
    private static string[] List(string? value) => value?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
    private static int Number(Dictionary<string, string> values, string key, int fallback) =>
        values.TryGetValue(key, out var value) && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : fallback;
}
