# Platform support and deployment

The [README capability matrix](../README.md#capabilities) shows what each built-in provider
implements. The [integration guide](usage.md) explains how to use those capabilities. This page owns
the deployment prerequisites; [native validation](validation.md) describes the evidence behind them.

## Package and runtime requirements

All packages target .NET 10. Choose a runtime and OS supported by .NET for your deployment; see
[.NET supported platforms](https://github.com/dotnet/core/blob/main/os-lifecycle-policy.md). A
native API's minimum OS version does not extend the .NET runtime's supported OS range.

The Linux package follows the same version and stable/prerelease channel as the other packages.
Experimental Linux behavior does not by itself require a prerelease package. Stable public API
compatibility covers Linux callers and implementers as well.

Provider constructors do not probe native libraries, filesystem features, desktop services, or
permissions. Constructing a provider or observing a nonnull capability does not prove that an
operation can succeed. Invoke native operations only on the matching OS and handle operation
failures.

The libraries supply managed interop, not bundled native frameworks or system-library installers.
File operations can be used without a desktop session. The desktop capabilities below have
additional requirements. Installing all providers does not make foreign-platform native operations
usable.

## Filesystem capabilities

| Capability                     | Deployment requirement                                                                                                                                                |
| ------------------------------ | --------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Atomic rename and publication  | Source and destination on the same filesystem/volume, with the native rename operation supported. Cross-volume copying is never a fallback.                           |
| Linux exclusive rename         | libc exporting `renameat2` and filesystem support for `RENAME_NOREPLACE`.                                                                                             |
| Windows snapshot replacement   | Windows 10 version 1607 or later, plus filesystem support for `FileRenameInfoEx` with POSIX replacement semantics. Retained readers must permit deletion/replacement. |
| Linux file inspection          | libc exporting `statx`; missing native entry points are operation failures.                                                                                           |
| Cooperative file leases        | A local filesystem supporting the native locking protocol, a preexisting parent directory, and an unchanged lock artifact and containing directory tree while leased. |
| Paths and standard directories | Native syntax and available host/account configuration. Directory discovery does not create locations or establish access.                                            |

Filesystem guarantees are scoped to the documented native operations. Network filesystems, aliases,
unusual mount policies, denied access, and cross-volume arrangements need their own evidence. Atomic
visibility is not a power-loss durability guarantee. Lease coordination applies to cooperating
instances of the same application on one device using the same stable lock location and protocol.

See [publication](usage.md#file-publication-and-inspection),
[paths](usage.md#native-paths-and-portable-names),
[directories](usage.md#standard-directories-and-application-identity), and
[leases](usage.md#cooperative-file-leases) for the caller obligations.

## Desktop capabilities

| Operation                         | Windows                                                          | macOS                                                                         | Linux                                                                                                                             |
| --------------------------------- | ---------------------------------------------------------------- | ----------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------- |
| Reveal an entry                   | Windows Shell selection APIs                                     | Finder through `/usr/bin/open -R`                                             | `libdbus-1.so.3`, a session bus, and `org.freedesktop.FileManager1.ShowItems`                                                     |
| Open an executable                | An installed executable accepting a file argument                | An installed executable accepting a file argument                             | An installed executable accepting a file argument                                                                                 |
| Native application lookup/handoff | App Paths registrations for lookup                               | LaunchServices for lookup and `/usr/bin/open` for app bundles                 | GIO, GLib, and GObject for desktop-entry lookup and activation                                                                    |
| Screen color sampling             | GDI desktop access and a host-supplied native dispatcher/UI loop | AppKit picker availability and a host-supplied main-thread dispatcher/UI loop | Explicit opt-in, `libdbus-1.so.3`, a session bus, and a portal backend implementing `org.freedesktop.portal.Screenshot.PickColor` |
| Installed color profiles          | Windows ICC/WCS inventory and native read access                 | ColorSync inventory and native read access                                    | Unavailable                                                                                                                       |

Linux desktop-entry operations load `libgio-2.0.so.0`, `libglib-2.0.so.0`, and
`libgobject-2.0.so.0`. Explicit executable invocation does not require these libraries. Linux file
operations do not require D-Bus, GIO, or a portal merely because the provider exposes desktop
capabilities.

Windows and macOS samplers are configured by supplying `INativeDispatcher`; the host owns the event
loop and must keep it alive until the operation and cleanup complete. The macOS sampler also checks
AppKit picker availability on macOS 10.15 or later. Linux sampling uses
`enableScreenColorSampler: true`; the portal owns interaction, so a host dispatcher is not required.
Services and permissions are checked when the operation is invoked, not when it is configured.

A desktop session must expose the requested native service and allow the operation. Headless hosts
and containers do not gain desktop support by installing the package or native libraries. Respect
system privacy controls. Screen colors are eight-bit RGB for ordinary selection, with no calibrated
color or HDR guarantee. Profile discovery returns raw bytes, without ICC parsing or conversion.

See the [desktop](usage.md#reveal-and-open-in-an-application),
[sampling](usage.md#screen-color-selection), and [color-profile](usage.md#installed-color-profiles)
recipes for configuration and operation semantics.

## Consumer publishing

Package verification publishes and directly executes a console consumer referencing only the core
and the executing host's provider. The current CI targets are `linux-x64`, `win-x64`, and
`osx-arm64`; other RIDs need their own evidence. Each mode runs native exclusive publication,
regular-file inspection, snapshot replacement with a retained reader, and lease contention and
reacquisition. macOS also enumerates ColorSync profiles and reads a profile, exercising native
framework loading and managed callbacks without opening a picker.

| Publish mode               | Configuration                                                                                  | Evidence and limits                                                                                                         |
| -------------------------- | ---------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------- |
| Self-contained directory   | `--self-contained true`                                                                        | Published executable runs with its included runtime, outside its build directory.                                           |
| Self-contained single file | `--self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true` | The bundled executable runs without loose managed/runtime DLLs. Runtime native libraries extract into a writable directory. |
| Trimmed                    | `--self-contained true -p:PublishTrimmed=true -p:TrimMode=full`                                | Experimental: both libraries are rooted for full-library analysis, and the published probe runs.                            |
| Native AOT                 | `-p:PublishAot=true`                                                                           | Experimental: both libraries are rooted for analysis and native compilation, and the produced executable runs.              |

Pass `--runtime <RID>` and `--configuration Release` to `dotnet publish` in your application
project, together with the selected mode's configuration. Self-contained publishing includes the
.NET runtime; it does not bundle OS frameworks, Linux system libraries, or desktop services. The
single-file check uses native-library extraction; configure a private writable extraction location
when the runtime's default is unsuitable. See Microsoft's
[single-file deployment guidance](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview).

Trimming and Native AOT remain experimental for these packages. Rooting the core and selected
provider with `TrimmerRootAssembly` ensures the probes analyze all their code, including interop and
callbacks outside the executed operations. Rooting retains library code, so these probes do not
establish the behavior of an application with only its used members retained or measure its size
savings. Analysis warnings fail validation. The shipping libraries do not declare `IsTrimmable` or
`IsAotCompatible`; a successful probe is narrower evidence than whole-library compatibility.

macOS ColorSync callbacks execute in these modes. The macOS AppKit picker/block callback and Windows
window procedure are compiled and analyzed but remain untested at runtime in published consumers.
Linux desktop services, visible handoff, UI dispatchers, permissions, and interactive pickers also
need separate acceptance in the application's chosen publish mode. An ordinary native test run does
not supply that published-application evidence. Review Microsoft's
[library trimming guidance](https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/prepare-libraries-for-trimming)
and
[Native AOT prerequisites and limitations](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)
before choosing those modes for a product.

## Tested environments and support limits

The [native CI workflow](../.github/workflows/validate.yml) is the source of truth for runner
images. It executes the suite on Linux x64, Windows x64, and macOS Arm64. These are the tested
combinations; other architectures, Linux distributions/libc variants, OS versions, and filesystems
require their own validation. CI success on one host does not establish another host's native
behavior.

Linux implementations remain experimental. Private D-Bus and GIO fixtures establish native transport
and protocol behavior; real portal, file-manager, compositor, and permission acceptance remains
manual. Windows/macOS screen pickers and visible desktop handoff also require human validation. See
[validation procedures and evidence boundaries](validation.md) before making deployment claims.

The engineering harnesses and Linux Python/D-Bus test-service dependencies are development tools,
not consumer package dependencies. Source setup and publish-check prerequisites belong in
[Contributing](../CONTRIBUTING.md).
