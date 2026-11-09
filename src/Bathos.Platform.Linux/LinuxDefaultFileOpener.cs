using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Bathos.Platform.Desktop;
using Bathos.Platform.Paths;

namespace Bathos.Platform.Linux;

/// <summary>Hands a regular file to GIO's default application with an owned asynchronous activation context.</summary>
public sealed unsafe partial class LinuxDefaultFileOpener : IDefaultFileOpener
{
    /// <include file="../Bathos.Platform/Documentation/Contracts.xml" path="contracts/OpenDefaultFileAsync/*" />
    public Task OpenAsync(string path, CancellationToken cancellationToken = default)
    {
        DesktopPaths.RequireAbsolute(path, nameof(path));
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Linux default-file opening requires Linux.");
        return Task.Run(() => Open(path, cancellationToken), cancellationToken);
    }

    private static void Open(string path, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        new LinuxFileEntryProbe().RequireRegularFile(path);
        var uri = NativeFilePaths.Posix.ToFileUri(path);
        var context = NewContext();
        if (context == 0) throw new IOException("GLib could not create an activation context.");
        nint loop = 0, error = 0;
        var state = new Completion();
        try
        {
            loop = NewLoop(context, 0);
            if (loop == 0) throw new IOException("GLib could not create an activation loop.");
            state.Loop = loop;
            PushContext(context);
            try
            {
                token.ThrowIfCancellationRequested();
                // No native cancellation after handoff: finish activation and release all owned resources.
                Launch(uri, 0, 0, &Completed, (nint)(&state));
                RunLoop(loop);
                if (state.Result == 0) throw new IOException("GIO supplied no activation result.");
                if (Finish(state.Result, out error) == 0)
                {
                    var detail = error == 0 ? "No native error supplied." : Marshal.PtrToStringUTF8(Marshal.PtrToStructure<NativeError>(error).Message);
                    throw new IOException($"GIO could not open the file with its default application: {detail}");
                }
            }
            finally { PopContext(context); }
        }
        finally
        {
            if (error != 0) FreeError(error);
            if (state.Result != 0) Unref(state.Result);
            if (loop != 0) UnrefLoop(loop);
            UnrefContext(context);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Completed(nint source, nint result, nint data)
    {
        var state = (Completion*)data;
        state->Result = Ref(result);
        QuitLoop(state->Loop);
    }

    private struct Completion { public nint Loop; public nint Result; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeError { public uint Domain; public int Code; public nint Message; }
    [LibraryImport("libgio-2.0.so.0", EntryPoint = "g_app_info_launch_default_for_uri_async", StringMarshalling = StringMarshalling.Utf8)]
    private static partial void Launch(string uri, nint context, nint cancellable, delegate* unmanaged[Cdecl]<nint, nint, nint, void> callback, nint data);
    [LibraryImport("libgio-2.0.so.0", EntryPoint = "g_app_info_launch_default_for_uri_finish")] private static partial int Finish(nint result, out nint error);
    [LibraryImport("libglib-2.0.so.0", EntryPoint = "g_main_context_new")] private static partial nint NewContext();
    [LibraryImport("libglib-2.0.so.0", EntryPoint = "g_main_context_push_thread_default")] private static partial void PushContext(nint context);
    [LibraryImport("libglib-2.0.so.0", EntryPoint = "g_main_context_pop_thread_default")] private static partial void PopContext(nint context);
    [LibraryImport("libglib-2.0.so.0", EntryPoint = "g_main_context_unref")] private static partial void UnrefContext(nint context);
    [LibraryImport("libglib-2.0.so.0", EntryPoint = "g_main_loop_new")] private static partial nint NewLoop(nint context, int running);
    [LibraryImport("libglib-2.0.so.0", EntryPoint = "g_main_loop_run")] private static partial void RunLoop(nint loop);
    [LibraryImport("libglib-2.0.so.0", EntryPoint = "g_main_loop_quit")] private static partial void QuitLoop(nint loop);
    [LibraryImport("libglib-2.0.so.0", EntryPoint = "g_main_loop_unref")] private static partial void UnrefLoop(nint loop);
    [LibraryImport("libglib-2.0.so.0", EntryPoint = "g_error_free")] private static partial void FreeError(nint error);
    [LibraryImport("libgobject-2.0.so.0", EntryPoint = "g_object_ref")] private static partial nint Ref(nint value);
    [LibraryImport("libgobject-2.0.so.0", EntryPoint = "g_object_unref")] private static partial void Unref(nint value);
}
