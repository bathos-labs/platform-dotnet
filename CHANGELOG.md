# Changelog

This changelog records changes that affect package consumers: capabilities, API contracts, behavior, compatibility,
and security. Internal maintenance and development tooling belong in pull requests and contributor documentation.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html). All four packages share one version. During prerelease
development, the API may change without compatibility shims; breaking changes are called out here.

## [Unreleased]

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

### Changed

- Breaking: custom `IPlatformProvider` implementations must now expose `FileRevealer` and `FileInvoker` capabilities.
- Screen colors describe eight-bit RGB for ordinary color selection; calibrated sRGB screen appearance is no longer promised.

### Fixed

- Publication relinquishes its staging path after a successful rename or disposal, preserving files subsequently
  created at that path. Publishing onto the staging filename is rejected.

[Unreleased]: https://github.com/bathos-labs/platform-dotnet/tree/master
