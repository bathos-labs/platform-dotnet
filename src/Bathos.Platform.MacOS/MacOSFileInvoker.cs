using System.Diagnostics;
using Bathos.Platform.Desktop;

namespace Bathos.Platform.MacOS;

/// <summary>Uses LaunchServices through the system open utility, or starts an explicitly selected executable.</summary>
public sealed class MacOSFileInvoker : IFileInvoker
{
    public Task OpenAsync(string path, ApplicationTarget application, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(application);
        DesktopPaths.RequireFile(path);
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("macOS file invocation requires macOS.");
        return application.Kind switch
        {
            ApplicationTargetKind.Executable => Task.Run(() => DesktopPaths.StartExecutable(path, application, cancellationToken), cancellationToken),
            ApplicationTargetKind.MacOSBundle => OpenBundleAsync(path, application.Value, cancellationToken),
            _ => throw new NotSupportedException("macOS requires an application bundle or executable target.")
        };
    }

    private static Task OpenBundleAsync(string path, string bundle, CancellationToken token)
    {
        DesktopPaths.RequireAbsolute(bundle, nameof(bundle));
        if ((File.GetAttributes(bundle) & FileAttributes.Directory) == 0) throw new IOException("An application bundle directory is required.");
        return MacOpen.RunAsync(["-a", bundle, path], token);
    }
}

internal static class MacOpen
{
    internal static async Task RunAsync(string[] arguments, CancellationToken token)
    {
        var start = new ProcessStartInfo("/usr/bin/open") { UseShellExecute = false, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        token.ThrowIfCancellationRequested();
        using var process = Process.Start(start) ?? throw new IOException("The native open helper could not be started.");
        var errors = process.StandardError.ReadToEndAsync(CancellationToken.None);
        // Once the helper starts it may already have handed off the file. Cancellation must not misreport that as failure.
        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        var detail = await errors.ConfigureAwait(false);
        if (process.ExitCode != 0) throw new IOException($"The native open helper failed ({process.ExitCode}): {detail.Trim()}");
    }
}
