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

    /// <summary>Creates owned staging with explicit directory-creation policy.</summary>
    /// <param name="directory">Absolute destination directory.</param>
    /// <param name="createDirectory">Whether missing directories may be created.</param>
    /// <returns>A single-use publication that the caller must dispose.</returns>
    /// <exception cref="ArgumentException">The directory is blank or relative.</exception>
    /// <exception cref="ArgumentNullException">The directory or configured provider is null.</exception>
    /// <exception cref="IOException">The directory is missing or staging cannot be created.</exception>
    /// <exception cref="UnauthorizedAccessException">Filesystem access is denied.</exception>
    public FilePublication Create(string directory, bool createDirectory) => new(directory, rename, createDirectory);

    /// <summary>Stages complete bytes and publishes them with explicit replacement and directory-creation policies.</summary>
    /// <remarks>For retained-reader replacement, construct this factory with ISnapshotFileRename. Serialization and writer coordination belong to the caller.</remarks>
    /// <param name="destination">Absolute destination file path.</param>
    /// <param name="contents">Complete file bytes; the caller must not mutate them until completion.</param>
    /// <param name="replaceExisting">Explicit permission to replace an occupied destination.</param>
    /// <param name="createDirectory">Whether missing destination directories may be created.</param>
    /// <param name="cancellationToken">Observed before publication, never after successful rename.</param>
    /// <returns>Completion after publication and owned staging cleanup.</returns>
    /// <exception cref="ArgumentException">The destination is not an absolute file path.</exception>
    /// <exception cref="ArgumentNullException">The destination or configured provider is null.</exception>
    /// <exception cref="IOException">Staging or publication fails, including an unauthorized collision.</exception>
    /// <exception cref="UnauthorizedAccessException">Filesystem access is denied.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was observed before publication.</exception>
    public async ValueTask WriteAsync(string destination, ReadOnlyMemory<byte> contents, bool replaceExisting,
        bool createDirectory, CancellationToken cancellationToken = default)
    {
        FileEntryErrors.ValidatePath(destination, nameof(destination));
        if (!Path.IsPathFullyQualified(destination)) throw new ArgumentException("An absolute destination is required.", nameof(destination));
        cancellationToken.ThrowIfCancellationRequested();
        var directory = Path.GetDirectoryName(destination) ?? throw new ArgumentException("A destination directory is required.", nameof(destination));
        await using var publication = Create(directory, createDirectory);
        await publication.Stream.WriteAsync(contents, cancellationToken).ConfigureAwait(false);
        await publication.PublishExactAsync(Path.GetFileName(destination), cancellationToken, replaceExisting).ConfigureAwait(false);
    }
}
