namespace Bathos.Platform.Desktop;

/// <summary>An immutable application choice with a transient native target and a display label.</summary>
/// <remarks>Labels are not identities or commands. Equality compares values and does not establish installation or command identity.</remarks>
public sealed record ApplicationCandidate
{
    /// <summary>Creates a choice without performing native lookup or retrieving an icon.</summary>
    /// <param name="target">Target accepted by the matching invocation and icon providers.</param>
    /// <param name="displayName">Nonblank display label without null characters; preserved without trimming.</param>
    /// <exception cref="ArgumentNullException">A required value is null.</exception>
    /// <exception cref="ArgumentException">The display name is blank or contains a null character.</exception>
    public ApplicationCandidate(ApplicationTarget target, string displayName)
    {
        ArgumentNullException.ThrowIfNull(target);
        DesktopPaths.RequireText(displayName, nameof(displayName));
        Target = target;
        DisplayName = displayName;
    }

    /// <summary>Gets the transient target for the application's current native registration.</summary>
    public ApplicationTarget Target { get; }
    /// <summary>Gets the display label; never use it to resolve or invoke an application.</summary>
    public string DisplayName { get; }
}
