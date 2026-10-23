using System.ComponentModel;

namespace Bathos.Platform.Files;

/// <summary>
/// Owns a private temporary sibling until complete bytes can be published with one native rename. Replacement requires explicit caller authorization.
/// Disposal cleans up only that temporary file; published files survive cancellation and later caller failures.
/// Staging is flushed before rename, but directory metadata is not synchronized; atomic visibility is not power-loss durability.
/// A publication is single-use and must not be accessed concurrently. Separate publications may race safely.
/// </summary>
public sealed class FilePublication : IAsyncDisposable
{
    private readonly INativeFileRename _rename;
    private readonly string _directory;
    private readonly string _temporary;
    private bool _completed;
    private bool _ownsTemporary = true;

    /// <summary>Creates and exclusively opens a temporary sibling in an absolute directory, creating the directory if needed.</summary>
    /// <exception cref="ArgumentException">The directory is empty or is not absolute.</exception>
    /// <exception cref="ArgumentNullException">The directory or rename provider is null.</exception>
    /// <exception cref="IOException">The directory or staging file cannot be created.</exception>
    /// <exception cref="UnauthorizedAccessException">The caller cannot create the directory or staging file.</exception>
    /// <param name="directory">Absolute destination directory in which staging is created.</param>
    /// <param name="rename">Native rename capability for the destination filesystem.</param>
    public FilePublication(string directory, INativeFileRename rename)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (!Path.IsPathFullyQualified(directory)) throw new ArgumentException("An absolute publication directory is required.", nameof(directory));
        _rename = rename ?? throw new ArgumentNullException(nameof(rename));
        _directory = directory;
        Directory.CreateDirectory(directory);
        _temporary = Path.Combine(directory, ".bathos-" + Guid.NewGuid().ToString("N") + ".tmp");
        // If exclusive creation fails, this instance never owns the path and must not clean it up.
        Stream = new FileStream(_temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
    }

    /// <summary>Borrowed seekable stream for encoding and fingerprinting. Publication closes it before renaming.</summary>
    public FileStream Stream { get; }

    /// <summary>Claims the first available candidate at publication, retrying only native destination collisions.</summary>
    /// <returns>The absolute published path.</returns>
    /// <exception cref="OperationCanceledException">Cancellation was observed before successful publication.</exception>
    /// <exception cref="InvalidOperationException">Publication was already attempted or the operation was disposed.</exception>
    /// <exception cref="IOException">Staging could not be flushed or published.</exception>
    /// <param name="name">Normalized generated filename components. Sequence numbers are claimed only at rename.</param>
    /// <param name="token">Cancellation checked before flush and rename attempts, never after a successful rename.</param>
    /// <exception cref="ArgumentNullException">The generated name is null.</exception>
    public Task<string> PublishAsync(GeneratedFileName name, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(name);
        return PublishAsync(name.Candidate, retryCollisions: true, replaceExisting: false, token);
    }

    /// <summary>Publishes an explicitly selected filename verbatim. The caller must authorize replacement; otherwise collisions fail.</summary>
    /// <returns>The absolute published path.</returns>
    /// <exception cref="ArgumentException">The name is invalid, contains a directory component, or identifies staging.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was observed before successful publication.</exception>
    /// <exception cref="InvalidOperationException">Publication was already attempted or the operation was disposed.</exception>
    /// <exception cref="IOException">Staging could not be flushed or published, including an unauthorized collision.</exception>
    /// <param name="fileName">A single nonblank filename, excluding the staging name and directory components.</param>
    /// <param name="token">Cancellation checked before flush and rename, never after successful publication.</param>
    /// <param name="replaceExisting">Explicit authorization to replace an occupied destination atomically.</param>
    /// <exception cref="ArgumentNullException">The filename is null.</exception>
    public Task<string> PublishExactAsync(string fileName, CancellationToken token, bool replaceExisting = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        if (fileName is "." or ".." || fileName.IndexOfAny(['/', '\\', '\0']) >= 0 ||
            fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || Path.GetFileName(fileName) != fileName)
            throw new ArgumentException("A single filename is required.", nameof(fileName));
        // Reject case aliases too, including on case-insensitive macOS volumes.
        var comparableName = OperatingSystem.IsWindows() ? fileName.TrimEnd(' ', '.') : fileName;
        if (string.Equals(comparableName, Path.GetFileName(_temporary), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The staging filename cannot be used as the publication filename.", nameof(fileName));
        return PublishAsync(_ => fileName, retryCollisions: false, replaceExisting, token);
    }

    private async Task<string> PublishAsync(Func<int, string> candidate, bool retryCollisions, bool replaceExisting, CancellationToken token)
    {
        if (_completed) throw new InvalidOperationException("This file publication has already completed.");
        _completed = true;
        token.ThrowIfCancellationRequested();
        await Stream.FlushAsync(token).ConfigureAwait(false);
        Stream.Flush(flushToDisk: true);
        await Stream.DisposeAsync().ConfigureAwait(false);
        for (var sequence = 1; ; sequence++)
        {
            token.ThrowIfCancellationRequested();
            var destination = Path.Combine(_directory, candidate(sequence));
            var status = _rename.Move(_temporary, destination, out var errorCode, replaceExisting);
            if (status == FileRenameStatus.Success)
            {
                _ownsTemporary = false;
                return destination;
            }
            if (status == FileRenameStatus.DestinationExists && retryCollisions && sequence < int.MaxValue) continue;
            throw new IOException(status == FileRenameStatus.DestinationExists
                ? "The publication filename is already occupied."
                : "The file could not be published with a native rename.", new Win32Exception(errorCode));
        }
    }

    /// <summary>Closes the stream and attempts to remove owned staging once. Never removes a successfully published output.</summary>
    /// <remarks>Cleanup IO and access failures are suppressed so disposal does not mask publication failure.</remarks>
    public async ValueTask DisposeAsync()
    {
        _completed = true;
        try { await Stream.DisposeAsync().ConfigureAwait(false); }
        finally
        {
            try
            {
                if (_ownsTemporary)
                {
                    _ownsTemporary = false;
                    File.Delete(_temporary);
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { /* Never remove a published source or mask the operation's failure. */ }
        }
    }
}
