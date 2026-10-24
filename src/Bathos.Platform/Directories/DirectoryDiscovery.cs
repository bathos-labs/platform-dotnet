using Bathos.Platform.Paths;

namespace Bathos.Platform.Directories;

internal static class DirectoryDiscovery
{
    internal static StandardDirectoryRoots Discover(PlatformOperatingSystem host) => DiscoverCore(host, Folder);

    internal static StandardDirectoryRoots Discover(PlatformOperatingSystem host, Func<string?> home) =>
        DiscoverCore(host, folder => folder == Environment.SpecialFolder.UserProfile ? home() ?? "" : Folder(folder));

    private static StandardDirectoryRoots DiscoverCore(PlatformOperatingSystem host, Func<Environment.SpecialFolder, string> folder)
    {
        if (PlatformHost.Current != host) throw new PlatformNotSupportedException("Directory discovery requires the matching runtime host.");
        var paths = host == PlatformOperatingSystem.Windows ? NativeFilePaths.Windows : NativeFilePaths.Posix;
        return Resolve(host, paths, folder, Environment.GetEnvironmentVariable, Path.GetTempPath(), AppContext.BaseDirectory);
    }

    private static string Folder(Environment.SpecialFolder folder) =>
        Environment.GetFolderPath(folder, Environment.SpecialFolderOption.DoNotVerify);

    internal static StandardDirectoryRoots Resolve(PlatformOperatingSystem host, INativeFilePaths paths,
        Func<Environment.SpecialFolder, string> folder, Func<string, string?> environment, string temporary, string applicationBase)
    {
        var home = Normalize(folder(Environment.SpecialFolder.UserProfile));
        var data = host == PlatformOperatingSystem.Linux ? Xdg("XDG_DATA_HOME", ".local", "share")
            : Normalize(folder(Environment.SpecialFolder.LocalApplicationData));
        var configuration = host == PlatformOperatingSystem.Linux ? Xdg("XDG_CONFIG_HOME", ".config")
            : host == PlatformOperatingSystem.Windows ? Normalize(folder(Environment.SpecialFolder.ApplicationData)) : data;
        var cache = host == PlatformOperatingSystem.Linux ? Xdg("XDG_CACHE_HOME", ".cache")
            : host == PlatformOperatingSystem.MacOS ? Normalize(folder(Environment.SpecialFolder.InternetCache)) : data;
        return new(host)
        {
            Home = home,
            Data = data,
            Configuration = configuration,
            Cache = cache,
            State = host == PlatformOperatingSystem.Linux ? Xdg("XDG_STATE_HOME", ".local", "state") : data,
            Temporary = Normalize(temporary),
            ApplicationBase = Normalize(applicationBase),
            // The runtime's Linux user-folder lookup also substitutes '/' when no home exists. Keep only explicit
            // independent overrides in that case, without consulting a fallback user-dirs file beneath '/'.
            Documents = host == PlatformOperatingSystem.Linux && home is null ? Normalize(environment("XDG_DOCUMENTS_DIR")) : Normalize(folder(Environment.SpecialFolder.MyDocuments)),
            Desktop = host == PlatformOperatingSystem.Linux && home is null ? Normalize(environment("XDG_DESKTOP_DIR")) : Normalize(folder(Environment.SpecialFolder.DesktopDirectory)),
        };

        string? Xdg(string variable, params string[] suffix) => Normalize(environment(variable)) ?? UnderHome(suffix);
        string? UnderHome(params string[] suffix) => home is null ? null : paths.NormalizeAbsolute(Path.Combine([home, .. suffix]));
        string? Normalize(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            try { return paths.NormalizeAbsolute(value); }
            catch (ArgumentException) { return null; }
            catch (NotSupportedException) { return null; }
            catch (PathTooLongException) { return null; }
        }
    }
}
