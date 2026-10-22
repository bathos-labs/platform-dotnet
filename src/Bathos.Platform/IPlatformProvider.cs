using Bathos.Platform.Files;

namespace Bathos.Platform;

/// <summary>Native host identity. Linux is a specific provider target, not a promise of generic POSIX compatibility.</summary>
public enum PlatformOperatingSystem { Unknown, MacOS, Windows, Linux }

/// <summary>Identifies the current OS without loading providers or invoking their native APIs.</summary>
public static class PlatformHost
{
    public static PlatformOperatingSystem Current => OperatingSystem.IsMacOS() ? PlatformOperatingSystem.MacOS
        : OperatingSystem.IsWindows() ? PlatformOperatingSystem.Windows
        : OperatingSystem.IsLinux() ? PlatformOperatingSystem.Linux : PlatformOperatingSystem.Unknown;
}

/// <summary>
/// A composed native capability set. Null means unsupported or not configured, not user cancellation or operation failure.
/// Hosts may register individual capabilities in DI or retain this facade in their own application wrapper.
/// </summary>
public interface IPlatformProvider
{
    PlatformOperatingSystem OperatingSystem { get; }
    INativeFileRename? FileRename { get; }
    IFileEntryProbe? FileEntries { get; }
    IScreenColorSampler? ScreenColorSampler { get; }
}

/// <summary>Selects only explicitly supplied providers; the core never references or dynamically scans implementation assemblies.</summary>
public static class PlatformProviders
{
    public static IPlatformProvider SelectCurrent(params IPlatformProvider[] providers) => Select(PlatformHost.Current, providers);
    public static IPlatformProvider Select(PlatformOperatingSystem host, params IPlatformProvider[] providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        IPlatformProvider? selected = null;
        foreach (var provider in providers)
        {
            ArgumentNullException.ThrowIfNull(provider);
            if (host == PlatformOperatingSystem.Unknown || provider.OperatingSystem != host) continue;
            if (selected is not null) throw new InvalidOperationException($"More than one provider was supplied for {host}.");
            selected = provider;
        }
        return selected ?? throw new PlatformNotSupportedException($"No native provider was supplied for {host}.");
    }
}
