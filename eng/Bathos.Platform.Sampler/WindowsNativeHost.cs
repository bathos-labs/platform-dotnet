using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Bathos.Platform.Sampler;

// Engineering-only Win32 host. Tests and production code never drive the interactive picker automatically.
internal sealed partial class WindowsNativeHost : ISamplerHost
{
    private static readonly ConcurrentDictionary<nint, WindowsNativeHost> Hosts = new();
    private readonly ConcurrentQueue<Action> _pending = new();
    private readonly List<nint> _buttons = [];
    private readonly uint _thread = GetCurrentThreadId();
    private readonly string _className = "BathosSamplerHarness_" + Guid.NewGuid().ToString("N");
    private readonly nint _module;
    private readonly nint _window;
    private readonly nint _status;
    private readonly nint _sampleLabel;
    private readonly nint _redBrush;
    private nint _sampleBrush;
    private Action? _finish;
    private bool _busy;
    private bool _disposed;

    public unsafe WindowsNativeHost()
    {
        _module = GetModuleHandle(null);
        fixed (char* name = _className)
        {
            var type = new WindowClass
            {
                Size = (uint)sizeof(WindowClass),
                Module = _module,
                Name = (nint)name,
                Background = 6,
                Procedure = (nint)(delegate* unmanaged[Stdcall]<nint, uint, nuint, nint, nint>)&Procedure,
                Cursor = LoadCursor(0, 32512)
            };
            if (RegisterClass(ref type) == 0) throw Failure();
        }
        _window = CreateWindow(0, _className, "Bathos screen sampler validation", 0x00CF0000,
            100, 100, 760, 680, 0, 0, _module, 0);
        if (_window == 0) { UnregisterClass(_className, _module); throw Failure(); }
        Hosts.TryAdd(_window, this);
        try
        {
            _redBrush = CreateSolidBrush(0x0000FF);
            if (_redBrush == 0) throw Failure();
            AddControl("STATIC", "Move this window before sampling. Choose a test; nothing starts automatically.\r\n" +
                "Click a pixel to select it, or press Escape to cancel.\r\n" +
                "The Windows picker shows a frozen desktop snapshot.", 24, 20, 690, 90);
            _status = AddControl("STATIC", "No tests run yet.", 24, 330, 430, 210);
            AddControl("STATIC", "Red target: #FF0000", 480, 295, 210, 25);
            _sampleLabel = AddControl("STATIC", "Last sample: none", 480, 460, 210, 60);
            ShowWindow(_window, 5);
        }
        catch { Dispose(); throw; }
    }

    public void ConfigureTests(Action<int> sample, Action finish)
    {
        VerifyAccess();
        _finish = finish;
        string[] titles = ["1. Sample the red target", "2. Sample outside this window", "3. Test Escape cancellation", "4. Test caller retirement"];
        for (var i = 0; i < titles.Length; i++) _buttons.Add(AddControl("BUTTON", titles[i], 24, 130 + i * 45, 430, 36, i + 1));
        _buttons.Add(AddControl("BUTTON", "Finish", 560, 560, 130, 32, 5));
        _sample = sample;
    }

    private Action<int>? _sample;

    public void SetStatus(string text) { VerifyAccess(); SetWindowText(_status, text.Replace("\n", "\r\n", StringComparison.Ordinal)); }
    public void ShowSample(ScreenColor color)
    {
        VerifyAccess();
        var brush = CreateSolidBrush((uint)(color.R | color.G << 8 | color.B << 16));
        if (brush == 0) throw Failure();
        if (_sampleBrush != 0) DeleteObject(_sampleBrush);
        _sampleBrush = brush;
        SetWindowText(_sampleLabel, $"Last sample: RGB\r\n#{color.R:X2}{color.G:X2}{color.B:X2}");
        InvalidateRect(_window, 0, false);
    }
    public void SetBusy(bool busy)
    {
        VerifyAccess(); _busy = busy;
        foreach (var button in _buttons) EnableWindow(button, !busy);
    }
    public void VerifyAccess()
    {
        if (GetCurrentThreadId() != _thread) throw new InvalidOperationException("The native UI thread is required.");
    }
    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        ObjectDisposedException.ThrowIf(_disposed, this);
        _pending.Enqueue(action);
        if (!PostMessage(_window, 0x8001, 0, 0)) throw Failure();
    }
    public Task<T> InvokeAsync<T>(Func<Task<T>> operation)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(async () =>
        {
            try { completion.SetResult(await operation().ConfigureAwait(false)); }
            catch (Exception error) { completion.SetException(error); }
        });
        return completion.Task;
    }
    public void Run()
    {
        VerifyAccess();
        int result;
        while ((result = GetMessage(out var message, 0, 0, 0)) > 0) { TranslateMessage(ref message); DispatchMessage(ref message); }
        if (result < 0) throw Failure();
    }
    public void Stop() { VerifyAccess(); PostQuitMessage(0); }

    private nint AddControl(string type, string title, int x, int y, int width, int height, int id = 0)
    {
        var control = CreateWindow(0, type, title, 0x50000000, x, y, width, height, _window, id, _module, 0);
        return control != 0 ? control : throw Failure();
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint Procedure(nint window, uint message, nuint wParam, nint lParam)
    {
        if (!Hosts.TryGetValue(window, out var host)) return DefaultProcedure(window, message, wParam, lParam);
        try
        {
            switch (message)
            {
                case 0x8001:
                    if (host._pending.TryDequeue(out var action)) action();
                    return 0;
                case 0x0111 when (wParam >> 16) == 0 && !host._busy:
                    var id = (int)(wParam & 0xffff);
                    if (id is >= 1 and <= 4) host._sample?.Invoke(id - 1);
                    else if (id == 5) host._finish?.Invoke();
                    return 0;
                case 0x0010:
                    if (!host._busy) host._finish?.Invoke();
                    return 0;
                case 0x000F:
                    var dc = BeginPaint(window, out var paint);
                    try
                    {
                        var target = new Rect { Left = 480, Top = 150, Right = 690, Bottom = 290 };
                        FillRect(dc, ref target, host._redBrush);
                        if (host._sampleBrush != 0)
                        {
                            var preview = new Rect { Left = 480, Top = 350, Right = 690, Bottom = 450 };
                            FillRect(dc, ref preview, host._sampleBrush);
                        }
                    }
                    finally { EndPaint(window, ref paint); }
                    return 0;
                default: return DefaultProcedure(window, message, wParam, lParam);
            }
        }
        catch (Exception error) { Environment.FailFast("Windows harness callback failed.", error); return 0; }
    }

    public void Dispose()
    {
        VerifyAccess();
        if (_disposed) return;
        if (!_pending.IsEmpty) throw new InvalidOperationException("The harness still owns pending dispatch work.");
        _disposed = true;
        Hosts.TryRemove(_window, out _);
        DestroyWindow(_window);
        UnregisterClass(_className, _module);
        if (_redBrush != 0) DeleteObject(_redBrush);
        if (_sampleBrush != 0) DeleteObject(_sampleBrush);
    }

    private static Win32Exception Failure() => new(Marshal.GetLastPInvokeError());
    [StructLayout(LayoutKind.Sequential)]
    private struct WindowClass
    {
        public uint Size, Style;
        public nint Procedure;
        public int ClassBytes, WindowBytes;
        public nint Module, Icon, Cursor, Background, Menu, Name, SmallIcon;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Message { public nint Window; public uint Id; public nuint WParam; public nint LParam; public uint Time; public int X, Y; public uint Private; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct Paint { public nint DC; public int Erase; public Rect Area; public int Restore, Update; public fixed byte Reserved[32]; }

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();
    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial nint GetModuleHandle(string? name);
    [LibraryImport("user32.dll", EntryPoint = "RegisterClassExW", SetLastError = true)]
    private static partial ushort RegisterClass(ref WindowClass type);
    [LibraryImport("user32.dll", EntryPoint = "UnregisterClassW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterClass(string name, nint module);
    [LibraryImport("user32.dll", EntryPoint = "LoadCursorW")]
    private static partial nint LoadCursor(nint module, nint resource);
    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial nint CreateWindow(uint extendedStyle, string type, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint module, nint parameter);
    [LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")]
    private static partial nint DefaultProcedure(nint window, uint message, nuint wParam, nint lParam);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(nint window, int command);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(nint window);
    [LibraryImport("user32.dll", EntryPoint = "SetWindowTextW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowText(nint window, string text);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnableWindow(nint window, [MarshalAs(UnmanagedType.Bool)] bool enable);
    [LibraryImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessage(nint window, uint message, nuint wParam, nint lParam);
    [LibraryImport("user32.dll", EntryPoint = "GetMessageW", SetLastError = true)]
    private static partial int GetMessage(out Message message, nint window, uint first, uint last);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TranslateMessage(ref Message message);
    [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")]
    private static partial nint DispatchMessage(ref Message message);
    [LibraryImport("user32.dll")]
    private static partial void PostQuitMessage(int code);
    [LibraryImport("gdi32.dll", SetLastError = true)]
    private static partial nint CreateSolidBrush(uint color);
    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(nint value);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool InvalidateRect(nint window, nint rectangle, [MarshalAs(UnmanagedType.Bool)] bool erase);
    [LibraryImport("user32.dll")]
    private static partial nint BeginPaint(nint window, out Paint paint);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EndPaint(nint window, ref Paint paint);
    [LibraryImport("user32.dll")]
    private static partial int FillRect(nint dc, ref Rect rectangle, nint brush);
}
