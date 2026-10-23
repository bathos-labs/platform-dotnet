using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Bathos.Platform.Desktop;

namespace Bathos.Platform.Linux;

/// <summary>Native GIO lookup of desktop-entry registrations. Linux desktop support remains experimental.</summary>
public static partial class LinuxApplicationDiscovery
{
    /// <summary>Returns a usable desktop-entry target, or an empty list when no usable registration exists.</summary>
    /// <param name="desktopId">Desktop-entry ID including .desktop; paths and directory separators are rejected.</param>
    /// <returns>A single usable registration, or an empty list when GIO cannot resolve a usable entry.</returns>
    /// <remarks>Run only on Linux. The host chooses among candidates; lookup does not prove that a target accepts a particular document format. Native loading and lookup failures propagate.</remarks>
    /// <exception cref="ArgumentNullException">The lookup key is null.</exception>
    /// <exception cref="ArgumentException">The key is blank, contains a null character, or has invalid syntax.</exception>
    [SupportedOSPlatform("linux")]
    public static IReadOnlyList<ApplicationTarget> FindByDesktopId(string desktopId)
    {
        var target = ApplicationTarget.LinuxDesktopEntry(desktopId);
        var app = NewDesktopApp(target.Value);
        if (app == 0) return [];
        try { return [target]; }
        finally { Unref(app); }
    }

    internal static void Open(string path, string desktopId, CancellationToken token)
    {
        var app = NewDesktopApp(desktopId);
        if (app == 0) throw new FileNotFoundException("No usable application is registered for the desktop-entry ID.", desktopId);
        nint file = 0, files = 0, error = 0;
        try
        {
            file = NewFile(path);
            if (file == 0) throw new IOException("GIO could not create the file reference.");
            files = AppendList(0, file);
            if (files == 0) throw new IOException("GLib could not create the file list.");
            token.ThrowIfCancellationRequested();
            if (Launch(app, files, 0, out error) == 0)
            {
                if (error == 0) throw new IOException("GIO could not hand the file to the application; no native error was supplied.");
                var native = Marshal.PtrToStructure<NativeError>(error);
                var detail = Marshal.PtrToStringUTF8(native.Message) ?? "The native error contained no message.";
                throw new IOException($"GIO could not hand the file to the application ({native.Domain}:{native.Code}): {detail}");
            }
        }
        finally
        {
            if (error != 0) FreeError(error);
            if (files != 0) FreeList(files);
            if (file != 0) Unref(file);
            Unref(app);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeError { public uint Domain; public int Code; public nint Message; }
    [LibraryImport("libgio-2.0.so.0", EntryPoint = "g_desktop_app_info_new", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint NewDesktopApp(string id);
    [LibraryImport("libgio-2.0.so.0", EntryPoint = "g_file_new_for_path", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint NewFile(string path);
    [LibraryImport("libgio-2.0.so.0", EntryPoint = "g_app_info_launch")]
    private static partial int Launch(nint app, nint files, nint context, out nint error);
    [LibraryImport("libglib-2.0.so.0", EntryPoint = "g_list_append")]
    private static partial nint AppendList(nint list, nint value);
    [LibraryImport("libglib-2.0.so.0", EntryPoint = "g_list_free")]
    private static partial void FreeList(nint list);
    [LibraryImport("libglib-2.0.so.0", EntryPoint = "g_error_free")]
    private static partial void FreeError(nint error);
    [LibraryImport("libgobject-2.0.so.0", EntryPoint = "g_object_unref")]
    private static partial void Unref(nint value);
}
