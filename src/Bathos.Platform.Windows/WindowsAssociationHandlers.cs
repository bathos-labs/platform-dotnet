using System.Runtime.InteropServices;
using Bathos.Platform.Desktop;

namespace Bathos.Platform.Windows;

internal static unsafe partial class WindowsAssociationHandlers
{
    internal sealed record Entry(string Name, string Label, bool Recommended, bool HasProgId, nint Handle);

    internal static Task<T> Run<T>(Func<T> action)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows associations require Windows.");
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { completion.SetResult(action()); }
            catch (OperationCanceledException error) { completion.SetCanceled(error.CancellationToken); }
            catch (Exception error) { completion.SetException(error); }
        })
        { IsBackground = true, Name = "Bathos application associations" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    internal static T WithHandlers<T>(string extension, Func<IReadOnlyList<Entry>, T> action, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Marshal.ThrowExceptionForHR(Initialize(0, 6));
        nint enumerator = 0;
        var owned = new List<nint>();
        try
        {
            Marshal.ThrowExceptionForHR(Enumerate(extension, 0, out enumerator));
            if (enumerator == 0) throw new IOException("Shell returned no handler enumerator.");
            var winners = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
            for (var count = 0; ; count++)
            {
                token.ThrowIfCancellationRequested();
                nint handler = 0;
                uint fetched = 0;
                var status = ((delegate* unmanaged[Stdcall]<nint, uint, nint*, uint*, int>)(*(nint**)enumerator)[3])(enumerator, 1, &handler, &fetched);
                if (handler != 0) owned.Add(handler);
                Marshal.ThrowExceptionForHR(status);
                if (status == 1 && fetched == 0 && handler == 0) break;
                if (status != 0 || fetched != 1 || handler == 0) throw new IOException("Invalid Shell enumeration result.");
                if (count >= 4096) throw new IOException("Shell handler enumeration exceeded its limit.");
                var name = Text(handler, 3);
                if (string.IsNullOrWhiteSpace(name)) throw new IOException("Shell handler has no usable native name.");
                var label = Text(handler, 4);
                var recommendation = ((delegate* unmanaged[Stdcall]<nint, int>)(*(nint**)handler)[6])(handler);
                Marshal.ThrowExceptionForHR(recommendation);
                var entry = new Entry(name, string.IsNullOrWhiteSpace(label) ? name : label, recommendation == 0, HasProgId(handler), handler);
                if (!winners.TryGetValue(name, out var previous) || Rank(entry) > Rank(previous)) winners[name] = entry;
            }
            token.ThrowIfCancellationRequested();
            return action(winners.Values.ToArray());
        }
        finally
        {
            foreach (var handler in owned) Release(handler);
            Release(enumerator);
            Uninitialize();
        }
    }

    private static int Rank(Entry entry) => (entry.Recommended ? 2 : 0) + (entry.HasProgId ? 1 : 0);

    internal static Entry Resolve(IReadOnlyList<Entry> entries, ApplicationTarget target) =>
        entries.SingleOrDefault(e => string.Equals(e.Name, target.Value, StringComparison.OrdinalIgnoreCase))
        ?? throw new FileNotFoundException("The selected application is no longer registered in this association context.");

    private static bool HasProgId(nint handler)
    {
        var id = new Guid("71e806fb-8dee-46fc-bf8c-7748a8a1ae13");
        nint identity = 0;
        var status = ((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)(*(nint**)handler)[0])(handler, &id, &identity);
        try
        {
            if (status == unchecked((int)0x80004002)) return false;
            Marshal.ThrowExceptionForHR(status);
            if (identity == 0) throw new IOException("Shell returned no identity interface.");
            return !string.IsNullOrWhiteSpace(Text(identity, 4, optional: true));
        }
        finally { Release(identity); }
    }

    private static string? Text(nint instance, int slot, bool optional = false)
    {
        nint value = 0;
        var status = ((delegate* unmanaged[Stdcall]<nint, nint*, int>)(*(nint**)instance)[slot])(instance, &value);
        try
        {
            if (optional && status == unchecked((int)0x80004005)) return null;
            Marshal.ThrowExceptionForHR(status);
            return ReadText(value);
        }
        finally { Marshal.FreeCoTaskMem(value); }
    }

    internal static void Invoke(nint handler, string path, CancellationToken token)
    {
        var itemId = new Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe");
        var dataId = new Guid("0000010e-0000-0000-c000-000000000046");
        var binding = new Guid("b8c0bd9f-ed24-455c-83e6-d5390c4fe8c4");
        nint item = 0, data = 0;
        try
        {
            Marshal.ThrowExceptionForHR(CreateItem(path, 0, ref itemId, out item));
            if (item == 0) throw new IOException("Shell returned no file item.");
            Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, nint, Guid*, Guid*, nint*, int>)(*(nint**)item)[3])(item, 0, &binding, &dataId, &data));
            if (data == 0) throw new IOException("Shell returned no file data object.");
            token.ThrowIfCancellationRequested();
            Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, nint, int>)(*(nint**)handler)[8])(handler, data));
        }
        finally { Release(data); Release(item); }
    }

    internal static (string? Location, int Index) IconLocation(nint handler)
    {
        nint location = 0;
        int index = 0;
        var status = ((delegate* unmanaged[Stdcall]<nint, nint*, int*, int>)(*(nint**)handler)[5])(handler, &location, &index);
        try
        {
            Marshal.ThrowExceptionForHR(status);
            return (ReadText(location), index);
        }
        finally { Marshal.FreeCoTaskMem(location); }
    }

    private static string? ReadText(nint value)
    {
        if (value == 0) return null;
        var characters = (char*)value;
        for (var length = 0; length < 32768; length++)
            if (characters[length] == '\0') return new string(characters, 0, length);
        throw new IOException("Shell metadata exceeded the character limit.");
    }

    private static void Release(nint value)
    {
        if (value != 0) ((delegate* unmanaged[Stdcall]<nint, uint>)(*(nint**)value)[2])(value);
    }
    [LibraryImport("ole32.dll", EntryPoint = "CoInitializeEx")] private static partial int Initialize(nint reserved, uint flags);
    [LibraryImport("ole32.dll", EntryPoint = "CoUninitialize")] private static partial void Uninitialize();
    [LibraryImport("shell32.dll", EntryPoint = "SHAssocEnumHandlers", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int Enumerate(string extension, uint filter, out nint enumerator);
    [LibraryImport("shell32.dll", EntryPoint = "SHCreateItemFromParsingName", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int CreateItem(string path, nint context, ref Guid id, out nint item);
}
