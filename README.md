# Bathos Platform for .NET

Native file publication, file inspection, screen color selection, and desktop file handoff for .NET applications.
Use these capabilities without coupling your application to a UI toolkit or dependency-injection container.
MIT licensed. Built for .NET 10;
currently prerelease, with no stable API compatibility promise yet.

| Package | Capability |
| --- | --- |
| `Bathos.Platform` | Facade and capability interfaces, application targets, OS probe/provider selection, atomic file publication |
| `Bathos.Platform.MacOS` | File publication/inspection, Finder reveal, application handoff and discovery, optional AppKit screen sampler |
| `Bathos.Platform.Windows` | File publication/inspection, Explorer reveal, executable handoff and discovery, optional GDI screen sampler |
| `Bathos.Platform.Linux` | Experimental file inspection, file-manager reveal, application handoff and discovery, optional portal sampler; rename is unavailable |

Providers reference only the core and runtime. Include only the providers your application needs. No UI toolkit,
image-processing library, DI container, global mutable provider, or assembly scanning is required.

## Try it from source

The initial release is still being prepared; this checkout can be consumed through project references.
Install the SDK pinned in `global.json`. From the repository root, create a console application outside the checkout:

```text
dotnet new console --framework net10.0 --output ../platform-example
dotnet add ../platform-example reference src/Bathos.Platform.MacOS/Bathos.Platform.MacOS.csproj
dotnet add ../platform-example reference src/Bathos.Platform.Windows/Bathos.Platform.Windows.csproj
```

Replace its `Program.cs` with this example, then run `dotnet run --project ../platform-example` on macOS or Windows:

```csharp
using Bathos.Platform;
using Bathos.Platform.Files;
using Bathos.Platform.MacOS;
using Bathos.Platform.Windows;

var platform = PlatformProviders.SelectCurrent(new MacOSPlatformProvider(), new WindowsPlatformProvider());
var rename = platform.FileRename ?? throw new PlatformNotSupportedException("Native publication is unavailable.");
var directory = Path.GetFullPath("published-files");
await using var publication = new FilePublication(directory, rename);
await publication.Stream.WriteAsync("Hello from Bathos Platform!\n"u8.ToArray());
var path = await publication.PublishAsync(new GeneratedFileName("Greeting", ".txt"), CancellationToken.None);
Console.WriteLine(path); // Greeting.txt, Greeting (2).txt, ...; existing files are preserved.
var probe = platform.FileEntries ?? throw new PlatformNotSupportedException("File inspection is unavailable.");
probe.RequireRegularFile(path);
```

Disposal removes abandoned staging files. A successful publication survives disposal. To use a specific filename,
call `PublishExactAsync("Greeting.txt", token)`; an occupied name fails unless you explicitly pass
`replaceExisting: true`. Neither mode falls back to cross-volume copy/delete. Atomic visibility does not promise
survival of a power failure: publication flushes file contents but does not synchronize directory metadata.

Once packages are published, select the provider packages you need from NuGet; each brings the core transitively.
See the [release instructions](https://github.com/bathos-labs/platform-dotnet/blob/master/RELEASING.md) for local package feeds and publication.

## Reveal and open in an application

`FileRevealer.RevealAsync(path, token)` asks the native file manager to select an existing file or directory in its
containing folder. `FileInvoker.OpenAsync(path, target, token)` hands an existing file to a host-selected application.
Both require absolute filesystem paths and work without a sampler dispatcher. The caller keeps ownership of the file.

```csharp
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
`WindowsApplicationDiscovery.FindByExecutableName` reads App Paths registrations across user/machine registry views;
`MacOSApplicationDiscovery.FindByBundleIdentifier` queries LaunchServices; and
`LinuxApplicationDiscovery.FindByDesktopId` resolves a usable desktop entry in the XDG search directories.
They return empty lists for absent registrations. These are focused lookups, not a complete installed-application
inventory. Portable/unregistered executables can be supplied directly. No defaults or file associations are changed.

Handoff success does not establish that the application loaded the document, supports its format, or became the
foreground application. Executable targets must support a plain file argument; application-specific automation,
extra command-line arguments, and packaged Windows application IDs are outside this API. Cancellation is honored
before handoff; a later cancellation does not reinterpret a successful request or terminate the selected application.
Operation failures throw, and a failed explicit target never falls back to the default application.

Windows reveal uses the Shell selection API on an owned COM worker. macOS uses the system `open -R` helper.
Experimental Linux reveal uses `org.freedesktop.FileManager1.ShowItems` over system libdbus, without a folder-only
fallback. Linux desktop-entry lookup/activation requires system GIO, GLib, and GObject (`libgio-2.0.so.0`,
`libglib-2.0.so.0`, `libgobject-2.0.so.0`); executable invocation does not require those libraries.
No desktop service is probed during provider construction. Missing services and libraries are operation failures.

For manual validation, run `dotnet run --project eng/Bathos.Platform.Desktop --` followed by `reveal <absolute-path>`,
`open <absolute-file> <executable|bundle|desktop-entry> <target>`, or `find <native-identifier>`.
See the [validation procedure](docs/validation.md) before recording interactive acceptance.

## Capability contracts

A host may register the facade or individual capabilities in DI, or retain it in an application-owned wrapper.
Null capabilities mean unsupported or unconfigured. Operation failures throw or return explicit native outcomes;
null screen samples mean user cancellation. OS selection does not establish permissions or filesystem support.

Screen samples are opaque eight-bit RGB for ordinary color selection. Values depend on the platform's sampling path
and may differ from source content or perceived display appearance. No colorimetric accuracy, display-profile
matching, or consistent HDR behavior is promised. Specialized color tools should handle those requirements.

For macOS or Windows screen sampling, supply `INativeDispatcher` to `MacOSPlatformProvider` or
`WindowsPlatformProvider`. It must verify the native UI thread and defer posted work until the current callback
unwinds. Invoke `SampleAsync()` on that thread and keep the native event loop alive through completion.
AppKit owns macOS selection and Escape. Windows shows a frozen GDI snapshot of the virtual desktop with a crosshair;
left-click selects, and Escape, right-click, or leaving the picker cancels. It samples the snapshot taken when
selection starts, including across displays; it does not track subsequent screen changes.

Linux support remains experimental pending desktop and broader filesystem validation. Enable sampling explicitly
with `new LinuxPlatformProvider(enableScreenColorSampler: true)`. It requires system `libdbus-1.so.3`, a session bus,
and an installed desktop portal backend implementing `org.freedesktop.portal.Screenshot.PickColor`.
The portal owns interaction; no host dispatcher is needed. Optionally pass `parentWindow` as the host's exported
`x11:` or `wayland:` portal window identifier; the default empty string requests an unparented picker.
Enabling the capability does not probe these services: missing libraries/services and failed requests throw when
sampling is attempted. Automated headless protocol tests do not establish desktop support. Linux rename remains unavailable.

On every platform, retiring a caller does not dismiss selection; ignore its late result and await native completion.
Run `task eng:sampler` in a graphical macOS or Windows session for the engineering-only manual validation harness. Move the window,
then use its buttons to start individual tests; instructions, results, and a sample color preview with its hex value
stay in the window. Each test can be repeated.
The interactive picker requires human input; computer-use automation is currently unreliable for this path.
`task eng:sampler:smoke` checks native dispatch and shutdown on macOS and Windows without opening the picker.
On Linux, `task eng:sampler` supplies a console-driven manual portal check. The macOS and Windows sampler tests have been manually confirmed; see the [validation record](https://github.com/bathos-labs/platform-dotnet/blob/master/docs/validation.md)
for the procedure and host-specific evidence.

See [architecture](https://github.com/bathos-labs/platform-dotnet/blob/master/docs/architecture.md), [contribution and validation](https://github.com/bathos-labs/platform-dotnet/blob/master/CONTRIBUTING.md), and
[NuGet releases](https://github.com/bathos-labs/platform-dotnet/blob/master/RELEASING.md). Package availability follows published releases; repository builds alone do not
publish to NuGet.org.

For local development, run `task check` to validate and `task --list` to see focused commands. Task is optional;
the contribution guide includes direct SDK commands. Consumer-facing release notes live in the
[changelog](https://github.com/bathos-labs/platform-dotnet/blob/master/CHANGELOG.md).
