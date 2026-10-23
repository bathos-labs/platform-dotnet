namespace Bathos.Platform.Files;

/// <summary>One native rename with explicit replacement policy. Never checks then copies/deletes or falls back across volumes.</summary>
public interface INativeFileRename
{
    /// <include file="../Documentation/Contracts.xml" path="contracts/Move/*" />
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
