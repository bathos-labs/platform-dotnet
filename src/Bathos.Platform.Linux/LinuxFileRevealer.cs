using Bathos.Platform.Desktop;

namespace Bathos.Platform.Linux;

/// <summary>Experimental selection through org.freedesktop.FileManager1.ShowItems, without a folder-only fallback.</summary>
public sealed class LinuxFileRevealer : IFileRevealer
{
    /// <include file="../Bathos.Platform/Documentation/Contracts.xml" path="contracts/RevealAsync/*" />
    public Task RevealAsync(string path, CancellationToken cancellationToken = default)
    {
        DesktopPaths.RequireEntry(path);
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Linux file reveal requires Linux.");
        return Task.Run(() =>
        {
            if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Linux file reveal requires Linux.");
            DesktopBus.Reveal(path, cancellationToken);
        }, cancellationToken);
    }
}
