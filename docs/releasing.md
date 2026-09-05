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

**Every release obliges this bump — it is part of releasing, not a follow-up.** The templates,
examples and `libraries/` in this repo reference the SDK as NuGet packages, and their checked-in
versions must match a published `Vion.Dale.*` release (preview or stable). Skipping it leaves the next
commit shipping references to a version that is no longer current, and — because `dale upload` reads
each example's `<Version>` and the workflow passes `--skip-duplicate` — leaves the upload silently
doing nothing.

**Wait for the packages, not for the green run.** The publish job reporting success is not the
precondition — the packages landing on the feed is, and they land *one at a time*, minutes after the
run goes green (PR #134 saw ~5 min, with the last package ~40 s behind the rest; the 0.10.8 bump saw
~6 min). Bumping on the first green check fails restore for whichever package has not landed yet. The
check is one loop over the referenced ids:

```bash
for p in vion.dale.sdk vion.dale.sdk.http vion.dale.sdk.digitalio vion.dale.sdk.digitalio.testkit          vion.dale.sdk.analogio vion.dale.sdk.analogio.testkit vion.dale.sdk.modbus.core          vion.dale.sdk.modbus.tcp vion.dale.sdk.modbus.tcp.testkit vion.dale.sdk.modbus.rtu          vion.dale.sdk.modbus.rtu.testkit vion.dale.sdk.testkit vion.dale.devhost vion.dale.devhost.web; do
  curl -s "https://api.nuget.org/v3-flatcontainer/$p/index.json" | grep -q '"X.Y.Z"' || echo "missing: $p"
done
```

It is still a change like any other, so it goes on a branch and through a PR (working agreement
rule 1) — never straight to `main`:

```bash
git switch -c chore/bump-refs-X.Y.Z
pwsh scripts/set-version.ps1 -Version X.Y.Z -Scope references
git add -A && git commit -m "Bump example/template refs to X.Y.Z"
git push -u origin HEAD && gh pr create --fill
```

`set-version.ps1` covers **templates, examples and `libraries/`** — the same three the paragraph above
obliges, so no part of the bump is manual. Per project it updates the `Vion.Dale.*` `PackageReference`
versions, and for the one packable project per example and per library also its own `<Version>` (the
DevHost and Test projects do not pack). A library's `<Version>` is what triggers its upload, so it
tracks the SDK release here rather than being bumped separately — see
[`upload-libraries.yml`](../.github/workflows/upload-libraries.yml).

Then check what the bump should *show*. A release that adds a capability is the moment to demonstrate
it in an example — several releases in this repo have carried an example change in the same breath
(`Guid` support, string formats, emission policy, service relations). Ask what the new version lets an
author do that the examples do not yet show.

(Note: the `Vion.Dale.Cli` package rewrites its bundled template's `PackageReference` versions at pack time to match its own `$(Version)` — see `Vion.Dale.Cli.csproj`. So a released tool's `dale new` output matches it regardless of when `set-version.ps1` last ran. The rewrite is **skipped for a `0.0.0*` version** — an untagged CI or local build is on no feed, so rewriting to it would produce a project that cannot restore; such a build's `dale new` scaffolds the checked-in references and says so.)

### After a release: answer the consumer

Where the release resolves a Jira item from the "Dale SDK Feedback" epic (VION-62), the item's
closing comment (written by `/fix`, naming the version) is the text the maintainer relays to the
consumer's channel — the consumer is holding a workaround until they know they can drop it and does
not read Jira.

## Version immutability

Once a version is published to nuget.org, the version ID is permanent. You can *unlist* a version (which hides it from search and `dotnet add package`), but the ID stays burned — you cannot re-upload the same version, even after yanking. Pick the next number for any subsequent change, even a tiny fix.

## Required configuration

One-time setup per repo. Flag this if you fork or rotate credentials:

- GitHub secret `AZURE_DEVOPS_PAT` — PAT with `Packaging: Read & write` on the Azure DevOps feed.
- GitHub secret `NUGET_API_KEY` — nuget.org API key scoped to push this SDK's packages. Rotate per nuget.org's policy (max 365 days).
- GitHub secret `DOCS_REPO_PAT` — PAT with `contents:write` on `VION-IoT/documentation`. Used by `publish.yml` to auto-push the API reference and to open a drift issue when the PublicApi surface changes.
- GitHub secret `ARCHITECTURE_REPO_PAT` — used by `publish.yml` to open a drift issue on `VION-IoT/architecture` when the CLI help snapshot changes.
- GitHub secrets `DALE_CI_CLIENT_ID` / `DALE_CI_CLIENT_SECRET` — Keycloak service-account credentials, scoped per GitHub Environment (`test` and `production`), used by `examples.yml` and `upload-libraries.yml`. Every qualifying `main` push uploads to Cloud test automatically and parks a production upload behind a required-reviewer approval; see [`specs/cli.md`](specs/cli.md).

Trusted Publishing was the prior approach but does not currently work with reusable workflows: the OIDC `job_workflow_ref` claim points at the shared-workflows repo, not this repo, and nuget.org rejects the token exchange. See [community discussion #179952](https://github.com/orgs/community/discussions/179952). Re-evaluate when nuget.org adds reusable-workflow support.

## Documentation drift detection

`publish.yml` keeps [`docs/snapshots/publicapi-manifest.json`](snapshots/publicapi-manifest.json) and [`docs/snapshots/cli-help-snapshot.txt`](snapshots/cli-help-snapshot.txt) in sync with the code:

- On PRs, the snapshots are regenerated and auto-committed to the PR branch so `main` is always up-to-date.
- On `main` pushes, the snapshots are diffed against `HEAD~1` and any change opens an issue in [`VION-IoT/documentation`](https://github.com/VION-IoT/documentation) so the docs can be kept in step.
- On `main` pushes, a fresh `api-reference.md` is also pushed to the docs repo.

None of these run on tag pushes — tags are for publishing.
