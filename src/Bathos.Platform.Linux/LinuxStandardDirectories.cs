using Bathos.Platform.Directories;

namespace Bathos.Platform.Linux;

/// <summary>Standard Linux directory discovery. Construction performs no lookup.</summary>
public sealed class LinuxStandardDirectories : IStandardDirectories
{
    /// <summary>Observes current Linux roots without creating directories. Invoke only on Linux.</summary>
    /// <remarks>Null locations are unavailable. Lookup errors can propagate; returned paths do not establish write access.
    /// Temporary roots are shared, and application-base resources are separate from writable storage.</remarks>
    public StandardDirectoryRoots Discover() => DirectoryDiscovery.Discover(PlatformOperatingSystem.Linux);
}
