> **Cross-repo work**: this repo is part of the VION platform.
> Architecture state, decisions, and cross-repo specs live in [`../architecture`](https://github.com/VION-IoT/architecture).
> Clone it: `git clone git@github.com:VION-IoT/architecture.git ../architecture`
> Before planning a feature with scope ≥ 2 repos, read the relevant `architecture/systems/*.md`
> and run `/spec <slug> <repos>` from the architecture repo.

# Vion Dale SDK

The Vion Dale SDK is an IoT runtime SDK for actor-based logic blocks. This repository is **source-available** (Apache 2.0) but closed to external contributions — see [CONTRIBUTING.md](CONTRIBUTING.md).

The private Vion Dale runtime lives in a separate repository and consumes the packages published from here as NuGet packages.

This file is the always-loaded contract. Detail lives in the convention docs below, loaded when you do
the work they govern — **read the linked doc before doing the matching work, and follow it.**

## Read before you write

| When you're… | Read |
| --- | --- |
| adding or changing an attribute, a public type or member, an analyzer, or anything reaching the introspection JSON | [`docs/sdk-surface-conventions.md`](docs/sdk-surface-conventions.md) — surface minimalism, XML-doc style, delete-don't-deprecate, the analyzer obligation and the **Metalama blind spot**, PublicApi snapshots |
| writing or modifying a test | [`docs/testing-conventions.md`](docs/testing-conventions.md) — MSTest inside / xunit.v3 outside, analyzer tests vs the real compilation, packed-artifact verification, determinism |
| touching `Vion.Dale.DevHost*`, the scenario runner, or stepping | [`docs/devhost-conventions.md`](docs/devhost-conventions.md) — the demonstrate-don't-assert verify loop, clock modes, the four scenario-step definition sites, contract pairing and what it refuses; the SPA's own contract is [`Vion.Dale.DevHost.Web/CLAUDE.md`](Vion.Dale.DevHost.Web/CLAUDE.md) |
| writing a simulator block or a provider face (the peer a bench needs on the far side of a contract) | [`docs/simulator-authoring.md`](docs/simulator-authoring.md) — provider faces, the ideal-I/O echo recipe, when to model the device instead |
| renaming anything that reaches introspection (service, member, contract, interface, enum member, enum/struct type, PackageId) | [`docs/identifier-stability.md`](docs/identifier-stability.md) — these identifiers are the cloud's translation keys |
| touching `Vion.Dale.Sdk.Modbus.*` or either Modbus example | the `modbus-smoke` skill ([`.claude/skills/modbus-smoke/`](.claude/skills/modbus-smoke/SKILL.md)) — the link policy over a real socket pair on `127.0.0.1:15020`; real clock, ~1 min |
| adding a CLI command | [`Vion.Dale.Cli/CLAUDE.md`](Vion.Dale.Cli/CLAUDE.md) |
| cutting a release, or bumping examples after one | [`docs/releasing.md`](docs/releasing.md) |
| reviewing a change before a PR | `/vion-code-review branch` — [`.claude/commands/vion-code-review.md`](.claude/commands/vion-code-review.md) |

**Before writing new code, read similar existing files** in the same area and replicate their
structure. Do not invent new patterns; name the precedent you followed.

**Design docs go in [`docs/rfcs/`](docs/rfcs/)** as numbered `NNNN-slug.md`, matching the header of the
existing ones. `docs/superpowers/` is **gitignored** (`.gitignore:300`) per architecture decision 0011,
so anything a planning skill writes there cannot be committed — `git check-ignore` is the tell. Redirect
anything meant to last to `docs/rfcs/`. Cross-repo specs live in `../architecture/specs/`, never here.

## Working agreement

1. **Branch and PR, never straight to main.** Work on a feature branch and open a PR. (Exceptions are
   explicit and rare — the user says "do it right on main".)
2. **Do not commit until the user has seen the change.** Show the diff and wait. When explicitly told
   to commit and open a PR, do the whole sequence without stopping in the middle.
3. **Merge `main` in before opening the PR**, and again before pushing if `main` moved. This repo
   releases every other day; a branch is stale fast.
4. **Run `pwsh scripts/cleanup-code.ps1 -Changed` before `gh pr create`** — automatically, without
   being asked. Style drift is this repo's most repeated CI failure.
5. **The snapshot bot commits to your PR head.** CI regenerates `docs/snapshots/*` and pushes them
   onto the branch. Pull and reconcile before pushing again; never force-push over it.
6. **Push back when a request violates a convention here**, and say which one. A defence of a
   deliberate exception is welcome; silently complying is not.
7. **When the user corrects produced work in-session** — a convention violation, approach pushback, a
   behaviour fix, or a reply choosing which review findings to apply — append a one-line `review` entry
   to [`docs/process-journal.md`](docs/process-journal.md) **in the commit that carries the fix**.
   Format and triggers: that file's header. It is the only durable record; transcripts age out and
   assistant memory does not leave one machine.
8. **Verify before claiming done.** Say what you actually ran — build, tests, cleanup, the
   `devhost-smoke` skill, `gh pr checks` after each push. A green local run says nothing about CI, and
   a DevHost change is shown working, not asserted.
9. **After a release, bump the examples, template and libraries** ([`docs/releasing.md`](docs/releasing.md)).
   A release without its bump leaves the next commit shipping inconsistent references.

## Repository Structure

```
Vion.Dale.Sdk/              Core SDK — LogicBlockBase, attributes, introspection
Vion.Dale.Sdk.Generators/   Roslyn source generator + analyzers (shipped inside Vion.Dale.Sdk)
Vion.Dale.Sdk.Http/         HTTP client extensions for logic blocks
Vion.Dale.Sdk.Modbus.*/     Modbus Core/Tcp/Rtu protocol bindings
Vion.Dale.Sdk.DigitalIo/    Digital I/O contract abstractions
Vion.Dale.Sdk.AnalogIo/     Analog I/O contract abstractions
Vion.Dale.Sdk.TestKit/      Test helpers for logic block unit testing
Vion.Dale.Sdk.*.TestKit/    I/O-specific test helpers (DigitalIo, AnalogIo, Modbus.Rtu, Modbus.Tcp)
Vion.Dale.ProtoActor/       Proto.Actor integration (net10.0)
Vion.Dale.Plugin/           Plugin AssemblyLoadContext (net10.0) — shared by the runtime + LogicBlockParser
Vion.Dale.LogicBlockParser/ Assembly introspector — bundled into Vion.Dale.Sdk as a tool
Vion.Dale.DevHost/          Local development host (+ headless IDevHostControl surface for CI/agents — RFC 0003). After changes here / Web / scenario runner / stepping, verify with the `devhost-smoke` skill.
Vion.Dale.DevHost.Web/      Web UI for DevHost (static SPA assets) + HTTP control endpoints
Vion.Dale.DevHost.SmokeHost/ Project-referencing smoke fixture — synthetic blocks (value shapes, HAL, wiring) + topologies + scenarios; boots a real server for the `devhost-smoke` skill's live-UI tier
Vion.Dale.Cli/              CLI tool (dotnet global tool `dale`) — see Vion.Dale.Cli/CLAUDE.md
Vion.Dale.Cli.Test/         CLI unit tests
templates/                  Project template bundled as content inside Vion.Dale.Cli (source used by `dale new`)
examples/                   Example LogicBlock libraries — in Vion.Dale.Sdk.sln, referencing published packages
libraries/                  First-party LogicBlock libraries shipped from here (Vion.Diagnostics)
docs/                       Conventions, RFCs, migrations, snapshots, the process journal/metrics and retro notes
scripts/                    Build / versioning / docs generation scripts
```

## Key Concepts

**LogicBlock**: an actor-based computation unit. Extends `LogicBlockBase`. Has service properties (observable state), measuring points (read-only metrics), timers, and communicates with other blocks via interfaces and contracts.

**Service properties vs measuring points — and dual-annotation (gotcha)**: a `[ServiceProperty]` (observable state) and a `[ServiceMeasuringPoint]` (charted time series) can both be declared on the **same C# property** — common for telemetry (e.g. grid-meter power surfaced as live state *and* a chart). They are **independent**: each publishes to its own retained MQTT stream (`…/property/state` vs `…/measuring-point/state`), is throttled/deadbanded separately (RFC 0004), and a single value change raises **both** `ServicePropertyValueChanged` and `ServiceMeasuringPointValueChanged`. Consequence for anyone touching the emission/publish pipeline: **never key per-member state by `(service, member)` name alone** — that collides the two streams and one silently suppresses the other (this caused a measuring-points-go-dark regression). `LogicBlockBase` keeps **separate per-stream collections** (`_servicePropertyThrottlers` / `_measuringPointThrottlers`); keep that separation.

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
- Push tag `vX.Y.Z` → CI publishes `X.Y.Z` to the private feed **and** nuget.org.

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
- Code cleanup: **ReSharper `cleanupcode`** with the `Custom: Full Cleanup (excl. optimize usings)` profile (JetBrains CLI) — see the cleanup note below. Do NOT use the `Built-in: Reformat Code` profile.
- Allman brace style throughout.
- Targets: `netstandard2.1` for SDK runtime + I/O / protocol contracts + Http (cross-platform plugin compatibility), `netstandard2.0` for source generator, `net10.0` for TestKits / CLI / DevHost / ProtoActor / Plugin / LogicBlockParser / tests.

Code style is **ReSharper cleanupcode** with the `Custom: Full Cleanup (excl. optimize usings)`
profile in `Vion.Dale.Sdk.sln.DotSettings` — the same profile ReSharper/Rider apply on save. The
single source of truth is **`scripts/cleanup-code.ps1`**: it restores the pinned `jb` tool
(`.config/dotnet-tools.json`) and runs the exact cleanup. CI runs the same script with `-Verify`
(fails on drift) via the shared `VION-IoT/shared-workflows` gate: `.github/workflows/publish.yml`
calls `publish-nuget.yml` with `gate: true`, which runs `scripts/cleanup-code.ps1 -Verify` (the
`dotnet-gate` composite) before packing — so local and CI can't diverge.

**Before opening a PR: run `pwsh scripts/cleanup-code.ps1 -Changed` (or the `/cleanup` slash command),
review `git diff`, and commit any changes** — this keeps the CI style gate from failing the PR.
`-Changed` scopes the cleanup to the `.cs` your branch touched — including files you have not
`git add`ed yet — and skips in ~0.5s when none did (the fast dev-loop path); the full-solution run
(drop `-Changed`) and the CI `-Verify` gate stay the authoritative backstop. **Agents: do this
automatically before `gh pr create`.** Do NOT run cleanup with `--profile="Built-in: Reformat Code"` —
it differs from the DotSettings profile and fights cleanup-on-save.

**Formatter escape hatch:** for the rare span where `cleanupcode` formats inconsistently
across OSes (local vs the Linux CI runner) or where you intentionally hand-format (e.g. an
aligned table), wrap it in `// @formatter:off` / `// @formatter:on` with a short reason
comment — `cleanupcode` honors these on every OS, so the style gate stays green. Use it
sparingly and locally, never to opt a whole file out.

## Related Repos

- `vion-iot/vion-contracts` — Shared DTOs (MQTT topics, payloads, FlatBuffers schemas, introspection models). Published as `Vion.Contracts`.
- `vion-iot/dale` (private) — Vion Dale runtime. Consumes `Vion.Dale.Sdk`, `Vion.Dale.ProtoActor`, and `Vion.Dale.Plugin` as NuGet packages.
- `documentation` — Public docs site (API reference auto-generated from this repo).
- `logic-block-libraries` (ecocoach org, private) — the first real consumer of this SDK, and the
  origin of most features and fixes here.

## Feedback intake

Consumer and customer feedback on the SDK is curated **by the maintainer** into Jira items under the
**"Dale SDK Feedback" epic (VION-62)** in the VION project, label `dale-sdk` — consumers do not file
items. Turn a report (mail, chat, logs, an RFC-shaped proposal) into an item or a recorded dismissal
with the `dale-sdk-feedback` skill ([`.claude/skills/dale-sdk-feedback/`](.claude/skills/dale-sdk-feedback/SKILL.md));
it verifies against this repo before drafting, keeps items short, and never decides the fix. Picking,
briefing and closing items is `/triage` + `/fix` in the architecture repo. When a release resolves an
item, `/fix`'s closing comment is what the maintainer relays to the consumer — they don't read Jira.
(Until 2026-08 the intake channel was a field log in `logic-block-libraries`, entries `DF-nn` — retired;
its history is in that repo's git, and the numbers survive as `Origin` lines on the migrated items.)

## How this file stays true

One owner per rule: where a convention doc owns the subject, the rule lives there and this file links
to it rather than restating it. Corrections and process friction go to
[`docs/process-journal.md`](docs/process-journal.md) as they happen; a periodic retro
([`docs/retro/`](docs/retro/)) reads the journal and promotes recurrences down the enforcement ladder —
**a DALE analyzer or a CI gate > a `/vion-code-review` check > prose here**. This repo has the analyzer
rung the sibling repos lack, and it is the cheapest of all: a diagnostic fires in the consumer's build,
not only in ours. A rule that exists only as prose and keeps drawing the same correction is an
enforcement gap, not a documentation gap.
