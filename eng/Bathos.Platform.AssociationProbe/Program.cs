using System.Runtime.InteropServices;
using System.Text.Json;

// Engineering-only Windows probe; fixture mode launches only the owned receiver.
if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Run this probe on Windows.");
if (args is ["--fixture" or "--applications-fixture", var receiver])
{
    AssociationFixture.Run(Path.GetFullPath(receiver), args[0] == "--applications-fixture");
    return;
}
if (args.Length != 1 || !Path.IsPathFullyQualified(args[0]) || !File.Exists(args[0]))
    throw new ArgumentException("Supply one existing absolute file path.");
var extension = Path.GetExtension(args[0]);
if (string.IsNullOrEmpty(extension)) throw new ArgumentException("This Windows probe requires a file extension.");
Exception? failure = null;
var worker = new Thread(() =>
{
    try
    {
        Console.WriteLine(JsonSerializer.Serialize(new { Extension = extension, First = Native.Read(extension), Second = Native.Read(extension) }, new JsonSerializerOptions { WriteIndented = true }));
    }
    catch (Exception exception) { failure = exception; }
});
worker.SetApartmentState(ApartmentState.STA);
worker.Start();
worker.Join();
if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();

internal sealed record Handler(string? Name, int NameResult, string? DisplayName, int DisplayNameResult,
    string? IconLocation, int IconIndex, int IconResult, int RecommendedResult, Identity ProgId, Identity AppId);

internal sealed record Identity(int QueryResult, int? ReadResult, string? Value);

internal static unsafe partial class Native
{
    internal static List<Handler> Read(string extension, string? selectedName = null, string? document = null, string? selectedProgId = null, Action? beforeInvoke = null)
    {
        var initialized = Initialize(0, 2); // COINIT_APARTMENTTHREADED; keep all COM ownership on this thread.
        Marshal.ThrowExceptionForHR(initialized);
        nint enumerator = 0, selected = 0;
        try
        {
            Marshal.ThrowExceptionForHR(Enumerate(extension, 0, out enumerator));
            if (enumerator == 0) throw new IOException("Shell returned no enumerator.");
            var results = new List<Handler>();
            while (true)
            {
                nint handler = 0;
                uint fetched = 0;
                var next = ((delegate* unmanaged[Stdcall]<nint, uint, nint*, uint*, int>)(*(nint**)enumerator)[3])(enumerator, 1, &handler, &fetched);
                try
                {
                    Marshal.ThrowExceptionForHR(next);
                    if (fetched == 0) break;
                    if (fetched != 1 || handler == 0) throw new IOException("Invalid handler enumeration result.");
                    var name = Text(handler, 3);
                    var progId = ReadIdentity(handler, new Guid("71e806fb-8dee-46fc-bf8c-7748a8a1ae13"));
                    if (selectedName is not null && string.Equals(name.Value, selectedName, StringComparison.OrdinalIgnoreCase)
                        && (selectedProgId is null || string.Equals(progId.Value, selectedProgId, StringComparison.OrdinalIgnoreCase)))
                    {
                        if (selected != 0) throw new IOException("Ambiguous handler name.");
                        ((delegate* unmanaged[Stdcall]<nint, uint>)(*(nint**)handler)[1])(handler);
                        selected = handler;
                    }
                    var display = Text(handler, 4);
                    nint icon = 0; int index = 0;
                    var iconResult = ((delegate* unmanaged[Stdcall]<nint, nint*, int*, int>)(*(nint**)handler)[5])(handler, &icon, &index);
                    string? location;
                    try { location = iconResult < 0 ? null : Marshal.PtrToStringUni(icon); }
                    finally { Marshal.FreeCoTaskMem(icon); }
                    var recommended = ((delegate* unmanaged[Stdcall]<nint, int>)(*(nint**)handler)[6])(handler);
                    results.Add(new(name.Value, name.Result, display.Value, display.Result, location, index, iconResult, recommended,
                        progId,
                        ReadIdentity(handler, new Guid("36db0196-9665-46d1-9ba7-d3709eecf9ed"))));
                    if (results.Count > 4096) throw new IOException("Handler enumeration exceeded the probe limit.");
                }
                finally { Release(handler); }
            }
            if (selectedName is not null)
            {
                if (selected == 0) throw new IOException("Selected handler no longer exists.");
                beforeInvoke?.Invoke();
                Invoke(selected, document ?? throw new ArgumentNullException(nameof(document)));
            }
            return results;
        }
        finally { Release(selected); Release(enumerator); Uninitialize(); }
    }

    private static Identity ReadIdentity(nint handler, Guid id)
    {
        nint identity = 0;
        var query = ((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)(*(nint**)handler)[0])(handler, &id, &identity);
        try
        {
            if (query < 0) return new(query, null, null);
            if (identity == 0) throw new IOException("Successful identity query returned no interface.");
            var value = Text(identity, 4);
            return new(query, value.Result, value.Value);
        }
        finally { Release(identity); }
    }

    private static void Invoke(nint handler, string document)
    {
        var itemId = new Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe");
        var dataId = new Guid("0000010e-0000-0000-c000-000000000046");
        var binding = new Guid("b8c0bd9f-ed24-455c-83e6-d5390c4fe8c4");
        nint item = 0, data = 0;
        try
        {
            Marshal.ThrowExceptionForHR(CreateItem(document, 0, ref itemId, out item));
            Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, nint, Guid*, Guid*, nint*, int>)(*(nint**)item)[3])(item, 0, &binding, &dataId, &data));
            Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, nint, int>)(*(nint**)handler)[8])(handler, data));
        }
        finally { Release(data); Release(item); }
    }

    [LibraryImport("shell32.dll", EntryPoint = "SHCreateItemFromParsingName", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int CreateItem(string path, nint context, ref Guid id, out nint item);

    private static (string? Value, int Result) Text(nint instance, int slot)
    {
        nint text = 0;
        var result = ((delegate* unmanaged[Stdcall]<nint, nint*, int>)(*(nint**)instance)[slot])(instance, &text);
        try { return (result < 0 ? null : Marshal.PtrToStringUni(text), result); }
        finally { Marshal.FreeCoTaskMem(text); }
    }
    private static void Release(nint value)
    {
        if (value != 0) ((delegate* unmanaged[Stdcall]<nint, uint>)(*(nint**)value)[2])(value);
    }
    [LibraryImport("ole32.dll", EntryPoint = "CoInitializeEx")] private static partial int Initialize(nint reserved, uint flags);
    [LibraryImport("ole32.dll", EntryPoint = "CoUninitialize")] private static partial void Uninitialize();
    [LibraryImport("shell32.dll", EntryPoint = "SHAssocEnumHandlers", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int Enumerate(string extension, uint filter, out nint enumerator);
}
