using Bathos.Platform.Desktop;
using Bathos.Platform.Files;

namespace Bathos.Platform.Windows;

/// <summary>Windows capabilities. A dispatcher enables screen sampling; construction does not invoke native APIs.</summary>
public sealed class WindowsPlatformProvider : IPlatformProvider
{
    /// <summary>Composes Windows capabilities without loading native services.</summary>
    /// <param name="dispatcher">Host dispatcher enabling the optional sampler; null disables sampling.</param>
    /// <remarks>File capabilities are constructed on any host but must be invoked only on Windows. Sampler availability also requires the matching runtime OS. The host owns the dispatcher and UI-loop lifetime.</remarks>
    public WindowsPlatformProvider(INativeDispatcher? dispatcher = null)
    {
        if (dispatcher is not null && System.OperatingSystem.IsWindows())
            ScreenColorSampler = new WindowsScreenColorSampler(dispatcher);
    }

    /// <summary>Gets the Windows provider identity, independently of the executing host.</summary>
    public PlatformOperatingSystem OperatingSystem => PlatformOperatingSystem.Windows;
    /// <summary>Gets Windows same-filesystem rename with explicit replacement policy.</summary>
    public INativeFileRename FileRename { get; } = new WindowsFileRename();
    /// <summary>Gets Windows regular-file inspection; the observation does not reserve the path.</summary>
    public IFileEntryProbe FileEntries { get; } = new WindowsFileEntryProbe();
    /// <summary>Gets the sampler when a dispatcher is supplied on a supported host; otherwise null. Invoke on that dispatcher’s native UI thread.</summary>
    public IScreenColorSampler? ScreenColorSampler { get; }
    /// <summary>Gets Windows file-manager selection. Service availability is checked at invocation.</summary>
    public IFileRevealer FileRevealer { get; } = new WindowsFileRevealer();
    /// <summary>Gets Windows explicit application handoff. Completion means native submission, not document readiness.</summary>
    public IFileInvoker FileInvoker { get; } = new WindowsFileInvoker();
}
