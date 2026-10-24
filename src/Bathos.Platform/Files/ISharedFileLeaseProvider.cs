namespace Bathos.Platform.Files;

/// <summary>Native compatibility mode for one stable lock artifact.</summary>
public enum FileLeaseMode
{
    /// <summary>Coexists with other shared leases, but excludes an exclusive lease.</summary>
    Shared,
    /// <summary>Excludes every other shared or exclusive lease.</summary>
    Exclusive,
}

/// <summary>Adds shared acquisition to the exclusive lease contract without requiring existing providers to implement it.</summary>
/// <remarks>Both modes use the same artifact and protocol. Modes cannot be changed on an acquired lease.
/// Shared means compatibility between owners; it does not authorize reading or writing application data.</remarks>
public interface ISharedFileLeaseProvider : IFileLeaseProvider
{
    /// <include file="../Documentation/Contracts.xml" path="contracts/TryAcquireSharedFileLease/*" />
    IFileLease? TryAcquire(string path, FileLeaseMode mode, CancellationToken cancellationToken = default);
}
