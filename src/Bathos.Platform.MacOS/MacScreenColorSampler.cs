using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Bathos.Platform.MacOS;

/// <summary>AppKit owns screen access, cross-display selection, magnification, and Escape cancellation.</summary>
[SupportedOSPlatform("macos10.15")]
internal sealed partial class MacScreenColorSampler(INativeDispatcher dispatcher) : IScreenColorSampler
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    private static readonly SelectionCallback Callback = Selected;
    private static bool _active;

    public Task<ScreenColor?> SampleAsync()
    {
        dispatcher.VerifyAccess();
        if (_active) throw new InvalidOperationException("A screen color picker is already open.");
        var request = new Request(dispatcher);
        _active = true;
        try { request.Show(); }
        catch { request.Dispose(); _active = false; throw; }
        return request.Completion.Task;
    }

    // A normal Objective-C stack block is copied by AppKit for the escaping completion handler.
    // Its captured GCHandle and descriptor remain alive until the native callback has returned and
    // the sampler is released. No managed delegate, memory, or exception crosses an unowned lifetime.
    [StructLayout(LayoutKind.Sequential)]
    private struct Block
    {
        public nint Isa;
        public int Flags;
        public int Reserved;
        public nint Invoke;
        public nint Descriptor;
        public nint Context;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct BlockDescriptor { public nuint Reserved; public nuint Size; }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SelectionCallback(nint block, nint color);

    private static void Selected(nint block, nint color)
    {
        var handle = Marshal.PtrToStructure<Block>(block).Context;
        if (GCHandle.FromIntPtr(handle).Target is not Request request) return;
        ScreenColor? result = null;
        Exception? failure = null;
        try
        {
            if (color != 0)
            {
                var space = Send(GetClass("NSColorSpace"), Selector("sRGBColorSpace"));
                var rgb = SendObject(color, Selector("colorUsingColorSpace:"), space);
                if (rgb == 0) throw new InvalidOperationException("The sampled color could not be converted to sRGB.");
                result = new ScreenColor(Component(rgb, "redComponent"), Component(rgb, "greenComponent"), Component(rgb, "blueComponent"));
            }
        }
        catch (Exception error) { failure = error; }
        // AppKit may still be releasing its copy of the block after invoking it. Finish on the next UI turn.
        request.Dispatcher.Post(() =>
        {
            request.Dispose();
            _active = false;
            if (failure is null) request.Completion.TrySetResult(result);
            else request.Completion.TrySetException(failure);
        });
    }
    private static byte Component(nint color, string selector)
    {
        var value = SendDouble(color, Selector(selector));
        if (!double.IsFinite(value)) throw new InvalidOperationException("The screen picker returned an invalid color.");
        return (byte)Math.Round(Math.Clamp(value, 0, 1) * 255, MidpointRounding.AwayFromZero);
    }
    private sealed class Request(INativeDispatcher dispatcher) : IDisposable
    {
        public INativeDispatcher Dispatcher { get; } = dispatcher;
        public TaskCompletionSource<ScreenColor?> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private nint _appKit;
        private nint _system;
        private nint _sampler;
        private nint _block;
        private nint _descriptor;
        private GCHandle _handle;
        public void Show()
        {
            _appKit = NativeLibrary.Load("/System/Library/Frameworks/AppKit.framework/AppKit");
            _system = NativeLibrary.Load("/usr/lib/libSystem.B.dylib");
            var type = GetClass("NSColorSampler");
            if (type == 0) throw new InvalidOperationException("The system screen color picker is unavailable.");
            _sampler = Send(Send(type, Selector("alloc")), Selector("init"));
            if (_sampler == 0) throw new InvalidOperationException("The system screen color picker could not be opened.");
            _handle = GCHandle.Alloc(this);
            _descriptor = Marshal.AllocHGlobal(Marshal.SizeOf<BlockDescriptor>());
            Marshal.StructureToPtr(new BlockDescriptor { Size = (nuint)Marshal.SizeOf<Block>() }, _descriptor, false);
            _block = Marshal.AllocHGlobal(Marshal.SizeOf<Block>());
            Marshal.StructureToPtr(new Block
            {
                Isa = NativeLibrary.GetExport(_system, "_NSConcreteStackBlock"),
                Invoke = Marshal.GetFunctionPointerForDelegate(Callback),
                Descriptor = _descriptor,
                Context = GCHandle.ToIntPtr(_handle)
            }, _block, false);
            SendVoidObject(_sampler, Selector("showSamplerWithSelectionHandler:"), _block);
        }
        public void Dispose()
        {
            if (_sampler != 0) { SendVoid(_sampler, Selector("release")); _sampler = 0; }
            if (_block != 0) { Marshal.FreeHGlobal(_block); _block = 0; }
            if (_descriptor != 0) { Marshal.FreeHGlobal(_descriptor); _descriptor = 0; }
            if (_handle.IsAllocated) _handle.Free();
            if (_system != 0) { NativeLibrary.Free(_system); _system = 0; }
            if (_appKit != 0) { NativeLibrary.Free(_appKit); _appKit = 0; }
        }
    }
    [LibraryImport(ObjC, EntryPoint = "objc_getClass", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint GetClass(string name);
    [LibraryImport(ObjC, EntryPoint = "sel_registerName", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint Selector(string name);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial nint Send(nint receiver, nint selector);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial nint SendObject(nint receiver, nint selector, nint value);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial void SendVoidObject(nint receiver, nint selector, nint value);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial void SendVoid(nint receiver, nint selector);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial double SendDouble(nint receiver, nint selector);
}
