using Bathos.Platform.ColorProfiles;
using Bathos.Platform.Desktop;
using Bathos.Platform.Files;
using Bathos.Platform.Paths;

namespace Bathos.Platform;

/// <summary>Native host identity. Linux is a specific provider target, not a promise of generic POSIX compatibility.</summary>
public enum PlatformOperatingSystem
{
    /// <summary>The runtime OS is not recognized by the built-in host probe.</summary>
    Unknown,
    /// <summary>Apple macOS.</summary>
    MacOS,
    /// <summary>Microsoft Windows.</summary>
    Windows,
    /// <summary>Linux; this does not imply support for other POSIX systems.</summary>
    Linux
}

/// <summary>Identifies the current OS without loading providers or invoking their native APIs.</summary>
public static class PlatformHost
{
    /// <summary>Gets the current runtime OS, or Unknown for an unrecognized host.</summary>
    public static PlatformOperatingSystem Current => OperatingSystem.IsMacOS() ? PlatformOperatingSystem.MacOS
        : OperatingSystem.IsWindows() ? PlatformOperatingSystem.Windows
        : OperatingSystem.IsLinux() ? PlatformOperatingSystem.Linux : PlatformOperatingSystem.Unknown;
}

/// <summary>
/// A composed native capability set. Null means unsupported or not configured, not user cancellation or operation failure.
/// Hosts may register individual capabilities in DI or retain this facade in their own application wrapper.
/// </summary>
/// <remarks>
/// Use the built-in OS providers for standard integration. This facade groups capabilities at the composition boundary;
/// application components should depend on focused capability interfaces, which support substitution and testing.
/// Every capability defaults to null and must be checked before invocation. Partial providers can supply an OS identity
/// and a subset of capabilities. Access default members through IPlatformProvider; implementing classes do not inherit
/// those members. New capabilities can add optional default-null properties.
/// </remarks>
public interface IPlatformProvider
{
    /// <summary>Gets the OS represented by this provider, independently of the executing host.</summary>
    PlatformOperatingSystem OperatingSystem { get; }
    /// <summary>Gets native rename support, or null when unavailable. Native failures are rename outcomes; argument validation and native loading can throw.</summary>
    INativeFileRename? FileRename => null;
    /// <summary>Gets host-native lexical paths, or null when unavailable. This does not establish filesystem identity or lease ownership.</summary>
    INativeFilePaths? FilePaths => null;
    /// <summary>Gets regular-file inspection, or null when unavailable. Inspection is advisory, not a path reservation.</summary>
    IFileEntryProbe? FileEntries => null;
    /// <summary>Gets interactive color selection, or null when unsupported or not configured. A null sample instead means user cancellation.</summary>
    IScreenColorSampler? ScreenColorSampler => null;
    /// <summary>Gets native file-manager selection, or null when unsupported. Availability does not establish that a file manager is running.</summary>
    IFileRevealer? FileRevealer => null;
    /// <summary>Gets explicit application handoff, or null when unsupported. Success means submission, not document readiness.</summary>
    IFileInvoker? FileInvoker => null;
    /// <summary>Gets installed color-profile discovery/readback, or null when unsupported. It does not provide ICC interpretation.</summary>
    IColorProfileProvider? ColorProfiles => null;
}

/// <summary>Selects only explicitly supplied providers; the core never references or dynamically scans implementation assemblies.</summary>
public static class PlatformProviders
{
    /// <summary>Selects the single supplied provider matching the current runtime OS.</summary>
    /// <param name="providers">Explicit provider instances; neither the array nor its entries may be null.</param>
    /// <returns>The original matching instance; native permissions and services are not probed.</returns>
    /// <exception cref="ArgumentNullException">The array or an entry is null.</exception>
    /// <exception cref="InvalidOperationException">Multiple providers match the requested OS.</exception>
    /// <exception cref="PlatformNotSupportedException">No provider matches, including an unknown OS.</exception>
    public static IPlatformProvider SelectCurrent(params IPlatformProvider[] providers) => Select(PlatformHost.Current, providers);
    /// <summary>Selects the single supplied provider matching the requested OS.</summary>
    /// <param name="host">OS to match. Unknown never selects a provider.</param>
    /// <param name="providers">Explicit provider instances; neither the array nor its entries may be null.</param>
    /// <returns>The original matching instance; native permissions and services are not probed.</returns>
    /// <exception cref="ArgumentNullException">The array or an entry is null.</exception>
    /// <exception cref="InvalidOperationException">Multiple providers match the requested OS.</exception>
    /// <exception cref="PlatformNotSupportedException">No provider matches, including an unknown OS.</exception>
    public static IPlatformProvider Select(PlatformOperatingSystem host, params IPlatformProvider[] providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        IPlatformProvider? selected = null;
        foreach (var provider in providers)
        {
            ArgumentNullException.ThrowIfNull(provider);
            if (host == PlatformOperatingSystem.Unknown || provider.OperatingSystem != host) continue;
            if (selected is not null) throw new InvalidOperationException($"More than one provider was supplied for {host}.");
            selected = provider;
        }
        return selected ?? throw new PlatformNotSupportedException($"No native provider was supplied for {host}.");
    }
}
