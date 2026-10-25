# Changelog

This changelog records changes that affect package consumers: capabilities, API contracts, behavior,
compatibility, and security. Internal maintenance and development tooling belong in pull requests
and contributor documentation.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html). All four packages share one version and
release channel. Stable releases preserve public API contracts for callers and implementers within a
major version. Breaking contract changes require a new major version and are called out here. Linux
native support remains experimental; public API compatibility still applies.

## [Unreleased]

## [1.1.0] - 2026-10-25

Changes since the previous stable release, `1.0.0`. Prerelease entries below preserve the
development history.

### Added

- Cooperative shared and exclusive file leases on Windows, macOS, and Linux, with explicit
  contention and native ownership lifetime. Lock artifacts and their containing directories must
  remain intact while leased.
- Native path normalization and file-URI conversion, explicit portability validation for new
  filenames, and standard-directory discovery with application scoping and host-supplied root
  overrides. Discovery does not create directories.
- Installed color-profile discovery and raw-byte access on Windows and macOS, with configurable
  inventory/read limits, incomplete-inventory reporting, and cancellation. Linux leaves this
  optional capability unavailable.
- Optional snapshot-file rename and shared byte publication with explicit directory-creation policy.
  Windows snapshot replacement preserves existing readers through handle-based POSIX rename
  semantics; directory relocation retains its existing provider. Existing providers may omit the new
  capability.
- Experimental Linux atomic rename and staged publication through `renameat2`, including exclusive
  claims and explicit replacement without cross-volume copy fallback.
- Staging-filename recognition for host-owned accounting and cleanup, without inferring ownership or
  abandonment.

### Fixed

- Exact file publication preserves native macOS/Linux filename characters such as literal
  backslashes instead of applying Windows separator rules. Generated names remain portable.
- Corrected copyright attribution and package authorship to Bathos Pty Ltd.

## [1.1.0-rc.1] - 2026-10-25

### Fixed

- Exact file publication preserves native macOS/Linux filename characters such as literal
  backslashes instead of applying Windows separator rules. Generated names remain portable.

## [1.1.0-beta.2] - 2026-10-25

### Added

- Staging-filename recognition for host-owned accounting and cleanup, without inferring ownership or
  abandonment.
- Optional snapshot-file rename capability and shared byte publication with explicit
  directory-creation policy. Windows snapshot replacement preserves readers of the previous file
  through handle-based POSIX rename semantics; directory relocation retains its existing provider.
  Existing providers may omit the new capability.
- Experimental Linux atomic rename and staged publication through renameat2, including exclusive
  claims and explicit replacement without cross-volume copy fallback.

## [1.1.0-beta.1] - 2026-10-25

### Added

- Cooperative shared or exclusive file leases on Windows, macOS, and Linux, with explicit contention
  and native ownership lifetime. Lock artifacts and their containing directories must remain intact
  at a stable location while leased.

- Optional standard-directory discovery and explicit application scoping, including Linux XDG roots,
  unavailable-location results, and host-supplied root overrides without directory creation.

- Optional host-native path normalization and file-URI conversion, with explicit portability
  validation for new filenames. Lexical conversion does not establish filesystem identity or lease
  ownership.

- Optional installed color-profile discovery and raw-byte access through `IColorProfileProvider` and
  the provider facade. macOS uses ColorSync with observed current device associations; Windows uses
  installed ICC/WCS inventory and native reads, without claiming device defaults or HDR coverage.
  Linux leaves the capability unavailable.
- Configurable profile-count and read-byte limits, incomplete-inventory reporting, and cancellation
  support. Profile interpretation, installation, and color conversion remain application
  responsibilities.

### Fixed

- Corrected copyright attribution and package authorship to Bathos Pty Ltd.

## [1.0.0] - 2026-10-23

First stable release of the native platform abstractions for .NET 10. Windows and macOS provide file
inspection, atomic staged publication, desktop reveal, explicit application handoff and lookup, and
optional screen color sampling. Linux native support remains experimental; native rename and file
publication are unavailable.

### Added

- Complete public API XML documentation in all four packages, including native operation contracts,
  and expanded integration guidance with validated examples for composition, publication, desktop
  handoff, discovery, and sampling.
- A WPF dispatcher adapter and window example showing native screen sampling and shutdown
  coordination.

### Changed

- `IPlatformProvider` requires only its OS identity. Optional capability properties default to null,
  allowing partial capability sets and omission of newly introduced optional capabilities.

## [0.1.0-beta.1] - 2026-10-23

Initial public beta. Targets .NET 10; all four packages share this version. Linux support is
experimental. Prerelease APIs may change without compatibility shims.

### Added

- Native file reveal and explicit application handoff capabilities, with host-supplied executable,
  macOS bundle, and Linux desktop-entry targets; Linux desktop support remains experimental.
- Native registration lookup utilities for Windows App Paths, macOS bundle identifiers, and Linux
  desktop-entry IDs.
- Toolkit-independent native capability interfaces, OS detection, and explicit provider selection
  for host composition.
- Atomic staged file publication with exclusive creation and explicit replacement, without
  cross-volume copy fallbacks.
- macOS and Windows native rename and regular-file inspection providers.
- Experimental Linux regular-file inspection through statx; native rename is not yet provided on
  Linux.
- Optional macOS AppKit screen color sampling through a host-supplied native dispatcher.
- Optional Windows GDI screen color sampling through a host-supplied native dispatcher, using a
  frozen desktop snapshot.
- Experimental opt-in Linux screen color sampling through the desktop portal and system libdbus,
  without a host dispatcher.
- Independently selectable MIT-licensed core, macOS, Windows, and Linux packages targeting .NET 10.

### Limitations

- Linux native rename and atomic file publication are unavailable. Linux desktop integration has
  headless protocol and native GIO test coverage, but no interactive desktop acceptance record.
- Screen colors are opaque eight-bit RGB for ordinary selection, without calibrated color or HDR
  guarantees.
- Desktop handoff reports native submission, not document readiness or application focus.
  Interactive Finder, Explorer, and editor validation remains pending; see the native validation
  record.

[Unreleased]: https://github.com/bathos-labs/platform-dotnet/compare/v1.1.0...master
[1.1.0]: https://github.com/bathos-labs/platform-dotnet/releases/tag/v1.1.0
[1.1.0-rc.1]: https://github.com/bathos-labs/platform-dotnet/releases/tag/v1.1.0-rc.1
[1.1.0-beta.2]: https://github.com/bathos-labs/platform-dotnet/releases/tag/v1.1.0-beta.2
[1.1.0-beta.1]: https://github.com/bathos-labs/platform-dotnet/releases/tag/v1.1.0-beta.1
[1.0.0]: https://github.com/bathos-labs/platform-dotnet/releases/tag/v1.0.0
[0.1.0-beta.1]: https://github.com/bathos-labs/platform-dotnet/releases/tag/v0.1.0-beta.1
