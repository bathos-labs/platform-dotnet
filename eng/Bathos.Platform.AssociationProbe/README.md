# Windows association discovery handoff

This engineering-only probe supports
[issue #36](https://github.com/bathos-labs/platform-dotnet/issues/36). It is deliberately outside
the shipping packages and solution. It enumerates all Shell handlers for an existing file's
extension twice, collecting native names, display names, icon locations/indices, recommendation
status, and HRESULTs. It launches nothing and changes no associations. JSON may contain local
installation paths: sanitize findings before posting or committing them.

```text
dotnet run --project eng/Bathos.Platform.AssociationProbe --configuration Release -- C:\absolute\existing.png
```

Use the repository-pinned SDK. Compilation on another host does not validate COM behavior. The two
snapshots are observations, not proof that native names are durable or unique. Recommendation is not
default selection. HRESULT zero means success; preserve other statuses when reporting results.

## Required Windows investigation

1. Run against PNG, plain text, a type with packaged handlers, and an existing file with an unknown
   extension. Record Windows build, architecture, candidate counts, HRESULTs, duplicate native
   names, icon forms, and differences across snapshots. Do not launch personal applications
   automatically.
2. Extend the owned test-application harness with a private per-user registration under a unique
   extension/ProgID. Its command must require a marker argument before the quoted document path. Use
   an owned process/pipe acknowledgment to verify native activation passes both arguments correctly.
   Do not alter defaults or existing registrations. Remove only fixture-owned keys/files in finally.
   No sleeps, polling, or nested dispatcher loops for synchronization.
3. Exercise `IAssocHandler.Invoke` using a Shell-created `IDataObject` for one owned file, with COM
   initialization and release on the same STA worker. Compare against executable-plus-file
   invocation to demonstrate why preserving the registration matters. Await the owned fixture's
   acknowledgment; handoff completion alone does not establish receipt.
4. Test re-enumeration and matching after discovery, duplicate/ambiguous names, removal, and a
   changed registration. Determine whether a native name plus association context is sufficient to
   select exactly the intended handler. Never substitute the default or a guessed executable.
5. Inspect packaged handlers and icon resource forms. Establish how their registration identity can
   feed both native invocation and the existing owned-RGBA icon contract. Do not assume icon
   locations are ordinary executable paths. Manual activation requires an explicitly selected
   disposable file.
6. Propose the Windows target representation with evidence before adding it to the core. If public
   native identifiers cannot support safe re-resolution, stop and compare a scoped discovery session
   against a managed descriptor; do not hide retained COM objects inside a supposedly inert value.

## Acceptance and scope

See [the proposed contract](../../docs/file-application-discovery.md). Keep production APIs
unchanged until the identity decision is reviewed. Validate native errors, cancellation/cleanup,
missing registrations, and icon absence separately. Build with analyzers and run `task check` after
changes; Windows execution, full CI, and Native AOT evidence must be reported separately. Do not
publish or tag packages. Branch: `feature/file-application-discovery`, based on `release/1.2`.
