# NuGet releases

All four packages use the same version from `Directory.Build.props`. A matching `v<Version>` tag
authorizes publication only when its source satisfies this policy:

| Source branch  | Allowed versions                                               |
| -------------- | -------------------------------------------------------------- |
| `master`       | Stable or prerelease                                           |
| `release/*`    | Prerelease only, including alpha, beta, and release candidates |
| Other branches | No release preparation or publication                          |

The release workflow runs the complete three-OS validation and isolated consumer checks, then
publishes the exact Ubuntu job artifacts; it never rebuilds packages after validation. This policy
applies to future release preparation and publication; existing releases and tags remain intact.

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

Tags do not record their branch of origin. Local release preparation checks the current branch;
publication checks require the tagged commit to be contained in remote `master`, or, for a
prerelease, a remote branch under `release/`. The shared checker fetches those heads from `origin`
and prunes deleted release branches before checking ancestry. A locally invented or stale tracking
ref is not publication evidence. Keep the source branch available through publication and retries.

For betas before integration, create a `release/<name>` branch from the candidate work and publish
there without merging each beta. An open PR can provide ordinary native CI evidence; tagging repeats
the full release validation. A stable release requires merging its preparation into `master` before
tagging and publication. This creates a short interval when source documentation can precede stable
packages on NuGet. Publishing stable packages before that merge would require changing the policy.

1. Choose the next Semantic Version (including a prerelease suffix where appropriate) and update
   `Version` in `Directory.Build.props`. Run `dotnet restore Bathos.Platform.slnx --force-evaluate`
   to update affected provider dependency lockfiles, and review them. Check that documentation
   covers changed capabilities and requirements; unchanged facts and versionless installation
   commands need no release edits. Package versions, release-note URLs, and packaged README link
   targets are derived from the version property.
2. Move consumer-facing notes from `Unreleased` in [CHANGELOG.md](CHANGELOG.md) into
   `## [<Version>] - YYYY-MM-DD`. Keep an `Unreleased` section for future changes. Add a release
   link to its tag and update the `Unreleased` link to compare that tag with the source branch
   (`master` or the relevant `release/*` branch).
3. Run `task check` and commit the version, changelog, and lockfiles. Push the candidate to its
   allowed branch: `release/*` or `master` for prereleases, and `master` for stable releases.
   Changes to `master` require a green PR under the repository ruleset. Use `task check` on ordinary
   feature branches; release-specific preparation is restricted to the branches in the table above.
   Wait for green native CI before tagging. Local checks cover only the current host; CI covers all
   three platforms.
4. Check out the prepared, pushed commit on its allowed branch, then create and push the explicit
   release tag:

```text
task release:tag TAG=vX.Y.Z
task release:push TAG=vX.Y.Z
```

`release:check` verifies branch eligibility, Semantic Versioning, the exact tag/version match, and a
valid dated changelog entry before running full local validation. `release:tag` requires a clean
checkout, runs that check, and creates an annotated local tag. `release:push` verifies the actual
tag against refreshed remote branch evidence, then pushes only that tag. Push the source branch
first. Publication verification requires a clean checkout at the tagged commit; checking another tag
from a later commit is rejected. CI uses the same `verify-tag --remote` guard from its detached tag
checkout. None of these tasks updates versions, commits changes, or edits release notes
automatically.

Task is optional. After running the SDK validation sequence in CONTRIBUTING.md, the corresponding
commands are:

```text
dotnet run --project eng/Bathos.Platform.Build --configuration Release --no-build -- verify-tag vX.Y.Z
git tag -a vX.Y.Z -m "Release X.Y.Z"
dotnet run --project eng/Bathos.Platform.Build --configuration Release --no-build -- verify-tag vX.Y.Z --remote
git push origin refs/tags/vX.Y.Z
```

Replace `X.Y.Z` with the actual committed version, including its prerelease suffix when present.
Tags authorize public NuGet publication. Do not move published tags or reuse a package version for
different bytes. Re-running a partially failed release skips already published packages and
continues the remaining packages/symbols. A missing publishing policy or identity must be configured
before retrying. Check all four package listings after publication; four pushes are not an atomic
transaction. In a fresh .NET console project outside this checkout, install the published packages
and run the README quick start twice on each supported host. For a prerelease, select its exact
version explicitly; the README's versionless commands intentionally select the latest stable
packages. After stable publication, execute those commands as written. Confirm complete output and
collision-safe naming. This public-feed smoke check establishes published-package behavior,
separately from candidate-feed validation. After a stable release is available, advance
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

## Guard verification

Run `task release:test` for the isolated Git fixtures, also included in `task check` and common CI.
They exercise both allowed branches/channels, feature-only and unpushed commits, detached tag
checkouts, exact tag selection, dirty checkouts, stale/deleted remote refs, failed fetches, Semantic
Versioning, and changelog validation. Fixtures use only disposable local repositories; they never
push release tags or obtain publishing credentials.
