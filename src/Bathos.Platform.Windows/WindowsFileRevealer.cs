using System.Runtime.InteropServices;
using Bathos.Platform.Desktop;

namespace Bathos.Platform.Windows;

/// <summary>Selects an entry in Explorer using the Shell API on an owned STA worker.</summary>
public sealed partial class WindowsFileRevealer : IFileRevealer
{
    public Task RevealAsync(string path, CancellationToken cancellationToken = default)
    {
        DesktopPaths.RequireEntry(path);
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows file reveal requires Windows.");
        cancellationToken.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var worker = new Thread(() =>
        {
            try { Reveal(path, cancellationToken); completion.SetResult(); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { completion.SetCanceled(cancellationToken); }
            catch (Exception error) { completion.SetException(error); }
        })
        { IsBackground = true, Name = "Bathos file reveal" };
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
        return completion.Task;
    }

    private static void Reveal(string path, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Marshal.ThrowExceptionForHR(InitializeCom(0, 6)); // STA and DISABLE_OLE1DDE; S_FALSE still requires uninitialization.
        try
        {
            Marshal.ThrowExceptionForHR(ParseDisplayName(path, 0, out var item, 0, out _));
            if (item == 0) throw new IOException("The Shell returned no filesystem item.");
            try
            {
                token.ThrowIfCancellationRequested();
                Marshal.ThrowExceptionForHR(OpenFolderAndSelectItems(item, 0, 0, 0));
            }
            finally { Marshal.FreeCoTaskMem(item); }
        }
        finally { UninitializeCom(); }
    }

    [LibraryImport("ole32.dll", EntryPoint = "CoInitializeEx")]
    private static partial int InitializeCom(nint reserved, uint flags);
    [LibraryImport("ole32.dll", EntryPoint = "CoUninitialize")]
    private static partial void UninitializeCom();
    [LibraryImport("shell32.dll", EntryPoint = "SHParseDisplayName", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int ParseDisplayName(string path, nint context, out nint item, uint attributes, out uint returnedAttributes);
    [LibraryImport("shell32.dll", EntryPoint = "SHOpenFolderAndSelectItems")]
    private static partial int OpenFolderAndSelectItems(nint item, uint count, nint children, uint flags);
}
