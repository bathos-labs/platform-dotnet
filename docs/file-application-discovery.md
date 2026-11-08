# File-driven application discovery

Status: proposed architecture for
[issue #36](https://github.com/bathos-labs/platform-dotnet/issues/36), a candidate for 1.2. The
file-driven use case is agreed; the result identity and Windows activation mapping require native
verification before shipping API declarations are added.

## Goal and scope

Given an existing local regular file, discover the applications the operating system registers as
candidates to open it. Each result provides display context and a target usable by the platform's
existing `IFileInvoker` and `IApplicationIconProvider`. Callers need no native type identifiers,
command templates, or application catalog to populate an Open in menu.

Discovery is advisory and read-only. It does not launch applications, change defaults, prove editing
or round-trip fidelity, or create temporary files. Caller-configured targets remain valid
independent of registration. Hosts own merging custom entries, preferences, caching, refresh, and
stale-result rejection. Query the actual file intended for handoff, including an exported copy when
applicable.

Exclude hypothetical-type queries, reverse inventories of supported types, default-handler lookup,
association mutation, directories, URLs, remote resources, and multi-file selections from this first
capability. Default lookup can be a later focused capability; ordering never implies default status.

## Proposed public shape

Add an optional `IApplicationDiscovery` capability with a default-null `ApplicationDiscovery` facade
property. Existing platform-specific explicit discovery utilities remain available and unchanged.
The intended operation is `FindForFileAsync(absolutePath, cancellationToken)`, returning an owned
read-only list of immutable `ApplicationCandidate` values. Each candidate has:

- `Target`: an `ApplicationTarget` accepted by the matching built-in invocation and icon providers.
- `DisplayName`: a nonblank display label, never an identity or a command. A native identifier may
  serve as a documented display fallback when the OS supplies no friendly label.

These names are a design sketch, not published signatures. The type names do not assert that a
candidate can edit a document. Candidates contain no COM handles, retained native objects,
disposable sessions, lazy native getters, or eager bitmap data. Retrieve icons separately at the
desired size. A missing icon is allowed; replacing an unresolved candidate with a generic executable
is not.

An empty list means no registered candidates were found. A null facade means unsupported or
unconfigured. Invalid arguments, missing/nonregular files, native failure, and cancellation are
separate outcomes. Do not return a successful partial list after an enumeration/identity error. No
global completeness promise is made: the list reflects native registrations visible at query time.

A discovery result is a snapshot, not a reservation. Invocation revalidates the selected target and
file. Removal or ambiguous re-resolution must fail explicitly, never select another/default handler.
A target is not an executable-content security identity and cannot guarantee unchanged installed
application bytes. Discovery does not lock or read an entire document; native metadata/type queries
may inspect file data. Exact file stability remains the caller's responsibility.

## Composition and compatibility

Keep one target model for explicit and discovered applications. macOS bundle paths and Linux desktop
entry IDs already preserve native activation. A Windows association-backed target is a proposed
addition, not an executable alias. It must retain whatever association context is needed to select
the same native handler and retrieve its icon. Native context remains an implementation detail for
ordinary callers; do not concatenate identifiers into an undocumented public string encoding.

Do not change the meaning of `ApplicationTarget.Executable`: its existing one-file-argument contract
remains intact. Do not add required members to existing capability interfaces. New target kinds are
not automatically supported by third-party invokers: existing unsupported-kind behavior remains
valid, and documentation must state that discovered targets compose with the matching provider.
Review enum additions and value equality explicitly. Results are not promised serializable or stable
across installations; preference persistence needs a separate identity decision.

If Windows cannot provide an adequately distinguishable registration descriptor, reconsider the
result model before implementation. A session retaining native handlers preserves exact activation
but introduces lifetime/threading obligations and weakens composition with existing targets.
Returning only executable-compatible handlers is simpler but excludes meaningful native candidates.
Neither trade-off should be adopted implicitly.

## Native adapters

| Platform | Discovery                                                                                                         | Invocation and icons                                                                                                                                            |
| -------- | ----------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| macOS    | NSWorkspace applications for the actual file URL; use native content interpretation.                              | Existing bundle target, native bundle handoff, and application-icon provider.                                                                                   |
| Linux    | Query the file's content type through GIO; enumerate registered recommended/fallback applications.                | Desktop-entry identity, GIO activation, existing icon provider. Validate usable identity; do not manufacture an executable target for an unrepresentable entry. |
| Windows  | Shell handler enumeration for the file extension; explicitly define extensionless-file behavior during the probe. | Native handler invocation with a Shell data object; handler icon metadata feeds the owned RGBA renderer. Identity/re-resolution remains a design gate.          |

Provider internals absorb differences in extension, MIME, and UTType semantics. No public
content-type normalization layer is needed for this operation. Native matching remains
platform-specific; equal files need not produce identical candidate lists across hosts. Avoid
exposing a portable "recommended" score without demonstrated consistent meaning. Deduplicate only
proven identical native targets, never friendly names or executable basenames.

Operations run on owned background work; Windows COM resources remain on their owning worker.
Cancellation must join owned native work and cleanup before completion. Bound enumeration and
metadata processing and fail explicitly when bounds are exceeded. Do not infer an availability
promise from provider construction or a successful earlier lookup.

## Evidence and acceptance

Read-only probes established macOS type-based bundle results and independent default lookup, and an
isolated Linux MIME fixture established multiple desktop-entry candidates and a user-selected
default. These are preliminary evidence, not validation of the proposed actual-file operation.
Windows currently has documentation evidence only. The
[Windows probe and handoff](../eng/Bathos.Platform.AssociationProbe/README.md) define the next gate.

Before implementation acceptance, test actual-file lookup, missing/nonregular files, extensionless
and unknown types, conflicting extension/content metadata, duplicate labels, custom entries, removed
registrations, cancellation, and native errors. Verify discovery-to-icon-to-invocation composition
with owned fixtures on all three platforms. A successful handoff is distinct from successful parsing
by the receiving application. Cover packaged Windows handlers and commands requiring extra
arguments.

The public API, XML contracts, integration example, capability matrix, package compatibility
fixtures, and changelog change together when implemented. No changelog entry claims this proposal is
available.

## Native references

- [Windows handler enumeration](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-shassocenumhandlers)
- [Windows native invocation](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-iassochandler-invoke)
- [NSWorkspace](https://developer.apple.com/documentation/appkit/nsworkspace)
- [GIO registered handlers](https://docs.gtk.org/gio/type_func.AppInfo.get_all_for_type.html)
