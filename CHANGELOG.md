# Changelog

This changelog records changes that affect package consumers: capabilities, API contracts, behavior, compatibility,
and security. Internal maintenance and development tooling belong in pull requests and contributor documentation.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html). All four packages share one version. During prerelease
development, the API may change without compatibility shims; breaking changes are called out here.

## [Unreleased]

### Added

- Complete public API XML documentation in all four packages, including native operation contracts, and expanded
  integration guidance with validated examples for composition, publication, desktop handoff, discovery, and sampling.
- A WPF dispatcher adapter and window example showing native screen sampling and shutdown coordination.

### Changed

- `IPlatformProvider` requires only its OS identity. Optional capability properties default to null, allowing partial
  capability sets and omission of newly introduced optional capabilities.

## [0.1.0-beta.1] - 2026-10-23

Initial public beta. Targets .NET 10; all four packages share this version. Linux support is experimental.
Prerelease APIs may change without compatibility shims.

### Added

- Native file reveal and explicit application handoff capabilities, with host-supplied executable, macOS bundle,
  and Linux desktop-entry targets; Linux desktop support remains experimental.
- Native registration lookup utilities for Windows App Paths, macOS bundle identifiers, and Linux desktop-entry IDs.
- Toolkit-independent native capability interfaces, OS detection, and explicit provider selection for host composition.
- Atomic staged file publication with exclusive creation and explicit replacement, without cross-volume copy fallbacks.
- macOS and Windows native rename and regular-file inspection providers.
- Experimental Linux regular-file inspection through statx; native rename is not yet provided on Linux.
- Optional macOS AppKit screen color sampling through a host-supplied native dispatcher.
- Optional Windows GDI screen color sampling through a host-supplied native dispatcher, using a frozen desktop snapshot.
- Experimental opt-in Linux screen color sampling through the desktop portal and system libdbus, without a host dispatcher.
- Independently selectable MIT-licensed core, macOS, Windows, and Linux packages targeting .NET 10.

### Limitations

- Linux native rename and atomic file publication are unavailable. Linux desktop integration has headless protocol
  and native GIO test coverage, but no interactive desktop acceptance record.
- Screen colors are opaque eight-bit RGB for ordinary selection, without calibrated color or HDR guarantees.
- Desktop handoff reports native submission, not document readiness or application focus. Interactive Finder,
  Explorer, and editor validation remains pending; see the native validation record.

[Unreleased]: https://github.com/bathos-labs/platform-dotnet/compare/v0.1.0-beta.1...master
[0.1.0-beta.1]: https://github.com/bathos-labs/platform-dotnet/releases/tag/v0.1.0-beta.1
