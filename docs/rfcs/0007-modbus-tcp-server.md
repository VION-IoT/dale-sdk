# RFC 0007: Modbus-TCP server support — logic blocks as Modbus slaves

Status: **Implemented** (v1) on branch `feat/modbus-tcp-server`. Author: jonas.bertsch. Date: 2026-06-11.
Revised the same day after a cross-repo review (logic-block-libraries as-built, RealtimeSystem
ST server blocks, FluentModbus 5.3.2 surface verification); the revision resolves the original
open questions, widens v1 to all four register areas, and aligns the surface 1:1 with the
client stack's conventions (no-arg factory, property configuration, `IsEnabled` gate,
`As<Type>` vocabulary, spec-standard byte/word-order defaults). Unit-id matching is dropped
entirely (spec endpoint behavior) and v1 is scalar-only (array overloads deferred).

## Implementation notes (post-implementation)

The surface shipped as designed (snapshot `Sync`, all four areas, property configuration,
diagnostics trio, Core-placed accessors, TestKit harness). Four findings are worth recording:

- **FluentModbus's single-zero-unit mode is filter-only — the "native one-liner" claim below was
  wrong.** `AddUnit(0)` makes the request *filter* accept every unit id, but request *processing*
  still resolves buffers by the raw incoming id and kills the connection for unregistered units
  (verified by the real-socket tests, then in source: `Find()` does not normalize). The proxy
  therefore aliases all 256 unit-id buffer-map entries to unit 0's arrays — shared arrays, one
  register map, no extra memory — via reflection over the pinned FluentModbus version, and
  **fails fast at construction** if an upgrade changes those internals (review feedback: a
  warn-and-degrade fallback to unit-0-only would look green in DevHost/TestKit paths while every
  fielded master breaks in the field). The `ServeAnyUnitIdentifier` integration test additionally
  pins the served behavior for any future FluentModbus bump. Relatedly, FC23
  (`ReadWriteMultipleRegisters`) carries independent read and write ranges — FluentModbus invokes
  the `RequestValidator` once per range (verified in source), so both are validated against the
  holding extent; the `ValidateBothRangesOfReadWriteMultipleRegisters` integration test pins it.
- **No server-side wrapper layer.** The client's wrapper exists to add conversion + validation
  between client and proxy; on the server side both live in the Core accessors, so
  `LogicBlockModbusTcpServer` sits directly on `IModbusTcpServerProxy`. The TestKit substitution
  seam (the proxy) is unchanged.
- **Block-side extent violations throw a dedicated `InvalidServerAddressException`** (Core), the
  analog of the client's parameter-validation exception family; wire-side violations answer
  IllegalDataAddress as designed, and the TestKit's client view throws Core's `ModbusException`
  with that code so tests see the wire behavior.
- **`scripts/generate-api-reference.cjs` didn't detect `readonly`/`ref` struct declarations** —
  fixed alongside (the new `ModbusServerAreaExtents` is a readonly record struct); the fix also
  surfaced the previously untracked `ServiceProviderMqttMessage` into the manifest.

Restartability (`Stop()` → `Start()` cycles with retained buffers) and the validator's
single-write quantity semantics were confirmed by the integration tests. The `examples/` server
block is deferred to the post-release reference bump, since examples reference *published*
package versions. The consumer migration (`VgtModbusTransport`) is a follow-up in
logic-block-libraries once a release ships these packages.

A post-implementation adversarial review (multi-lens, findings independently verified — one by
live repro) hardened five spots before merge:

- **Deadlock guard.** Setting `IsEnabled` or calling `Dispose` from *inside* a `Sync` callback
  now throws `InvalidOperationException`: stopping the listener joins FluentModbus request-handler
  tasks that may themselves be waiting for the server lock the callback holds — a permanent
  actor-thread deadlock, reproduced live during review. React to client-written commands after
  the callback returns (the canonical pattern anyway).
- **String accessors aligned with the client and de-trapped.** `ReadAsString`/`WriteAsString`
  lost their `byteOrder` parameter and gained the client's `TextEncoding.Ascii` default — string
  bytes go onto the wire in natural sequential order, exactly like the client's string methods
  (the dropped swap was a host-endianness transform that inverted semantics on little-endian
  hosts; non-standard layouts go through `ReadRaw`/`WriteRaw`, per the client's documented
  rationale). Wire-byte tests now pin the layout so a swap bug can't cancel out in a round-trip.
- **`LastClientWriteAt` hardened threefold:** stored as volatile UTC ticks (the
  `Nullable<DateTimeOffset>` auto-property could tear across the request-thread/actor-thread
  boundary); `AlwaysRaiseChangedEvent` enabled (FC5/FC6 writes that don't change the stored value
  raise no event otherwise — a master cyclically re-asserting an unchanged setpoint must still
  count as alive); and stamped via the DI-injected `TimeProvider` per the SDK's virtual-time
  convention (`AddDaleModbusTcpSdk` now `TryAdd`s `TimeProvider.System`; the TestKit fake exposes
  a settable `TimeProvider`).
- **`Stop()` zombie-handler sweep.** FluentModbus disposes request handlers without holding the
  server lock, so a handler accepted in the stop window could survive and keep serving its
  master; the proxy now re-stops while `ConnectionCount > 0` (bounded) and logs teardown races at
  Warning instead of Debug.
- **TestKit fidelity:** the fake rejects odd-length raw holding-register writes (un-wire-able on
  FC16, and Core's `WriteRaw` already rejects them), and the harness gained the `ServerFactory`
  member the usage example below relies on.

## Motivation

`Vion.Dale.Sdk.Modbus.Tcp` wraps the **client** role only (`ILogicBlockModbusTcpClient` + factory, request queue, the `Vion.Dale.Sdk.Modbus.Core` data converter): a logic block polls/commands external hardware. The first block that needed the **server** role has now shipped in the reference consumer ([logic-block-libraries](../../../logic-block-libraries)): `trading-source-vgt` serves a Modbus-TCP register map that an external trading center connects to as client (`Ecocoach.EnergyManagement/LogicBlocks/TradingSourceVgt/VgtModbusTransport.cs`).

Because the SDK has no server abstraction, that block had to:

1. **Reference FluentModbus directly from a packable consumer library** — the dependency the SDK otherwise encapsulates leaks into downstream package graphs.
2. **Hand-roll the wire-format encode/decode** (`BinaryPrimitives` big-endian per word, low-word-first 32-bit composition) — duplicating exactly what `Vion.Dale.Sdk.Modbus.Core`'s `IModbusDataConverter` + `WordOrder32` already solve on the client side.
3. **Re-discover the FluentModbus pitfalls as tribal knowledge** (now in consumer CLAUDE.md files rather than behind an SDK seam):
   - `ModbusServer.GetHoldingRegisters<short>()` returns a byte-swapped host-native view on x86 — wire-correct access must go through the byte buffers + explicit big-endian writes.
   - Buffer access from outside the FluentModbus request thread must hold `ModbusServer.Lock`.
   - `EnableRaisingEvents` defaults to `false` — `RegistersChanged` silently never fires.
   - `ModbusTcpServer.Stop()/Dispose()` can throw from a benign client-handler teardown race and must be guarded.
4. **Solve actor-safety alone.** FluentModbus serves requests on background threads; the consumer chose an event-free design (the block's actor tick snapshots the buffers under the lock) to avoid cross-thread reentry into block state. That pattern works, but every future server-fronted block has to know to do it.
5. **Test over real sockets.** With no server-side TestKit, the wire tests bind real loopback sockets with a free-port dance (`Ecocoach.EnergyManagement.IntegrationTest/TradingSourceVgtIntegrationShould.cs`) — correct, but slower and less parallel-friendly than the client side's in-memory `FakeModbusTcpHarness`.

## Demand — the RealtimeSystem porting pipeline

The original draft called a second server-fronted block "plausible". The cross-repo review found it is a pipeline: the RealtimeSystem ST codebase contains **four production Modbus-TCP server function blocks plus two client+server monitor hybrids**, all candidates to be ported to Dale logic blocks — and one of them is the block already ported as `trading-source-vgt`:

| ST block (`Ecocoach.RealtimeSystem.FunctionBlocks`) | Input regs | Discrete inputs | Coils (client-written) | Holding regs (client-written) | Notes |
|---|---|---|---|---|---|
| `TradingGatewayVgt` | 20 | 1 | 0 | 10 | already ported = the reference consumer |
| `BatterySystemModbusTcpProxy` | 25 | 5 | **7** | 7 | EMS writes commands (stop, alarm reset, …) via coils |
| `PowerPlantController` (EZA-Regler) | 42 | 4 | **2** | 5 | heartbeat echo, 15 s comm timeout |
| `SocControlledSlowConsumerModbus` | 4 | 2 | 0 | 0 | smallest case |
| `ElectricityMeterModbusTcpMonitor` | 37 | 1 | 0 | 0 | Modbus **client + server in one block** |
| `BatterySystemModbusTcpMonitor` | 21 | 5 | 0 | 0 | Modbus client + server in one block |

Three consequences for the design:

- **Client-written coils are load-bearing, not symmetric garnish.** Two candidates receive their commands as coils; an API without coil reads forces those ports to hand-roll again.
- **The heartbeat-echo idiom is universal.** Three of four blocks read a master-written heartbeat from holding registers and echo a feedback value into input registers each cycle — a read-modify-publish that wants to be atomic under one lock.
- **The cyclic-copy model is already the ST world's model.** None of the ST servers are event-driven (TF6250 serves reads from buffers the PLC task copies into cyclically), so the snapshot-on-tick design ports their structure 1:1.

## Goals

- A logic block can host a Modbus-TCP server with the same ergonomics the client role has: factory-injected, property-configured, `IsEnabled`-gated, actor-safe by construction.
- Cover **all four register areas** in v1 — including reading client-written coils and seeding/echoing holding registers (see pipeline table above).
- Reuse `Vion.Dale.Sdk.Modbus.Core` (`IModbusDataConverter`, `ByteOrder`, `WordOrder32`/`WordOrder64`) for all typed register access — consumers never touch `BinaryPrimitives` or FluentModbus types.
- Encapsulate the FluentModbus server pitfalls (typed-span byte-swap, `Lock` discipline, `EnableRaisingEvents`, teardown race) behind the SDK boundary.
- Minimal connection diagnostics in v1 (`IsListening`, `ConnectionCount`, `LastClientWriteAt`) — every surveyed server block hand-rolls communication surveillance.
- An in-memory server-side TestKit (`FakeModbusTcpServerHarness`) so wire-contract tests need no sockets.
- Keep the consumer's FluentModbus `PackageReference` removable.

## Non-goals

- **Polling cadence / sync strategy** — when and how often a block snapshots its registers is domain logic (the trading gateway deliberately syncs on a 1 s actor tick, the ST gateway cycle). The SDK provides safe snapshot primitives; it does not schedule them.
- **Register maps** — layout, scaling, sign conventions stay consumer-side (they are the device's wire contract). The consumer's "declared-twice constants, proven equal by an integration test" discipline is unaffected.
- **Heartbeat semantics** — comm-surveillance registers are domain wire contract; the SDK supplies the atomic snapshot they need, not the logic.
- **Shared-port aggregation.** RealtimeSystem multiplexes *all* blocks into one TF6250 endpoint via a register-allocation manager; Dale's model is one server per block on its own port. A fielded site whose master expects a single endpoint needs master reconfiguration or a dedicated aggregator block — out of scope for v1, and not structurally precluded (the snapshot API is thread-safe by construction, so a future shared-server arrangement stays possible).
- Modbus-RTU server support (no consumer; the API should not preclude it — see snapshot-interface placement below).
- Multi-server-per-block beyond what the factory already gives (`Create()` per server, exactly like the client side's multi-client story).

## Background — verified facts that constrain the design

**The client stack's shape is the template — including its configuration model.** `ILogicBlockModbusTcpClientFactory.Create()` takes **no arguments**; the client is configured via mutable properties (`IpAddress`, `Port`, `ConnectionTimeout`, …) and gated by `IsEnabled` (default `false`; operations are no-ops while disabled). The client's XmlDoc canonizes the **disable → reconfigure → re-enable** idiom for multi-property updates, and the reference consumer built `ModbusClientConfigurator` around exactly that idiom. Settings that cannot change after startup (queue capacity/policy) are documented as sealed once first enabled. Typed access uses the `Raw`/`AsShort`/`AsUShort`/`AsInt`/`AsUInt`/`AsFloat`/`AsLong`/`AsULong`/`AsDouble`/`AsString` vocabulary with `ByteOrder byteOrder = ByteOrder.MsbToLsb` and `WordOrder32 wordOrder = WordOrder32.MswToLsw` as defaulted trailing parameters. Layering is `ILogicBlockModbusTcpClient` → wrapper → proxy (the FluentModbus boundary), with the TestKit substituting the proxy in DI. The server side mirrors all of this so the substitution point, the configuration idiom, and the type vocabulary are identical.

**FluentModbus 5.3.2 server surface** (verified against the package's shipped XML docs): per-unit byte buffers (`GetHoldingRegisterBuffer/GetInputRegisterBuffer/GetCoilBuffer/GetDiscreteInputBuffer(byte unitId)`), `AddUnit`/`RemoveUnit`/`UnitIdentifiers`, a public `Lock` object guarding buffer access against the background request handlers, `RegistersChanged`/`CoilsChanged` events (off by default), **`RequestValidator`** (per-request validation hook), **`ConnectionCount`/`MaxConnections`** on `ModbusTcpServer`, and an asynchronous request-serving mode (default) where client reads/writes are served from the buffers without consumer involvement. Buffers are allocated full-range (the `Max*Address` properties are get-only) — declared area sizes therefore drive *validation*, not allocation, and ported maps that live at offset `0x8000` (the TF6250 convention used by every RealtimeSystem block) keep their addresses unchanged. FluentModbus also ships a `ModbusRtuServer`, which is why the snapshot interfaces must not be TCP-coupled. Source-verified at tag v5.3.2: registering exactly unit 0 and nothing else enables `IsSingleZeroUnitMode` (`UnitIdentifiers.Count == 1 && UnitIdentifiers[0] == 0`) — the request handler then accepts every incoming unit id ("If we have only one UnitIdentifier, and it is zero, then we accept all incoming messages") and echoes the request's unit id in the response frame; outside that mode, unmatched unit ids are silently dropped (no error response). The id-agnostic endpoint is therefore a native one-liner (`AddUnit(0)`), not a workaround.

**The consumer's working pattern** (`VgtModbusTransport`, ~200 lines): start server + `AddUnit` at config time; on the block's actor tick, under `lock (server.Lock)`, decode the holding registers into a domain record and encode domain telemetry into the input registers + discrete inputs; dispose guarded. The block never sees FluentModbus. This is the pattern the SDK should productize — the abstraction cut (snapshot-in/snapshot-out under the lock, no events into the actor) is the part that makes it actor-safe without marshaling machinery.

**Word order is genuinely bimodal in the field.** The wire-verified VGT port composes 32-bit values low-word-first (`WordOrder32.LswToMsw`, the Beckhoff/TwinCAT block-copy layout); the SmartLogger convention is `MswToLsw`. The ST sources never state word order explicitly — it falls out of `MEMCPY` into `WORD` arrays on a little-endian CPU — so each port must wire-verify its convention. The defaults match the client side (`MsbToLsb`/`MswToLsw`, the Modbus-spec-standard order); non-standard maps pass the parameter explicitly, exactly as client-side blocks do today.

## Design sketch

New surface in `Vion.Dale.Sdk.Modbus.Tcp` (+ TestKit twin); the snapshot interfaces live in `Vion.Dale.Sdk.Modbus.Core`. The shape deliberately mirrors `ILogicBlockModbusTcpClient`: no-arg factory, mutable configuration properties, `IsEnabled` gate, identical typed vocabulary and defaults.

```csharp
public interface ILogicBlockModbusTcpServerFactory
{
    /// Creates a new instance. Multiple servers per block (distinct ports) work
    /// exactly like multiple clients per block on the client side.
    ILogicBlockModbusTcpServer Create();
}

public interface ILogicBlockModbusTcpServer : IDisposable
{
    /// Default false. Setting true binds and starts listening (throws on bind
    /// failure — the consumer decides whether that is fatal; the reference block
    /// logs and degrades). Setting false stops listening. Mirrors the client's gate.
    bool IsEnabled { get; set; }

    // Configuration — changeable only while disabled (the client side's documented
    // "disable → reconfigure → re-enable" idiom; a live port change is a rebind):
    string? ListenAddress { get; set; }        // default "0.0.0.0"; validated like the client's IpAddress
    int Port { get; set; }                     // default 502, like the client
    ushort HoldingRegisterCount { get; set; }  // declared map extents (addresses 0..Count-1 are served):
    ushort InputRegisterCount { get; set; }    // drive request validation + accessor bounds checks, NOT
    ushort CoilCount { get; set; }             // buffer allocation (FluentModbus buffers are full-range).
    ushort DiscreteInputCount { get; set; }    // Default 0 = area not served. Offset-based maps (e.g. a
                                               // 10-register map at 0x8000) declare extent = offset + size.

    /// All buffer access for one cycle in a single lock acquisition, executed on the
    /// caller's (actor) thread. Read-modify-publish (e.g. heartbeat echo) is atomic.
    /// Works while disabled too (buffers exist independently of the listener), so a
    /// block can seed defaults before enabling.
    void Sync(Action<IModbusServerSnapshot> access);
    T Sync<T>(Func<IModbusServerSnapshot, T> access);

    // Minimal diagnostics (v1) — SDK-maintained, no events into the actor:
    bool IsListening { get; }
    int ConnectionCount { get; }              // native on FluentModbus.ModbusTcpServer
    DateTimeOffset? LastClientWriteAt { get; }
}

// Vion.Dale.Sdk.Modbus.Core — transport-agnostic; an RTU server (FluentModbus ships
// ModbusRtuServer) would reuse these unchanged.
public interface IModbusServerSnapshot
{
    IModbusRegisterAccessor HoldingRegisters { get; }  // client-written setpoints; block may seed/echo
    IModbusRegisterAccessor InputRegisters { get; }    // block-published telemetry
    IModbusBitAccessor Coils { get; }                  // client-written commands
    IModbusBitAccessor DiscreteInputs { get; }         // block-published flags
}

/// Typed vocabulary, parameter order, and defaults are the client's, verbatim
/// (Raw/AsShort/AsUShort/AsInt/AsUInt/AsFloat/AsLong/AsULong/AsDouble/AsString):
public interface IModbusRegisterAccessor
{
    ushort ReadAsUShort(ushort startingAddress, ByteOrder byteOrder = ByteOrder.MsbToLsb);
    void WriteAsUShort(ushort startingAddress, ushort value, ByteOrder byteOrder = ByteOrder.MsbToLsb);
    int ReadAsInt(ushort startingAddress,
                  ByteOrder byteOrder = ByteOrder.MsbToLsb,
                  WordOrder32 wordOrder = WordOrder32.MswToLsw);
    void WriteAsInt(ushort startingAddress, int value,
                    ByteOrder byteOrder = ByteOrder.MsbToLsb,
                    WordOrder32 wordOrder = WordOrder32.MswToLsw);
    // … AsShort/AsUInt/AsFloat (WordOrder32), AsLong/AsULong/AsDouble (WordOrder64),
    //   AsString (TextEncoding), Raw — converter-backed, one method pair per client type
}

public interface IModbusBitAccessor
{
    bool Read(ushort address);
    void Write(ushort address, bool value);
}
```

Key decisions:

- **Property configuration + `IsEnabled`, not an options record.** An earlier revision sketched `Create(ModbusTcpServerOptions)`; that contradicts the client stack, where `Create()` is parameterless and configuration is mutable properties behind the `IsEnabled` gate — the idiom the consumer's `ModbusClientConfigurator` already encodes and unit-tests. Following it means a server-side configurator is the same three lines, and config-restore paths (`Starting()` + runtime re-edit) work identically for both roles. Server config properties are changeable only while disabled (documented; a live rebind is the non-goal), which is the same "settings sealed while active" rule the client applies to its queue settings.
- **One snapshot accessor, not events — and not per-area lambdas.** The first draft sketched per-area methods (`ReadHoldingRegisters<T>(…)`, `PublishInputRegisters(…)`); the survey replaced them with a single `Sync` because ported blocks touch up to four areas per cycle (4–5 lock acquisitions per tick) and three of four candidates do an atomic read-modify-publish (heartbeat echo). All buffer access happens inside the `Sync` callback executed under the FluentModbus lock on the *caller's* thread — i.e. the block's actor, on whatever cadence the block chooses (`[Timer]`, `InvokeSynchronizedAfter`, or reactive). No background-thread callbacks into block code, so no marshaling problem exists. This is also why `Sync` needs no `IActorDispatcher`/callback parameters like the client's operations: the client queues background I/O and marshals results back; the server's "operation" is a synchronous in-memory buffer access — there is nothing to wait for. Blocks with split cadences (`PowerPlantController` reads commands at 100 ms and publishes at 2 s) call `Sync` from independent `[Timer]`s; each call is atomic on its own.
- **Converter-backed typed access with the client's vocabulary and defaults.** `IModbusRegisterAccessor` delegates to `IModbusDataConverter`; method names (`As<Type>`), parameter order, and the `ByteOrder.MsbToLsb`/`WordOrder32.MswToLsw` defaults are taken from `ILogicBlockModbusTcpClient` verbatim, so a developer who has written a client block already knows the server surface. The byte-swap trap is structurally unreachable.
- **All four areas in v1.** Coil reads are required by the `BatterySystemModbusTcpProxy` and `PowerPlantController` ports (client-written command coils); holding-register writes cover seeding defaults and feedback echo. The protocol already enforces client-side read-only on input registers/discrete inputs, so the block-side accessors stay symmetric without weakening wire behavior.
- **Unit id: ignored, always — no strict/relaxed mode.** The Modbus TCP spec treats the unit id as gateway-routing metadata and expects a directly-connected endpoint server to ignore it; TF6250 does exactly that (one port = one register space), so every fielded master in the pipeline assumes id-agnostic behavior — FluentModbus's own client docs steer directly-connected masters to `0x00`/`0xFF`. In a one-map-per-port topology strict matching protects nothing (there is no second device to discriminate), so the server accepts any unit id and echoes it in the response. This removes both a `UnitId` property and a mode enum from the surface. An optional strict filter remains addable later, non-breakingly, if a certified interface ever demands it — and a future multi-unit server is strict by nature, so nothing is precluded. The mechanics are source-verified: the wrapper registers unit 0 and nothing else, which puts FluentModbus in its native `IsSingleZeroUnitMode` — every incoming unit id accepted, echoed in the response (see Background).
- **Request validation in v1, auto-derived.** The declared area counts feed a `RequestValidator` that answers out-of-map client access with a proper `IllegalDataAddress` exception code instead of silently serving zeros — correct field-device behavior, nearly free since 5.3.2 exposes the hook.
- **Diagnostics in v1, minimal.** `ConnectionCount` is native; `LastClientWriteAt` falls out of an **SDK-internal** subscription to `RegistersChanged`/`CoilsChanged` that just stores a timestamp — the events never cross into actor code, so the no-events-into-the-actor principle holds. The trading block hand-rolled `LastCenterWriteAt`; every surveyed server block wants the same. A full `[ServiceInterface] IModbusServerDiagnostics` mixin (analog of the client's write diagnostics) stays v1.1.
- **Lifecycle.** `IsEnabled = true` binds and starts; `Dispose` encapsulates the guarded `Stop()`/`Dispose()` teardown-race handling and follows the client's documented dispose semantics (errors always logged). Registration extends the existing `AddDaleModbusTcpSdk()` (same package, one registration method), so the private runtime lights up server support with a plain package bump — no coordinated runtime change.

### Usage — the reference consumer over the proposed API

Construction and configuration mirror a client block 1:1 (compare `InverterHuaweiSun2000` + `ModbusClientConfigurator`):

```csharp
public TradingSourceVgt(ILogicBlockModbusTcpServerFactory serverFactory /*, …*/)
{
    _server = serverFactory.Create();
}

// Connection-SP setter + Starting() both delegate here — the server-side analog of
// ModbusClientConfigurator.Apply (disable → reconfigure → re-enable):
private void ApplyConnection(int port)
{
    _server.IsEnabled = false;
    _server.Port = port;
    _server.HoldingRegisterCount = VgtRegisters.HoldingRegisterCount; // 10 — center-written
    _server.InputRegisterCount = VgtRegisters.InputRegisterCount;    // 20 — telemetry
    _server.DiscreteInputCount = 1;                                  // ready bit
    _server.IsEnabled = true;   // binds + starts listening; throws on bind failure
}
```

The 1 s `[Timer]` tick replaces `SyncFromCenter()` + `PublishTelemetry()` with one `Sync` — one lock acquisition, heartbeat echo atomic, domain logic outside the lock:

```csharp
private void OnTick()
{
    var request = _server.Sync(snapshot =>
    {
        var holding = snapshot.HoldingRegisters;                       // center-written
        var reservationMode = holding.ReadAsUShort(VgtRegisters.ReservationModeAddress);
        var heartbeat       = holding.ReadAsUShort(VgtRegisters.HeartbeatAddress);
        var requestedKw     = -holding.ReadAsInt(VgtRegisters.RequestedTradingPowerAddress,
                                  wordOrder: WordOrder32.LswToMsw) / VgtRegisters.Scale;

        var input = snapshot.InputRegisters;                           // telemetry out
        input.WriteAsUInt(VgtRegisters.InstalledTradingPowerAddress,
            ToWire(InstalledTradingPowerKw), wordOrder: WordOrder32.LswToMsw);
        // … 13 more typed writes — no BinaryPrimitives, no FluentModbus …
        input.WriteAsUShort(VgtRegisters.FeedbackHeartbeatAddress, heartbeat); // echo

        snapshot.DiscreteInputs.Write(VgtRegisters.TradingSystemReadyAddress, IsReady);

        return new TradingCenterRequest(reservationMode, requestedKw, heartbeat);
    });

    ApplyCenterRequest(request);            // pure domain logic, outside the lock
    LastCenterWriteAt = _server.LastClientWriteAt;   // was hand-rolled heartbeat diffing
}
```

A ported command-coil block (`BatterySystemModbusTcpProxy` shape) uses the same two calls — nothing new to learn:

```csharp
var commands = _server.Sync(s => new EmsCommands(
    StopBattery:    s.Coils.Read(BatteryRegisters.StopBatteryAddress),
    ResetAlarm:     s.Coils.Read(BatteryRegisters.InverterAlarmResetAddress),
    AllocatedPowerKw: s.HoldingRegisters.ReadAsInt(BatteryRegisters.AllocatedPowerAddress)));
```

And the TestKit drives the wire side with the **client stack's own vocabulary** (the harness's test-side client view reuses the `ILogicBlockModbusTcpClient` method names, so a wire-contract test reads like a client block):

```csharp
using var harness = new FakeModbusTcpServerHarness();   // no sockets, no free-port dance
// … construct the block with harness.ServerFactory …

harness.Client.WriteSingleHoldingRegister(VgtRegisters.HeartbeatAddress, 42);   // act as the center
context.FireTimer(TradingSourceVgt.SyncTimer);

CollectionAssert.AreEqual(
    expectedWireBytes,                                   // byte-level: the declared-twice discipline
    harness.Client.ReadInputRegistersRaw(0, VgtRegisters.InputRegisterCount));
```

### Coverage check — every ST server pattern maps onto this surface

Walked against all six RealtimeSystem candidates:

| ST pattern (where it occurs) | Covered by |
|---|---|
| Client-written setpoints as holding registers, WORD + DINT, both word orders (all blocks) | `HoldingRegisters.ReadAsShort/AsUShort/AsInt(…, wordOrder:)` |
| Telemetry as input registers, scaled INT16/DINT (all blocks) | `InputRegisters.WriteAs*` (scaling stays domain-side, per non-goals) |
| Status flags as discrete inputs (all blocks) | `DiscreteInputs.Write` |
| Command coils written by the master — stop, alarm reset, control-loop enables (`BatterySystemModbusTcpProxy`, `PowerPlantController`) | `Coils.Read`; `Coils.Write` covers the consume-and-reset acknowledge pattern |
| Heartbeat echo: master heartbeat in holding → feedback into input, atomically (`TradingGatewayVgt`, `PowerPlantController`, `BatterySystemModbusTcpProxy`) | one `Sync` callback |
| Split cadences — commands read at 100 ms, telemetry published at 2 s (`PowerPlantController`) | `Sync` from independent `[Timer]`s; each call atomic |
| Configurable register start offsets (`SocControlledSlowConsumerModbus`; all `0x8000`-based maps) | counts are extents (offset + size); addresses port unchanged |
| Comm surveillance — heartbeat watchdog, alive flags (`PowerPlantController` 15 s timeout) | domain timer + `LastClientWriteAt`/`ConnectionCount` |
| Client+server hybrid — meter poller that also serves the EMS (`ElectricityMeter`/`BatterySystemModbusTcpMonitor`) | inject both `ILogicBlockModbusTcpClientFactory` and `ILogicBlockModbusTcpServerFactory`; the roles compose, they don't interact |
| Unit-id-agnostic masters (TF6250 behavior, all blocks) | unit id ignored by design |
| Data types in the wild: BOOL/INT/UINT/DINT/UDINT only | the `As<Type>` family covers these, plus float/64-bit/string for parity with the client |
| Many blocks sharing one endpoint (allocation manager) | **deliberately not covered** — non-goal; master reconfiguration or a dedicated aggregator block |

### Consistency with the client stack — summary

| Convention | Client (as-built) | Server (proposed) |
|---|---|---|
| Factory | `Create()`, no args | `Create()`, no args |
| Configuration | mutable properties; disable → reconfigure → re-enable | same; properties settable only while disabled |
| Gate | `IsEnabled`, default `false`, ops no-op while disabled | `IsEnabled`, default `false`, = listener on/off; `Sync` works on buffers regardless |
| Defaults | port 502; `MsbToLsb`; `MswToLsw` | identical |
| Typed vocabulary | `Raw`/`As<Type>` (`AsShort`…`AsString`) | identical, via `IModbusRegisterAccessor` |
| Layering / TestKit seam | client → wrapper → proxy; fake proxy via DI override | server → wrapper → proxy; same override |
| Errors | always logged, callback optional | always logged |
| Justified asymmetries | per-op `unitIdentifier` (many slaves); `IActorDispatcher` + callbacks (queued background I/O); request queue | one register map, unit id ignored (spec endpoint behavior) → no unit-id surface at all; `Sync` is synchronous in-memory buffer access on the caller's thread → no dispatcher, no callbacks, no queue |

### TestKit

`Vion.Dale.Sdk.Modbus.Tcp.TestKit` gains `FakeModbusTcpServerHarness`, mirroring `FakeModbusTcpHarness`'s wiring (ServiceCollection + proxy-registration override — the substitution seam is the server proxy, exactly as on the client side). It provides:

- the same snapshot surface the block sees, backed by an in-memory register store;
- a **test-side client view** named after `ILogicBlockModbusTcpClient`'s methods, with both typed and raw-byte access — byte-level fidelity is the point, because the consumer's "declared-twice constants, proven equal" discipline only works if tests can assert exact wire bytes;
- the same `RequestValidator`/unit-id path as the real proxy, so extent validation and the unit-id-agnostic endpoint behavior are testable in-memory.

Real-socket tests (FluentModbus client against the real server) stay possible and remain the right tool for byte-level sanity checks; the harness covers the fast lane.

### Migration of the reference consumer — and the ST ports after it

`VgtModbusTransport` shrinks to its domain content (the VGT register map, ×10 scaling, the VGT↔K0 sign flip, request decoding) over `ILogicBlockModbusTcpServer`; the `FluentModbus` PackageReference drops from `Ecocoach.EnergyManagement`. The block (`TradingSourceVgt`) does not change — its seam is already two calls (`SyncFromCenter`/`PublishTelemetry`), folding into one `Sync` invocation as shown above. The existing register-level integration tests keep passing unchanged against the real server path; new fast-lane tests can move to the harness.

For the ST porting pipeline: FluentModbus's full-range buffers mean the `0x8000`-based register maps port **address-for-address** — field masters reconfigure IP/port only, never the map. The one structural difference is endpoint topology (one server per block vs. one shared PLC endpoint); see non-goals.

## Resolved questions (2026-06-11 review)

1. **Unit-id strictness** — *resolved: no mode distinction; the server ignores unit ids, period.* The spec treats the unit id as gateway-routing metadata and expects directly-connected endpoint servers to ignore it; TF6250 does, so every fielded master in the pipeline assumes it. Strict matching protects nothing in a one-map-per-port topology and would break ported masters. An optional strict filter remains addable later, non-breakingly, if a certified interface ever demands it.
2. **Multi-unit maps** — *resolved: defer.* Zero multi-unit setups exist in either consumer repo (the SmartLogger-style gateway multiplexes registers, not units). A unit-aware surface (ids + per-unit maps) arrives with multi-unit support if a consumer ever appears; it layers onto the id-agnostic v1 without breaking it.
3. **Request validation hook** — *resolved: include in v1*, auto-derived from the declared area extents (`IllegalDataAddress` outside the map). A consumer-facing custom validator hook is deferred until a consumer needs one.
4. **Where the snapshot interfaces live** — *resolved: `Vion.Dale.Sdk.Modbus.Core` from the start.* FluentModbus ships `ModbusRtuServer`, `Vion.Dale.Sdk.Modbus.Rtu` already shares Core, and Core placement costs nothing now versus a breaking move later. It also lets a future RTU-server TestKit reuse the same fakes.
5. **Options record vs. property configuration** — *resolved: properties + `IsEnabled`*, matching the client stack (this revision; see key decisions). The earlier `ModbusTcpServerOptions` sketch is superseded.
6. **Typed-accessor naming** — *resolved: the client's `Raw`/`As<Type>` family verbatim*, including parameter order and the `MsbToLsb`/`MswToLsw` defaults.
7. **Array overloads** — *resolved: scalar accessors only in v1.* The surveyed maps decode field-by-field — every value has its own address, scaling, and sign convention — so bulk array access has no consumer. Overloads mirroring the client's `count` signatures can be added later without breaking anything.

## Remaining open questions

None. The last one — FluentModbus's mechanics for the id-agnostic endpoint — was resolved by source inspection at tag v5.3.2: `AddUnit(0)` alone enables `IsSingleZeroUnitMode`, which accepts every incoming unit id and echoes it in the response. The wrapper always registers unit 0.

## Implementation checklist (repo gates)

- New public types: `[PublicApi]` + full XML docs (DALE013/014/015 enforce), new namespaces registered via `PublicApiNamespace` in `PublicApiConfig.cs`; `publicapi-manifest.json` updates and the architecture notification fire automatically.
- XML docs must state the load-bearing contracts: *whose thread the `Sync` callback runs on* (caller's), *what lock is held*, *that `Dispose` swallows the teardown race*, *which properties require the disabled state* — matching the client surface's `<remarks>` style (threading/timeout/buffer-lifetime semantics, exception `cref`s, "Default is …" property docs).
- Register server types inside the existing `AddDaleModbusTcpSdk()`.
- Tests: MSTest + Moq `*Should` convention; `netstandard2.1` for `Modbus.Tcp`/`Modbus.Core` additions, `net10.0` for the TestKit; the existing consumer integration tests are the acceptance bar (must pass unchanged post-migration).
- Optional but recommended: an `examples/` server-role block (analog of `Vion.Examples.ModbusRtu`) so the docs pipeline picks up consumer-facing ergonomics.
- Post-merge: set Status to **Implemented** with implementation notes, per the RFC 0001/0003 precedent.
