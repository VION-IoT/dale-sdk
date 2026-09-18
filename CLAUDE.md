> **Cross-repo work**: this repo is part of the VION platform.
> Architecture state, decisions, and cross-repo specs live in [`../architecture`](https://github.com/VION-IoT/architecture).
> Clone it: `git clone git@github.com:VION-IoT/architecture.git ../architecture`
> Before planning a feature with scope ≥ 2 repos, read the relevant `architecture/systems/*.md`
> and run `/spec <slug> <repos>` from the architecture repo.
> Cross-repo work is dispatched with the `vion-dispatch` plugin
> ([mechanics](https://github.com/VION-IoT/architecture/blob/main/plugins/vion-dispatch/README.md),
> [VION procedure](https://github.com/VION-IoT/architecture/blob/main/runbooks/session-orchestration.md)).
> A session dispatched into this repo ends with `/vion-dispatch:report`.

# Vion Dale SDK

The Vion Dale SDK is an IoT runtime SDK for actor-based logic blocks. This repository is **source-available** (Apache 2.0) but closed to external contributions — see [CONTRIBUTING.md](CONTRIBUTING.md).

The private Vion Dale runtime lives in a separate repository and consumes the packages published from here as NuGet packages.

This file is the always-loaded contract. Detail lives in the convention docs below, loaded when you do
the work they govern — **read the linked doc before doing the matching work, and follow it.**

## Read before you write

| When you're… | Read |
| --- | --- |
| adding or changing an attribute, a public type or member, an analyzer, or anything reaching the introspection JSON | [`docs/sdk-surface-conventions.md`](docs/sdk-surface-conventions.md) — surface minimalism, XML-doc style, delete-don't-deprecate, the analyzer obligation and the **Metalama blind spot**, PublicApi snapshots |
| writing or modifying a test | [`docs/testing-conventions.md`](docs/testing-conventions.md) — MSTest inside / xunit.v3 outside, analyzer tests vs the real compilation, packed-artifact verification, determinism, and the authoring discipline (§9–17: behavior tables, prove-red, naming, Moq, async) |
| changing specified behavior, or dispatching an implementing session | [`docs/spec-process.md`](docs/spec-process.md) — the spec corpus (`docs/specs/`), the change-doc lane (`docs/changes/`), the three lanes work runs in and lane 3's recipe, the launcher recipe, the gates |
| writing or reworking an inline comment | [`docs/comment-conventions.md`](docs/comment-conventions.md) — why not what, a comment is a claim, no history/tickets, name the concrete failure |
| touching `Vion.Dale.DevHost*`, the scenario runner, or stepping | [`docs/specs/devhost-control.md`](docs/specs/devhost-control.md) — what the host guarantees in process, over HTTP and on stdout; and [`docs/devhost-conventions.md`](docs/devhost-conventions.md) — the demonstrate-don't-assert verify loop, clock modes, the four scenario-step definition sites; the SPA's own contract is [`Vion.Dale.DevHost.Web/CLAUDE.md`](Vion.Dale.DevHost.Web/CLAUDE.md) |
| writing a simulator block or a provider face (the peer a bench needs on the far side of a contract) | [`docs/simulator-authoring.md`](docs/simulator-authoring.md) — provider faces, the ideal-I/O echo recipe, when to model the device instead |
| renaming anything that reaches introspection (service, member, contract, interface, enum member, enum/struct type, PackageId) | [`docs/specs/introspection.md`](docs/specs/introspection.md) — the document, and which C# name keys each element; [`docs/identifier-stability.md`](docs/identifier-stability.md) is the short practical companion |
| touching any `Vion.Dale.Sdk.*TestKit` package or one of their test projects | [`docs/specs/testkit.md`](docs/specs/testkit.md) — what the six kits guarantee a block author: the builder's phases and knobs, the verification family and its `Times` default, the raise helpers, virtual time's two drivers, the Modbus and HTTP fakes, and what a kit costs the SDK |
| touching `Vion.Dale.Sdk.Modbus.*` or either Modbus example | the `modbus-smoke` skill ([`.claude/skills/modbus-smoke/`](.claude/skills/modbus-smoke/SKILL.md)) — the link policy over a real socket pair on `127.0.0.1:15020`; real clock, ~1 min |
| adding a CLI command | [`Vion.Dale.Cli/CLAUDE.md`](Vion.Dale.Cli/CLAUDE.md) |
| cutting a release, or bumping examples after one | [`docs/releasing.md`](docs/releasing.md) |
| reviewing a change | `/vion-git:review` — it reads [`docs/review-checks.md`](docs/review-checks.md), this repo's named checks |

**Before writing new code, read similar existing files** in the same area and replicate their
structure. Do not invent new patterns; name the precedent you followed.

**Design docs are change docs in [`docs/changes/`](docs/changes/)** per
[`docs/spec-process.md`](docs/spec-process.md); the current-truth spec corpus is
[`docs/specs/`](docs/specs/). There is no `docs/rfcs/`, and nothing left to add one to.
`docs/superpowers/` is **gitignored** (`.gitignore:301`) per architecture decision 0011, so anything
a planning skill writes there cannot be committed — `git check-ignore` is the tell; redirect anything
meant to last to `docs/changes/`. Cross-repo specs live in `../architecture/specs/`, never here.

## Working agreement

### Lanes

At the start of a task, answer two questions out loud: is the change local? is a design point open?

- **Fix-sized** — local, nothing open: branch, commit, review, pull request. No document. A change
  that turns out not to be local stops and says so: it is feature-sized.
- **Feature-sized** — a change doc first, in `docs/changes/`. Ratified before code when a question
  in it is open. Archived in the pull request that lands it.

### STOPs

- A STOP is named up front — by the brief, an open question in the change doc, or the lane answer —
  and no other. With none named, human review is on the pull request.
- A STOP is a `partial` REPORT with a question in it.
- A decision nobody named is surfaced, not taken. A hedge in a brief is a STOP when it fails.
- Scope does not widen on its own: a design or naming question gets options and changes nothing
  until the human chooses; work nobody asked for is proposed, not produced.
- A question from the human is a question, not an instruction.
- Anything committed after a `done` REPORT needs a new REPORT.
- A request that breaks a convention of this repo is pushed back on before complying, by name.
- Verification only a human can do is not a STOP: write it as "not run, routes to a human" under the
  pull request's Verification.

### Communication

- Say what was run, not that it worked.
- A claim a decision rests on names its evidence: a command, a file and line, or that it is inferred.
- Promise no notification that cannot be subscribed to.

### Never

- Push to or commit on the default branch.
- Force-push.
- Delete a remote branch.
- Merge a pull request.
- Write to Jira without saying so first.
- Paste a secret into chat.

## Skills in this repo

| moment | skill |
|---|---|
| starting work on a change | `/vion-git:branch` |
| a unit of work lands — a task, a criterion, a fixed review finding | `/vion-git:commit` |
| a correction to produced work, tooling that fought or false-passed, upstream that was wrong, a settled point, a grumble | `/vion-improve:journal` |
| editing a file written for the agent — `CLAUDE.md`, a command, a skill, a convention doc, settings | `/vion-improve:harness` |
| the branch is ready for a pull request | `/vion-git:pr` |
| a retro is due, by the count and age `retro` states | `/vion-improve:retro` |

### Lanes in this repo

The block's two lanes map onto the three of [`docs/spec-process.md`](docs/spec-process.md) § Lanes:

- **Fix-sized** is lane 1: the pull request carries the edit to the touched [`docs/specs/`](docs/specs/)
  page in the same commit set.
- **Feature-sized** is lane 2: the change doc is written from `docs/changes/_template.md`, its
  Spec-delta section names the ids, the tests cite them, and the doc is distilled and archived.
- **Lane 3**, a whole contract brought to current truth in one round, is the spec process's own.

Every area carries a traced page, so a change to specified behaviour edits a page in every lane. Kind
is not lane: the branch prefix names what the change is for release notes, the lane names its process
weight.

### Pre-PR obligations

1. On `*.cs`: `/cleanup`; commit what it changes.
2. `/check`, with `-Build` and `-Test` when the change touches C#.

The pull request body follows [`.github/pull_request_template.md`](.github/pull_request_template.md),
which adds `## Draft items`, `## Spec ids touched` and `## Gates` to the base sections.

### Reader depth

Beyond `/vion-git:pr`'s defaults:

- contract: `docs/specs/**`
- generated: `docs/snapshots/**`

### Parallel sessions

The main checkout, `C:\_gh\dale-sdk`, stays on `main`. Every branch — a dispatched worker's or a
session's own — lives in the sibling worktree `../dale-sdk-<key>`, cut from `origin/main`, never nested
inside a checkout: `/vion-git:branch` creates it or reuses it. The `vion-git` hook denies a write the
main checkout's git does not ignore, and a branch or commit made there.

A new worktree gets nothing copied into it, so it lacks the main checkout's ignored local state:

- `Vion.Dale.DevHost.Web/Properties/launchSettings.json`, which binds ports 54815 and 54816 — a
  machine singleton. Running the DevHost web server from that project stays in the main checkout.
- `.claude/settings.local.json`, the permission mode. A dispatched session's launcher carries it
  over; a worktree opened by hand starts without it.

Build, test, `/cleanup` and `/check` write only inside the checkout they run in, so they work in any
worktree. The `modbus-smoke` skill binds `127.0.0.1:15020`: one run at a time on this machine,
whichever checkout it runs from.

### The snapshot bot

CI regenerates `docs/snapshots/*` and commits them onto the pull request head. That commit is
accepted; pull before the next push.

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
Vion.Dale.DevHost/          Local development host (+ headless IDevHostControl surface for CI/agents). After changes here / Web / scenario runner / stepping, verify with the `devhost-smoke` skill.
Vion.Dale.DevHost.Web/      Web UI for DevHost (static SPA assets) + HTTP control endpoints
Vion.Dale.DevHost.SmokeHost/ Project-referencing smoke fixture — synthetic blocks (value shapes, HAL, wiring) + topologies + scenarios; boots a real server for the `devhost-smoke` skill's live-UI tier
Vion.Dale.Cli/              CLI tool (dotnet global tool `dale`) — see Vion.Dale.Cli/CLAUDE.md
Vion.Dale.Cli.Test/         CLI unit tests
templates/                  Project template bundled as content inside Vion.Dale.Cli (source used by `dale new`)
examples/                   Example LogicBlock libraries — in Vion.Dale.Sdk.sln, referencing published packages
libraries/                  First-party LogicBlock libraries shipped from here (Vion.Diagnostics)
docs/                       Conventions, the spec corpus (specs/) + change docs (changes/), migrations, snapshots, the review checks, the process journal and retro notes
scripts/                    Build / versioning / docs generation scripts
```

## Key Concepts

**LogicBlock**: an actor-based computation unit. Extends `LogicBlockBase`. Has service properties (observable state), measuring points (read-only metrics), timers, and communicates with other blocks via interfaces and contracts.

**Service properties vs measuring points (gotcha)**: a `[ServiceProperty]` and a `[ServiceMeasuringPoint]` may sit on the **same C# property**, and the two streams are then independent — separate retained topics, separate knobs, both change events ([`docs/specs/emission.md`](docs/specs/emission.md)). So, in the emission pipeline: never key per-member state by `(service, member)` name alone, and never read a member's knobs off a `PropertyInfo` without being told which stream is being served. Either one collides the two streams, and the measuring point silently goes dark.

**Contracts**: define hardware I/O bindings (Modbus registers, digital pins, etc.) and inter-block messaging (commands, request-response). Shared DTOs live in `Vion.Contracts` (separate repo).

**Introspection**: `Vion.Dale.LogicBlockParser` loads a built assembly, runs `LogicBlockIntrospection`, and outputs full metadata as JSON. This is the source of truth — not source code parsing.

## Building

```bash
dotnet build Vion.Dale.Sdk.sln
dotnet test Vion.Dale.Sdk.sln
```

Examples (`examples/*`) and the inner template projects (`templates/vion-iot-library/VionIotLibraryTemplate*`) are in `Vion.Dale.Sdk.sln` and reference the SDK by `PackageReference`: their checked-in versions must match a published `Vion.Dale.*` package, preview or stable, and [`docs/releasing.md`](docs/releasing.md) owns bumping them. The template content is bundled into `Vion.Dale.Cli`, where a pack-time target rewrites its `Vion.Dale.*` versions to the CLI's own `$(Version)`, so `dale new` always matches the installed CLI.

## Dale CLI

The CLI (`dale`, installed with `dotnet tool install -g Vion.Dale.Cli`) is the primary developer interface for consumers of the SDK. Its commands are [`docs/snapshots/cli-help-snapshot.txt`](docs/snapshots/cli-help-snapshot.txt), which CI regenerates; architecture, patterns and how to add one are [Vion.Dale.Cli/CLAUDE.md](Vion.Dale.Cli/CLAUDE.md).

## Versioning & Releases

Versions are driven by git tags; no `<Version>` in any SDK `.csproj`. A push to `main` that builds publishes `0.0.0-ci.{run_number}` to the private Azure DevOps feed; a `vX.Y.Z` tag publishes `X.Y.Z` there **and** to nuget.org. The full flow is [README.md#releases](README.md#releases); cutting a release, and bumping the example and template refs after it, is [`docs/releasing.md`](docs/releasing.md).

## CI/CD

Single GitHub Actions workflow: [.github/workflows/publish.yml](.github/workflows/publish.yml). Builds, tests, packs, pushes to the private Azure DevOps feed on every push that builds (its "Scope the run" step decides), and additionally pushes to nuget.org on tags.

## Auth (CLI consumer-facing)

- **Interactive (developers)**: `dale login` — PKCE browser flow against Keycloak (`dale-cli` public client — external identity, do not rename).
- **CI/CD**: `--client-id <id> --client-secret <secret>` — client-credentials flow with service accounts.
- **Environments**: `production` (api.vion.swiss) and `test` (api.test.vion.swiss), configurable via `dale config set-environment`.
- **Integrator context**: auto-resolved from `/me` if the user belongs to one integrator, otherwise selected during `dale login` or via `--integrator-id`.

Env vars: `DALE_CLIENT_ID`, `DALE_CLIENT_SECRET`, `DALE_INTEGRATOR_ID` (all user-facing, keep the `DALE_` prefix).

## Code Style

- C# with `ImplicitUsings: false` (all usings explicit).
- `Nullable: enabled`.
- Code cleanup: **ReSharper `cleanupcode`** with the `Custom: Full Cleanup (excl. optimize usings)` profile from `Vion.Dale.Sdk.sln.DotSettings`, the one Rider applies on save. `/cleanup` (`scripts/cleanup-code.ps1`) is the single source of truth and what CI's `style` job verifies with `-Verify`; never the `Built-in: Reformat Code` profile, which fights cleanup-on-save.
- Allman brace style throughout.
- Targets: `netstandard2.1` for SDK runtime + I/O / protocol contracts + Http (cross-platform plugin compatibility), `netstandard2.0` for source generator, `net10.0` for TestKits / CLI / DevHost / ProtoActor / Plugin / LogicBlockParser / tests.

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
An item migrated from the retired `logic-block-libraries` field log carries its `DF-nn` as an `Origin` line.

## How this file stays true

Harness budgets, in bytes of the committed file: this file 10 kB, a `.claude/commands/*.md` 12 kB,
any other `.claude/**/*.md` 6 kB; `docs/review-checks.md` 15 checks. No gate reads these numbers —
enforced by hand.
