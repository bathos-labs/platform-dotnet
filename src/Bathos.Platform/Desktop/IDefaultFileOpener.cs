namespace Bathos.Platform.Desktop;

/// <summary>Hands an existing regular file to the operating system's current default application without changing associations.</summary>
/// <remarks>
/// This operation does not discover, select, or return the default application. The operating system resolves it at handoff.
/// The caller retains file ownership. No application process is awaited or terminated; submission helpers are joined.
/// </remarks>
public interface IDefaultFileOpener
{
    /// <include file="../Documentation/Contracts.xml" path="contracts/OpenDefaultFileAsync/*" />
    Task OpenAsync(string path, CancellationToken cancellationToken = default);
}
