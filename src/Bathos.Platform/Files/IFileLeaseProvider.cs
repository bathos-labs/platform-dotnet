namespace Bathos.Platform.Files;

/// <summary>Acquires cooperative, handle-owned exclusion at an explicit persistent lock-file location.</summary>
/// <remarks>
/// Coordinates instances of the same application on one device using this protocol and a stable location on a
/// filesystem supporting the native lock. This is not distributed locking or filesystem identity discovery.
/// Aliases, alternate path representations, other network clients, and noncooperating applications are outside
/// the guarantee. Hosts must not delete or replace the artifact while leased. The only supported relocation is one
/// same-filesystem rename of its containing directory tree to an unoccupied destination, retaining this lease
/// throughout. The host must stop using the old location and admit subsequent work at the new location;
/// the old pathname is not reserved. Copy/delete moves, artifact replacement, and independent relocation are unsupported. Artifact bytes and existence are never ownership authority.
/// </remarks>
public interface IFileLeaseProvider
{
    /// <include file="../Documentation/Contracts.xml" path="contracts/TryAcquireFileLease/*" />
    IFileLease? TryAcquire(string path, CancellationToken cancellationToken = default);
}

/// <summary>Owns one native file lock until disposal or process exit. The persistent artifact is not deleted.</summary>
/// <remarks>Disposal is idempotent. No stream or native handle is exposed; metadata is separate from ownership.</remarks>
public interface IFileLease : IDisposable, IAsyncDisposable;
