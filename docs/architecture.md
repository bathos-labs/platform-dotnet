# Platform architecture

The core owns focused capability contracts and shared mechanics. Each OS-specific assembly references only the core
and runtime; the core never references providers. Provider constructors avoid native work. Hosts explicitly supply
providers to selection and supply a native dispatcher when enabling interactive capabilities. Independent capability
interfaces support injection without requiring the facade. The facade supports application-level composition without DI.

File publication stages a private sibling, flushes completed bytes, and publishes through one native rename.
Exclusive rename checks and claims the destination atomically. Replacement is explicit; cross-volume copy/delete is
never a fallback. Cancellation after successful rename cannot reinterpret publication as failure. Disposal owns only
staging. File-kind inspection rejects links and special files without opening POSIX special entries; inspection is
advisory and is not a path reservation.

Linux is its own runtime target, not generic POSIX. Its statx provider currently supplies file-kind inspection only.
Do not hide missing capabilities behind a weaker implementation. Add capabilities from demonstrated consumers,
keeping product entities, document lifecycle, path policy, UI toolkits, and image pipelines in their owning packages.

AppKit owns native screen selection and callback completion. The supplied dispatcher must post asynchronously to
the native main loop, allowing the callback to unwind before resource cleanup. The host keeps that loop alive.
Callers that retire while sampling must reject stale results. OS compatibility and capability availability do not
imply permission or successful operation.

This repository was extracted from Makura Studio's reusable platform boundary. It contains no Studio runtime
references. The public distribution is MIT licensed; unrelated Bathos/Studio packages have separate licensing.
