# Platform architecture

The core owns focused capability contracts and shared mechanics. Each OS-specific assembly references only the core
and runtime; the core never references providers. Provider constructors avoid native work. Hosts explicitly supply
providers to selection and supply a native dispatcher when enabling interactive capabilities. Independent capability
interfaces support injection without requiring the facade. The facade supports application-level composition without DI.

The built-in Windows, macOS, and Linux providers are the normal integration path. Focused capability interfaces and
the native dispatcher are the supported boundaries for host integration, substitution, and testing. The provider facade
groups capabilities for application composition, with selection using repository-defined OS identities and explicitly
supplied instances. Partial providers are an optional composition technique: only OS identity is required, and capability
getters default to null. Consumers check availability before passing focused interfaces to application components.

New capabilities use focused interfaces and optional default-null facade properties. Existing providers can omit them;
public facade implementations remain part of compatibility review. Broader provider infrastructure needs a demonstrated
consumer requirement. The current model allows capability growth without requiring a third-party provider ecosystem.
Default operation implementations must fulfill the operation's contract; silent no-ops and throwing placeholders do not
establish capability support. Changes to existing required operations or their guarantees need explicit compatibility review.

File publication stages a private sibling, flushes completed bytes, and publishes through one native rename.
Exclusive rename checks and claims the destination atomically. Replacement is explicit; cross-volume copy/delete is
never a fallback. Cancellation after successful rename cannot reinterpret publication as failure. Disposal owns only
staging. File-kind inspection rejects links and special files without opening POSIX special entries; inspection is
advisory and is not a path reservation.

Linux is its own runtime target, not generic POSIX. Its experimental provider supplies statx file-kind inspection
and opt-in desktop-portal color sampling; native rename and broader host validation are pending.
Do not hide missing capabilities behind a weaker implementation. Add capabilities from demonstrated consumers,
keeping product entities, document lifecycle, path policy, UI toolkits, and image pipelines in their owning packages.

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
