using Bathos.Platform.Desktop;
using Bathos.Platform.Files;

namespace Bathos.Platform.Linux;

/// <summary>Experimental Linux file inspection and optional desktop-portal color sampling. No exclusive rename is supplied.</summary>
public sealed class LinuxPlatformProvider : IPlatformProvider
{
    /// <param name="enableScreenColorSampler">Enables the experimental sampler without probing the session bus or desktop.</param>
    /// <param name="parentWindow">Portal window identifier (x11: or wayland:), or empty for an unparented picker.</param>
    public LinuxPlatformProvider(bool enableScreenColorSampler = false, string parentWindow = "")
    {
        ArgumentNullException.ThrowIfNull(parentWindow);
        if (parentWindow.Contains('\0', StringComparison.Ordinal) ||
            (parentWindow.Length != 0 && !parentWindow.StartsWith("x11:", StringComparison.Ordinal) && !parentWindow.StartsWith("wayland:", StringComparison.Ordinal)))
            throw new ArgumentException("Use an empty parent or a portal x11:/wayland: window identifier.", nameof(parentWindow));
        if (enableScreenColorSampler && System.OperatingSystem.IsLinux())
            ScreenColorSampler = new LinuxScreenColorSampler(() => System.OperatingSystem.IsLinux()
                ? DesktopBus.PickColor(parentWindow) : throw new PlatformNotSupportedException("A Linux desktop portal is required."));
    }

    public PlatformOperatingSystem OperatingSystem => PlatformOperatingSystem.Linux;
    public INativeFileRename? FileRename => null;
    public IFileEntryProbe FileEntries { get; } = new LinuxFileEntryProbe();
    public IScreenColorSampler? ScreenColorSampler { get; }
    public IFileRevealer FileRevealer { get; } = new LinuxFileRevealer();
    public IFileInvoker FileInvoker { get; } = new LinuxFileInvoker();
}
