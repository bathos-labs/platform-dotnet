# Native validation

Evidence applies to the named host and scenarios. A build or skipped test is not native validation of another OS.

| Area | Evidence / status |
| --- | --- |
| macOS Arm64, macOS 26.6.2 | `task check`: 30 tests passed, formatting/build/package checks passed; README publication example ran twice and preserved both outputs. |
| macOS harness, same host | `task eng:sampler:smoke` passed: dispatch is deferred, main-thread access is enforced, worker dispatch completes, and the native application exits after owned work. |
| macOS interactive sampler | User confirmed the macOS sampler test on 2026-10-22. Exact OS/display details were not supplied with that confirmation. The new color preview still needs visual confirmation. |
| Windows | Local confirmation pending. Run `task check` and record OS/architecture and results here. The sampler is unavailable on Windows. |
| Linux | Temporarily experimental pending broader host/filesystem validation. CI exercises file inspection on Linux x64; no Linux desktop evidence has been recorded here. Rename/publication and sampling are unavailable. |

The local macOS evidence above was collected on 2026-10-22. Remote CI results were not inspected as part of this run.
Permission-denied, cross-volume, special-file, and additional filesystem scenarios still need separate evidence.

## macOS screen sampler

From a logged-in graphical macOS session, run:

```text
task eng:sampler
```

Without Task:

```text
dotnet run --project eng/Bathos.Platform.Sampler --configuration Release
```

The harness opens an AppKit window with instructions, four test buttons, a red target, a last-sample color preview, and persistent results.
It does not start sampling automatically. Move the window somewhere convenient before choosing a test.

1. **Sample the red target:** click the center of the red swatch. The harness checks for approximately `#FF0000`
   (each channel within 5 of the expected value); otherwise it shows the observed color and asks you to check/retry.
2. **Sample outside this window:** select another location or display. Compare the preview swatch and displayed sRGB value with what
   you selected; the harness cannot validate the unknown external color automatically.
3. **Test Escape cancellation:** press Escape after starting the picker. The harness reports a pass for cancellation
   or asks you to retry if a color was selected instead.
4. **Test caller retirement:** the caller retires when the picker opens. Select or cancel; the harness ignores the
   late result and reports native completion.

The preview shows the last successfully selected color alongside its hex value, using opaque sRGB. Cancellation,
errors, and retired-caller results leave that preview unchanged; the per-test result records what happened.

Tests are independent: select the second test directly or run them in any order, and repeat any test after completion.
Buttons, including **Finish**, are disabled while a picker owns an outstanding operation. After completion or an
operation exception, controls are enabled again and results remain visible. Choose **Finish** to close the harness.
Check that a new sample works after both selection and cancellation; a stuck operation is a failure to record,
not a reason to force an overlapping sample.

Record OS version, architecture, displays, observed values, cancellation, retirement, and any privacy prompts or
failures in this file. Color values describe rendered screen appearance, not source image values. Do not bypass
system privacy controls. If a step fails, record the failure rather than marking it passed. Escape cancels a sample;
stopping the process forcibly does not establish clean shutdown.

Interactive checks must be performed by a person. Computer-use automation does not currently interact reliably
with the system picker; automated pointer/key attempts are not evidence of picker correctness or defects. Do not
use that path unless explicitly investigating automation compatibility.

`task eng:sampler:smoke` briefly opens the host window but does not invoke the picker. It checks dispatch and shutdown
without human input. It is intentionally separate from `task check`, which also runs in noninteractive environments.
A mocked picker cannot validate AppKit selection, display access, or the Objective-C completion-block lifetime.

The development-only harness has no extra package dependencies. It uses AppKit's application event loop and
`dispatch_async_f` for deferred main-thread work. Shutdown posts an application-defined event after `stop:` so
AppKit can leave its event wait; it does not poll or run a nested dispatcher loop. See Apple's
[main-queue documentation](https://developer.apple.com/documentation/dispatch/dispatch_get_main_queue) and
[application shutdown documentation](https://developer.apple.com/documentation/appkit/nsapplication/stop(_:)).
