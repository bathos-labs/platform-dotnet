namespace Bathos.Platform.Desktop;

/// <summary>The native representation of a host-selected application.</summary>
public enum ApplicationTargetKind { Executable, MacOSBundle, LinuxDesktopEntry }

/// <summary>An explicit application location or registration ID, never a shell command or product name.</summary>
/// <remarks>Construction validates syntax only. Installation and file-opening support are checked at invocation.</remarks>
public sealed record ApplicationTarget
{
    private ApplicationTarget(ApplicationTargetKind kind, string value) { Kind = kind; Value = value; }
    public ApplicationTargetKind Kind { get; }
    public string Value { get; }

    /// <summary>An absolute executable path in the current host's path syntax. The file path is passed as one argument.</summary>
    public static ApplicationTarget Executable(string path)
    {
        DesktopPaths.RequireAbsolute(path, nameof(path));
        return new(ApplicationTargetKind.Executable, path);
    }

    /// <summary>An absolute macOS application-bundle path.</summary>
    public static ApplicationTarget MacOSBundle(string path)
    {
        DesktopPaths.RequireText(path, nameof(path));
        if (!path.StartsWith('/') || !path.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("An absolute .app bundle path is required.", nameof(path));
        return new(ApplicationTargetKind.MacOSBundle, path);
    }

    /// <summary>A desktop-entry ID including its .desktop suffix, resolved through the native XDG search directories.</summary>
    public static ApplicationTarget LinuxDesktopEntry(string id)
    {
        DesktopPaths.RequireText(id, nameof(id));
        if (id.Length <= ".desktop".Length || !id.EndsWith(".desktop", StringComparison.Ordinal) || id.IndexOfAny(['/', '\\']) >= 0)
            throw new ArgumentException("A desktop-entry ID, not a path, is required.", nameof(id));
        return new(ApplicationTargetKind.LinuxDesktopEntry, id);
    }
}
