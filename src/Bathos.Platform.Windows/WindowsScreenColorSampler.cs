using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Bathos.Platform.Windows;

/// <summary>A frozen GDI desktop snapshot under a native selection window. The host owns the message loop.</summary>
[SupportedOSPlatform("windows")]
internal sealed partial class WindowsScreenColorSampler(INativeDispatcher dispatcher) : IScreenColorSampler
{
    private static int _active;
    private static readonly ConcurrentDictionary<nint, Request> Requests = new();

    public Task<ScreenColor?> SampleAsync()
    {
        dispatcher.VerifyAccess();
        if (Interlocked.CompareExchange(ref _active, 1, 0) != 0)
            throw new InvalidOperationException("A screen color picker is already open.");
        Request? request = null;
        try { request = new Request(dispatcher); request.Show(); return request.Completion.Task; }
        catch { request?.Dispose(); Interlocked.Exchange(ref _active, 0); throw; }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint WindowProcedure(nint window, uint message, nuint wParam, nint lParam)
    {
        if (!Requests.TryGetValue(window, out var request)) return DefaultProcedure(window, message, wParam, lParam);
        try { return request.Handle(message, wParam, lParam); }
        catch (Exception error)
        {
            // No managed exception may unwind through user32. A broken dispatcher cannot safely retire native UI.
            try { request.Finish(null, error); }
            catch (Exception dispatchError) { Environment.FailFast("The screen sampler dispatcher failed.", dispatchError); }
            return 0;
        }
    }

    private sealed class Request(INativeDispatcher dispatcher) : IDisposable
    {
        public TaskCompletionSource<ScreenColor?> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly string _className = "BathosScreenSampler_" + Guid.NewGuid().ToString("N");
        private nint _module;
        private nint _window;
        private nint _previousForeground;
        private nint _snapshot;
        private nint _bitmap;
        private nint _previousBitmap;
        private bool _registered;
        private bool _shown;
        private bool _finishing;
        private bool _disposed;
        private int _left;
        private int _top;
        private int _width;
        private int _height;

        public unsafe void Show()
        {
            // Keep host-wide DPI policy untouched. Windows remembers this context for the new window's callbacks.
            var previousDpi = SetDpiContext(-4); // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2
            if (previousDpi == 0) throw NativeFailure("Could not establish physical screen coordinates.");
            try
            {
                _left = GetMetric(76); _top = GetMetric(77); // virtual desktop, including negative monitor origins
                _width = GetMetric(78); _height = GetMetric(79);
                if (_width <= 0 || _height <= 0) throw new InvalidOperationException("No desktop is available for sampling.");
                var screen = GetDC(0);
                if (screen == 0) throw NativeFailure("Could not access the desktop.");
                int released;
                try
                {
                    _snapshot = CreateCompatibleDC(screen);
                    if (_snapshot == 0) throw NativeFailure("Could not create the screen capture context.");
                    _bitmap = CreateCompatibleBitmap(screen, _width, _height);
                    if (_bitmap == 0) throw NativeFailure("Could not allocate the desktop snapshot.");
                    _previousBitmap = SelectObject(_snapshot, _bitmap);
                    if (_previousBitmap == 0 || _previousBitmap == -1) throw NativeFailure("Could not select the desktop snapshot.");
                    if (!CopyPixels(_snapshot, 0, 0, _width, _height, screen, _left, _top, 0x40CC0020)) // SRCCOPY | CAPTUREBLT
                        throw NativeFailure("Could not capture the desktop.");
                }
                finally { released = ReleaseDC(0, screen); }
                if (released == 0) throw NativeFailure("Could not release the desktop capture context.");

                _module = GetModuleHandle(null);
                fixed (char* name = _className)
                {
                    var type = new WindowClass
                    {
                        Size = (uint)sizeof(WindowClass),
                        Module = _module,
                        Name = (nint)name,
                        Procedure = (nint)(delegate* unmanaged[Stdcall]<nint, uint, nuint, nint, nint>)&WindowProcedure,
                        Cursor = LoadCursor(0, 32515) // IDC_CROSS
                    };
                    if (type.Cursor == 0 || RegisterClass(ref type) == 0) throw NativeFailure("Could not register the screen picker.");
                }
                _registered = true;
                _previousForeground = GetForegroundWindow();
                _window = CreateWindow(0x88, _className, "Select a color; Escape cancels", 0x80000000, // TOPMOST | TOOLWINDOW; POPUP
                    _left, _top, _width, _height, 0, 0, _module, 0);
                if (_window == 0) throw NativeFailure("Could not create the screen picker.");
                if (!Requests.TryAdd(_window, this)) throw new InvalidOperationException("The screen picker window is already registered.");
                ShowWindow(_window, 5);
                SetForegroundWindow(_window);
                if (GetForegroundWindow() != _window) throw new InvalidOperationException("The screen picker could not receive foreground input.");
                SetFocus(_window);
                _shown = true;
            }
            finally { SetDpiContext(previousDpi); }
        }

        public nint Handle(uint message, nuint wParam, nint lParam)
        {
            switch (message)
            {
                case 0x000F: // WM_PAINT: the opaque snapshot intercepts clicks without changing the sampled pixels.
                    var dc = BeginPaint(_window, out var paint);
                    if (dc == 0) throw NativeFailure("Could not paint the screen picker.");
                    try
                    {
                        if (!CopyPixels(dc, 0, 0, _width, _height, _snapshot, 0, 0, 0x00CC0020))
                            throw NativeFailure("Could not display the desktop snapshot.");
                    }
                    finally { EndPaint(_window, ref paint); }
                    return 0;
                case 0x0014: return 1; // WM_ERASEBKGND: WM_PAINT covers the client area.
                case 0x0202: // WM_LBUTTONUP: consume the complete click before dismissing the overlay.
                    if (!_finishing)
                    {
                        // Use the queued click position, not a pointer that may already have moved. Without
                        // mouse capture these client coordinates are inside the overlay and therefore nonnegative.
                        var x = (int)(lParam & 0xffff); var y = (int)((lParam >> 16) & 0xffff);
                        if (x < 0 || y < 0 || x >= _width || y >= _height)
                            throw new InvalidOperationException("The selected pixel is outside the captured desktop.");
                        var color = GetPixel(_snapshot, x, y);
                        if (color == uint.MaxValue) throw new InvalidOperationException("The selected pixel could not be read.");
                        Finish(new ScreenColor((byte)color, (byte)(color >> 8), (byte)(color >> 16)), null);
                    }
                    return 0;
                case 0x0100 when wParam == 27: // Escape
                case 0x0205: // right-click
                case 0x0010: // close
                    Finish(null, null); return 0;
                case 0x0006 when (wParam & 0xffff) == 0 && _shown: // focus transferred away
                    Finish(null, null); return 0;
                case 0x007E: // display configuration changed
                case 0x02E0: // DPI changed
                    Finish(null, new InvalidOperationException("The display configuration changed during color selection.")); return 0;
                case 0x0082: // externally destroyed: relinquish the HWND before Windows can reuse its value.
                    var window = _window;
                    Requests.TryRemove(window, out _);
                    _window = 0;
                    Finish(null, new InvalidOperationException("The screen picker closed before selection completed."));
                    return DefaultProcedure(window, message, wParam, lParam);
                default: return DefaultProcedure(_window, message, wParam, lParam);
            }
        }

        public void Finish(ScreenColor? color, Exception? error)
        {
            if (_finishing) return;
            _finishing = true;
            dispatcher.Post(() =>
            {
                if (_disposed) return; // Opening may already have failed after a native callback scheduled retirement.
                Dispose(); // Native callback has unwound before its window and class are released.
                Interlocked.Exchange(ref _active, 0);
                if (error is null) Completion.TrySetResult(color);
                else Completion.TrySetException(error);
            });
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _finishing = true;
            if (_window != 0)
            {
                var restoreFocus = GetForegroundWindow() == _window;
                Requests.TryRemove(_window, out _);
                DestroyWindow(_window);
                _window = 0;
                if (restoreFocus && _previousForeground != 0) SetForegroundWindow(_previousForeground);
            }
            if (_registered) { UnregisterClass(_className, _module); _registered = false; }
            if (_snapshot != 0)
            {
                if (_previousBitmap != 0 && _previousBitmap != -1) SelectObject(_snapshot, _previousBitmap);
                DeleteDC(_snapshot); _snapshot = 0;
            }
            if (_bitmap != 0) { DeleteObject(_bitmap); _bitmap = 0; }
        }
    }

    private static Win32Exception NativeFailure(string message) => new(Marshal.GetLastPInvokeError(), message);

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowClass
    {
        public uint Size, Style;
        public nint Procedure;
        public int ClassBytes, WindowBytes;
        public nint Module, Icon, Cursor, Background, Menu, Name, SmallIcon;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct Paint
    {
        public nint DC;
        public int Erase;
        public Rect Area;
        public int Restore, Update;
        public fixed byte Reserved[32];
    }

    [LibraryImport("user32.dll", EntryPoint = "SetThreadDpiAwarenessContext", SetLastError = true)]
    private static partial nint SetDpiContext(nint context);
    [LibraryImport("user32.dll", EntryPoint = "GetSystemMetrics")]
    private static partial int GetMetric(int index);
    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint GetDC(nint window);
    [LibraryImport("user32.dll")]
    private static partial int ReleaseDC(nint window, nint dc);
    [LibraryImport("gdi32.dll", SetLastError = true)]
    private static partial nint CreateCompatibleDC(nint dc);
    [LibraryImport("gdi32.dll", SetLastError = true)]
    private static partial nint CreateCompatibleBitmap(nint dc, int width, int height);
    [LibraryImport("gdi32.dll", SetLastError = true)]
    private static partial nint SelectObject(nint dc, nint value);
    [LibraryImport("gdi32.dll", EntryPoint = "BitBlt", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CopyPixels(nint destination, int x, int y, int width, int height, nint source, int sourceX, int sourceY, uint operation);
    [LibraryImport("gdi32.dll")]
    private static partial uint GetPixel(nint dc, int x, int y);
    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteDC(nint dc);
    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(nint value);
    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial nint GetModuleHandle(string? name);
    [LibraryImport("user32.dll", EntryPoint = "RegisterClassExW", SetLastError = true)]
    private static partial ushort RegisterClass(ref WindowClass type);
    [LibraryImport("user32.dll", EntryPoint = "UnregisterClassW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterClass(string name, nint module);
    [LibraryImport("user32.dll", EntryPoint = "LoadCursorW", SetLastError = true)]
    private static partial nint LoadCursor(nint module, nint resource);
    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial nint CreateWindow(uint extendedStyle, string type, string title, uint style, int x, int y, int width, int height,
        nint parent, nint menu, nint module, nint parameter);
    [LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")]
    private static partial nint DefaultProcedure(nint window, uint message, nuint wParam, nint lParam);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(nint window, int command);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(nint window);
    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint window);
    [LibraryImport("user32.dll")]
    private static partial nint SetFocus(nint window);
    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint BeginPaint(nint window, out Paint paint);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EndPaint(nint window, ref Paint paint);
}
