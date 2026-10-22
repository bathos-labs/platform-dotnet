using Bathos.Platform.Files;

namespace Bathos.Platform.Windows;

/// <summary>Windows native file capabilities. Construction does not invoke native APIs.</summary>
public sealed class WindowsPlatformProvider : IPlatformProvider
{
    public PlatformOperatingSystem OperatingSystem => PlatformOperatingSystem.Windows;
    public INativeFileRename FileRename { get; } = new WindowsFileRename();
    public IFileEntryProbe FileEntries { get; } = new WindowsFileEntryProbe();
    public IScreenColorSampler? ScreenColorSampler => null;
}
