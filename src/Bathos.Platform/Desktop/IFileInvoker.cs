namespace Bathos.Platform.Desktop;

/// <summary>Hands an existing file to an explicit application without changing file associations.</summary>
/// <remarks>
/// Paths must be absolute. Cancellation is observed before handoff. Completion means the request was submitted,
/// not that the application loaded the document. The caller retains file ownership; no application process is awaited
/// or terminated. Native helpers used to submit a request are awaited. No product-specific arguments are inferred.
/// </remarks>
public interface IFileInvoker
{
    Task OpenAsync(string path, ApplicationTarget application, CancellationToken cancellationToken = default);
}
