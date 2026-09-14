---
slug: devhost-next-free-port
status: in-flight
blocked-on: none
areas: CTRL, CLI
author: jonasbertsch
created: 2026-09-14
updated: 2026-09-14
supersedes: none
---

# DevHost binds the next free port

> Change doc — one in-flight change. The `Spec delta` below is distilled into the current-truth
> pages under `docs/specs/` in the implementation PR, then this doc is archived
> (`pwsh scripts/spec-change.ps1 archive <slug>`). Process: `docs/spec-process.md`. Never put
> change narrative inline in a spec page — it lives here.

## At a glance

### Summary

Several DevHosts run side by side on one machine with no configuration and no `Program.cs` edit
(VION-215). A host whose preferred port is taken binds the next free one above it, and every surface
that names the port — the browser, the readiness line, the failure receipt, the console's address and
scenario deep links — names the port actually bound. `dale dev` stops announcing a port it cannot
know. A recycle keeps the port the first generation bound.

### Spec implications

`devhost-control.md`: `AC-CTRL-002.6` reverses (a taken preferred port is walked past, not refused);
`AC-CTRL-014.1` stops saying "configured port"; `AC-CTRL-005.4` states that later generations rebind
the first generation's port; a new `AC-CTRL-005.10` states what happens when that rebind fails;
`AC-CTRL-005.6`, `AC-CTRL-006.2` and `AC-CTRL-006.8` name the bound port. `cli.md`: `AC-CLI-009.9`
and the note after it — the announcement names no port. `AC-CLI-010.1` / `AC-CLI-010.4` keep their
text (`D5`).

### Decisions

- `D1` — **Walk from the preferred port, bounded at twenty ports.** The preferred port is
  `WithWebUi(port)`'s value (default 5000). The host binds the first of it and the nineteen above it
  that binds on both loopback families; when none does, the start fails naming the range. — Ratified
  constraint 1 (next free port, no setting); the bound is this session's (b): a failing start must
  eventually fail, and twenty covers every side-by-side desk while keeping the exhaustion refusal a
  test can reach.
- `D2` — **Try the real bind; never probe.** A candidate port is tried by starting Kestrel on it; a
  bind failure disposes that attempt and tries the next. — A probe socket answers "free" for a port
  held on only one loopback family, which Kestrel's dual-family `ListenLocalhost` then fails on, and it
  races whoever binds between probe and bind. (b)
- `D3` — **The runner reports the port the web host bound.** Both entry points — the prebuilt-host
  `RunAsync(IDevHost, port)` and the supervised loop — read the bound port from the host they started.
  The runner's own `port` argument names the port only where the host serves no web UI of its own (no
  `WithWebUi`), and in a failure receipt written before any generation bound one. — Ratified
  constraint 3. The argument stays in every signature, because consumer `Program.cs` files pass it and
  must keep compiling unchanged (`Leave alone: consumer repos`). No public member is added: the bound
  port travels host → runner inside `Vion.Dale.DevHost.Web`, keyed on the host's control surface. (b)
- `D4` — **A later generation rebinds exactly the first generation's port, or fails.** The supervisor
  pins generation 1's bound port for every later generation; a pinned host does not walk. If something
  took the port in the release window, that generation fails with the port named, and — nothing being
  recyclable onto — the process ends with the failure receipt (`AC-CTRL-005.6`). — Ratified
  constraint 4 (keep the port). Walking instead would keep the process alive but leave the open page,
  a waiting `dale scenario run` and a smoke script polling the old port all addressing whatever took it
  — most plausibly another DevHost that walked into the gap. Failing loudly is the non-silent answer.
  (b, delegated by the brief)
- `D5` — **The scenario verbs keep `--port` defaulting to 5000.** Their option description and their
  "no host reachable" refusals now say a host started while its preferred port was taken serves on the
  port it printed. — The brief delegated this with "no new public surface". Every answer that would
  find *this project's* host without `--port` adds surface: a port file the host writes and the CLI
  reads (a new file format), a project identity on `/api/control/status` the CLI scans ports for (a new
  wire field), or a changed default. So the default stands and the gap is written down; a bare verb
  against a second host is non-silent in the common case, because `apply` resolves the scenario id in
  the *host's* scenarios directory and a different project's host answers not-found. The surface-adding
  alternatives are this change's Reviewer's question 1. (b)
- `D6` — **Smoke scripts learn their own host's port and stop their own process.** `smoke-modbus.ps1`
  and both smoke skills read the readiness line of the process they started and tear down by that
  process id, never by "whatever listens on 5000". The fixed Modbus simulator port `15020` cannot walk;
  a held `15020` is refused naming the owning process rather than killed. `smoke-modbus.ps1 -Port` is
  deleted — the host chooses its port. (b)
- `D7` — **Other 5000 mentions** say "5000, or the next free port" (READMEs, the template's
  `AGENTS.md`/`README.md`, `dale new`'s hint) or need nothing (example and library `Program.cs`
  `Port = 5000`, the runner's defaults — the walk makes them a starting point). (b)

### Reviewer's questions

1. **(c) — follow-up, not a STOP.** `D5` leaves a bare `dale scenario validate`/`schema` able to read
   a *different* project's host on 5000 (`schema` enriches silently with its names). Options:
   (a) leave as `D5` — no surface; (b) the host writes `.dale/devhost.port` (or similar) in its
   working directory and the verbs default `--port` from it — one small file format, fixes the common
   case; (c) `/api/control/status` carries the host's working directory and the verbs pick the host
   whose directory matches — a wire field plus a port scan. **Recommendation: (b)**, as its own backlog
   item. OUTCOME: (pending the operator)
2. **(b)** — `D1`'s bound of twenty. OUTCOME: (pending review)
3. **(b)** — `D4`: fail rather than walk when the pinned port is taken during a recycle. OUTCOME:
   (pending review)
4. **(b)** — `D3`: the runner's `port` argument now only names the port for a host without a web UI.
   It is dead weight for every template-shaped `Program.cs`; deleting it breaks every consumer's
   compile, so it stays, documented. OUTCOME: (pending review)

---

## Full design

### Where the port went, before

- **Bind:** `WithWebUi(int port = 5000)` stores it (`DevHostBuilderExtensions.cs:15,25`);
  `WebHostService` binds via `ListenLocalhost` (`Services/WebHostService.cs:69`) and turns the in-use
  `IOException` into "could not bind port … start this one on a different port" (`:227-238`).
- **Report:** the runner's own `port` argument — readiness (`DevHostWebRunner.cs:73,281`), failure
  receipt (`:238,265`), browser (`:77,286`). The template passes `Port = 5000` to the runner and calls
  `.WithWebUi()` bare (`templates/vion-iot-library/VionIotLibraryTemplate.DevHost/Program.cs:11,30,36`),
  so the two numbers were only equal by coincidence.
- **Printed before the bind:** "DevHost Web UI running at" and the deep links
  (`WebHostService.cs:198,205`, before `StartServerAsync` at `:209`); `dale dev`'s banner, hardcoded
  (`DevCommand.cs:149-156`).

### After

`WebHostService.StartAsync` loops over candidate ports. Each attempt builds the `WebApplication`
pipeline with Kestrel on that port and starts it; an `IOException` from the start (Kestrel's bind
failure, on either loopback family) disposes that attempt and moves on. The first attempt that starts
is kept, its port recorded, and only then are the address and the deep links printed. With a pinned
port (a later generation) the loop has one candidate.

The bound and pinned ports live in an internal per-host record in `Vion.Dale.DevHost.Web`, looked up by
the host's `IDevHostControl` — the one object both the runner (`IDevHost.Control`) and the hosted
service (constructor injection, the same singleton) hold. Nothing public is added.

`DevHostWebRunner`:

- prebuilt-host overload: after `StartAsync`, `servingPort = bound ?? port`; readiness and browser use it.
- supervised loop: generation 1's bound port is captured as `servingPort`; every later generation's host is
  pinned to it before `StartAsync`. Each readiness line and the browser name the port *that generation*
  bound; failure receipts name `servingPort` once set, else the argument.

### Recycle window

`host.StopAsync` then a 250 ms delay (`DevHostWebRunner.cs:320-321`) — unchanged. A process that binds
the port in that window makes the next generation's single bind attempt fail; the start throws
`InvalidOperationException` naming the port, the supervisor prints the failure receipt and the process
ends (`D4`). Under a topology switch the same failure is first treated as a recoverable topology
failure and falls back once onto the running topology, which fails the same way and ends the process.

### `dale dev`

`DescribeStartup` no longer names a port: `  Web UI — its address is printed once it is serving` /
`  Control API, no browser — the readiness line names its port`.

### Considered and not taken

- Probe-then-bind (`D2`).
- Walk on recycle (`D4`).
- A `dale dev --port` or environment variable — excluded by ratified constraint 1.

---

## Drift checkpoints

- 2026-09-14: Kestrel's `ListenLocalhost` fails the whole bind with `IOException` when the port is held on
  **either** loopback family — probed on Windows 11 with a `TcpListener` on `[::1]` only
  (`ServeOnNextFreePortWhenPreferredOneIsHeld`, row "held on the IPv6 loopback only", red before the fix
  with `Failed to bind to address http://[::1]:…: address already in use`). This is what `D2` rests on.
  Unproven on Linux: CI's runner exercises the same rows.
- 2026-09-14: each failed bind attempt logged `Hosting failed to start` with a stack trace from the
  generic host. The `Microsoft.Extensions.Hosting.Internal.Host` category is filtered to `Critical` on
  the per-attempt web application, and the walk prints one line per port it passes instead. A genuine
  start failure still reaches the caller as the thrown exception.
- 2026-09-14: the first cut of the supervised readiness line named `servingPort` (the pinned port) rather
  than the port the generation bound, so a regression that stopped pinning still printed the old port —
  mutation M5 survived `RebindLaterGenerationOnPortFirstGenerationBound`. The line now names each
  generation's own bound port, and the test also answers a request on that port. Sibling sweep: the
  browser URL had the same shape and moved with it; the prebuilt-host overload already read the host's
  own bound port; the failure receipt deliberately names `servingPort` (`AC-CTRL-005.6`).
- 2026-09-14: `FailLaterGenerationNamingPortWhenItWasTakenDuringRecycle` awaited the runner unbounded, so
  a mutation that let the generation walk (M5, M6) hung the suite instead of failing it. The wait is now
  bounded and the runner is cancelled in `finally`.
- 2026-09-14: `D7` sweep result, from `git grep -n "5000" -- ':!**/bin/**' ':!**/obj/**' ':!**/*.min.js'`
  minus unrelated hits: edited — `README.md`, five `examples/*/README.md`, `templates/vion-iot-library/AGENTS.md`
  (two sites) and `README.md`, `NewCommand.cs`'s hint, `Vion.Dale.DevHost.SmokeHost/Program.cs`'s summary,
  `ScenarioCommand.cs`'s `--port` description; nothing needed — `Port = 5000` in ten example and one library
  `Program.cs`, the template's `Program.cs`, the runner and `WithWebUi` defaults (a starting point now),
  `ScenarioCommandTests.cs` / `TopologyCommandTests.cs` (the default is unchanged). The example READMEs
  describe the published package they reference, and become true when the examples are bumped to the
  release carrying this change.
- 2026-09-14: the brief's hit list named `DevHostWebRunner.cs:55,105,118,140` as defaults to handle; they
  stay — the argument is the fallback `D3` describes, and removing the default breaks no one but changes
  every signature in the PublicApi manifest for nothing.

---

## Spec delta (to distill)

- MODIFIED AC-CTRL-002.6 -> docs/specs/devhost-control.md : WHEN the preferred port is already bound THE SYSTEM SHALL bind the first free port among the nineteen above it, and SHALL fail the start with a message naming the range when none of them is free.
- MODIFIED AC-CTRL-014.1 -> docs/specs/devhost-control.md : THE SYSTEM SHALL bind the web host on loopback only.
- MODIFIED AC-CTRL-005.4 -> docs/specs/devhost-control.md : THE SYSTEM SHALL rebind every later generation on the port the first generation bound, letting the previous generation release it first, and SHALL announce each recycle on standard output naming the generation.
- ADDED AC-CTRL-005.10 -> docs/specs/devhost-control.md : IF a later generation cannot rebind the port the first generation bound THEN THE SYSTEM SHALL fail that generation naming the port rather than bind another.
- MODIFIED AC-CTRL-005.6 -> docs/specs/devhost-control.md : WHEN a generation that nothing can be recycled back onto fails THE SYSTEM SHALL print a machine-readable failure receipt before the process ends, naming the port the process was serving on, or the port it was given when no generation bound one.
- MODIFIED AC-CTRL-006.2 -> docs/specs/devhost-control.md : THE SYSTEM SHALL open a browser at the bound address when not headless and print a machine-readable readiness line naming the bound port when it is, in one shape whichever entry point is serving.
- MODIFIED AC-CTRL-006.8 -> docs/specs/devhost-control.md : THE SYSTEM SHALL print the bound web address and one deep link per discovered scenario on it before the readiness line, marking a scenario that could not be parsed rather than omitting it.
- MODIFIED AC-CLI-009.9 -> docs/specs/cli.md : THE SYSTEM SHALL announce what it is about to do — a web UI, a control API, or a one-shot export that starts no server — and SHALL name no port, which only the host knows once it has bound one.

---

## Tasks

- `T-001` (`AC-CTRL-002.6`, `AC-CTRL-014.1`, `AC-CTRL-006.8`): walk the bind in `WebHostService`, print the address after it.
- `T-002` (`AC-CTRL-006.2`, `AC-CTRL-005.4`, `AC-CTRL-005.6`, `AC-CTRL-005.10`): the runner reports the bound port on both entry points and pins it across recycles.
- `T-003` (`AC-CLI-009.9`): `dale dev`'s announcement; the scenario verbs' description and refusals (`D5`).
- `T-004`: smoke skills and `smoke-modbus.ps1` (`D6`); the other 5000 mentions (`D7`).
- `T-005`: distill, archive.

## Relay notes for the PR body

- A DevHost whose preferred port is taken now binds the next free port (up to nineteen above it)
  instead of failing; its readiness line, browser, console address and deep links name that port.
- A recycle keeps the port; if another process took it in the recycle window the host exits with the
  failure receipt naming it.
- `dale dev` no longer prints `http://localhost:5000` before the host has bound anything.
- `dale scenario` verbs still default `--port` to 5000 — pass the port a second host printed.
- `scripts/smoke-modbus.ps1` loses `-Port`; it reads its host's port from the readiness line.
