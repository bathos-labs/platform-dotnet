namespace Bathos.Platform.Files;

/// <summary>Acquires cooperative, handle-owned exclusion at an explicit persistent lock-file location.</summary>
/// <remarks>
/// Coordinates instances of the same application on one device using this protocol and a stable location on a
/// filesystem supporting the native lock. This is not distributed locking or filesystem identity discovery.
/// Aliases, alternate path representations, other network clients, and noncooperating applications are outside
/// the guarantee. Hosts must keep the artifact and its containing directory tree intact at the same location for
/// the entire lease lifetime. Moving, renaming, deleting, or replacing either is unsupported on every platform.
/// If application data may move, choose a stable lock location outside that tree. Artifact bytes and existence
/// are never ownership authority.
/// </remarks>
public interface IFileLeaseProvider
{
    /// <include file="../Documentation/Contracts.xml" path="contracts/TryAcquireFileLease/*" />
    IFileLease? TryAcquire(string path, CancellationToken cancellationToken = default);
}

/// <summary>Owns one native file lock until disposal or process exit. The persistent artifact is not deleted.</summary>
/// <remarks>Disposal is idempotent. Keep the artifact and its containing directory tree intact at the same location
/// while leased. No stream or native handle is exposed; metadata is separate from ownership.</remarks>
public interface IFileLease : IDisposable, IAsyncDisposable;
