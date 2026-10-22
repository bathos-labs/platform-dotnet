using System.Diagnostics;

namespace Bathos.Platform.Desktop;

internal static class DesktopPaths
{
    internal static void RequireText(string value, string parameter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameter);
        if (value.Contains('\0', StringComparison.Ordinal)) throw new ArgumentException("Embedded null characters are not allowed.", parameter);
    }

    internal static void RequireAbsolute(string path, string parameter)
    {
        RequireText(path, parameter);
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("An absolute filesystem path is required.", parameter);
    }

    internal static void RequireEntry(string path)
    {
        RequireAbsolute(path, nameof(path));
        _ = File.GetAttributes(path); // Preserve missing-entry and access errors instead of treating them as absence.
    }

    internal static void RequireFile(string path)
    {
        RequireAbsolute(path, nameof(path));
        if ((File.GetAttributes(path) & FileAttributes.Directory) != 0) throw new IOException("A file is required, not a directory.");
    }

    internal static void StartExecutable(string path, ApplicationTarget application, CancellationToken token)
    {
        RequireFile(application.Value);
        var start = new ProcessStartInfo(application.Value) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add(path);
        token.ThrowIfCancellationRequested();
        using var process = Process.Start(start) ?? throw new InvalidOperationException("The application could not be started.");
        // Releasing the process handle does not wait for or terminate the application.
    }
}
