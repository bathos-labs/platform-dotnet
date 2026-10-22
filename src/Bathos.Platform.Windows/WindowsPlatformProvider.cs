using Bathos.Platform.Desktop;
using Bathos.Platform.Files;

namespace Bathos.Platform.Windows;

/// <summary>Windows capabilities. A dispatcher enables screen sampling; construction does not invoke native APIs.</summary>
public sealed class WindowsPlatformProvider : IPlatformProvider
{
    public WindowsPlatformProvider(INativeDispatcher? dispatcher = null)
    {
        if (dispatcher is not null && System.OperatingSystem.IsWindows())
            ScreenColorSampler = new WindowsScreenColorSampler(dispatcher);
    }

    public PlatformOperatingSystem OperatingSystem => PlatformOperatingSystem.Windows;
    public INativeFileRename FileRename { get; } = new WindowsFileRename();
    public IFileEntryProbe FileEntries { get; } = new WindowsFileEntryProbe();
    public IScreenColorSampler? ScreenColorSampler { get; }
    public IFileRevealer FileRevealer { get; } = new WindowsFileRevealer();
    public IFileInvoker FileInvoker { get; } = new WindowsFileInvoker();
}
