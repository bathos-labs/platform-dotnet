# Contributing

Use the SDK pinned by `global.json` and Node.js 24 LTS with its bundled npm/npx for documentation
formatting. The default branch is `master`. Keep changes focused and preserve explicit capability
absence, native-resource ownership, nullable analysis, and publication guarantees. Update tests and
docs with contract changes. Preserve stable public API contracts for callers and implementers within
a major version; breaking contract changes require an explicitly planned major release.

Run `task check` from the repository root on Windows, macOS, or Linux. `task --list` lists focused
build, test, formatting, packaging, and release commands. Use `task lint` for the fast whitespace,
formatting, and documentation-link gate, or `task format` to apply C# and Markdown formatting.
`task check` runs lint followed by the build, tests, and package verification.
[Task](https://taskfile.dev/) is optional; the equivalent lint and full-check commands are:

```text
git diff --check
git diff --cached --check
npx --yes prettier@3.9.10 --check "**/*.md"
npx --yes --package=remark-cli@12.0.1 --package=remark-gfm@4.0.1 --package=remark-validate-links@13.1.0 --package=remark-lint-no-undefined-references@5.0.2 remark --rc-path .remarkrc.cjs --ignore-path .prettierignore --frail --no-stdout --no-color .
dotnet restore Bathos.Platform.slnx --locked-mode
dotnet format Bathos.Platform.slnx --verify-no-changes --no-restore
npx --yes --package=remark-cli@12.0.1 --package=remark-gfm@4.0.1 --package=remark-validate-links@13.1.0 --package=remark-lint-no-undefined-references@5.0.2 node eng/CheckDocumentationLinks.test.cjs
dotnet build Bathos.Platform.slnx --configuration Release --no-restore
dotnet test --solution Bathos.Platform.slnx --configuration Release --no-build
dotnet pack Bathos.Platform.slnx --configuration Release --no-build --no-restore --output artifacts/packages
dotnet run --project eng/Bathos.Platform.Build --configuration Release --no-build -- verify-packages artifacts/packages
```

`task lint` runs the first six commands above in order and does not build the solution, run tests,
or pack libraries. Its whitespace checks include both unstaged and staged changes; CI checks the
commit being validated. Both use the same Markdown/link configuration, pinned tool versions, locked
restore, and C# formatting rules. The full check also runs disposable link-checker regression
fixtures and reuses that restore before the Release build.

Use `task code:format` and `task code:check` for C# alone, or `task docs:format` and
`task docs:check` for Markdown alone. `task format:check` verifies both without edits. Without Task,
apply Markdown with `npx --yes prettier@3.9.10 --write "**/*.md"`, then restore locked dependencies
and run `dotnet format Bathos.Platform.slnx --no-restore` for C#.

## Markdown formatting

After documentation edits, run `task docs:format` (or
`npx --yes prettier@3.9.10 --write "**/*.md"`), then review the diff. Prettier wraps prose at a
100-column target and aligns table columns. Tables, long links, and code blocks may exceed that
width; embedded code formatting is disabled to preserve examples. Generated output is excluded
through `.prettierignore` and `.gitignore`.

`task docs:check` verifies formatting without edits. It also runs as part of `task check`, and CI
runs the same pinned Prettier check once in the common validation job. Formatting differences fail
validation; CI does not write changes back. Agents must apply formatting and complete validation
before committing.

Prettier runs through `npx` with an exact version, using npm's cache without a repository package
manifest, lockfile, or local installation. A cold cache requires npm registry access. When
upgrading, update the pins in `Taskfile.yml`, `.github/workflows/validate.yml`, and this guide
together, then reformat and review the Markdown. Editor integrations should use the same Prettier
version and repository configuration.

## Documentation links

Run `task docs:links` to validate local file targets, same-file and cross-file heading anchors,
images, and reference-link definitions. It also runs in `task lint` and the common CI prerequisite
job, before the native matrix. The command reads Markdown without rewriting it; Prettier continues
to own formatting. The pinned remark CLI, GFM parser, link validator, and undefined-reference rule
run through `npx` without a repository package manifest or local installation. Keep their pins in
`Taskfile.yml`, `.github/workflows/validate.yml`, and the equivalent commands above synchronized.

The checker handles GitHub heading slugs, duplicate headings, encoded paths and fragments, and
root-relative paths. Fenced code and inline-code examples are not interpreted as links. Markdown
inputs use `.prettierignore` to exclude generated output and dependency caches. Full GitHub
`blob/<ref>/...` links into this repository are checked against the local checkout; the ref is not
fetched. Use relative links or the existing `blob/master/...` references for source documentation,
and release-page links for historical releases. Slash-containing refs in absolute file URLs are not
supported by the upstream validator.

The checker makes no HTTP requests to external sites or GitHub. An unavailable website cannot fail
this gate; installing uncached `npx` tools still requires npm registry access. External availability
checks are not part of local lint or required CI. Failures identify the source file, line, and
broken destination; correct the link or target heading and rerun the check.

`task docs:links:test` checks the tool/configuration behavior against disposable fixtures, including
encoded Unicode anchors, reference links, GFM tables, ignored outputs, missing files/headings, and
repair. It runs in `task check` and common CI. No native APIs, desktop services, or external
websites are used.

## Native development and validation

Linux headless portal tests also require `libdbus-1.so.3`, `/usr/bin/dbus-run-session`, and system
Python 3 with `dbus` and `gi` modules (Ubuntu packages `dbus-daemon`, `python3-dbus`, and
`python3-gi`). Each test starts its own private bus and fake service; it never invokes a desktop
picker or uses the user's portal. This validates protocol and native transport behavior, not
compositor support, permissions, or selection accuracy. Desktop-entry activation tests also use
system GIO/GLib/GObject and an owned test application. They do not open real editors. The test
application runs outside the test-project directory so it does not inherit the test runner entry
point.

Update lockfiles intentionally with an unlocked restore after dependency changes. Keep package
versions central. The package check uses an isolated consumer and cache outside the repository. It
verifies MIT license metadata, dependencies, and expanded XML documentation, and loads only the
current host's provider. Tests use disposable files and real native operations on the executing
host. Host-specific scenarios on other hosts are explicitly skipped; skips do not establish
implementation acceptance. Native permission, cross-volume, special-file, and interactive scenarios
require their own evidence.

Tests await owned operations or use explicit gates for races. Do not use sleeps or polling as
synchronization. Preserve source/destination bytes on failed exclusive rename; never replace it with
check-then-move or cross-volume copy/delete. Cleanup must delete only files owned by the operation.
No sensitive, machine-local, generated, or unrelated product content belongs in commits.

CI first runs whitespace, Markdown formatting and local-link checks, link-checker regression
fixtures, and C# formatting checks in a common Ubuntu job. The Linux x64, Windows x64, and macOS
Arm64 native jobs start only after that job passes. Each native job restores locked dependencies,
builds with analyzers, runs tests with coverage, and packs and verifies packages on its own host.
These checks run for PRs and `master`, and before tagged publication. An OS-specific failure is not
excused by passing on another host. Screen selection is manual: invoke the picker in a native host,
sample inside/outside the application, cancel with Escape, and repeat; verify main-loop cleanup and
caller retirement. `task eng:sampler` supplies a small native launch harness;
`task eng:sampler:smoke` checks its dispatch and shutdown without opening the picker. See the
[native validation guide](docs/validation.md) for the procedure. Respect system privacy controls.
Linux support is experimental. Its harness is console-driven; the portal owns the native UI loop and
has no dispatcher smoke mode. Desktop reveal and editor handoff require separate manual validation
through `eng/Bathos.Platform.Desktop`; headless fixtures and argument probes do not establish
visible selection or editor compatibility.

## Published package consumers

`task check` and `task packages:check` also publish and execute the isolated consumer in
self-contained directory, bundled single-file, and rooted trimming modes. This adds three publish
operations per host and downloads matching SDK runtime/linker packs from NuGet.org into a disposable
cache. Both Bathos packages are source-mapped exclusively to the freshly packed feed. Native
operations run only on the executing OS; see the
[publish evidence](docs/validation.md#published-consumer-evidence) and
[consumer deployment limits](docs/platform-support.md#consumer-publishing).

Use `task packages:aot` to include the rooted Native AOT publish/run probe. Without Task, pack as
above, then run:

```text
dotnet run --project eng/Bathos.Platform.Build --configuration Release --no-build -- verify-packages artifacts/packages --native-aot
```

AOT is optional for the ordinary local check and required in each native CI job. It adds a fourth
publish operation and requires a native toolchain: Xcode Command Line Tools on macOS, Visual
Studio's Desktop development with C++ workload on Windows, or `clang` and `zlib1g-dev` on Ubuntu.
The macOS and Windows runner images supply their toolchains; CI installs the Linux prerequisites.
These tools and SDK packs are development requirements, not shipping package dependencies. See
Microsoft's
[Native AOT prerequisites](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/).

All four publish modes are blocking CI checks, including the experimental trimming and Native AOT
probes. Keep them blocking unless demonstrated toolchain or maintenance churn warrants an explicit
decision to change that policy.

The shipping libraries are not marked trim/AOT-compatible. Rooted analysis covers their full code,
but executed noninteractive operations cover a smaller surface. Keep warnings actionable and runtime
evidence explicit; do not add suppressions or compatibility metadata merely to pass a probe. Publish
modes use clean intermediates and sequential owned processes, not sleeps or polling.

## Issues and pull requests

Use the [issue chooser](https://github.com/bathos-labs/platform-dotnet/issues/new/choose) for bug
reports, capability requests, and integration questions. Consumers can report an installed-package
problem without checking out or building this repository. Feature requests and questions have
lightweight templates; blank issues remain available.

The PR template asks for the resulting behavior, validation on the executing host, and relevant
documentation and public-contract compatibility details. Link the related issue when applicable, run
`task check`, and identify pending manual acceptance. Keep detailed contracts and procedures in
their existing guides rather than copying them into issue or PR templates.

## Merge requirements

The active GitHub ruleset for `master` requires a PR and passing common validation, all three native
jobs, and complete native coverage upload. The [ruleset definition](eng/master-branch.ruleset.json)
records the exact check names and pins their source to the GitHub Actions app. Keep this definition
and the live ruleset synchronized when renaming jobs or changing policy. The definition is
configuration for GitHub settings; builds do not apply it automatically.

PR branches must be up to date with `master` before merging. No approving reviews are required, and
there are no owner, administrator, or app bypass actors. Direct pushes, force pushes, and deletion
of `master` are blocked. An exceptional recovery requires an explicit administrator change to the
ruleset. Feature branches retain their existing workflow, and the release/tag publication process
remains governed by [RELEASING.md](RELEASING.md).

Common validation is required independently because its failure skips the downstream native jobs;
GitHub can accept skipped required checks. Native jobs include the experimental trimming and AOT
probes, which remain blocking. Complete coverage upload is required, while Codecov percentage and
patch checks remain informational.

When changing merge policy, verify GitHub's merge eligibility with a temporary ready-for-review PR:
a failed common job must block merging even when native jobs are skipped, a failed native job must
block merging, and all required checks passing on an up-to-date branch must allow merging. Inspect
eligibility without merging the probe; close it and remove its branch afterward. Record the evidence
in the tracking issue.

## Changelog

Record consumer-visible changes under `Unreleased` in [CHANGELOG.md](CHANGELOG.md), grouped as
Added, Changed, Deprecated, Removed, Fixed, or Security as appropriate. Omit empty categories and
internal-only maintenance. Describe observable behavior and call out breaking API changes. Do not
record an unreleased version as published. Follow [RELEASING.md](RELEASING.md) when moving notes
into a dated release section.

## API and developer documentation

Keep the README and consumer guides largely static: use versionless stable installation commands,
keep release history in the changelog, and update capability or prerequisite facts only when
behavior changes. The README owns the capability/platform matrix, `docs/usage.md` owns integration
recipes, and `docs/platform-support.md` owns native deployment requirements and tested
configurations. `docs/validation.md` owns evidence and manual acceptance procedures. Link to the
owning page instead of copying its detailed requirements. Do not add generated installation blocks
or repeated current package-version declarations.

All four packages share a release version and channel. Linux's experimental implementation status
does not require its package to use a different version or prerelease channel. Keep pending native
and desktop acceptance explicit. Develop upcoming behavior and its documentation on feature/release
branches, advancing `Version` when development resumes; prefer small releases so `master` stays
close to the published state. Source documentation can describe changes awaiting publication; the
matching tag and shipped XML describe an installed package.

Present built-in OS providers as the normal integration path. Keep application dependencies focused
on the capability interfaces they use; these and the native dispatcher support host integration,
substitution, and testing. The provider facade is a composition convenience, and partial providers
are an optional technique. Expand provider infrastructure only for demonstrated consumer
requirements.

Add new capabilities as focused interfaces with optional default-null properties on
`IPlatformProvider`; its OS identity is the only required member. Do not add required operations to
existing interfaces or weaken their guarantees as an additive change. For stable releases,
compatibility review covers both callers and implementers, including public facade implementations.
Default operation bodies must provide meaningful behavior that satisfies the contract.

Shipping libraries generate XML documentation and fail the build for missing public documentation
(CS1591). Engineering executables and tests are explicitly exempt in their own directory build
properties; XML generation remains enabled there because the unused-import analyzer requires it. Do
not add a suppression to shipping projects.

Document caller-visible contracts: ownership, null results, native-thread requirements, concurrency,
cancellation boundaries, exceptions, and the limits of successful completion. Treat native error
messages as diagnostic details; document actionable outcomes without promising identical native
failures across operating systems. Prefer meaningful summaries and parameter descriptions to
paraphrasing signatures. Shared interface/implementation contracts live in
`src/Bathos.Platform/Documentation/Contracts.xml` and use compiler-expanded `include` comments. Keep
selectors and parameter names synchronized with their declarations. The shipped XML must contain
real text; do not rely on implicit IDE inheritance or unresolved `inheritdoc` tags. No documentation
generator is required.

Keep the README quick start runnable and integration examples in `docs/usage.md` self-contained. The
package verifier compiles every `csharp` fenced block in those files against the freshly packed
libraries in an isolated consumer. It executes the README's versionless installation commands
against a feed containing only the candidate packages and checks the resolved versions. Prerelease
candidates are opted into only in that isolated feed; this does not establish public package
availability. The packaged README must preserve the source text while pinning documentation links to
its release tag. The README contains one executable quick start, compiled with only the packages
listed by its installation commands; guide snippets declare public example classes and are compiled
separately with all providers available. On Windows/macOS/Linux, validation runs the README twice
and verifies the expected bytes and collision-safe publication. The WPF example in
`docs/examples/wpf-dispatcher.md` is compiled separately on Windows with the WPF framework
reference. Interactive snippets are compiled only; their acceptance remains manual. Retain the
separate single-provider consumer check before examples bring in all providers. Keep Markdown fences
conventional so extraction remains deterministic.

When changing a public contract, update its XML comments, integration recipe, example, and
consumer-facing changelog entry together. Link published documentation to its tag; distinguish
development guidance from the installed package's contract. Keep the support status and
compatibility commitment current for each release. Linux native support is experimental; the stable
public API compatibility commitment still applies. A documentation build does not establish native
acceptance or authorize a release. A hosted DocFX site remains an optional long-term enhancement
rather than part of this validation workflow.

## Released API compatibility

All four shipping packages enable the SDK's package validation during `dotnet pack`. The stable
baseline is pinned in `eng/Bathos.Package.props`; removed or incompatible signatures, dropped target
frameworks, and changed parameter names fail the gate. Compatible additions are allowed. Do not
enable strict baseline equality or automatically suppress diagnostics: a failure requires a
compatibility-preserving design or an explicitly approved breaking release.

The existing `verify-packages` command also exercises `eng/api-compatibility` in isolated temporary
projects:

1. Restore the released packages from NuGet.org using the consumer's committed lockfile, then
   compile its callers, minimal `IPlatformProvider`, and `INativeFileRename` implementation against
   that release.
2. Compile the same source against only the freshly packed candidate packages in a separate cache.
3. Run the original released consumer assembly with the candidate packages, without recompiling it.
   Verify all four loaded package assembly hashes and the consumer hash; exercise old constructors,
   named calls, native inspection, publication, and optional capability defaults on the old provider
   implementation.

The released consumer must use only baseline APIs. The current runner may assert new optional
defaults. Keep the baseline consumer's source and lockfile together when advancing the stable
baseline after a release; regenerate its lockfile in an isolated copy with `BathosVersion` set to
the new stable version. Never resolve the baseline from the local candidate feed or update it
automatically to make a failure disappear. Baseline restore requires NuGet.org; candidate consumers
use only the local feed. No compatibility tooling becomes a shipping runtime dependency.

These checks cover API shape and representative source/binary integration. They do not establish
every consumer's behavior or replace native ownership, cancellation, filesystem, and manual desktop
validation.

## Standalone coverage and CI setup

`task test:coverage` runs the same standalone suite with Coverlet's Microsoft.Testing.Platform
extension and writes raw Cobertura/TRX evidence under `artifacts/test-results` plus a
repository-relative report under `artifacts/coverage`. No consuming application or sibling checkout
is needed. Equivalent commands after the Release build are:

```text
dotnet run --project eng/Bathos.Platform.Build --configuration Release --no-build -- clean-test-results
dotnet test --solution Bathos.Platform.slnx --configuration Release --no-build --coverlet --coverlet-output-format cobertura --coverlet-include "[Bathos.Platform*]*" --coverlet-exclude "[Bathos.Platform.Tests]*" --coverlet-exclude "[Bathos.Platform.TestApplication]*" --coverlet-exclude-by-file "**/obj/**" --coverlet-exclude-by-attribute GeneratedCodeAttribute --report-xunit-trx --results-directory artifacts/test-results
dotnet run --project eng/Bathos.Platform.Build --configuration Release --no-build -- verify-coverage artifacts/test-results artifacts/coverage/coverage.cobertura.xml
```

Coverage includes all four shipping assemblies on every host, including unexecuted native code.
Exclusions are limited to test/engineering assemblies, generated `obj` sources, and
`GeneratedCodeAttribute`; compiler-generated async state machines and handwritten interop remain
measurable. Review changed-code coverage and uncovered failure branches together with assertions and
ownership guarantees. Executing a line does not establish correctness. See
[validation](docs/validation.md) for the portable, native, skipped, and manual evidence boundaries.

CI retains each platform's raw and normalized reports with TRX for 14 days. Only after all three
native jobs pass and all three reports exist does the reporting job upload `linux`, `windows`, and
`macos` flags. Codecov combines these into aggregate coverage; flags retain host visibility.
Notifications wait for three uploads and passing CI, with no carried-forward platform evidence. The
upload job must also pass: a partially uploaded Codecov view or a missing comment is not complete
matrix evidence. Missing/empty reports and upload errors fail visibly; coverage percentages and
patch checks are informational and must not be made required branch-protection checks initially. No
percentage threshold is enforced.

Owner setup: enable `bathos-labs/platform-dotnet` in Codecov and authorize the Codecov GitHub App
for this repository so it can publish PR comments/checks. Uploads use
[Codecov OIDC](https://github.com/codecov/codecov-action#using-oidc) with `id-token: write` confined
to the reporting job; no upload secret is required. Organization policy must allow that permission.
Fork PRs keep the ordinary `pull_request` security boundary; do not switch to `pull_request_target`
or expose secrets to make an upload pass. If policy blocks authentication, retain the artifacts and
report the blocked upload. After merging, verify a complete `master` run in Codecov to establish the
comparison baseline and activate the README badge. The initial PR may have no base coverage to
compare. Never store Codecov credentials in source files.
