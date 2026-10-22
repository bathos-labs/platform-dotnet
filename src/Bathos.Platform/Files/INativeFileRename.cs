namespace Bathos.Platform.Files;

/// <summary>One native rename with explicit replacement policy. Never checks then copies/deletes or falls back across volumes.</summary>
public interface INativeFileRename
{
    /// <summary>Returns the native outcome and error code. Success is the publication boundary and cannot be undone by cancellation.</summary>
    /// <exception cref="ArgumentException">A path is empty or contains a null character.</exception>
    /// <exception cref="ArgumentNullException">A path is null.</exception>
    FileRenameStatus Move(string source, string destination, out int errorCode, bool replaceExisting = false);
}

public enum FileRenameStatus { Success, DestinationExists, CrossVolumeUnsupported, Unavailable }
