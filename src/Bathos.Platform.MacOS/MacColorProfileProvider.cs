using Bathos.Platform.ColorProfiles;

namespace Bathos.Platform.MacOS;

/// <summary>ColorSync installed profiles and observed current device associations.</summary>
public sealed class MacColorProfileProvider : IColorProfileProvider
{
    private readonly ColorProfileAccessLimits _limits;
    /// <summary>Creates an inert provider; native operations run only when called on the matching OS.</summary>
    /// <param name="limits">Optional resource bounds; null uses the documented default limits.</param>
    public MacColorProfileProvider(ColorProfileAccessLimits? limits = null) => _limits = limits ?? new();

    /// <include file="../Bathos.Platform/Documentation/Contracts.xml" path="contracts/EnumerateColorProfiles/*" />
    public ValueTask<ColorProfileInventory> EnumerateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequireHost();
        return new(Task.Run(() => MacColorProfiles.Enumerate(_limits, cancellationToken), cancellationToken));
    }

    /// <include file="../Bathos.Platform/Documentation/Contracts.xml" path="contracts/ReadColorProfile/*" />
    public ValueTask<ReadOnlyMemory<byte>> ReadAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (key.Contains('\0')) throw new ArgumentException("Profile keys cannot contain NUL.", nameof(key));
        cancellationToken.ThrowIfCancellationRequested();
        RequireHost();
        return new(ReadFileAsync(key, cancellationToken));
    }

    private static void RequireHost()
    {
        if (!System.OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("This profile provider requires MacOS.");
    }

    private async Task<ReadOnlyMemory<byte>> ReadFileAsync(string key, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(key, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
            8192, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length > _limits.MaximumBytes) throw new InvalidDataException("The profile exceeds the configured read limit.");
        var bytes = new byte[checked((int)stream.Length)];
        await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
        var extra = new byte[1];
        if (await stream.ReadAsync(extra, cancellationToken).ConfigureAwait(false) != 0)
            throw new IOException("The profile changed while being read.");
        return bytes;
    }
}
