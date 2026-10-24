using Bathos.Platform.ColorProfiles;
using Bathos.Platform.Desktop;
using Bathos.Platform.Directories;
using Bathos.Platform.Files;
using Bathos.Platform.Paths;

namespace Bathos.Platform.MacOS;

/// <summary>macOS capabilities. Construction does not invoke native APIs; a dispatcher enables interactive sampling.</summary>
public sealed class MacOSPlatformProvider : IPlatformProvider
{
    /// <summary>Composes macOS capabilities without loading native services.</summary>
    /// <param name="dispatcher">Host dispatcher enabling the optional sampler; null disables sampling.</param>
    /// <remarks>File capabilities are constructed on any host but must be invoked only on macOS. Sampler availability also requires the matching runtime OS, at least macOS 10.15. The host owns the dispatcher and UI-loop lifetime.</remarks>
    public MacOSPlatformProvider(INativeDispatcher? dispatcher = null)
    {
        if (dispatcher is not null && System.OperatingSystem.IsMacOSVersionAtLeast(10, 15))
            ScreenColorSampler = new MacScreenColorSampler(dispatcher);
    }
    /// <summary>Gets installed profile discovery and readback; invoke only on MacOS.</summary>
    public IColorProfileProvider ColorProfiles { get; } = new MacColorProfileProvider();
    /// <summary>Gets cooperative shared or exclusive file leases; invoke on the matching runtime with a stable lock location.</summary>
    public IFileLeaseProvider FileLeases { get; } = new MacOSFileLeaseProvider();
    /// <summary>Gets standard directory discovery; invoke on the matching runtime.</summary>
    public IStandardDirectories Directories { get; } = new MacOSStandardDirectories();
    /// <summary>Gets lexical path operations for the matching runtime; no filesystem identity is inferred.</summary>
    public INativeFilePaths FilePaths { get; } = NativeFilePaths.Posix;
    /// <summary>Gets the MacOS provider identity, independently of the executing host.</summary>
    public PlatformOperatingSystem OperatingSystem => PlatformOperatingSystem.MacOS;
    /// <summary>Gets macOS same-filesystem rename with explicit replacement policy.</summary>
    public INativeFileRename FileRename { get; } = new MacFileRename();
    /// <summary>Gets macOS regular-file inspection; the observation does not reserve the path.</summary>
    public IFileEntryProbe FileEntries { get; } = new MacFileEntryProbe();
    /// <summary>Gets the sampler when a dispatcher is supplied on a supported host; otherwise null. Invoke on that dispatcher’s native UI thread.</summary>
    public IScreenColorSampler? ScreenColorSampler { get; }
    /// <summary>Gets macOS file-manager selection. Service availability is checked at invocation.</summary>
    public IFileRevealer FileRevealer { get; } = new MacOSFileRevealer();
    /// <summary>Gets macOS explicit application handoff. Completion means native submission, not document readiness.</summary>
    public IFileInvoker FileInvoker { get; } = new MacOSFileInvoker();
}
