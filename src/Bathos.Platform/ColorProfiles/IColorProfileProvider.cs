namespace Bathos.Platform.ColorProfiles;

/// <summary>Observes installed native color profiles and reads their bytes without interpreting or installing them.</summary>
/// <remarks>Inventory keys belong to the provider and must not be treated as portable paths. Observations do not reserve
/// profiles; a later read may fail or return changed bytes. No display accuracy, HDR, or device-default guarantee is implied.</remarks>
public interface IColorProfileProvider
{
    /// <include file="../Documentation/Contracts.xml" path="contracts/EnumerateColorProfiles/*" />
    ValueTask<ColorProfileInventory> EnumerateAsync(CancellationToken cancellationToken = default);
    /// <include file="../Documentation/Contracts.xml" path="contracts/ReadColorProfile/*" />
    ValueTask<ReadOnlyMemory<byte>> ReadAsync(string key, CancellationToken cancellationToken = default);
}

/// <summary>One provider-owned profile reference and observed device names; names are informational, not device identities.</summary>
/// <param name="Key">Opaque lookup key for this provider.</param>
/// <param name="Name">Informational profile filename/name.</param>
/// <param name="Display">Informational location/scope evidence.</param>
public sealed record ColorProfileLocation(string Key, string Name, string Display)
{
    /// <summary>Observed associated device names. Empty does not establish that no device uses the profile.</summary>
    public IReadOnlyList<string> DeviceNames { get; init; } = [];
}

/// <summary>Independently usable profile references and completeness of the attempted discovery.</summary>
/// <param name="Locations">Observed references, including those retained after partial discovery.</param>
/// <param name="IsComplete">Whether all attempted discovery completed within configured limits.</param>
/// <param name="Finding">Human-readable discovery coverage or failure details, not a machine-readable status.</param>
public sealed record ColorProfileInventory(IReadOnlyList<ColorProfileLocation> Locations, bool IsComplete, string Finding);

/// <summary>Resource bounds for the built-in providers; these do not validate ICC content.</summary>
public sealed class ColorProfileAccessLimits
{
    /// <summary>Creates positive profile-count and byte-read limits.</summary>
    /// <param name="maximumEntries">Maximum distinct inventory entries.</param>
    /// <param name="maximumBytes">Maximum bytes in one profile read.</param>
    public ColorProfileAccessLimits(int maximumEntries = 10_000, int maximumBytes = 16 * 1024 * 1024)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumEntries);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        MaximumEntries = maximumEntries; MaximumBytes = maximumBytes;
    }
    /// <summary>Maximum distinct inventory entries.</summary>
    public int MaximumEntries { get; }
    /// <summary>Maximum bytes in one profile read.</summary>
    public int MaximumBytes { get; }
}
