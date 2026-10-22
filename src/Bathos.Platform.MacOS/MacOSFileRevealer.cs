using Bathos.Platform.Desktop;

namespace Bathos.Platform.MacOS;

/// <summary>Requests Finder selection through the system open utility.</summary>
public sealed class MacOSFileRevealer : IFileRevealer
{
    public Task RevealAsync(string path, CancellationToken cancellationToken = default)
    {
        DesktopPaths.RequireEntry(path);
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("macOS file reveal requires macOS.");
        return MacOpen.RunAsync(["-R", path], cancellationToken);
    }
}
