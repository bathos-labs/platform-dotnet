# Native validation

Evidence applies to the named host and scenarios. A build or skipped test is not native validation of another OS.

| Area | Evidence / status |
| --- | --- |
| macOS Arm64, macOS 26.6.2 | `task check`: 30 tests passed, formatting/build/package checks passed; README publication example ran twice and preserved both outputs. |
| macOS harness, same host | `task eng:sampler:smoke` passed: dispatch is deferred, main-thread access is enforced, worker dispatch completes, and the native application exits after owned work. |
| macOS interactive sampler | User confirmed the macOS sampler tests and color preview on 2026-10-22. Exact OS/display details were not supplied with that confirmation. |
| Windows x64, build 26200 | `task check` passed: 31 tests passed, 12 platform-specific tests skipped; formatting/build/package checks and isolated Windows package consumption passed. |
| Windows harness, same host | `task eng:sampler:smoke` passed: deferred UI dispatch, worker rejection, sample preview, and owned shutdown. See the interactive confirmation below. |
| Windows interactive sampler | User confirmed all Windows sampler harness tests passed on 2026-10-23 and signed off on the sampler. Exact OS/display details were not supplied with that confirmation. |
| Ubuntu 22.04 x64 under WSL2 | Release build, tests (32 passed, 11 unsupported publication scenarios skipped), packaging, and isolated Linux package consumption passed. All 11 private-bus portal protocol cases passed. This is not Linux desktop validation. |
| Linux desktop | Experimental. No interactive desktop evidence is available. Sampling requires a supporting portal backend and system libdbus. Rename/publication remain unavailable. |
| Desktop API, Windows x64 build 26200, 2026-10-23 | Release build and tests passed (35 passed, 16 platform-specific skips), including owned executable handoff and isolated App Paths registration lookup. Visible Explorer/editor behavior is pending manual validation. |
| Desktop API, Ubuntu 22.04 x64 WSL2, 2026-10-23 | Release build, tests (40 passed, 11 unsupported publication skips), packaging, and isolated Linux package consumption passed. Owned GIO desktop-entry activation and all three private file-manager protocol cases passed; no desktop acceptance is implied. |
| Desktop API, macOS CI (`macos-15`) | [Native validation for `09eccf8`](https://github.com/bathos-labs/platform-dotnet/actions/runs/39794923968) passed: 36 tests passed, 15 platform-specific skips; native LaunchServices lookup, owned executable handoff, formatting/build/package checks, and isolated macOS package consumption passed. Interactive Finder reveal and bundle handoff remain unverified. |

Undated automated sampler/file evidence above was collected on 2026-10-22; later desktop API checks and interactive confirmations carry their own dates.
The three-OS CI run for `09eccf8` was inspected during beta preparation on 2026-10-23; all jobs passed.
Permission-denied, cross-volume, special-file, and additional filesystem scenarios still need separate evidence.

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

Record OS version, architecture, displays, observed values, cancellation, retirement, and any privacy prompts or
failures in this file. Color values are for ordinary selection and need not match source content or calibrated display appearance. Do not bypass
system privacy controls. If a step fails, record the failure rather than marking it passed. Escape cancels a sample;
stopping the process forcibly does not establish clean shutdown.

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
displays where available. Record the distribution, desktop/compositor, portal backend, display setup, and results.
No Linux interactive success is claimed until this procedure has been performed by a person.

Linux has no harness-owned native dispatcher or dispatcher smoke mode. Its unattended tests start an isolated
session bus and fake portal to exercise native libdbus messages, request races, malformed responses, cancellation,
unrelated signals, backend errors, and connection lifetimes. They never invoke a real desktop portal.

## Desktop reveal and application handoff

These new capabilities have no interactive acceptance record yet. Use the engineering console host:

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
2. Open a supported image/document in the host-selected Photoshop or Clip Studio Paint application on macOS/Windows.
   Test both when the editor is closed and when it is already running. Confirm the expected document appears in the
   selected application. Test each selected executable/bundle/version separately; a launcher hub is a different target.
3. Check a missing application, missing file, and an unsupported target kind. Failures must not open a default application
   or change file associations. The caller's file must remain intact.
4. On a Linux desktop, repeat reveal and explicit desktop-entry invocation with an available file manager/editor.
   Record distribution, desktop/compositor, registration ID, and native-library versions. Linux stays experimental.

Automated tests use an owned executable and a private D-Bus file-manager fixture. They check argument preservation,
handoff completion without waiting for application exit, cancellation before handoff, native registration lookup,
encoded file URIs, and native errors. Linux GIO activation runs with a private child-process XDG data directory.
These tests do not drive Explorer/Finder or a real editor and do not establish desktop focus or document readiness.
