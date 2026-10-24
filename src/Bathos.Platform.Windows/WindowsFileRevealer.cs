using System.Runtime.InteropServices;
using Bathos.Platform.Desktop;

namespace Bathos.Platform.Windows;

/// <summary>Selects an entry in Explorer using the Shell API on an owned STA worker.</summary>
public sealed partial class WindowsFileRevealer : IFileRevealer
{
    /// <include file="../Bathos.Platform/Documentation/Contracts.xml" path="contracts/RevealAsync/*" />
    public Task RevealAsync(string path, CancellationToken cancellationToken = default)
    {
        DesktopPaths.RequireEntry(path);
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows file reveal requires Windows.");
        cancellationToken.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var worker = new Thread(() =>
        {
            try { Reveal(path, new NativeShell(), cancellationToken); completion.SetResult(); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { completion.SetCanceled(cancellationToken); }
            catch (Exception error) { completion.SetException(error); }
        })
        { IsBackground = true, Name = "Bathos file reveal" };
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
        return completion.Task;
    }

    internal static void Reveal(string path, IShell shell, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Marshal.ThrowExceptionForHR(shell.Initialize()); // S_FALSE still requires uninitialization.
        try
        {
            nint item = 0;
            try
            {
                Marshal.ThrowExceptionForHR(shell.Parse(path, out item));
                if (item == 0) throw new IOException("The Shell returned no filesystem item.");
                token.ThrowIfCancellationRequested();
                Marshal.ThrowExceptionForHR(shell.Select(item));
            }
            finally { if (item != 0) shell.Free(item); }
        }
        finally { shell.Uninitialize(); }
    }

    // Keep the real ownership sequence testable without launching Explorer or substituting the STA worker.
    internal interface IShell
    {
        int Initialize();
        int Parse(string path, out nint item);
        int Select(nint item);
        void Free(nint item);
        void Uninitialize();
    }

    private sealed class NativeShell : IShell
    {
        public int Initialize() => InitializeCom(0, 6); // STA and DISABLE_OLE1DDE.
        public int Parse(string path, out nint item) => ParseDisplayName(path, 0, out item, 0, out _);
        public int Select(nint item) => OpenFolderAndSelectItems(item, 0, 0, 0);
        public void Free(nint item) => Marshal.FreeCoTaskMem(item);
        public void Uninitialize() => UninitializeCom();
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
