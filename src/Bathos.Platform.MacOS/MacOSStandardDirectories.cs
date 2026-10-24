using Bathos.Platform.Directories;

namespace Bathos.Platform.MacOS;

/// <summary>Standard MacOS directory discovery. Construction performs no lookup.</summary>
public sealed class MacOSStandardDirectories : IStandardDirectories
{
    /// <summary>Observes current MacOS roots without creating directories. Invoke only on MacOS.</summary>
    /// <remarks>Null locations are unavailable. Lookup errors can propagate; returned paths do not establish write access.
    /// Temporary roots are shared, and application-base resources are separate from writable storage.</remarks>
    public StandardDirectoryRoots Discover() => DirectoryDiscovery.Discover(PlatformOperatingSystem.MacOS);
}
