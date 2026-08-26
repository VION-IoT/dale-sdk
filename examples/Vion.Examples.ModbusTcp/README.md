# Vion.Examples.ModbusTcp

An interactive Modbus TCP debug client, built as a LogicBlock. Point it at a device, read a range of
registers or coils, and reinterpret the same bytes under different field types and byte/word orders
until the numbers make sense — then write values back the same way.

It ships with a simulated server, so you can run the whole thing without any hardware.

## Getting Started

1. **Set the startup project:**
   - **Visual Studio:** Right-click `Vion.Examples.ModbusTcp.DevHost` in Solution Explorer → **"Set as Startup Project"**
   - **Rider:** Select `Vion.Examples.ModbusTcp.DevHost` from the run configuration dropdown (top-right toolbar)

2. **Run the DevHost:**
   - Press `F5` to run
   - The browser should open automatically at `http://localhost:5000`

The default topology starts two blocks: `SimServer` (binding `127.0.0.1:15020` — loopback only, so the
simulated device is never reachable from the rest of the network) and `DebugClient` (already pointed at
it). No configuration needed to see traffic. To reach the simulator from another machine, set
`SimServer`'s *Listen address* to `0.0.0.0` or to the interface you want.

## Two things worth knowing before you start

**Addresses are protocol (base-0) addresses** — the number that goes on the wire. Device documentation
written in the PLC/Modicon convention is one-based and encodes the area in the leading digit, so
holding register `40001` is address `0` here, and input register `30007` is address `6`. If everything
you read is off by one, this is why.

**Byte order and word order are separate settings.** Byte order is the order of the two bytes inside a
single register; Modbus standardises it as most-significant-first, and almost every device follows
that. Word order only matters for values spanning two or four registers, and it is the setting devices
actually disagree about. Both are chosen independently, and the dropdowns only appear for field types
where they apply.

## Try it against the simulator

The tour below uses the bundled `SimServer` and takes about a minute.

1. **Read a float.** Set *Read function* to `FC4 — Input registers`, *Start address* `6`, *Quantity*
   `2`, *Field type* `Float32`. Press **Read now**. The Interpreted column of the first row shows
   `3.1415927`.

2. **See a word-order mismatch.** Change *Start address* to `8` and read again. Input registers 8-9
   hold the very same π, stored with its two registers exchanged — so with the standard word order you
   get about `2.16e-29` instead. Now switch *Word order (32 bit)* to `LswToMsw` and read again: π is
   back. This is what a mismatch looks like in the field, and recognising it is most of the work.

3. **Write something and confirm it landed.** Set *Write function* to `FC16 — Write multiple
   registers`, *Write address* `100`, *Field type* `Float32`, *Value* `42.5`, then press **Write now**.
   Read holding registers at address `100`, quantity `2`, field type `Float32` — `42.5` comes back.

4. **Watch a value over time.** In the `Watch1` section set *Enabled*, *Function* `FC4 — Input
   registers`, *Address* `10`, *Field type* `Float32`. Its value is a sine wave with a 60-second period,
   charted as a measuring point.

## Watch the link policy

The tour above is about *what the device says*. This one is about *what happens when it stops saying
it* — the client's own reconnect and backoff policy, which since SDK 0.10.4 the block does not have to
write. What you look at is the headline pill in **Status** — the SDK's own verdict, not a tally this
block keeps — plus the **Diagnostics** group behind it: `Link` (the verdict on the device) and
`Connection` (the verdict on the socket), both published as whole structs straight from the client.
It takes about a minute.

The endpoint and the policy knobs are two editable structs — *Connection* (address, port, unit id, both
timeouts) and *Link policy* (max queued age, connect backoff, backoff max) — so each edit is one value.
Each field shows its own label, a duration box takes `3s` as readily as `PT3S`, and the nullable *Max
queued age* has an ∅ toggle for "off".

1. **Nothing to connect to.** Edit the port inside *Connection* to `15021`, which nothing is listening
   on. On the next poll the headline pill goes **Faulted** and `Link → State` with it. Two consecutive
   failed connects later `Connection → State` goes `BackingOff`, the pill follows to **Backing off**,
   and a `NextAttemptAt` and a `CurrentBackoff` appear — the backoff doubles from 1 s towards 30 s.
   Requests issued during a backoff do not wait out a connect timeout — they fail fast, and
   `Link → BackedOffCount` climbs. Notice `Link → TransportErrorCount` rather than `TimeoutCount`: on
   localhost a closed port is *refused* immediately.

2. **The fix applies at once.** Put the port back to `15020`. A **changed** address or port cancels the
   backoff, so the very next poll connects — you do not wait out the remaining backoff.

3. **Re-applying the same values does nothing.** Note `Connection → ConnectAttemptCount`, then save
   *Connection* again without changing a field. The count does not move and the socket is never
   dropped: the client's setters detect an unchanged value. That is what lets this block push all five
   fields to the client on every edit — one reconfigure chokepoint, no diffing — without an unrelated
   edit costing a reconnect.

4. **The peer goes away, and comes back.** Turn `SimServer`'s *Server enabled* off mid-poll. The link
   faults, the socket closes, and the client falls into backoff on its own. Turn it back on and watch
   the client recover **with no operator action on it at all** — no reconnect button, no restart.

5. **Too much cadence is not a fault.** Turn all three watch slots on and drop *Poll interval* and
   *Watch interval* to `100` ms. `Link → LastQueuedWait` and `MaxQueuedWait` grow — that is time
   requests spend waiting their turn locally — while `Link → State` stays `Online`. Local outcomes are
   counted but never fault the device, so a congested client stays distinguishable from a broken one.
   Now set *Max queued age* to `1` ms: requests that wait longer than that are dropped rather than
   sent, `Link → ExpiredCount` ticks, and the state is *still* `Online`.

   (The simulator answers in well under a millisecond, so this is the one step you cannot make bite
   hard on localhost. A real device on a real network will.)

6. **The slow variant.** Point *Connection → Server address* at a black-hole address such as
   `10.255.255.1` — one the network drops rather than refuses. Now each attempt takes the full
   *Connection timeout* (3 s) before it fails, which is what the same policy looks like against an
   unplugged device rather than a wrong port.

**Every `127.0.0.x` address is this machine.** To make a connection fail, use a closed port (`15021`) or
a black-hole address (`10.255.255.1`) — not another loopback IP. `127.0.0.2` does fail here, but only
because `SimServer` binds `127.0.0.1` specifically; against a server bound to `0.0.0.0` — the SDK's
default, and what most devices do — it would connect, and `Online` would be the truthful answer.

Throughout, read the error strings alongside the pill: *Last error*, *Last read error* and *Last write
error* all start with the SDK's outcome — `Timeout`, `TransportError`, `BackedOff`, `Expired`,
`DeviceError` — so a local backlog never reads as a device fault, and a device that answered with an
exception code never reads as an unreachable one.

While the link is `Faulted` the block polls at *Poll interval while faulted* (5 s by default) instead
of the normal interval — the recommended unattended pattern, and one line of block code: the client is
already reconnecting on its own schedule, so polling a dead device at full rate only fills the log.

Steps 1-5 are also committed as a replayable scenario. `pwsh scripts/smoke-modbus.ps1` from the repo
root runs it (and a healthy baseline) against a freshly booted host and prints the report; the same
files are in the DevHost's own Player under **modbus-healthy** and **modbus-link-policy**.

## Simulated register map

All addresses are protocol (base-0). The simulator advances once per second; `tick` below is the number
of seconds since it started.

### Input registers (read-only, FC4)

| Address | Type / order | Content |
|---------|--------------|---------|
| 0 | UInt16 | Seconds counter |
| 1 | Int16 | Fixed `-1234` — a value the unsigned column shows as `64302` |
| 2-3 | UInt32, MswToLsw | `tick × 10` |
| 4-5 | Int32, MswToLsw | Fixed `-123456` |
| 6-7 | Float32, MswToLsw | π |
| 8-9 | Float32, **LswToMsw** | π again, words exchanged |
| 10-11 | Float32, MswToLsw | Sine wave, ±100.0, 60 s period |
| 12-15 | UInt64, ABCD | Uptime in milliseconds |
| 16-19 | Float64, ABCD | e |
| 20-27 | ASCII | `DALE-SIM-SERVER!` |
| 40-49 | UInt16 | Live echo of holding registers 0-9 |

### Holding registers (read/write, FC3/FC6/FC16)

| Address | Type / order | Content |
|---------|--------------|---------|
| 0-9 | UInt16 | Scratch space — whatever you write appears at input registers 40-49 on the next tick |
| 100-101 | Float32, MswToLsw | Seeded with π; overwrite it and read it back |

### Coils and discrete inputs (FC1/FC2/FC5/FC15)

| Area | Address | Content |
|------|---------|---------|
| Coils | 0-7 | The tick counter in binary — coil *n* flips every 2ⁿ seconds |
| Coils | 8-15 | Static alternating pattern |
| Discrete inputs | 0-15 | A single set bit, walking one position per second |

## Debugging a real device

Turn `SimServer` off (or remove it from the topology), then set *Connection → Server address* and
*Port* to your device. Most devices use port 502.

The Solar-Log™ Modbus TCP direct-marketing interface is a good worked example, because it documents
exactly the trap this tool exists for: the byte order follows the Modbus standard (most significant
byte first), but the **word order for 32-bit values is little-endian** — the low word sits in the first
register. Reading it with the standard word order produces plausible-looking nonsense rather than an
obvious error.

Connect with unit id `1` on port `502`, then:

| Register | Field type | Word order | Meaning |
|----------|-----------|------------|---------|
| 10901 | `UInt16` | — | Grid operator's limit, in percent (FC4) |
| 10902 | `Float32` | `LswToMsw` | Grid operator's limit, in kW (FC4) |
| 10904 | `UInt32` | `LswToMsw` | Current generation, in W (FC4) |
| 10910 | `Int32` | `LswToMsw` | Grid feed-in (+) or draw (−), in W (FC4) |
| 10401 | `UInt16` | — | Power limit setpoint, in percent (FC6) |

Read `10902` under both word orders to see the difference for yourself: with `MswToLsw` the value is
wildly wrong, with `LswToMsw` it is the kW figure the device meant.

## What is in the project

- `LogicBlocks/ModbusTcpDebugClient.cs` — the client. Reads go over the wire as raw reads and are
  decoded locally, so the hex, unsigned, signed and interpreted columns of one result always describe
  the same bytes. It opens two connections: one carries the poll loop and the watch slots, the other
  carries the reads and writes you trigger by hand, so a slow poll cannot delay a manual command.
- `LogicBlocks/WatchSlot.cs` — one pinned register. How many slots an instance has is decided at
  configuration time by the `WatchSlotCount` instantiation parameter (RFC 0016); slots above the count
  do not exist rather than sitting empty.
- `LogicBlocks/ModbusTcpSimServer.cs` — the simulated device described above. It binds `127.0.0.1`
  rather than the SDK server's default `0.0.0.0`, so a wrong address stays wrong and the simulator does
  not answer the network; *Listen address* opens it up when you want that.
- `scenarios/` — two committed scenarios (RFC 0006): `modbus-healthy` and `modbus-link-policy`, the
  replayable form of the two tours. They run in the DevHost Player, from `pwsh scripts/smoke-modbus.ps1`,
  and in CI through `Vion.Examples.ModbusTcp.IntegrationTest`, which drives the same files headlessly.
  All three run on the **real** clock: the client's sockets and timeouts are real time, so a stepped
  host would never let a connect backoff elapse.

## Where the diagnostics come from

The Diagnostics group publishes `Link`, `Connection` and `Command link` — the SDK's own accumulated
summaries — rather than counters this block keeps. The client stamps a `ModbusReceipt` on every
transaction (when the answer was observed, how long it took on the wire, how long it waited first, how
it ended) and accumulates them; the block just assigns the snapshot. *Last read at* and *Last round
trip* are read straight off the poll's receipt, which is the only place they can be measured correctly
— by the time a callback runs, the block's own mailbox has had a turn.

The Status pill is folded from the same two summaries in the one place they are republished: backing
off outranks faulted (both mean the device is silent, but backing off also means the client has stopped
trying and requests are failing fast), and nothing local — a rejected quantity, an unparseable write
value — can move it, because neither says anything about the device. Those surface in the error strings
instead.

Three things to know about what the pill covers:

- **It is the poll connection's verdict.** *Read now* and every write travel the second, command
  connection; `Command link` in Diagnostics is that one's summary.
- **`Disabled` is the block's own word, not the SDK's.** A client switched off with *Connection enabled*
  issues nothing, so its `Link` keeps the last verdict — correctly, since no newer evidence exists. Only
  the block knows the silence is deliberate, so it overlays `Disabled` while leaving the SDK's snapshot
  untouched.
- **Polling off is not `Disabled`.** The connection is still up and *Read now* still uses it, so the
  last verdict remains the best answer — and *Last read at*, shown as a relative time, is the staleness
  signal that tells you how old that answer is.

### Choosing how to surface the diagnostics in your own block

This block publishes both summaries whole and folds a composite pill on top, because it is a debug tool
and every field earns its place. Yours probably is not, so there are cheaper shapes:

- **Publish `Link` and `Connection` as they are.** Since 0.10.5 both carry `[StructField]` titles and
  descriptions, so they arrive labelled with no work from you — no wrapper type, no mapping code.
- **Map the subset you care about into your own struct** with its own `[StructField]`s when eighteen
  fields is more than a tile should hold. Three or four — state, last contact, a failure count, a round
  trip — is usually a better dashboard citizen than the whole summary.
- **Publish `Link.State` directly as the status pill.** `ModbusLinkState` ships its own `[EnumLabel]`s
  and `[Severity]`s as of 0.10.5, so a `[Presentation(StatusIndicator = true)]` property assigned from
  it is coloured correctly without an enum of your own. Reach for a composite like this block's
  `LinkHealth` only when you actually need to fold in something the SDK cannot see — the socket's
  backoff, or the fact that your block switched the client off.

**Where an enum field's label comes from** (from the next SDK release — the version this example
pins predates it). For the three enum-typed fields inside the summaries —
`Link.state`, `Link.lastFailureOutcome` and `Connection.state` — `schema.title` holds the CLR type name,
because that is the cloud's translation key. Their authored title travels in a second slot instead:
`presentation.fields.<field>.displayName`, alongside the field enum's `[EnumLabel]` and `[Severity]`
maps. So a client reads *Link state* rather than `ModbusLinkState`, shows `Backing off` for the member
`BackingOff`, and colours the row — no projection property needed. Every other field, and every
description, lands inline in the schema as before.

## Where the configuration comes from

*Connection* (address, port, unit id, connection timeout, operation timeout) and *Link policy* (max
queued age, connect backoff, backoff max) are two editable `[StructField]`-annotated structs rather than
eight flat properties. It is the shape the SDK's first consumer arrived at independently — one property,
one setter, one reconfigure chokepoint — and it keeps the SDK's own types: `TimeSpan`, so the unit lives
in the type rather than in a `…Ms` suffix, and a nullable `MaxQueuedAge`, so "off" stays `null` instead
of becoming a `0` the block has to translate. Both setters push every field to both clients; that is
safe precisely because the SDK's setters detect change, so re-supplying an unchanged endpoint neither
reconnects nor cancels a backoff.

Since SDK 0.10.5 the DevHost honours the `[StructField]` annotations these carry: the title is the row
label, `ipv4` and duration fields get the right input, and a duration reads back scaled (`910 ms`, not
`PT0.91S`). Descriptions are deliberately not inline — they are one click away in the ▸ **docs &
schema** pane, because an eighteen-field struct is a scannable grid or it is nothing.

If you are moving a block onto SDK 0.10.4, the recipe and the behaviour changes are in
[`docs/migrations/0.10.4-modbus-client-surface.md`](../../docs/migrations/0.10.4-modbus-client-surface.md).

## Limitations

FC1-FC4 reads and FC5/FC6/FC15/FC16 writes are supported. Address scanning, device discovery and a raw
frame log are not part of this iteration.
