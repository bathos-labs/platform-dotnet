using Bathos.Platform.Desktop;

namespace Bathos.Platform.MacOS;

/// <summary>Submits a regular file to LaunchServices through the system open utility, without selecting an application.</summary>
public sealed class MacOSDefaultFileOpener : IDefaultFileOpener
{
    /// <include file="../Bathos.Platform/Documentation/Contracts.xml" path="contracts/OpenDefaultFileAsync/*" />
    public Task OpenAsync(string path, CancellationToken cancellationToken = default)
    {
        DesktopPaths.RequireAbsolute(path, nameof(path));
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("macOS default-file opening requires macOS.");
        return Task.Run(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            new MacFileEntryProbe().RequireRegularFile(path);
            await MacOpen.RunAsync([path], cancellationToken).ConfigureAwait(false);
        }, cancellationToken);
    }
}
