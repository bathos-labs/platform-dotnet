# Integration guide

The [README](../README.md) covers package selection and a runnable publication example. All packages target
.NET 10. Providers can be retained directly, selected with `PlatformProviders.SelectCurrent`, or registered in
the host's own composition system. The core does not load or scan provider assemblies.

## Compose only the capabilities you need

Pass provider instances explicitly; selection returns the matching instance and does not probe permissions or desktop
services. Duplicate providers for the same selected OS fail, as does selecting an unknown or omitted OS. Providers
can be constructed on other hosts, but their native operations must only be invoked on their owning OS.

```csharp
using Bathos.Platform;
using Bathos.Platform.Linux;
using Bathos.Platform.MacOS;
using Bathos.Platform.Windows;

public static class CompositionExample
{
    public static IPlatformProvider Create(INativeDispatcher? dispatcher = null) =>
        PlatformProviders.SelectCurrent(new WindowsPlatformProvider(dispatcher),
            new MacOSPlatformProvider(dispatcher), new LinuxPlatformProvider());
}
```

This opts out of Linux sampling; use the explicit Linux opt-in below when your host supports it. A null dispatcher
disables Windows/macOS sampling but leaves their file capabilities available. Register either the facade or individual
interfaces in your own composition system. No specific DI container is required or supplied.

| Signal | Meaning | Caller response |
| --- | --- | --- |
| Null capability property | Unsupported or not configured | Disable the corresponding action or configure the capability |
| Null sample result | The user canceled native selection | Leave the previous application value unchanged |
| Exception or failed rename outcome | The operation failed | Report the failure and preserve caller-owned files |
| Empty discovery list | No usable native registration found | Ask for another target or an explicit executable |

## Operation behavior

Use capability availability to decide which actions to offer, and handle cancellation separately from failure.
Await each operation even when its caller retires; discard a stale result while allowing owned cleanup to finish.
File publication owns staging until success; reveal and application handoff leave the caller's file ownership unchanged.

Cancellation tokens can prevent publication or handoff before its native boundary. After success, the result remains
successful even if the token is later canceled. For sampling, caller retirement only suppresses application of the result;
it does not cancel the native picker. Keep its dispatcher alive until completion.

Use documented outcomes and exception categories in application logic. Native failure details and concrete exception
types can differ by OS, API, or filesystem. Preserve those details for diagnostics, and avoid parsing exception messages
or assuming that the same failure has the same numeric error code on every platform.

## File publication and inspection

`FilePublication` owns a private temporary sibling of the destination file. Write to its borrowed `Stream`, then
publish once using `PublishAsync(new GeneratedFileName(stem, extension), token)` or
`PublishExactAsync(fileName, token, replaceExisting: false)`. Use an absolute destination directory; it is created
if needed. Each publication is single-use and must not be accessed concurrently.

Generated names retry native destination collisions: `Greeting.txt`, `Greeting (2).txt`, and so on. Exact names
fail on a collision unless the caller explicitly authorizes replacement. Staging is flushed and closed before
one native rename; there is no cross-volume copy/delete fallback. Atomic visibility does not promise survival
of a power failure because directory metadata is not synchronized.

Disposal removes abandoned staging. Once publication succeeds, the file belongs to the caller and survives
disposal or later cancellation. Windows and macOS provide native rename capabilities; Linux does not yet.

`platform.FileEntries` offers regular-file inspection. `RequireRegularFile(path)` rejects missing or nonregular
entries. Like other filesystem checks, it describes the observed entry rather than reserving it against later changes.

Use `await using` even when publication can be canceled or throw. Once a publication attempt begins, create a new
operation to retry a failure. Validation of an exact filename can fail before an attempt begins. Neither the borrowed
stream nor a publication instance supports concurrent access; independently owned publications may race for names.
Do not mutate the staging path through other filesystem APIs. Cleanup owns a pathname, not a defense against hostile
replacement of directory entries.

```csharp
using Bathos.Platform;
using Bathos.Platform.Files;

public static class PublicationExample
{
    public static async Task<string> PublishReportAsync(IPlatformProvider platform, string directory,
        ReadOnlyMemory<byte> contents, CancellationToken token)
    {
        var rename = platform.FileRename ?? throw new PlatformNotSupportedException("Publication is unavailable.");
        await using var publication = new FilePublication(Path.GetFullPath(directory), rename);
        await publication.Stream.WriteAsync(contents, token);
        return await publication.PublishExactAsync("report.txt", token); // Never replace an existing report implicitly.
    }
}
```

`GeneratedFileName` bounds and normalizes portable filename components; it does not choose an application naming
policy or reserve sequence numbers. If using `INativeFileRename` directly, inspect both `FileRenameStatus` and the
OS-specific error code. `Unavailable` covers native failures such as missing sources or denied access; it is not a
substitute for capability absence. `CrossVolumeUnsupported` does not authorize a copy/delete fallback.

## Reveal and open in an application

`FileRevealer.RevealAsync(path, token)` asks the native file manager to select an existing file or directory in
its containing folder. `FileInvoker.OpenAsync(path, target, token)` hands an existing file to a host-selected
application. Both require absolute filesystem paths, work without a sampler dispatcher, and keep file ownership
with the caller.

```csharp
using Bathos.Platform;
using Bathos.Platform.Desktop;

public static class DesktopExample
{
    public static async Task RevealAndOpenAsync(IPlatformProvider platform, string filePath,
        ApplicationTarget selectedApplication, CancellationToken token)
    {
        await (platform.FileRevealer ?? throw new PlatformNotSupportedException()).RevealAsync(filePath, token);
        await (platform.FileInvoker ?? throw new PlatformNotSupportedException()).OpenAsync(filePath, selectedApplication, token);
    }
}
```

The host supplies the target and chooses which application/version to use:

| Target factory | Supported route |
| --- | --- |
| `ApplicationTarget.Executable(absolutePath)` | Windows, macOS, or Linux executable accepting one file-path argument |
| `ApplicationTarget.MacOSBundle(absoluteBundlePath)` | macOS `.app` bundle, handed off through LaunchServices using `/usr/bin/open` |
| `ApplicationTarget.LinuxDesktopEntry(desktopId)` | Linux desktop-entry ID including `.desktop`, activated through native GIO metadata |

Native discovery helpers provide candidates without choosing an application or embedding product-specific rules:

- `WindowsApplicationDiscovery.FindByExecutableName` reads App Paths registrations across user/machine registry views.
- `MacOSApplicationDiscovery.FindByBundleIdentifier` queries LaunchServices.
- `LinuxApplicationDiscovery.FindByDesktopId` resolves a usable desktop entry in the XDG search directories.

These return empty lists for absent registrations. They are focused lookups rather than a complete application
inventory. Portable/unregistered executables can be supplied directly. No defaults or file associations are changed.

```csharp
using Bathos.Platform.Desktop;
using Bathos.Platform.Linux;
using Bathos.Platform.MacOS;
using Bathos.Platform.Windows;

public static class DiscoveryExample
{
    public static IReadOnlyList<ApplicationTarget> Find(string nativeKey)
    {
        if (OperatingSystem.IsWindows()) return WindowsApplicationDiscovery.FindByExecutableName(nativeKey);
        if (OperatingSystem.IsMacOS()) return MacOSApplicationDiscovery.FindByBundleIdentifier(nativeKey);
        if (OperatingSystem.IsLinux()) return LinuxApplicationDiscovery.FindByDesktopId(nativeKey);
        throw new PlatformNotSupportedException();
    }
}
```

Supply a key in the current platform's native format (an `.exe` filename, a bundle identifier, or a `.desktop` ID).
An empty result is not an error; lookup exceptions represent a different failure. The returned order is not a ranking.
Present candidates to the user or apply your own policy rather than silently selecting a fallback.

Handoff success does not establish that the application loaded the document, supports its format, or became the
foreground application. Executable targets must support a plain file argument. Application-specific automation,
extra command-line arguments, and packaged Windows application IDs are outside this API. Cancellation is honored
before handoff; a later cancellation does not reinterpret a successful request or terminate the application.
Failures throw, and a failed explicit target never falls back to the default application.

Windows reveal uses the Shell selection API on an owned COM worker. macOS uses the system `open -R` helper.
Experimental Linux reveal uses `org.freedesktop.FileManager1.ShowItems` over system libdbus, without a folder-only
fallback. Linux desktop-entry lookup/activation requires system GIO, GLib, and GObject (`libgio-2.0.so.0`,
`libglib-2.0.so.0`, `libgobject-2.0.so.0`); executable invocation does not require those libraries.
Provider construction does not probe desktop services. Missing services and libraries are operation failures.

## Screen color selection

Samples are opaque eight-bit RGB for ordinary color selection. Values depend on the platform's sampling path
and may differ from source content or perceived display appearance. Colorimetric accuracy, display-profile
matching, and consistent HDR behavior are not promised. Specialized color tools should handle those requirements.

For macOS or Windows, supply `INativeDispatcher` to `MacOSPlatformProvider` or `WindowsPlatformProvider`.
It must verify the native UI thread and defer posted work until the current callback unwinds. Invoke
`SampleAsync()` on that thread and keep the native event loop alive through completion. The library does not
create a host UI event loop or adapt a particular UI toolkit's dispatcher.

AppKit owns macOS selection and Escape. Windows shows a frozen GDI snapshot of the virtual desktop with a
crosshair; left-click selects, and Escape, right-click, or leaving the picker cancels. It samples the snapshot
taken when selection starts, including across displays, without tracking subsequent screen changes.

Enable experimental Linux sampling with `new LinuxPlatformProvider(enableScreenColorSampler: true)`. It requires
system `libdbus-1.so.3`, a session bus, and a desktop portal backend implementing
`org.freedesktop.portal.Screenshot.PickColor`. The portal owns interaction; no host dispatcher is needed.
Optionally pass `parentWindow` as the host's exported `x11:` or `wayland:` portal window identifier; the default
empty string requests an unparented picker. Opt-in does not probe the services; missing dependencies and failed
requests throw when sampling is attempted.

A null screen sample means user cancellation. On every platform, retiring a caller does not dismiss selection;
ignore its late result and await native completion. Native cleanup belongs to the outstanding operation.

### Applying a result to a live caller

Call the following from the host's native UI thread. The validity predicate and result callback also run on that
thread. Set the caller's validity state to false when it retires; keep the loop and dispatcher alive until this task
finishes. This pattern works without assuming that the native UI loop installs a .NET synchronization context.

```csharp
using Bathos.Platform;

public static class SamplingExample
{
    public static async Task SampleForCallerAsync(IScreenColorSampler sampler, INativeDispatcher dispatcher,
        Func<bool> callerIsCurrent, Action<ScreenColor> applyColor)
    {
        dispatcher.VerifyAccess();
        var color = await sampler.SampleAsync().ConfigureAwait(false);
        var applied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        dispatcher.Post(() =>
        {
            try
            {
                if (callerIsCurrent() && color is { } selected) applyColor(selected);
                applied.SetResult();
            }
            catch (Exception error) { applied.SetException(error); }
        });
        await applied.Task.ConfigureAwait(false);
    }
}
```

The dispatcher must enqueue asynchronously, never run inline, and must not drop cleanup work during shutdown.
Its `VerifyAccess` implementation must check the actual native UI thread, including the macOS main-thread constraint.
Adapt your existing host dispatcher; see the [WPF dispatcher example](examples/wpf-dispatcher.md) for a concrete adapter
and UI event-handler integration. The engineering sampler hosts demonstrate AppKit and Win32 event loops for manual
validation.
Only one built-in sampling operation per platform implementation may be active at a time.

## Prerequisites and troubleshooting

| Symptom | Check |
| --- | --- |
| Capability is null | Provider selection, supplied dispatcher, and explicit Linux sampler opt-in |
| Invalid path or target | Absolute filesystem paths; one filename for exact publication; native registration ID syntax |
| File inspection fails | Missing entry, directory, symbolic link/reparse point, special entry, or access restriction |
| Publication fails | Destination collision, permissions, filename limits, filesystem rename support, or cross-volume paths |
| Linux native library load fails | System libdbus for reveal/sampling; GIO, GLib, and GObject for desktop-entry lookup/handoff; libc statx for inspection |
| Linux desktop service rejects a request | Logged-in session bus, FileManager1 service, or portal backend implementing PickColor; headless CI does not establish these |
| Windows/macOS sampler is busy or rejects thread access | Await the previous operation and invoke on the configured native UI thread |
| Picker completes but the host hangs during shutdown | Keep its native event loop alive through cleanup and result dispatch; await owned work rather than pumping a nested loop |
| Application launches but does not open the file | Verify the target accepts a single file argument or the selected native registration supports the format |

Native failure exception types can vary by OS and boundary (filesystem exceptions, Win32/COM errors, or loader
exceptions). Do not treat every exception as unsupported capability or user cancellation. Preserve the original
exception details for diagnostics; do not silently retry an explicit handoff through another application.

## Optional partial providers

The built-in providers are the normal integration path. For individual substitutions or test doubles, supply a focused
capability interface directly. If your host benefits from grouping a particular subset of capabilities, a partial provider
is also available as a composition technique. Implement `IPlatformProvider` with an OS identity and only those capabilities;
all optional getters default to null. For example, this provider exposes file reveal:

```csharp
using Bathos.Platform;
using Bathos.Platform.Desktop;

public sealed class RevealOnlyProvider : IPlatformProvider
{
    public RevealOnlyProvider(PlatformOperatingSystem operatingSystem, IFileRevealer revealer)
    {
        ArgumentNullException.ThrowIfNull(revealer);
        OperatingSystem = operatingSystem;
        FileRevealer = revealer;
    }

    public PlatformOperatingSystem OperatingSystem { get; }
    public IFileRevealer FileRevealer { get; }
}
```

Inspect the full capability set through an `IPlatformProvider` reference; C# does not add default interface members
to the implementing class's own surface. Selection returns that interface, so callers can check every optional property.
Guard missing capabilities when composing the host or presenting optional actions. Pass the resulting focused interfaces
to application components so they depend only on the operations they use.

New capabilities follow the same pattern: a focused interface and a default-null facade property. Existing providers
can omit a new optional capability. An exposed capability must fulfill its operation contract; a successful no-op or a
throwing placeholder does not represent a supported operation.

## Validation from source

The engineering-only desktop host supports `reveal`, `open`, and native lookup. The sampler harness supports
manual interaction and a separate noninteractive dispatcher smoke check on Windows/macOS. Neither harness is
included in the NuGet packages.

See [native validation](validation.md) for commands, human acceptance procedures, and manual checks.
Headless Linux tests establish protocol and transport behavior, not graphical desktop support.

## Installed color profiles

The optional `IColorProfileProvider` capability discovers installed profiles and reads their current bytes. Keys are
opaque provider references; do not convert them into filesystem paths or persist them as portable identities. A scan
can be incomplete while retaining useful entries. `Finding` is informational diagnostic text, not a status code.
Read failure is separate from inventory completeness; cancellation throws rather than returning partial success.

macOS observes ColorSync installed profiles and current device associations. Device names are labels, not stable
identities or a guarantee of display accuracy. Windows observes user/system ICC/WCS inventory; it does not evaluate
device defaults or advanced-color/HDR profiles. Linux currently has no built-in capability. Native calls execute on
worker threads without a native UI dispatcher; cancellation joins an already running native call.

Built-in defaults bound scans to 10,000 distinct profiles and individual reads to 16 MiB. Windows additionally bounds
each native enumeration buffer to 4 MiB. To select different count/read bounds, construct `MacColorProfileProvider`
or `WindowsColorProfileProvider` with `ColorProfileAccessLimits` and inject that focused capability directly.
Returned bytes are not inspected or converted by this library. Keep ICC validation and catalog policy in the consumer.

```csharp
using Bathos.Platform;
using Bathos.Platform.ColorProfiles;
using Bathos.Platform.MacOS;
using Bathos.Platform.Windows;

public static class ProfileDiscoveryExample
{
    public static async Task<ColorProfileInventory> ScanAsync(CancellationToken token)
    {
        var platform = PlatformProviders.SelectCurrent(new MacOSPlatformProvider(), new WindowsPlatformProvider());
        var profiles = platform.ColorProfiles ?? throw new PlatformNotSupportedException("Profile discovery is unavailable.");
        return await profiles.EnumerateAsync(token);
    }

    public static ValueTask<ReadOnlyMemory<byte>> ReadAsync(IColorProfileProvider profiles, ColorProfileLocation entry,
        CancellationToken token) => profiles.ReadAsync(entry.Key, token);
}
```

## Native paths and portable names

`IPlatformProvider.FilePaths` supplies host-native lexical operations on Windows, macOS, and Linux. The capability
requires its matching runtime; it does not emulate Windows parsing on POSIX hosts or reinterpret foreign references.
Windows admits absolute drive and UNC paths, with either separator; device/extended namespaces are excluded. POSIX
admits local absolute slash paths; ambiguous double-slash network roots and drive-shaped paths are excluded.
Relative input must be resolved against an explicit host-owned base before calling the capability.

```csharp
using Bathos.Platform.Paths;

public static class PathExample
{
    public static string DirectoryReference(INativeFilePaths paths, string absoluteDirectory) =>
        paths.ToFileUri(absoluteDirectory, directory: true);

    public static bool IsPortableNewName(string name) => PortableFileNames.IsValid(name);
}
```

Normalization collapses lexical dot segments without filesystem probing. Encoding preserves significant whitespace,
Unicode, and literal URI punctuation; it never sanitizes an existing filename. Encoding rejects malformed Unicode
instead of replacing it. URI decoding rejects queries, fragments, credentials, ports, malformed escapes or UTF-8,
encoded separators, and unsupported foreign evidence. A literal backslash in a POSIX filename is escaped and preserved.
Directory references explicitly request a trailing slash.
Windows normalization preserves trailing dots/spaces and short-name spellings rather than applying Win32 trimming
or filesystem-based short-name expansion. This lexical preservation does not establish how a later filesystem API
will interpret the path.

Comparison is host policy: choose an ordinal or ordinal-ignore-case comparer deliberately; neither proves filesystem
identity. Alias resolution, network coordination, and ownership across alternate representations are not guaranteed.
Hosts requiring exclusion must supply consistent locations and keep lock locations and containing directories stable
throughout ownership. This path capability itself does not acquire a lease or enforce those host obligations.

`PortableFileNames.IsValid` is an opt-in policy for new single names, not a native-path validator. It rejects reserved
Windows device stems and bare `CONIN$`/`CONOUT$` names, path separators, Windows-reserved punctuation, controls,
malformed Unicode, trailing dots/spaces, and names exceeding 255 UTF-8 bytes or UTF-16 code units. It never modifies input.
`GeneratedFileName` remains the separate opt-in generator with its existing sanitization and collision-suffix behavior.
Unlike legacy device stems such as `CON`, `CONIN$` and `CONOUT$` are admitted when followed by an extension.
