using System.Runtime.InteropServices;
using Bathos.Platform.Directories;

namespace Bathos.Platform.MacOS;

/// <summary>Standard MacOS directory discovery. Construction performs no lookup.</summary>
public sealed partial class MacOSStandardDirectories : IStandardDirectories
{
    /// <summary>Observes current MacOS roots without creating directories. Invoke only on MacOS.</summary>
    /// <remarks>Null locations are unavailable. Lookup errors can propagate; returned paths do not establish write access.
    /// Temporary roots are shared, and application-base resources are separate from writable storage.</remarks>
    public StandardDirectoryRoots Discover() => DirectoryDiscovery.Discover(PlatformOperatingSystem.MacOS,
        () => UnixHomeDirectory.Resolve(Environment.GetEnvironmentVariable("HOME"), LookupAccount));

    internal static UnixHomeDirectory.Result LookupAccount(nint buffer, int size)
    {
        var error = GetPwUidR(GetEffectiveUid(), out var entry, buffer, (nuint)size, out var result);
        return new(error, result != 0, entry.Home);
    }

    // Complete Darwin 64-bit passwd allocation; pw_dir follows pw_gecos.
    [StructLayout(LayoutKind.Explicit, Size = 72)]
    private struct Passwd { [FieldOffset(48)] internal nint Home; }

    [LibraryImport("libSystem.B.dylib", EntryPoint = "geteuid")]
    private static partial uint GetEffectiveUid();
    [LibraryImport("libSystem.B.dylib", EntryPoint = "getpwuid_r")]
    private static partial int GetPwUidR(uint user, out Passwd entry, nint buffer, nuint size, out nint result);
}
