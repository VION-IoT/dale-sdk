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

Detail lives in the convention docs below, loaded when you do the work they govern — **read the linked doc before doing the matching work, and follow it.**

## Read before you write

| When you're… | Read |
| --- | --- |
| adding or changing an attribute, a public type or member, an analyzer, or anything reaching the introspection JSON | [`docs/sdk-surface-conventions.md`](docs/sdk-surface-conventions.md) — including the analyzer obligation and the **Metalama blind spot** |
| touching the emission pipeline, or a member carrying both `[ServiceProperty]` and `[ServiceMeasuringPoint]` | [`docs/specs/emission.md`](docs/specs/emission.md) — the two streams are independent, so never key per-member state by `(service, member)` name alone, and never read a member's knobs off a `PropertyInfo` without being told which stream is served |
| writing or modifying a test | [`docs/testing-conventions.md`](docs/testing-conventions.md) |
| changing specified behavior, or dispatching an implementing session | [`docs/spec-process.md`](docs/spec-process.md) — the corpus, the lanes, what a pass here adds, the gates |
| writing or reworking an inline comment | [`docs/comment-conventions.md`](docs/comment-conventions.md) |
| touching `Vion.Dale.DevHost*`, the scenario runner, or stepping | [`docs/specs/devhost-control.md`](docs/specs/devhost-control.md) and [`docs/devhost-conventions.md`](docs/devhost-conventions.md); the SPA's own contract is [`Vion.Dale.DevHost.Web/CLAUDE.md`](Vion.Dale.DevHost.Web/CLAUDE.md); after a change here, the `devhost-smoke` skill verifies it |
| writing a simulator block or a provider face (the peer a bench needs on the far side of a contract) | [`docs/simulator-authoring.md`](docs/simulator-authoring.md) |
| renaming anything that reaches introspection (service, member, contract, interface, enum member, enum/struct type, PackageId) | [`docs/specs/introspection.md`](docs/specs/introspection.md); [`docs/identifier-stability.md`](docs/identifier-stability.md) is the short practical companion |
| touching any `Vion.Dale.Sdk.*TestKit` package or one of their test projects | [`docs/specs/testkit.md`](docs/specs/testkit.md) |
| touching `Vion.Dale.Sdk.Modbus.*` or either Modbus example | the `modbus-smoke` skill ([`.claude/skills/modbus-smoke/`](.claude/skills/modbus-smoke/SKILL.md)) |
| adding a CLI command | [`Vion.Dale.Cli/CLAUDE.md`](Vion.Dale.Cli/CLAUDE.md) |
| cutting a release, or bumping examples after one | [`docs/releasing.md`](docs/releasing.md) |
| reviewing a change | `/vion-git:review` — it reads [`docs/review-checks.md`](docs/review-checks.md), this repo's named checks |

**Before writing new code, read similar existing files** in the same area and replicate their
structure. Do not invent new patterns; name the precedent you followed.

**Design docs are change docs in [`docs/changes/`](docs/changes/)** per
[`docs/spec-process.md`](docs/spec-process.md); the current-truth spec corpus is
[`docs/specs/`](docs/specs/). `docs/superpowers/` is **gitignored** (`.gitignore:301`), so anything a
planning skill writes there cannot be committed; redirect anything meant to last to `docs/changes/`.
Cross-repo specs live in `../architecture/specs/`, never here.

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
| work changes specified behaviour — a criterion fixed, behaviour new or reshaped | `/vion-sdd:change` |
| a whole area is brought to current truth | `/vion-sdd:pass` |

### Lanes in this repo

The block's **Fix-sized** is lane 1 and **Feature-sized** is lane 2 of
[`docs/spec-process.md`](docs/spec-process.md) § Lanes, which owns all three and what each obliges;
lane 3 is `/vion-sdd:pass`, whose roles are: dispatch `/vion-dispatch:spawn`, report
`/vion-dispatch:report`, ingest `/vion-dispatch:ingest`, git flow `/vion-git:branch`, `commit` and `pr`.
Kind is not lane: the branch prefix names what the change is for release notes, the lane names its
process weight.

### Pre-PR obligations

1. On `*.cs`, `*.csproj`, `Vion.Dale.Sdk.sln`, `Vion.Dale.Sdk.sln.DotSettings`, `Directory.Build.props`,
   `Directory.Build.targets`, `.config/dotnet-tools.json`: `/cleanup`; commit what it changes.
2. On `*.cs`, `*.csproj`, `Vion.Dale.Sdk.sln`, `Directory.Build.props`, `Directory.Build.targets`,
   `.config/dotnet-tools.json`: `dotnet build Vion.Dale.Sdk.sln`, then `dotnet test Vion.Dale.Sdk.sln`.
3. `/check`.

The pull request body follows [`.github/pull_request_template.md`](.github/pull_request_template.md),
which adds `## Draft items`, `## Spec ids touched` and `## Gates` to the base sections.

### Reader depth

Beyond `/vion-git:pr`'s defaults:

- contract: `docs/specs/**`
- harness: `docs/spec-process.md`, `docs/*-conventions.md`, `docs/releasing.md`
- generated: `docs/snapshots/**`

### Parallel sessions

The main checkout, `C:\_gh\dale-sdk`, stays on `main`; a branch lives in `../dale-sdk-<key>`
(`/vion-git:branch`).

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

## Feedback intake

Consumer and customer feedback on the SDK is curated **by the maintainer** into the **"Dale SDK
Feedback" epic (VION-62)** in the VION project, label `dale-sdk` — consumers do not file items; the
first of them, `logic-block-libraries` (ecocoach org, private), is where most features and fixes
here come from. Turning a report into an item or a recorded dismissal is the `dale-sdk-feedback`
skill ([`.claude/skills/dale-sdk-feedback/`](.claude/skills/dale-sdk-feedback/SKILL.md)).

## How this file stays true

Harness budgets, in bytes of the committed file: this file 10 kB, a `.claude/commands/*.md` 12 kB,
any other `.claude/**/*.md` 6 kB, `docs/testing-conventions.md` 31 kB; `docs/review-checks.md` 15
checks. No gate reads these numbers — enforced by hand.
