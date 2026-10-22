# Integration guide

The [README](../README.md) covers package selection and a runnable publication example. All packages target
.NET 10. Providers can be retained directly, selected with `PlatformProviders.SelectCurrent`, or registered in
the host's own composition system. The core does not load or scan provider assemblies.

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

## Reveal and open in an application

`FileRevealer.RevealAsync(path, token)` asks the native file manager to select an existing file or directory in
its containing folder. `FileInvoker.OpenAsync(path, target, token)` hands an existing file to a host-selected
application. Both require absolute filesystem paths, work without a sampler dispatcher, and keep file ownership
with the caller.

```csharp
using Bathos.Platform;
using Bathos.Platform.Desktop;

static async Task RevealAndOpenAsync(IPlatformProvider platform, string filePath,
    ApplicationTarget selectedApplication, CancellationToken token)
{
    await (platform.FileRevealer ?? throw new PlatformNotSupportedException()).RevealAsync(filePath, token);
    await (platform.FileInvoker ?? throw new PlatformNotSupportedException()).OpenAsync(filePath, selectedApplication, token);
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

## Validation from source

The engineering-only desktop host supports `reveal`, `open`, and native lookup. The sampler harness supports
manual interaction and a separate noninteractive dispatcher smoke check on Windows/macOS. Neither harness is
included in the NuGet packages.

See [native validation](validation.md) for commands, human acceptance procedures, and host-specific evidence.
Headless Linux tests establish protocol and transport behavior, not graphical desktop support.
