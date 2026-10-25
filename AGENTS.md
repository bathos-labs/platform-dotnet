# Working on Bathos native platform abstractions for .NET

- Inspect Git status first; preserve unrelated changes. Work serially unless delegation is
  explicitly requested.
- Read README.md, docs/architecture.md, and CONTRIBUTING.md before structural changes. Explain
  architectural tradeoffs before expanding scope. Prefer demonstrated requirements and focused
  capability interfaces.
- Core and provider packages must remain free of UI, DI, imaging, and product dependencies.
  Providers reference only the core and runtime. Null capability, user cancellation, and operation
  failure are distinct contracts.
- Preserve nullable analysis. Narrow native/serialization/filesystem uncertainty at the boundary.
  Never silence diagnostics or introduce unsafe fallbacks to make a check pass.
- The project is released. Backward compatibility is a formal requirement for stable public API
  contracts, including callers and implementers. Add capabilities through focused interfaces and
  default-null facade properties. Discuss breaking changes before implementation; do not introduce
  speculative compatibility machinery.
- Follow CONTRIBUTING.md validation before a cohesive commit. Native evidence is specific to the
  executing host. Wait for owned work; do not synchronize tests with sleeps, polling, or dispatcher
  loops.
- After Markdown edits, run `task docs:format`, review the changes, and run `task check` before
  committing. CI verifies Markdown formatting without modifying files.
- Keep consumer documentation static: versionless stable installs, release history in CHANGELOG.md,
  and detailed deployment requirements in docs/platform-support.md. All providers share a release
  channel; experimental Linux behavior does not require a separate prerelease package.
- Screen sampler interaction requires human validation. Do not launch or drive the interactive
  picker with computer-use automation; that path is currently unreliable. Use
  `task eng:sampler:smoke` for noninteractive checks and hand off `task eng:sampler` to the user.
  Revisit automation only when explicitly investigating its compatibility; never infer sampler
  success or failure from automated pointer/key behavior.
- Use American English. Keep documentation proportional and current. GitHub Issues owns work
  tracking.
- Default branch: master. License: MIT. Never commit local paths, credentials, build output, IDE
  state, or unrelated code.
- Commit when requested. Tagging a release publishes packages; follow RELEASING.md and require an
  explicit release request.
