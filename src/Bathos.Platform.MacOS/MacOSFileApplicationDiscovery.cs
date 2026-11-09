using System.Runtime.InteropServices;
using Bathos.Platform.Desktop;

namespace Bathos.Platform.MacOS;

/// <summary>Discovers registered applications for an existing file through NSWorkspace.</summary>
public sealed partial class MacOSFileApplicationDiscovery : IApplicationDiscovery
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    /// <include file="../Bathos.Platform/Documentation/Contracts.xml" path="contracts/FindApplicationsForFileAsync/*" />
    public Task<IReadOnlyList<ApplicationCandidate>> FindForFileAsync(string absolutePath, CancellationToken cancellationToken = default)
    {
        DesktopPaths.RequireAbsolute(absolutePath, nameof(absolutePath));
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("macOS discovery requires macOS.");
        return Task.Run<IReadOnlyList<ApplicationCandidate>>(() => Read(absolutePath, cancellationToken), cancellationToken);
    }

    private static System.Collections.ObjectModel.ReadOnlyCollection<ApplicationCandidate> Read(string path, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        new MacFileEntryProbe().RequireRegularFile(path);
        var framework = NativeLibrary.Load("/System/Library/Frameworks/AppKit.framework/AppKit");
        nint pool = 0;
        try
        {
            pool = Send(Send(GetClass("NSAutoreleasePool"), Sel("alloc")), Sel("init"));
            if (pool == 0) throw new IOException("Could not create the discovery autorelease pool.");
            var workspace = Send(GetClass("NSWorkspace"), Sel("sharedWorkspace"));
            var nativePath = String(GetClass("NSString"), Sel("stringWithUTF8String:"), path);
            if (workspace == 0 || nativePath == 0) throw new IOException("Could not prepare application discovery.");
            var file = Object(GetClass("NSURL"), Sel("fileURLWithPath:"), nativePath);
            if (file == 0) throw new IOException("Could not represent the file URL.");
            var applications = Object(workspace, Sel("URLsForApplicationsToOpenURL:"), file);
            if (applications == 0) throw new IOException("NSWorkspace returned no application array.");
            var count = Count(applications, Sel("count"));
            if (count > 4096) throw new IOException("Application discovery exceeded its candidate limit.");
            var results = new List<ApplicationCandidate>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var manager = Send(GetClass("NSFileManager"), Sel("defaultManager"));
            if (manager == 0) throw new IOException("The native file manager is unavailable.");
            for (nuint index = 0; index < count; index++)
            {
                token.ThrowIfCancellationRequested();
                var url = At(applications, Sel("objectAtIndex:"), index);
                if (url == 0 || Boolean(url, Sel("isFileURL")) == 0) throw new IOException("NSWorkspace returned a nonlocal application URL.");
                var bundlePath = Send(url, Sel("path"));
                var value = ReadString(bundlePath);
                ApplicationTarget target;
                try { target = ApplicationTarget.MacOSBundle(value); }
                catch (ArgumentException error) { throw new IOException("NSWorkspace returned an invalid bundle target.", error); }
                if (!seen.Add(value)) continue;
                var label = Object(manager, Sel("displayNameAtPath:"), bundlePath);
                var name = label == 0 ? value : ReadString(label);
                results.Add(new(target, string.IsNullOrWhiteSpace(name) ? value : name));
            }
            token.ThrowIfCancellationRequested();
            return results.AsReadOnly();
        }
        finally
        {
            if (pool != 0) Void(pool, Sel("drain"));
            NativeLibrary.Free(framework);
        }
    }

    private static unsafe string ReadString(nint text)
    {
        if (text == 0) throw new IOException("Native application metadata is missing.");
        var length = Length(text);
        if (length < 0 || length > 32768) throw new IOException("Native application metadata exceeds its character limit.");
        var buffer = new char[(int)length];
        fixed (char* destination = buffer) Characters(text, new Range { Length = length }, destination);
        var value = new string(buffer);
        if (value.Contains('\0', StringComparison.Ordinal)) throw new IOException("Native application metadata contains a null character.");
        return value;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Range { public nint Location, Length; }
    [LibraryImport(CoreFoundation, EntryPoint = "CFStringGetLength")] private static partial nint Length(nint text);
    [LibraryImport(CoreFoundation, EntryPoint = "CFStringGetCharacters")] private static unsafe partial void Characters(nint text, Range range, char* destination);
    [LibraryImport(ObjC, EntryPoint = "objc_getClass", StringMarshalling = StringMarshalling.Utf8)] private static partial nint GetClass(string name);
    [LibraryImport(ObjC, EntryPoint = "sel_registerName", StringMarshalling = StringMarshalling.Utf8)] private static partial nint Sel(string name);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")] private static partial nint Send(nint instance, nint selector);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")] private static partial void Void(nint instance, nint selector);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")] private static partial nint Object(nint instance, nint selector, nint argument);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")] private static partial nint At(nint instance, nint selector, nuint index);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")] private static partial byte Boolean(nint instance, nint selector);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")] private static partial nuint Count(nint instance, nint selector);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend", StringMarshalling = StringMarshalling.Utf8)] private static partial nint String(nint instance, nint selector, string value);
}
