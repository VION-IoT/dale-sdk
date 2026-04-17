# Vion Dale SDK

The Vion Dale SDK is an IoT runtime SDK for actor-based logic blocks. Logic blocks are composable units of behaviour that talk to hardware (Modbus, digital/analog I/O, HTTP) and to each other, hosted by the Vion Dale runtime.

This repository ships:

| Package | Purpose |
|---|---|
| `Vion.Dale.Sdk` | Core SDK — `LogicBlockBase`, attributes, introspection, code generator |
| `Vion.Dale.Sdk.Http` | HTTP client extensions for logic blocks |
| `Vion.Dale.Sdk.DigitalIo` / `Vion.Dale.Sdk.AnalogIo` | Digital and analog I/O contracts |
| `Vion.Dale.Sdk.Modbus.Core` / `.Tcp` / `.Rtu` | Modbus bindings |
| `Vion.Dale.Sdk.TestKit` (+ `.DigitalIo.TestKit`, `.AnalogIo.TestKit`, `.Modbus.Rtu.TestKit`) | Test helpers for unit-testing logic blocks |
| `Vion.Dale.ProtoActor` | Proto.Actor integration |
| `Vion.Dale.Plugin` | `AssemblyLoadContext` used to load Dale plugins (extracted so the runtime and the parser can share it) |
| `Vion.Dale.DevHost` / `Vion.Dale.DevHost.Web` | Local development host + dashboard |
| `Vion.Dale.Cli` | `dale` — the .NET global tool developers use to build, test, pack, and upload logic block libraries |

## Install the CLI

```bash
dotnet tool install -g Vion.Dale.Cli
dale --help
```

## Use the SDK

```bash
dale new MyLibrary
cd MyLibrary
dale build
dale test
dale dev
```

## Source-available

This repository is source-available. Issues and pull requests are not accepted from outside the `vion-iot` organization. For questions or to report a problem, see [SUPPORT.md](SUPPORT.md).

## Releases

Versions are driven by git tags. There is no version number in any `.csproj`.

| Trigger | Published version | Destination |
|---|---|---|
| Push to `main` | `0.0.0-ci.{run_number}` | Private Azure DevOps feed only — for internal integration testing, never depend on from shipped code |
| Push tag `v0.2.0` | `0.2.0` | Private feed + nuget.org |
| Push tag `v0.2.0-preview.1` | `0.2.0-preview.1` | Private feed + nuget.org (treated as pre-release) |

All packages in this repo share a single version, bumped together.

### Cutting a release

Prerequisites:
- `main` is green on the commit you want to release.
- You have [`gh`](https://cli.github.com/) installed and authenticated (`gh auth status`).

```bash
# Stable release:
gh release create v0.2.0 --target main --generate-notes \
  --title "v0.2.0" --notes "Short release summary."

# Pre-release (add --prerelease for the UI badge; NuGet detects pre-release
# automatically from the SemVer suffix):
gh release create v0.2.0-preview.1 --target main --prerelease --generate-notes \
  --title "v0.2.0-preview.1" --notes "What this preview validates."
```

Creating the release pushes the tag, which triggers [`publish.yml`](.github/workflows/publish.yml):

1. Builds and packs every packable project with `Version` taken from the tag (strips the `v` prefix).
2. Pushes `.nupkg` + `.snupkg` to the private Azure DevOps feed.
3. Publishes to nuget.org using [Trusted Publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing) (short-lived OIDC token — no API key stored).

Verify the result under the [VION-IoT profile on nuget.org](https://www.nuget.org/profiles/VION-IoT).

### After a release: update example/template references

The templates and examples in this repo reference the SDK as NuGet packages. After a release, bump their `PackageReference` versions to match:

```bash
pwsh scripts/set-version.ps1 -Version 0.2.0 -Scope references
git add -A && git commit -m "Bump example/template refs to 0.2.0"
git push
```

### Version immutability

Once a version is published to nuget.org, the version ID is permanent. You can *unlist* a version (which hides it from search and `dotnet add package`), but the ID stays burned — you cannot re-upload the same version, even after yanking. Pick the next number for any subsequent change, even a tiny fix.

### Required configuration

One-time setup per repo; flag this if you fork or rotate credentials:

- GitHub secret `AZURE_DEVOPS_PAT` — PAT with `Packaging: Read & write` on the Azure DevOps feed.
- GitHub secret `NUGET_USER` — **individual** nuget.org username that is a member of the org owning the Trusted Publishing policy. Not the org name. (Docs use `contoso-bot` as the example — this matters; setting it to the org name produces `No matching trust policy owned by user 'X' was found`.)
- Trusted Publishing policy on nuget.org: Package Owner `VION-IoT`, Repository Owner `VION-IoT`, Repository `dale-sdk`, Workflow File `publish.yml`. See [NuGet's Trusted Publishing docs](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing) for the UI walkthrough.

## Repository layout

```
Vion.Dale.Sdk/              Core SDK
Vion.Dale.Sdk.Generators/   Roslyn source generator + analyzers (bundled into Vion.Dale.Sdk)
Vion.Dale.Sdk.*/            Extension packages (Http, DigitalIo, AnalogIo, Modbus.*)
Vion.Dale.Sdk.TestKit/      Test helpers (+ I/O-specific TestKits)
Vion.Dale.ProtoActor/       Proto.Actor integration
Vion.Dale.Plugin/           AssemblyLoadContext for Dale plugins
Vion.Dale.DevHost/          Local dev host
Vion.Dale.DevHost.Web/      Dev host web UI
Vion.Dale.LogicBlockParser/ Assembly introspector bundled in Vion.Dale.Sdk
Vion.Dale.Cli/              dale global tool
templates/                  Project template bundled in the CLI
examples/                   Example logic block libraries
scripts/                    Build & release scripts
```

The private Vion Dale runtime lives in a separate repository and consumes `Vion.Dale.Sdk`, `Vion.Dale.ProtoActor`, and `Vion.Dale.Plugin` as NuGet packages.

## License

Apache 2.0 — see [LICENSE](LICENSE).
