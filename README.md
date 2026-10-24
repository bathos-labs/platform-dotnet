# Native platform abstractions for .NET

[![NuGet](https://img.shields.io/nuget/vpre/Bathos.Platform?label=NuGet)](https://www.nuget.org/packages/Bathos.Platform)
[![Native validation](https://github.com/bathos-labs/platform-dotnet/actions/workflows/validate.yml/badge.svg?branch=master)](https://github.com/bathos-labs/platform-dotnet/actions/workflows/validate.yml)
[![Codecov](https://img.shields.io/codecov/c/github/bathos-labs/platform-dotnet?branch=master)](https://app.codecov.io/gh/bathos-labs/platform-dotnet)

Common .NET interfaces for native OS operations: atomic file publication, file inspection and leases, desktop
integration, screen color sampling, installed color profiles, native paths, and standard directories.

The `Bathos.Platform` packages keep platform-specific interop behind focused capability interfaces. Choose the
Windows, macOS, or experimental Linux providers your application needs, and check which capabilities are available.

Your application chooses its UI toolkit and composition approach, selects application targets for file handoff,
and supplies a native dispatcher when needed for screen sampling. The libraries have no UI toolkit,
dependency injection container, or image-processing dependencies.

**.NET 10 · [MIT license](https://github.com/bathos-labs/platform-dotnet/blob/master/LICENSE).** Public APIs follow Semantic Versioning. Linux support is experimental.

## Packages

Install the providers your application needs. Each depends only on the core package and .NET runtime;
the core is included as a transitive dependency.

| NuGet package | What it provides |
| --- | --- |
| [Bathos.Platform](https://www.nuget.org/packages/Bathos.Platform) | Capability interfaces, provider selection, application targets, and atomic file publication with collision-safe naming. Publication requires a native rename provider. |
| [Bathos.Platform.Windows](https://www.nuget.org/packages/Bathos.Platform.Windows) | Native file rename/replacement and inspection, Explorer reveal, executable handoff, App Paths lookup, installed color profiles, and an optional GDI screen sampler. |
| [Bathos.Platform.MacOS](https://www.nuget.org/packages/Bathos.Platform.MacOS) | Native file rename/replacement and inspection, Finder reveal, executable/bundle handoff, LaunchServices lookup, installed color profiles, and an optional AppKit screen sampler. |
| [Bathos.Platform.Linux](https://www.nuget.org/packages/Bathos.Platform.Linux) | Experimental statx inspection, file-manager reveal, executable/desktop-entry handoff, GIO lookup, and an optional portal screen sampler. Native rename and file publication are unavailable. |

All three providers also support native paths, standard directories, and cooperative file leases.

Each package contains a `net10.0` library, XML API documentation, this README, and MIT license text. Portable PDB
symbols are published separately as `.snupkg` files. The engineering harnesses and test applications are not shipped.
Native OS frameworks and Linux system libraries are used from the host rather than bundled in the packages.

## Get started

For an application targeting Windows and macOS, install the latest stable packages:

```text
dotnet add package Bathos.Platform.Windows
dotnet add package Bathos.Platform.MacOS
```

Add `--prerelease` to include prerelease builds.

This console example writes a complete file to private staging, then publishes it under the first available name:

```csharp
using Bathos.Platform;
using Bathos.Platform.Files;
using Bathos.Platform.MacOS;
using Bathos.Platform.Windows;

var platform = PlatformProviders.SelectCurrent(new WindowsPlatformProvider(), new MacOSPlatformProvider());
var rename = platform.FileRename ?? throw new PlatformNotSupportedException("Native publication is unavailable.");
await using var publication = new FilePublication(Path.GetFullPath("published-files"), rename);
await publication.Stream.WriteAsync("Hello from .NET!\n"u8.ToArray());
var path = await publication.PublishAsync(new GeneratedFileName("Greeting", ".txt"), CancellationToken.None);
Console.WriteLine(path); // Greeting.txt, Greeting (2).txt, ...; existing files are preserved.
```

Disposal removes abandoned staging; a published file survives disposal. `PublishExactAsync` supports an exact
filename and explicitly authorized replacement. Publication uses a native rename without a cross-volume copy
fallback. Atomic visibility does not guarantee survival of a power failure.

## Desktop integration

When available, `platform.FileRevealer` selects a file or directory in its containing folder, and
`platform.FileInvoker` opens a file in an application selected by the host.
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

Use the provider facade to compose your application, then pass focused capability interfaces to the components
that use them. You can substitute implementations or test doubles through those interfaces.

## Color profiles

`platform.ColorProfiles` discovers installed profiles and reads their raw bytes on macOS and Windows. It returns
provider-owned lookup keys, without ICC parsing, installation, or conversion. Linux leaves this optional capability
null. See the [integration guide](https://github.com/bathos-labs/platform-dotnet/blob/master/docs/usage.md#installed-color-profiles)
for limits and partial discovery.

## Paths, directories, and file leases

`platform.FilePaths` normalizes native paths and converts file URIs without probing the filesystem.
`platform.Directories` discovers standard user and application roots without creating directories or claiming access.
Applications choose their storage layout and create the directories they need.

`platform.FileLeases` supports shared owners that coexist and exclusive owners that exclude every other owner.
Each acquisition specifies its mode and requires a persistent lock artifact at a stable location. Keep the artifact
and its containing directory tree intact while leased. If application data may move, place its lock outside that tree.
Artifact existence and diagnostic metadata do not establish ownership. See the
[integration guide](https://github.com/bathos-labs/platform-dotnet/blob/master/docs/usage.md#cooperative-file-leases)
for acquisition, disposal, and the limits of coordination.

## Support and documentation

Repository documentation describes current development. For the version you installed, use the XML API documentation
shipped with the assemblies and the corresponding source tag listed in [releases](https://github.com/bathos-labs/platform-dotnet/releases).

Stable releases preserve public API contracts for callers and implementers within a major version. Minor releases can
add optional capabilities; breaking contract changes require a new major version. Linux native support remains
experimental; public API compatibility still applies. See the [native validation guide](https://github.com/bathos-labs/platform-dotnet/blob/master/docs/validation.md)
for automated evidence and manual desktop checks.

- [Integration guide](https://github.com/bathos-labs/platform-dotnet/blob/master/docs/usage.md): capability usage, examples, and native prerequisites.
- [Changelog](https://github.com/bathos-labs/platform-dotnet/blob/master/CHANGELOG.md) and [releases](https://github.com/bathos-labs/platform-dotnet/releases): release notes and published artifacts.
- [Architecture](https://github.com/bathos-labs/platform-dotnet/blob/master/docs/architecture.md): capability boundaries and resource ownership.
- [Contributing](https://github.com/bathos-labs/platform-dotnet/blob/master/CONTRIBUTING.md) and [publishing](https://github.com/bathos-labs/platform-dotnet/blob/master/RELEASING.md): source builds, validation, and NuGet releases.
- [GitHub Issues](https://github.com/bathos-labs/platform-dotnet/issues): bugs and capability requests.

For source development, install the SDK pinned in `global.json` and run `task check`. Task is optional; the
contribution guide includes the equivalent SDK commands.

Copyright © 2026 Bathos Pty Ltd.
