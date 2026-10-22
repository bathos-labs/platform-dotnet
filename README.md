# Bathos Platform for .NET

Native desktop capabilities for .NET applications: publish files atomically, inspect file entries, reveal files in
Finder or Explorer, open documents in a selected application, and pick a screen color.

Bathos Platform keeps these operations independent of your UI toolkit and application model. The host chooses its
providers, application targets, and native dispatcher. Use it with your own composition or DI setup; the packages
have no UI-toolkit, DI-container, or image-processing dependencies.

**First beta: `0.1.0-beta.1` · .NET 10 · MIT license.** Prerelease APIs may change. Linux support is experimental.

## Packages

Install the providers your application needs. Each provider brings the core package transitively and references
only the core and .NET runtime; sibling providers are optional.

| NuGet package | What it provides |
| --- | --- |
| [Bathos.Platform](https://www.nuget.org/packages/Bathos.Platform/0.1.0-beta.1) | Capability interfaces, explicit OS/provider selection, application targets, and staged file publication with collision-safe naming. Requires a native rename provider for publication. |
| [Bathos.Platform.Windows](https://www.nuget.org/packages/Bathos.Platform.Windows/0.1.0-beta.1) | Native file rename/replacement and inspection, Explorer reveal, executable handoff, App Paths lookup, and an optional GDI screen sampler. |
| [Bathos.Platform.MacOS](https://www.nuget.org/packages/Bathos.Platform.MacOS/0.1.0-beta.1) | Native file rename/replacement and inspection, Finder reveal, executable/bundle handoff, LaunchServices lookup, and an optional AppKit screen sampler. |
| [Bathos.Platform.Linux](https://www.nuget.org/packages/Bathos.Platform.Linux/0.1.0-beta.1) | Experimental statx inspection, file-manager reveal, executable/desktop-entry handoff, GIO lookup, and an optional portal screen sampler. Native rename and file publication are unavailable. |

Each package contains a `net10.0` library, XML API documentation, this README, and MIT license text. Portable PDB
symbols are published separately as `.snupkg` files. The engineering harnesses and test applications are not shipped.
Native OS frameworks and Linux system libraries are used from the host rather than bundled in the packages.

## Get started

For an application targeting Windows and macOS:

```text
dotnet add package Bathos.Platform.Windows --version 0.1.0-beta.1
dotnet add package Bathos.Platform.MacOS --version 0.1.0-beta.1
```

This console example writes a complete file to private staging, then publishes it under the first available name:

```csharp
using Bathos.Platform;
using Bathos.Platform.Files;
using Bathos.Platform.MacOS;
using Bathos.Platform.Windows;

var platform = PlatformProviders.SelectCurrent(new WindowsPlatformProvider(), new MacOSPlatformProvider());
var rename = platform.FileRename ?? throw new PlatformNotSupportedException("Native publication is unavailable.");
await using var publication = new FilePublication(Path.GetFullPath("published-files"), rename);
await publication.Stream.WriteAsync("Hello from Bathos Platform!\n"u8.ToArray());
var path = await publication.PublishAsync(new GeneratedFileName("Greeting", ".txt"), CancellationToken.None);
Console.WriteLine(path); // Greeting.txt, Greeting (2).txt, ...; existing files are preserved.
```

Disposal removes abandoned staging; a published file survives disposal. `PublishExactAsync` supports an exact
filename and explicitly authorized replacement. Publication uses a native rename without a cross-volume copy
fallback. Atomic visibility does not guarantee survival of a power failure.

## Desktop integration

`platform.FileRevealer.RevealAsync(path)` selects a file or directory in its containing folder.
`platform.FileInvoker.OpenAsync(path, target)` hands a file to an application selected by the host.
Both use absolute paths and keep file ownership with the caller. A successful handoff means submission, not that
the document has loaded or the application has focus.

Targets can be an absolute executable path, a macOS `.app` bundle, or a Linux desktop-entry ID. Focused native
lookup helpers support Windows App Paths, macOS bundle identifiers, and Linux desktop entries. The host chooses
the target; no product-specific rules or file-association changes are imposed.

Screen sampling is optional. On Windows and macOS, supply an `INativeDispatcher` backed by your existing native UI
loop. On Linux, opt in to the desktop portal sampler. Samples are opaque eight-bit RGB for basic color selection;
display-profile accuracy and consistent HDR behavior are outside the contract.

A null capability means unsupported or unconfigured; a null screen sample means user cancellation. Operation
failures remain distinct and are reported to the caller. OS detection alone does not establish native-service or
permission availability.

## Support and documentation

Windows and macOS have native automated test coverage and manually confirmed sampler harness tests. Linux has
native headless test coverage, including portal/file-manager protocol tests and GIO activation, but no interactive
desktop acceptance record. Finder/Explorer reveal and real-editor handoff still need manual acceptance.
See the [validation record](https://github.com/bathos-labs/platform-dotnet/blob/master/docs/validation.md) for the
specific hosts, scenarios, and remaining gaps.

- [Integration guide](https://github.com/bathos-labs/platform-dotnet/blob/master/docs/usage.md): publication, application targets, sampler dispatch, and native prerequisites.
- [Changelog](https://github.com/bathos-labs/platform-dotnet/blob/master/CHANGELOG.md) and [releases](https://github.com/bathos-labs/platform-dotnet/releases): beta notes and published artifacts.
- [Architecture](https://github.com/bathos-labs/platform-dotnet/blob/master/docs/architecture.md): capability boundaries and resource ownership.
- [Contributing](https://github.com/bathos-labs/platform-dotnet/blob/master/CONTRIBUTING.md) and [publishing](https://github.com/bathos-labs/platform-dotnet/blob/master/RELEASING.md): source builds, validation, and NuGet releases.
- [GitHub Issues](https://github.com/bathos-labs/platform-dotnet/issues): bugs and capability requests.

For source development, install the SDK pinned in `global.json` and run `task check`. Task is optional; the
contribution guide includes the equivalent SDK commands.
