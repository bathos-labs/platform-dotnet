# Manual native checks

Linux support is experimental. CI runs automated native tests on Windows, macOS, and Linux; interactive desktop
behavior can be checked using the engineering harnesses below. The harnesses are not shipped in NuGet packages.

## Automated evidence boundaries

All tests build and run from this repository alone with `task test` or `task test:coverage`. Every test is labeled
`Validation=Portable` or `Validation=Native`. After building, use the existing runner to select one evidence tier or
feature independently, for example:

```text
dotnet test --solution Bathos.Platform.slnx --configuration Release --no-build --filter-trait "Validation=Portable"
dotnet test --solution Bathos.Platform.slnx --configuration Release --no-build --filter-trait "Validation=Native"
dotnet test --solution Bathos.Platform.slnx --configuration Release --no-build --filter-class "*Publication*"
```

The full CI suite runs both tiers. Platform-inapplicable native cases still report explicit skips; manual checks are
procedures, not disabled tests counted as automated evidence.

| Evidence | Runs where | What it establishes |
| --- | --- | --- |
| Portable contracts | Every supported host | Capability selection/absence, input validation, filename rules, and controlled ownership/concurrency outcomes. Test doubles establish orchestration, not native API behavior. |
| Native filesystem and executable handoff | Executing OS only | Real disposable files, native rename/inspection, registration lookup, and an owned argument/exit probe. Linux has no rename capability. |
| Private D-Bus and GIO integration | Linux only | Real native transport and activation against isolated fixtures, without desktop services. |
| Foreign-platform scenarios | Explicitly skipped | No evidence for the skipped OS; retain TRX reasons and review all matrix legs. |
| Desktop and permissions | Manual only | Picker interaction, visible selection, real editor behavior, privacy controls, and multi-display behavior described below. |

Coverage reports include handwritten native code even when it cannot run on the current host. Aggregate coverage is
useful for locating gaps, but must be read with per-platform flags, successful matrix jobs, skipped-test reasons, and
behavioral assertions. A macOS run cannot accept Windows/Linux behavior. Cross-volume, permission denial, and special
filesystem behavior require evidence on the relevant filesystem; an unattended substitute does not establish them.

## Contract audit and remaining gaps

| Capability | Unattended assertions | Remaining native/desktop evidence |
| --- | --- | --- |
| Provider composition | Minimal implementers inherit null capabilities; selection rejects absence and ambiguity; construction does no native work. | Service/permission availability cannot be inferred from a nonnull capability. |
| Publication | Real concurrent publishers, collision gaps, explicit replacement, staging-path reuse; controlled native failures, flush failure, cancellation between attempts and after success, single-use ownership and recovery with a new operation. | Real cross-volume rejection, denied access, and unusual/network filesystems need host/filesystem-specific checks. The portable rename double does not establish atomicity. |
| Entry inspection/rename | Real files, links, FIFOs/sockets, retained bytes on failed rename, successful subsequent operations, and Windows handle-sharing cleanup. Special-entry probes run in owned child processes with a deadline, so accidentally opening a FIFO cannot strand the runner. | Windows symlink fixtures explicitly skip when privilege is unavailable; run them with privilege or record a manual check. Junctions/devices and adversarial ancestor replacement remain separate cases. |
| Desktop handoff/discovery | Owned executable argument/exit probe, cancellation before/after submission, native invalid-executable errors; actual registration lookup and malformed Windows App Paths records. | Real application formats, visible selection, focus, app reuse, and macOS bundle handoff remain manual. GIO allocation/launch failures and malformed LaunchServices objects need additional native-boundary fault injection. |
| Windows Shell reveal | Portable ownership tests cover COM/PIDL cleanup and malformed/error outcomes. Windows-only tests exercise the production STA worker, task states, cleanup before completion, concurrent independent workers, and real COM/PIDL preparation for disposable files/directories. A native PIDL is converted back to its requested path; cleanup is checked after success, parse failure, cancellation, and controlled selection failure. | Selection is replaced at the desktop boundary. The real `SHOpenFolderAndSelectItems` call and visible Explorer behavior remain manual; follow the desktop checks below. |
| Linux portal/reveal | Private native D-Bus fixtures exercise early/late signals, malformed method replies/paths and color components, owner loss, errors/cancellation, slot recovery, and caller cancellation while awaiting a submitted reveal. Except for intentional disconnects, fixtures wait for the client's bus name to disappear, checking connection cleanup. | A real portal, compositor, file manager, and privacy controls still require manual validation. Native allocation failures are not injected. |
| Windows/macOS screen samplers | Construction and dispatcher-access contracts; Linux's portable gate test independently covers overlap, error recovery, and user cancellation. | AppKit callback/block retirement and GDI/window cleanup, allocation failures, display changes, and picker cancellation require further focused fault-injection coverage plus the manual procedure below. Linux gate tests do not validate these implementations. |

Windows preparation tests call the same native adapter as production for COM initialization, PIDL parsing/freeing, and
COM shutdown. They record native apartment state before/after the operation, assert STA execution and same-thread
cleanup, and join their owned workers. Gates control races without sleeps or message pumping. COM state comparison
checks the observable apartment state; it does not measure native reference counts or prove all memory is leak-free.
The selection callback is controlled for these tests and never forwards to Explorer. Keep that native selection line
visible as uncovered until there is separate desktop evidence; do not exclude the adapter to remove a patch warning.

These gaps are consequences to investigate, not coverage targets. Keep profile-specific tests with their feature;
new capabilities should use these same standalone runner, boundary, ownership, and evidence conventions.

## macOS and Windows screen sampler

From a logged-in graphical macOS or Windows session, run:

```text
task eng:sampler
```

Without Task:

```text
dotnet run --project eng/Bathos.Platform.Sampler --configuration Release
```

The harness opens a native window with instructions, four test buttons, a red target, a last-sample color preview, and persistent results.
It does not start sampling automatically. Move the window somewhere convenient before choosing a test.

1. **Sample the red target:** click the center of the red swatch. The harness checks for approximately `#FF0000`
   (each channel within 5 of the expected value); otherwise it shows the observed color and asks you to check/retry.
2. **Sample outside this window:** select another location or display. Compare the preview swatch and displayed RGB value with what
   you selected; the harness cannot validate the unknown external color automatically.
3. **Test Escape cancellation:** press Escape after starting the picker. The harness reports a pass for cancellation
   or asks you to retry if a color was selected instead.
4. **Test caller retirement:** the caller retires when the picker opens. Select or cancel; the harness ignores the
   late result and reports native completion.

The preview shows the last successfully selected color alongside its RGB hex value. Cancellation,
errors, and retired-caller results leave that preview unchanged; the per-test result records what happened.

Tests are independent: select the second test directly or run them in any order, and repeat any test after completion.
Buttons, including **Finish**, are disabled while a picker owns an outstanding operation. After completion or an
operation exception, controls are enabled again and results remain visible. Choose **Finish** to close the harness.
Check that a new sample works after both selection and cancellation; a stuck operation is a failure to record,
not a reason to force an overlapping sample.

Color values are for ordinary selection and need not match source content or calibrated display appearance.
Respect system privacy controls. When reporting a problem, include the OS, architecture, display setup, and steps
to reproduce it. Escape cancels a sample; stopping the process forcibly does not establish clean shutdown.

Interactive checks must be performed by a person. Computer-use automation does not currently interact reliably
with the system picker; automated pointer/key attempts are not evidence of picker correctness or defects. Do not
use that path unless explicitly investigating automation compatibility.

`task eng:sampler:smoke` briefly opens the host window but does not invoke the picker. It checks dispatch and shutdown
without human input. It is intentionally separate from `task check`, which also runs in noninteractive environments.
A mocked picker cannot validate AppKit selection, display access, or the Objective-C completion-block lifetime.

On Windows, selection displays a frozen snapshot taken when each test starts. Verify a second display and mixed
scaling where available, right-click and focus-loss cancellation, and that selecting a pixel does not click through
to the application underneath. A display-configuration change during selection should report an operation error.
There is no magnifier or live desktop update. The native harness uses a normal Win32 message loop and deferred
window messages for dispatch; it never synthesizes selection input.

The development-only harness has no extra package dependencies. On macOS it uses AppKit's application event loop and
`dispatch_async_f` for deferred main-thread work. Shutdown posts an application-defined event after `stop:` so
AppKit can leave its event wait; it does not poll or run a nested dispatcher loop. See Apple's
[main-queue documentation](https://developer.apple.com/documentation/dispatch/dispatch_get_main_queue) and
[application shutdown documentation](https://developer.apple.com/documentation/appkit/nsapplication/stop(_:)).

## Experimental Linux sampler

From a graphical desktop session with a supporting Screenshot portal backend, run `task eng:sampler` in a terminal.
The console harness accepts `s` to sample, `c` to test Escape cancellation, `r` to retire a caller, and `q` to finish.
Each operation waits for portal completion before accepting another command. Compare selected RGB values with
known targets inside and outside the application, repeat after both selection and cancellation, and try additional
displays where available. Include the distribution, desktop/compositor, portal backend, and display setup when reporting a problem.

Linux has no harness-owned native dispatcher or dispatcher smoke mode. Its unattended tests start an isolated
session bus and fake portal to exercise native libdbus messages, request races, malformed responses, cancellation,
unrelated signals, backend errors, and connection lifetimes. They never invoke a real desktop portal.

## Desktop reveal and application handoff

Use the engineering console host:

```text
dotnet run --project eng/Bathos.Platform.Desktop -- reveal <absolute-file-or-directory>
dotnet run --project eng/Bathos.Platform.Desktop -- find <native-identifier>
dotnet run --project eng/Bathos.Platform.Desktop -- open <absolute-file> <executable|bundle|desktop-entry> <target>
```

Replace placeholders with host-selected values, quoting paths in the terminal as needed. Discovery keys are an
executable filename on Windows, a bundle identifier on macOS, or a desktop-entry ID on Linux. Opening always
requires an explicit target; lookup results are never selected automatically.

1. Reveal both a file and a directory. Confirm the requested entry is selected in its parent, including names with
   spaces, Unicode, `#`, and `%`. Merely opening the containing directory is insufficient.
2. Open a supported image/document in the host-selected editor on macOS/Windows.
   Test both when the editor is closed and when it is already running. Confirm the expected document appears in the
   selected application. Test each selected executable/bundle/version separately.
3. Check a missing application, missing file, and an unsupported target kind. Failures must not open a default application
   or change file associations. The caller's file must remain intact.
4. On a Linux desktop, repeat reveal and explicit desktop-entry invocation with an available file manager/editor.
   Include the desktop/compositor and registration ID when reporting a problem.

Automated tests use an owned executable and a private D-Bus file-manager fixture. They check argument preservation,
handoff completion without waiting for application exit, cancellation before handoff, native registration lookup,
encoded file URIs, and native errors. Linux GIO activation runs with a private child-process XDG data directory.
These tests do not drive Explorer/Finder or a real editor and do not establish desktop focus or document readiness.
