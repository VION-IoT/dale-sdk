---
slug: modbus-per-unit-maps
status: proposed           # proposed | in-flight | parked | archived
blocked-on: none           # for parked docs: what's blocking + ref
areas: MODB,TKIT
author: jonasbertsch
created: 2026-10-28
updated: 2026-10-28
supersedes: none           # path of a superseded change doc, or none
---

# One hosted Modbus TCP server, a register map per unit id (VION-377)

> Change doc — one in-flight change. The `Spec delta` below is distilled into the current-truth
> pages under `docs/specs/` in the implementation PR, then this doc is archived
> (`spec-change.ps1 archive <slug>`). Process: `docs/spec-process.md`. Never put change narrative
> inline in a spec page — it lives here.

## At a glance

### Summary

A block's hosted Modbus TCP server can today serve only one register map, answered under every unit
id (`AC-MODB-014.1`). A device that serves different values at the same addresses under several unit
ids — an Alfen Eve Double: socket 1 at unit 1, socket 2 at unit 2, station data at unit 200, one port,
one connection — cannot be simulated with it, so logic-block-libraries' Alfen simulator builds its own
FluentModbus server. This change lets a server opt into one map per declared unit id behind one port;
an undeclared unit id gets a Modbus exception and the connection stays open. A server that does not
opt in is unchanged. The TestKit's fake server and its master-side view gain the same unit dimension.

### Spec implications

`modbus.md` gains a section, *Serving a map per unit*, with a new umbrella `AC-MODB-021`: the
per-unit map, per-unit extents, the answer to an undeclared unit, the per-unit last write, the
configuration refusals, and the unit's `Sync`. `AC-MODB-014.1` is scoped to a server that declares no
unit (constraint 2), its rationale prose kept for that case. `AC-MODB-014.2` is untouched: the same
guard covers both paths (`D4`). The prose under `AC-MODB-020.1` gains the per-unit server, and the
premise it rests on is pinned for the configuration that server runs (`D9`). `testkit.md` gains
`AC-TKIT-012.4`–`.6` under *The fake Modbus TCP server*: a unit-addressed master view, the refusal of
an unaddressed one against a per-unit fake, and the per-unit last write. `docs/simulator-authoring.md`
§ 4 points a several-unit simulator at the hosted server and drops the narrow allowance to build one's
own (`D10`). The exact ids and texts in the delta are drafted against option A of `D5` and are
re-drafted on whichever option is ratified.

### Decisions

- `D1` **(a) ratified** — **Opt-in; the default is unchanged.** A server that declares no unit
  answers every unit id from one map, and `AC-MODB-014.1`'s rationale stands for it. Brief
  constraint 2.
- `D2` **(a) ratified** — **An undeclared unit id on a per-unit server gets a Modbus exception and
  the connection stays open.** Brief constraint 1.
- `D3` **(b)** — **The exception is `0x0B`, `GatewayTargetDeviceFailedToRespond`.** A per-unit
  endpoint is, to the master, a gateway in front of several devices, and an undeclared id is a device
  that is not there; `0x0B` is the code the Modbus application protocol gives for exactly that ("usually
  means that the device is not present on the network"). `0x0A` (`GatewayPathUnavailable`) says the
  gateway itself is misconfigured or overloaded, which would send an operator to the wrong end;
  `0x04` (`ServerDeviceFailure`) says a present device failed. It is also what the reporter's real
  device answered. The SDK's own client classifies any exception code as a device error, not a link
  fault (`ModbusOutcomeClassifier.cs:25`), so a production block polling an absent unit keeps its link
  healthy.
- `D4` **(b)** — **Mechanism: stay in FluentModbus's single-zero-unit mode, give each declared unit
  its own buffers in the per-unit buffer maps the default path already aliases, and refuse undeclared
  ids in the request validator.** Read from FluentModbus 5.3.2, decompiled (§ Full design, *What
  FluentModbus does*): the validator receives the unit id and runs before any buffer access, so a
  refusal there answers an exception and touches nothing. Registering each unit with `AddUnit`
  instead puts FluentModbus in multi-unit mode, whose request filter drops an unregistered id's request
  with no answer at all. The guard `AC-MODB-014.2` states is reused as is: the per-unit path writes the
  same four private maps the aliasing does, so one upgrade check covers both.
- `D5` **(c)** — **The API shape.** Three options in § Full design, *The API shape*. **Recommended: A,
  a unit handle** — `ILogicBlockModbusTcpServerUnit AddUnit(byte unitId)` on the existing server,
  the handle carrying the unit's four extents, its own `Sync` pair and its own `LastClientWriteAt`.
  It covers how a block declares units and their extents, reads and writes one unit's map, reads its
  last write, and how the fake's master view reaches a unit (`ForUnit`).
- `D6` **(b)** — **A unit serves all four areas, each with that unit's own extent.** FluentModbus
  keeps all four per unit anyway (`ModbusServer.AddUnit`), the server's own map serves all four
  (`AC-MODB-012.1`), and a holding-registers-only unit would be a narrower special case of the same
  shape. A unit's extents are its own, so unit 200 answers `IllegalDataAddress` at a socket's address
  the way the device does, rather than every unit sharing the widest extent.
- `D7` **(b)** — **A unit's last write is a timestamp, as the server's is; no write counter.** The
  consumer's question is not "written since my last tick" for its own sake: its tick turns a change in
  the count into `_lastWriteUtc = now` and then asks whether that instant is within the validity window
  (`ChargingStationAlfenEveSimulator.cs:412-424`). The timestamp *is* that instant, measured at the
  write rather than at the next tick. Two writes at one clock instant leave the same stamp, and the same
  answer to "within the window", so the case the brief raises changes nothing the consumer decides.
  A count would be a second, parallel notion of liveness for one call site (`sdk-surface-conventions.md`
  § 1).
- `D8` **(b)** — **Any unit id 0–255 may be declared.** The single-zero-unit mechanism serves unit 0
  like any other id, so the restriction the consumer carries (`PerUnitModbusTcpHost.cs:40-44`, "unit 0
  is a sole unit") is FluentModbus's multi-unit mode's, not this server's.
- `D9` **(b)** — **The stepped-host premise is pinned for the configuration the per-unit server
  runs.** The prose under `AC-MODB-020.1` (store before answer, so the server counts nothing of its
  own) holds for the per-unit server because it is the same FluentModbus mode and write path as the
  default one. `FluentModbusWriteOrderingShould` pins it today for unit 0 alone and for several units in
  multi-unit mode; it gains the case the per-unit server runs — single-zero-unit mode, writes at units 1,
  2 and 200 into per-unit buffers — driven through the hosted proxy itself rather than a bare server, so
  the shipped configuration is what is pinned.
- `D10` **(b)** — **The authoring guide points a several-unit simulator at the hosted server and drops
  the narrow allowance.** Its stated reason (`simulator-authoring.md:173-174`) becomes false, and no
  other reason to build one's own server is known; § 3 of `sdk-surface-conventions.md` (delete while it
  is cheap) applies to a guide's carve-out as much as to a type. The multi-unit case of
  `FluentModbusWriteOrderingShould` pinned the premise for that allowance only, and is retired with it.
- `D11` **(b)** — **No analyzer.** The new refusals are runtime state — a unit declared while the
  server is enabled, declared twice, a server-level `Sync` on a per-unit server — of the same kind as
  `AC-MODB-011.1`, which no analyzer can see either (`sdk-surface-conventions.md` § 4 covers rules an
  author can violate in source).

### Reviewer's questions

1. **(c) `D5` — which API shape?** A (unit handle, recommended), B (unit-addressed overloads on the
   server), or C (a separate per-unit server type from the factory). Under A, two sub-points ride
   along and are part of the ratification: (i) a server that declares units refuses its own `Sync` and
   refuses to enable with a non-zero server-level extent, rather than ignoring either; (ii) the fake's
   unaddressed master view (`harness.Client`) refuses on a per-unit fake, and `harness.Client.ForUnit(id)`
   addresses one unit on either kind of fake.
2. **(b) `D3` — `0x0B`.** Recorded with its reason; say so if the consumer's production block treats
   `0x0B` differently from another code.
3. **(b) `D7` — no write counter.** The consumer's migration swaps a count comparison for the stamp
   (Affects others). If a second consumer needs "how many writes", it is an additive member later.
4. **(b) `D10` — drop the allowance and retire the multi-unit premise case.** The Alfen simulator in
   logic-block-libraries runs the self-built configuration until it migrates; it stays green meanwhile
   (nothing it runs changes), but the premise it rests on is no longer pinned here once the case is
   gone. Keeping the case until the consumer migrates is the alternative.
5. **The brief's account of a plain `AddUnit` server is not quite what FluentModbus does.** Read, not
   run: an undeclared id is *dropped unanswered* in multi-unit mode (the connection stays open, the
   master times out); it is single-zero-unit mode *without* a buffer-map entry that closes the
   connection (`9f240521`). The verification keeps both failure targets — § Full design, *What each
   check fails against*.

---

## Full design

### What FluentModbus does

FluentModbus 5.3.2, the version the SDK ships, decompiled with `ilspycmd` (file names are the
decompiled sources; not run, except where an existing test already runs it):

- **The request filter.** `ModbusTcpRequestHandler.TryReceiveRequestAsync` accepts a request when
  `IsSingleZeroUnitMode || UnitIdentifiers.Contains(UnitIdentifier)`, and otherwise returns `false`;
  `ReceiveRequestAsync` then writes no response and reads the next request. So in multi-unit mode an
  unregistered id is never answered, and the connection stays up.
- **Single-zero-unit mode** is exactly one registered unit, which is 0 (`ModbusServer.IsSingleZeroUnitMode`).
  `AddUnit(0)` after another unit throws `ArgumentException`; `AddUnit(n)` for `n ≠ 0` while unit 0 is
  registered removes unit 0 first (`ModbusServer.AddUnit`). That verifies the consumer's "unit 0 is a
  sole unit" (`[assumed]` in the brief).
- **Buffer lookup** is by the raw request id in four private `Dictionary<byte, byte[]>` maps
  (`ModbusServer.Find`), throwing `KeyNotFoundException` for a missing entry. That throw happens inside
  the frame writer, escapes `WriteResponse`, and `ReceiveRequestAsync`'s catch cancels the handler —
  the connection close `9f240521` saw in single-zero-unit mode before the aliasing existed.
- **The validator.** `ModbusRequestHandler.CheckRegisterBounds` calls
  `RequestValidator(UnitIdentifier, functionCode, address, quantity)` first and answers any non-OK code
  as an exception response, before any `Process*` method touches a buffer. Every function code the SDK
  maps to an area goes through it (`ProcessReadHoldingRegisters` … `ProcessReadWriteMultipleRegisters`);
  a function code it maps to none is answered `IllegalFunction` without a buffer, as `AC-MODB-014.3`
  already leaves it.
- **Change notifications** carry the unit id (`RegistersChangedEventArgs.UnitIdentifier`,
  `CoilsChangedEventArgs.UnitIdentifier`), so a per-unit last write needs no new hook.
- **The response echoes the request's unit id** (`ModbusTcpRequestHandler.WriteFrame`) in every mode.

### The mechanism (`D4`)

`ModbusTcpServerProxy` keeps its constructor: unit 0 registered alone, ids 1–255 aliased to unit 0's
buffers (`ModbusTcpServerProxy.cs:47-58`, `:202-223`), and the refusal `AC-MODB-014.2` states when the
maps are not the expected shape. Declaring a unit:

1. replaces that id's entry in each of the four maps with fresh full-range arrays (unit 0, if declared,
   keeps its own, which is already distinct);
2. records the unit and its extents.

On `Start`, the validator becomes unit-aware: with no units declared it is today's
`ValidateRequest(functionCode, …, _extents)` unchanged; with units declared, an undeclared id returns
`GatewayTargetDeviceFailedToRespond` and a declared one is validated against its own extents. An
undeclared id still has an alias entry, but the validator refuses it before any read, so its alias is
never served; the alias also means a code path that bypassed the validator would read unit 0's map
rather than close the connection.

Memory: one declared unit costs FluentModbus's full-range buffers, about 272 KB (two 128 KB register
areas, two 8 KB bit areas), as an `AddUnit` does.

The last write: `OnClientWrite` and `OnClientWriteCoils` already run per notification; they also stamp
a per-unit `long[256]` of UTC ticks at `e.UnitIdentifier`, with the same `Volatile` discipline as the
server-level stamp (`ModbusTcpServerProxy.cs:37-40`). The server-level stamp keeps moving on every
write, so `AC-MODB-014.5` holds unchanged on a per-unit server: the most recent write to any area, now
at any unit.

`IModbusTcpServerProxy` (`[InternalApi]`) gains the unit dimension: `AddUnit(byte)`,
`Get…Buffer(byte unitId)` for the four areas, `GetLastClientWriteAt(byte unitId)`, and a `Start`
overload taking the per-unit extents. It is plumbing the TestKit's fake implements; no consumer names
it (`modbus.md` § The published surface).

### The API shape (`D5`, propose-and-wait)

What every option shares: units are declared while the server is disabled, as every other setting is
(`AC-MODB-011.1`); declaring the first unit is the opt-in; there is no way to undeclare one (a block
that wants a different layout creates another server). Each sketch is the whole new surface.

**A — a unit handle (recommended).**

```csharp
// ILogicBlockModbusTcpServer gains one member:
ILogicBlockModbusTcpServerUnit AddUnit(byte unitId);   // disabled only; once per id

[PublicApi]
public interface ILogicBlockModbusTcpServerUnit
{
    byte UnitId { get; }
    ushort HoldingRegisterCount { get; set; }   // disabled only, like the server's own
    ushort InputRegisterCount { get; set; }
    ushort CoilCount { get; set; }
    ushort DiscreteInputCount { get; set; }
    DateTimeOffset? LastClientWriteAt { get; }
    void Sync(Action<IModbusServerSnapshot> access);
    T Sync<T>(Func<IModbusServerSnapshot, T> access);
}
```

A block reads like the server it already knows, per unit:

```csharp
_socket1 = _server.AddUnit(1);  _socket1.HoldingRegisterCount = 1212;
_station = _server.AddUnit(200); _station.HoldingRegisterCount = 1212;
_server.IsEnabled = true;
_socket1.Sync(s => s.HoldingRegisters.WriteFloat(1206, current));
if (_socket1.LastClientWriteAt is { } at && now - at < validity) { … }
```

A unit's `Sync` takes the same server lock, so one unit's callback may nest another's when a block
wants several units changed atomically. On a server that declares units, the server's own `Sync` throws
`InvalidOperationException` (there is no one map to give it) and enabling with a non-zero server-level
extent throws the same (a setting that would be silently ignored). The server's `IsEnabled`,
`ListenAddress`, `Port`, `IsListening`, `ConnectionCount` and `LastClientWriteAt` (any unit) are
unchanged. One new `[PublicApi]` type; the manifest moves by one line.

TestKit under A: `FakeModbusTcpServerClient` gains `ForUnit(byte unitId)`, which returns a master view
whose requests carry that unit id. On a per-unit fake it is answered from that unit's map, or throws
`ModbusException` with `GatewayTargetDeviceFailedToRespond` for an undeclared unit, as on the wire; on a
default fake it is answered from the one map, as `AC-MODB-014.1` answers any id. The unaddressed
`harness.Client` is unchanged on a default fake and throws `InvalidOperationException` naming `ForUnit`
on a per-unit one, because a master always sends some id and a per-unit fake cannot guess which.
`FakeModbusTcpServerProxy` gains per-unit storage and `SimulateClient…(byte unitId, …)` overloads
beside today's, and stamps a per-unit last write from its `TimeProvider`. It does not offer setting a
unit's last write directly, as `AC-TKIT-012.3` offers for the server's: no consumer needs it, and a
test reaches any value by simulating a write on a fake clock.

**B — unit-addressed overloads on the server.**

```csharp
void AddUnit(byte unitId, ModbusServerAreaExtents extents);
void Sync(byte unitId, Action<IModbusServerSnapshot> access);
T Sync<T>(byte unitId, Func<IModbusServerSnapshot, T> access);
DateTimeOffset? GetLastClientWriteAt(byte unitId);
```

No new type (the manifest does not move; `ModbusServerAreaExtents` is already published), and closest
to the reporter's suggestion. Against it: extents become a positional four-field constructor where the
server itself uses named properties; a unit's extents cannot be read back; and every unit-level member
is a sibling of a server-level one with the same name and different meaning, so a block that forgets the
id compiles and fails at run time (the refusal of the server's own `Sync` is still needed). The fake's
master view would take `unitId` as a first parameter on each method instead of `ForUnit`.

**C — a separate per-unit server.** `ILogicBlockModbusTcpServerFactory.CreatePerUnit()` returning an
`ILogicBlockModbusTcpPerUnitServer` that has no server-level extents or `Sync` at all, units as in A.
No modal members and no refusals, at the price of a second server interface, a second harness, and a
member added to a published factory interface, which breaks a consumer's own implementation of it (a
test double — the TestKit has one, `FakeModbusTcpServerHarness.cs:78-91`). The modal refusals A needs
are two lines; C trades them for a parallel surface.

### What each check fails against

The real-socket test mirrors the consumer's layout: units 1, 2 and 200 behind one port, one client
connection, units 1 and 2 holding different values at one shared address, unit 200's extent not
covering that address.

| Check | Fails against |
| --- | --- |
| units 1 and 2 read different values at one address; a write at 2 reads back at 2 and not at 1 | today's server: one map under every id |
| unit 200 refuses a socket address with `IllegalDataAddress` | one extent shared by every unit |
| an unchanged re-write at 1 moves unit 1's stamp and not unit 2's | one server-level stamp |
| an undeclared unit gets `0x0B` | today's server (answers with data); a plain `AddUnit` server (no answer: the client times out) |
| the next request on the same connection, to unit 1, is answered | single-zero-unit mode without a map entry for the id (connection closed, `9f240521`) |
| `ServeAnyUnitIdentifier` and the rest of the `AC-MODB-014.x` suite, unchanged | a per-unit filter leaking into the default path |

A fixture with one unit, or with units at non-overlapping addresses, would pass the first row
vacuously; the shared address is the point.

### Alternatives considered

- **`AddUnit` per declared unit (multi-unit mode), as the consumer does.** No reflection beyond today's,
  but an undeclared id is never answered (`D4`), which constraint 1 rules out.
- **Registering every id 1–255 with `AddUnit`** so the filter passes all of them: about 69 MB of
  buffers for one server, and the undeclared ones would still need the validator refusal.
- **A write counter beside or instead of the stamp** — `D7`.
- **Holding registers only** — `D6`.

---

## Drift checkpoints

- _(none yet — append as implementation diverges from Full design)_

---

## Spec delta (to distill)

> Drafted against option A of `D5`; re-drafted on ratification.

- MODIFIED AC-MODB-014.1 -> docs/specs/modbus.md : WHERE a server declares no unit THE SYSTEM SHALL accept requests for any unit identifier, answer them from its one register map, and echo the request's identifier in the response.
- ADDED AC-MODB-021.1 -> docs/specs/modbus.md : WHERE a server declares units THE SYSTEM SHALL serve each declared unit identifier from a register map of its own, so that a value one unit holds or a client writes at one unit is not seen at another, and SHALL echo the request's identifier in the response.
- ADDED AC-MODB-021.2 -> docs/specs/modbus.md : WHERE a server declares units THE SYSTEM SHALL bound each unit's four areas by that unit's own declared extents, on the wire and in that unit's snapshot.
- ADDED AC-MODB-021.3 -> docs/specs/modbus.md : IF a server declares units and a client requests an area at a unit identifier the server did not declare THEN THE SYSTEM SHALL answer with a `GatewayTargetDeviceFailedToRespond` Modbus exception and keep serving the connection.
- ADDED AC-MODB-021.4 -> docs/specs/modbus.md : WHERE a server declares units THE SYSTEM SHALL timestamp the most recent client write at each unit, including a write that does not change the stored value, leaving every other unit's timestamp where it was, and SHALL move the server's own timestamp on a write at any unit.
- ADDED AC-MODB-021.5 -> docs/specs/modbus.md : WHEN a unit is declared, or a unit's extent is set, while the server is enabled THE SYSTEM SHALL throw an `InvalidOperationException`, and WHEN a unit identifier is declared a second time THE SYSTEM SHALL throw an `ArgumentException`.
- ADDED AC-MODB-021.6 -> docs/specs/modbus.md : WHEN a server that declares units is given a server-level `Sync` callback, or is enabled with a non-zero server-level extent, THE SYSTEM SHALL throw an `InvalidOperationException`.
- ADDED AC-MODB-021.7 -> docs/specs/modbus.md : THE SYSTEM SHALL execute a unit's `Sync` callback, in both forms, on the caller's thread under the server lock, while the server is disabled as while it is enabled, and SHALL hold it to the refusals `AC-MODB-013.3` to `AC-MODB-013.5` state for the server's own.
- ADDED AC-TKIT-012.4 -> docs/specs/testkit.md : THE SYSTEM SHALL offer a master-side view addressing one unit identifier, answered from that unit's map on a fake server that declares units, with the `GatewayTargetDeviceFailedToRespond` exception for a unit it did not declare, and from the one map on a fake server that declares none.
- ADDED AC-TKIT-012.5 -> docs/specs/testkit.md : WHEN a simulated client access addresses no unit and the fake server declares units THE SYSTEM SHALL throw an `InvalidOperationException` naming the unit-addressed view.
- ADDED AC-TKIT-012.6 -> docs/specs/testkit.md : THE SYSTEM SHALL stamp a fake server's last client write at each unit from its own clock, together with the server's own.

---

## Tasks

- `T-001` (`AC-MODB-021.1`, `AC-MODB-021.2`, `AC-MODB-021.3`, `AC-MODB-021.4`): the proxy's unit
  dimension — `AddUnit`, per-unit buffers in the four maps, the unit-aware validator, the per-unit
  stamp; flips this doc `in-flight`.
- `T-002` (`AC-MODB-021.5`, `AC-MODB-021.6`, `AC-MODB-021.7`, `AC-MODB-014.1`): the block-facing
  surface of the ratified option, XML docs on every new or changed member, and the server's remarks
  scoped to the default.
- `T-003` (`AC-MODB-021.1`–`.4`, `AC-MODB-014.1`): the real-socket test of the consumer's layout per
  § What each check fails against, each check's mutation run and observed.
- `T-004` (`AC-MODB-020.1` prose): the write-ordering premise for the per-unit configuration through
  the hosted proxy; the multi-unit case retired with the guide's allowance (`D9`, `D10`).
- `T-005` (`AC-TKIT-012.4`, `AC-TKIT-012.5`, `AC-TKIT-012.6`): the fake proxy's per-unit storage and
  overloads, `ForUnit`, and a block test driving a per-unit server through the harness at a shared
  address.
- `T-006`: distill into `modbus.md` and `testkit.md`, rewrite `simulator-authoring.md` § 4's allowance,
  archive.
