using System.Runtime.InteropServices;
using System.Text.Json;

// Engineering-only read-only Windows probe. No registration changes or application launches.
if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Run this probe on Windows.");
if (args.Length != 1 || !Path.IsPathFullyQualified(args[0]) || !File.Exists(args[0]))
    throw new ArgumentException("Supply one existing absolute file path.");
var extension = Path.GetExtension(args[0]);
if (string.IsNullOrEmpty(extension)) throw new ArgumentException("This Windows probe requires a file extension.");
Console.WriteLine(JsonSerializer.Serialize(new { Extension = extension, First = Native.Read(extension), Second = Native.Read(extension) }, new JsonSerializerOptions { WriteIndented = true }));

internal sealed record Handler(string? Name, int NameResult, string? DisplayName, int DisplayNameResult,
    string? IconLocation, int IconIndex, int IconResult, int RecommendedResult);

internal static unsafe partial class Native
{
    internal static List<Handler> Read(string extension)
    {
        var initialized = Initialize(0, 2); // COINIT_APARTMENTTHREADED; keep all COM ownership on this thread.
        Marshal.ThrowExceptionForHR(initialized);
        nint enumerator = 0;
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
                    var display = Text(handler, 4);
                    nint icon = 0; int index = 0;
                    var iconResult = ((delegate* unmanaged[Stdcall]<nint, nint*, int*, int>)(*(nint**)handler)[5])(handler, &icon, &index);
                    string? location;
                    try { location = iconResult < 0 ? null : Marshal.PtrToStringUni(icon); }
                    finally { Marshal.FreeCoTaskMem(icon); }
                    var recommended = ((delegate* unmanaged[Stdcall]<nint, int>)(*(nint**)handler)[6])(handler);
                    results.Add(new(name.Value, name.Result, display.Value, display.Result, location, index, iconResult, recommended));
                    if (results.Count > 4096) throw new IOException("Handler enumeration exceeded the probe limit.");
                }
                finally { Release(handler); }
            }
            return results;
        }
        finally { Release(enumerator); Uninitialize(); }
    }

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
