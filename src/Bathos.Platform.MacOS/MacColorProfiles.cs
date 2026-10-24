using System.Runtime.InteropServices;
using System.Text;
using Bathos.Platform.ColorProfiles;

namespace Bathos.Platform.MacOS;

internal static partial class MacColorProfiles
{
    private const string ColorSync = "/System/Library/Frameworks/ColorSync.framework/ColorSync";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    internal static ColorProfileInventory Enumerate(ColorProfileAccessLimits limits, CancellationToken cancellationToken)
    {
        var cs = NativeLibrary.Load(ColorSync);
        nint cf = 0;
        nint options = 0;
        nint error = 0;
        try
        {
            cf = NativeLibrary.Load(CoreFoundation);
            var key = Marshal.ReadIntPtr(NativeLibrary.GetExport(cs, "kColorSyncWaitForCacheReply"));
            var yes = Marshal.ReadIntPtr(NativeLibrary.GetExport(cf, "kCFBooleanTrue"));
            options = DictionaryCreate(0, in key, in yes, 1, 0, 0);
            if (options == 0) throw new IOException("ColorSync could not prepare profile discovery.");
            var urlKey = Marshal.ReadIntPtr(NativeLibrary.GetExport(cs, "kColorSyncProfileURL"));
            var paths = new HashSet<string>(StringComparer.Ordinal);
            var complete = true;
            string? callbackFailure = null;
            ProfileCallback callback = (info, _) =>
            {
                // No managed exception may escape a native callback.
                try
                {
                    if (cancellationToken.IsCancellationRequested || paths.Count >= limits.MaximumEntries)
                    { complete = false; return 0; }
                    var url = DictionaryGetValue(info, urlKey);
                    var buffer = new byte[65536];
                    if (url == 0 || UrlFileSystemRepresentation(url, 1, buffer, buffer.Length) == 0)
                    { complete = false; return 1; }
                    var end = Array.IndexOf(buffer, (byte)0);
                    if (end <= 0) { complete = false; return 1; }
                    paths.Add(new UTF8Encoding(false, true).GetString(buffer, 0, end));
                    return 1;
                }
                catch (Exception exception)
                {
                    complete = false; callbackFailure = exception.Message;
                    return 0;
                }
            };
            Iterate(callback, 0, 0, options, out error);
            GC.KeepAlive(callback);
            cancellationToken.ThrowIfCancellationRequested();
            if (error != 0) throw new IOException("ColorSync could not complete profile discovery. Refresh to try again.");
            var deviceUrlKey = Marshal.ReadIntPtr(NativeLibrary.GetExport(cs, "kColorSyncDeviceProfileURL"));
            var deviceNameKey = Marshal.ReadIntPtr(NativeLibrary.GetExport(cs, "kColorSyncDeviceDescription"));
            var currentKey = Marshal.ReadIntPtr(NativeLibrary.GetExport(cs, "kColorSyncDeviceProfileIsCurrent"));
            var devices = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            var deviceCount = 0;
            ProfileCallback deviceCallback = (info, _) =>
            {
                try
                {
                    if (cancellationToken.IsCancellationRequested || ++deviceCount > limits.MaximumEntries)
                    { complete = false; return 0; }
                    var current = DictionaryGetValue(info, currentKey);
                    if (current != yes) return 1;
                    var url = DictionaryGetValue(info, deviceUrlKey);
                    // Devices may be registered without a profile URL (kCFNull).
                    if (url == 0 || GetTypeId(url) != UrlTypeId()) return 1;
                    var buffer = new byte[65536];
                    if (UrlFileSystemRepresentation(url, 1, buffer, buffer.Length) == 0)
                    { complete = false; return 1; }
                    var end = Array.IndexOf(buffer, (byte)0);
                    if (end <= 0) { complete = false; return 1; }
                    var path = new UTF8Encoding(false, true).GetString(buffer, 0, end);
                    if (!paths.Contains(path) && paths.Count >= limits.MaximumEntries)
                    { complete = false; return 0; }
                    paths.Add(path);
                    var description = DictionaryGetValue(info, deviceNameKey);
                    if (description == 0 || GetTypeId(description) != StringTypeId()) return 1;
                    if (StringGetCString(description, buffer, buffer.Length, 0x08000100) == 0) return 1;
                    end = Array.IndexOf(buffer, (byte)0);
                    if (end <= 0) return 1;
                    var name = new UTF8Encoding(false, true).GetString(buffer, 0, end).Trim();
                    if (name.Length == 0) return 1;
                    if (!devices.TryGetValue(path, out var names)) devices.Add(path, names = new(StringComparer.Ordinal));
                    names.Add(name);
                    return 1;
                }
                catch (Exception exception)
                { complete = false; callbackFailure = exception.Message; return 0; }
            };
            IterateDevices(deviceCallback, 0);
            GC.KeepAlive(deviceCallback);
            cancellationToken.ThrowIfCancellationRequested();
            return new(Array.AsReadOnly(paths.Order(StringComparer.Ordinal)
                .Select(path => new ColorProfileLocation(path, Path.GetFileName(path), path)
                { DeviceNames = devices.TryGetValue(path, out var names) ? names.Order(StringComparer.Ordinal).ToArray() : [] }).ToArray()), complete,
                complete ? "ColorSync installed profiles and current device associations. Display accuracy is not evaluated."
                    : "ColorSync discovery was incomplete or reached the configured profile limit. Refresh to try again." +
                        (callbackFailure is null ? "" : " " + callbackFailure));
        }
        finally
        {
            if (error != 0) Release(error);
            if (options != 0) Release(options);
            if (cf != 0) NativeLibrary.Free(cf);
            NativeLibrary.Free(cs);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate byte ProfileCallback(nint info, nint context);

    [LibraryImport(ColorSync, EntryPoint = "ColorSyncIterateInstalledProfilesWithOptions")]
    private static partial void Iterate(ProfileCallback callback, nint seed, nint context, nint options, out nint error);
    [LibraryImport(ColorSync, EntryPoint = "ColorSyncIterateDeviceProfiles")]
    private static partial void IterateDevices(ProfileCallback callback, nint context);
    [LibraryImport(CoreFoundation, EntryPoint = "CFGetTypeID")]
    private static partial nuint GetTypeId(nint value);
    [LibraryImport(CoreFoundation, EntryPoint = "CFURLGetTypeID")]
    private static partial nuint UrlTypeId();
    [LibraryImport(CoreFoundation, EntryPoint = "CFStringGetTypeID")]
    private static partial nuint StringTypeId();
    [LibraryImport(CoreFoundation, EntryPoint = "CFStringGetCString")]
    private static partial byte StringGetCString(nint value, [Out] byte[] buffer, nint length, uint encoding);
    [LibraryImport(CoreFoundation, EntryPoint = "CFDictionaryCreate")]
    private static partial nint DictionaryCreate(nint allocator, in nint keys, in nint values, nint count, nint keyCallbacks, nint valueCallbacks);
    [LibraryImport(CoreFoundation, EntryPoint = "CFDictionaryGetValue")]
    private static partial nint DictionaryGetValue(nint dictionary, nint key);
    [LibraryImport(CoreFoundation, EntryPoint = "CFURLGetFileSystemRepresentation")]
    private static partial byte UrlFileSystemRepresentation(nint url, byte resolve, [Out] byte[] buffer, nint length);
    [LibraryImport(CoreFoundation, EntryPoint = "CFRelease")]
    private static partial void Release(nint value);
}
