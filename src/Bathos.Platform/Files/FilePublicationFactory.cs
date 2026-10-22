namespace Bathos.Platform.Files;

/// <summary>Creates independently owned staging operations using the composed host rename provider.</summary>
public sealed class FilePublicationFactory(INativeFileRename rename)
{
    public FilePublication Create(string directory) => new(directory, rename);
}
