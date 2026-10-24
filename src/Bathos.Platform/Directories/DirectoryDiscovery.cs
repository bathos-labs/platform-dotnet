using Bathos.Platform.Paths;

namespace Bathos.Platform.Directories;

internal static class DirectoryDiscovery
{
    internal static StandardDirectoryRoots Discover(PlatformOperatingSystem host)
    {
        if (PlatformHost.Current != host) throw new PlatformNotSupportedException("Directory discovery requires the matching runtime host.");
        var paths = host == PlatformOperatingSystem.Windows ? NativeFilePaths.Windows : NativeFilePaths.Posix;
        return Resolve(host, paths, Folder, Environment.GetEnvironmentVariable, Path.GetTempPath(), AppContext.BaseDirectory);
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
            Documents = Normalize(folder(Environment.SpecialFolder.MyDocuments)),
            Desktop = Normalize(folder(Environment.SpecialFolder.DesktopDirectory)),
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
