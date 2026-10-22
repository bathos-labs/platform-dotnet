using Bathos.Platform.Files;

namespace Bathos.Platform.MacOS;

/// <summary>macOS capabilities. Construction does not invoke native APIs; a dispatcher enables interactive sampling.</summary>
public sealed class MacOSPlatformProvider : IPlatformProvider
{
    public MacOSPlatformProvider(INativeDispatcher? dispatcher = null)
    {
        if (dispatcher is not null && System.OperatingSystem.IsMacOSVersionAtLeast(10, 15))
            ScreenColorSampler = new MacScreenColorSampler(dispatcher);
    }
    public PlatformOperatingSystem OperatingSystem => PlatformOperatingSystem.MacOS;
    public INativeFileRename FileRename { get; } = new MacFileRename();
    public IFileEntryProbe FileEntries { get; } = new MacFileEntryProbe();
    public IScreenColorSampler? ScreenColorSampler { get; }
}
