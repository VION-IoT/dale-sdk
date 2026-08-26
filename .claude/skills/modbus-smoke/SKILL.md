---
name: modbus-smoke
description: Smoke-test the Modbus client surface end to end against a real client/server pair on localhost. Tier 1 (headless, one script) runs the committed scenarios through the DevHost control API; Tier 2 (live, chrome-devtools) eyeballs the Link / Connection structs in the browser while the host is up. Use after changing anything under Vion.Dale.Sdk.Modbus.* or either Modbus example, and after a release bump touches them.
---

# Modbus smoke test

The Modbus TCP example is a genuine client/server pair — `ModbusTcpSimServer` binds `127.0.0.1:15020`
(loopback only, deliberately: a wildcard bind would answer on every `127.0.0.x`, so a "wrong" address
would still reach it) and `ModbusTcpDebugClient` talks to it over a real socket. That makes it the only
place in this repo where the SDK's link policy can be exercised for real: a refused connect, a peer
that goes away, a backoff that actually elapses. The TestKit's fake client proxy answers from a
register store and can prove none of it.

Run this after changing `Vion.Dale.Sdk.Modbus.Core` / `.Tcp` / `.Rtu`, after changing
`examples/Vion.Examples.ModbusTcp` or `examples/Vion.Examples.ModbusRtu`, and after a release bump
moves those examples onto a new SDK version.

## The one gotcha: real clock

The DevHost's stepped mode drives the SDK's `TimeProvider`, but the TCP client's **sockets, connect
timeout and operation timeout are real time**. Under a virtual clock a connect backoff never elapses
and every `RoundTrip` reads zero, so a stepped run proves nothing about the link policy and hangs on
the waits. Never set `DALE_DEVHOST_STEPPED` for this. The script refuses to run against a stepped
host, and the scenarios' waits are real waits.

## Tier 1 — headless, the whole policy (~1 minute)

```bash
pwsh scripts/smoke-modbus.ps1
```

Builds the example DevHost against the **published** `Vion.Dale.*` packages, boots it headless on the
real clock, resets the host before each scenario, and runs every committed scenario in
`examples/Vion.Examples.ModbusTcp/scenarios` through the control API. Prints the run report, exits
non-zero unless each reaches `succeeded`, and frees ports 5000 and 15020 either way.

Expect **~1 minute wall**: `modbus-healthy` finishes in about a second, `modbus-link-policy` takes
about 30 s because every wait in it is a real wait, and the rest is the build.

To verify a working-tree SDK change against the example *before* a release:

```bash
pwsh scripts/smoke-modbus.ps1 -LocalSource
```

Same scenarios, `-p:DaleLocalSource=true` — the example builds against the local SDK projects instead
of the published packages. This is the point of the script being rerunnable: an SDK PR can prove it
did not break its first consumer without cutting a release first.

One scenario at a time, against an already-built example:

```bash
pwsh scripts/smoke-modbus.ps1 -Scenario modbus-link-policy -NoBuild
```

### What the scenarios assert

- **`modbus-healthy`** — the floor: socket `Connected`, link `Online`, polls landing, the watch slot
  decoding π out of input registers 6-7, a receipt carrying the wire time, and zero timeouts,
  transport errors and drops.
- **`modbus-link-policy`** — the whole v0.10.4 policy in one run: re-supplying the entire unchanged
  `ConnectionSettings` struct is a no-op (the socket is never dropped), an unreachable port faults the
  link and arms the connect backoff with a `nextAttemptAt`, requests then fail fast as `BackedOff`, a
  corrected port ends the backoff and reconnects, a wrong loopback address (`127.0.0.2`) is refused
  because the simulator binds one address rather than `0.0.0.0` — the regression guard for that bind —
  stopping and restarting the simulator's listener
  recovers with **no operator action on the client**, and the client driven at its minimum poll interval
  with every watch slot active stays `Online` — a local backlog is not a device fault. The headline pill
  is asserted alongside the two summaries at each transition.

`MaxQueuedAge` and the `Expired` outcome are deliberately **not** asserted: the simulated server
answers in well under a millisecond, so no cadence the client can generate builds a queue deep enough
to age a request out. That one is a Tier-2 observation.

**If it fails:** the printed step `detail` is the diagnosis (`expected Link above 1, but was 1`).
Re-run just that scenario with `-Scenario <id>`, or boot the host yourself and watch the two structs:

```bash
curl -s http://localhost:5000/api/state/DebugClient/Link
```

## Tier 2 — live UI while the host is up (optional, chrome-devtools)

Tier 1 never loads the page. To see the release's headline surface render, boot the host and drive the
browser:

```powershell
$dir = "$(git rev-parse --show-toplevel)\examples\Vion.Examples.ModbusTcp\Vion.Examples.ModbusTcp.DevHost"
dotnet build $dir --nologo
$env:DALE_DEVHOST_NO_BROWSER = "1"
Start-Process dotnet -ArgumentList "$dir\bin\Debug\net10.0\Vion.Examples.ModbusTcp.DevHost.dll" -WorkingDirectory $dir -WindowStyle Hidden
```

Poll `http://localhost:5000/api/control/status` until it answers (`stepped:false`), then navigate to
`http://localhost:5000` and check:

- `DebugClient` → **Status** shows one pill, `LinkHealth`, folded from `Link.State` and
  `Connection.State`; **Diagnostics** renders `Link`, `Connection` and `Command link` as structs — every
  field labelled, no row of hand-kept counters — plus *Last read at* as a relative time and the *Last
  round trip* chart.
- `DebugClient` → **connection** renders the two editable structs, *Connection* and *Link policy*, each
  with a **Form** / **Raw JSON** editor: rows are labelled by their `[StructField]` title, the int
  fields carry their ranges (`1 – 65535`, `0 – 255`), `address` gets an `ipv4` hint, the `TimeSpan`
  fields take `3s` as well as `PT3S`, and the nullable *Max queued age* has an ∅ toggle. In the
  **Diagnostics** viewer the SDK's own summaries are labelled too (`Link state`, `Round trip (last)`,
  scaled to `910 ms` rather than `PT0.91S`) — all of it since 0.10.5. An enum-typed struct field
  keeps the CLR type name in its schema title, so the three of them take their label from
  `presentation.fields` instead (VION-105): `Link.state` reads *Link state*,
  `Link.lastFailureOutcome` reads *Last failure outcome*, `Connection.state` reads *Connection
  state*, and all three render as severity-coloured pills. Two of the value labels are the
  discriminating ones, because they differ from their member names: `Connection.state` reads
  `Backing off (BackingOff)` and `lastFailureOutcome` reads `Device error (DeviceError)`.
  `ModbusLinkState`'s own labels all equal their member names, so `Link.state` proves the pill and
  the field label but says nothing about value labels.
- Open the **Player**, run `modbus-link-policy`, and watch `Connection.state` move
  `Connected → Disconnected → BackingOff → Connected`, `Link.state` move `Online → Faulted → Online`,
  and the pill follow them `Online → Faulted → Backing off → Online`. The watch panel pins all three.
- The one thing Tier 1 cannot assert: set *Max queued age* to `1` ms by hand while the poll and watch
  intervals are at their minimum, and watch `Link.expiredCount` tick — then confirm `Link.state` stays
  `Online`, because an expiry is a local outcome and never faults the device.
- The pill's one non-SDK state: turn *Connection enabled* off and it reads **Disabled** while `Link`
  still shows the last verdict — the block overlays what only it knows, without rewriting the snapshot.
  Turning *Polling enabled* off instead must **not** produce `Disabled`: the connection is still up, so
  the last verdict holds and *Last read at* is the staleness signal.

Tear down:

```powershell
Get-NetTCPConnection -LocalPort 5000,15020 -State Listen -EA SilentlyContinue | ForEach-Object { Stop-Process -Id $_.OwningProcess -Force -EA SilentlyContinue }
```

## CI

`Vion.Examples.ModbusTcp.IntegrationTest` runs the same two committed scenario files headlessly
through `ScenarioRunner`, so `dotnet test Vion.Dale.Sdk.sln` guards the link policy on every push —
against the published packages, which is what a release smoke should test. It adds about 35 s to the
suite, nearly all of it the link-policy scenario's real waits. Select just it with:

```bash
dotnet test examples/Vion.Examples.ModbusTcp/Vion.Examples.ModbusTcp.IntegrationTest --filter "Category=Smoke"
```

The script and the test run the **same scenario files** — there is no second copy to keep in step. Add
a scenario to `examples/Vion.Examples.ModbusTcp/scenarios/` and the script picks it up automatically;
add a `[Fact]` calling `RunScenarioAsync("<id>")` to put it in CI too.

## RTU

There is no live RTU tier. An RTU binding needs a HAL service provider on a request/response contract,
which the DevHost does not stand in for, so `Vion.Examples.ModbusRtu.Test` **is** the RTU smoke:

```bash
dotnet test examples/Vion.Examples.ModbusRtu/Vion.Examples.ModbusRtu.Test
```

Watch for the migration gotcha it encodes: RTU callbacks travel the dispatcher now, so every
`Simulate*` needs a `FlushPendingActions()` before the assertion — see
[`docs/migrations/0.10.4-modbus-client-surface.md`](../../../docs/migrations/0.10.4-modbus-client-surface.md).
