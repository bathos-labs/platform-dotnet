using System.Runtime.InteropServices;
using Bathos.Platform.Directories;

namespace Bathos.Platform.Linux;

/// <summary>Standard Linux directory discovery. Construction performs no lookup.</summary>
public sealed partial class LinuxStandardDirectories : IStandardDirectories
{
    /// <summary>Observes current Linux roots without creating directories. Invoke only on Linux.</summary>
    /// <remarks>Null locations are unavailable. Lookup errors can propagate; returned paths do not establish write access.
    /// Temporary roots are shared, and application-base resources are separate from writable storage.</remarks>
    public StandardDirectoryRoots Discover() => DirectoryDiscovery.Discover(PlatformOperatingSystem.Linux,
        () => UnixHomeDirectory.Resolve(Environment.GetEnvironmentVariable("HOME"), LookupAccount));

    internal static UnixHomeDirectory.Result LookupAccount(nint buffer, int size)
    {
        var error = GetPwUidR(GetEffectiveUid(), out var entry, buffer, (nuint)size, out var result);
        return new(error, result != 0, entry.Home);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Passwd
    {
        internal nint Name;
        internal nint Password;
        internal uint User;
        internal uint Group;
        internal nint Comment;
        internal nint Home;
        internal nint Shell;
    }

    [LibraryImport("libc", EntryPoint = "geteuid")]
    private static partial uint GetEffectiveUid();
    [LibraryImport("libc", EntryPoint = "getpwuid_r")]
    private static partial int GetPwUidR(uint user, out Passwd entry, nint buffer, nuint size, out nint result);
}
