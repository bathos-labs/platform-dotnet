namespace Bathos.Platform.Desktop;

/// <summary>Requests selection of an existing filesystem entry in its containing folder.</summary>
/// <remarks>Paths must be absolute. Cancellation is observed before handoff; success does not establish foreground focus.</remarks>
public interface IFileRevealer
{
    Task RevealAsync(string path, CancellationToken cancellationToken = default);
}
