using System.Runtime.InteropServices;
using System.Text;
using Bathos.Platform.Desktop;

namespace Bathos.Platform.Linux;

/// <summary>Discovers desktop-entry application choices using a local file's GIO content type.</summary>
public sealed partial class LinuxFileApplicationDiscovery : IApplicationDiscovery
{
    /// <include file="../Bathos.Platform/Documentation/Contracts.xml" path="contracts/FindApplicationsForFileAsync/*" />
    public Task<IReadOnlyList<ApplicationCandidate>> FindForFileAsync(string absolutePath, CancellationToken cancellationToken = default)
    {
        DesktopPaths.RequireAbsolute(absolutePath, nameof(absolutePath));
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Linux discovery requires Linux.");
        return Task.Run<IReadOnlyList<ApplicationCandidate>>(() => Read(absolutePath, cancellationToken), cancellationToken);
    }

    private static System.Collections.ObjectModel.ReadOnlyCollection<ApplicationCandidate> Read(string path, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        new LinuxFileEntryProbe().RequireRegularFile(path);
        var file = NewFile(path);
        if (file == 0) throw new IOException("GIO could not represent the file.");
        nint info = 0, list = 0, remaining = 0;
        try
        {
            info = Query(file, "standard::content-type", 0, 0, out var error);
            if (info == 0) throw Failure(error);
            var type = Text(ContentType(info));
            if (string.IsNullOrWhiteSpace(type)) throw new IOException("GIO returned no usable file content type.");
            list = Applications(type);
            remaining = list;
            var results = new List<ApplicationCandidate>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var count = 0;
            while (remaining != 0)
            {
                token.ThrowIfCancellationRequested();
                var node = Marshal.PtrToStructure<ListNode>(remaining);
                if (count++ >= 4096) throw new IOException("Application discovery exceeded its candidate limit.");
                if (node.Data == 0) throw new IOException("GIO returned an invalid application entry.");
                var id = Text(Id(node.Data));
                if (string.IsNullOrWhiteSpace(id)) throw new IOException("GIO returned an application without a desktop-entry identity.");
                ApplicationTarget target;
                try { target = ApplicationTarget.LinuxDesktopEntry(id); }
                catch (ArgumentException exception) { throw new IOException("GIO returned an invalid desktop-entry identity.", exception); }
                if (seen.Add(id))
                {
                    var name = Text(DisplayName(node.Data));
                    results.Add(new(target, string.IsNullOrWhiteSpace(name) ? id : name));
                }
                // Move cleanup ownership only after processing the complete candidate.
                Unref(node.Data);
                remaining = node.Next;
            }
            token.ThrowIfCancellationRequested();
            return results.AsReadOnly();
        }
        finally
        {
            while (remaining != 0)
            {
                var node = Marshal.PtrToStructure<ListNode>(remaining);
                if (node.Data != 0) Unref(node.Data);
                remaining = node.Next;
            }
            if (list != 0) FreeList(list);
            if (info != 0) Unref(info);
            Unref(file);
        }
    }

    private static unsafe string? Text(nint value)
    {
        if (value == 0) return null;
        var bytes = (byte*)value;
        for (var length = 0; length < 131072; length++)
            if (bytes[length] == 0)
            {
                try { return new UTF8Encoding(false, true).GetString(new ReadOnlySpan<byte>(bytes, length)); }
                catch (DecoderFallbackException error) { throw new IOException("GIO returned invalid UTF-8 metadata.", error); }
            }
        throw new IOException("GIO application metadata exceeded its byte limit.");
    }
    private static IOException Failure(nint error)
    {
        if (error == 0) return new IOException("GIO could not query the file content type.");
        try { return new IOException($"GIO could not query the file content type: {Text(Marshal.PtrToStructure<NativeError>(error).Message)}"); }
        finally { FreeError(error); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct ListNode { public nint Data, Next, Previous; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeError { public uint Domain; public int Code; public nint Message; }
    [LibraryImport("libgio-2.0.so.0", EntryPoint = "g_file_new_for_path", StringMarshalling = StringMarshalling.Utf8)] private static partial nint NewFile(string path);
    [LibraryImport("libgio-2.0.so.0", EntryPoint = "g_file_query_info", StringMarshalling = StringMarshalling.Utf8)] private static partial nint Query(nint file, string attributes, int flags, nint cancellable, out nint error);
    [LibraryImport("libgio-2.0.so.0", EntryPoint = "g_file_info_get_content_type")] private static partial nint ContentType(nint info);
    [LibraryImport("libgio-2.0.so.0", EntryPoint = "g_app_info_get_all_for_type", StringMarshalling = StringMarshalling.Utf8)] private static partial nint Applications(string type);
    [LibraryImport("libgio-2.0.so.0", EntryPoint = "g_app_info_get_id")] private static partial nint Id(nint app);
    [LibraryImport("libgio-2.0.so.0", EntryPoint = "g_app_info_get_display_name")] private static partial nint DisplayName(nint app);
    [LibraryImport("libgobject-2.0.so.0", EntryPoint = "g_object_unref")] private static partial void Unref(nint value);
    [LibraryImport("libglib-2.0.so.0", EntryPoint = "g_list_free")] private static partial void FreeList(nint value);
    [LibraryImport("libglib-2.0.so.0", EntryPoint = "g_error_free")] private static partial void FreeError(nint value);
}
