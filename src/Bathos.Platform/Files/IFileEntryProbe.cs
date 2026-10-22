namespace Bathos.Platform.Files;

/// <summary>Checks existing entries without opening POSIX special files. This observation is not a path reservation.</summary>
public interface IFileEntryProbe
{
    /// <summary>Returns normally only when the observed entry is a regular file, rather than a link or special entry.</summary>
    /// <param name="path">Path to inspect. This observation does not reserve the path against later changes.</param>
    /// <exception cref="IOException">The entry is missing, nonregular, or cannot be inspected by the native API.</exception>
    /// <exception cref="UnauthorizedAccessException">Access to the entry is denied by a managed filesystem operation.</exception>
    /// <exception cref="ArgumentException">The path is empty or contains a null character.</exception>
    /// <exception cref="ArgumentNullException">The path is null.</exception>
    void RequireRegularFile(string path);
}
