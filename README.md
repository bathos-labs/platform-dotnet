# Bathos Platform for .NET

Toolkit-independent native capabilities with explicit host composition. MIT licensed. Built for .NET 10;
currently prerelease, with no stable API compatibility promise yet.

| Package | Capability |
| --- | --- |
| `Bathos.Platform` | Facade and capability interfaces, OS probe/provider selection, atomic file publication |
| `Bathos.Platform.MacOS` | Exclusive rename/replacement, regular-file inspection, optional AppKit screen sampler |
| `Bathos.Platform.Windows` | Exclusive rename/replacement and regular-file inspection |
| `Bathos.Platform.Linux` | statx regular-file inspection; rename and sampling are currently unavailable |

Providers reference only the core and runtime. Include only the providers your application needs. No UI toolkit,
image-processing library, DI container, global mutable provider, or assembly scanning is required.

```csharp
using Bathos.Platform;
using Bathos.Platform.MacOS;
using Bathos.Platform.Windows;
using Bathos.Platform.Linux;

var platform = PlatformProviders.SelectCurrent(
    new MacOSPlatformProvider(), new WindowsPlatformProvider(), new LinuxPlatformProvider());
var probe = platform.FileEntries ?? throw new PlatformNotSupportedException();
probe.RequireRegularFile(selectedPath);
```

A host may register the facade or individual capabilities in DI, or retain it in an application-owned wrapper.
Null capabilities mean unsupported or unconfigured. Operation failures throw or return explicit native outcomes;
null screen samples mean user cancellation. OS selection does not establish permissions or filesystem support.

For macOS screen sampling, supply `INativeDispatcher` to `MacOSPlatformProvider`. It must verify the native main
thread and defer posted work until the current callback unwinds. Keep the native event loop alive through completion.
Samples are opaque sRGB screen appearance, not original image samples. AppKit owns selection and Escape; retiring
a caller does not dismiss the native chooser. Interactive sampling requires manual verification.

See [architecture](https://github.com/bathos-labs/platform-dotnet/blob/master/docs/architecture.md), [contribution and validation](https://github.com/bathos-labs/platform-dotnet/blob/master/CONTRIBUTING.md), and
[NuGet releases](https://github.com/bathos-labs/platform-dotnet/blob/master/RELEASING.md). Package availability follows published releases; repository builds alone do not
publish to NuGet.org.

For local development, run `task check` to validate and `task --list` to see focused commands. Task is optional;
the contribution guide includes direct SDK commands. Consumer-facing release notes live in the
[changelog](https://github.com/bathos-labs/platform-dotnet/blob/master/CHANGELOG.md).
