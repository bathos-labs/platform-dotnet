namespace Bathos.Platform.Desktop;

/// <summary>Retrieves icon data for an explicit native application target without launching it.</summary>
public interface IApplicationIconProvider
{
    /// <include file="../Documentation/Contracts.xml" path="contracts/GetIconAsync/*" />
    Task<ApplicationIcon?> GetIconAsync(ApplicationTarget application, int pixelSize, CancellationToken cancellationToken = default);
}
