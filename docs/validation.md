# Native validation

Evidence applies to the named host and scenarios. A build or skipped test is not native validation of another OS.

| Area | Evidence / status |
| --- | --- |
| macOS Arm64, macOS 26.6.2 | `task check`: 30 tests passed, formatting/build/package checks passed; README publication example ran twice and preserved both outputs. |
| macOS harness, same host | `task sampler:smoke` passed: dispatch is deferred, main-thread access is enforced, worker dispatch completes, and the native application exits after owned work. |
| macOS interactive sampler | Pending the manual sequence below. The smoke check does not invoke the picker or establish color/cancellation behavior. |
| Windows | Local confirmation pending. Run `task check` and record OS/architecture and results here. The sampler is unavailable on Windows. |
| Linux | Temporarily experimental pending broader host/filesystem validation. CI exercises file inspection on Linux x64; no Linux desktop evidence has been recorded here. Rename/publication and sampling are unavailable. |

The local macOS evidence above was collected on 2026-10-22. Remote CI results were not inspected as part of this run.
Permission-denied, cross-volume, special-file, and additional filesystem scenarios still need separate evidence.

## macOS screen sampler

From a logged-in graphical macOS session, run:

```text
task sampler
```

Without Task:

```text
dotnet run --project eng/Bathos.Platform.Sampler --configuration Release
```

The harness opens a red AppKit window and prints each step in the terminal. It advances after native completion:

1. Select the red window. Confirm the returned opaque sRGB value is approximately `#FF0000`.
2. Select outside the harness window; try another display if available. Confirm a plausible color and normal picker cleanup.
3. Press Escape. Confirm `Canceled by user.` and that a new picker opens for the next step.
4. The caller retires immediately after opening the picker. Select or cancel. Confirm the late result is ignored,
   completion is observed, and the window/process exit without hanging or crashing.

Repeat the command to check reuse. Record OS version, architecture, displays, observed values, cancellation,
retirement, and any privacy prompts or failures in this file. Color values describe rendered screen appearance,
not source image values. Do not bypass system privacy controls. If a step fails, record the failure rather than
marking the sequence passed. Escape cancels a step; stopping the process forcibly does not establish clean shutdown.

`task sampler:smoke` briefly opens the host window but does not invoke the picker. It checks dispatch and shutdown
without human input. It is intentionally separate from `task check`, which also runs in noninteractive environments.
A mocked picker cannot validate AppKit selection, display access, or the Objective-C completion-block lifetime.

The development-only harness has no extra package dependencies. It uses AppKit's application event loop and
`dispatch_async_f` for deferred main-thread work. Shutdown posts an application-defined event after `stop:` so
AppKit can leave its event wait; it does not poll or run a nested dispatcher loop. See Apple's
[main-queue documentation](https://developer.apple.com/documentation/dispatch/dispatch_get_main_queue) and
[application shutdown documentation](https://developer.apple.com/documentation/appkit/nsapplication/stop(_:)).
