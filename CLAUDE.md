> **Cross-repo work**: this repo participates in a system of 16 repos.
> Architecture state, decisions, and cross-repo specs live in [`../architecture`](https://github.com/VION-IoT/architecture).
> Clone it: `git clone git@github.com:VION-IoT/architecture.git ../architecture`
> Before planning a feature with scope ≥ 2 repos, read the relevant `architecture/systems/*.md`
> and run `/spec <slug> <repos>` from the architecture repo.

# Vion Dale SDK

The Vion Dale SDK is an IoT runtime SDK for actor-based logic blocks. This repository is **source-available** (Apache 2.0) but closed to external contributions — see [CONTRIBUTING.md](CONTRIBUTING.md).

The private Vion Dale runtime lives in a separate repository and consumes the packages published from here as NuGet packages.

## Repository Structure

```
Vion.Dale.Sdk/              Core SDK — LogicBlockBase, attributes, introspection
Vion.Dale.Sdk.Generators/   Roslyn source generator + analyzers (shipped inside Vion.Dale.Sdk)
Vion.Dale.Sdk.Http/         HTTP client extensions for logic blocks
Vion.Dale.Sdk.Modbus.*/     Modbus Core/Tcp/Rtu protocol bindings
Vion.Dale.Sdk.DigitalIo/    Digital I/O contract abstractions
Vion.Dale.Sdk.AnalogIo/     Analog I/O contract abstractions
Vion.Dale.Sdk.TestKit/      Test helpers for logic block unit testing
Vion.Dale.Sdk.*.TestKit/    I/O-specific test helpers (DigitalIo, AnalogIo, Modbus.Rtu)
Vion.Dale.ProtoActor/       Proto.Actor integration (net10.0)
Vion.Dale.Plugin/           Plugin AssemblyLoadContext (net10.0) — shared by the runtime + LogicBlockParser
Vion.Dale.LogicBlockParser/ Assembly introspector — bundled into Vion.Dale.Sdk as a tool
Vion.Dale.DevHost/          Local development host
Vion.Dale.DevHost.Web/      Web UI for DevHost (static SPA assets)
Vion.Dale.Cli/              CLI tool (dotnet global tool `dale`) — see Vion.Dale.Cli/CLAUDE.md
Vion.Dale.Cli.Test/         CLI unit tests
templates/                  Project template bundled as content inside Vion.Dale.Cli (source used by `dale new`)
examples/                   Example LogicBlock libraries (not in the main sln; see Phase 3b in the migration plan)
scripts/                    Build / versioning / docs generation scripts
```

## Key Concepts

**LogicBlock**: an actor-based computation unit. Extends `LogicBlockBase`. Has service properties (observable state), measuring points (read-only metrics), timers, and communicates with other blocks via interfaces and contracts.

**Contracts**: define hardware I/O bindings (Modbus registers, digital pins, etc.) and inter-block messaging (commands, request-response). Shared DTOs live in `Vion.Contracts` (separate repo).

**Introspection**: `Vion.Dale.LogicBlockParser` loads a built assembly, runs `LogicBlockIntrospection`, and outputs full metadata as JSON. This is the source of truth — not source code parsing.

## Building

```bash
dotnet build Vion.Dale.Sdk.sln
dotnet test Vion.Dale.Sdk.sln
```

Examples (`examples/*`) and the inner template projects (`templates/vion-iot-library/VionIotLibraryTemplate*`) are in `Vion.Dale.Sdk.sln` and reference the SDK via `PackageReference`. Their checked-in versions must match a published `Vion.Dale.*` package (any preview or stable release). `scripts/set-version.ps1 -Scope references` bumps them after each release. The template content is bundled into `Vion.Dale.Cli` and a pack-time MSBuild target rewrites the template's `Vion.Dale.*` `PackageReference` versions to match the CLI's own `$(Version)`, so `dale new` always produces projects that reference the same version as the CLI installed.

## Dale CLI

The CLI (`dale`) is the primary developer interface for consumers of the SDK. Install as a .NET global tool:

```bash
dotnet tool install -g Vion.Dale.Cli
```

Commands: `dale build`, `dale test`, `dale dev`, `dale list`, `dale new`, `dale add logicblock|serviceproperty|measuringpoint|timer`, `dale pack`, `dale upload`, `dale login`, `dale logout`, `dale whoami`, `dale config show|set-environment|set-integrator`.

See [Vion.Dale.Cli/CLAUDE.md](Vion.Dale.Cli/CLAUDE.md) for architecture, patterns, and how to add commands.

## Versioning & Releases

Versions are driven by git tags. No `<Version>` in any SDK `.csproj`. See [README.md#releases](README.md#releases) for the full flow.

- Push to `main` → CI publishes `0.0.0-ci.{run_number}` to the private Azure DevOps feed.
- Push tag `vX.Y.Z` → CI publishes `X.Y.Z` to the private feed **and** nuget.org (via Trusted Publishing).

After a release, bump the template/example `PackageReference` versions so the next commit ships consistent refs:

```bash
pwsh scripts/set-version.ps1 -Version X.Y.Z -Scope references
```

## CI/CD

Single GitHub Actions workflow: [.github/workflows/publish.yml](.github/workflows/publish.yml). Builds, tests, packs, pushes to the private Azure DevOps feed on every push, and additionally pushes to nuget.org on tags.

## Auth (CLI consumer-facing)

- **Interactive (developers)**: `dale login` — PKCE browser flow against Keycloak (`dale-cli` public client — external identity, do not rename).
- **CI/CD**: `--client-id <id> --client-secret <secret>` — client-credentials flow with service accounts.
- **Environments**: `production` (api.vion.swiss) and `test` (api.test.vion.swiss), configurable via `dale config set-environment`.
- **Integrator context**: auto-resolved from `/me` if the user belongs to one integrator, otherwise selected during `dale login` or via `--integrator-id`.

Env vars: `DALE_CLIENT_ID`, `DALE_CLIENT_SECRET`, `DALE_INTEGRATOR_ID` (all user-facing, keep the `DALE_` prefix).

## Code Style

- C# with `ImplicitUsings: false` (all usings explicit).
- `Nullable: enabled`.
- Format with `jb cleanupcode Vion.Dale.Sdk.sln --profile="Built-in: Reformat Code"` (JetBrains CLI).
- Allman brace style throughout.
- Targets: `netstandard2.1` for SDK (cross-platform plugin compatibility), `netstandard2.0` for source generator, `net10.0` for CLI / DevHost / ProtoActor / Plugin / LogicBlockParser / tests.

## Related Repos

- `vion-iot/vion-contracts` — Shared DTOs (MQTT topics, payloads, FlatBuffers schemas, introspection models). Published as `Vion.Contracts`.
- `vion-iot/dale` (private) — Vion Dale runtime. Consumes `Vion.Dale.Sdk`, `Vion.Dale.ProtoActor`, and `Vion.Dale.Plugin` as NuGet packages.
- `documentation` — Public docs site (API reference auto-generated from this repo).
