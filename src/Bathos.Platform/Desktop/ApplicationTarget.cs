namespace Bathos.Platform.Desktop;

/// <summary>The native representation of a host-selected application.</summary>
public enum ApplicationTargetKind
{
    /// <summary>An absolute executable path, invoked with one file argument.</summary>
    Executable,
    /// <summary>An absolute macOS .app bundle path, submitted through LaunchServices.</summary>
    MacOSBundle,
    /// <summary>A Linux desktop-entry ID, resolved and activated through GIO.</summary>
    LinuxDesktopEntry,
    /// <summary>A Windows application selected within a file-extension association context.</summary>
    WindowsAssociation
}

/// <summary>An explicit application location or registration ID, never a shell command or product name.</summary>
/// <remarks>Construction validates syntax only. Invocation checks native availability; successful submission does not prove support for the document format.</remarks>
public sealed record ApplicationTarget
{
    private ApplicationTarget(ApplicationTargetKind kind, string value) { Kind = kind; Value = value; }
    /// <summary>Gets the native target representation.</summary>
    public ApplicationTargetKind Kind { get; }
    /// <summary>Gets the validated path or desktop-entry ID, without shell quoting or command arguments.</summary>
    public string Value { get; }
    /// <summary>Gets the Windows extension context, or null for other target kinds. Empty means extensionless.</summary>
    public string? AssociationExtension { get; private init; }

    /// <summary>Creates a transient Windows application selection using its native name and extension context.</summary>
    /// <param name="nativeName">Nonblank Shell handler name, not a display label or command.</param>
    /// <param name="extension">A dot-prefixed extension, or empty for an extensionless file.</param>
    /// <returns>A descriptor for current registration lookup, not an immutable command or security identity.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException">The native name or extension has invalid syntax.</exception>
    public static ApplicationTarget WindowsAssociation(string nativeName, string extension)
    {
        DesktopPaths.RequireText(nativeName, nameof(nativeName));
        ArgumentNullException.ThrowIfNull(extension);
        if (extension.Length > 0 && (extension.Length == 1 || extension[0] != '.' || extension.IndexOfAny(['/', '\\', '\0']) >= 0))
            throw new ArgumentException("A file extension or empty context is required.", nameof(extension));
        return new(ApplicationTargetKind.WindowsAssociation, nativeName) { AssociationExtension = extension };
    }

    /// <summary>An absolute executable path in the current host's path syntax. The file path is passed as one argument.</summary>
    /// <param name="path">Absolute executable path in the executing host path syntax.</param>
    /// <returns>A syntax-validated target; no installation or registration lookup is performed.</returns>
    /// <exception cref="ArgumentNullException">The value is null.</exception>
    /// <exception cref="ArgumentException">The value is blank, contains a null character, or violates the target syntax.</exception>
    public static ApplicationTarget Executable(string path)
    {
        DesktopPaths.RequireAbsolute(path, nameof(path));
        return new(ApplicationTargetKind.Executable, path);
    }

    /// <summary>An absolute macOS application-bundle path.</summary>
    /// <param name="path">Absolute slash-prefixed path ending in .app, case-insensitively.</param>
    /// <returns>A syntax-validated target; no installation or registration lookup is performed.</returns>
    /// <exception cref="ArgumentNullException">The value is null.</exception>
    /// <exception cref="ArgumentException">The value is blank, contains a null character, or violates the target syntax.</exception>
    public static ApplicationTarget MacOSBundle(string path)
    {
        DesktopPaths.RequireText(path, nameof(path));
        if (!path.StartsWith('/') || !path.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("An absolute .app bundle path is required.", nameof(path));
        return new(ApplicationTargetKind.MacOSBundle, path);
    }

    /// <summary>A desktop-entry ID including its .desktop suffix, resolved through the native XDG search directories.</summary>
    /// <param name="id">Nonblank desktop-entry ID ending in .desktop, with no directory separators.</param>
    /// <returns>A syntax-validated target; no installation or registration lookup is performed.</returns>
    /// <exception cref="ArgumentNullException">The value is null.</exception>
    /// <exception cref="ArgumentException">The value is blank, contains a null character, or violates the target syntax.</exception>
    public static ApplicationTarget LinuxDesktopEntry(string id)
    {
        DesktopPaths.RequireText(id, nameof(id));
        if (id.Length <= ".desktop".Length || !id.EndsWith(".desktop", StringComparison.Ordinal) || id.IndexOfAny(['/', '\\']) >= 0)
            throw new ArgumentException("A desktop-entry ID, not a path, is required.", nameof(id));
        return new(ApplicationTargetKind.LinuxDesktopEntry, id);
    }
}
