using Bathos.Platform.Desktop;
using Bathos.Platform.Directories;
using Bathos.Platform.Files;
using Bathos.Platform.Paths;

namespace Bathos.Platform.Linux;

/// <summary>Experimental Linux file inspection and optional desktop-portal color sampling. No exclusive rename is supplied.</summary>
public sealed class LinuxPlatformProvider : IPlatformProvider
{
    /// <summary>Composes experimental Linux capabilities without probing desktop services or loading native libraries.</summary>
    /// <param name="enableScreenColorSampler">Enables the experimental sampler without probing the session bus or desktop.</param>
    /// <param name="parentWindow">Portal window identifier (x11: or wayland:), or empty for an unparented picker.</param>
    /// <exception cref="ArgumentNullException">The parent window identifier is null.</exception>
    /// <exception cref="ArgumentException">The identifier contains a null character or lacks a supported prefix.</exception>
    /// <remarks>File capabilities must be invoked on Linux. Sampling also requires a Linux runtime and opt-in; opt-in does not establish portal availability. The caller supplies a valid exported window identifier when parenting is needed.</remarks>
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

    /// <summary>Gets cooperative shared or exclusive file leases; invoke on the matching runtime with a stable lock location.</summary>
    public IFileLeaseProvider FileLeases { get; } = new LinuxFileLeaseProvider();
    /// <summary>Gets standard directory discovery; invoke on the matching runtime.</summary>
    public IStandardDirectories Directories { get; } = new LinuxStandardDirectories();
    /// <summary>Gets lexical path operations for the matching runtime; no filesystem identity is inferred.</summary>
    public INativeFilePaths FilePaths { get; } = NativeFilePaths.Posix;
    /// <summary>Gets the Linux provider identity, independently of the executing host.</summary>
    public PlatformOperatingSystem OperatingSystem => PlatformOperatingSystem.Linux;
    /// <summary>Always null: Linux native rename and staged publication are not implemented.</summary>
    public INativeFileRename? FileRename => null;
    /// <summary>Gets Linux regular-file inspection; the observation does not reserve the path.</summary>
    public IFileEntryProbe FileEntries { get; } = new LinuxFileEntryProbe();
    /// <summary>Gets the experimental portal sampler when enabled on Linux; otherwise null. No dispatcher is required.</summary>
    public IScreenColorSampler? ScreenColorSampler { get; }
    /// <summary>Gets Linux file-manager selection. Service availability is checked at invocation.</summary>
    public IFileRevealer FileRevealer { get; } = new LinuxFileRevealer();
    /// <summary>Gets Linux explicit application handoff. Completion means native submission, not document readiness.</summary>
    public IFileInvoker FileInvoker { get; } = new LinuxFileInvoker();
}
