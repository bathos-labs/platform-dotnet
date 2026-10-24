using Bathos.Platform.Directories;

namespace Bathos.Platform.Windows;

/// <summary>Standard Windows directory discovery. Construction performs no lookup.</summary>
public sealed class WindowsStandardDirectories : IStandardDirectories
{
    /// <summary>Observes current Windows roots without creating directories. Invoke only on Windows.</summary>
    /// <remarks>Null locations are unavailable. Lookup errors can propagate; returned paths do not establish write access.
    /// Temporary roots are shared, and application-base resources are separate from writable storage.</remarks>
    public StandardDirectoryRoots Discover() => DirectoryDiscovery.Discover(PlatformOperatingSystem.Windows);
}
