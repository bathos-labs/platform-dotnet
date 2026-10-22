namespace Bathos.Platform.Files;

/// <summary>One native rename with explicit replacement policy. Never checks then copies/deletes or falls back across volumes.</summary>
public interface INativeFileRename
{
    /// <summary>Returns the native outcome and error code. Success is the publication boundary and cannot be undone by cancellation.</summary>
    /// <param name="source">Existing source path. Relative paths use the process working directory.</param>
    /// <param name="destination">Destination path on the same filesystem.</param>
    /// <param name="errorCode">Zero on success; otherwise the OS error (POSIX errno or Windows GetLastError).</param>
    /// <param name="replaceExisting">Whether replacement is explicitly authorized.</param>
    /// <returns>The native result. Unavailable represents an operation failure, not an absent capability.</returns>
    /// <exception cref="ArgumentException">A path is empty or contains a null character.</exception>
    /// <exception cref="ArgumentNullException">A path is null.</exception>
    FileRenameStatus Move(string source, string destination, out int errorCode, bool replaceExisting = false);
}

/// <summary>Outcome of one native rename attempt. Inspect the accompanying OS error for failure details.</summary>
public enum FileRenameStatus
{
    /// <summary>The rename completed; the source was published at the destination.</summary>
    Success,
    /// <summary>The destination is occupied and the native operation did not replace it.</summary>
    DestinationExists,
    /// <summary>The paths cross filesystem boundaries; no copy/delete fallback was attempted.</summary>
    CrossVolumeUnsupported,
    /// <summary>Another native failure occurred, such as access denied or a missing source. Consult the OS error.</summary>
    Unavailable
}
