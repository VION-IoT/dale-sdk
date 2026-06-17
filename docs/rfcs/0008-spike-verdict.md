# RFC 0008 — Phase 0 Spike Verdict: Deterministic DevHost Stepping

- **Date:** 2026-06-16
- **Branch:** `spike/deterministic-stepping` (commits `41ba44b`, `16b6e90`, `6032dbd`)
- **Gates:** RFC 0008 ([0008-unified-scenario-topology.md](0008-unified-scenario-topology.md)) §6.1 / §9 Phase 0
- **Plan:** `docs/superpowers/plans/2026-06-16-deterministic-stepping-spike.md` (local)

## Verdict: **GO**

Deterministic stepping of the **real-wired** DevHost is feasible and works. The keystone of RFC 0008 — running the *same* scenario artifact exact-deterministically in CI and interactively in the Player — is sound. Proceed to Phase 1, with **one** known hardening task (an exact quiescence barrier, below).

## What was proven (with evidence)

| Claim | Evidence |
|---|---|
| **The clock can drive the runtime.** Routing the single delayed-self-send choke point (`ActorContext.SendToSelfAfter`) + the two `ActorSystem` ack/stop timeouts through the DI-injected `TimeProvider` makes advancing a `FakeTimeProvider` fire `[Timer]` ticks. | Task 1 (`41ba44b`). Baseline test fails (Ticks=0) → passes (Ticks=1) after the change; independently confirmed by revert experiment. Production unchanged (`TryAddSingleton(TimeProvider.System)`; real-clock resolves as before). Full suite green. |
| **Single-block stepping is deterministic.** A quiescence barrier (advance clock → wait for the cascade to settle → repeat) yields identical results every run. | Task 3 (`16b6e90`). 50 iterations × multiple invocations × a 200-iteration run **under full CPU saturation** = ~2,250 advances, zero deviation. |
| **Multi-hop (request-response) cascades are deterministic.** | Task 4 (`6032dbd`). Depths to 16 hops, under CPU load, with artificial 0–100 ms dequeue→post stalls — **6,000 cascade evaluations, zero short-reads.** |

The mechanism is **understood, not just observed**: every timed continuation parks in `Task.Delay(…, fakeClock)` and posts nothing to a mailbox until the next `Advance`; for request-response cascades, overlapping forward+ack traffic keeps the mailbox-depth signal > 0 until the whole cascade drains (measured: a diagnostic never saw mailbox-depth read 0 while a handler was in flight).

## How it works

- **Clock:** register a `FakeTimeProvider` via `DevHostBuilder.ConfigureServices(s => s.AddSingleton<TimeProvider>(clock))`. `AddDaleSdk` now uses `TryAddSingleton` so it doesn't clobber the injection.
- **Stepper:** `IDevHostControl.AdvanceAsync(TimeSpan interval, int cycles)` — settle once, then per cycle `clock.Advance(interval)` + wait for quiescence. Built lazily; throws on a non-`FakeTimeProvider` clock.
- **Quiescence signal:** `Σ ActorVitals.MailboxDepth == 0` read from the already-registered `RuntimeVitals.Snapshot()` (`MailboxDepth = posted − received`, both from the same Proto mailbox-statistics hook). Currently confirmed stable across 5 reads × 2 ms.

## Key findings

1. **The "exact" `posted − handled` signal does NOT work** — `posted` (mailbox hook, counts all messages incl. system/infra) and `handled` (actor middleware, user handlers only) have **different denominators**, so their difference has a permanent positive floor. `MailboxDepth` (`posted − received`, same hook) is the correct "mailboxes empty" signal.
2. **The current barrier is a time-window heuristic.** It is proven robust for single-block and request-response cascades, but the window (≈10 ms) is in principle a real-time race detector.
3. **The one untested shape is the production-relevant one.** A *pure fire-and-forget fan-out with no reverse traffic* is the single cascade class where mailbox-depth could momentarily read 0 mid-cascade (a hop dequeued, follow-up not yet posted) → a potential one-hop "short-read." **The EnergyManager's real cascade is exactly this shape** (`SendAllocationCommands → SendCommand`, fire-and-forget `[Command]`, no ack). It was not directly exercised in the spike.

## Carried into Phase 1 (required before this leaves spike status)

1. **Replace the time-window with an EXACT, event-driven barrier.** Add an opt-in (DevHost-only, like the message tap) in-flight-handler counter in `ActorMiddleware.ReceiveMiddleware` (increment before the handler, decrement after). Quiescence becomes the atomic predicate **`Σ MailboxDepth == 0` AND `in-flight handlers == 0`** — no stability window. Because a handler increments in-flight before it can post-and-exit, the predicate cannot be true mid-cascade, which **closes the fire-and-forget/EM short-read gap by construction**. (Verify timer continuations / system messages flow through the middleware, or account for the boundary.) Low risk, well-understood; the spike deferred building it to avoid another long adversarial run, but it is the right production design.
2. **`ActorVitals.MessagesPosted` was inserted as positional record param #3** — a latent source/binary-breaking change (masked in-repo only because consumers reference the published `0.8.1` package). Append it, or treat as a deliberate breaking change, before release.
3. **Style-gate drift on the spike commits.** `scripts/cleanup-code.ps1 -Verify` flags ~5 of the Task 1/3 files (e.g. a trailing-newline diff on `QuiescenceBarrier.cs`) and is **non-idempotent** here — the known local-vs-Linux-CI `cleanupcode` inconsistency the repo documents. Resolve with the `@formatter:off/on` escape hatch (or a tool-version pin) when this code is productionized; not addressed on the spike branch.

## Scope notes

- **Task 2 (broad `Task.Delay` audit guard) was skipped** as owner's call: a source-wide scan flags many legitimate un-clocked delays (test settle-polls, CLI loops, `RunControlShould` wall-clock waits) → allow-list churn. The load-bearing scheduling delays are clocked and verified. Revisit a *narrow* guard in Phase 1.
- **Task 5 (port one TestKit matrix → explicit scenarios for the verbosity verdict)** was not run — it is cross-repo (`logic-block-libraries`, local SDK package) and independent of the determinism question. Defer to early Phase 1 once the data-model + struct-paths land; it informs the *port-vs-freeze* matrix-migration decision (RFC 0008 §7), not the GO/NO-GO.

## Bottom line

Deterministic stepping is real and the design is correct. Phase 1 should start by making the barrier exact (item 1) — which also validates the EnergyManager's fire-and-forget cascade — then build the data model and topology-as-data on top, per RFC 0008.
