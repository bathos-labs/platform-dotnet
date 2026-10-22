# Bathos Platform for .NET

Native file publication, file inspection, and macOS screen color selection for .NET applications.
Use these capabilities without coupling your application to a UI toolkit or dependency-injection container.
MIT licensed. Built for .NET 10;
currently prerelease, with no stable API compatibility promise yet.

| Package | Capability |
| --- | --- |
| `Bathos.Platform` | Facade and capability interfaces, OS probe/provider selection, atomic file publication |
| `Bathos.Platform.MacOS` | Exclusive rename/replacement, regular-file inspection, optional AppKit screen sampler |
| `Bathos.Platform.Windows` | Exclusive rename/replacement and regular-file inspection |
| `Bathos.Platform.Linux` | Experimental statx regular-file inspection; rename and sampling are unavailable |

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

## Capability contracts

A host may register the facade or individual capabilities in DI, or retain it in an application-owned wrapper.
Null capabilities mean unsupported or unconfigured. Operation failures throw or return explicit native outcomes;
null screen samples mean user cancellation. OS selection does not establish permissions or filesystem support.

Linux support is temporarily experimental pending broader host validation. The provider currently offers only
file inspection; automated Linux CI does not establish desktop or filesystem coverage.

For macOS screen sampling, supply `INativeDispatcher` to `MacOSPlatformProvider`. It must verify the native main
thread and defer posted work until the current callback unwinds. Keep the native event loop alive through completion.
Samples are opaque sRGB screen appearance, not original image samples. AppKit owns selection and Escape; retiring
a caller does not dismiss the native chooser. Run `task eng:sampler` on macOS for the engineering-only manual validation harness. Move the window,
then use its buttons to start individual tests; instructions and results stay in the window. Each test can be repeated.
The interactive picker requires human input; computer-use automation is currently unreliable for this path.
`task eng:sampler:smoke` checks native dispatch and shutdown without opening the picker. Interactive verification is
still pending; see the [validation record](https://github.com/bathos-labs/platform-dotnet/blob/master/docs/validation.md)
for the procedure and host-specific evidence.

See [architecture](https://github.com/bathos-labs/platform-dotnet/blob/master/docs/architecture.md), [contribution and validation](https://github.com/bathos-labs/platform-dotnet/blob/master/CONTRIBUTING.md), and
[NuGet releases](https://github.com/bathos-labs/platform-dotnet/blob/master/RELEASING.md). Package availability follows published releases; repository builds alone do not
publish to NuGet.org.

For local development, run `task check` to validate and `task --list` to see focused commands. Task is optional;
the contribution guide includes direct SDK commands. Consumer-facing release notes live in the
[changelog](https://github.com/bathos-labs/platform-dotnet/blob/master/CHANGELOG.md).
