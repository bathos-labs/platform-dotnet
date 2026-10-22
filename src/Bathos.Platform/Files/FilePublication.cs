using System.ComponentModel;

namespace Bathos.Platform.Files;

/// <summary>
/// Owns a private temporary sibling until complete bytes can be published with one native rename. Replacement requires explicit caller authorization.
/// Disposal cleans up only that temporary file; published files survive cancellation, registration failure, and Undo.
/// A publication is single-use and must not be accessed concurrently. Separate publications may race safely.
/// </summary>
public sealed class FilePublication : IAsyncDisposable
{
    private readonly INativeFileRename _rename;
    private readonly string _directory;
    private readonly string _temporary;
    private bool _completed;

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
    public Task<string> PublishAsync(GeneratedFileName name, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(name);
        return PublishAsync(name.Candidate, retryCollisions: true, replaceExisting: false, token);
    }

    /// <summary>Publishes an explicitly selected filename verbatim. The caller must authorize replacement; otherwise collisions fail.</summary>
    public Task<string> PublishExactAsync(string fileName, CancellationToken token, bool replaceExisting = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        if (fileName is "." or ".." || fileName.IndexOfAny(['/', '\\', '\0']) >= 0 ||
            fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || Path.GetFileName(fileName) != fileName)
            throw new ArgumentException("A single filename is required.", nameof(fileName));
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
            if (status == FileRenameStatus.Success) return destination;
            if (status == FileRenameStatus.DestinationExists && retryCollisions && sequence < int.MaxValue) continue;
            throw new IOException(status == FileRenameStatus.DestinationExists
                ? "The publication filename is already occupied."
                : "The file could not be published with a native rename.", new Win32Exception(errorCode));
        }
    }

    public async ValueTask DisposeAsync()
    {
        _completed = true;
        try { await Stream.DisposeAsync().ConfigureAwait(false); }
        finally
        {
            try { File.Delete(_temporary); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { /* Never remove a published source or mask the operation's failure. */ }
        }
    }
}
