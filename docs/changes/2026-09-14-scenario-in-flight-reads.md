---
slug: scenario-in-flight-reads
status: in-flight          # proposed | in-flight | parked | archived
blocked-on: none           # for parked docs: what's blocking + ref
areas: CTRL, SCEN
author: jonasbertsch
created: 2026-09-14
updated: 2026-09-14
supersedes: none           # path of a superseded change doc, or none
---

# A scenario's read sees what start published and waits on what a drive changes

> Change doc — one in-flight change. The `Spec delta` below is distilled into the current-truth
> pages under `docs/specs/` in the implementation PR, then this doc is archived
> (`pwsh scripts/spec-change.ps1 archive <slug>`). Process: `docs/spec-process.md`. Never put
> change narrative inline in a spec page — it lives here.

## At a glance

### Summary

Two CI flakes have one shape: a scenario reads a value still travelling through the actor system.
`ExpectStepShould.JudgeStructFieldTargetAgainstItsScalarLeaf` read a seeded member right after
`StartAsync` and got `null` (runs 34820511458, 34824065171 — the second the `v0.13.0` tag). The
committed `io-control` scenario read a driven analog input one step after waiting on a *different*
input and got `0` (run 34168395206). Start now returns only once every value the blocks published
while starting is readable; drives stay fire-and-forget, the scenarios and tests that relied on luck
wait explicitly, and `dale scenario validate` warns on the shape.

### Spec implications

`devhost-control.md` § Starting a host gains `AC-CTRL-002.10` — what start guarantees a first read —
and the failure half of `AC-CTRL-002.4` widens to cover it. `AC-CTRL-009.5` (a drive completes before
the block has seen the value) is unchanged and is now cited by the new warning. `scenarios.md` § The
offline validators gains `AC-SCEN-015.11`, the drive-then-read warning, and its prose names what
counts as a wait. No runner behaviour changes: `expect` stays a point-in-time read.

### Decisions

- `D1` — Start waits on a **barrier through the two value handlers**, not on a per-member cache check —
  it proves every publication the blocks sent while starting has been cached without the host
  having to know which members publish, so a member whose getter throws cannot fail or slow start.
- `D2` — The barrier's bound is the existing real-time `DevHostBudgets.StartAcknowledgement`, and its
  elapse fails start with a `TimeoutException` shaped like the start acknowledgement's — one budget,
  one failure vocabulary for the whole start, no new knob.
- `D3` — For the warning, **a wait is one that targets the member the read reads**: a `waitUntil` on
  it, a `settle` whose targets include it, or an `advance` — a wait on another member is not, because
  that is exactly `io-control`'s flake. Paths compare as written, so two spellings of one member warn;
  the remedy is spelling them alike, and resolving would make the check depend on the export.
- `D4` — The warning covers `expect` (its property and a relational comparand's path) and a drive in
  `setup` as well as in `steps`; it never fails a file.

### Reviewer's questions

1. (a) ratified — A, C, the warning in scope, B rejected, D not taken: amendment 1 to the ci-flakes
   brief, operator, 2026-09-14. Not relitigated here.
2. (b) decide-and-document — **D1 narrows the ratified wording.** The amendment says start waits
   "until every bound member's first value is cached". A bound member whose getter throws during the
   initial publish is skipped with a warning (`ServiceBinder.cs:151-162`) and never publishes, so a
   literal per-member wait would fail every start of such a block — for the web UI and the xunit
   harness too, which is the STOP the amendment named. D1 guarantees every value that *was*
   published; the member that was not stays unreadable, as it is today, and its warning is in the log.
3. (b) decide-and-document — D3's `advance` counts as a wait for every member. On a stepped host it
   drains to quiescence (`AC-SCEN-012.1`); on the real clock it is a wait of real time, which is not a
   guarantee but is what an author who wrote it asked for.
4. (b) decide-and-document — `serviceProviderExpect` after a drive with no wait has the same race and
   is not warned about. The amendment names `expect`; widening it is proposed here, not produced.
5. Outcome: _(filled before archive)_

---

## Full design

### Why start publications can be unreadable after start

`LogicBlockBase` handles `StartLogicBlockRequest` by running the start hook, publishing every bound
member (`LogicBlockBase.cs:319`), then acknowledging (`:328`). Each publication is a direct send to
the property or measuring-point handler (`LogicBlockBase.cs:1240`, `:1274`); the acknowledgement goes
to the temporary actor behind `SendAndWaitForAcknowledgementAsync`. The host's value cache is written
when the handler handles the publication (`MockServicePropertyHandler.cs:28-41` →
`DevHostControl.cs:724-727`). Two receivers, so nothing orders the handler's work against the
acknowledgement: `DevLogicSystemInitializer.StartAsync` (`:186-209`) can return while the publications
are still queued. A `set` has no such gap — the block answers the write *to the handler*, after the
change, so FIFO on one mailbox orders them (`MockServicePropertyHandler.cs:47-54`).

### D1 — the barrier

After the start acknowledgement, `StartAsync` sends one barrier request to
`MockServicePropertyHandler` and one to `MockServiceMeasuringPointHandler` and waits for both answers.
Every start publication was enqueued on those mailboxes before the block acknowledged, and the barrier
is enqueued after the acknowledgement arrived, so a handler answers the barrier only after it has
handled — and cached — every start publication. It is the teardown's precedent in miniature: `StopAsync`
already waits for publishes "still queued on the mock handlers' mailboxes" (`DevLogicSystemInitializer.cs:276-287`),
but over whole-system quiescence, which a block with a tight message cascade could hold off. Start
cannot take that risk without changing start for every DevHost user, so the barrier is scoped to the
two mailboxes the cache is fed from.

Alternatives: a per-member cache check against the configuration (rejected — Reviewer's question 2);
whole-system quiescence (rejected above); doing it in the scenario runner before the first step
(rejected — the web UI's and a test's first read have the same gap, and the ratified decision puts
the guarantee on start).

### D2 — the bound and the failure

The barrier wait is virtual like every actor wait, so it takes the real-time backstop the start
acknowledgement takes (`DevLogicSystemInitializer.cs:198-207`), from the same budget. Its elapse throws
`TimeoutException` saying the values published while starting were not handled within the budget —
not which handler, because the actor wait counts outstanding answers without naming them. Nothing in
the repository makes a mock handler withhold an answer, so that half of `AC-CTRL-002.4` has no test of
its own; the acknowledgement half keeps `HostHealthShould.FailStartNoBlockAcknowledgesWithinRealTimeBudget`.

### D3/D4 — the warning

`ScenarioFileChecks.Validate` gains a `Warnings` list beside `Errors`, the shape
`TopologyFileChecks` already has (`TopologyFileChecks.cs:17`), rendered as `!` lines and as a
`warnings` array in `--json`. Walking setup then steps, a drive marks every member "in flight"; a
`waitUntil` on a member, a `settle` covering it (its `until`, or the watch list when `until` is
omitted) clears that member; an `advance` clears all. An `expect` whose property or comparand path is
still in flight warns, naming the drive's and the read's step and the three remedies. The check is
structural, so it runs whether or not the scenario's topology resolves.

### The proof

- **Start:** a test registers an `IActorMessageObserver` whose `OnReceived` (called before dispatch,
  `ActorMiddleware.cs:24-28`) parks `MockServicePropertyHandler` on the seeded member's publication
  until the test has taken its read or a fallback bound passes. Pre-fix, start completes while the
  handler is parked and the read is `null`; with the barrier, start cannot complete until the parked
  publication is handled. The fallback only ends the park on the fixed path — it decides duration,
  never the outcome.
- **The warning:** unit rows over `ScenarioFileChecks.Validate` for each clearing step and each
  non-clearing one, `io-control`'s shape among them.
- **The scenarios:** a scenario fix is shown by the warning going quiet on it, plus the suites green
  five times; a drive's delivery has no seam to park, so the red half is carried by the warning row.

---

## Drift checkpoints

- 2026-09-14: the start test first released its hold when start completed, and passed against the
  pre-fix host 3 of 3 — the release let the handler cache the value before the test thread read it.
  The hold now ends once the read is taken; red 3 of 3 pre-fix, green with the barrier.
- 2026-09-14: the brief's sweep said six committed scenarios share the drive shape; re-derived with
  setup drives counted and the same-target rule — still six: `toggle-light`, `grid-demand`,
  `io-control`, `output-confirmation`, `plant-control`, `provider-faces`, plus inline scenarios in
  `DevHostSmokeShould`, `ServiceProviderSetStepShould` and `ServiceProviderStructContractShould`.
  Command: `pwsh -File <scratchpad>/sweep.ps1 -Root .` (an over-approximation; each hit is read
  before it is changed).

---

## Spec delta (to distill)

- ADDED AC-CTRL-002.10 -> docs/specs/devhost-control.md : (Ubiquitous): THE SYSTEM SHALL complete a start only once every value the logic blocks published while starting is readable through the control surface.
- MODIFIED AC-CTRL-002.4 -> docs/specs/devhost-control.md : (Event-driven): WHEN not every block acknowledges start, or the values published while starting are not readable, THE SYSTEM SHALL fail the start within a real-time budget no clock mode can stall.
- ADDED AC-SCEN-015.11 -> docs/specs/scenarios.md : (Event-driven): WHEN an `expect` reads a member after a `serviceProviderSet` with no `waitUntil` on that member, `settle` covering it or `advance` between them THE SYSTEM SHALL report a warning in the offline validator naming both steps, and SHALL not refuse the file for it.

---

## Tasks

- `T-001` (`AC-CTRL-002.10`, `AC-CTRL-002.4`): the start barrier through both value handlers, under the start budget, with the parked-handler test proven red.
- `T-002` (`AC-SCEN-015.11`): the validator warning, its rows, and the `--json` `warnings` array.
- `T-003` (`AC-SCEN-015.11`): the six committed scenarios and three inline test scenarios wait on what they read; the warning is quiet over every committed scenario.
- `T-004`: distill the delta into both pages and archive this doc.
