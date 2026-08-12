# Process journal

Friction log for how this repo gets built — review corrections and process creaks, one line each,
written **the moment they happen**. Not a work log: what shipped is in git and the PRs. This file
records where the *process* creaked. Modeled on the architecture repo's `docs/workflow-journal.md` and
the equivalents in dale, dashboard and cloud-api.

Why: corrections and friction are felt in the moment and remembered nowhere. Without this file they
surface only when someone has accumulated enough irritation to raise them all at once — by which point
the specifics are gone and only the annoyance is left. One line, written when it happens, keeps the
specifics.

**Transcripts are not a fallback.** They age out of `~/.claude/projects/` after weeks;
[retro-0](retro/2026-08-12-review-mining-round.md) mined them once and that window is closed — the
oldest session it could still read started 2026-06-12, so everything before the Modbus-TCP-server work
was already gone. Assistant memory is not a fallback either: it is machine-local, so a colleague, a CI
runner or a second workstation never sees it, and it goes stale silently (retro-0 found two of this
repo's memory entries already false).

## Format

```
YYYY-MM-DD · <where> · <topic, PR #, or —> · <what happened, one line>
```

`where` is one of:

- `review` — the user corrected produced work in-session (**the most important line type**)
- `brief` — a brief (from the architecture repo or a prior session) was wrong, ambiguous, or missing
  something; also where a `Friction:` field would have gone if the work had not been done locally
- `gate` — the CI style gate, build, test gate or snapshot bot false-fails, false-passes, or fights the work
- `consumer` — friction in the SDK-user feedback loop (a `DF-nn` entry, an adoption blocker, a
  workaround a consumer had to keep)
- `release` — the release / example-bump / upload lane creaked
- `infra` — CI runner, package feeds, credentials
- `agent` — agent behavior or process
- `manual` — human grumble

Append at the bottom, newest last. Naming the taxonomy check a correction maps to (`D1`…`D10`, see
[`.claude/commands/vion-code-review.md`](../.claude/commands/vion-code-review.md) § 5) is worth the four
characters — retro-1's open question is which of them actually fire.

Two markers earn their keystrokes because [`process-metrics.md`](process-metrics.md) counts them and
nothing else can produce them:

- **`(second ask)`** on a `review` line the user has now had to make **twice**. Each one is a standing
  candidate for promotion to an analyzer, a gate, or a convention rule.
- **`(escape)`** on a `review` line for work that had already passed `/vion-code-review` — the review
  ran and missed it. This is the loop-quality signal; it is the number that should fall.

Both go at the end of the line, before the D-number.

**Record what happened, not what should change** — the fix is the retro's job, and pre-judging it here
loses the evidence.

**Qualitative one-liners only.** Anything countable afterwards from durable artifacts — PRs merged,
corrections per PR, gate catches, durations — is **not** journalled; the retro counts it into
[`process-metrics.md`](process-metrics.md) from git, `gh` and this file. Journal what was *felt*, when
it was felt.

## Entries

<!-- retro-0 marker: everything below this line is unread by a retro. Move the marker when a round reads it. -->

2026-08-12 · gate · cleanup-code.ps1 · `-Changed` and the drift check both saw tracked files only, so a brand-new `.cs` was neither cleaned nor reported — it surfaced as CI style drift instead. Fixed in this round rather than documented (enforcement ladder).
2026-08-12 · brief · — · briefs to this repo have been asserting `IDevHostControl` is `[PublicApi]` and predicting snapshot churn from DevHost changes. It carries no such attribute and `Vion.Dale.DevHost` declares no `PublicApiNamespace`, so no DevHost change can move the manifest. One implementing session lost a step regenerating a snapshot that could not drift.
2026-08-12 · review · — · `ScenarioStep`'s XML summary still lists `digitalInput`/`analogInput`, deleted by RFC 0010. Found while verifying doc claims for retro-0; filed rather than folded into the substrate PR. (D2)
2026-08-12 · review · retro-0 · The user asked for a correctness pass over the freshly-written convention docs; it found eight wrong claims in text that read as confident — a miscounted analyzer registry, a wrong `Diagnostic.Create` overload, two overstated frequency counts, a wrong assembly breakdown, and an over-generalised `Identifier=` decoupling rule. Every one came from reasoning about the code instead of reading it. This is D10 firing on the substrate itself. (D10)
2026-08-12 · review · #128 · First `/vion-code-review` run in this repo, on the PR that installs it. Found the PublicApi §8 mechanism stated **backwards** — `[PublicApi]` types are the manifest gate, `[assembly: PublicApiNamespace]` only drives grouping and DALE014/015 — with `Modbus.Core` (12 marked types, no assembly attribute, in the manifest) as the counterexample sitting inside my own list. Also two counts still wrong after the correctness pass above: MSTest 15→16 (`Vion.Dale.Cli.Test` uses the split packages) and 519→520 `var(--` (a token with a fallback). (second ask) (D10)
2026-08-12 · review · #128 · Same run: `process-metrics.md` defined `second asks` and `escapes` as journal markers that `process-journal.md` never documented, so two columns could only have been filled from memory — the failure the journal exists to prevent. Markers now defined at the site that has to produce them. (D9)
2026-08-12 · infra · #128 · CI `pack` broke on a toolchain shift, not on the change: with no `global.json`, the runner floated to .NET SDK 10.0.400, whose `Microsoft.CodeAnalysis.Razor.Compiler.dll` needs Roslyn 5.9 while Metalama Compiler bundles 5.5 (`LAMA0625`). Fails at build, before test and style even run, on `Vion.Dale.DevHost.Web`. Blocks every PR; main last built green 2026-08-11.
2026-08-12 · infra · #129 · Resolved. Bumping Metalama was the obvious move and it is a dead end — 2026.1.18→2026.1.23 moves the bundled Roslyn only 5.5→5.6, still short of 5.9, and no current release supports it. The actual cause was `Vion.Dale.DevHost.Web` using `Microsoft.NET.Sdk.Web` with zero Razor files: the Web SDK fed the Razor analyzer to a project that had no use for it, and that is the only place it met Metalama. Now plain `Microsoft.NET.Sdk` + a `FrameworkReference`. Side effects, both verified: the nupkg stopped shipping a duplicate `staticwebassets/` copy of the SPA (823→426 KB, nothing reads it — `WebHostService` serves only via `EmbeddedFileProvider`), and the two AspNetCore analyzers no longer apply here. **A repo with no `global.json` has an unpinned compiler; this will recur.**
2026-08-12 · gate · — · The `devhost-smoke` SKILL.md says "expect 8 tests passed" for Tier 1; it is 19. The number has drifted as the fixture grew and now reads as a failure signal when it is not.
