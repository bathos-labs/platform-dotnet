using System.ComponentModel;
using System.Diagnostics;
using Bathos.Platform.Desktop;

namespace Bathos.Platform.Windows;

/// <summary>Hands a regular file to the Windows Shell's registered open application.</summary>
public sealed class WindowsDefaultFileOpener : IDefaultFileOpener
{
    /// <include file="../Bathos.Platform/Documentation/Contracts.xml" path="contracts/OpenDefaultFileAsync/*" />
    public Task OpenAsync(string path, CancellationToken cancellationToken = default)
    {
        DesktopPaths.RequireAbsolute(path, nameof(path));
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows default-file opening requires Windows.");
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            new WindowsFileEntryProbe().RequireRegularFile(path);
            var start = new ProcessStartInfo(path) { UseShellExecute = true, Verb = "open", ErrorDialog = false };
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                // The runtime joins its Shell STA worker with SEE_MASK_NOASYNC and owns any process handle.
                // A null process is successful when an existing application handles the document.
                using var process = Process.Start(start);
            }
            catch (Win32Exception error) { throw new IOException("The Windows Shell could not open the file with its registered application.", error); }
        }, cancellationToken);
    }
}
