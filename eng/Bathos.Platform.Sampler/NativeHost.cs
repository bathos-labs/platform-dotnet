using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Bathos.Platform.Sampler;

// Development-only native host. The library itself never owns an application or its event loop.
internal sealed partial class NativeHost : INativeDispatcher, IDisposable
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    private const string SystemLibrary = "/usr/lib/libSystem.B.dylib";
    private readonly nint _appKit;
    private readonly nint _system;
    private readonly nint _queue;
    private readonly nint _pool;
    private readonly nint _window;
    private readonly nint _application;

    public NativeHost()
    {
        VerifyAccess();
        _appKit = NativeLibrary.Load("/System/Library/Frameworks/AppKit.framework/AppKit");
        _system = NativeLibrary.Load(SystemLibrary);
        // dispatch_get_main_queue is a C macro returning the address of this exported queue.
        _queue = NativeLibrary.GetExport(_system, "_dispatch_main_q");
        _pool = Send(Send(GetClass("NSAutoreleasePool"), Selector("alloc")), Selector("init"));
        _application = Send(GetClass("NSApplication"), Selector("sharedApplication"));
        SetPolicy(_application, Selector("setActivationPolicy:"), 0);
        _window = InitWindow(Send(GetClass("NSWindow"), Selector("alloc")), Selector("initWithContentRect:styleMask:backing:defer:"),
            new Rect { X = 100, Y = 100, Width = 480, Height = 240 }, 1, 2, 0);
        if (_window == 0) throw new InvalidOperationException("Could not create the manual validation window.");
        SendObject(_window, Selector("setBackgroundColor:"), Send(GetClass("NSColor"), Selector("redColor")));
        SendObject(_window, Selector("setTitle:"), String(GetClass("NSString"), Selector("stringWithUTF8String:"), "Bathos sampler — select this red window"));
        SendObject(_window, Selector("makeKeyAndOrderFront:"), 0);
        Activate(_application, Selector("activateIgnoringOtherApps:"), 1);
    }

    public void VerifyAccess()
    {
        if (IsMainThread() == 0) throw new InvalidOperationException("The native main thread is required.");
    }

    public unsafe void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var handle = GCHandle.Alloc(action);
        Dispatch(_queue, GCHandle.ToIntPtr(handle), &Invoke);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Invoke(nint context)
    {
        var handle = GCHandle.FromIntPtr(context);
        try
        {
            if (handle.Target is not Action action) throw new InvalidOperationException("Missing dispatch action.");
            action();
        }
        // A broken dispatcher callback cannot be allowed to unwind into native code.
        catch (Exception error) { Environment.FailFast("Native harness callback failed.", error); }
        finally { handle.Free(); }
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

    public void Run() { VerifyAccess(); SendVoid(_application, Selector("run")); }
    public void Stop()
    {
        VerifyAccess();
        SendObject(_application, Selector("stop:"), 0);
        // stop: from a dispatch callback needs an event to wake AppKit's event wait.
        // This is shutdown signaling, not a polling or nested event loop.
        var wake = OtherEvent(GetClass("NSEvent"),
            Selector("otherEventWithType:location:modifierFlags:timestamp:windowNumber:context:subtype:data1:data2:"),
            15, default, 0, 0, 0, 0, 0, 0, 0);
        if (wake == 0) throw new InvalidOperationException("Could not signal AppKit shutdown.");
        PostEvent(_application, Selector("postEvent:atStart:"), wake, 1);
    }
    public void Dispose()
    {
        VerifyAccess();
        SendObject(_window, Selector("orderOut:"), 0);
        SendVoid(_window, Selector("release"));
        SendVoid(_pool, Selector("drain"));
        NativeLibrary.Free(_system);
        NativeLibrary.Free(_appKit);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public double X; public double Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public double X; public double Y; public double Width; public double Height; }
    [LibraryImport(SystemLibrary, EntryPoint = "pthread_main_np")]
    private static partial int IsMainThread();
    [LibraryImport(SystemLibrary, EntryPoint = "dispatch_async_f")]
    private static unsafe partial void Dispatch(nint queue, nint context, delegate* unmanaged[Cdecl]<nint, void> callback);
    [LibraryImport(ObjC, EntryPoint = "objc_getClass", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint GetClass(string name);
    [LibraryImport(ObjC, EntryPoint = "sel_registerName", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint Selector(string name);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial nint Send(nint receiver, nint selector);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial void SendVoid(nint receiver, nint selector);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial void SendObject(nint receiver, nint selector, nint value);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial byte SetPolicy(nint receiver, nint selector, nint policy);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial void Activate(nint receiver, nint selector, byte active);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint String(nint receiver, nint selector, string value);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial nint OtherEvent(nint receiver, nint selector, nuint type, Point point, nuint flags,
        double timestamp, nint window, nint context, short subtype, nint data1, nint data2);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial void PostEvent(nint receiver, nint selector, nint value, byte atStart);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial nint InitWindow(nint receiver, nint selector, Rect rect, nuint style, nuint backing, byte defer);
}
