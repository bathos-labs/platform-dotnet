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
    private readonly nint _actionTarget;
    private readonly nint _status;
    private readonly List<nint> _buttons = [];
    private static readonly Dictionary<nint, Action> Actions = [];

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
            new Rect { X = 100, Y = 100, Width = 720, Height = 600 }, 1, 2, 0);
        if (_window == 0) throw new InvalidOperationException("Could not create the manual validation window.");
        SendObject(_window, Selector("setTitle:"), String(GetClass("NSString"), Selector("stringWithUTF8String:"), "Bathos screen sampler validation"));
        _actionTarget = CreateActionTarget();
        AddLabel("Move this window before sampling. Choose any test below; nothing starts automatically.\n" +
            "Select a color with a click, or cancel with Escape. Results stay here for review.\n" +
            "Repeat any test after completion. Finish closes the harness when no picker is active.",
            new Rect { X = 24, Y = 490, Width = 672, Height = 90 });
        var swatch = SendObjectResult(Send(GetClass("NSBox"), Selector("alloc")), Selector("initWithFrame:"),
            new Rect { X = 480, Y = 310, Width = 210, Height = 140 });
        SetInteger(swatch, Selector("setBoxType:"), 4); // NSBoxCustom
        SetInteger(swatch, Selector("setBorderType:"), 0);
        SendObject(swatch, Selector("setFillColor:"), Send(GetClass("NSColor"), Selector("redColor")));
        SendObject(Send(_window, Selector("contentView")), Selector("addSubview:"), swatch);
        SendVoid(swatch, Selector("release"));
        AddLabel("Red target: #FF0000", new Rect { X = 480, Y = 275, Width = 210, Height = 30 });
        _status = AddLabel("No tests run yet.", new Rect { X = 24, Y = 65, Width = 672, Height = 195 });
        SendObject(_window, Selector("makeKeyAndOrderFront:"), 0);
        Activate(_application, Selector("activateIgnoringOtherApps:"), 1);
    }

    public void ConfigureTests(Action<int> sample, Action finish)
    {
        VerifyAccess();
        string[] titles = ["1. Sample the red target", "2. Sample outside this window",
            "3. Test Escape cancellation", "4. Test caller retirement"];
        for (var index = 0; index < titles.Length; index++)
        {
            var test = index;
            AddButton(titles[index], new Rect { X = 24, Y = 410 - index * 45, Width = 430, Height = 36 }, () => sample(test));
        }
        AddButton("Finish", new Rect { X = 560, Y = 16, Width = 130, Height = 32 }, finish);
    }

    public void SetStatus(string text)
    {
        VerifyAccess();
        SendObject(_status, Selector("setStringValue:"), NativeString(text));
    }

    public void SetBusy(bool busy)
    {
        VerifyAccess();
        foreach (var button in _buttons) Activate(button, Selector("setEnabled:"), busy ? (byte)0 : (byte)1);
    }

    private nint AddLabel(string text, Rect frame)
    {
        var label = Send(Send(GetClass("NSTextField"), Selector("alloc")), Selector("init"));
        SetFrame(label, Selector("setFrame:"), frame);
        Activate(label, Selector("setEditable:"), 0);
        Activate(label, Selector("setSelectable:"), 1);
        Activate(label, Selector("setBordered:"), 0);
        Activate(label, Selector("setDrawsBackground:"), 0);
        SendObject(label, Selector("setStringValue:"), NativeString(text));
        SendObject(Send(_window, Selector("contentView")), Selector("addSubview:"), label);
        SendVoid(label, Selector("release")); // The content view retains the control.
        return label;
    }

    private void AddButton(string title, Rect frame, Action action)
    {
        var button = SendObjectResult(Send(GetClass("NSButton"), Selector("alloc")), Selector("initWithFrame:"), frame);
        SendObject(button, Selector("setTitle:"), NativeString(title));
        SetInteger(button, Selector("setBezelStyle:"), 1);
        SendObject(button, Selector("setTarget:"), _actionTarget);
        SendObject(button, Selector("setAction:"), Selector("perform:"));
        Actions.Add(button, action);
        _buttons.Add(button);
        SendObject(Send(_window, Selector("contentView")), Selector("addSubview:"), button);
        SendVoid(button, Selector("release"));
    }

    private static nint NativeString(string text) => String(GetClass("NSString"), Selector("stringWithUTF8String:"), text);

    private static unsafe nint CreateActionTarget()
    {
        const string name = "BathosSamplerActions";
        var type = GetClass(name);
        if (type == 0)
        {
            type = AllocateClass(GetClass("NSObject"), name, 0);
            if (type == 0 || AddMethod(type, Selector("perform:"), &ButtonClicked, "v@:@") == 0)
                throw new InvalidOperationException("Could not register sampler controls.");
            RegisterClass(type);
        }
        return Send(Send(type, Selector("alloc")), Selector("init"));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void ButtonClicked(nint receiver, nint selector, nint sender)
    {
        try { if (Actions.TryGetValue(sender, out var action)) action(); }
        catch (Exception error) { Environment.FailFast("Sampler control callback failed.", error); }
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
        foreach (var button in _buttons) Actions.Remove(button);
        SendVoid(_window, Selector("release"));
        SendVoid(_actionTarget, Selector("release"));
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
    [LibraryImport(ObjC, EntryPoint = "objc_allocateClassPair", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint AllocateClass(nint superclass, string name, nuint bytes);
    [LibraryImport(ObjC, EntryPoint = "objc_registerClassPair")]
    private static partial void RegisterClass(nint type);
    [LibraryImport(ObjC, EntryPoint = "class_addMethod", StringMarshalling = StringMarshalling.Utf8)]
    private static unsafe partial byte AddMethod(nint type, nint selector, delegate* unmanaged[Cdecl]<nint, nint, nint, void> method, string encoding);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial void SetFrame(nint receiver, nint selector, Rect frame);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial nint SendObjectResult(nint receiver, nint selector, Rect frame);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial void SetInteger(nint receiver, nint selector, nint value);
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
