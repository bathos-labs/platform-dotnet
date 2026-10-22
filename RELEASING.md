# NuGet releases

All four packages use the same version from `Directory.Build.props`. Only a matching `v<Version>` tag on a commit
contained in `master` can publish. The release workflow runs the complete three-OS validation and isolated consumer
checks, then publishes the exact Ubuntu job artifacts; it never rebuilds packages after validation.

## One-time setup

1. On NuGet.org, create a trusted publishing policy for owner `bathos-labs`, repository `platform-dotnet`, workflow
   **`release.yml`**, environment **`nuget`**. Choose the intended NuGet account/organization owner and enable creation
   and update of the four `Bathos.Platform` package IDs (core plus `.MacOS`, `.Windows`, `.Linux`).
2. Set repository variable **`NUGET_USER`** to the NuGet login username, not an email or organization name.
3. Configure the GitHub **`nuget`** environment for release tags and any desired reviewer requirement.

The publish job alone has `id-token: write`. `NuGet/login` exchanges GitHub OIDC evidence for a short-lived credential;
no long-lived API key is stored. NuGet policy setup requires access to the publishing account and is not performed
by committing this workflow. Official setup: https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing

## Publish

1. Choose the next Semantic Version (including a prerelease suffix where appropriate) and update `Version` in
   `Directory.Build.props`. Run `dotnet restore Bathos.Platform.slnx --force-evaluate` to update affected provider
   dependency lockfiles, and review them.
2. Move consumer-facing notes from `Unreleased` in [CHANGELOG.md](CHANGELOG.md) into `## [<Version>] - YYYY-MM-DD`.
   Keep an `Unreleased` section for future changes. Add a release link to its tag and update the `Unreleased` link to
   compare that tag with `master`.
3. Run `task release:check TAG=v<Version>`, commit the version, changelog, and lockfiles, then merge/push to `master`
   and wait for green native CI. The local check covers only the current host; CI covers all three platforms.
4. Create and push the explicit release tag:

```text
task release:tag TAG=v0.1.0-beta.1
task release:push TAG=v0.1.0-beta.1
```

`release:tag` requires a clean `master`, repeats local validation, verifies the tag matches the package version, and
creates an annotated local tag. `release:push` pushes only that tag, not branch commits or other tags. Push `master`
first: CI rejects a tagged commit outside remote `master`. Neither task updates versions, commits changes, or edits
release notes automatically. Review the dated changelog before tagging.

Task is optional. After running the SDK validation sequence in CONTRIBUTING.md, the corresponding commands are:

```text
dotnet run --project eng/Bathos.Platform.Build --configuration Release --no-build -- verify-tag v0.1.0-beta.1
git tag -a v0.1.0-beta.1 -m "Release 0.1.0-beta.1"
git push origin refs/tags/v0.1.0-beta.1
```

Use the actual committed version. Tags authorize public NuGet publication. Do not move published tags or reuse a
package version for different bytes. Re-running a partially failed release skips already published packages and
continues the remaining packages/symbols. A missing publishing policy or identity must be configured before retrying.
Check all four package listings and consumer restore after publication; four pushes are not an atomic transaction.
Create a GitHub prerelease for a beta tag using the corresponding changelog entry. Attach the verified `packages`
workflow artifact's `.nupkg` and `.snupkg` files if providing release downloads; do not rebuild them locally.

For local consumers, use a unique prerelease version and a folder feed, or an explicit source-project override.
Keep consumer package lockfiles and source-mode restore outputs separate. Never commit a developer-specific checkout path.
