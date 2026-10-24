using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Bathos.Platform.ColorProfiles;

namespace Bathos.Platform.Windows;

internal static partial class WindowsColorProfiles
{
    private const int MaximumEnumerationBytes = 4 * 1024 * 1024;

    internal static ColorProfileInventory Enumerate(ColorProfileAccessLimits limits, CancellationToken cancellationToken)
    {
        var locations = new Dictionary<string, ColorProfileLocation>(StringComparer.OrdinalIgnoreCase);
        var findings = new List<string>();
        for (uint scope = 0; scope < 2; scope++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var criteria = new EnumerationCriteria { Size = (uint)Marshal.SizeOf<EnumerationCriteria>(), Version = 0x0300 };
            if (EnumerationSize(scope, in criteria, out var size) == 0)
            { findings.Add(new Win32Exception(Marshal.GetLastPInvokeError()).Message); continue; }
            if (size == 0) continue;
            if (size > MaximumEnumerationBytes || size % 2 != 0)
            { findings.Add("Windows profile enumeration exceeded its buffer limit."); continue; }
            var buffer = new byte[size];
            if (EnumerateProfiles(scope, in criteria, buffer, size, out var count) == 0)
            { findings.Add(new Win32Exception(Marshal.GetLastPInvokeError()).Message); continue; }
            IReadOnlyList<string> names;
            try { names = ParseNames(buffer, count, limits.MaximumEntries); }
            catch (InvalidDataException exception) { findings.Add(exception.Message); continue; }
            foreach (var name in names)
            {
                if (locations.Count >= limits.MaximumEntries)
                { findings.Add("Windows profile enumeration reached the configured profile limit."); break; }
                var label = scope == 0 ? "System-wide" : "Current user";
                locations[name] = new(name, Path.GetFileName(name), $"{name} ({label})");
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new(Array.AsReadOnly(locations.Values.OrderBy(location => location.Key, StringComparer.Ordinal).ToArray()),
            findings.Count == 0, findings.Count == 0
                ? "Windows ICC/WCS inventory. Advanced-color/HDR display profiles and device defaults are not evaluated."
                : string.Join("\n", findings));
    }

    internal static IReadOnlyList<string> ParseNames(ReadOnlySpan<byte> buffer, uint count, int maximumEntries)
    {
        if (count > maximumEntries || buffer.Length % 2 != 0)
            throw new InvalidDataException("Windows returned an invalid profile list.");
        string text;
        try { text = new UnicodeEncoding(false, false, true).GetString(buffer); }
        catch (DecoderFallbackException exception) { throw new InvalidDataException("Windows returned invalid profile-name text.", exception); }
        if (!text.EndsWith('\0')) throw new InvalidDataException("Windows returned an unterminated profile list.");
        var names = text.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        if (names.Length != count) throw new InvalidDataException("Windows returned an incomplete profile list.");
        return Array.AsReadOnly(names);
    }

    internal static unsafe ReadOnlyMemory<byte> Read(string key, ColorProfileAccessLimits limits, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        fixed (char* path = key)
        {
            var profile = new NativeProfile { Type = 1, Data = (nint)path, Size = checked((uint)(key.Length + 1) * 2) };
            var handle = OpenProfile(in profile, 1, 3, 3);
            if (handle == 0) throw Failure("Windows could not open the profile.");
            try
            {
                uint size = 0;
                _ = ProfileBytes(handle, null, ref size);
                if (size == 0 || size > limits.MaximumBytes)
                    throw new InvalidDataException("Windows could not supply a profile within the configured read limit.");
                var bytes = new byte[size];
                if (ProfileBytes(handle, bytes, ref size) == 0) throw Failure("Windows could not read the profile.");
                cancellationToken.ThrowIfCancellationRequested();
                if (size != bytes.Length) throw new IOException("The profile changed while it was being read. Refresh to inspect it again.");
                return bytes;
            }
            finally { _ = CloseProfile(handle); }
        }
    }

    private static IOException Failure(string message) => new(message, new Win32Exception(Marshal.GetLastPInvokeError()));

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeProfile { public uint Type; public nint Data; public uint Size; }

    [StructLayout(LayoutKind.Sequential)]
    private struct EnumerationCriteria
    {
        public uint Size, Version, Fields;
        public nint DeviceName;
        public uint MediaType, DitheringMode, ResolutionX, ResolutionY, CmmType, Class, DataColorSpace, ConnectionSpace,
            Signature, Platform, ProfileFlags, Manufacturer, Model, AttributesLow, AttributesHigh, RenderingIntent, Creator, DeviceClass;
    }

    [LibraryImport("mscms.dll", EntryPoint = "WcsEnumColorProfilesSize", SetLastError = true)]
    private static partial int EnumerationSize(uint scope, in EnumerationCriteria criteria, out uint size);
    [LibraryImport("mscms.dll", EntryPoint = "WcsEnumColorProfiles", SetLastError = true)]
    private static partial int EnumerateProfiles(uint scope, in EnumerationCriteria criteria, [Out] byte[] buffer, uint size, out uint count);
    [LibraryImport("mscms.dll", EntryPoint = "OpenColorProfileW", SetLastError = true)]
    private static partial nint OpenProfile(in NativeProfile profile, uint access, uint share, uint creation);
    [LibraryImport("mscms.dll", EntryPoint = "GetColorProfileFromHandle", SetLastError = true)]
    private static partial int ProfileBytes(nint profile, [Out] byte[]? bytes, ref uint size);
    [LibraryImport("mscms.dll", EntryPoint = "CloseColorProfile")]
    private static partial int CloseProfile(nint profile);
}
