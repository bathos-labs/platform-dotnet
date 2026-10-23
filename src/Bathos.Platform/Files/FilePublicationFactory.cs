namespace Bathos.Platform.Files;

/// <summary>Creates independently owned staging operations using the composed host rename provider.</summary>
/// <param name="rename">Native rename capability used by each created operation; validated when Create is called.</param>
public sealed class FilePublicationFactory(INativeFileRename rename)
{
    /// <summary>Creates a new owned staging operation. The caller must dispose it after use, including on failure.</summary>
    /// <param name="directory">Absolute destination directory, created if needed.</param>
    /// <returns>An independent single-use publication with an open writable stream.</returns>
    /// <exception cref="ArgumentException">The directory is blank or not absolute.</exception>
    /// <exception cref="ArgumentNullException">The directory or configured rename provider is null.</exception>
    /// <exception cref="IOException">The staging file or directory cannot be created.</exception>
    /// <exception cref="UnauthorizedAccessException">Filesystem access is denied.</exception>
    public FilePublication Create(string directory) => new(directory, rename);
}
