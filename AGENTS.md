# Working in Bathos Platform for .NET

- Inspect Git status first; preserve unrelated changes. Work serially unless delegation is explicitly requested.
- Read README.md, docs/architecture.md, and CONTRIBUTING.md before structural changes. Explain architectural tradeoffs
  before expanding scope. Prefer demonstrated requirements and focused capability interfaces.
- Core and provider packages must remain free of UI, DI, imaging, and product dependencies. Providers reference only
  the core and runtime. Null capability, user cancellation, and operation failure are distinct contracts.
- Preserve nullable analysis. Narrow native/serialization/filesystem uncertainty at the boundary. Never silence
  diagnostics or introduce unsafe fallbacks to make a check pass.
- The API is prerelease. Replace obsolete representations directly; do not add compatibility shims without agreement.
- Follow CONTRIBUTING.md validation before a cohesive commit. Native evidence is specific to the executing host.
  Wait for owned work; do not synchronize tests with sleeps, polling, or dispatcher loops.
- Use American English. Keep documentation proportional and current. GitHub Issues owns work tracking.
- Default branch: master. License: MIT. Never commit local paths, credentials, build output, IDE state, or unrelated code.
- Commit when requested. Tagging a release publishes packages; follow RELEASING.md and require an explicit release request.
