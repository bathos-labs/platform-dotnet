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

Update the shared version and provider project-dependency lockfiles, run validation, and merge into `master`.
After checking the intended commit and MIT package contents:

```text
git tag -a v0.1.0-alpha.1 -m "Release 0.1.0-alpha.1"
git push origin v0.1.0-alpha.1
```

Use the actual committed version. Tags authorize public NuGet publication. Do not move published tags or reuse a
package version for different bytes. Re-running a partially failed release skips already published packages and
continues the remaining packages/symbols. A missing publishing policy or identity must be configured before retrying.
Check all four package listings and consumer restore after publication; four pushes are not an atomic transaction.

For local consumers, use a unique prerelease version and a folder feed, or an explicit source-project override.
Keep consumer package lockfiles and source-mode restore outputs separate. Never commit a developer-specific checkout path.
