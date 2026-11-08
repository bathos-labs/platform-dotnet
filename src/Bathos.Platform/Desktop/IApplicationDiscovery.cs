namespace Bathos.Platform.Desktop;

/// <summary>Discovers advisory application choices for an existing local regular file without launching applications.</summary>
/// <remarks>
/// Results describe current registrations, not immutable commands, durable preference keys, or security identities.
/// Implementations may coalesce entries by native application identity or full executable path, never display label alone.
/// General-purpose handlers may be returned for unknown types; ordering implies neither suitability nor default selection.
/// </remarks>
public interface IApplicationDiscovery
{
    /// <summary>Finds useful registered application choices for one existing local regular file.</summary>
    /// <param name="absolutePath">Absolute path to the actual file intended for handoff; directories and remote URLs are not supported.</param>
    /// <param name="cancellationToken">Cancellation request; completion follows cleanup of all owned native work.</param>
    /// <returns>An owned read-only snapshot of immutable candidates; empty when native discovery returns no choices after coalescing.</returns>
    /// <remarks>
    /// Targets compose with the matching invocation and icon providers. Invocation uses the application's current registration;
    /// it does not reserve the command observed during discovery. Missing or unresolved selections fail without default substitution.
    /// Labels, icons, and activation are separate observations. No completeness, format support, or successful parsing is guaranteed.
    /// Native enumeration failures must not become successful partial results. Hosts own refresh, caching, custom entries, and trust.
    /// </remarks>
    /// <exception cref="ArgumentNullException">The path is null.</exception>
    /// <exception cref="ArgumentException">The path is blank, contains a null character, or is not absolute.</exception>
    /// <exception cref="IOException">The file is missing or not regular, or native discovery fails.</exception>
    /// <exception cref="UnauthorizedAccessException">Access to the file or native registration data is denied.</exception>
    /// <exception cref="PlatformNotSupportedException">The implementation is unavailable on the executing host.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested before completion.</exception>
    Task<IReadOnlyList<ApplicationCandidate>> FindForFileAsync(string absolutePath, CancellationToken cancellationToken = default);
}
