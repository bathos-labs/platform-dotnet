# Platform architecture

The core defines capability contracts and shared mechanics. Each OS-specific assembly references only the core and
.NET runtime; the core never references providers. Provider constructors perform no native work. The host supplies
provider instances to selection and a native dispatcher for capabilities that require one.

Applications normally use the built-in Windows, macOS, and Linux providers. The provider facade groups capabilities
for application composition without requiring a DI container. Selection uses explicit OS identities and the supplied
instances. Components can depend directly on focused capability interfaces, which also support substitution and testing.
Partial providers need only an OS identity; optional capability getters default to null. Applications check availability
before passing capabilities to their components.

New capabilities use focused interfaces and optional default-null facade properties, so existing providers can omit
them. Compatibility review covers both callers and public facade implementations. Broader provider infrastructure
needs a demonstrated consumer requirement.
Default operation implementations must fulfill the operation's contract; silent no-ops and throwing placeholders do not
establish capability support. Changes to existing required operations or their guarantees need explicit compatibility review.

File publication stages a private sibling, flushes completed bytes, and publishes through one native rename.
Exclusive rename checks and claims the destination atomically. Replacement is explicit; cross-volume copy/delete is
never a fallback. Cancellation after successful rename cannot reinterpret publication as failure. Disposal owns only
staging. File-kind inspection rejects links and special files without opening POSIX special entries; inspection is
advisory and is not a path reservation.

Linux is a distinct runtime target with its own native APIs. Its experimental provider supplies statx file-kind
inspection, desktop reveal and application handoff, native lookup, paths, standard directories, cooperative file leases,
and opt-in desktop-portal color sampling. Native rename uses libc renameat2 with RENAME_NOREPLACE for exclusive claims; unsupported kernels or filesystems fail without a fallback. Graphical desktop validation remains pending.
Do not hide missing capabilities behind a weaker implementation. Add capabilities from demonstrated consumers,
keeping product entities, document lifecycle, product-specific path policy, UI toolkits, and image pipelines in their owning packages.

Screen colors are eight-bit RGB for everyday selection, without a promise of calibrated appearance, original image
values, or identical results across display profiles and HDR configurations. AppKit owns macOS selection and callback
completion. Windows uses a frozen GDI desktop snapshot in an opaque native selection window, so the selection click
does not reach the application underneath. It scopes per-monitor DPI awareness to window creation and native callbacks,
preserving the host's DPI policy. Neither implementation owns a nested message loop. Their supplied dispatcher must
post asynchronously to the host UI loop, allowing callbacks to unwind before resource cleanup; the host keeps that loop alive.

Linux delegates selection to the Screenshot portal's PickColor method using system libdbus. Each operation owns a
private session-bus connection on a dedicated worker, subscribes before sending the request, and accepts completion
only from the replying portal's unique bus name and request path. A host dispatcher or GLib loop is not required.
Only a user-canceled portal response returns null; other failures, malformed colors, and disconnects throw.
No native work occurs during provider construction, and opt-in does not establish desktop-backend availability.
Linux remains experimental until graphical desktop validation is available.

Callers that retire while sampling must reject stale results and allow owned work to finish. OS compatibility and
capability availability do not imply permission or successful operation. Native requests, connections, subscriptions,
graphics resources, and selection windows belong to their operation and must be released before its task completes.

Desktop handoff has two focused capabilities: reveal an existing entry in its containing folder, and open an existing
file in a host-selected application. Both require absolute paths and keep file ownership with the caller. An immutable
ApplicationTarget distinguishes executable paths, macOS application bundles, and Linux desktop-entry IDs. Targets
are syntax-checked values, not promises of installation or file-format support. Providers reject unsupported target
kinds instead of changing intent or falling back to a default handler. Providers can expose FileRevealer and
FileInvoker independently; omitted capabilities use the facade's null defaults.

Windows reveal uses SHParseDisplayName and SHOpenFolderAndSelectItems on an owned STA worker with balanced COM
initialization and PIDL ownership. macOS reveal and bundle invocation await only the native /usr/bin/open helper;
they do not wait for the selected application to exit or introduce another Objective-C callback lifetime.
Linux reveal and sampling share one native D-Bus transport. Reveal uses FileManager1.ShowItems without weakening
selection to opening a folder. Linux desktop-entry invocation uses GIO so native registration metadata handles
Exec field codes, file/URI conversion, and D-Bus activation. It adds system GLib/GIO/GObject requirements, not managed
package or UI-toolkit dependencies. Explicit executable invocation uses the shared runtime process mechanism,
passing one file argument through ArgumentList without shell parsing or a command template.

Discovery is a set of native-specific lookup utilities, not an application-selection policy or inventory service.
App Paths executable names, LaunchServices bundle IDs, and XDG desktop-entry IDs have different native meanings;
the host supplies those keys and chooses among returned candidates. No product names, installation-directory scans,
document entities, icons, or UI belong here. Missing registrations are explicit empty results; native lookup failures throw.

Cancellation is checked before irreversible handoff, including immediately before submission after preparation.
After submission, await any owned helper/connection cleanup and report the native outcome without rechecking a
caller token. Success establishes submission, not document readiness, foreground focus, or target-process lifetime.

Installed color-profile discovery is an optional facade capability with a focused `IColorProfileProvider` interface.
The core owns inventory/reference values and configurable resource limits; macOS and Windows own native discovery
and byte retrieval. Linux leaves it unavailable. Profile keys are provider-owned references, not portable identities.
Partial discovery retains usable entries; read failures and cancellation remain distinct. No ICC parser, image-library
dependency, profile installation, or color-management policy belongs to this capability.

## Paths and ownership

Path handling and lease ownership are separate capabilities. Native path syntax, lossless file-URI conversion, and
an explicitly selected filename portability policy belong in Platform. Product-relative references and storage
policy remain with the host. Normalization preserves significant characters; it does not sanitize an existing location,
resolve filesystem aliases, or prove that two paths identify the same resource. Lexical comparison is explicit and
must not be presented as observed filesystem identity.

The lease contract is bounded to cooperating instances of the same application on one device, using the same lock
protocol and a stable location on a supported filesystem. The common API provides platform-specific implementations;
it does not promise coordination between operating systems, network clients, unrelated applications, or callers
that bypass the protocol. Different aliases and representations across path/URI normalization boundaries are not
promised to share a lease. Hosts with critical exclusion requirements must establish consistent locations and restrict
storage to supported configurations. A process-local path dictionary alone cannot provide cross-process exclusion.

The host must keep the lock artifact and its containing directory tree intact at the same location for the entire
lease lifetime. Moving, renaming, deleting, or replacing either is unsupported on every platform. If application data
may move, hosts must choose a stable lock location outside that tree. Platform does not supply a relocation protocol.
Host document admission and output ownership policy remain outside Platform. Disposal and process exit release ownership;
a leftover artifact alone must not prevent reacquisition, and diagnostic metadata is not ownership authority.

Public path and lease APIs carry these bounds in their shipped XML documentation and usage
examples, including caller obligations and the distinction between contention and operational failure. Native tests
must establish the supported same-device guarantees independently on each supported OS; lexical tests cannot do so.

## Standard directories

`IStandardDirectories` discovers standard roots on demand. Provider construction performs no discovery. The shared
runtime adapter uses .NET special folders with `DoNotVerify`, the runtime temporary directory, and the application
base directory. The macOS cache root comes from the runtime's native user-cache lookup, independently of `HOME`.
OS layout rules supply state directories and XDG defaults. Lookup does not create folders, verify write access, or
acquire ownership. A missing or invalid location is null; operational lookup failures may propagate.
Unix home discovery observes `HOME` or a reentrant effective-user account lookup in the matching native provider.
Shared mechanics own and bound the lookup buffer and validate its returned home pointer/UTF-8 before release.
This preserves missing-home evidence and genuine root homes without relying on .NET's ambiguous `/` substitution.
Linux suppresses home-derived defaults when home is unavailable while retaining independently configured roots.

`StandardDirectoryRoots` is a snapshot, with explicit record-copy overrides for host/test/portable-install roots.
`ApplicationDirectoryIdentity` validates publisher/product as portable components without sanitizing them. Scoping
validates nonnull roots with supplied native path syntax. Unknown layouts and relative overrides fail explicitly;
there is no working-directory fallback. User home/documents/desktop, shared temporary storage, and deployed resources
remain separate from application data/configuration/cache/state. Hosts retain filenames, recovery layouts, cache
ownership and cleanup, and product-specific placement choices.

## Cooperative file leases

`IFileLeaseProvider.TryAcquire` makes one native acquisition attempt in an explicit shared or exclusive mode at an
absolute lock-file path whose parent already exists. Null means native contention; other errors throw. Cancellation
is checked before the attempt and before returning ownership, not by interrupting native calls. Construction is inert. All built-in providers expose
the optional capability; partial providers can omit it without breaking their existing contracts.

Windows opens a noninheritable regular disk handle with sharing enabled and takes an immediate shared or exclusive
lock on byte zero using `LockFileEx`. macOS/Linux use raw noninheritable, nonblocking, no-follow opens, validate the
opened file kind, and acquire `flock` with `LOCK_SH` or `LOCK_EX`, combined with `LOCK_NB`. Raw Unix opening avoids
the runtime's implicit shared lock and its best-effort treatment of unsupported locking. Native lock errors other
than contention fail explicitly. Disposal
unlocks before closing the handle, avoiding transient retention by concurrent Unix fork/exec. Process exit closes the
handle and releases ownership. Disposal is idempotent, and no native handle or stream is exposed to the caller.
The implementation does not rely on a process-local path dictionary.

The artifact persists and its bytes are not modified or interpreted. Hosts must not delete it during normal lease
release: unlinking/recreating a path can split ownership across distinct open files. Diagnostic metadata belongs in
a separate host-owned file and cannot prove current ownership. Existing foreign locking protocols are not coordinated.
Local filesystems supporting the native operations are the intended environment; no network-client or alias guarantee
is made. The leaf is validated, but ancestor replacement by noncooperating actors is outside the contract.

Shared owners coexist and exclude exclusive owners over the same artifact and protocol. `FileLeaseMode.Exclusive`
is the zero enum value, and every acquisition specifies its mode. The facade exposes one optional `FileLeases`
capability. There are no lock upgrades, hierarchy rules, or waiting policies in Platform; these belong to consumers.
