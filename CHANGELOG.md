# Changelog

This changelog records changes that affect package consumers: capabilities, API contracts, behavior, compatibility,
and security. Internal maintenance and development tooling belong in pull requests and contributor documentation.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html). All four packages share one version. During prerelease
development, the API may change without compatibility shims; breaking changes are called out here.

## [Unreleased]

### Added

- Toolkit-independent native capability interfaces, OS detection, and explicit provider selection for host composition.
- Atomic staged file publication with exclusive creation and explicit replacement, without cross-volume copy fallbacks.
- macOS and Windows native rename and regular-file inspection providers.
- Linux regular-file inspection through statx; native rename and screen sampling are not yet provided on Linux.
- Optional macOS AppKit screen color sampling through a host-supplied native dispatcher.
- Independently selectable MIT-licensed core, macOS, Windows, and Linux packages targeting .NET 10.

### Fixed

- Publication relinquishes its staging path after a successful rename or disposal, preserving files subsequently
  created at that path. Publishing onto the staging filename is rejected.

[Unreleased]: https://github.com/bathos-labs/platform-dotnet/tree/master
