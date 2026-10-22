# Contributing

Use the SDK pinned by `global.json`. The default branch is `master`. Keep changes focused and preserve explicit
capability absence, native-resource ownership, nullable analysis, and publication guarantees. Update tests and docs
with contract changes. No compatibility aliases or migrations are needed for unreleased APIs unless explicitly agreed.

Run `task check` from the repository root on Windows, macOS, or Linux. `task --list` lists focused build, test,
formatting, packaging, and release commands. [Task](https://taskfile.dev/) is optional; the equivalent SDK commands are:

```text
dotnet restore Bathos.Platform.slnx --locked-mode
dotnet format Bathos.Platform.slnx --verify-no-changes --no-restore
dotnet build Bathos.Platform.slnx --configuration Release --no-restore
dotnet test --solution Bathos.Platform.slnx --configuration Release --no-build
dotnet pack Bathos.Platform.slnx --configuration Release --no-build --no-restore --output artifacts/packages
dotnet run --project eng/Bathos.Platform.Build --configuration Release --no-build -- verify-packages artifacts/packages
```

Update lockfiles intentionally with an unlocked restore after dependency changes. Keep package versions central.
The package check uses an isolated consumer and cache outside the repository, verifies MIT metadata/dependencies,
and loads only the current host's provider. Tests use disposable files and real native operations on the executing
host. Linux rename/publication and macOS-only scenarios on other hosts are explicitly skipped; skips do not establish
implementation acceptance. Native permission, cross-volume, special-file, and interactive scenarios require their own evidence.

Tests await owned operations or use explicit gates for races. Do not use sleeps or polling as synchronization.
Preserve source/destination bytes on failed exclusive rename; never replace it with check-then-move or cross-volume
copy/delete. Cleanup must delete only files owned by the operation. No sensitive, machine-local, generated, or unrelated
product content belongs in commits.

Native CI runs on Linux x64, Windows x64, and macOS Arm64 for PRs and `master`, and before tagged publication.
An OS-specific failure is not excused by passing on another host. AppKit screen selection is manual: invoke the picker
in a native host, sample inside/outside the application, cancel with Escape, and repeat; verify main-loop cleanup and
caller retirement. `task sampler` supplies a small native launch harness; `task sampler:smoke` checks its dispatch
and shutdown without opening the picker. See [native validation](docs/validation.md) for the procedure and evidence.
Do not weaken system privacy controls to automate selection. Windows confirmation is pending; Linux support remains
experimental pending broader host validation.

## Changelog

Record consumer-visible changes under `Unreleased` in [CHANGELOG.md](CHANGELOG.md), grouped as Added, Changed,
Deprecated, Removed, Fixed, or Security as appropriate. Omit empty categories and internal-only maintenance.
Describe observable behavior and call out breaking API changes. Do not record an unreleased version as published.
Follow [RELEASING.md](RELEASING.md) when moving notes into a dated release section.
