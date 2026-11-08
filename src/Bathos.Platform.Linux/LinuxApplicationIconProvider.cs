using System.Runtime.InteropServices;
using Bathos.Platform.Desktop;

namespace Bathos.Platform.Linux;

/// <summary>Resolves desktop-entry icons through GIO and an explicitly selected freedesktop theme, without a UI toolkit.</summary>
public sealed partial class LinuxApplicationIconProvider : IApplicationIconProvider
{
    private readonly string _theme;
    /// <summary>Creates an inert provider. Theme selection belongs to the host; hicolor is the standard fallback theme.</summary>
    /// <param name="theme">A single theme-directory component, such as hicolor or Adwaita.</param>
    /// <exception cref="ArgumentException">The theme is not a nonempty single directory component.</exception>
    /// <exception cref="ArgumentNullException">The theme is null.</exception>
    public LinuxApplicationIconProvider(string theme = "hicolor")
    {
        ArgumentNullException.ThrowIfNull(theme);
        if (!LinuxIconTheme.IsComponent(theme)) throw new ArgumentException("Use one icon-theme directory name.", nameof(theme));
        _theme = theme;
    }

    /// <include file="../Bathos.Platform/Documentation/Contracts.xml" path="contracts/GetIconAsync/*" />
    public Task<ApplicationIcon?> GetIconAsync(ApplicationTarget application, int pixelSize, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(application); ApplicationIcon.ValidatePixelSize(pixelSize);
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Linux application icons require Linux.");
        if (application.Kind != ApplicationTargetKind.LinuxDesktopEntry) throw new NotSupportedException("Linux icons require a desktop-entry target.");
        return Task.Run(() =>
        {
            try { return Read(application.Value, pixelSize, cancellationToken); }
            catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
            {
                throw new PlatformNotSupportedException($"Linux application-icon lookup requires compatible libgio-2.0.so.0, libgobject-2.0.so.0, and libglib-2.0.so.0. Native loading failure: {error.Message}", error);
            }
        }, cancellationToken);
    }

    private ApplicationIcon? Read(string id, int size, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var app = NewDesktopApp(id);
        if (app == 0) throw new FileNotFoundException("The desktop-entry application is unavailable.", id);
        try
        {
            var icon = GetIcon(app); // Borrowed from app; never unref independently.
            if (icon == 0) { token.ThrowIfCancellationRequested(); return null; }
            string? path;
            if (IsA(icon, FileIconType()) != 0)
            {
                var file = GetFile(icon); var nativePath = FilePath(file);
                if (nativePath == 0) throw new NotSupportedException("Only local application-icon resources are supported.");
                try { path = Marshal.PtrToStringUTF8(nativePath) ?? throw new IOException("GIO returned no icon path."); }
                finally { Free(nativePath); }
            }
            else if (IsA(icon, ThemedIconType()) != 0)
            {
                var names = new List<string>(); var nativeNames = GetNames(icon);
                if (nativeNames == 0) { token.ThrowIfCancellationRequested(); return null; }
                for (var index = 0; ; index++)
                {
                    var name = Marshal.ReadIntPtr(nativeNames, index * IntPtr.Size);
                    if (name == 0) break;
                    if (index == 64) throw new IOException("The application icon has too many fallback names.");
                    names.Add(Marshal.PtrToStringUTF8(name) ?? throw new IOException("GIO returned an invalid icon name."));
                }
                path = LinuxIconTheme.Find(names, _theme, size, IconRoots(), token);
            }
            else throw new NotSupportedException("The native application icon is not a file or themed icon.");
            token.ThrowIfCancellationRequested();
            if (path is null) return null;
            DesktopPaths.RequireAbsolute(path, nameof(path));
            var result = LinuxIconRenderer.Read(path, size);
            token.ThrowIfCancellationRequested();
            return result;
        }
        finally { Unref(app); }
    }

    private static string[] IconRoots()
    {
        var roots = new List<string>();
        var directories = new LinuxStandardDirectories().Discover();
        if (directories.Home is { } home) roots.Add(Path.Combine(home, ".icons"));
        if (directories.Data is { } data) roots.Add(Path.Combine(data, "icons"));
        var system = Environment.GetEnvironmentVariable("XDG_DATA_DIRS");
        foreach (var root in (string.IsNullOrEmpty(system) ? "/usr/local/share:/usr/share" : system).Split(':', StringSplitOptions.RemoveEmptyEntries))
            if (Path.IsPathFullyQualified(root)) roots.Add(Path.Combine(root, "icons"));
        if (roots.Count > 64) throw new IOException("The icon search path has too many directories.");
        roots.Add("/usr/share/pixmaps");
        return roots.Distinct(StringComparer.Ordinal).ToArray();
    }

    [LibraryImport("libgio-2.0.so.0", EntryPoint = "g_desktop_app_info_new", StringMarshalling = StringMarshalling.Utf8)] private static partial nint NewDesktopApp(string id);
    [LibraryImport("libgio-2.0.so.0", EntryPoint = "g_app_info_get_icon")] private static partial nint GetIcon(nint app);
    [LibraryImport("libgio-2.0.so.0", EntryPoint = "g_file_icon_get_type")] private static partial nuint FileIconType();
    [LibraryImport("libgio-2.0.so.0", EntryPoint = "g_themed_icon_get_type")] private static partial nuint ThemedIconType();
    [LibraryImport("libgio-2.0.so.0", EntryPoint = "g_file_icon_get_file")] private static partial nint GetFile(nint icon);
    [LibraryImport("libgio-2.0.so.0", EntryPoint = "g_file_get_path")] private static partial nint FilePath(nint file);
    [LibraryImport("libgio-2.0.so.0", EntryPoint = "g_themed_icon_get_names")] private static partial nint GetNames(nint icon);
    [LibraryImport("libgobject-2.0.so.0", EntryPoint = "g_type_check_instance_is_a")] private static partial int IsA(nint instance, nuint type);
    [LibraryImport("libgobject-2.0.so.0", EntryPoint = "g_object_unref")] private static partial void Unref(nint value);
    [LibraryImport("libglib-2.0.so.0", EntryPoint = "g_free")] private static partial void Free(nint value);
}
