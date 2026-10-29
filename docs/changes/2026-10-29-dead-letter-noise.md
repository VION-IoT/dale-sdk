---
slug: dead-letter-noise
status: in-flight          # proposed | in-flight | parked | archived
blocked-on: none           # for parked docs: what's blocking + ref
areas: LIFE
author: jonasbertsch
created: 2026-10-29
updated: 2026-10-29
supersedes: none           # path of a superseded change doc, or none
---

# Dead letters only for messages nobody expected to lose (VION-409)

> Change doc — one in-flight change. The `Spec delta` below is distilled into the current-truth
> pages under `docs/specs/` in the implementation PR, then this doc is archived
> (`spec-change.ps1 archive dead-letter-noise`). Process: `docs/spec-process.md`. Never put change
> narrative inline in a spec page — it lives here.

## At a glance

### Summary

A dale stop on the CX82xx gateway logs up to ~530 `DeadLetter` warnings, and a boot whose restore
wait expired logs one more per late block. Nothing failed: each is a message to an actor the runtime
stopped on purpose. This change stops the SDK sending those messages to nobody, at the three places
they come from, so the warning that remains is the one for a message to an actor nobody stopped. The
two expiry warnings that name no actor now name them.

### Spec implications

`block-lifecycle.md` gains `AC-LIFE-014.8` (a self-send made once an actor has begun to stop is
dropped), `AC-LIFE-016.9` (a termination wait watches each actor before it stops it),
`AC-LIFE-016.10` (an expired acknowledgement wait keeps receiving from the actors still outstanding)
and two `GAP` log criteria under `testing-conventions.md` §15: `AC-LIFE-016.11` (an expiry warning
names its actors) and `AC-LIFE-017.5` (a dead letter is a warning). `AC-LIFE-017.1` is split: the
dead letter stays in it and is now tested, and the warning moves to `AC-LIFE-017.5`, so no test
cites a log level. No criterion is removed. `modbus.md`'s `AC-MODB-010.1` keeps its text — its
subject is the client, which still completes every queued request on disposal — and the sentence
below it that says a block's teardown write "gets" that receipt is corrected: the block hears nothing
once its actor has begun to stop.

### Decisions

- `D1` **(b)** — **Prevent each dead letter where it originates; classify none.** The brief left the
  direction open (constraint 1). Treating dead letters to stopped actors as expected needs a record
  of which actors the runtime stopped. That record grows without bound for the anonymous wait actors,
  must forget a name when a block is respawned under it, and shows only as a log level — a `GAP`
  (§15), so the Done-when's regression tests would have nothing to observe. Prevention is observable
  on the actor framework's event stream, which also drives Proto.Actor's own dead-letter counter. It
  also leaves `AC-LIFE-017.1` total: every message that reaches no actor still warns, with no
  exception to state.
- `D2` **(b)** — **A self-send made once an actor has begun to stop is dropped in the actor layer**,
  not in the client. `Actor<T>` marks itself on `Stopping`, and `ActorContext.SendToSelf` drops what
  it is handed after that. This is the "deliver no callback to a block once it has terminated"
  direction. It sits below every client, so it covers the Modbus TCP and RTU drains, the HTTP
  executor's late callback, `InvokeSynchronizedAfter` and a timer due after the stop alike. The flag
  is set on `Stopping`, not `Stopped`: Proto.Actor removes the actor's registration before it
  delivers `Stopped` (`FinalizeStopAsync`, decompiled from Proto.Actor 1.8.0), so a flag set on
  `Stopped` leaves a window in which a self-send is still a dead letter.
- `D3` **(b)** — **`AC-MODB-010.1` is not reworded.** The client still completes each queued
  request with a `RequestDroppedException` and a `Cancelled` receipt and hands the callback to the
  block's dispatcher. Its tests drive the queue against a fake dispatcher
  (`RequestQueueShould.cs:350`, `:378`, `:393`) and are unchanged. What changes is whether that
  callback reaches a block whose actor has begun to stop, and that is `AC-LIFE-014.8`.
  `modbus.md`'s prose under the criterion is corrected to say so.
- `D4` **(b)** — **The termination wait's own actor stops each actor, right after watching it.**
  Watching from the caller's thread is not possible, because `PID.SendSystemMessage` is internal to
  Proto.Actor. Each actor gets the watch and then the stop from the same sender, so it handles the
  watch first. A watch that reaches an actor already stopping is answered at once, not
  dead-lettered (`ActorContext.HandleWatch`, decompiled). This moves each stop by one hop: it used
  to be sent on the caller's thread before the wait returned its task, and now it is sent from the
  wait actor's `Started`. Both callers await the task (`DevLogicSystemInitializer.cs:326`,
  `dale/LogicSystemTeardown.cs:125`).
- `D5` **(b)** — **An expired acknowledgement wait stays alive for its stragglers.** On expiry it
  fails the caller's task as before, then watches each actor that has not answered and stops once
  every one has answered or terminated. It never watches an actor whose request was itself
  dead-lettered — the framework tells the sender so with a `DeadLetterResponse` — because watching it
  would publish a dead letter of its own. So the wait actor lives no longer than its slowest
  outstanding actor. The termination wait's counterpart, a late termination notification reaching an
  expired wait, is not in the issue's list. It is proposed under the pull request's Draft items, not
  produced.
- `D6` **(b)** — **Both expiry warnings name the actors still outstanding; neither exception
  changes.** Constraint 2 offers naming or dropping. Dropping leaves a named record nowhere for four
  acknowledgement-wait callers (`dale/LogicSystemConfigurationInitializer.cs:410`,
  `dale/LogicSystemTeardown.cs:195`, `dale/Program.cs:284`, `:292`), and it leaves it nowhere for the
  termination wait's only caller, which logs an unnamed Error (`dale/LogicSystemTeardown.cs:127-131`).
  Naming covers every caller from one place, and it leaves both exceptions as they are, which keeps
  the class-(c) point closed. Callers that already log names now log them twice; that is a second
  named line, not an unnamed one.
- `D7` **(b)** — **`AC-LIFE-017.1` is split, not reworded in place.** The guard the issue asks for,
  "a dead letter to an actor nobody stopped still warns", has one observable that is not a log: the
  dead letter is published. `AC-LIFE-017.1` keeps the dead letter and gets a test. The level moves
  to `AC-LIFE-017.5`, which is `GAP` like `AC-LIFE-021.3`.
- `D8` **(a)** — **Out of scope as ratified:** the timeout defaults (VION-410, constraint 3); tests
  or spec text for the HTTP executor (constraint 4); dead letters the issue does not list
  (constraint 5). The `ContractMessage`s from hardware and the `FunctionInterfaceMessage` pair are
  sent by *other* actors to a block that has terminated, so `D2` does not reach them and they still
  warn.

### Reviewer's questions

1. **Is dropping a stopping actor's self-sends right for every self-send, not only the Modbus
   callbacks?** (b) A timer due after the stop, an `InvokeSynchronizedAfter` and a periodic save are
   dropped the same way. Each was already a dead letter, and its handler never ran. `AC-LIFE-015.6`
   means a receiver cannot observe the `Stopping` message itself, so no block can tell the
   difference.
2. **Is an expired acknowledgement wait living on acceptable?** (b) It ends when every outstanding
   actor has answered or terminated. An actor that never answers and never terminates keeps one wait
   actor alive for as long as it runs. That actor is itself the fault the expiry reports.
3. **The stop now leaves from the wait actor, one hop later.** (b) On a stepped host that actor runs
   on the deterministic dispatcher (`ActorSystem.cs:330`), so the stop joins the same serial
   timeline as the rest of the cascade. How the stepped path was checked is under Drift checkpoints.

---

## Full design

### Where each dead letter came from, and what now happens

| Source (brief) | Mechanism today | Change | Criterion |
| --- | --- | --- | --- |
| `InvokeActionMessage` from a disposed client's queue, ~2×256 per stop | `Actor<T>` disposes its scope on `Stopped` (`Actor.cs:45-56`). The client's drain calls each error callback (`RequestQueue.cs:297-314`, `Request.cs:35-39`), and `InvokeSynchronized` sends to the block's own actor (`LogicBlockBase.cs:172-176`), which is already deregistered | `SendToSelf` drops once the actor has begun to stop | `AC-LIFE-014.8` |
| `Watch` from the termination wait | The caller spawns the wait actor, then stops each actor (`ActorSystem.cs:405-408`). The wait actor watches in its `Started` (`:334-341`), which can run after an actor has finished stopping | The wait actor watches each actor, then stops it | `AC-LIFE-016.9` |
| Late acknowledgement at boot | On expiry the acknowledgement wait stops its own actor (`:180`). A later answer has no actor to reach | The wait actor stays until each straggler has answered or terminated | `AC-LIFE-016.10` |
| Unnamed expiry warnings | `:167-170`, `:372-375` log a count | Each names the actors still outstanding | `AC-LIFE-016.11` |

### Self-sends after the stop

`Actor<T>` holds a `volatile bool` it sets when it receives `Stopping`. That is the first lifecycle
message of a stop, and `Actor<T>` already sees it and passes it to no receiver (`AC-LIFE-015.6`). It
hands each `ActorContext` a way to read the flag. `ActorContext.SendToSelf` returns without sending
once it is set. `SendToSelfAfter`'s three paths all end in `SendToSelf`, so a delayed self-send whose
delay ends after the stop is dropped there too. The flag is read on whatever thread calls — a Modbus
consumer, an HTTP continuation — which is why it is volatile.

### The termination wait

`Started` now watches each actor and then stops it, both through the wait actor's context. The zero
timeout's early expiry stays after both, so a zero wait still stops every actor and still expires
before it handles any notification. `Started` is the first system message in the wait actor's
mailbox, and every termination notification is queued after it. The debug line that announces the
watch moves after the loop.

### The acknowledgement wait after expiry

On expiry the wait fails the caller's task with the same exception and names the outstanding actors
in its warning. Then, for each one not known to be absent, it watches it, and it stops once that set
is empty. While the wait is live, a `DeadLetterResponse` marks its target absent. After expiry, an
acknowledgement from an outstanding actor removes it and unwatches it, and so does a termination
notification. An actor that answers after expiry changes nothing for the caller: the exception has
already been thrown, and it holds a copy of what had answered by then.

### Alternatives considered

- **Classifying dead letters by target** — rejected in `D1`.
- **Withholding the callback in each client** (`DropOnDisposal` skipping it) — rejected. It reshapes
  `AC-MODB-010.1`, would need repeating in the RTU and HTTP clients, and still leaves an in-flight
  request's completion and every timer.
- **Watching from the caller** — not possible (`D4`).
- **Watching every actor from the acknowledgement wait's start** — rejected. A wait that completes
  normally would then have to unwatch every actor, or each actor's later termination would notify a
  wait actor that is gone, which is a dead letter per block per wait.

---

## Drift checkpoints

> One line per divergence discovered during implementation:
> `YYYY-MM-DD: <what changed and why>`. Never inline in a spec page. A checkpoint that fixes a
> CLASS bug states the sibling sweep (done / N/A / handed off).

- _(none yet — append as implementation diverges from Full design)_

---

## Spec delta (to distill)

- ADDED AC-LIFE-014.8 -> docs/specs/block-lifecycle.md : WHEN an actor sends itself a message after it has begun to stop THE SYSTEM SHALL drop the message, handing it to no handler and publishing no dead letter.
- ADDED AC-LIFE-016.9 -> docs/specs/block-lifecycle.md : THE SYSTEM SHALL watch each actor of a termination wait before it stops that actor, so stopping an actor that is running publishes no dead letter however soon it terminates.
- ADDED AC-LIFE-016.10 -> docs/specs/block-lifecycle.md : WHEN an acknowledgement wait expires THE SYSTEM SHALL keep receiving from each actor that had not answered until it answers or terminates, so a late acknowledgement publishes no dead letter.
- ADDED AC-LIFE-016.11 -> docs/specs/block-lifecycle.md : WHEN a wait expires THE SYSTEM SHALL log one warning naming every actor that had not answered or terminated. GAP: log text, which no test asserts on (testing-conventions.md §15); evidenced by a run observed at Warning and recorded under the pull request's Verification (VION-409).
- MODIFIED AC-LIFE-017.1 -> docs/specs/block-lifecycle.md : THE SYSTEM SHALL mint an actor reference from a name alone, whether or not an actor of that name exists, and SHALL publish a dead letter for each message that reaches no actor.
- ADDED AC-LIFE-017.5 -> docs/specs/block-lifecycle.md : THE SYSTEM SHALL log each dead letter as a warning, except the actor framework's own timeout continuations. GAP: a log level, which no test asserts on (testing-conventions.md §15); evidenced by a run observed at Warning and recorded under the pull request's Verification (VION-409).

---

## Tasks

- `T-001` (`AC-LIFE-014.8`): drop a self-send once the actor has begun to stop; the test stops a block whose scoped dependency self-sends while it is disposed.
- `T-002` (`AC-LIFE-016.9`): watch, then stop, from the termination wait's own actor; the test forces an actor to finish stopping before the old code's watch.
- `T-003` (`AC-LIFE-016.10`, `AC-LIFE-016.11`): keep an expired acknowledgement wait alive for its stragglers, and name the outstanding actors in both expiry warnings.
- `T-004` (`AC-LIFE-017.1`, `AC-LIFE-017.5`): the guard test that a message to a name never spawned is still dead-lettered; split the criterion.
- `T-005`: distill every delta line into `block-lifecycle.md`, correct `modbus.md`'s prose under `AC-MODB-010.1`, archive.
