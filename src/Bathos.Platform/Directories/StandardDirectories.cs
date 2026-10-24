using Bathos.Platform.Paths;

namespace Bathos.Platform.Directories;

/// <summary>Discovers user and application roots without creating directories or claiming access or ownership.</summary>
public interface IStandardDirectories
{
    /// <summary>Observes current host roots. Null locations are unavailable; lookup failures may propagate.</summary>
    /// <remarks>Invoke on the provider's OS. Environment changes are observed on each call, without atomic snapshot guarantees.
    /// Temporary roots are shared locations, not owned workspaces. Application base is a resource location, not writable user storage.</remarks>
    StandardDirectoryRoots Discover();
}

/// <summary>Observed or explicitly overridden roots. Null means unavailable; existence and write access are not guaranteed.</summary>
/// <param name="OperatingSystem">Layout convention used when scoping applications, independently of the executing host.</param>
/// <remarks>Hosts may replace roots with a record copy for portable installs or tests. Scoping validates supplied roots
/// through explicit native path syntax; invalid nonnull overrides throw rather than using a working-directory fallback.</remarks>
public sealed record StandardDirectoryRoots(PlatformOperatingSystem OperatingSystem)
{
    /// <summary>User home directory.</summary>
    public string? Home { get; init; }
    /// <summary>Durable local application data root.</summary>
    public string? Data { get; init; }
    /// <summary>Configuration root; Windows uses roaming application data.</summary>
    public string? Configuration { get; init; }
    /// <summary>Disposable cache root; on Windows this is local application data before application scoping.</summary>
    public string? Cache { get; init; }
    /// <summary>Persistent state root; Windows/macOS share the data root, Linux uses XDG state.</summary>
    public string? State { get; init; }
    /// <summary>Shared temporary directory; no unique workspace or cleanup authority is acquired.</summary>
    public string? Temporary { get; init; }
    /// <summary>Application base directory for deployed resources, independently of writable storage.</summary>
    public string? ApplicationBase { get; init; }
    /// <summary>User documents directory, when supplied by the runtime.</summary>
    public string? Documents { get; init; }
    /// <summary>User desktop directory, when supplied by the runtime.</summary>
    public string? Desktop { get; init; }

    /// <summary>Scopes data/configuration/cache/state under publisher/product without creating anything.</summary>
    /// <remarks>Windows adds Cache beneath the scoped local-data root; other platforms already have a separate cache root.
    /// Unavailable roots remain null. Home, documents, desktop, temporary, and resource roots are not application-scoped.</remarks>
    public ApplicationDirectoryLocations ForApplication(ApplicationDirectoryIdentity identity, INativeFilePaths paths)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(paths);
        if (OperatingSystem is not (PlatformOperatingSystem.Windows or PlatformOperatingSystem.MacOS or PlatformOperatingSystem.Linux))
            throw new InvalidOperationException("A supported directory layout is required.");
        var cache = Scope(Cache);
        if (cache is not null && OperatingSystem == PlatformOperatingSystem.Windows) cache = paths.NormalizeAbsolute(Path.Combine(cache, "Cache"));
        return new(Scope(Data), Scope(Configuration), cache, Scope(State));

        string? Scope(string? root) => root is null ? null : identity.Scope(root, paths);
    }
}

/// <summary>Application-scoped locations. Null means unavailable; hosts decide which locations are required.</summary>
/// <param name="Data">Durable local data location.</param>
/// <param name="Configuration">Configuration location.</param>
/// <param name="Cache">Disposable cache location.</param>
/// <param name="State">Persistent state location.</param>
public sealed record ApplicationDirectoryLocations(string? Data, string? Configuration, string? Cache, string? State);

/// <summary>Explicit directory identity using portable single filename components; no sanitization or case folding occurs.</summary>
public sealed class ApplicationDirectoryIdentity
{
    /// <summary>Validates a product name and optional publisher against the Bathos portable filename policy.</summary>
    public ApplicationDirectoryIdentity(string product, string? publisher = null)
    {
        if (!PortableFileNames.IsValid(product)) throw new ArgumentException("Use a portable product directory name.", nameof(product));
        if (publisher is not null && !PortableFileNames.IsValid(publisher)) throw new ArgumentException("Use a portable publisher directory name.", nameof(publisher));
        Product = product;
        Publisher = publisher;
    }
    /// <summary>Product directory component.</summary>
    public string Product { get; }
    /// <summary>Optional publisher directory component.</summary>
    public string? Publisher { get; }
    /// <summary>Appends the identity to an explicit absolute root. No directory is created or reserved.</summary>
    public string Scope(string root, INativeFilePaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        root = paths.NormalizeAbsolute(root);
        return paths.NormalizeAbsolute(Publisher is null ? Path.Combine(root, Product) : Path.Combine(root, Publisher, Product));
    }
}
