using Bathos.Platform.Desktop;

namespace Bathos.Platform.Windows;

/// <summary>Opens a file by passing its absolute path as one argument to a host-selected executable.</summary>
public sealed class WindowsFileInvoker : IFileInvoker
{
    /// <include file="../Bathos.Platform/Documentation/Contracts.xml" path="contracts/OpenAsync/*" />
    public Task OpenAsync(string path, ApplicationTarget application, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(application);
        DesktopPaths.RequireFile(path);
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows file invocation requires Windows.");
        if (application.Kind != ApplicationTargetKind.Executable) throw new NotSupportedException("Windows requires an executable application target.");
        return Task.Run(() => DesktopPaths.StartExecutable(path, application, cancellationToken), cancellationToken);
    }
}
