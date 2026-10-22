using System.Runtime.Versioning;
using Bathos.Platform.Desktop;
using Microsoft.Win32;

namespace Bathos.Platform.Windows;

/// <summary>Looks up executable registrations in App Paths. This is not a complete installed-application inventory.</summary>
[SupportedOSPlatform("windows")]
public static class WindowsApplicationDiscovery
{
    /// <summary>Returns existing registered executable paths across user/machine and registry views, without choosing a preferred application.</summary>
    public static IReadOnlyList<ApplicationTarget> FindByExecutableName(string executableName)
    {
        DesktopPaths.RequireText(executableName, nameof(executableName));
        if (executableName.IndexOfAny(['/', '\\', ':']) >= 0 || !executableName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("An executable filename, including .exe, is required.", nameof(executableName));
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var results = new List<ApplicationTarget>();
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var entry = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths\" + executableName);
                if (entry?.GetValue(null) is not string value) continue;
                // App Paths holds a path, not a command line. Some installers quote that single path.
                if (value.Length >= 2 && value[0] == '"' && value[^1] == '"') value = value[1..^1];
                if (value.Contains('\0', StringComparison.Ordinal) || !Path.IsPathFullyQualified(value) || !File.Exists(value) || !paths.Add(value)) continue;
                results.Add(ApplicationTarget.Executable(value));
            }
        return results;
    }
}
