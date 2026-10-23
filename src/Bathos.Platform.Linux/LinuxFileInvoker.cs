using Bathos.Platform.Desktop;

namespace Bathos.Platform.Linux;

/// <summary>Uses GIO desktop-entry activation or a host-selected executable. Linux desktop support is experimental.</summary>
public sealed class LinuxFileInvoker : IFileInvoker
{
    /// <include file="../Bathos.Platform/Documentation/Contracts.xml" path="contracts/OpenAsync/*" />
    public Task OpenAsync(string path, ApplicationTarget application, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(application);
        DesktopPaths.RequireFile(path);
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Linux file invocation requires Linux.");
        if (application.Kind is not (ApplicationTargetKind.Executable or ApplicationTargetKind.LinuxDesktopEntry))
            throw new NotSupportedException("Linux requires an executable or desktop-entry target.");
        return Task.Run(() =>
        {
            if (application.Kind == ApplicationTargetKind.Executable) DesktopPaths.StartExecutable(path, application, cancellationToken);
            else LinuxApplicationDiscovery.Open(path, application.Value, cancellationToken);
        }, cancellationToken);
    }
}
