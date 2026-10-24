using Bathos.Platform.ColorProfiles;

namespace Bathos.Platform.Windows;

/// <summary>Installed user/system ICC/WCS profiles; device defaults and HDR profiles are not evaluated.</summary>
public sealed class WindowsColorProfileProvider : IColorProfileProvider
{
    private readonly ColorProfileAccessLimits _limits;
    /// <summary>Creates an inert provider; native operations run only when called on the matching OS.</summary>
    /// <param name="limits">Optional resource bounds; null uses the documented default limits.</param>
    public WindowsColorProfileProvider(ColorProfileAccessLimits? limits = null) => _limits = limits ?? new();

    /// <include file="../Bathos.Platform/Documentation/Contracts.xml" path="contracts/EnumerateColorProfiles/*" />
    public ValueTask<ColorProfileInventory> EnumerateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequireHost();
        return new(Task.Run(() => WindowsColorProfiles.Enumerate(_limits, cancellationToken), cancellationToken));
    }

    /// <include file="../Bathos.Platform/Documentation/Contracts.xml" path="contracts/ReadColorProfile/*" />
    public ValueTask<ReadOnlyMemory<byte>> ReadAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (key.Contains('\0')) throw new ArgumentException("Profile keys cannot contain NUL.", nameof(key));
        cancellationToken.ThrowIfCancellationRequested();
        RequireHost();
        return new(Task.Run(() => WindowsColorProfiles.Read(key, _limits, cancellationToken), cancellationToken));
    }

    private static void RequireHost()
    {
        if (!System.OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("This profile provider requires Windows.");
    }
}
