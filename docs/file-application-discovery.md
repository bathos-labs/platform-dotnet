# File-driven application discovery

Status: agreed contract for [issue #36](https://github.com/bathos-labs/platform-dotnet/issues/36),
with core API declarations and the Windows adapter implemented; macOS/Linux adapters and broader
acceptance validation remain pending. Windows exposes the capability; other built-in providers leave
it null. The contract selects useful applications for an Open in menu, not immutable activation
commands or task-dispatch registrations.

## Goal and scope

Given an existing local regular file, discover useful application choices registered with the
operating system to open it. Each result supplies display context and a target usable by the
matching built-in `IFileInvoker` and `IApplicationIconProvider`. Callers need no native type
identifiers, command templates, or application catalog.

Discovery is advisory and read-only. It does not launch applications, change defaults, prove format
support, editing capability, or round-trip fidelity, or create temporary files. General-purpose
handlers may appear even for unknown file types. The list is neither an exhaustive inventory nor a
ranking of application suitability. Ordering never implies default status.

Hosts own custom entries, preferences, caching, refresh, and stale-result rejection. Query the
actual file intended for handoff, including an exported copy when applicable. Exclude hypothetical
type queries, reverse type inventories, default-handler lookup, association mutation, directories,
URLs, remote resources, and multi-file selections from the first capability.

## Agreed API contract

Add a focused optional `IApplicationDiscovery` capability with a default-null `ApplicationDiscovery`
facade property. Existing explicit discovery utilities remain unchanged. The intended operation is
`FindForFileAsync(absolutePath, cancellationToken)`, returning an owned read-only list of immutable
`ApplicationCandidate` values:

- `Target`: an `ApplicationTarget` accepted by the matching built-in invocation and icon providers.
- `DisplayName`: a nonblank label, never an invocation identity or command. A native identifier may
  serve as a documented fallback when no friendly label is available.

These are the agreed API responsibilities; concrete declarations and target representation must be
implemented with their providers, XML contracts, compatibility fixtures, and examples. Candidates
contain no COM handles, retained native objects, disposable sessions, lazy native getters, or eager
bitmap data. Retrieve icons separately at the desired physical pixel size. A missing icon is
allowed.

An empty list means no candidates were returned by native discovery after application-level
coalescing. A null facade means unsupported or unconfigured. Invalid arguments, missing/nonregular
files, native failures, and cancellation remain distinct. Enumeration failures must not become
successful partial lists. Absence of optional ProgID/AppUserModelID metadata alone does not make an
otherwise usable Windows handler invalid.

## Selection and lifetime

A target means **the selected application's current native registration for the association
context**. It does not reserve a command, executable contents, installation, or registration
version. Changes to registered arguments and launch behavior between discovery and invocation are
accepted. Successful invocation establishes native submission, not successful document parsing or
readiness.

Invocation revalidates the file and resolves the application within the original association
context. Missing or unresolved selections fail explicitly. Never substitute the default application,
guess an executable invocation, or match by display label. After permitted application-level
coalescing, any remaining ambiguity fails explicitly. Stronger registration identifiers may refine
lookup, but a ProgID is not a promise to preserve one command when the selected application has
multiple entries.

Targets are transient values, not durable preference keys, serialized identities, or security
credentials. Reinstallation at the same path and native metadata changes cannot be excluded. Labels,
icons, and activation are separate observations and need not form an atomic snapshot. Hosts should
refresh menus as appropriate; exact file stability and application trust remain their
responsibility.

## Coalescing application choices

Preserve native coalescing when the OS already returns one choice for multiple registrations.
Additional entries may be coalesced when they share a native application identity or the same full
executable path under the provider's native comparison rules. Do not merge merely equal display
names or executable basenames; distinct applications with the same label remain separate choices.
Path matching does not promise filesystem-alias or executable-content identity.

Keep a complete native candidate as the winner so its invocation and icon metadata stay together.
For additional Windows coalescing, prefer a native-recommended candidate, then a candidate with a
usable stronger registration identifier, then native enumeration order. These are local
tie-breakers, not a portable recommendation score, default selection, or a stable ordering promise.
Validate them against owned duplicate fixtures before implementation acceptance. Do not synthesize
commands or combine the icon of one registration with another registration's activation descriptor.

## Composition and compatibility

Keep one target model for explicit and discovered applications. macOS bundle paths and Linux desktop
entry IDs already preserve native activation. Add a Windows association-backed descriptor containing
association context and native application identity, with optional stronger registration metadata.
Handlers without ProgIDs are supported through context plus native application name; re-resolution
uses the same application-level coalescing policy. Labels are never a fallback identity. Native
context must be structured internally, not concatenated into an undocumented public string encoding.

Do not change `ApplicationTarget.Executable` or its one-file-argument contract. Add no required
members to existing interfaces. Third-party invokers may continue rejecting unfamiliar target kinds;
discovered targets compose with matching built-in providers. Review enum additions and value
equality explicitly. Descriptor equality must not imply immutable command or installed-application
identity.

Retained COM sessions are not required: they add threading and lifetime obligations without freezing
registration behavior. Each operation owns its native resources and releases them before completion.

## Native adapters

| Platform | Discovery                                                                 | Invocation and icons                                                                                             |
| -------- | ------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------- |
| macOS    | NSWorkspace applications for the actual file URL.                         | Existing bundle target, native bundle handoff, and icon provider.                                                |
| Linux    | GIO content-type lookup and registered recommended/fallback applications. | Desktop-entry identity, GIO activation, and icon provider.                                                       |
| Windows  | Shell handler enumeration for the file extension.                         | Re-resolved application handler, Shell data object activation, and handler icon metadata rendered to owned RGBA. |

Providers absorb native type semantics; no portable type-normalization API is needed. Define and
validate extensionless-file behavior before implementation acceptance. Operations run on owned
background work, with Windows COM resources confined to their STA worker. Cancellation joins owned
native work and cleanup. Bound enumeration and metadata processing and fail explicitly on overflow.

## Evidence and remaining acceptance

Windows build 26200 x64 probes established native activation with required marker arguments and a
space/Unicode file path, changed commands, removed registrations, competing ProgIDs, and rejection
of a stale ProgID in the stricter experimental matcher. An owned Applications registration
reproduces handlers with no usable ProgID or AppUserModelID. Retaining an IAssocHandler still
observes a command changed after enumeration. Packaged handlers expose AppX ProgIDs,
AppUserModelIDs, and indirect ms-resource icons. These observations support current-registration
semantics, not durable identity.

Read-only macOS probes and an isolated Linux MIME fixture provide preliminary native lookup
evidence, not acceptance of the actual-file operation. See the
[Windows engineering probe](../eng/Bathos.Platform.AssociationProbe/README.md) and issue #36 for
investigation details. The probe's strict ProgID matcher is an experiment, not the final coalescing
implementation.

Before acceptance, validate actual-file lookup, missing/nonregular files, extensionless and unknown
types, conflicting extension/content metadata, duplicate labels, competing Applications
registrations, coalescing winners, removal, cancellation, errors, and resource cleanup. Verify
discovery-to-icon-to-invocation composition on all three platforms, including packaged Windows icon
rendering and commands requiring additional arguments. No personal application is launched
automatically for validation.

Update public declarations, XML contracts, integration examples, the capability matrix,
compatibility fixtures, and the changelog together when implemented. This decision does not claim
feature availability or authorize a release.

## Native references

- [Windows handler enumeration](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-shassocenumhandlers)
- [Windows native invocation](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-iassochandler-invoke)
- [ProgID access](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-iobjectwithprogid)
- [NSWorkspace](https://developer.apple.com/documentation/appkit/nsworkspace)
- [GIO registered handlers](https://docs.gtk.org/gio/type_func.AppInfo.get_all_for_type.html)

## Handover to macOS and Linux

The Windows implementation is ready for platform handover, with no known Windows blocker for the
agreed convenience contract. Final Windows validation passed `task check` (203 passed, 47 skipped)
and `task packages:aot`, including discovery and association-icon retrieval in self-contained,
single-file, trimmed, and Native AOT consumers. SDK 10.0.403 with runtime/compiler 10.0.14 resolved
the previous AOT security-audit blocker without suppressing diagnostics. Install the SDK required by
`global.json` on the receiving host before validation; the local Windows SDK installation is not
part of the branch. [Issue #36](https://github.com/bathos-labs/platform-dotnet/issues/36) tracks the
remaining platform work and validation evidence.

The Windows facade now exposes discovery, with association targets accepted by its invoker and icon
provider. Existing executable behavior is unchanged. Production Windows tests cover owned
Applications registrations without IDs, competing registrations for one executable, removal,
pre-cancellation across discovery/invocation/icons, extensionless and unknown types, conflicting
extension/content, invalid files, and a pipe-acknowledged discovery/icon/invocation sequence that
uses changed registration arguments. Metadata is bounded to 32768 characters and enumeration to 4096
handlers. Regular-file validation uses the native file-entry probe.

Portable policy tests cover case-insensitive native-name coalescing, recommendation and ProgID
priority, complete winner retention, and stable ties. Handler ownership storage is allocated before
COM initialization; recording acquired references does not allocate. An injected consumer allocation
failure exercises exception recovery and subsequent enumeration on the same worker.

Installed packaged-handler icon checks establish nonempty correctly sized RGBA through the Shell
cache; they do not establish visual fidelity or absence of generic Shell fallback. No personal
applications are launched. Native fault injection for malformed COM outputs, exact reference-count
cleanup, and mid-operation cancellation remains useful Windows follow-up. Symlink acceptance
evidence depends on host privileges. These limits are not hidden by passing portable tests.

Continue on `feature/file-application-discovery`:

1. Implement macOS `IApplicationDiscovery` using actual-file NSWorkspace lookup, returning existing
   bundle targets. Preserve bundle invocation/icon composition and native ownership. Validate on
   macOS with owned applications; compilation on Windows is not native evidence.
2. Implement Linux actual-file GIO content-type and application lookup, returning desktop-entry
   targets. Use isolated XDG/MIME fixtures, preserve activation arguments, and validate icons and
   cleanup on Linux. A macOS run does not substitute for Linux execution.
3. Keep the agreed convenience contract: current registrations, transient targets, complete native
   winners, no label-only coalescing or default substitution. Providers remain null until supported.
4. Update capability tables, XML, examples, package consumers, and tests for each adapter. Run
   `task check` and native AOT on each executing host; keep platform skips and manual acceptance
   explicit. Do not release or tag as part of this handover.
