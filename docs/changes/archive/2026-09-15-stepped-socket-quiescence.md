---
slug: stepped-socket-quiescence
status: archived
blocked-on: none           # for parked docs: what's blocking + ref
areas: SCEN, MODB, HTTP, CTRL
author: jonasbertsch
created: 2026-09-15
updated: 2026-09-15
supersedes: none           # path of a superseded change doc, or none
---

# A stepped settle waits on socket exchanges the SDK owns, and stops idling while it waits

> Change doc — one in-flight change. The `Spec delta` below is distilled into the current-truth
> pages under `docs/specs/` in the implementation PR, then this doc is archived
> (`pwsh scripts/spec-change.ps1 archive <slug>`). Process: `docs/spec-process.md`. Never put
> change narrative inline in a spec page — it lives here.

## At a glance

### Summary

VION-222. A stepped DevHost settles by polling its quiescence predicate with a real 1 ms delay, which
costs ~11 ms per wait on Windows and nearly all of a stepped lane's wall time. The same delay hides a
hole: a Modbus TCP or HTTP exchange a block starts leaves the actor system entirely until its callback
is posted back, so a settle that returned at once would miss the round trip's result. This change
makes the predicate count those exchanges (and the requests an SDK-hosted HTTP server has read), and
makes the barrier wake on the moment activity drains instead of on a timer. Stepped benches over
real loopback sockets become supported, and fast.

### Spec implications

`scenarios.md` § Stepping guarantees: `AC-SCEN-012.5`'s predicate widens to name the exchanges it
counts (`MODIFIED`); `AC-SCEN-012.6` is unchanged in text, and a new `.11` names the open exchanges
when the budget is spent. `modbus.md` gains one criterion — a request's completion reaches the block
before the virtual clock next moves and before an advance in progress returns — and `http.md` gains
two, the client's equivalent and the hosted server's for a client in the same host. Prose, no ids: `devhost-control.md:371-372` (what the barrier
reads), `http.md:576-577` and `simulator-authoring.md:154-155` (a socket bench "runs on the wall
clock" — reversed, with the guarantee's edge stated: a block opening its own socket stays invisible).
No wall-clock behaviour, no test-kit behaviour and nothing on a gateway changes.

### Decisions

- `D1` — **One counter, not three.** An open exchange adds to the same atomic count a running
  handler does; the DevHost keeps a handler-only count beside it for the teardown drain — so the
  stepped predicate stays one exact observation (constraint 3) and teardown is untouched (D5).
- `D2` — **The settle wakes on the count reaching zero**, re-evaluating the exact predicate, with a
  slow fallback re-check for a missed edge; no spin, no shorter timer.
- `D3` — **Accounting is an opt-in abstraction the DevHost registers**, beside the four it already
  has (`IActorActivityMonitor`, `IVirtualSchedule`, `IDelayedSendGate`, `IActorMessageObserver`);
  `Vion.Dale.Sdk.Modbus.Tcp` and `Vion.Dale.Sdk.Http` resolve it optionally, so a host that registers
  none — the gateway runtime, every test kit — runs the code it runs today.
- `D4` — **Where each exchange opens and closes**: a Modbus TCP request from enqueue until its
  completion has been handed to the block (or it was dropped); an HTTP call from the block's call
  until its callback has been handed over; an SDK-hosted HTTP server's request from the end of its
  full read until it is recorded or its connection ends (moved from accept by amendment 2; Drift
  checkpoints). The Modbus TCP server needs none (reviewer's question 5).
- `D5` — **Only the stepper reads the exchange count.** The teardown drain (`AC-CTRL` stop sequence)
  keeps today's handler-only predicate on both clock modes.
- `D6` — **Fake-clock bounds get no code.** While a counted exchange is open the stepper cannot
  advance virtual time, so an HTTP per-request timeout and the Modbus queued-age check cannot elapse
  during it on a stepped host; the page says so rather than a mechanism working around it.
- `D7` — **Test peers are test-owned raw sockets that hold their answer**, on an OS-assigned port
  (`ContractPairingShould.FreePort`, `Vion.Dale.DevHost.Test/ContractPairingShould.cs:439`); the
  stepped fixtures are test blocks in `Vion.Dale.DevHost.Test` (a Drift checkpoint records the move
  from the SmokeHost).

### Reviewer's questions

1. (a) ratified — the brief's four constraints: socket-wired stepped runs are supported by widening
   the predicate, with no transport swap and no refusal; concurrent stepped hosts in one process are
   out of scope (`AC-MODB-006.3`, `AC-HTTP-015.4` untouched); no timing window, `AC-SCEN-012.6`'s
   fail-rather-than-assume stays; nothing observable changes outside a stepped host. VION-222 brief,
   2026-09-15. Not relitigated. OUTCOME: implemented as ratified; the test kits and the gateway runtime
   register no counting monitor, and a wall-clock host never reads it (D3, D5).
2. (c) propose-and-wait — **what a stepped run does when a counted exchange waits on a real-clock
   bound** (an absent or slow peer). Options in § Full design › The absent peer. Recommendation:
   **A, wait honestly within the existing quiescence budget, and name the open exchanges when it is
   spent** (`ADDED AC-SCEN-012.11`). OUTCOME: accepted as A — the operator, amendment 1 to the
   VION-222 brief, 2026-09-15; B not chosen, C–E stay rejected; `AC-SCEN-012.11` and `T-005` in scope.
3. (c) propose-and-wait — **the off-schedule timeout continuations** the brief asked to check
   (§ Full design › Other continuations the poll hides). Found: on a stepped host `SendToSelfAfter`
   arms no `Task.Delay`, but the acknowledgement and stop-wait timeouts in `ActorSystem` still do, and
   a fast settle can return before the timeout's continuation posts. Recommendation: **close it in
   this change** by having the stepper deliver those two timeouts, as it delivers timer sends.
   Alternative: a draft item, and the window ships (it is reached only when a block fails to
   acknowledge on a stepped host). OUTCOME: changed — the operator, amendment 1, 2026-09-15: the
   example effects named above come from elsewhere (a refused write is `DevHostControl`'s own
   real-clock wait, `DevHostControl.cs:354-356`; a failed start is decided by the real-clock
   `WaitAsync`, `DevLogicSystemInitializer.cs:200`), so the window is closed only if a stepped advance
   can reach one of the timeout entries with an observable effect; otherwise "not reachable from an
   advance", with the evidence in Drift checkpoints. OUTCOME: not reachable from an advance — no
   `T-006` code; the evidence is the Drift checkpoint on the timeout waits.
4. (b) decide-and-document — **D3 crosses the public surface by one interface** in
   `Vion.Dale.Sdk.Abstractions`, unmarked like its four siblings, so the PublicApi manifest does not
   move (`docs/sdk-surface-conventions.md` § 8). Rejected: reusing `IActorActivityMonitor` (it would
   feed exchanges to the teardown drain and to a counter documented as "handlers"); an
   `InternalsVisibleTo` from `Vion.Dale.Sdk` to the two packages (ties one shipped package to another's
   internals across independent versions). OUTCOME: decided as written — `IExchangeActivityMonitor`
   in `Vion.Dale.Sdk/Abstractions/`, unmarked; on review.
5. (b) decide-and-document — **how much of `simulator-authoring.md:154` reverses**: both SDK-hosted
   servers and both SDK clients. The HTTP server records a request only after its response is written
   (`TcpHttpServerTransport.cs:327`), after the client can already have finished, so it counts its own
   requests. The Modbus TCP server needs none on one premise: FluentModbus applies a client write and
   raises its change notification before it sends the response [assumed — probed before any code relies
   on it; refuted, it is a STOP]. A block opening a raw socket, and the consumer's
   `SmartLogger3000Simulator` (FluentModbus built directly), stay invisible. OUTCOME: decided as
   written; the premise was probed and held (Drift checkpoint on the FluentModbus probe).

---

## Full design

### What the barrier sees today

The barrier returns on one observation of `in-flight == 0` then `Σ mailbox depth == 0`
(`Vion.Dale.DevHost/Control/QuiescenceBarrier.cs:97-105`), polled every 1 ms of real time
(`:58`, `:89`). The stepper settles before a hop (`DeterministicStepper.cs:96`, `:133`), after each
delivered event (`:112`, `:149`) and after the remainder advance (`:122`). On a stepped host the
dispatcher raises in-flight synchronously when a mailbox is scheduled and lowers it when the run
drains (`Vion.Dale.ProtoActor/DeterministicDispatcher.cs:54-75`), so a handler's follow-up post can
never be observed between counts.

A Modbus TCP request leaves the actor at the channel write (`Vion.Dale.Sdk.Modbus.Tcp/Client/Request/RequestQueue.cs:185`),
runs on the queue's detached consumer (`:94`, `:202-213`), and re-enters only when its callback is
posted with `InvokeSynchronized` (`Request.cs:35-45` → `Vion.Dale.Sdk/Core/LogicBlockBase.cs:172-176`).
HTTP has the same shape: the call yields at `httpClient.SendAsync`
(`Vion.Dale.Sdk.Http/HttpRequestExecutor.cs:315`) and the callback is posted at `:394`. Between the
leave and the post, depth and in-flight both read zero. After `deliver`, the first check usually
fails (the delivery raised in-flight) and pays one 1 ms delay — on Windows ~11 ms, the consumer's
measurement (`logic-block-libraries/docs/process-journal.md:856`, not re-measured) — which is
longer than a loopback round trip. That is the mask the brief names: the reporter's yield-before-delay
experiment ran the consumer corpus in 74 s and failed `askoma-boiler-reheat-duty` 3 of 6 runs.

The SDK-hosted servers answer on transport threads and never post to an actor
(`ModbusTcpServerProxy.cs:93-103` keeps change notifications inside the SDK; `ILogicBlockHttpServer.cs:25`;
`TcpHttpServerTransport.cs:316`, `:327`). A server's effect a block can read — a register a client
wrote, `LastClientWriteAt`, a recorded HTTP request — is read inside the block's own `Sync` on the
block's own cadence, so what a settle must cover is only that the effect has been *stored* by the time
the exchange is over.

### D1 — one counter

Three counters read one after another are not one observation. Read exchanges = 0, then a
just-finished exchange's callback run starts and ends, opens a new exchange and exits, then in-flight
and depth read 0: a false quiescence. So an exchange adds to the **same** atomic count a mailbox run
does. The chain never touches zero: an exchange posts its callback (the post schedules the mailbox,
raising the count) before it closes; a handler opens an exchange while its own run is counted. The
predicate stays `count == 0` then `Σ depth == 0`, and the argument `AC-SCEN-012.5`'s prose already
makes for it carries over unchanged.

The DevHost implementation keeps two counts: the combined one (runs and exchanges) the stepper reads,
and the handler-only one the teardown drain reads (D5). A run raises both; an exchange raises only the
combined one.

### D2 — waking instead of polling

The monitor completes a waiter when the combined count reaches zero. The barrier loop becomes: arm a
waiter; evaluate the predicate; if it holds, return; otherwise await the waiter, a fallback interval,
or the budget, whichever is first — and evaluate again. Arming before evaluating is what makes a
zero-edge between the evaluation and the await impossible to lose.

On a stepped host depth cannot stay above zero once the count is zero: every post schedules its
mailbox, which raises the count until that run drains, and the drain's exit is the edge. The fallback
(order of tens of milliseconds, a constant in the barrier, not a budget) exists for the case that
argument does not cover — a message posted to a mailbox that is never scheduled (a stopped actor) —
where today's poll also never settles and the budget fails the run. It re-evaluates an exact predicate;
it is not a window (constraint 3).

Rejected: **yield-then-spin** (the reporter's experiment) — it burns a core the serial scheduler and
the socket I/O share, which is exactly the 2-vCPU runner criterion 3 worries about; **a shorter
timer** — Windows rounds a sub-15 ms delay up regardless.

### D3/D4 — where accounting lives

A new opt-in interface in `Vion.Dale.Sdk.Abstractions` (working name `IExchangeActivityMonitor`),
shaped for naming what is open: opening returns a handle carrying a short description, disposing it
closes the exchange. The DevHost registers one implementation, in
`DevHostBuilder` beside `InFlightActivityMonitor` (`DevHostBuilder.cs:205-209`), on both clock modes —
only the stepper reads it (D5), so registering it unconditionally changes nothing a wall-clock host
shows.

- **Modbus TCP client** — `RequestQueue` opens a handle before `TryWrite` (`RequestQueue.cs:185`) and
  closes it on every way a request ends: after `ProcessRequestAsync` returns (the request's completion
  — success or error callback — has been posted; a request with no callback has simply finished), in
  the channel's item-dropped callback (`:90`), on a refused write (`:189-194`), and in
  `DropOnDisposal` (`:294-298`). A bounded channel hands each item to exactly one of reader or
  dropped-callback, so each handle closes once. `Vion.Dale.Sdk.Modbus.Tcp.TestKit` replaces the queue
  (`SynchronousRequestQueue`) and is untouched.
- **HTTP client** — `HttpRequestExecutor`'s three public overloads open a handle after the refusals
  (`RefuseUnusableRequest`, `:257-273`, which throw before any exchange exists) and close it when the
  returned task completes; that task completes after the callback post (`:142-164`, `:184-205`,
  `:220-244`). The HTTP test kit wraps the executor (`Vion.Dale.Sdk.Http.TestKit/HeldExchanges.cs:303-380`)
  and registers no monitor.
- **HTTP server** — `TcpHttpServerTransport` opens a handle once a request has been read in full and
  closes it after `Delivered` or on whichever path ends the connection without it (a write that fails,
  the read bound, stop). A refusal written before the full read (503 over the limit, 431, 413, 400,
  411) records nothing and opens none. As first implemented the handle opened at accept; amendment 2
  moved it (Drift checkpoints).
- **Modbus TCP server** — none; reviewer's question 5 carries the premise and its probe.

Every monitor instance lives in one host's service provider, so no state is static and a second host
in the process would not share counts — which is not a claim that concurrent stepped hosts work
(constraint 2).

The pages state the guarantee by what a block author uses — requests made through
`ILogicBlockModbusTcpClient` and `ILogicBlockHttpClient`, and requests served by
`ILogicBlockHttpServer` and `ILogicBlockModbusTcpServer` — never by these types.

### The absent peer (reviewer's question 2)

Today a stepped settle does not wait for a client talking to nothing. Once exchanges count it waits on
the exchange's own real-clock bounds: Modbus connect (3 s default, `LogicBlockModbusTcpClient.cs:51`;
the consumer's askoma scenarios set 5 s), Modbus operation (1 s default, `:310`; the scenarios set 3 s,
real clock at `ModbusTcpClientWrapper.cs:1142`), the HTTP client's 30 s default
(`Vion.Dale.Sdk.Http/ServiceCollectionExtensions.cs:22`). The quiescence budget is 10 s
(`Vion.Dale.DevHost/DevHostBudgets.cs:42`), settable through `WithSafetyBudgets` (`AC-CTRL-013.1`).

Probed this session, a refused connect to a closed loopback port:

| OS | Probe | Result |
|---|---|---|
| Windows 11 Pro 10.0.26200 | PowerShell 7, `TcpClient.ConnectAsync(IPAddress.Loopback, 5999)` then `5998`, nothing listening | `ConnectionRefused` after 2062 ms and 2034 ms |
| Linux 6.6.87.2-microsoft-standard-WSL2 (the `docker-desktop` distro) | busybox `nc -z -w 5 127.0.0.1 5999` | refused, 0 ms |

The Linux probe is the kernel's answer through `nc`, not .NET's `TcpClient`; the implementation
repeated it with .NET in a Linux container (Drift checkpoint on barrier share). What the numbers mean: on Windows every connect attempt
to a dead loopback port costs ~2 s of real time and stays under the budget; the connect backoff
(two failures, then 1 s doubling to 30 s of **virtual** time, `ModbusTcpClientWrapper.cs:23-27`,
`:235-245`) caps how many attempts a stepped run makes. The consumer's `askoma-boiler-comms-watchdog`
re-points a connection at dead port 5999 by design, and its simulator treats `ModbusPort = 0` as
"transport disabled" (`AskomaBoilerSimulator.cs:387`) — both reach this path. An HTTP request with no
per-request timeout to a peer that accepts and never answers exceeds the budget and fails the run.

- **A — wait honestly (recommended).** The settle waits for the exchange, bounded by the existing
  budget; when the budget is spent the failure names each open exchange rather than "the cascade is
  stuck". An exchange's result lands in the step that issued it
  on every OS; a slow refusal costs real time, never virtual time. The author's lever for a longer
  exchange is the budget. Cost: Windows benches that talk to dead ports by design pay ~2 s per attempt
  — criterion 3's consumer run measures it.
- **B — a separate, longer exchange ceiling.** As A, but only-exchanges-open waits against a second
  budget. One more knob, same determinism; buys nothing A's settable budget does not.
- **C — count an exchange only once connected.** Dead-port benches settle as fast as today, but the
  first poll after every (re)connect lands in a thread-timing-dependent step again — the askoma flap
  this change exists to remove. Rejected.
- **D — move the socket bounds onto virtual time on a stepped host.** A virtual connect timeout cannot
  elapse while the settle it would end is waiting on it; letting the stepper advance past an open
  exchange makes the outcome depend on whether the socket answered first. Rejected.
- **E — make Windows refuse fast** (`SIO_TCP_INITIAL_RTO` with zero SYN retransmissions on the client
  socket). Changes the gateway transport, outside a stepped host (constraint 4). Rejected.

### D5 — teardown stays as it is

`DevLogicSystemInitializer` runs the same barrier as a teardown step on both clock modes
(`DevLogicSystemInitializer.cs:315-321`, `:375-380`), and its D3 deliberately does not wait for a
device write enqueued in `Stopping()` (`:302-308`). Counting exchanges there would make every
wall-clock host's stop wait on open sockets (constraint 4). The teardown barrier reads the
handler-only count.

### D6 — fake-clock bounds while an exchange is open

HTTP's per-request timeout is a cancellation source on the registered clock
(`HttpRequestExecutor.cs:280-283`); Modbus's queued-age check and connect backoff read the registered
clock (`DeviceRequest.cs:48-49`, `ModbusTcpClientWrapper.cs:216-232`). On a stepped host the clock
moves only when the stepper advances it, and the stepper advances only after a settle. With exchanges
counted, a queued or open request therefore sees zero virtual time pass: the per-request timeout and
`Expired` cannot occur while it is open, and the backoff — which elapses between requests — is
unaffected. On the real clock and in the test kits nothing changes. The HTTP and Modbus pages state
the stepped consequence; no mechanism is added.

### Other continuations the poll hides (reviewer's question 3)

Checked, as the brief asked:

- `ActorContext.SendToSelfAfter` on a stepped host registers a stepper-delivered send and arms no
  `Task.Delay` (`Vion.Dale.ProtoActor/ActorContext.cs:71-75`); the `Task.Delay` branches (`:82-96`)
  are real-clock only. No window.
- `ActorSystem.SendAndWaitForAcknowledgementAsync` and the stop wait register a **plain** schedule
  entry and arm `ReenterAfter(Task.Delay(timeout, clock))` (`ActorSystem.cs:154-172`, `:346`). When
  the stepper advances to that entry the fake clock completes the delay inside `Advance`, and the
  re-entry posts its continuation afterwards [inferred — Proto.Actor 1.8.0's `ReenterAfter` schedules a
  task continuation; the post's thread and timing are not read]. Between the advance returning and
  that post, count and depth are zero, so a fast settle can return before the timeout's effect — a
  write refused as unacknowledged, a start failed — has landed.
- The Modbus request timestamps and backoff only read the clock; HTTP's timeout source cannot fire
  during a counted exchange (D6). No window.

Recommendation: on a stepped host those two waits register a stepper-delivered entry, as timer sends
do, so the stepper completes the timeout itself and the settle after it sees its effect. Reached only
when a block fails to acknowledge on a stepped host.

### Proof plan

- **Discriminating (the brief's Verify).** A stepped host runs a block that issues one Modbus TCP read
  (and, separately, one HTTP GET) from a timer; the peer is a test-owned `TcpListener` that parks the
  request until the test has taken its read **or** a real-time fallback passes (the
  `scenario-in-flight-reads` shape, `docs/changes/archive/2026-09-14-scenario-in-flight-reads.md`).
  With accounting the advance cannot complete while the answer is parked, the fallback releases it,
  and the value read after the advance is the answer. Without accounting and with D2's fast settle the
  advance completes while parked, the peer releases, and the read is stale — deterministically,
  because the hold makes the window, not the machine. Proven red against D2 with the exchange handle
  disabled, never against origin/main's poll (the brief's first hazard). The fallback is paid on every
  green run.
- **HTTP server.** A SmokeHost block serving through `ILogicBlockHttpServer`, a stepped host, a
  test-owned client; the server's recorded request is visible after the settle that follows the
  client's completion. The window between response and `Delivered` needs a seam to park — the
  transport is DI-resolved (`Vion.Dale.Sdk.Http/ServiceCollectionExtensions.cs:117-119`), so a
  decorating handler in the test's registration is the candidate; if no seam can park it, it is a
  surviving mutation reported with the observable that is carried (`spec-process.md` § 5).
- **Modbus TCP server premise.** A probe over FluentModbus 5.3.2: a client write, then the server's
  `LastClientWriteAt` read immediately after the client's write completes, repeated; and a reading of
  the package's request handling. Recorded with its OS in Drift checkpoints before any criterion rests
  on it.
- **Barrier share, before and after.** A local, uncommitted stopwatch around the barrier wait on
  origin/main and on the branch, over `Vion.Dale.DevHost.Test`'s stepping suites and one SmokeHost
  scenario; wall time against barrier time, on Windows here and on Linux through the PR's CI run.
- **Absent peer.** Per the operator's answer to question 2.
- **Criterion 3** routes to the coordinator: packages packed from the branch, the consumer corpus run
  on Windows against them.

---

## Drift checkpoints

> One line per divergence discovered during implementation:
> `YYYY-MM-DD: <what changed and why>`. Never inline in a spec page. A checkpoint that fixes a
> CLASS bug states the sibling sweep (done / N/A / handed off).

- 2026-09-15: the brief assumed a refused loopback connect takes ~2 s on Windows and asked for a probe
  on Linux too — probed (§ The absent peer): Windows 2034–2062 ms, Linux 0 ms (through busybox `nc`,
  not .NET; repeated with .NET in a Linux container: 18 ms and 0 ms).
- 2026-09-15: the brief's "Other off-schedule continuations" named `ActorContext.cs:84-96` as a
  bypass; on a stepped host that branch is not taken (`:71-75`). The window is real only for the two
  `ActorSystem` timeout waits (reviewer's question 3).
- 2026-09-15: FluentModbus premise (reviewer's question 5) probed and held. Probe: a
  `ModbusTcpServer` 5.3.2 with `EnableRaisingEvents` and `AlwaysRaiseChangedEvent`, a
  `RegistersChanged` handler that parks until released, a `ModbusTcpClient` on loopback; after the
  handler entered, the client's write was checked for completion over 500 ms. FC6 5 of 5 and FC16 3 of 3
  runs: the write did not complete while the handler was parked. Windows 11 10.0.26200, .NET 10. The
  ordering is the library's request handling, not a socket behaviour, so it was not repeated on Linux.
- 2026-09-15: question 3's timeout waits are not reachable from an advance. The two plain schedule
  entries are registered only by `ActorSystem.SendAndWaitForAcknowledgementAsync` and
  `StopActorsAndWaitAsync` (`Vion.Dale.ProtoActor/ActorSystem.cs:154`, `:346`), whose only callers in
  the repository are the host's start, restore and stop sequences
  (`Vion.Dale.DevHost/DevLogicSystemInitializer.cs:187`, `:223`, `:286`, `:296`, `:326`, `:342`); no
  block, library or example calls either (a grep over `examples/`, `libraries/`,
  `Vion.Dale.DevHost.Web/` and the consumer checkout found only a comment,
  `BatterySystemHuaweiLuna2000Should.cs:2454`). The acknowledgement path unregisters its entry when the
  last answer arrives (`ActorSystem.cs:192`), so after a completed start nothing of it is left for an
  advance to hop to. The one shape that reaches an entry is an advance issued concurrently with a start
  or stop in which a block never answers, and the effect there is that sequence's own failure, decided by
  its real-clock backstop (`DevLogicSystemInitializer.cs:200-207`), which no settle governs. No red test
  was written; `T-006` has no code.
- 2026-09-15: the proof's fallback is 500 ms, not the 2 s the Proof plan drafted — the Modbus client's
  one-second default operation timeout ended a 2 s hold as a failed read (first run of
  `SocketExchangeSteppingShould.DeliverModbusReadIssuedDuringAdvanceBeforeAdvanceReturns`).
- 2026-09-15: the stepped fixtures are test blocks in `Vion.Dale.DevHost.Test`
  (`Stepping/SocketExchangeFixtureBlocks.cs`) rather than SmokeHost blocks: the tests need a held
  peer and a short budget, which a committed topology cannot carry.
- 2026-09-15: the HTTP server's window between writing the response and recording the request has no
  seam in a stepped host — the transport is internal and the server's only DI-visible collaborator is
  its clock. `AC-HTTP-018.2` is carried end to end without a hold
  (`SocketExchangeSteppingShould.RecordServedRequestFromClientInSameHostBeforeClockNextAdvances`), and
  both server mutations (no exchange for a read request; exchange closed before `Delivered`) survive there; the
  ordering is pinned by uncited premise tests in `TcpHttpServerTransportExchangeShould`, which the same
  two mutations redden.
- 2026-09-15: a Modbus TCP exchange is named by its operation only (`Modbus TCP
  ReadHoldingRegistersAsShort`), not by endpoint: the queue that opens it does not know the client's
  address, and carrying it there changes `IRequestQueue`, which the Modbus TCP test kit implements.
  HTTP client exchanges carry the method and URL; a hosted server's carries the method, the path and the
  server's local endpoint.
- 2026-09-15: the quiescence failure message now renders its budget in the invariant culture; it
  rendered in the current culture before, so a German-locale host wrote `0,4s`.
- 2026-09-15: barrier share, before and after. Fixture: a stepped host with one `TickerBlock`
  (`[Timer(1)]`), warmed by a 1 s advance, then one `AdvanceAsync(2000 s)` timed — 2034 settles, 2001
  ticks; an uncommitted stopwatch around the stepper's settle, applied identically to origin/main
  (7c8343a1) and to the branch. Windows 11 10.0.26200: origin/main 22805–26233 ms wall, 99.8–99.9% in
  the barrier; branch 63–69 ms wall, 81–89% in the barrier. Linux (`mcr.microsoft.com/dotnet/sdk:10.0`
  on Docker Desktop, kernel 6.6.87.2): origin/main 8168–10680 ms, 99.7%; branch 60–70 ms, 89–90%.
  Three runs each. The share stays high after the change because in this fixture every handler runs
  while a settle is waiting for it, so the barrier's time is the actor work itself; the idle part is
  what fell, from ~11 ms (Windows) and ~4–5 ms (Linux) per settle to ~30 µs including the handler.
  The same container ran `SocketExchangeSteppingShould` and `QuiescenceBarrierShould` green (9 of 9)
  and repeated the refused-connect probe through .NET's `TcpClient`: `ConnectionRefused` after 18 ms and
  0 ms. The consumer journal's 99.7% figure is a whole lane; a lane was not re-run here.
- 2026-09-15: how the figures above were produced, so they can be re-run.
  - Stopwatch patch, applied to `Vion.Dale.DevHost/Control/DeterministicStepper.cs` on each tree and never
    committed: `SettleAsync` renamed `SettleCoreAsync`, and a new `SettleAsync` wraps it in
    `Stopwatch.GetTimestamp()` before and after, adding the difference to a `public static long
    MeasureBarrierTicks` and incrementing `MeasureSettles`.
  - Measurement test, also uncommitted, `Vion.Dale.DevHost.Test/Stepping/BarrierShareMeasure.cs`: builds the
    `TickerBlock` stepped host above, calls `AdvanceAsync(1 s)`, zeroes both counters, times
    `AdvanceAsync(2000 s)` with a `Stopwatch`, and prints wall time, `MeasureBarrierTicks` as milliseconds,
    `MeasureSettles` and the `Ticks` property.
  - Run, three times per tree: `dotnet test Vion.Dale.DevHost.Test --no-build --filter
    "FullyQualifiedName~BarrierShareMeasure" --logger "console;verbosity=detailed"`. origin/main came from
    `git worktree add --detach <scratch> origin/main`.
  - Linux: each tree tarred without `bin`/`obj`/`.git`, then `docker run --rm -v <scratch>:/in
    mcr.microsoft.com/dotnet/sdk:10.0 bash /in/linux-measure.sh`. The script untars each tree, runs
    `dotnet build Vion.Dale.DevHost.Test`, the three measurement runs above, and on the branch
    `dotnet test Vion.Dale.DevHost.Test --no-build --filter
    "FullyQualifiedName~SocketExchangeSteppingShould|FullyQualifiedName~QuiescenceBarrierShould"`, whose
    summary line read `Passed! - Failed: 0, Passed: 9`. It then `dotnet run`s a console program that calls
    `new TcpClient().ConnectAsync(IPAddress.Loopback, port)` for ports 5999 and 5998 and prints the
    `SocketErrorCode` with the elapsed milliseconds.
  - FluentModbus probe: a console program referencing `FluentModbus` 5.3.2, run with `dotnet run -c Release`.
    Five `WriteSingleRegister` and three `WriteMultipleRegisters` iterations, each printing whether
    `write.Wait(500 ms)` returned true after the parked handler had entered; all eight printed `False`. The
    same ordering is now a committed premise test, `FluentModbusWriteOrderingShould`.
- 2026-09-15: amendment 2 (operator) moved the HTTP server's exchange from accept to the end of the
  request's full read. Accept-time counting let an idle connection from outside the host — a probe, a
  client that never sends — hold every stepped settle for up to the 10 s read bound. Recording
  (`Delivered`) runs after the full read and the response write, so opening anywhere between them stays
  exact for a client in the same host, whose own exchange is open until its callback is posted.
  Refusals written before the full read record nothing and now open no exchange. Known limit
  (inferred, not reproduced): a client in the host that gives up on its request — its own timeout —
  before the server has read it in full closes its exchange before the server opens one, and the
  server's recording is then not waited for; stated on the page. Proof:
  `TcpHttpServerTransportExchangeShould.OpenNoExchangeForIdleConnectionsAndOneForRequestReadInFull` (red
  with the open at accept or before the read); `CloseExchangeOfConnectionRefusedOverLimit` became
  `OpenNoExchangeForConnectionRefusedOverLimit`, asserting `Opened == 0` where it asserted `1`. `AC-HTTP-018.2`'s
  text is unchanged; the page's prose, this doc and the interface's summary were brought to the new point
  after the archive commit, and the archive gate was re-run against a slug-renamed copy under
  `docs/changes/`.

---

## Spec delta (to distill)

> The machine-readable change, one line per id. Grammar:
> `<OP> <ID> -> <target> : <payload>`
>
> - `OP` ∈ `ADDED` | `MODIFIED` | `REMOVED`
> - `target` is **repo-root-relative** (normally `docs/specs/<page>.md`)
> - `payload`: for `ADDED`/`MODIFIED` the EARS text; for `REMOVED` the reason
>
> On the implementation PR each line is applied into the named target; `spec-change.ps1 archive`
> refuses until every line is applied. The `ID` must be an exact token greppable in the target
> after distill (backticks stripped) — a real `AC-`/`SYS-` id, never an ad-hoc label.

Ratified by amendment 1 (2026-09-15): `AC-SCEN-012.5`, `AC-SCEN-012.11`, `AC-MODB-020.1` and `AC-HTTP-018.1` as written. `AC-HTTP-018.2`'s text is the session's, brought to the PR review: the server's handle opens once a request is read in full, so a request from a client outside the host read after a settle has already observed zero is not waited for; a client in the same host holds its own handle open until the server has read its request.

- MODIFIED AC-SCEN-012.5 -> docs/specs/scenarios.md : THE SYSTEM SHALL treat the actor system as quiescent exactly when every mailbox is empty, no handler is in flight, and no Modbus TCP or HTTP exchange started or served through the SDK is still open.
- ADDED AC-SCEN-012.11 -> docs/specs/scenarios.md : IF the quiescence budget is spent while an exchange is still open THEN THE SYSTEM SHALL name each open exchange in the failure.
- ADDED AC-MODB-020.1 -> docs/specs/modbus.md : WHILE the host is stepped THE SYSTEM SHALL deliver a Modbus TCP request's completion to the block before the virtual clock next advances and before a stepped advance in progress returns.
- ADDED AC-HTTP-018.1 -> docs/specs/http.md : WHILE the host is stepped THE SYSTEM SHALL deliver an HTTP request's callback to the block before the virtual clock next advances and before a stepped advance in progress returns.
- ADDED AC-HTTP-018.2 -> docs/specs/http.md : WHILE the host is stepped THE SYSTEM SHALL record a request the hosted server answered for a client in the same host before the virtual clock next advances and before a stepped advance in progress returns.

---

## Tasks

> One-commit tasks, each tagged with ≥1 AC id. Ephemeral — they live and die with this change doc.
> Plain list, no checkboxes — the PR's per-task commits are the completion record.

- `T-001` (`AC-SCEN-012.5`): the exchange monitor abstraction, the DevHost's combined and handler-only
  counts, and the barrier's wake (D1, D2, D5) — the discriminating tests written first with the handle
  disabled.
- `T-002` (`AC-MODB-020.1`): Modbus TCP client accounting in the request queue.
- `T-003` (`AC-HTTP-018.1`): HTTP client accounting in the executor.
- `T-004` (`AC-HTTP-018.2`): HTTP server accounting in the transport; the Modbus TCP server premise probe.
- `T-005` (`AC-SCEN-012.11`): the named-exchange failure.
- `T-006`: none — question 3 is not reachable from an advance.
- `T-007`: prose in `devhost-control.md`, `http.md`, `simulator-authoring.md`; barrier-share
  measurement; distill and archive.

## Relay notes for the PR body

- A stepped DevHost settle no longer polls on a 1 ms real-clock delay; it wakes when the host's
  activity count reaches zero. Stepped runs that spent their time in that delay run many times faster.
- A stepped settle waits for Modbus TCP and HTTP requests a block makes through the SDK, and for
  requests a block's hosted HTTP server read from a client in the same host, until each result has
  reached the block (`AC-SCEN-012.5`, `AC-MODB-020.1`, `AC-HTTP-018.1`, `AC-HTTP-018.2`). Stepped
  benches over SDK clients and SDK-hosted servers on loopback are supported; a socket a block opens any
  other way is still not seen.
- Behaviour change on stepped hosts: a client talking to an absent or silent peer now holds the settle
  until its own real-clock bound ends the request — on Windows a refused loopback connect takes about
  2 s. In the consumer corpus the one scenario that re-points a connection at a dead port by design,
  `askoma-boiler-comms-watchdog`, went from 13.1 s to 18.8 s while the corpus as a whole went from
  1,687 s to under a minute. A bound longer than the quiescence budget fails the advance, and the
  failure names each open exchange (`AC-SCEN-012.11`); raise the budget with `WithSafetyBudgets`.
- While a counted request is open on a stepped host, virtual time does not move, so an HTTP per-request
  timeout and the Modbus maximum queued age cannot elapse during it.
- New public, unmarked opt-in interface `Vion.Dale.Sdk.Abstractions.IExchangeActivityMonitor`. Nothing
  outside a development host registers one; the gateway runtime and the test kits are unchanged.
