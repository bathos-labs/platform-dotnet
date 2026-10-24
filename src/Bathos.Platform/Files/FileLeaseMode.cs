namespace Bathos.Platform.Files;

/// <summary>Native compatibility mode for one stable lock artifact.</summary>
public enum FileLeaseMode
{
    /// <summary>Excludes every other shared or exclusive lease. This is the default enum value.</summary>
    Exclusive = 0,
    /// <summary>Coexists with other shared leases, but excludes an exclusive lease.</summary>
    Shared = 1,
}
