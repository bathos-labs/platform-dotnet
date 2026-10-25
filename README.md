# Native platform abstractions for .NET

[![NuGet](https://img.shields.io/nuget/v/Bathos.Platform?label=NuGet)](https://www.nuget.org/packages/Bathos.Platform)
[![Native validation](https://github.com/bathos-labs/platform-dotnet/actions/workflows/validate.yml/badge.svg?branch=master)](https://github.com/bathos-labs/platform-dotnet/actions/workflows/validate.yml)
[![Codecov](https://img.shields.io/codecov/c/github/bathos-labs/platform-dotnet?branch=master)](https://app.codecov.io/gh/bathos-labs/platform-dotnet)

Common .NET interfaces for native OS operations: atomic file publication, file inspection and
leases, desktop integration, screen color sampling, installed color profiles, native paths, and
standard directories.

Choose the providers your application needs, compose them explicitly, and pass focused capability
interfaces to the components that use them. The libraries have no UI toolkit, dependency injection
container, or image-processing dependencies. Your application owns its UI loop, application targets,
and storage policy.

**.NET 10 · [MIT license][license].** All four packages share a version and follow Semantic
Versioning. Linux implementations remain experimental; their public APIs have the same compatibility
commitment, and the Linux package follows the same stable/prerelease channel as the other packages.

## Capabilities

| Capability                                                  | Windows                | macOS                   | Linux                      |
| ----------------------------------------------------------- | ---------------------- | ----------------------- | -------------------------- |
| [Atomic file publication and rename][publication]           | Available              | Available               | Available                  |
| [Snapshot replacement with retained readers][publication]   | Available              | Available               | Available                  |
| [Regular-file inspection][publication]                      | Available              | Available               | Available                  |
| [Native paths and file URIs][paths]                         | Available              | Available               | Available                  |
| [Standard directories and application scoping][directories] | Available              | Available               | Available                  |
| [Cooperative shared/exclusive file leases][leases]          | Available              | Available               | Available                  |
| [File-manager reveal][desktop]                              | Explorer               | Finder                  | FileManager1 service       |
| [Application handoff and native lookup][desktop]            | Executable / App Paths | Executable / app bundle | Executable / desktop entry |
| [Screen color sampling][sampling]                           | Host dispatcher        | Host dispatcher         | Portal opt-in              |
| [Installed color profiles][profiles]                        | Available              | Available               | Unavailable                |

Availability describes the built-in implementations. Native operations require the matching OS and
may require filesystem support, desktop services, or permissions. A null capability means
unsupported or unconfigured; a null screen sample means user cancellation. Operation failures remain
distinct. Linux's experimental status applies throughout the table.

See [platform support and prerequisites][support] before deployment, including headless Linux use,
native library requirements, and the limits of the tested configurations.

## Packages

Install only the providers you intend to support. Each provider depends on the core package and the
.NET runtime; the core is included transitively.

- [Bathos.Platform](https://www.nuget.org/packages/Bathos.Platform): shared contracts and file
  publication.
- [Bathos.Platform.Windows](https://www.nuget.org/packages/Bathos.Platform.Windows): Windows
  implementations.
- [Bathos.Platform.MacOS](https://www.nuget.org/packages/Bathos.Platform.MacOS): macOS
  implementations.
- [Bathos.Platform.Linux](https://www.nuget.org/packages/Bathos.Platform.Linux): Linux
  implementations.

Packages contain `net10.0` libraries, XML API documentation, this README, and MIT license text.
Portable PDB symbols are published as `.snupkg` files. Native OS frameworks and Linux system
libraries come from the host. Engineering harnesses and test applications are not shipped.

## Get started

In a .NET 10 console project, install all three providers for the cross-platform example below.
These commands select the latest stable packages:

```text
dotnet add package Bathos.Platform.Windows
dotnet add package Bathos.Platform.MacOS
dotnet add package Bathos.Platform.Linux
```

Write a complete file to private staging, then publish it under the first available name:

```csharp
using Bathos.Platform;
using Bathos.Platform.Files;
using Bathos.Platform.Linux;
using Bathos.Platform.MacOS;
using Bathos.Platform.Windows;

var platform = PlatformProviders.SelectCurrent(
    new WindowsPlatformProvider(), new MacOSPlatformProvider(), new LinuxPlatformProvider());
var rename = platform.FileRename ?? throw new PlatformNotSupportedException("Native publication is unavailable.");
await using var publication = new FilePublication(Path.GetFullPath("published-files"), rename);
await publication.Stream.WriteAsync("Hello from .NET!\n"u8.ToArray());
var path = await publication.PublishAsync(new GeneratedFileName("Greeting", ".txt"), CancellationToken.None);
Console.WriteLine(path); // Greeting.txt, Greeting (2).txt, ...; existing files are preserved.
```

Disposal removes abandoned staging; a published file survives disposal. `PublishExactAsync` supports
an exact filename and explicitly authorized replacement. Publication requires a native rename and
does not fall back to cross-volume copying. Atomic visibility does not guarantee survival of a power
failure. See the [publication recipes][publication] for streaming, snapshot replacement, and
ownership.

## Documentation and support

- [Integration guide][usage]: composition, capability recipes, and operation contracts.
- [Platform support][support]: deployment prerequisites, configuration, and tested environments.
- [Changelog][changelog] and [releases](https://github.com/bathos-labs/platform-dotnet/releases):
  history and published artifacts.
- [Native validation][validation]: automated evidence, limitations, and manual desktop checks.
- [Architecture][architecture]: capability boundaries and resource ownership.
- [Contributing][contributing] and [releasing][releasing]: source development and publication.
- [GitHub Issues](https://github.com/bathos-labs/platform-dotnet/issues): bugs and capability
  requests.

The README and guides describe the source revision being viewed. Use the matching source tag or
shipped XML documentation for an older installed package. Package README links point to their
release snapshot. Stable public API contracts cover callers and implementers within a major version;
new capabilities are optional, and breaking contract changes require a new major version.

For source development, install the SDK pinned in `global.json` and Node.js with npm as described in
[Contributing][contributing], then run `task check`. Task is optional; equivalent commands are
listed there.

Copyright © 2026 Bathos Pty Ltd.

[license]: https://github.com/bathos-labs/platform-dotnet/blob/master/LICENSE
[usage]: https://github.com/bathos-labs/platform-dotnet/blob/master/docs/usage.md
[support]: https://github.com/bathos-labs/platform-dotnet/blob/master/docs/platform-support.md
[publication]:
  https://github.com/bathos-labs/platform-dotnet/blob/master/docs/usage.md#file-publication-and-inspection
[paths]:
  https://github.com/bathos-labs/platform-dotnet/blob/master/docs/usage.md#native-paths-and-portable-names
[directories]:
  https://github.com/bathos-labs/platform-dotnet/blob/master/docs/usage.md#standard-directories-and-application-identity
[leases]:
  https://github.com/bathos-labs/platform-dotnet/blob/master/docs/usage.md#cooperative-file-leases
[desktop]:
  https://github.com/bathos-labs/platform-dotnet/blob/master/docs/usage.md#reveal-and-open-in-an-application
[sampling]:
  https://github.com/bathos-labs/platform-dotnet/blob/master/docs/usage.md#screen-color-selection
[profiles]:
  https://github.com/bathos-labs/platform-dotnet/blob/master/docs/usage.md#installed-color-profiles
[changelog]: https://github.com/bathos-labs/platform-dotnet/blob/master/CHANGELOG.md
[validation]: https://github.com/bathos-labs/platform-dotnet/blob/master/docs/validation.md
[architecture]: https://github.com/bathos-labs/platform-dotnet/blob/master/docs/architecture.md
[contributing]: https://github.com/bathos-labs/platform-dotnet/blob/master/CONTRIBUTING.md
[releasing]: https://github.com/bathos-labs/platform-dotnet/blob/master/RELEASING.md
