using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Bathos.Platform.Desktop;

namespace Bathos.Platform.MacOS;

/// <summary>Looks up application bundles through LaunchServices without scanning installation directories.</summary>
[SupportedOSPlatform("macos")]
public static partial class MacOSApplicationDiscovery
{
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string LaunchServices = "/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/LaunchServices";

    /// <summary>Returns registered bundles for a host-supplied bundle ID. Missing registrations return an empty list.</summary>
    /// <param name="bundleIdentifier">Nonblank LaunchServices bundle identifier, such as com.apple.TextEdit.</param>
    /// <returns>Registered bundle targets, or an empty list when LaunchServices reports no registration. Ordering does not imply preference.</returns>
    /// <remarks>Run only on MacOS. The host chooses among candidates; lookup does not prove that a target accepts a particular document format. Native loading and lookup failures propagate.</remarks>
    /// <exception cref="ArgumentNullException">The lookup key is null.</exception>
    /// <exception cref="ArgumentException">The key is blank, contains a null character, or has invalid syntax.</exception>
    /// <exception cref="IOException">LaunchServices lookup fails or returns an invalid application URL.</exception>
    public static unsafe IReadOnlyList<ApplicationTarget> FindByBundleIdentifier(string bundleIdentifier)
    {
        DesktopPaths.RequireText(bundleIdentifier, nameof(bundleIdentifier));
        nint identifier;
        fixed (char* characters = bundleIdentifier) identifier = CreateString(0, characters, bundleIdentifier.Length);
        if (identifier == 0) throw new InvalidOperationException("The bundle identifier could not be allocated.");
        nint applications = 0, error = 0;
        try
        {
            applications = CopyApplications(identifier, out error);
            if (applications == 0)
            {
                if (error != 0 && ErrorCode(error) == -10814) return []; // kLSApplicationNotFoundErr
                throw new IOException($"LaunchServices application lookup failed (code {(error == 0 ? 0 : ErrorCode(error))}).");
            }
            var results = new List<ApplicationTarget>();
            var count = GetCount(applications);
            for (nint index = 0; index < count; index++)
            {
                var url = GetValue(applications, index); // Borrowed from the retained array.
                if (url == 0) throw new IOException("LaunchServices returned an invalid application URL.");
                var nativePath = CopyPath(url, 0); // kCFURLPOSIXPathStyle
                if (nativePath == 0) throw new IOException("LaunchServices returned no application path.");
                try { results.Add(ApplicationTarget.MacOSBundle(ReadString(nativePath))); }
                finally { Release(nativePath); }
            }
            return results;
        }
        finally
        {
            if (error != 0) Release(error);
            if (applications != 0) Release(applications);
            Release(identifier);
        }
    }

    private static unsafe string ReadString(nint value)
    {
        var length = checked((int)GetLength(value));
        var characters = new char[length];
        fixed (char* destination = characters) GetCharacters(value, new Range { Length = length }, destination);
        return new string(characters);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Range { public nint Location, Length; }
    [LibraryImport(CoreFoundation, EntryPoint = "CFStringCreateWithCharacters")]
    private static unsafe partial nint CreateString(nint allocator, char* characters, nint length);
    [LibraryImport(CoreFoundation, EntryPoint = "CFStringGetLength")]
    private static partial nint GetLength(nint text);
    [LibraryImport(CoreFoundation, EntryPoint = "CFStringGetCharacters")]
    private static unsafe partial void GetCharacters(nint text, Range range, char* destination);
    [LibraryImport(CoreFoundation, EntryPoint = "CFArrayGetCount")]
    private static partial nint GetCount(nint array);
    [LibraryImport(CoreFoundation, EntryPoint = "CFArrayGetValueAtIndex")]
    private static partial nint GetValue(nint array, nint index);
    [LibraryImport(CoreFoundation, EntryPoint = "CFURLCopyFileSystemPath")]
    private static partial nint CopyPath(nint url, int style);
    [LibraryImport(CoreFoundation, EntryPoint = "CFRelease")]
    private static partial void Release(nint value);
    [LibraryImport(CoreFoundation, EntryPoint = "CFErrorGetCode")]
    private static partial nint ErrorCode(nint error);
    [LibraryImport(LaunchServices, EntryPoint = "LSCopyApplicationURLsForBundleIdentifier")]
    private static partial nint CopyApplications(nint identifier, out nint error);
}
