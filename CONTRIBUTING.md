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

Linux headless portal tests also require `libdbus-1.so.3`, `/usr/bin/dbus-run-session`, and system Python 3 with
`dbus` and `gi` modules (Ubuntu packages `dbus-daemon`, `python3-dbus`, and `python3-gi`). Each test starts its own
private bus and fake service; it never invokes a desktop picker or uses the user's portal. This validates protocol
and native transport behavior, not compositor support, permissions, or selection accuracy.
Desktop-entry activation tests also use system GIO/GLib/GObject and an owned test application. They do not open real
editors. The test application runs outside the test-project directory so it does not inherit the test runner entry point.

Update lockfiles intentionally with an unlocked restore after dependency changes. Keep package versions central.
The package check uses an isolated consumer and cache outside the repository, verifies MIT metadata/dependencies and expanded XML documentation,
and loads only the current host's provider. Tests use disposable files and real native operations on the executing
host. Linux rename/publication and macOS-only scenarios on other hosts are explicitly skipped; skips do not establish
implementation acceptance. Native permission, cross-volume, special-file, and interactive scenarios require their own evidence.

Tests await owned operations or use explicit gates for races. Do not use sleeps or polling as synchronization.
Preserve source/destination bytes on failed exclusive rename; never replace it with check-then-move or cross-volume
copy/delete. Cleanup must delete only files owned by the operation. No sensitive, machine-local, generated, or unrelated
product content belongs in commits.

Native CI runs on Linux x64, Windows x64, and macOS Arm64 for PRs and `master`, and before tagged publication.
An OS-specific failure is not excused by passing on another host. Screen selection is manual: invoke the picker
in a native host, sample inside/outside the application, cancel with Escape, and repeat; verify main-loop cleanup and
caller retirement. `task eng:sampler` supplies a small native launch harness; `task eng:sampler:smoke` checks its dispatch
and shutdown without opening the picker. See [native validation](docs/validation.md) for the procedure and evidence.
Do not weaken system privacy controls to automate selection. The macOS and Windows samplers have user-confirmed
interactive validation; Linux support remains experimental pending desktop and broader host validation.
The Linux harness is console-driven;
its portal owns the native UI loop and has no dispatcher smoke mode.
Desktop reveal and editor handoff require separate manual validation through `eng/Bathos.Platform.Desktop`;
headless fixtures and argument probes do not establish visible selection or Photoshop/Clip Studio compatibility.

## Changelog

Record consumer-visible changes under `Unreleased` in [CHANGELOG.md](CHANGELOG.md), grouped as Added, Changed,
Deprecated, Removed, Fixed, or Security as appropriate. Omit empty categories and internal-only maintenance.
Describe observable behavior and call out breaking API changes. Do not record an unreleased version as published.
Follow [RELEASING.md](RELEASING.md) when moving notes into a dated release section.

## API and developer documentation

Shipping libraries generate XML documentation and fail the build for missing public documentation (CS1591).
Engineering executables and tests are explicitly exempt in their own directory build properties; XML generation
remains enabled there because the unused-import analyzer requires it. Do not add a suppression to shipping projects.

Document caller-visible contracts: ownership, null results, native-thread requirements, concurrency, cancellation
boundaries, exceptions, and the limits of successful completion. Prefer meaningful summaries and parameter descriptions
to paraphrasing signatures. Shared interface/implementation contracts live in
`src/Bathos.Platform/Documentation/Contracts.xml` and use compiler-expanded `include` comments. Keep selectors and
parameter names synchronized with their declarations. The shipped XML must contain real text; do not rely on
implicit IDE inheritance or unresolved `inheritdoc` tags. No documentation generator is required.

Keep the README quick start runnable and integration examples in `docs/usage.md` self-contained. The package verifier
compiles every `csharp` fenced block in those files against the freshly packed libraries in an isolated consumer.
The README contains one executable quick start; guide snippets declare public example classes so they compile together.
On Windows/macOS, validation runs the README twice and verifies collision-safe publication. Interactive snippets are
compiled only; their acceptance remains manual. Retain the separate single-provider consumer check before examples
bring in all providers. Keep Markdown fences conventional so extraction remains deterministic.

When changing a public contract, update its XML comments, integration recipe, example, and consumer-facing changelog
entry together. Link published documentation to its tag; distinguish development guidance from the installed package's
contract. Before 1.0, explicitly review the supported/experimental capability matrix and the compatibility commitment.
A documentation build does not establish native acceptance or authorize a release. A hosted DocFX site remains an
optional long-term enhancement rather than part of this validation workflow.
