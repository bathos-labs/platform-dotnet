namespace Bathos.Platform.Files;

/// <summary>Checks existing entries without opening POSIX special files. This observation is not a path reservation.</summary>
public interface IFileEntryProbe
{
    /// <exception cref="ArgumentException">The path is empty or contains a null character.</exception>
    /// <exception cref="ArgumentNullException">The path is null.</exception>
    void RequireRegularFile(string path);
}
