---
slug: modbus-latency-window
status: proposed           # proposed | in-flight | parked | archived
blocked-on: none           # for parked docs: what's blocking + ref
areas: MODB
author: jonasbertsch
created: 2026-09-16
updated: 2026-09-16
supersedes: none           # path of a superseded change doc, or none
---

# A Modbus client's link summary reports its latency over a recent time window

> Change doc — one in-flight change. The `Spec delta` below is distilled into the current-truth
> pages under `docs/specs/` in the implementation PR, then this doc is archived
> (`pwsh scripts/spec-change.ps1 archive <slug>`). Process: `docs/spec-process.md`. Never put
> change narrative inline in a spec page — it lives here.

## At a glance

### Summary

VION-228. `ModbusLinkSummary` reports round trip and queued wait as last / min / max over the client's
whole life. On a card that shows only the live value, a support engineer cannot tell a one-off spike
from a recurring one, "last" is one transaction in thousands, and the lifetime min is pinned at zero.
This change replaces the last and min fields with a mean, a max and a transaction count over a fixed
recent time window (proposed: 15 minutes), keeps the lifetime max and adds the instant it occurred —
for both metrics, on Modbus TCP and Modbus RTU, computed in the SDK because the cloud keeps no
per-field history.

### Spec implications

`modbus.md` § The link diagnostics the SDK owns. `AC-MODB-016.5` is reworded from "round-trip
extremes / queued-wait fields" to every round-trip and queued-wait figure, windowed and lifetime
(`MODIFIED`). `AC-MODB-016.6` narrows from "count … and never reset" to the counters and the lifetime
maxima, because the windowed figures forget by design (`MODIFIED`). `AC-MODB-016.3`'s GAP reason
("nothing in the accumulator reads a clock") becomes false: the proposal un-GAPs it with a test
(`MODIFIED`, reviewer's question 6). Three criteria are added: the windowed figures and the window's
extent on the client's clock (`.7`), an empty window (`.8`), the lifetime max and its instant (`.9`).
The prose after the criteria loses "the gauge a block reads to see congestion" (it was
`LastQueuedWait`) and gains the window's cost. `AC-MODB-016.1` keeps its text (reviewer's question 5).
No other page: the field keys reach introspection, but `introspection.md` states the rule for any struct
field, not this struct's roster.

### Decisions

- `D1` — **The window is a ring of fixed time slots on the client's own clock** — each slot holds a
  count, a sum and a max per metric, recorded in O(1) inside `Record`'s existing lock and merged at
  snapshot. Length and slot size are reviewer's question 1.
- `D2` — **Both clients take the clock from the container that already stamps their receipts**, and the
  accumulator reads it itself, in `Record` and in `Snapshot`. The Modbus TCP client gains a
  `TimeProvider` constructor parameter; the Modbus RTU client already holds one. No stamp is taken from
  the receipt (§ Full design › Which clock).
- `D3` — **The lifetime max's instant is the receipt's `ReceivedAt`** (UTC wall clock, the same scale as
  `LastContactAt`), and only a strictly larger value moves it: the first occurrence of the worst value
  is the one reported.
- `D4` — **An empty window reads its mean and max as null and its count as zero**: a count of zero is a
  fact, not an absence.
- `D5` — **The two example scenarios assert `maxRoundTrip` instead of `lastRoundTrip`**: the field exists
  in the published 0.14.0 packages and after this change, so both runs of those scenarios stay green
  across the release boundary (§ Full design › What the removed fields reach).
- `D6` — **Constant-time, allocation-free recording is pinned by a premise test that cites no id**, not
  by a criterion: it is a property of the implementation a consumer relies on, not an observable of the
  summary (`docs/testing-conventions.md` §17).

### Reviewer's questions

1. (c) propose-and-wait — **Window length and mechanism.** Options in § Full design › The window.
   Recommendation: **15 minutes, one-minute slots, a ring of 16** (the current, partial minute plus 15
   whole ones), so a read covers at least the last 15 minutes and at most the last 16 — or the client's
   whole life where that is shorter. The alternative worth weighing is 30-second slots in a ring of 31
   (15 to 15½ minutes, twice the memory and the merge). A tumbling window like `WindowedMax<T>` is
   rejected: a read one second after it restarts rests on one second of traffic.
2. (c) propose-and-wait — **What the transaction count counts.** Recommendation: **one count per
   metric** — `RecentRoundTripCount` over the transactions that reached the wire, `RecentQueuedWaitCount`
   over every transaction but `Invalid` — so each mean is shown with its own denominator. The single
   shared count is exact only when nothing was backed off, expired, dropped or cancelled, and that is
   the congestion case where queued wait is read. Alternative: one count of the transactions that
   reached the wire, with the queued-wait population's size left unpublished.
3. (c) propose-and-wait — **Field names, titles and descriptions.** Recommendation: § Full design ›
   The fields, option A — a `Recent` prefix in the identifier and the length only in the title, so a
   later change of length changes a translation's source string and orphans no key
   (`docs/specs/introspection.md`, the display-string table). The two kept fields keep their
   identifiers; their titles gain "since start" so the card distinguishes them from the windowed max.
4. (a) ratified — the maintainer's seven constraints in the VION-228 brief: mean and max over the
   window, lifetime max with its instant, a transaction count; empty reads null; today's inclusion
   rules; one fixed window stated in titles; no percentile; new fields on `ModbusLinkSummary`, no new
   type and no new `IModbusClient` member; `LastRoundTrip`, `LastQueuedWait`, `MinRoundTrip` removed and
   the two maxima kept; O(1), allocation-free recording in the existing lock, bounded memory, the
   snapshot cost stated, no emission-policy change and no new property. Not relitigated.
5. (b) decide-and-document — **`AC-MODB-016.1` keeps its text.** The snapshot already copies out under
   `Record`'s lock; it now also merges the ring — a bounded loop of 16 slots — so a transaction
   completing during a read waits at most that merge, which is the same kind of wait it has today, not a
   new one (§ Full design › What a snapshot costs). Rewording it to name a bound would state an
   implementation detail no consumer can observe.
6. (b) decide-and-document — **`AC-MODB-016.3` loses its GAP.** Its reason was that the accumulator
   reads no clock; after `D2` it does, so "the state is indifferent to time" becomes a behaviour a test
   can break. The test advances the client's clock past the window and asserts the state unchanged, on
   the same through-the-client fixture as the window tests.

---

## Full design

### What the summary does today

`ModbusLinkAccumulator.Record` takes one lock per receipt and updates counters, state, last contact,
last failure, and five latency fields
(`Vion.Dale.Sdk.Modbus.Core/Diagnostics/ModbusLinkAccumulator.cs:56-119`). Round trip is fed only from
the five outcomes that reached the wire (`:103`, `ReachedTheWire` at `:147-150`); queued wait from every
outcome but `Invalid` (`:113`). `Snapshot(int queueDepth)` copies out under the same lock (`:122-145`).
It reads no clock. `ModbusLinkSummary` has 18 positional fields
(`Vion.Dale.Sdk.Modbus.Core/Diagnostics/ModbusLinkSummary.cs:60-96`), and its remark says counts and
extremes are lifetime and never reset (`:35`).

The two populations differ on `BackedOff`, `Expired`, `Dropped` and `Cancelled` — all four are in the
queued-wait population and out of the round-trip one — and on nothing else (`Invalid` is out of both).

### The window

The window has to answer "over the last N minutes" at any read, including a read after the client
has been idle, without keeping per-transaction samples.

**A (recommended): a ring of slots with absolute slot ids.** Slot length `S`, ring size `R`. Each ring
entry holds its absolute slot id (elapsed time since the accumulator was built, divided by `S`) and, per
metric, `count`, `sumTicks` and `maxTicks`.

- `Record`: read the clock inside the lock, compute `slotId`, take `entry = ring[slotId % R]`; if
  `entry.Id != slotId`, zero it and set its id; add the sample. Constant work, no allocation — the ring
  is a struct array allocated once in the constructor. Because the clock is read inside the lock and is
  monotonic, the ids a writer sees never decrease, so an entry is never reset to an older id.
- `Snapshot`: read the clock inside the lock, compute `nowSlot`, and fold every entry whose id lies in
  `[nowSlot − (R − 1), nowSlot]`. Entries left behind by an idle period are simply outside that range —
  nothing rotates on a timer, and an idle client reads an empty window.
- Extent: the fold includes the current, partial slot plus `R − 1` whole ones, so a read covers at least
  `(R − 1)·S` and less than `R·S` of history — or the accumulator's whole life where that is shorter,
  since slots before it existed are empty.

With `S` = 1 min and `R` = 16: at least 15 min, less than 16. With `S` = 30 s and `R` = 31: at least
15 min, less than 15½.

**B: a tumbling window** (`WindowedMax<T>`, `Vion.Dale.Sdk/Diagnostics/WindowedMax.cs:31-50`): one
accumulator restarted every `N`. Cheapest, but a read reports only the time since the restart — one
second of traffic just after it — which the brief's first point excludes.

**C: two tumbling windows, current and previous, read together.** Covers between `N` and `2N`; a spike
lingers up to twice the stated length, so a title cannot say what the figure is.

**D: exponentially weighted mean and a decaying max.** Constant memory, but neither figure is "over the
last N minutes", and there is no honest count. Rejected on constraint 3.

**Length.** 15 minutes is the reporter's example and spans 30 publishes at the consumer's 30-second
throttle, so a spike stays on the card for many publishes and is gone within the quarter hour. Five
minutes spans ten publishes and makes a recurring hourly spike look one-off far more often; an hour
makes a one-off spike look recurring for most of an hour.

### Which clock

The hazard is two clocks: one stamping a transaction into a slot and another supplying "now" at read
time, which on a virtual clock would put every transaction outside the window or keep it forever.

- **Modbus TCP.** `LogicBlockModbusTcpClient` holds no clock and builds its accumulator in a field
  initializer (`Vion.Dale.Sdk.Modbus.Tcp/Client/LogicBlock/LogicBlockModbusTcpClient.cs:30`). Its
  receipts are stamped by `DeviceRequest` from the `TimeProvider` the `RequestFactory` received from the
  container (`Client/Request/DeviceRequest.cs:90-93`, `Client/Request/RequestFactory.cs:14`), and the
  registration keeps a host-supplied or test-supplied clock (`ServiceCollectionExtensions.cs:39-41`).
  Proposal: the client takes `TimeProvider` as a constructor parameter from the same container and
  builds the accumulator with it in the constructor. The client is `[InternalApi]` and container-built;
  the one direct construction is its own unit test
  (`Vion.Dale.Sdk.Modbus.Tcp.Test/Client/LogicBlock/LogicBlockModbusTcpClientShould.cs:64`).
  Rejected: handing the clock over through `IRequestQueue.Initialize` — `FakeModbusTcpHarness`
  substitutes the queue (`Vion.Dale.Sdk.Modbus.Tcp.TestKit/FakeModbusTcpHarness.cs:80`), so a clock
  wired only there is invisible to the kit's tests; and the connection accumulator's `UseClock`
  hand-off (`Diagnostics/ModbusTcpConnectionAccumulator.cs:38`, `:61`), which runs on the system clock
  until the hand-off, a window that a link accumulator recording from its first transaction must not
  have.
- **Modbus RTU.** `ModbusRtu` already holds the `TimeProvider` it stamps its own `Invalid` receipts
  with (`Vion.Dale.Sdk.Modbus.Rtu/ModbusRtu.cs:34`, `:1240`). Proposal: build the accumulator in the
  constructor with it, replacing the field initializer at `:28`.
- **Not the receipt's stamp.** On RTU, `ReceivedTimestamp` is stamped by `ModbusRtuHandler` from the
  handler's own `TimeProvider` (`Vion.Dale.Sdk.Modbus.Rtu/ModbusRtuHandler.cs:264-265`), a different
  instance from the block's. Reading the client's clock in `Record` makes both ends of the window the
  same clock by construction. The cost is one `GetTimestamp()` per transaction, and the slot a
  transaction lands in is the moment it was recorded rather than observed — on RTU that is after the
  hop from the handler to the block's actor, far below a one-minute slot.

The TestKit's `LogicBlockTestContextBuilder` registers the context's virtual clock as the
`TimeProvider` of the block's container (`Vion.Dale.Sdk.TestKit/LogicBlockTestContextBuilder.cs:347`),
and `IModbusRtuExtensions.Simulate*` stamps with `testContext.TimeProvider`
(`Vion.Dale.Sdk.Modbus.Rtu.TestKit/IModbusRtuExtensions.cs:44-45`). Whether the `ModbusRtu` a block
receives is built from that container after the clock is registered is the first thing the RTU
through-the-client test proves; if it is not, that is a Drift checkpoint and a STOP, not a workaround.

### The fields

The summary loses three fields and gains eight: 18 → 23. Order groups each metric; `QueueDepth` stays
last. Wire keys are the camelCased names.

**Option A (recommended) — `Recent` prefix, length in titles only.**

| Field | Type | Title | Description |
| --- | --- | --- | --- |
| `RecentRoundTripCount` | `long` | Round trips (15 min) | Transactions that reached the wire in the last 15 minutes, which the two round-trip figures beside it rest on. |
| `RecentMeanRoundTrip` | `TimeSpan?` | Round trip (mean, 15 min) | Mean dispatch-to-response time over the last 15 minutes; empty when nothing reached the wire. |
| `RecentMaxRoundTrip` | `TimeSpan?` | Round trip (max, 15 min) | Longest dispatch-to-response time in the last 15 minutes; empty when nothing reached the wire. |
| `MaxRoundTrip` *(kept)* | `TimeSpan?` | Round trip (max since start) | The longest dispatch-to-response time since the client started. |
| `MaxRoundTripAt` | `DateTime?` | Round trip (max since start) at | When the longest round trip since the client started was observed (UTC). |
| `RecentQueuedWaitCount` | `long` | Queued waits (15 min) | Requests queued in the last 15 minutes, which the two queued-wait figures beside it rest on. |
| `RecentMeanQueuedWait` | `TimeSpan?` | Queued wait (mean, 15 min) | Mean local wait before dispatch over the last 15 minutes; empty when nothing was queued. |
| `RecentMaxQueuedWait` | `TimeSpan?` | Queued wait (max, 15 min) | Longest local wait before dispatch in the last 15 minutes; empty when nothing was queued. |
| `MaxQueuedWait` *(kept)* | `TimeSpan?` | Queued wait (max since start) | The longest local wait before dispatch since the client started. |
| `MaxQueuedWaitAt` | `DateTime?` | Queued wait (max since start) at | When the longest queued wait since the client started was observed (UTC). |

Removed: `LastRoundTrip`, `MinRoundTrip`, `LastQueuedWait`.

"15 min" is the recommendation of question 1; the descriptions say "the last 15 minutes" where the
covered span is 15 to 16. The XML `<remarks>` states the exact extent; a title does not have room.

**Option B — length in the identifier** (`RoundTripMean15Min`). Self-describing on the wire, but a later
change of length is a rename, which orphans every authored translation of the field.

**Option C — `Window` prefix** (`WindowMeanRoundTrip`). Same stability as A; "window" is a word a
support engineer does not use, though only the key carries it.

The two kept fields' titles change ("Round trip (max)" → "Round trip (max since start)"). Their keys
are unchanged, so a translation stays attached and is flagged by its changed source string.

### What the removed fields reach

From `git grep -niE 'LastRoundTrip|MinRoundTrip|LastQueuedWait|Round trip \((last|min)\)|Queued wait \(last\)' origin/main -- . ':!docs/changes/archive' ':!docs/retro'`,
sorted:

| Hit | What it is | Action |
| --- | --- | --- |
| `ModbusLinkSummary.cs`, `ModbusLinkAccumulator.cs` | the struct and the accumulator | reshaped |
| `Vion.Dale.Sdk.Modbus.Core.Test/Diagnostics/ModbusLinkAccumulatorShould.cs:116,133,150,152` | `AC-MODB-016.5` tests | rewritten on the new fields |
| `Vion.Dale.Sdk.Modbus.Tcp.TestKit.Test/ModbusDiagnosticsIntrospectionShould.cs:44,47,92,168` | a field key, the field count, a title, a duration format | moved to kept or new fields; 18 → 23 |
| `.claude/skills/modbus-smoke/SKILL.md:124` | a title the skill tells the reader to look for | a kept title |
| `examples/Vion.Examples.ModbusTcp/README.md:94` | `Link → LastQueuedWait` in a walk-through | the windowed queued wait |
| `examples/Vion.Examples.ModbusTcp/scenarios/modbus-healthy.scenario.json:27`, `modbus-link-policy.scenario.json:21` | assert `DebugClient.Link.lastRoundTrip` is not null | `maxRoundTrip` (`D5`) |
| `Vion.Dale.DevHost.SmokeHost/LogicBlocks/ShowcaseBlock.cs:201,205` | the SmokeHost's own `LinkProfile` struct | none — a different type |
| `examples/.../ModbusTcpDebugClient.cs:380,713` | the example's receipt-fed `LastRoundTripMs` | none — a different member |

The two scenarios run against the published packages in `dotnet test Vion.Dale.Sdk.sln` and against
the working tree under `smoke-modbus.ps1 -LocalSource` (`.claude/skills/modbus-smoke/SKILL.md:53-57`).
Asserting a new field would redden the first until a release; keeping `lastRoundTrip` would redden the
second at once. `maxRoundTrip` is not null after the first wire transaction in both.

The consumer, logic-block-libraries at `origin/main`, names none of the removed fields: every block
assigns `_client.Link` whole, and its one test builds a summary with `with { State, SuccessCount }`
(`Ecocoach.EnergyManagement.Test/LogicBlocks/ElectricityMeterSiemensPac2200Should.cs:1004`), which
compiles against either shape.

### A shared assembly on a gateway

`Vion.Dale.Sdk.Modbus.Core` carries `[assembly: DaleSharedAssembly]`; the first plugin to bind it loads
it and every later plugin resolves that instance (`docs/specs/plugin-loading.md`, `AC-PLUG-005.1`,
`AC-PLUG-005.2`). With two plugins on one gateway built against the SDK before and after this change:

- **A plugin that only passes `Link` whole** — the consumer's shape — names no field, so it runs
  against either version. The value it publishes carries the loaded version's fields, while its
  introspection was generated at its own build and declares the fields of the version it built
  against. How the cloud renders a published field its schema does not declare, or a declared one the
  value lacks, is not stated anywhere in this repo [inferred: the card shows the declared fields and the
  missing ones as empty].
- **A plugin whose own code reads a field the loaded version lacks** (`Link.LastRoundTrip` with the new
  one loaded, `Link.RecentMeanRoundTrip` with the old) fails with `MissingMethodException` when that
  code first runs.
- The positional constructor and `Deconstruct` change shape too; nothing outside the accumulator
  constructs the struct, and a consumer deconstructing it would fail the same way.

This is the same exposure any field change to a shared type has; the release notes name it, and
upgrading every Modbus plugin on a gateway together removes it.

### What a snapshot costs

Per client, the ring holds 16 entries of one `long` id and two `(long count, long sumTicks, long
maxTicks)` triples: 56 bytes of payload each, 896 bytes plus one array header, allocated once. `Record`
adds one `GetTimestamp()`, one modulo, a compare, and at most one entry reset to the work it does today.
`Snapshot` adds one `GetTimestamp()` and a loop over 16 entries with a range check and three
accumulations per metric, under the lock, and allocates nothing — the summary is a struct. Not
measured; the premise test (`D6`) pins "no allocation per `Record`" with
`GC.GetAllocatedBytesForCurrentThread`, and the loop bound is the ring size. The mean's sum cannot
overflow: at the reporter's 55 transactions a second, 16 minutes of 30-second round trips sums to about
1.6·10¹³ ticks against a `long`'s 9.2·10¹⁸.

### Proof plan

Discriminating fixtures, per the brief's hazards:

- **Accumulator, on a `FakeTimeProvider`** (`ModbusLinkAccumulatorShould`): the `Receipt` helper gains
  distinct `ReceivedAt`s. Mean from unequal round trips (e.g. 10, 20, 90 ms → 40, max 90). Instant: a
  later larger spike moves it; a later smaller one and a later equal one do not. Populations, as
  `[DataRow]`s: `BackedOff`, `Expired`, `Dropped`, `Cancelled` stay out of every round-trip figure,
  `Invalid` out of every queued-wait figure, each after a success that set the figures. Extent: a sample
  still counted 15 minutes later and gone 16 minutes later; a young client's window rests on its life
  alone; an idle client reads null mean and max and a zero count.
- **Modbus TCP, through the client** (`Vion.Dale.Sdk.Modbus.Tcp.TestKit.Test/ModbusLinkDiagnosticsShould`,
  `FakeModbusTcpHarness` built with the clock): a spike via `Proxy.ResponseDelay`, then quick
  transactions, then advance past 16 minutes: the windowed max and mean are null and the count zero,
  while `MaxRoundTrip` and `MaxRoundTripAt` still show the spike; the state is unchanged
  (`AC-MODB-016.3`).
- **Modbus RTU, through the client** (`Vion.Dale.Sdk.Modbus.Rtu.TestKit.Test`): a read, the context's
  clock advanced before `SimulateReadResponse` to make the spike, then the same advance and assertions.
  `ModbusRtuShould` mocks the request factory (`Vion.Dale.Sdk.Modbus.Rtu.Test/ModbusRtuShould.cs:103`),
  so it cannot carry this.
- **Introspection**: the field count, the removed keys absent, a new title, a duration and a date-time
  format on new fields.
- **Rendered**: `ModbusTcpDebugClient` against the working tree (`-p:DaleLocalSource=true`,
  `modbus-smoke` `-LocalSource` tier), reading what the Diagnostics viewer shows for `Link`.

The reporter's live card needs a release and a logic-block-libraries upgrade: not run, routes to a
human.

---

## Drift checkpoints

- _(none yet — append as implementation diverges from Full design)_

---

## Spec delta (to distill)

- MODIFIED AC-MODB-016.3 -> docs/specs/modbus.md : THE SYSTEM SHALL leave the link state unchanged by the passage of time.
- MODIFIED AC-MODB-016.5 -> docs/specs/modbus.md : THE SYSTEM SHALL feed every round-trip figure only from transactions that reached the wire, and every queued-wait figure only from requests that were queued.
- MODIFIED AC-MODB-016.6 -> docs/specs/modbus.md : THE SYSTEM SHALL keep the outcome counters and the two lifetime maxima for the lifetime of the client instance and never reset them. GAP: an absence — the summary offers no reset, which is grep-enumerable from its surface.
- ADDED AC-MODB-016.7 -> docs/specs/modbus.md : THE SYSTEM SHALL report, for round trip and for queued wait separately, the mean, the maximum and the number of transactions over at least the last 15 minutes and less than the last 16 on the client's clock, or over the client's whole life where that is shorter.
- ADDED AC-MODB-016.8 -> docs/specs/modbus.md : WHILE no transaction feeding a metric falls within its window THE SYSTEM SHALL report that metric's windowed mean and maximum as empty and its windowed count as zero.
- ADDED AC-MODB-016.9 -> docs/specs/modbus.md : THE SYSTEM SHALL report, for round trip and for queued wait separately, the lifetime maximum with the instant its transaction was observed, and SHALL move that instant only when a strictly larger value is recorded.

The lengths in `.7` follow reviewer's question 1 and the count shape follows question 2; both lines are
re-read against the answer before the first implementation commit.

---

## Tasks

- `T-001` (`AC-MODB-016.7`, `AC-MODB-016.8`, `AC-MODB-016.9`, `AC-MODB-016.5`): the accumulator's ring,
  lifetime maxima with instants, its clock, the reshaped `ModbusLinkSummary` with XML docs and
  `[StructField]`s; the accumulator suite rewritten on the discriminating fixtures; the premise test (`D6`).
- `T-002` (`AC-MODB-016.7`, `AC-MODB-016.3`): the Modbus TCP client takes its clock from the container;
  the through-the-client test on `FakeModbusTcpHarness`.
- `T-003` (`AC-MODB-016.7`, `AC-MODB-016.3`): the Modbus RTU client builds its accumulator with its
  clock; the through-the-client test on the RTU TestKit's `Simulate*` path.
- `T-004` (`AC-MODB-016.7`): the introspection suite, the two example scenarios (`D5`), the example
  README and the `modbus-smoke` skill.
- `T-005`: distill the delta into `docs/specs/modbus.md` (criteria and prose), `/check`, the rendered
  `-LocalSource` run, archive.
