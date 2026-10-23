namespace Bathos.Platform.Desktop;

/// <summary>Requests selection of an existing filesystem entry in its containing folder.</summary>
/// <remarks>Paths must be absolute. Cancellation is observed before handoff; success does not establish foreground focus.</remarks>
public interface IFileRevealer
{
    /// <include file="../Documentation/Contracts.xml" path="contracts/RevealAsync/*" />
    Task RevealAsync(string path, CancellationToken cancellationToken = default);
}
