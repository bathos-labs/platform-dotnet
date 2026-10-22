using Bathos.Platform.Files;

namespace Bathos.Platform.Linux;

/// <summary>Linux statx file inspection. No exclusive rename or screen sampler is currently supplied.</summary>
public sealed class LinuxPlatformProvider : IPlatformProvider
{
    public PlatformOperatingSystem OperatingSystem => PlatformOperatingSystem.Linux;
    public INativeFileRename? FileRename => null;
    public IFileEntryProbe FileEntries { get; } = new LinuxFileEntryProbe();
    public IScreenColorSampler? ScreenColorSampler => null;
}
