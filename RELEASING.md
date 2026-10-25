# NuGet releases

All four packages use the same version from `Directory.Build.props`. Only a matching `v<Version>`
tag on a commit contained in `master` can publish. The release workflow runs the complete three-OS
validation and isolated consumer checks, then publishes the exact Ubuntu job artifacts; it never
rebuilds packages after validation.

## One-time setup

1. On NuGet.org, create a trusted publishing policy for owner `bathos-labs`, repository
   `platform-dotnet`, workflow **`release.yml`**, environment **`nuget`**. Choose the intended NuGet
   account/organization owner and enable creation and update of the four `Bathos.Platform` package
   IDs (core plus `.MacOS`, `.Windows`, `.Linux`).
2. Set repository variable **`NUGET_USER`** to the NuGet login username, not an email or
   organization name.
3. Configure the GitHub **`nuget`** environment for release tags and any desired reviewer
   requirement.

The publish job alone has `id-token: write`. `NuGet/login` exchanges GitHub OIDC evidence for a
short-lived credential; no long-lived API key is stored. NuGet policy setup requires access to the
publishing account and is not performed by committing this workflow. Official setup:
https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing

## Documentation and development policy

Advance `Version` on the feature/release branch when development resumes. Keep all four packages on
the same version and channel; experimental Linux implementations do not require a separate package
prerelease status. Small releases and isolated feature branches keep published behavior and the
README close together. Merging release preparation before publication can briefly expose upcoming
capabilities; it does not establish that those packages are already available.

Consumer installation instructions select stable releases without embedded package versions. Release
history belongs in the changelog. Update the README matrix, integration recipes, or
[platform prerequisites](docs/platform-support.md) when capabilities or requirements change, rather
than rewriting them for each package version. There is no generated installation block. Packing pins
the README's repository-file links to the package tag in an untracked intermediate copy. Candidate
example checks use an isolated local feed and do not establish public package availability.

## Publish

1. Choose the next Semantic Version (including a prerelease suffix where appropriate) and update
   `Version` in `Directory.Build.props`. Run `dotnet restore Bathos.Platform.slnx --force-evaluate`
   to update affected provider dependency lockfiles, and review them. Check that documentation
   covers changed capabilities and requirements; unchanged facts and versionless installation
   commands need no release edits. Package versions, release-note URLs, and packaged README link
   targets are derived from the version property.
2. Move consumer-facing notes from `Unreleased` in [CHANGELOG.md](CHANGELOG.md) into
   `## [<Version>] - YYYY-MM-DD`. Keep an `Unreleased` section for future changes. Add a release
   link to its tag and update the `Unreleased` link to compare that tag with `master`.
3. Run `task release:check TAG=vX.Y.Z`, commit the version, changelog, and lockfiles, then
   merge/push to `master` and wait for green native CI. The local check covers only the current
   host; CI covers all three platforms.
4. Create and push the explicit release tag:

```text
task release:tag TAG=vX.Y.Z
task release:push TAG=vX.Y.Z
```

`release:tag` requires a clean `master`, repeats local validation, verifies the tag matches the
package version, and creates an annotated local tag. `release:push` pushes only that tag, not branch
commits or other tags. Push `master` first: CI rejects a tagged commit outside remote `master`.
Neither task updates versions, commits changes, or edits release notes automatically. Review the
dated changelog before tagging; `verify-tag` requires a matching version and a dated changelog
entry.

Task is optional. After running the SDK validation sequence in CONTRIBUTING.md, the corresponding
commands are:

```text
dotnet run --project eng/Bathos.Platform.Build --configuration Release --no-build -- verify-tag vX.Y.Z
git tag -a vX.Y.Z -m "Release X.Y.Z"
git push origin refs/tags/vX.Y.Z
```

Replace `X.Y.Z` with the actual committed version, including its prerelease suffix when present.
Tags authorize public NuGet publication. Do not move published tags or reuse a package version for
different bytes. Re-running a partially failed release skips already published packages and
continues the remaining packages/symbols. A missing publishing policy or identity must be configured
before retrying. Check all four package listings after publication; four pushes are not an atomic
transaction. In a fresh .NET console project outside this checkout, execute the README installation
commands against NuGet.org and run its quick start twice on each supported host. Confirm complete
output and collision-safe naming. This public-feed smoke check establishes what new consumers
receive, separately from candidate-feed validation. After a stable release is available, advance
`PackageValidationBaselineVersion` in `eng/Bathos.Package.props` and regenerate the released
consumer lockfile under `eng/api-compatibility/consumer` for that version in a follow-up PR. Review
the consumer fixture's covered contracts and run the full gate. Prereleases do not replace the
stable baseline. Keep the previous baseline throughout release validation; do not advance it to an
unpublished candidate or suppress compatibility diagnostics without an explicitly approved
breaking-release plan. See CONTRIBUTING.md for the checks. Create a GitHub release using the
corresponding changelog entry, marking prerelease tags as prereleases. Attach the verified
`packages` workflow artifact's `.nupkg` and `.snupkg` files if providing release downloads; do not
rebuild them locally.

For local consumers, use a unique prerelease version and a folder feed, or an explicit
source-project override. Keep consumer package lockfiles and source-mode restore outputs separate.
Never commit a developer-specific checkout path.
