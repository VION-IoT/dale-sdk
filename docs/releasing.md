# Releasing

Maintainer reference. Not for public consumption — see the root [README](../README.md) for the user-facing intro.

## How versions work

Git tags drive versions. There is no `<Version>` in any SDK `.csproj`.

| Trigger | Published version | Destination |
|---|---|---|
| Push to `main` | `0.0.0-ci.{run_number}` | Private Azure DevOps feed only — for internal integration testing, never depend on from shipped code |
| Push tag `v0.2.0` | `0.2.0` | Private feed + nuget.org |
| Push tag `v0.2.0-preview.1` | `0.2.0-preview.1` | Private feed + nuget.org (treated as pre-release) |

All packages in this repository ship at the same version, bumped together.

## Cutting a release

Prerequisites:
- `main` is green on the commit you want to release.
- `gh` is installed and authenticated (`gh auth status`).

```bash
# Stable:
gh release create v0.2.0 --target main --generate-notes \
  --title "v0.2.0" --notes "Short release summary."

# Pre-release (add --prerelease for the UI badge; NuGet detects pre-release
# automatically from the SemVer suffix):
gh release create v0.2.0-preview.1 --target main --prerelease --generate-notes \
  --title "v0.2.0-preview.1" --notes "What this preview validates."
```

`gh release create` creates the git tag (at the `--target` commit) and the GitHub Release in one step. The new tag triggers [`publish.yml`](../.github/workflows/publish.yml):

1. Builds and packs every packable project with `Version` taken from the tag (strips the `v` prefix).
2. Pushes `.nupkg` + `.snupkg` to the private Azure DevOps feed.
3. Publishes to nuget.org using a long-lived API key (`NUGET_API_KEY`).

Verify the result under the [VION-IoT profile on nuget.org](https://www.nuget.org/profiles/VION-IoT).

### After a release: update example/template references

The templates and examples in this repo reference the SDK as NuGet packages. After a release, bump their `PackageReference` versions to match:

```bash
pwsh scripts/set-version.ps1 -Version X.Y.Z -Scope references
git add -A && git commit -m "Bump example/template refs to X.Y.Z"
git push
```

(Note: the `Vion.Dale.Cli` package rewrites its bundled template's `PackageReference` versions at pack time to match its own `$(Version)` — see `Vion.Dale.Cli.csproj`. So the `dale new` output is always self-consistent regardless of when `set-version.ps1` last ran.)

## Version immutability

Once a version is published to nuget.org, the version ID is permanent. You can *unlist* a version (which hides it from search and `dotnet add package`), but the ID stays burned — you cannot re-upload the same version, even after yanking. Pick the next number for any subsequent change, even a tiny fix.

## Required configuration

One-time setup per repo. Flag this if you fork or rotate credentials:

- GitHub secret `AZURE_DEVOPS_PAT` — PAT with `Packaging: Read & write` on the Azure DevOps feed.
- GitHub secret `NUGET_API_KEY` — nuget.org API key scoped to push this SDK's packages. Rotate per nuget.org's policy (max 365 days).
- GitHub secret `DOCS_REPO_PAT` — PAT with `contents:write` on `VION-IoT/documentation`. Used by `publish.yml` to auto-push the API reference and to open drift-detection issues when the PublicApi surface or CLI help changes.
- GitHub secrets `DALE_CI_CLIENT_ID` / `DALE_CI_CLIENT_SECRET` — Keycloak service-account credentials used by `examples.yml` to upload example libraries to Cloud test on every example change.

Trusted Publishing was the prior approach but does not currently work with reusable workflows: the OIDC `job_workflow_ref` claim points at the shared-workflows repo, not this repo, and nuget.org rejects the token exchange. See [community discussion #179952](https://github.com/orgs/community/discussions/179952). Re-evaluate when nuget.org adds reusable-workflow support.

## Documentation drift detection

`publish.yml` keeps [`docs/snapshots/publicapi-manifest.json`](snapshots/publicapi-manifest.json) and [`docs/snapshots/cli-help-snapshot.txt`](snapshots/cli-help-snapshot.txt) in sync with the code:

- On PRs, the snapshots are regenerated and auto-committed to the PR branch so `main` is always up-to-date.
- On `main` pushes, the snapshots are diffed against `HEAD~1` and any change opens an issue in [`VION-IoT/documentation`](https://github.com/VION-IoT/documentation) so the docs can be kept in step.
- On `main` pushes, a fresh `api-reference.md` is also pushed to the docs repo.

None of these run on tag pushes — tags are for publishing.
