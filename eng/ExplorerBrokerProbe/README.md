# Desktop Explorer launch probe

Disposable Windows x64 engineering experiment for
[Makura #272](https://github.com/makura-studio/desktop/issues/272). This is not a Platform API,
production launcher, or admission-policy implementation. It does not change Makura startup or
Project write leases.

## Reproduce

Use an interactive Windows desktop with an existing Explorer shell, matching medium-integrity
user/session/logon context, and the repository-pinned .NET SDK. Run only `bootstrap`; the other
modes are internal harness protocols. The native bootstrap requests Windows-authorized Job breakaway
to create an isolated test coordinator. It fails if Windows denies breakaway or the coordinator
remains in a Job. It never asks Explorer to escape the coordinator's ambient restrictions.

```powershell
dotnet restore eng/ExplorerBrokerProbe/ExplorerBrokerProbe.csproj --locked-mode
dotnet build eng/ExplorerBrokerProbe/ExplorerBrokerProbe.csproj --configuration Release --no-restore
$stage = Join-Path $env:TEMP ('bathos explorer ' + [char]0x65E5 + [char]0x672C + [char]0x8A9E + ' ' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage | Out-Null
Copy-Item eng/ExplorerBrokerProbe/bin/Release/net10.0-windows/* -Destination $stage
& (Join-Path $stage 'ExplorerBrokerProbe.exe') bootstrap (Join-Path $stage 'evidence.txt')
if ($LASTEXITCODE -ne 0) { throw 'Probe failed; inspect evidence.txt' }
Get-CimInstance Win32_Process | Where-Object {
    $_.ExecutablePath -and $_.ExecutablePath.StartsWith($stage, [StringComparison]::OrdinalIgnoreCase)
} | Select-Object ProcessId, ParentProcessId, ExecutablePath
```

The final inventory must be empty. Keep evidence and staged binaries outside the repository. Do not
terminate Explorer, kill by process name, or remove lock files.

## Mechanism and observations

The probe follows Microsoft's
[Execute In Explorer sample](https://learn.microsoft.com/en-us/windows/win32/shell/samples-execinexplorer):
local-server `CLSID_ShellWindows`, `FindWindowSW(SWC_DESKTOP, SWFO_NEEDDISPATCH)`,
`IServiceProvider.QueryService(SID_STopLevelBrowser)`, `IShellBrowser.QueryActiveShellView`,
`IShellView.GetItemObject(SVGIO_BACKGROUND)`, then `IShellFolderViewDual.Application` and
`IShellDispatch2.ShellExecute`. It does not instantiate a generic `Shell.Application` and assume
ownership.

The selected desktop window must match `GetShellWindow`'s process. Caller and broker must match user
SID, authentication LUID, session, and elevation type. Elevated, restricted, AppContainer,
non-medium-integrity and impersonating callers are rejected. These checks are snapshots, not an
atomic security/admission proof. The disposable target has no elevation request. Other application
manifests and compatibility settings are outside this experiment.

Submission uses an exact absolute executable, explicit working directory, `open` verb and a quoted
argument string. The target verifies empty arguments, spaces, quotes, trailing backslashes, and
Unicode using .NET/CRT argument parsing; this is not a guarantee for applications with another
parser. A random current-user pipe, OS-reported client PID, executable path, context comparison and
explicit readiness message identify the target. Submission alone is never readiness.

The harness covers orderly caller EOF, caller-rooted process-tree termination, explicit-breakaway
and silent-breakaway kill-on-close Jobs, and known nested Jobs. After caller retirement it awaits
stdout/stderr EOF, exchanges ping/pong with the target, requests target exit and joins it.
Restrictive Jobs are rejected before broker invocation. The coordinator constructs the complete Job
chain from outside any Job, so its knowledge is specific to this controlled experiment.

Missing Explorer and pre-invocation failure are injected without disturbing the desktop. A lost
reply is simulated after real submission; the target is captured and joined without retry.
Discovery/admission failure before invocation is definite non-submission. Once invocation begins, an
unclassified COM failure or missing readiness must be treated as ambiguous, with no fallback or
automatic retry. This does not simulate an actual Explorer crash or RPC failure.

All synchronization uses gates, pipe messages or process completion; deadlines only bound hangs.
Targets have their own watchdog and exit on control-channel EOF. The parent-process query is
diagnostic only. Native layouts are x64-specific and deliberately confined to this disposable
engineering project.

## Admission boundary

Do not reuse the caller's immediate-Job check as a production admission rule.
[QueryInformationJobObject](https://learn.microsoft.com/en-us/windows/win32/api/jobapi2/nf-jobapi2-queryinformationjobobject)
with a null handle reports only the immediate Job. A permissive inner Job can hide a restrictive
ancestor, as the nested-restrictive scenario demonstrates.
[Nested Job breakaway rules](https://learn.microsoft.com/en-us/windows/win32/procthread/nested-jobs)
apply across the hierarchy. Explorer submission does not enforce those caller rules on the caller's
behalf. The harness rejects that case using its known chain; it does not discover a complete
arbitrary caller hierarchy.

The route establishes useful desktop behavior, but does not establish a simple, reliable policy
boundary for arbitrary API/MCP supervisors. Admission races, shell availability and ambiguous
submission remain separate costs. No additional launch mechanism, shipping abstraction, weaker
fallback or architecture decision is introduced by this probe. Retaining connect-only is the
conservative recommendation unless the supported caller boundary is deliberately narrowed and
enforceable.
