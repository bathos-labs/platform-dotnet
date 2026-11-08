# Windows association discovery investigation

This engineering-only probe supports
[issue #36](https://github.com/bathos-labs/platform-dotnet/issues/36) and the
[agreed discovery contract](../../docs/file-application-discovery.md). It is outside shipping
packages and the solution. Use the repository-pinned SDK. JSON can contain local installation paths;
sanitize findings before posting or committing them.

## Read-only enumeration

```text
dotnet run --project eng/Bathos.Platform.AssociationProbe --configuration Release -- C:\absolute\existing.png
```

This mode launches nothing and changes no registrations. It enumerates the extension twice on an
owned STA worker, collecting native names, labels, icon locations/indices, recommendation HRESULTs,
and ProgID/AppUserModelID query and getter results. Two snapshots do not prove durable identity.
Recommendation is not default selection. Preserve nonzero HRESULTs in evidence; successful interface
queries do not guarantee usable identifier values. This probe currently rejects extensionless files.

## Owned activation fixtures

Build `eng/Bathos.Platform.TestApplication` in Release, then supply the absolute path to its built
executable:

```text
dotnet run --project eng/Bathos.Platform.AssociationProbe --configuration Release -- --fixture C:\absolute\Bathos.Platform.TestApplication.exe
dotnet run --project eng/Bathos.Platform.AssociationProbe --configuration Release -- --applications-fixture C:\absolute\Bathos.Platform.TestApplication.exe
```

Use only the owned receiver. These modes create unique per-user registrations and disposable files,
activate the receiver through IAssocHandler with a Shell-created IDataObject, and verify marker and
space/Unicode document arguments through a pipe acknowledgment. They change no defaults or existing
registrations. Fixture-owned keys and files are removed in finally. Synchronization uses owned
operations, not sleeps, polling, or nested dispatcher loops.

The ProgID fixture checks changed commands, competing registrations for one executable, stale-ProgID
rejection, removal, and the failure of executable-plus-file invocation to preserve required
arguments. The Applications fixture checks native activation without usable ProgID/AppUserModelID,
command changes, retained-handler behavior, and removal. Its retained-handler experiment changes the
command after enumeration and reports which document argument arrives. The strict ProgID matcher is
an investigation aid, not the agreed application-level coalescing implementation.

## Findings and remaining work

On Windows build 26200 x64, both fixture modes passed. The Shell coalesced competing ProgIDs for one
executable; the exposed ProgID distinguished the chosen registration. Applications handlers returned
E_FAIL from identity getters but invoked correctly. A retained handler used a command changed after
enumeration. Packaged PNG handlers exposed AppX identities and indirect ms-resource icon locations.
Unknown extensions also returned general-purpose handlers. These are host-specific observations.

The identity decision is now agreed: transient application choices, current native registration
semantics, inert descriptors, and support for handlers without ProgIDs. Coalesce by native
application identity or full executable path, never display label alone. Preserve native winners;
for additional Windows duplicates prefer recommendation, then stronger identity, then enumeration
order. See the contract for limitations and failure behavior.

Remaining work includes competing Applications registrations, final coalescing and re-resolution,
packaged icon rendering to owned RGBA, extensionless files, native errors, cancellation, cleanup,
and end-to-end provider composition. Do not assume icon locations are ordinary executable paths or
launch personal applications automatically. Manual activation requires an explicitly selected
disposable file.

Build with analyzers and run `task check` after changes. Windows execution, full CI, and Native AOT
evidence must be reported separately. Local Windows `task check` encountered ERR_INVALID_URL_SCHEME
in the remark configuration; successful builds and fixtures do not replace that gate. Do not publish
or tag packages. Branch: `feature/file-application-discovery`, based on `release/1.2`.
