namespace Bathos.Platform.Files;

/// <summary>Checks existing entries without opening POSIX special files. This observation is not a path reservation.</summary>
public interface IFileEntryProbe
{
    /// <include file="../Documentation/Contracts.xml" path="contracts/RequireRegularFile/*" />
    void RequireRegularFile(string path);
}
