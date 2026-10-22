using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Bathos.Platform.Linux;

/// <summary>Shared native libdbus transport for color selection and file-manager requests.</summary>
[SupportedOSPlatform("linux")]
internal static partial class DesktopBus
{
    private const string Library = "libdbus-1.so.3";
    private const string Portal = "org.freedesktop.portal.Desktop";
    private const string RequestInterface = "org.freedesktop.portal.Request";
    private static readonly Lazy<bool> ThreadSupport = new(() => InitializeThreads() != 0);

    internal static void Reveal(string path, CancellationToken token, string? busAddress = null)
    {
        if (!ThreadSupport.Value) throw new InvalidOperationException("D-Bus thread support could not be initialized.");
        var connection = Connect(busAddress);
        try
        {
            SetExitOnDisconnect(connection, 0);
            using var call = new Message(NewMethodCall("org.freedesktop.FileManager1", "/org/freedesktop/FileManager1",
                "org.freedesktop.FileManager1", "ShowItems"));
            InitializeAppend(call.Value, out var writer);
            Open(ref writer, 'a', "s", out var uris);
            AppendText(ref uris, new Uri(path).AbsoluteUri);
            Check(CloseContainer(ref writer, ref uris));
            AppendText(ref writer, string.Empty); // No caller-supplied startup notification ID.
            token.ThrowIfCancellationRequested();
            using var reply = new Message(Call(connection, call.Value));
            if (ReadString(GetSignature(reply.Value)) != string.Empty)
                throw new IOException("The file manager returned a malformed reveal response.");
        }
        finally { CloseConnection(connection); UnrefConnection(connection); }
    }

    internal static ScreenColor? PickColor(string parentWindow, string? busAddress = null)
    {
        if (!ThreadSupport.Value) throw new InvalidOperationException("D-Bus thread support could not be initialized.");
        var connection = Connect(busAddress);
        try
        {
            SetExitOnDisconnect(connection, 0);
            // Subscribe before making the call: a fast Response may arrive before the method reply.
            // This private connection has only one request, so a broad match also handles older portal path formats.
            Subscribe(connection);
            using var call = new Message(NewMethodCall(Portal, "/org/freedesktop/portal/desktop", "org.freedesktop.portal.Screenshot", "PickColor"));
            AppendRequest(call.Value, parentWindow);
            using var reply = new Message(Call(connection, call.Value));
            if (ReadString(GetSignature(reply.Value)) != "o") throw InvalidResponse();
            InitializeRead(reply.Value, out var reader);
            var requestPath = ReadText(ref reader, 'o');
            if (!requestPath.StartsWith("/org/freedesktop/portal/desktop/request/", StringComparison.Ordinal)) throw InvalidResponse();
            var sender = ReadString(GetSender(reply.Value));
            if (!sender.StartsWith(':')) throw InvalidResponse();

            while (true)
            {
                var next = PopMessage(connection);
                if (next == 0)
                {
                    // A blocking socket wait, not polling or a nested host dispatcher loop.
                    if (ReadWrite(connection, -1) == 0) throw new IOException("The desktop portal disconnected before color selection completed.");
                    continue;
                }
                using var message = new Message(next);
                if (IsSignal(message.Value, RequestInterface, "Response") != 0 &&
                    ReadString(GetSender(message.Value)) == sender && ReadString(GetPath(message.Value)) == requestPath)
                    return ReadResponse(message.Value);
                if (IsSignal(message.Value, "org.freedesktop.DBus", "NameOwnerChanged") != 0 &&
                    ReadString(GetSender(message.Value)) == "org.freedesktop.DBus")
                {
                    // The request cannot finish if the portal disappears while its bus stays alive.
                    InitializeRead(message.Value, out var owner);
                    if (ReadText(ref owner, 's') == Portal)
                    {
                        Next(ref owner);
                        var previous = ReadText(ref owner, 's');
                        Next(ref owner);
                        if (previous == sender && ReadText(ref owner, 's') != sender)
                            throw new IOException("The desktop portal exited before color selection completed.");
                    }
                }
            }
        }
        finally { CloseConnection(connection); UnrefConnection(connection); }
    }

    private static void AppendRequest(nint message, string parentWindow)
    {
        InitializeAppend(message, out var writer);
        AppendText(ref writer, parentWindow);
        Open(ref writer, 'a', "{sv}", out var options);
        Open(ref options, 'e', null, out var entry);
        AppendText(ref entry, "handle_token");
        Open(ref entry, 'v', "s", out var value);
        AppendText(ref value, "bathos_" + Guid.NewGuid().ToString("N"));
        Check(CloseContainer(ref entry, ref value));
        Check(CloseContainer(ref options, ref entry));
        Check(CloseContainer(ref writer, ref options));
    }

    private static ScreenColor? ReadResponse(nint message)
    {
        if (ReadString(GetSignature(message)) != "ua{sv}") throw InvalidResponse();
        InitializeRead(message, out var reader);
        GetBasicUInt(ref reader, out var status);
        if (status == 1) return null;
        if (status != 0) throw new InvalidOperationException($"The desktop portal could not select a color (response {status}).");
        Next(ref reader);
        Recurse(ref reader, out var entries);
        ScreenColor? result = null;
        while (GetType(ref entries) != 0)
        {
            Recurse(ref entries, out var entry);
            var key = ReadText(ref entry, 's');
            Next(ref entry);
            if (key == "color")
            {
                if (result is not null || GetType(ref entry) != 'v') throw InvalidResponse();
                Recurse(ref entry, out var variant);
                if (GetType(ref variant) != 'r') throw InvalidResponse();
                Recurse(ref variant, out var components);
                var r = ReadComponent(ref components); Next(ref components);
                var g = ReadComponent(ref components); Next(ref components);
                var b = ReadComponent(ref components);
                if (Next(ref components) != 0) throw InvalidResponse();
                result = new ScreenColor(r, g, b);
            }
            Next(ref entries);
        }
        return result ?? throw InvalidResponse();
    }

    private static byte ReadComponent(ref Iterator reader)
    {
        if (GetType(ref reader) != 'd') throw InvalidResponse();
        GetBasicDouble(ref reader, out var component);
        if (!double.IsFinite(component) || component < 0 || component > 1) throw InvalidResponse();
        return (byte)Math.Round(component * 255, MidpointRounding.AwayFromZero);
    }

    private static string ReadText(ref Iterator reader, int type)
    {
        if (GetType(ref reader) != type) throw InvalidResponse();
        GetBasicPointer(ref reader, out var text);
        return ReadString(text);
    }

    private static string ReadString(nint value) => Marshal.PtrToStringUTF8(value) ?? throw InvalidResponse();
    private static InvalidOperationException InvalidResponse() => new("The desktop portal returned a malformed color-selection response.");
    private static void Check(int result)
    {
        if (result == 0) throw new InvalidOperationException("D-Bus could not allocate a color-selection message.");
    }

    private static unsafe void AppendText(ref Iterator writer, string text)
    {
        var utf8 = Marshal.StringToCoTaskMemUTF8(text);
        try { Check(AppendBasic(ref writer, 's', &utf8)); }
        finally { Marshal.FreeCoTaskMem(utf8); }
    }

    private static void Open(ref Iterator writer, int type, string? signature, out Iterator child) =>
        Check(OpenContainer(ref writer, type, signature, out child));

    private static nint Connect(string? address)
    {
        var error = new Error();
        InitializeError(ref error);
        try
        {
            var result = address is null ? GetPrivateBus(0, ref error) : OpenPrivateConnection(address, ref error);
            if (result == 0) throw Failure("Could not connect to the desktop session bus", error);
            if (address is not null && RegisterBus(result, ref error) == 0)
            {
                CloseConnection(result); UnrefConnection(result);
                throw Failure("Could not register with the desktop session bus", error);
            }
            return result;
        }
        finally { FreeError(ref error); }
    }

    private static void Subscribe(nint connection)
    {
        AddMatch(connection, "type='signal',sender='org.freedesktop.portal.Desktop',interface='org.freedesktop.portal.Request',member='Response'");
        AddMatch(connection, "type='signal',sender='org.freedesktop.DBus',interface='org.freedesktop.DBus',member='NameOwnerChanged',arg0='org.freedesktop.portal.Desktop'");
    }

    private static void AddMatch(nint connection, string rule)
    {
        var error = new Error();
        InitializeError(ref error);
        try
        {
            AddBusMatch(connection, rule, ref error);
            if (error.Name != 0) throw Failure("Could not subscribe to the desktop portal", error);
        }
        finally { FreeError(ref error); }
    }

    private static nint Call(nint connection, nint message)
    {
        var error = new Error();
        InitializeError(ref error);
        try
        {
            // These methods acknowledge handoff; PickColor completes its interaction separately by signal.
            var reply = SendWithReply(connection, message, 30_000, ref error);
            return reply != 0 ? reply : throw Failure("The desktop service request failed", error);
        }
        finally { FreeError(ref error); }
    }

    private static InvalidOperationException Failure(string context, Error error) =>
        new(context + ": " + (Marshal.PtrToStringUTF8(error.Message) ?? "unknown D-Bus error"));

    private sealed class Message(nint value) : IDisposable
    {
        internal nint Value { get; } = value != 0 ? value : throw new InvalidOperationException("D-Bus could not allocate a message.");
        public void Dispose() => UnrefMessage(Value);
    }

    // Public libdbus ABI layouts (dbus-message.h and dbus-errors.h), including reserved storage.
    [StructLayout(LayoutKind.Sequential)]
    private struct Iterator
    {
        public nint Dummy1, Dummy2;
        public uint Dummy3;
        public int Dummy4, Dummy5, Dummy6, Dummy7, Dummy8, Dummy9, Dummy10, Dummy11, Padding1;
        public nint Padding2, Padding3;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Error
    {
        public nint Name, Message;
        public uint Flags;
        public nint Padding;
    }

    [LibraryImport(Library, EntryPoint = "dbus_threads_init_default")]
    private static partial int InitializeThreads();
    [LibraryImport(Library, EntryPoint = "dbus_error_init")]
    private static partial void InitializeError(ref Error error);
    [LibraryImport(Library, EntryPoint = "dbus_error_free")]
    private static partial void FreeError(ref Error error);
    [LibraryImport(Library, EntryPoint = "dbus_bus_get_private")]
    private static partial nint GetPrivateBus(int type, ref Error error);
    [LibraryImport(Library, EntryPoint = "dbus_connection_open_private", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint OpenPrivateConnection(string address, ref Error error);
    [LibraryImport(Library, EntryPoint = "dbus_bus_register")]
    private static partial int RegisterBus(nint connection, ref Error error);
    [LibraryImport(Library, EntryPoint = "dbus_connection_set_exit_on_disconnect")]
    private static partial void SetExitOnDisconnect(nint connection, int exit);
    [LibraryImport(Library, EntryPoint = "dbus_bus_add_match", StringMarshalling = StringMarshalling.Utf8)]
    private static partial void AddBusMatch(nint connection, string rule, ref Error error);
    [LibraryImport(Library, EntryPoint = "dbus_connection_send_with_reply_and_block")]
    private static partial nint SendWithReply(nint connection, nint message, int timeout, ref Error error);
    [LibraryImport(Library, EntryPoint = "dbus_connection_pop_message")]
    private static partial nint PopMessage(nint connection);
    [LibraryImport(Library, EntryPoint = "dbus_connection_read_write")]
    private static partial int ReadWrite(nint connection, int timeout);
    [LibraryImport(Library, EntryPoint = "dbus_connection_close")]
    private static partial void CloseConnection(nint connection);
    [LibraryImport(Library, EntryPoint = "dbus_connection_unref")]
    private static partial void UnrefConnection(nint connection);
    [LibraryImport(Library, EntryPoint = "dbus_message_new_method_call", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint NewMethodCall(string destination, string path, string contract, string method);
    [LibraryImport(Library, EntryPoint = "dbus_message_unref")]
    private static partial void UnrefMessage(nint message);
    [LibraryImport(Library, EntryPoint = "dbus_message_get_signature")]
    private static partial nint GetSignature(nint message);
    [LibraryImport(Library, EntryPoint = "dbus_message_get_sender")]
    private static partial nint GetSender(nint message);
    [LibraryImport(Library, EntryPoint = "dbus_message_get_path")]
    private static partial nint GetPath(nint message);
    [LibraryImport(Library, EntryPoint = "dbus_message_is_signal", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int IsSignal(nint message, string contract, string member);
    [LibraryImport(Library, EntryPoint = "dbus_message_iter_init")]
    private static partial int InitializeRead(nint message, out Iterator reader);
    [LibraryImport(Library, EntryPoint = "dbus_message_iter_init_append")]
    private static partial void InitializeAppend(nint message, out Iterator writer);
    [LibraryImport(Library, EntryPoint = "dbus_message_iter_get_arg_type")]
    private static partial int GetType(ref Iterator reader);
    [LibraryImport(Library, EntryPoint = "dbus_message_iter_next")]
    private static partial int Next(ref Iterator reader);
    [LibraryImport(Library, EntryPoint = "dbus_message_iter_recurse")]
    private static partial void Recurse(ref Iterator reader, out Iterator child);
    [LibraryImport(Library, EntryPoint = "dbus_message_iter_get_basic")]
    private static partial void GetBasicUInt(ref Iterator reader, out uint value);
    [LibraryImport(Library, EntryPoint = "dbus_message_iter_get_basic")]
    private static partial void GetBasicDouble(ref Iterator reader, out double value);
    [LibraryImport(Library, EntryPoint = "dbus_message_iter_get_basic")]
    private static partial void GetBasicPointer(ref Iterator reader, out nint value);
    [LibraryImport(Library, EntryPoint = "dbus_message_iter_append_basic")]
    private static unsafe partial int AppendBasic(ref Iterator writer, int type, void* value);
    [LibraryImport(Library, EntryPoint = "dbus_message_iter_open_container", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int OpenContainer(ref Iterator writer, int type, string? signature, out Iterator child);
    [LibraryImport(Library, EntryPoint = "dbus_message_iter_close_container")]
    private static partial int CloseContainer(ref Iterator writer, ref Iterator child);
}
