# Native validation

Linux support is experimental. CI runs automated native tests on Windows, macOS, and Linux;
interactive desktop behavior can be checked using the engineering harnesses below. The harnesses are
not shipped in NuGet packages. See [platform support](platform-support.md) for consumer deployment
requirements; this guide describes validation evidence.

## Automated evidence boundaries

A common Ubuntu CI job checks whitespace, Markdown formatting, local links, and the link-checker
regression fixtures, then restores locked dependencies and verifies C# formatting. It also builds
the release checker and runs its [isolated Git policy fixtures](../RELEASING.md#guard-verification).
The three native jobs depend on its success; a failure skips the matrix and downstream coverage
upload. Native jobs each retain their own locked restore, build and analyzer checks, tests, coverage
verification, packing, and package-consumer checks. Build output and native evidence are not shared
across operating systems.

All tests build and run from this repository alone with `task test` or `task test:coverage`. Every
test is labeled `Validation=Portable` or `Validation=Native`. After building, use the existing
runner to select one evidence tier or feature independently, for example:

```text
dotnet test --solution Bathos.Platform.slnx --configuration Release --no-build --filter-trait "Validation=Portable"
dotnet test --solution Bathos.Platform.slnx --configuration Release --no-build --filter-trait "Validation=Native"
dotnet test --solution Bathos.Platform.slnx --configuration Release --no-build --filter-class "*Publication*"
```

The full CI suite runs both tiers. Platform-inapplicable native cases still report explicit skips;
manual checks are procedures, not disabled tests counted as automated evidence.

| Evidence                                 | Runs where           | What it establishes                                                                                                                                                           |
| ---------------------------------------- | -------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Portable contracts                       | Every supported host | Capability selection/absence, input validation, filename rules, and controlled ownership/concurrency outcomes. Test doubles establish orchestration, not native API behavior. |
| Native filesystem and executable handoff | Executing OS only    | Real disposable files, native rename/inspection, registration lookup, and an owned argument/exit probe. Linux exercises renameat2 on the executing filesystem.                |
| Private D-Bus and GIO integration        | Linux only           | Real native transport and activation against isolated fixtures, without desktop services.                                                                                     |
| Foreign-platform scenarios               | Explicitly skipped   | No evidence for the skipped OS; retain TRX reasons and review all matrix legs.                                                                                                |
| Desktop and permissions                  | Manual only          | Picker interaction, visible selection, real editor behavior, privacy controls, and multi-display behavior described below.                                                    |

Coverage reports include handwritten native code even when it cannot run on the current host.
Aggregate coverage is useful for locating gaps, but must be read with per-platform flags, successful
matrix jobs, skipped-test reasons, and behavioral assertions. A macOS run cannot accept
Windows/Linux behavior. Cross-volume, permission denial, and special filesystem behavior require
evidence on the relevant filesystem; an unattended substitute does not establish them.

## Contract audit and remaining gaps

| Capability                    | Unattended assertions                                                                                                                                                                                                                                                                                                                                                                                                                                                                              | Remaining native/desktop evidence                                                                                                                                                                                                                                |
| ----------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Provider composition          | Minimal implementers inherit null capabilities; selection rejects absence and ambiguity; construction does no native work.                                                                                                                                                                                                                                                                                                                                                                         | Service/permission availability cannot be inferred from a nonnull capability.                                                                                                                                                                                    |
| Publication                   | Real concurrent publishers, collision gaps, explicit replacement, staging-path reuse; controlled native failures, flush failure, cancellation between attempts and after success, single-use ownership and recovery with a new operation.                                                                                                                                                                                                                                                          | Real cross-volume rejection, denied access, and unusual/network filesystems need host/filesystem-specific checks. The portable rename double does not establish atomicity.                                                                                       |
| Snapshot publication          | Retained readers finish old bytes while later readers see the replacement; exclusive collisions, cancellation, directory-creation policy, and failed-publication cleanup use each host's native snapshot capability. Windows tests deny destination delete-sharing or mark the destination read-only, verifying retained source/destination bytes, handle release, staging cleanup, and retry. POSIX tests replace a read-only destination in a writable directory while retaining its old reader. | Windows requires FileRenameInfoEx/POSIX support. Unusual/network filesystems and metadata policies require separate validation; atomic visibility is not power-loss durability.                                                                                  |
| Entry inspection/rename       | Real files, links, FIFOs/sockets, retained bytes on failed rename, successful subsequent operations, and Windows handle-sharing cleanup. Special-entry probes run in owned child processes with a deadline, so accidentally opening a FIFO cannot strand the runner.                                                                                                                                                                                                                               | Windows symlink fixtures explicitly skip when privilege is unavailable; run them with privilege or record a manual check. Junctions/devices and adversarial ancestor replacement remain separate cases.                                                          |
| Desktop handoff/discovery     | Owned executable argument/exit probe, cancellation before/after submission, native invalid-executable errors; actual registration lookup and malformed Windows App Paths records.                                                                                                                                                                                                                                                                                                                  | Real application formats, visible selection, focus, app reuse, and macOS bundle handoff remain manual. GIO allocation/launch failures and malformed LaunchServices objects need additional native-boundary fault injection.                                      |
| Windows Shell reveal          | Portable ownership tests cover COM/PIDL cleanup and malformed/error outcomes. Windows-only tests exercise the production STA worker, task states, cleanup before completion, concurrent independent workers, and real COM/PIDL preparation for disposable files/directories. A native PIDL is converted back to its requested path; cleanup is checked after success, parse failure, cancellation, and controlled selection failure.                                                               | Selection is replaced at the desktop boundary. The real `SHOpenFolderAndSelectItems` call and visible Explorer behavior remain manual; follow the desktop checks below.                                                                                          |
| Linux portal/reveal           | Private native D-Bus fixtures exercise early/late signals, malformed method replies/paths and color components, owner loss, errors/cancellation, slot recovery, and caller cancellation while awaiting a submitted reveal. Except for intentional disconnects, fixtures wait for the client's bus name to disappear, checking connection cleanup.                                                                                                                                                  | A real portal, compositor, file manager, and privacy controls still require manual validation. Native allocation failures are not injected.                                                                                                                      |
| Windows/macOS screen samplers | Construction and dispatcher-access contracts; Linux's portable gate test independently covers overlap, error recovery, and user cancellation.                                                                                                                                                                                                                                                                                                                                                      | AppKit callback/block retirement and GDI/window cleanup, allocation failures, display changes, and picker cancellation require further focused fault-injection coverage plus the manual procedure below. Linux gate tests do not validate these implementations. |

Windows preparation tests call the same native adapter as production for COM initialization, PIDL
parsing/freeing, and COM shutdown. They record native apartment state before/after the operation,
assert STA execution and same-thread cleanup, and join their owned workers. Gates control races
without sleeps or message pumping. COM state comparison checks the observable apartment state; it
does not measure native reference counts or prove all memory is leak-free. The selection callback is
controlled for these tests and never forwards to Explorer. Keep that native selection line visible
as uncovered until there is separate desktop evidence; do not exclude the adapter to remove a patch
warning.

These gaps are consequences to investigate, not coverage targets. Keep profile-specific tests with
their feature; new capabilities should use these same standalone runner, boundary, ownership, and
evidence conventions.

## Published-consumer evidence

`task check` and `task packages:check` publish the isolated package consumer as self-contained,
bundled single-file, and fully trimmed applications. `task packages:aot` adds Native AOT; CI runs
all four modes on each native host. Each published executable runs directly in a fresh working
directory, with owned single-file extraction and cleanup. Self-contained runtime configuration,
single-file output shape, resolved candidate package versions, and absence of sibling providers are
checked. The AOT consumer also requires dynamic code support to be disabled.

SDK runtime, linker, and AOT compiler packs restore from NuGet.org into an isolated cache. Exact
source mappings restrict both Bathos packages to the freshly packed candidate-only feed. Each mode
starts with clean build intermediates; no repository project references or installed Bathos packages
can satisfy the consumer. Trimming and AOT root both libraries and fail on analysis warnings without
adding compatibility metadata or suppressions.

The executable asserts exclusive publication, file inspection, retained-reader snapshot replacement,
lease contention and release/reacquisition. On macOS it additionally exercises real ColorSync
callbacks and reads an installed profile. Runtime picker callbacks and desktop acceptance remain
manual; retaining and analyzing those methods is not evidence of their execution. See the
[deployment modes and limitations](platform-support.md#consumer-publishing).

## Released API evidence

Packing compares each shipping package with the stable version pinned in `eng/Bathos.Package.props`,
including parameter names used by named-argument callers. Package verification additionally builds a
locked released consumer and provider implementation, rebuilds the same source against the
candidate, and executes the original assembly with the candidate libraries. Runtime hash checks
establish that all four loaded library assemblies came from the candidate packages and that the
consumer was not rebuilt. Added optional capabilities are checked against the old provider. This
runs in the same Windows/macOS/Linux matrix as native validation. See CONTRIBUTING.md for baseline
maintenance. API compatibility and representative consumer execution do not establish every
behavioral or native guarantee.

## macOS and Windows screen sampler

From a logged-in graphical macOS or Windows session, run:

```text
task eng:sampler
```

Without Task:

```text
dotnet run --project eng/Bathos.Platform.Sampler --configuration Release
```

The harness opens a native window with instructions, four test buttons, a red target, a last-sample
color preview, and persistent results. It does not start sampling automatically. Move the window
somewhere convenient before choosing a test.

1. **Sample the red target:** click the center of the red swatch. The harness checks for
   approximately `#FF0000` (each channel within 5 of the expected value); otherwise it shows the
   observed color and asks you to check/retry.
2. **Sample outside this window:** select another location or display. Compare the preview swatch
   and displayed RGB value with what you selected; the harness cannot validate the unknown external
   color automatically.
3. **Test Escape cancellation:** press Escape after starting the picker. The harness reports a pass
   for cancellation or asks you to retry if a color was selected instead.
4. **Test caller retirement:** the caller retires when the picker opens. Select or cancel; the
   harness ignores the late result and reports native completion.

The preview shows the last successfully selected color alongside its RGB hex value. Cancellation,
errors, and retired-caller results leave that preview unchanged; the per-test result records what
happened.

Tests are independent: select the second test directly or run them in any order, and repeat any test
after completion. Buttons, including **Finish**, are disabled while a picker owns an outstanding
operation. After completion or an operation exception, controls are enabled again and results remain
visible. Choose **Finish** to close the harness. Check that a new sample works after both selection
and cancellation; a stuck operation is a failure to record, not a reason to force an overlapping
sample.

Color values are for ordinary selection and need not match source content or calibrated display
appearance. Respect system privacy controls. When reporting a problem, include the OS, architecture,
display setup, and steps to reproduce it. Escape cancels a sample; stopping the process forcibly
does not establish clean shutdown.

Interactive checks must be performed by a person. Computer-use automation does not currently
interact reliably with the system picker; automated pointer/key attempts are not evidence of picker
correctness or defects. Do not use that path unless explicitly investigating automation
compatibility.

`task eng:sampler:smoke` briefly opens the host window but does not invoke the picker. It checks
dispatch and shutdown without human input. It is intentionally separate from `task check`, which
also runs in noninteractive environments. A mocked picker cannot validate AppKit selection, display
access, or the Objective-C completion-block lifetime.

On Windows, selection displays a frozen snapshot taken when each test starts. Verify a second
display and mixed scaling where available, right-click and focus-loss cancellation, and that
selecting a pixel does not click through to the application underneath. A display-configuration
change during selection should report an operation error. There is no magnifier or live desktop
update. The native harness uses a normal Win32 message loop and deferred window messages for
dispatch; it never synthesizes selection input.

The development-only harness has no extra package dependencies. On macOS it uses AppKit's
application event loop and `dispatch_async_f` for deferred main-thread work. Shutdown posts an
application-defined event after `stop:` so AppKit can leave its event wait; it does not poll or run
a nested dispatcher loop. See Apple's
[main-queue documentation](https://developer.apple.com/documentation/dispatch/dispatch_get_main_queue)
and
[application shutdown documentation](<https://developer.apple.com/documentation/appkit/nsapplication/stop(_:)>).

## Experimental Linux sampler

From a graphical desktop session with a supporting Screenshot portal backend, run `task eng:sampler`
in a terminal. The console harness accepts `s` to sample, `c` to test Escape cancellation, `r` to
retire a caller, and `q` to finish. Each operation waits for portal completion before accepting
another command. Compare selected RGB values with known targets inside and outside the application,
repeat after both selection and cancellation, and try additional displays where available. Include
the distribution, desktop/compositor, portal backend, and display setup when reporting a problem.

Linux has no harness-owned native dispatcher or dispatcher smoke mode. Its unattended tests start an
isolated session bus and fake portal to exercise native libdbus messages, request races, malformed
responses, cancellation, unrelated signals, backend errors, and connection lifetimes. They never
invoke a real desktop portal.

## Desktop reveal and application handoff

Use the engineering console host:

```text
dotnet run --project eng/Bathos.Platform.Desktop -- reveal <absolute-file-or-directory>
dotnet run --project eng/Bathos.Platform.Desktop -- find <native-identifier>
dotnet run --project eng/Bathos.Platform.Desktop -- open <absolute-file> <executable|bundle|desktop-entry> <target>
```

Replace placeholders with host-selected values, quoting paths in the terminal as needed. Discovery
keys are an executable filename on Windows, a bundle identifier on macOS, or a desktop-entry ID on
Linux. Opening always requires an explicit target; lookup results are never selected automatically.

1. Reveal both a file and a directory. Confirm the requested entry is selected in its parent,
   including names with spaces, Unicode, `#`, and `%`. Merely opening the containing directory is
   insufficient.
2. Open a supported image/document in the host-selected editor on macOS/Windows. Test both when the
   editor is closed and when it is already running. Confirm the expected document appears in the
   selected application. Test each selected executable/bundle/version separately.
3. Check a missing application, missing file, and an unsupported target kind. Failures must not open
   a default application or change file associations. The caller's file must remain intact.
4. On a Linux desktop, repeat reveal and explicit desktop-entry invocation with an available file
   manager/editor. Include the desktop/compositor and registration ID when reporting a problem.

Automated tests use an owned executable and a private D-Bus file-manager fixture. They check
argument preservation, handoff completion without waiting for application exit, cancellation before
handoff, native registration lookup, encoded file URIs, and native errors. Linux GIO activation runs
with a private child-process XDG data directory. These tests do not drive Explorer/Finder or a real
editor and do not establish desktop focus or document readiness.

## Installed profile discovery

The automated profile tests exercise native enumeration and bounded readback on the executing
macOS/Windows host. They also verify incomplete scans under count limits, byte limits, cancellation
before native entry, capability absence, and Windows enumeration-buffer parsing. Linux explicitly
skips native profile discovery. These checks do not prove ICC validity, current display association
accuracy, HDR behavior, or another OS's native implementation.

Profile read tests also verify recovery after missing/oversized reads and exact macOS byte-limit
behavior. Native allocation failures, cancellation during a running native call, and exhaustive
ColorSync/WCS handle/callback fault injection remain unverified; successful inventory/readback does
not establish those failure paths.

## Lexical paths and portability

Path tests exercise the executing runtime's native parser without filesystem discovery: absolute
paths, dot segments, root/directory references, escaped punctuation and Unicode, significant
whitespace, foreign references, and malformed URI boundaries. Conversion rejects unpaired surrogates
and invalid escaped UTF-8 without replacing them or reinterpreting them as literal-percent
filenames; valid supplementary characters and escaped-percent names round-trip unchanged. Windows
matrix execution additionally covers drive/UNC roots, dot-segment clamping, preserved trailing
dots/spaces, URI round-trips, and device-namespace rejection; POSIX execution covers literal
backslashes and ambiguous-root rejection. Portable filename policy tests run on every host,
including case-insensitive console-device rejection and the distinction between bare names and names
with extensions. These tests establish lexical behavior only, not alias equivalence, network
coordination, or lease exclusion.

## Directory discovery

Deterministic tests supply folder/environment values without changing the process environment. They
cover XDG defaults, invalid relative overrides, unavailable home and roots, application identity
validation, explicit overrides, and Windows/macOS/Linux layout scoping. Native tests read the
current host's roots without writing into user directories; foreign-provider invocation is rejected.
Paths and successful lookup do not establish filesystem access or ownership. An owned macOS child
runs with an uncreated home override and compares cache discovery with the independent native cache
lookup. The parent environment and real user directories are unchanged. Packaged sandbox behavior
remains a separate host validation case. Portable account-boundary tests cover absent and explicit
root homes, buffer growth and limits, native errors, invalid pointers, missing terminators,
malformed UTF-8, and recovery. Owned macOS/Linux children remove `HOME` only from their own
environment and compare discovery with an independent runtime account lookup. These checks do not
require changing the parent environment, user identity, permissions, or real user directory
contents.

## File leases

The native lease tests use disposable roots and owned child processes, with redirected pipe messages
for acquisition and completion. Shared owners coexist within and across processes, and exclusive
owners exclude both modes. Releasing one shared owner preserves another owner's claim. Tests cover
disposal and forced process exit in both modes, stale artifact bytes, pre-cancellation, missing
parents, invalid paths, links/special files, noninherited handles, creation permissions, significant
names, and retained exclusion at a stable lock location outside a moved application-data tree.
Default-mode acquisition is checked against both shared and exclusive contenders. Watchdogs only
bound hangs; no timing-based synchronization is used. Foreign providers reject invocation in both
modes. Run on all three hosts: local success does not establish another OS's locking semantics.
Windows link tests require developer mode or symlink privileges and explicitly skip otherwise.

The guarantee requires stable artifacts and a common protocol on supported storage. Moving the
artifact or any containing directory while leased is outside the contract on every platform.
Noncooperating replacement, network-client coordination, and filesystem alias discovery are
intentionally not guaranteed by these tests. Unix tests reject malformed Unicode without creating or
retargeting an artifact. An owned child holds a private pseudo-terminal while a separate
session-leader child verifies that rejecting its slave does not assign a controlling terminal.
Cancellation at an internal gate after a real native lock verifies contention before cancellation,
then reacquisition and unchanged bytes after rollback in both modes. A shared-mode case retains
another shared owner, verifies its claim survives rollback, then releases it and proves the canceled
acquisition did not leak a lock. In both modes, a separate owner is killed while its descendant
remains alive, without explicit unlock; reacquisition verifies that ownership did not survive in the
descendant. Unix launches that descendant through plain `posix_spawn`, so runtime descriptor cleanup
cannot mask a missing close-on-exec flag. Windows uses the runtime launcher. Windows can finish
implicit unlock after signaling process exit. Abrupt-exit tests await a cancellable native lock
completion before asserting reacquisition, without sleeping or polling. A retained lock still fails
the bounded wait.
