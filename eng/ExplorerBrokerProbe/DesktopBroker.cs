using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ExplorerBrokerProbe;

// Disposable translation of Microsoft's Execute In Explorer sample's exact desktop-view route.
internal sealed unsafe class DesktopBroker : IDisposable
{
    private readonly List<object> _objects = [];
    private readonly List<nint> _pointers = [];
    private readonly dynamic _shell;
    internal int ProcessId { get; }

    internal DesktopBroker()
    {
        try
        {
            var clsid = new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39"); // ShellWindows
            var dispatch = new Guid("00020400-0000-0000-C000-000000000046");
            Marshal.ThrowExceptionForHR(Native.CoCreateInstance(ref clsid, 0, 4, ref dispatch, out var windowsPointer));
            _pointers.Add(windowsPointer);
            dynamic windows = Own(Marshal.GetObjectForIUnknown(windowsPointer));
            object location = null!; object root = null!; int hwnd;
            object desktop = Own(windows.FindWindowSW(ref location, ref root, 8, out hwnd, 1));
            if (desktop is null) throw new InvalidOperationException("Desktop Explorer unavailable before submission");
            Native.GetWindowThreadProcessId((nint)hwnd, out var pid);
            Native.GetWindowThreadProcessId(Native.GetShellWindow(), out var shellPid);
            if (pid == 0 || pid != shellPid) throw new InvalidOperationException("Desktop window identity mismatch");
            ProcessId = checked((int)pid);
            using var broker = Process.GetProcessById(ProcessId);
            using var caller = Process.GetCurrentProcess();
            if (broker.SessionId != caller.SessionId || Native.Identity(broker) != Native.Identity(caller))
                throw new InvalidOperationException("Token/user/session context mismatch: refusing implicit context change");
            Native.RequireSupportedToken(caller); Native.RequireSupportedToken(broker);
            Native.RequireNoImpersonation();
            var unknown = Marshal.GetIUnknownForObject(desktop); _pointers.Add(unknown);
            var serviceProvider = new Guid("6D5140C1-7436-11CE-8034-00AA006009FA");
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(unknown, in serviceProvider, out var provider)); _pointers.Add(provider);
            var service = new Guid("4C96BE40-915C-11CF-99D3-00AA004AE837"); // SID_STopLevelBrowser
            var browserId = new Guid("000214E2-0000-0000-C000-000000000046");
            nint browser;
            Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, Guid*, Guid*, nint*, int>)(*(nint**)provider)[3])(provider, &service, &browserId, &browser)); _pointers.Add(browser);
            nint view;
            Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, nint*, int>)(*(nint**)browser)[15])(browser, &view)); _pointers.Add(view);
            nint background;
            Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, uint, Guid*, nint*, int>)(*(nint**)view)[15])(view, 0, &dispatch, &background)); _pointers.Add(background);
            dynamic folderView = Own(Marshal.GetObjectForIUnknown(background));
            _shell = Own(folderView.Application);
        }
        catch { Dispose(); throw; }
    }

    private object Own(object value) { _objects.Add(value); return value; }
    internal void Execute(string executable, string arguments) => _shell.ShellExecute(executable, arguments, Path.GetDirectoryName(executable), "open", 0);
    public void Dispose()
    {
        // RCWs can alias: release each distinct identity once.
        foreach (var value in _objects.Distinct(ReferenceEqualityComparer.Instance).Reverse()) Marshal.FinalReleaseComObject(value);
        _objects.Clear();
        foreach (var pointer in Enumerable.Reverse(_pointers)) Marshal.Release(pointer);
        _pointers.Clear();
    }
}
