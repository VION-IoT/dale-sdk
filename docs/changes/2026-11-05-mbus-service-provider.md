---
slug: mbus-service-provider
status: proposed           # proposed | in-flight | parked | archived
blocked-on: none           # for parked docs: what's blocking + ref
areas: MBUS, TKIT, EMIT
author: Jonas Bertsch (drafted in a Claude session)
created: 2026-11-05
updated: 2026-11-05
supersedes: none           # path of a superseded change doc, or none
---

# M-Bus as an SDK-owned service-provider contract — raw telegrams on the wire, decoding in the SDK, meters read by meaning

> Change doc — one in-flight change. The `Spec delta` below is distilled into the current-truth
> pages under `docs/specs/` in the implementation PR, then this doc is archived
> (`spec-change.ps1 archive <slug>`). Process: `docs/spec-process.md`. Never put change narrative
> inline in a spec page — it lives here.

## At a glance

### Summary

A new package, `Vion.Dale.Sdk.MBus`, gives a logic block a wired M-Bus (EN 13757-2/-3) read through a
service provider that owns the master, shaped like `IModbusRtu`. The provider sends the telegrams
exactly as received (ecocoach's option A); the SDK decodes them and a block picks values **by meaning**,
not by record position. v1 has reads only, primary addressing only, and one read in flight per binding.
A provider may serve the first telegram only, and reading more later is a provider upgrade, not a
contract change. It is driven by ecocoach's legacy fleet (84 lines, 1 317 meters) and the AVM project's
five meters (VION-436/438). It answers [ecocoach's proposal](#where-this-comes-from) from the SDK's side.

### Spec implications

A new page `docs/specs/mbus.md` under a new area code `MBUS`. Its code is added to
`docs/specs/_corpus.yml`'s `owns:` and its row to `docs/spec-process.md` § The roster in the first
implementation commit, as § The roster requires. The page specifies the block-facing contract, the
handler, decoding and record selection, the link diagnostics, and, because the wire records live here in
v1 (`D4`), the wire. `docs/specs/testkit.md` gains a seventh kit (`AC-TKIT-016.*`), and every place
that says "six kits" moves to seven: `spec-process.md:49`, `testkit.md:579`, `analyzers.md:486`.
`AC-EMIT-002.8` is `MODIFIED` to name `MBusLinkSummary`. `_invariants.md`'s release roster
(`SYS-REL-001`, `scripts/set-version.ps1`) names both new packages. `contracts.md` is cited, not
changed: binding, the handler base, link maps and publishing are `AC-BIND-*` as they stand.

### Decisions

**(a) Ratified by the operator, 2026-11-05**, in the session that drafted this doc:

- `D1` — **Wire option A: the provider sends raw telegrams and the SDK decodes them.** A provider may
  return fewer telegrams than asked when it can't read further. The readout then reports itself
  incomplete, so a first-telegram-only provider on `FB_MBUS_RawData` is a valid v1 (§ First telegram
  only). The ST bench of 2026-11-10 settled full reads too. `Tc2_MBus` 3.5.4's `FB_MBUS_General_Ext`
  leaves each received telegram whole in `ST_MBUS_Communication.arrMBusLongFrame`, so an ST provider can
  return telegrams 2…n raw on an internal member, with the library version pinned (§ ST feasibility,
  results).
- `D2` — **Unused features stay out of v1.** No writes, no secondary addressing or scan, no rewind
  options, no per-request baud (§ Deferred).
- `D3` — **One read in flight per binding.** This keeps an oversubscribed 2400 Bd line from filling
  with stale requests.
- `D4` — **Single-repo for v1.** The wire records live in this package. Moving them, and any decoding
  helpers a C# provider wants, into `Vion.Contracts` is a follow-up that may need its own
  cross-repo spec. The move is wire-neutral: the `schema` user property is the record's name
  (`AC-MBUS-005.1`), so a moved record with the same name and shape is the same message.
- `D5` — **The ST provider is outside this process.** It is ecocoach's, in another GitHub org. This
  doc owes it a wire it can implement (§ The wire); it does not specify it.

**(b) Decided here, each with its precedent:**

- `D6` — **The wire mirrors RTU's.** Routing key `/hw/mbus`, action `/hw/mbus/get`, with dale
  subscribing `/hw/mbus/get/dale/response`. Records are `GetMBusPayload` and
  `GetMBusResponsePayload`, with `responseCode` and `errorMessage` as on `GetModbusResponsePayload`
  and frames in base64. Precedent: `Vion.Contracts/Hw/Modbus/*` (vion-contracts `7eb4d0b`),
  `ModbusRtuHandler.cs:69-72`. `/hw/mbus` and `/hw/modbus` are not substrings of each other, so
  dale's `topic.Contains` dispatch keeps them apart (decision 0014's routing-segment rule).
- `D7` — **Decoding happens block-side, in the contract's own implementation.** The handler carries
  bytes only, as RTU's does (`ActorMessages.cs:49`, `byte[]? Data`). A decode failure corrects the
  outcome before it reaches the link summary, the pattern `AC-MODB-015.8` states for RTU's
  conversion.
- `D8` — **The block surface copies `IModbusClient`'s settings.** These are `IsEnabled` (default
  false), `DefaultOperationTimeout`, `MaxQueuedAge` and `Link` (`IModbusClient.cs:60-87`). The
  callbacks are `(result, receipt)` and `(exception, receipt)`, per decision 0119.
- `D14` — **`Read` takes an `MBusAddress` and an `MBusReadOptions` from v1.** This deliberately departs
  from Modbus's trailing optional parameters (`IModbusClient.cs:605-613`). Adding an optional parameter
  changes a method's signature, and an already-built plugin calling the old one fails with
  `MissingMethodException` against a newer shared assembly. With these two types, secondary addressing,
  rewind and baud arrive as a new factory and new init-only properties, and the call sites stay
  binary-compatible (§ Forward view). `MBusAddress` has only `Primary(int)` in v1, refused outside
  0–250 as `unitIdentifier` is refused outside its range (`AC-MODB-004.1`). `MBusReadOptions` has only
  `MaxTelegrams`.
- `D9` — **One shared handler per runtime, with RTU's pending limit, expiry sweep and link-map
  routing.** The handler is a copy of `ModbusRtuHandler`, the named precedent, not a generic base
  lifted into `Vion.Dale.Sdk`. A base in core would land in the host-shared SDK
  (`PluginLoadContext.cs:417-430`) and widen this change into RTU's code. Extract it when a third
  request/response contract arrives.
- `D10` — **Values are `double`, scaled into the quantity's unit.** Block properties are `double`, and
  Modbus delivers `float`/`double`. A 12-digit BCD value is exact in a `double`. A binary counter past
  2^53 is not, and the record's raw bytes remain for it.
- `D11` — **Decoding helpers follow `IModbusDataConverter`.** They are an injectable
  `IMBusDataConverter` that the package registers, because a consumer injects the Modbus one
  (`modbus.md:677`). Static helpers would read more easily, but they would be a second pattern for the
  same job.
- `D12` — **A partial read goes to the error callback.** A read the provider broke off after some
  telegrams arrives as an `MBusException` that carries the partial readout. The legacy library treats
  it as an error too (`Tc2_MBus` error 120, "Not all telegram values were received"). A block that
  stops early on purpose (`maxTelegrams` reached, more to follow) gets a success with
  `IsComplete == false`.
- `D13` — **The contract-type token is `MBus`, chosen once.** It is the one identifier whose rename
  breaks pairing (`docs/identifier-stability.md`). The packages are `Vion.Dale.Sdk.MBus` and
  `Vion.Dale.Sdk.MBus.TestKit`.

### Reviewer's questions

1. **(c) Is a first-telegram v1 enough for AVM?** At AVM the shortcut loses the EM5XX's export and
   T1/T2 billing points, which sit in telegrams 3 and 5 (§ First telegram only). The options:
   - bill on import only until a provider reads further;
   - move the values into telegram 1 with the meter's data selection, at commissioning, with the
     vendor's tool;
   - wait for full raw reads.

   This is a STOP on the operator, with ecocoach. Separately, F1 (§ ST feasibility) must confirm that
   `FB_MBUS_RawData` yields a frame the provider can hand over whole. F2 and F3 then decide when full
   reads arrive. If both fail, the choice is ST driving the KL6781 itself or `B′`, records with string
   units (§ Alternatives).

   **OUTCOME (2026-11-10): no longer a STOP.** On the bench, F1 and F3 hold. An ST provider can serve
   the EM5XX's billing telegrams raw in v1, through `arrMBusLongFrame` with `Tc2_MBus` pinned. Whether
   ecocoach's first provider does that or starts first-telegram on the documented FB is the provider's
   choice (`D5`). The contract serves both unchanged.
2. **(c) Is this the decoding experience to build?** § The decoding experience proposes it: three
   layers, a selector a block declares once, values converted into the unit the block publishes, and
   `Describe()` for authoring. The operator asked for this to be defined, so it waits for the operator.
3. **(c) Default operation timeout of 120 s** (60 s before the bench). The timeout includes the
   provider's queue (§ Timeouts). Measured on the bench, a five-telegram EM530 read takes 3.75 s, so one
   pass of AVM's line HV1 (32 EM5XX) takes about 120 s. 60 s would time out the tail of that line on
   every pass. Alternatives: keep 60 s and have blocks on busy lines override it, or have the provider
   report its own queue wait.
4. **(c) Own diagnostics and exception types, or a shared set?** v1 proposes M-Bus-named receipt,
   outcome, link summary and exceptions, with no dependency on any Modbus package. The alternative is a
   transport-neutral set in core, which would also be a guideline for the next protocol (CAN, KNX). But
   moving Modbus's published types there is breaking, and that is its own change.
5. **(b) Deadline in the payload?** If the bench shows ST can't read a received message's expiry, the
   request gains a relative `expiresInMs` field. Otherwise the deadline rides only as MQTT message
   expiry, as RTU's does (`AC-MODB-015.11`).

   **OUTCOME (2026-11-10): MQTT expiry only, no payload field.** F4 holds: `Tc3_IotBase`'s
   `FB_IotMqtt5Client` hands ST `pubExpiryInterval` with the seconds remaining. A message published with
   60 s and delivered 15 s later read 45 (`mbus-st-bench` `30c2c5d`,
   `logs/2026-11-10-f4-expiry-probe.txt`).

---

## Full design

### Where this comes from

- **Input.** ecocoach's proposal and research: `logic-block-libraries` PR #195,
  `docs/notes/2026-11-04-mbus-service-provider-proposal.md` and its `-investigation.md` hub, at
  `202637d7`. Its block-facing sketch, its three diagnostic tiers and its line-configuration principle
  carry over.
- **What changes.** Option B (the provider decodes) is replaced by A. The surface is cut to the
  consumers that exist now. Scheduling an oversubscribed line becomes an explicit rule (`D3`).
- **The review.** The arguments are recorded in the session that drafted this doc. The points the
  operator corrected there are in the process journal.

### Three objectives, and how each choice serves them

| Objective | What serves it |
|---|---|
| **Block-author DX** | One call reads a meter (`Read`). Values are picked by meaning with a selector declared once and arrive in the unit the block publishes. `Describe()` turns a captured telegram into ready-to-paste selectors. The TestKit replays captured telegrams through the production decoder. |
| **SDK consistency** | Settings, callbacks, receipt, link summary, enablement, timeouts, refusal-as-`Invalid`, the shared handler and the wire all mirror RTU's (`D6`–`D9`). Helpers follow `IModbusDataConverter` (`D11`). Surface marks, the analyzer wiring, `[DaleSharedAssembly]`, an example and a kit come as with every protocol package. |
| **Provider DX** | The wire is flat: two fields in, an array of base64 strings out. There is no vocabulary to map and nothing to decode, and the provider returns frames as they came off the line. It may start with the first telegram only (§ First telegram only), and it answers anything newer than itself with `NotSupported` (§ Forward view). It is testable with any MQTT 5 client (§ The wire). Line configuration stays the provider's own. |

### The wire — what a provider implements

Topic, under the contract's base `{installationTopic}/{serviceProviderId}/{service}/{contract}`:

| Direction | Topic suffix | Payload |
|---|---|---|
| dale → provider | `/hw/mbus/get` | `GetMBusPayload`, with an MQTT 5 response topic and correlation data |
| provider → dale | the request's response topic (`/hw/mbus/get/dale/response`) | `GetMBusResponsePayload`, correlation data echoed |

The JSON follows the `Hw` rules (decision 0227): camelCase, enums by member name, bytes in base64,
`schema` user property set to the record's name, content type `application/json`.

```json
// request: primary address 5, a meter that sends up to three telegrams
{ "primaryAddress": 5, "maxTelegrams": 3 }

// answer: every RSP_UD long frame as received, from 68h to 16h inclusive, one base64 string each
{ "responseCode": "Ok", "errorMessage": null, "telegrams": [ "aE1NaAgFcl…", "aDs7aAgFcl…" ] }

// failure
{ "responseCode": "NoResponse", "errorMessage": "no answer after 2 repetitions", "telegrams": [] }
```

`responseCode` is one of:

| Code | Meaning |
|---|---|
| `Ok` | Answered |
| `NoResponse` | Silent after the provider's repetitions |
| `Garbled` | Checksum, framing, length or collision fault |
| `LineUnavailable` | Terminal or port not ready |
| `QueueFull` | The provider's queue is full |
| `InvalidRequest` | Address outside 0–250, or `maxTelegrams` below 1 |
| `NotSupported` | An action, or a request member, this provider doesn't implement (§ Forward view) |

A failure may still carry the telegrams read before it, which is how a partial read reaches `D12`. A
provider that can't read as many telegrams as asked answers `Ok` with the ones it read. That is not a
failure: the last telegram still announces more records, and the SDK reports the readout incomplete.

**What a provider must do** (carried on the page as provider obligations, not as SDK criteria):

1. **One request at a time per line, first in, first out.**
2. **Run the link layer.**
   - SND_NKE first, expecting E5h.
   - Then REQ_UD2, toggling the frame-count bit (FCB) per EN 13757-2, until a telegram arrives without
     the more-records-follow marker (DIF 1Fh) or `maxTelegrams` telegrams are in hand.
   - Repetitions follow its own setting; the standard suggests up to two.
3. **Return every frame verbatim.** It decodes nothing, and it does not report whether the read is
   complete: the SDK reads that from the last telegram.
4. **Drop a request whose deadline passed before its turn**, read from the MQTT message expiry. A
   dropped request gets no answer: dale has already timed it out.
5. **Answer every other request exactly once.** That includes a request on an action under
   `/hw/mbus/` it doesn't implement, or one carrying a member it doesn't know: answer those
   `NotSupported` instead of ignoring the member. This is what lets a later contract version meet a v1
   provider with a clear refusal rather than a timeout (§ Forward view).
6. **Keep line settings as its own service properties** (baud, repetitions, limits). The contract
   carries none, as RTU's carries no serial settings.
7. **Report line health through the platform's component health**, with one sub-component per line.
   This is guidance: the SDK neither sees nor checks it.

Size: a long frame is at most 261 bytes, so about 350 base64 characters; three telegrams are about 1 kB
of JSON. A provider is testable before any dale exists. Publish a request from any MQTT 5 client that
sets a response topic and correlation data (MQTTX does), and compare the frames against the meter's
datasheet or a libmbus read of the same meter.

### The block API

```csharp
namespace Vion.Dale.Sdk.MBus;

[PublicApi]
[ServiceProviderContractType("MBus")]
public interface IMBus
{
    bool IsEnabled { get; set; }                     // default false
    TimeSpan DefaultOperationTimeout { get; set; }   // default 120 s (question 3)
    TimeSpan? MaxQueuedAge { get; set; }             // default 30 s; null = no limit
    MBusLinkSummary Link { get; }

    void Read(MBusAddress address,                   // v1: MBusAddress.Primary(0–250)
              MBusReadOptions options,               // v1: MaxTelegrams (≥ 1, default 1)
              IActorDispatcher dispatcher,
              Action<MBusReadout, MBusReceipt> successCallback,
              Action<Exception, MBusReceipt>? errorCallback = null,
              TimeSpan? operationTimeout = null);
}

public readonly record struct MBusAddress { public static MBusAddress Primary(int address); }
public sealed record MBusReadOptions { public int MaxTelegrams { get; init; } = 1; public static MBusReadOptions Default { get; } }
```

A heat-meter block, read against the Modbus RTU example (`Em122ElectricityMeter.cs:200-227`):

```csharp
[ServiceProviderContractBinding(Identifier = "MBus", DefaultName = "M-Bus")]
public IMBus MBus { get; set; } = null!;

private static readonly MBusSelector Energy      = MBusSelector.Of(MBusQuantity.Energy);
private static readonly MBusSelector Volume      = MBusSelector.Of(MBusQuantity.Volume);
private static readonly MBusSelector FlowTemp    = MBusSelector.Of(MBusQuantity.FlowTemperature);
private static readonly MBusSelector DueDayValue = MBusSelector.Of(MBusQuantity.Energy, storage: 1);

[Timer(900)]
public void Poll()
{
    MBus.Read(MBusAddress.Primary(PrimaryAddress), new MBusReadOptions { MaxTelegrams = 2 }, this,
              (readout, receipt) =>
              {
                  HeatEnergy      = readout.Value(Energy, MBusUnit.KilowattHour);
                  Volume          = readout.Value(Volume, MBusUnit.CubicMetre);
                  FlowTemperature = readout.Value(FlowTemp, MBusUnit.DegreeCelsius);
                  DueDayEnergy    = readout.Value(DueDayValue, MBusUnit.KilowattHour);
                  MeterError      = readout.Header.Status.HasError;
                  Link            = MBus.Link;
              },
              OnError);
}
```

**Outcomes.** `MBusOutcome` takes Modbus's names where the meaning is the same: `Success`, `Timeout`,
`ProtocolError`, `Expired`, `Dropped`, `Invalid`. It adds M-Bus's own: `NoResponse`, `Garbled`,
`LineUnavailable`, `QueueFull` and `Skipped`.

- **Link state.** The state is `Online` on `Success`. It is `Faulted` on `NoResponse`, `Garbled`,
  `LineUnavailable`, `Timeout` and `ProtocolError`. Every outcome decided locally leaves it unchanged,
  and so does `QueueFull`, which says nothing about the meter. This is RTU's split (`AC-MODB-016.2`),
  except that a silent meter is `Faulted` here. On RTU a gateway's "no answer" (0x0B) maps to
  `DeviceError` and so to `Online` (`ModbusRtuHandler.cs:188`); that is a separate RTU item, not
  this change.
- **The summary.** `MBusLinkSummary` has `ModbusLinkSummary`'s shape: state, last contact, last
  failure, per-outcome counters, and the 15-minute windowed round-trip and queued-wait figures. It
  declares the 30 s `[DefaultMinInterval]` (`AC-EMIT-002.8`, `MODIFIED`).
- **Its accumulator** mirrors `ModbusLinkAccumulator`, the named precedent, and takes no dependency on
  the Modbus packages (question 4).

**One read in flight (`D3`).** A `Read` while the binding's previous read is still pending completes at
once through the error callback with a `Skipped` receipt and is not published. This bounds the
provider's queue to one entry per binding, and fairness between meters emerges as round-robin. It is
the legacy master's behaviour: each meter waits for `NOT bBusy` and retries only at its next interval
(`MBusManager.TcPOU:314`, from an independent read of `RealtimeSystem` `ff8ee4a99`). A rising
`Skipped` count is the line-load signal the ecocoach proposal wanted (its § 8) and costs nothing
extra. Refusing was chosen over coalescing because a coalesced read would hand two callers one receipt
and hide the congestion.

**Timeouts.** As on RTU, the operation timeout starts when the handler publishes (`AC-MODB-015.3`). It
therefore includes the provider's queue, which with `D3` holds at most one entry per meter on the line.
The bench measured the EM530 at 2400 Bd through `Tc2_MBus`: 1.27 s for SND_NKE plus one telegram, and
3.75 s for five telegrams. The library leaves about half a second between telegrams, so this is above
the ecocoach research's line-time model of 2.4–2.6 s. A pass of AVM's HV1 (32 EM5XX) therefore takes
about 120 s, which is what the proposed default covers (question 3). The legacy runs on the same
library, so this is the line's existing speed, not a regression: HV1's 60 s interval is already
stretched to about two minutes today. The
request's deadline is its publish instant plus its operation timeout (`AC-MODB-015.11`'s rule), and it
is what lets a provider drop a stale request instead of spending line time on it.

### The decoding experience

The generic EN 13757-3 decode is not device-specific, and every block gets it. What is
device-specific stays with the block: which records mean what for this meter, manufacturer-specific
data, and quirks. That is what the legacy meter blocks hard-coded by record position. The same model
ships with different record layouts by vendor or protocol (Multical 40x as Techem or GWF; GWF M140
compressed or extended, `HM/…Multical40x.TcPOU:324-393`, from the same read), and that is what
selection by meaning fixes. Three layers, each a step further from the standard:

**Layer 1 — the readout, by meaning.** The SDK decodes each telegram:

- **The fixed header** (CI 72h):
  - identification (8 BCD digits, as text, keeping leading zeros);
  - manufacturer (three letters);
  - version and medium;
  - access number;
  - the status byte, as `MBusStatus` with `HasError`, `PowerLow`, `PermanentError`, `TemporaryError`
    and the three manufacturer bits.
- **Its data records.** Each record exposes:
  - `Quantity`: `Energy`, `Volume`, `Power`, `VolumeFlow`, `FlowTemperature`, `ReturnTemperature`,
    `TemperatureDifference`, `OperatingTime`, `DateTime`, `FabricationNumber`, … and `Unknown`;
  - `Function`: instantaneous, maximum, minimum or during error;
  - `Storage`, `Tariff` and `Subunit`;
  - the value, already scaled by the VIF exponent into the quantity's unit.

  A record whose VIF the table doesn't know stays in the readout as `Unknown` with its bytes; it never
  fails the readout (`AC-MBUS-006.3`).

The readout joins the records of every telegram in order and adds:

- `Find(selector)`, `FindAll(selector)`, and `Value(selector, unit)` returning a nullable `double`;
- `Header` and `Telegrams`;
- `IsComplete`;
- `Describe()`.

`MBusSelector.Of(quantity, storage = 0, tariff = 0, subunit = 0, function = Instantaneous)` names a
record by meaning, and the defaults pick "the current value". A block declares its selectors once, as
`static readonly` fields, which reads like the meter's datasheet. `Value(selector, unit)` converts
within a dimension: Wh, kWh, MWh, J, MJ and GJ for energy; m³ and L for volume; W and kW for power;
m³/h and L/h for flow. A unit of another dimension is refused. This replaces the legacy library's
`usiUnit` scaling (`MBusManager.TcPOU:170-180`), which every legacy meter set to kW(h).

**Layer 2 — codes and bytes.**

- **`MBusSelector.ByCode("01 FD 17")`** matches a record by its exact DIF/DIFE/VIF/VIFE bytes, as a
  datasheet lists them. Use it for vendor records (error flags, manufacturer VIFs) and for meters
  whose layout varies.
- **Raw access.** Every record keeps `Dib`, `Vib` and `Data`. A telegram keeps its
  manufacturer-specific block (after DIF 0Fh/1Fh) as bytes.
- **`IMBusDataConverter` (`D11`)** decodes the standard data types a vendor block reuses: BCD
  (including the negative form), little-endian integers, real32, the date and time types F/G/I/J, and
  LVAR.

**Layer 3 — the telegram.** `MBusTelegram.Raw` is the whole frame, for anything else. The Celsa meter's
byte-swapped fabrication number (`EM/…CelsaECMxxMM.TcPOU:661`) is a layer-2 job; layer 3 is the
escape hatch, not the expected path.

**The authoring loop** — what makes "easy" true:

1. **Capture a telegram from the device.**
   - The provider's own log has it.
   - So does the block, via `readout.Telegrams[i].Raw` as hex.
   - Public vectors exist for a Supercal 531, an Elster TMP-A water module and a Gavazzi EM111
     (ecocoach research, `vendor-target-meters.md` §§ 2.8, 5.3, 6.8).
2. **Describe it in a test.** `MBusTelegram.Parse("68 4D 4D 68 08 05 72 …").Describe()` prints one line
   per record: quantity, value and unit, storage, tariff, function, the DIF/VIF bytes, and a
   ready-to-paste `MBusSelector`.
3. **Write the block with those selectors**, and test it by replaying the capture through the TestKit
   (§ TestKit).

**Decode failures.** A telegram the decoder can't parse (a CI it doesn't serve, a truncated record)
completes the read through the error callback with a `ProtocolError` receipt and the telegrams as
received. A meter that answers nonsense is a fault on its link, as on RTU.

### TestKit — `Vion.Dale.Sdk.MBus.TestKit`

- **The simulation helpers** follow the RTU kit's (`AC-TKIT-009.*`, `IModbusRtuExtensions.cs`):
  - `SimulateReadResponse(testContext, params string[] telegramsHex)`, with a `byte[][]` overload. It
    answers the most recent matching pending read through the production callback chain, including the
    decoder, stamped from the virtual clock.
  - `SimulateReadFailure(testContext, MBusResponseCode code, params string[] telegramsHex)`, for
    failures and partial reads.
- **No telegram builder in v1** (`D2`). Captures are the consumers' path, and a builder needs a
  consumer first.

### Package layout

The layout follows `Vion.Dale.Sdk.Modbus.Rtu`:

- **`Vion.Dale.Sdk.MBus`**, targeting `netstandard2.1`, contains:
  - the contract and its implementation;
  - the handler (`[InternalApi]`) and the actor messages;
  - the decoder, the readout types, the diagnostics and the exceptions;
  - the wire records with their own source-generated JSON context (`internal` in v1; `D4`);
  - a public `DependencyInjection : IConfigureServices`, as RTU's (`modbus.md:679-683`).
- **`[assembly: DaleSharedAssembly]`**, because the actor messages cross the plugin boundary
  (`AC-MODB-018.4`'s rule). One consequence is accepted: the first plugin a gateway loads fixes this
  assembly's version for all of them (`architecture/systems/dale.md`, plugin sandboxing). A decoder
  fix therefore reaches a gateway when the libraries on it are rebuilt against it.
- **The usual package obligations:** the analyzer `ProjectReference` and a declared published
  namespace, so `DALE014` asks for surface marks; `[PublicApi]`/`[InternalApi]` on every public type;
  the manifest snapshot; and the release roster (`docs/sdk-surface-conventions.md` §§ 4, 8).
- **`Vion.Dale.Sdk.MBus.TestKit`**: § TestKit.
- **`examples/Vion.Examples.MBus`**: one meter block and a test replaying a captured telegram, like
  `examples/Vion.Examples.ModbusRtu`. A DevHost stand-in is deferred.

### ST feasibility — the bench that decides when full reads arrive

**What it has to establish.** Can an ST provider on `Tc2_MBus` return telegrams 2…n raw? The library
returns raw bytes only from `FB_MBUS_RawData`, which documents "only the first telegram"; its
multi-telegram FB returns decoded records only (ecocoach research, `vendor-mbus-standard-kl6781.md`
§ 8.4). Four answers are needed:

- **(F1)** Does `FB_MBUS_RawData`'s `arrData` hold the whole frame (68h…16h) or only the user data,
  and does it refuse a CI other than 72h?
- **(F2)** Does calling it again with `bSND_NKE := FALSE` toggle the FCB and return the *next*
  telegram?
- **(F3)** If not: does `ST_MBUS_Communication.arrMBusLongFrame` (documented as "Sent or received
  bytes") hold each received frame whole between telegrams while `FB_MBUS_General_Ext` reads, long
  enough for a 10 ms task to copy it?
- **(F4)** Can the ST MQTT client read a received message's expiry? This decides question 5.

**Step 0 — at a desk, no M-Bus hardware.**

1. Ask Beckhoff support F1–F3. This is the proposal's own first preference and costs only a ticket.
2. Open `Tc2_MBus` in TwinCAT XAE. If the bodies of `FB_MBUS_RawData` and `FB_MBUS_General_Ext` are
   browsable, their FCB and SND_NKE handling answers F2 and F3 without a bench. If the library is
   protected, move on. XAE is a free install, and VION's Windows build VM already has XAE 4026 (TwinCAT
   spec, finding C21).
3. Answer F4 from the `Tc3_IotBase` documentation, or with any TwinCAT runtime and a broker. No M-Bus
   is involved.

**Step 1 — the bench, if step 0 leaves F2 or F3 open:**

| Item | Why | Where from |
|---|---|---|
| A TwinCAT 3 runtime with an EtherCAT port: a CX8200, or a PC on a trial licence | runs the test program | the AVM target is a CX8200; any CX or a laptop with a supported NIC works for a library question |
| An EtherCAT coupler (EK1100 or EK1200) + BK1250 + KL9010 | carries K-bus terminals on EtherCAT, which avoids the open K-bus question on the CX82xx image (VION-385) | **ecocoach's legacy test rig is exactly this**: `RealtimeSystem/Ecocoach.RealtimeSystem.FunctionBlocks/_Config/IO/Device 1 (EtherCAT)/Term 1 (EK1200)/Box 3 (BK1250).xti` holds a KL6781 |
| KL6781 + 24 V supply | the M-Bus master; it powers the bus itself, so no level converter | the same rig |
| One multi-telegram meter, mains-powered | the subject; a battery meter would spend its read budget | any meter the legacy reads through `FB_MBUS_General_Ext`. The Carlo Gavazzi EM5xx is an AVM target, a commodity DIN-rail meter and multi-telegram |
| XAE on a PC or VM that can reach the runtime | builds and downloads the program | the build VM, or any Windows machine |

**The program.** About fifty lines of ST in one `MAIN`, using the library alone, with no provider
around it:

- **E1** — `FB_MBUS_RawData` once with `bSND_NKE := TRUE`. Log `iLen` and `arrData`, and check them
  against F1.
- **E2** — `FB_MBUS_RawData` again with `bSND_NKE := FALSE`, up to six times. **F2 holds** if each call
  returns a different telegram (new content, access number moving) and the last one lacks DIF 1Fh.
- **E3** — `FB_MBUS_General_Ext` on the same meter, while a 10 ms task copies `arrMBusLongFrame`
  whenever its first bytes change. **F3 holds** if each copy is a whole frame whose values match the
  decoded records `General_Ext` returns.
- **E4** — time one read per telegram count. This calibrates question 3.

Half a day once the rig stands.

**What the results mean:**

| Result | What follows |
|---|---|
| F1: the whole frame, or user data the provider can frame itself (L, checksum, stop byte) | A first-telegram provider on a documented FB (§ First telegram only) |
| F2 holds | Full reads on a documented FB |
| F2 fails, F3 holds | Full reads on a documented-but-internal member. Pin the library version in the provider and re-run E3 on every library update |
| F2 and F3 fail | Full reads need ST to drive the KL6781 itself, and until then the provider stays first-telegram. `B′` only if that is too costly *and* the first-telegram limit can't be lived with (question 1) |

**Results, 2026-11-10.** The bench ran on real hardware:

- **Setup:** the test CX8200 `BTN-000tr9nu` (VION image, TwinCAT 4026), with a KL6781 directly on the
  CX8200's K-bus; a Carlo Gavazzi EM530 at primary address 1, 2400 Bd; `Tc2_MBus` 3.5.4, built with XAE
  3.1.4026.28.
- **Evidence:** `mbus-st-bench` `aadb307`, the clean run `logs/2026-11-10-all.txt` and its
  `.report.md`, with journals cleared before the run and no sequence gaps. That repository is local to
  the operator's machine and has no remote yet.

| | Result | What it shows |
|---|---|---|
| **F1** | **holds** | `arrData` holds the whole frame, 68h…16h. `iLen` is the frame length after a REQ_UD2 but one more (112 for 111) when the read began with SND_NKE. **A provider takes the length from the frame's L field (L + 6), never from `iLen`.** CI refusal stays open: the EM530 answers only CI 72h |
| **F2** | **fails** | `FB_MBUS_RawData` sends every REQ_UD2 with FCB = 1 (`10 7B 01 7C 16`), so the meter repeats telegram 1 with its access number moving (208…214). Confirmed again in the after-power run. This makes RawData a safe first-telegram reader: it never steps |
| **F3** | **holds** | `FB_MBUS_General_Ext` toggles the FCB (7Bh, 5Bh, 7Bh, …) and reads 5 telegrams. Each sits whole at offset 0 of `arrMBusLongFrame` from its arrival until the next one, 46–81 cycles of 10 ms. A separate 10 ms task copied all five, and the copies' values match the library's decoded records 47 of 47 |
| **F4** | **holds** | question 5's outcome |
| **E4** | measured | SND_NKE + 1 telegram 1.27 s; 1 telegram 0.79 s (111 B); 5 telegrams 3.75 s (519 B) |

**Not covered:** a CI other than 72h, and a maximum-length frame (L = 255 is 261 bytes;
`arrMBusLongFrame` is `ARRAY[1..260]`, an inference, so a frame that long may not fit).

**A note on the bench's own logs.** The standalone `logs/2026-11-10-e3.report.md` reads "F3 fails".
That log starts mid-journal (copy journal from sequence 574), and the copy lines are delta-encoded
("the changed range of the copy", `MBusBench/Bench/POUs/PRG_Copy.TcPOU:7`). The analyser therefore had
no base to rebuild frames from. The clean run is the evidence.

**What this means for an ST provider:**

- **Full reads** use `FB_MBUS_General_Ext`, with a task of ≤ 100 ms copying `arrMBusLongFrame` on change;
  a frame stays in place for at least 0.46 s.
  - `cMBus_MaxTelegrams` is raised above 5 for the Superstatic's 6 telegrams.
  - `Tc2_MBus` is pinned to 3.5.4, and E3 is re-run on every library update, because the member is
    internal.
- **First-telegram reads** use `FB_MBUS_RawData`, a documented FB.
- **Either way**, the frame length comes from L, not `iLen`.

A side finding for the TwinCAT HAL spec: a KL6781 ran on the CX8200's own K-bus on the VION image,
which bears on VION-385.

### First telegram only — a v1 shortcut for the provider

**The fleet says it covers most meters.** ecocoach's telegram map records what each legacy read takes
from which telegram (`logic-block-libraries` `97f84c3a`, investigation hub § 2.9):

- **1 099 of 1 317 meters** (1 040 of 1 252 enabled) read telegram 1 only. EMU Professional II asks for
  every telegram and uses only the first.
- **218 meters (17 %)** read later telegrams: the EM5XX, the Supercal 531, the Celsa ECMxxMM (98; every
  energy counter is in telegram 2) and the Schneider iEM3135 (18). Celsa and Schneider are not
  near-term ports.

**At AVM:**

| Meter | Lost with telegram 1 only | Who consumes it |
|---|---|---|
| Supercal 5, Superstatic 749/789, EF/MOF | nothing; the legacy reads telegram 1 only | — |
| Supercal 531 Radio | power, flow, flow and return temperature (t2) | nothing at AVM; monitoring only |
| EM5XX (46) | per-phase power (t2); per-phase voltage, export total, frequency (t3); import T1/T2 (t5) | **export, T1 and T2 are configured billing points.** The E-Mob energy manager's grid input uses currents and total power, which are in t1 |

The hub names three ways through the EM5XX billing gap (question 1):

- **Bill on import only** until a provider reads further. In the snapshot, T2 is 0.0 on all 46 meters
  and T1 tracks import on the 7 with live values. That is an inference from a snapshot, not a rule.
- **Move the values into telegram 1** with the meter's data selection, set at commissioning with the
  vendor's tool. `UNVERIFIED`: selection is by VIF, holds at most 16 values per frame, and is cleared
  by an application reset; whether it survives a power loss is unknown.
- **Full raw reads**, once § ST feasibility allows them.

**What it costs the contract: nothing.**

- The block asks for what the meter sends (`MaxTelegrams = 5` for an EM5XX). A first-telegram provider
  answers `Ok` with telegram 1. The readout is `IsComplete == false`, and `Value()` for a record in a
  later telegram returns `null`.
- The block therefore publishes "not valid", never a zero. Boot zeros are on ecocoach's must-not-port
  list (hub § 2.7).
- A block can tell the two kinds of incomplete read apart: fewer telegrams than it asked for means the
  provider stopped; as many as it asked for with more announced means the meter has more.
- When the provider learns to read further, the same block, unchanged, gets every value: no contract
  version, no block release.
- The provider declares its ceiling as a read-only service property (e.g. `MBusMaxTelegrams = 1`), so
  commissioning can see why values are missing. The SDK doesn't read it.

**What it saves.** The ST v1 is `FB_MBUS_RawData`, a documented FB, and it needs no library pin. It
also gives the smallest JSON of any option. Since the bench (F3 holds), the shortcut is a provider's
option rather than a necessity: full reads are possible on the pinned internal member.

### Forward view — the deferred features, and how each arrives without breaking v1

**The rules every addition follows**, so that v1 blocks and v1 providers keep working:

1. **The wire only grows.**
   - A new operation is a new action under `/hw/mbus/`; writes take `/set`, as RTU's
     `/hw/modbus/set` does.
   - A new capability of an existing operation is a new optional request member, whose absence means
     v1 behaviour.
   - Response codes and outcomes are appended, never renamed. They travel by name (decision 0227), and
     vion-contracts' own rule is that topics are append-only (hub § 2.7, vion-contracts
     `CLAUDE.md:108-133`).
2. **A v1 provider answers a newer request with a refusal, not silence.** Provider obligation 5 makes
   it answer `NotSupported` to an action or member it doesn't know. The SDK reports that as an
   `MBusException` with an `Invalid` outcome, which leaves the link state alone, because a deployment
   mismatch says nothing about the meter. A block learns at its first call that its provider is too
   old, instead of waiting out a timeout.
3. **The block surface only grows.**
   - New members on `IMBus`, which consumers never implement (only the SDK and its kit do).
   - New factories on `MBusAddress`, and new init-only properties on `MBusReadOptions` (`D14`).
   - No existing member's signature changes within a major version.
   - A library that uses a newer member needs the gateway's loaded `Vion.Dale.Sdk.MBus` to be at least
     that version. That is the first-loaded-wins rule for shared assemblies
     (`architecture/systems/dale.md`, plugin sandboxing), which `Vion.Dale.Sdk.Modbus.Rtu` lives with
     today.
4. **The contract-type token stays `MBus`.** Nothing below needs a new one; only a breaking change
   would, which is why rules 1–3 exist.
5. **Work on the line, rather than on one meter, stays with the provider.** A scan, a line-wide baud
   change or an address sweep occupies the line far longer than one read and acts on every meter. It
   is a provider operation, driven through the provider's own service properties, never a block call.

**Each deferred feature:**

| Feature | Block API | Wire | Provider | Comes back when |
|---|---|---|---|---|
| **Writes** (SND_UD) | `IMBus.Send(address, ciField, data, dispatcher, success, error, timeout)`, shaped like RTU's writes (a write may omit its success callback, `AC-MODB-001.5`). It shares the binding's single-flight slot. Typed helpers come later as extension methods building the data: data-set selection, set primary address, application reset | `/hw/mbus/set`: `SetMBusPayload { primaryAddress, ciField, data }` → `SetMBusResponsePayload { responseCode, errorMessage }`; `Ok` = E5h received | frames it (68h L L 68h C A CI data CS 16h). The legacy's caller-built checksum and stop byte are must-not-port (hub § 2.7) | Hager ECM381D or K'Electric KEPx is ported (`EM/…HagerECM381D.TcPOU:256-279`, `EM/…KElectricKEPx.TcPOU:232-253`), or the EM5XX's data selection is wanted from the block |
| **Secondary addressing** | `MBusAddress.Secondary(identification, manufacturer?, version?, medium?)`, no wildcards | optional `secondaryAddress { identification, manufacturer, version, medium }` in place of `primaryAddress` | selects (SND_UD, CI 52h, to address 253, E5h) and reads as one queue item, so nothing interleaves | a site with duplicate or unset primaries; eco-prod has three duplicate-address clusters live (hub § 2.6) |
| **Scan, commissioning** | none: not a block call (rule 5) | none in the contract | a provider operation, triggered through its own service properties, results as its own read-only properties. An empty primary scan takes ≈ 2.7 min at 2400 Bd (ecocoach proposal § 11) | commissioning tooling is planned |
| **Rewind, telegram selection** | `MBusReadOptions.Rewind`: `LinkReset` (the default, SND_NKE), `ApplicationReset(subcode?)` or `None` | optional `rewind`; absent = SND_NKE | sends it before REQ_UD2 | a meter needs it. The Supercal 531's selectable telegrams would also let a first-telegram provider serve chosen values |
| **Per-request baud** | `MBusReadOptions.Baud` | optional `baud` | switches the terminal per request, as `Tc2_MBus`'s `eBaudrate` does | a line mixes rates; all 84 surveyed lines run 2400 Bd |
| **Full multi-telegram on ST** | none | none | a provider upgrade | § ST feasibility, F2/F3 |
| **Wire records in `Vion.Contracts`** | none | none: same names and shape (`D4`) | a C# provider references them | a C# provider is built |
| **Wireless M-Bus** | a separate contract type, not `IMBus`: receive-only, push-shaped, with keys | its own | its own | a project needs it. The decoder's record layer is shared, so v1 keeps it free of the wired transport |
| **A telegram builder in the kit** | `MBusTelegramBuilder` | — | — | a test can't get a capture |
| **A simulation tier** | § Simulation | — | — | its own change |

### Simulation — a path for request/response contracts (not v1)

Modbus RTU has the same gap today, so this section is written for both contracts. Facts are from a read
of this repository at `ec62d764`.

**What happens now.** A block that binds `IModbusRtu` in the DevHost, stepped or real-time:

- **No handler runs.** The DevHost spawns handler actors only as stand-ins for `[ScenarioWire]`
  handlers (`Vion.Dale.DevHost/Mocking/ServiceProviderContractHandlerScan.cs:20-33`,
  `DevLogicSystemInitializer.cs:441-466`), and the DevHost has no MQTT at all.
- **Every request is a dead letter.** The send succeeds because a name always resolves
  (`AC-LIFE-017.1`). It is dead-lettered with one warning per request, and no callback ever fires, not
  even a timeout: the expiry sweep lives in the handler (`ModbusRtuHandler.cs:610-660`).
- **Pairing and the scenario steps are refused.** Pairing an RTU endpoint is refused (`AC-SCEN-014.6`),
  and so are `serviceProviderSet` and `serviceProviderExpect` on it (`AC-SCEN-007.2`, `007.4`).
- **An M-Bus binding would behave the same way.**

**Why the push-shaped mechanism can't carry it:**

- The pairing hop re-serialises to JSON (`ScenarioWireCodec.cs:101-149`).
- DALE046 rejects a delegate member, and arrays and reference types besides
  (`ScenarioWireTypeAnalyzer.cs:10-14, 119-142`), so the request's callback, its `byte[]` and its
  `Exception?` all fail.
- Pairing fans out to every block mapped to the endpoint, so it has no correlation back to the one
  block that asked.
- A stand-in never originates a message (`AC-SCEN-014.10`), but a timeout has to come from somewhere.

dale-sdk RFC 0020 § 6, "Phase 2", already sketched a request/response face (`IModbusRtuProvider`, with
expiry on the virtual clock). The area pass that absorbed the RFC dropped it, and no spec mentions it
(`git show 75e9650b^:docs/rfcs/0020-contract-pairing.md`, lines 357–386).

**The options:**

| | Mechanism | Fidelity | Stepped | Serves both contracts | Against |
|---|---|---|---|---|---|
| **a1** | A development-only provider face per contract (`IModbusRtuProvider`, `IMBusProvider`). A DevHost stand-in keeps the callback, a correlation map and the expiry, and routes the simulator's answer back | block-side decoding and callbacks | yes | one mechanism, one face each | re-implements the handler's rules, which can drift; the stand-in originates the timeout, against `AC-SCEN-014.10` |
| **a2** | The same faces, but the **real handler runs**. The DevHost adds a development-only MQTT loopback actor: it serves the handler's registration and publishes, hands each request's payload to the paired simulator block, and returns the simulator's response record as the handler's incoming message | real handler, wire records, correlation, outcomes and timeouts; only the broker is bypassed | yes; the handler's sweep runs on the virtual clock | yes, for any handler on the service-provider base | it runs production handler code in a host that isn't a runtime |
| **b** | A stand-in answering from fixtures or a new scenario step | decoding only | stepped only | partly | puts device behaviour in the host, against `scenarios.md:459-462`; a new step touches four definition sites (`devhost-conventions.md` § 7) |
| **c** | Decision 0139's transport tier: a fake service provider on real MQTT beside a real dale | highest | no | yes | not a DevHost tier; possible today with no SDK change |
| **d** | Bridge RTU requests to a hosted Modbus TCP simulator | decoding only | yes | Modbus only | doesn't model the HAL's error codes |

**The path forward: a2, with a1 as the fallback.**

- **Why a2.** It is one mechanism for every request/response contract and stays deterministic under
  stepping. The behaviour stays in a simulator block, and every hop is an actor message
  (`AC-SCEN-014.12`). The real handler originates the timeout, as it does in production.
- **What a simulator author writes.** A block binding `IMBusProvider` receives `GetMBusPayload` with
  its correlation id and answers with `GetMBusResponsePayload`, exactly what a real provider does. For
  M-Bus that means serving captured telegrams per address, so a bench exercises the production decoder
  on field bytes.
- **The constraints it keeps.** The face is development-only twice over: `DevelopmentOnly = true` on
  the contract type, and `[DevelopmentOnlyHandler]` with an empty routing key on its handler
  (`AC-BIND-015.1`–`.3`, the `DigitalOutputProviderHandler.cs:18-38` precedent). `dale pack` excludes
  a block that binds it, and the runtime refuses one. There is no production pairing (0139).
- **What has to be solved first:**
  - `InstallationTopic` is process-wide and can be set only once (`MqttConfiguration.cs:17-19`);
  - the real handler and a stand-in can collide on an actor name;
  - the registration acknowledgement has to be served.
- **What it decides.** It moves 0139's fidelity line, "pairing bypasses the MQTT hop", so it needs
  **an amendment to decision 0139 and a change doc of its own** before any code. RTU goes first: its
  handler exists, and the ABB port asked for it (ecocoach's RTU feedback, point 1).

**What v1 does to keep this open, at no cost:**

- The handler has no DevHost-specific branches.
- The wire records are the only thing a simulator needs to speak.
- Decoding runs block-side (`D7`), so any tier that delivers telegrams exercises it.
- The TestKit's `SimulateReadResponse` is the unit-test tier until then.

### Alternatives considered

- **B — the provider decodes into a vocabulary that the contract pins as enums.** This was ecocoach's
  preference. Rejected for:
  - an N-provider mapping of the layer that cost the legacy most;
  - a vocabulary change touching every provider;
  - nested JSON of up to 6 × 40 records hand-built in ST.
- **`B′` — records with string units, like Beckhoff's `ST_MBus_Data`** (operator's point,
  2026-11-05). It keeps `Vion.Contracts` stable because a new unit is a new string, not a new enum
  member. Its costs:
  - providers' unit strings differ (Beckhoff's against libmbus's), and the SDK has to parse each
    dialect;
  - `Tc2_MBus` drops DIF/VIF and subunit;
  - manufacturer data arrives as text, if at all;
  - the JSON stays large.

  It was the fallback in case the bench failed. Since F3 holds (§ ST feasibility, results), it is kept
  only as the record of the path not taken.
  The block API above survives it, because only the decoder's input changes.
- **Raw or records (ecocoach's § 6.4).** Two handler paths, with the vocabulary on the wire anyway.
  Rejected as a permanent shape.
- **One request per telegram, or a frame tunnel.** Rejected for the ecocoach proposal's reasons
  (§ 6.5): a non-portable rewind, provider state per meter, and the link layer pulled into the handler.
- **A provider-scheduled subscription** (the block declares an interval, the provider pushes
  readouts). It would fit a slow line, but it is a second contract shape for one protocol. `D3` gets
  the same fairness with RTU's shape.
- **A generic request/response base now.** See `D9`.

---

## Drift checkpoints

- 2026-11-10: the ST bench (`mbus-st-bench` `aadb307`) answered F1–F4: F1 holds, F2 fails, F3 holds,
  F4 holds. `D1` no longer depends on it, question 1 is closed, and question 5 is closed (no payload
  deadline). Question 3's figures and the proposed default (60 → 120 s, `AC-MBUS-004.1`) are re-based on
  the measured EM530 read. Sibling sweep: the Timeouts paragraph, the block API sketch and provider
  obligation 4 were updated in the same commit.

---

## Spec delta (to distill)

> Draft while `proposed`: the ids follow `D1`–`D13` and the open questions, and are re-cut after
> ratification. `MBUS` enters `owns:` in the first implementation commit.

- ADDED AC-MBUS-001.1 -> docs/specs/mbus.md : THE SYSTEM SHALL offer an M-Bus read on a service-provider contract type whose token is `MBus`, SHALL return from it immediately and SHALL deliver the result later through the `IActorDispatcher` the call was given.
- ADDED AC-MBUS-001.2 -> docs/specs/mbus.md : THE SYSTEM SHALL hand an `MBusReceipt` to both the success and the error callback of every read, carrying the wall-clock instant the response or failure was observed, the same instant on the monotonic scale, the round trip, the queued wait and the outcome.
- ADDED AC-MBUS-001.3 -> docs/specs/mbus.md : WHEN a primary address below 0 or above 250, or a telegram count below 1, is passed THE SYSTEM SHALL complete the read through its error callback with an `Invalid` receipt, without publishing it.
- ADDED AC-MBUS-001.4 -> docs/specs/mbus.md : THE SYSTEM SHALL render every number and duration in an M-Bus exception message and in a readout's description in the invariant culture.
- ADDED AC-MBUS-002.1 -> docs/specs/mbus.md : THE SYSTEM SHALL default an M-Bus contract to disabled.
- ADDED AC-MBUS-002.2 -> docs/specs/mbus.md : WHILE an M-Bus contract is disabled THE SYSTEM SHALL execute no read and SHALL invoke neither callback.
- ADDED AC-MBUS-003.1 -> docs/specs/mbus.md : WHEN a read is requested while the same contract's previous read has not completed THE SYSTEM SHALL complete the new read through its error callback with a `Skipped` receipt, without publishing it.
- ADDED AC-MBUS-004.1 -> docs/specs/mbus.md : THE SYSTEM SHALL default the operation timeout to 120 seconds and the maximum queued age to 30 seconds, and SHALL accept `null` as no queued-age limit.
- ADDED AC-MBUS-004.2 -> docs/specs/mbus.md : THE SYSTEM SHALL start the operation timeout when the handler publishes the request, and SHALL check the maximum queued age against the hop from the block to the handler.
- ADDED AC-MBUS-004.3 -> docs/specs/mbus.md : THE SYSTEM SHALL publish each read with a deadline of the moment the handler publishes it plus the read's operation timeout.
- ADDED AC-MBUS-005.1 -> docs/specs/mbus.md : THE SYSTEM SHALL publish a read as a `GetMBusPayload` JSON document carrying the primary address and the most telegrams wanted, labelled with the record's name as its schema and with the JSON content type.
- ADDED AC-MBUS-005.2 -> docs/specs/mbus.md : WHEN a response with the code `Ok` arrives THE SYSTEM SHALL decode every telegram it carries, in order, and SHALL deliver the readout to the success callback.
- ADDED AC-MBUS-005.3 -> docs/specs/mbus.md : WHEN a response with any other code arrives THE SYSTEM SHALL complete the read through its error callback with an `MBusException` naming the code and carrying any telegrams the response holds, and with the outcome that code names.
- ADDED AC-MBUS-005.4 -> docs/specs/mbus.md : WHEN a read arrives for a contract with no service-provider mapping THE SYSTEM SHALL complete it with an `Invalid` receipt.
- ADDED AC-MBUS-005.5 -> docs/specs/mbus.md : WHEN a response arrives whose correlation identifier matches no pending read THE SYSTEM SHALL ignore it.
- ADDED AC-MBUS-005.6 -> docs/specs/mbus.md : WHEN the shared pending-request limit is reached THE SYSTEM SHALL complete the arriving read with a `Dropped` receipt, without publishing it.
- ADDED AC-MBUS-006.1 -> docs/specs/mbus.md : THE SYSTEM SHALL decode a variable-data telegram into its fixed header and its data records, and SHALL keep each record's data information block, value information block and data bytes as received.
- ADDED AC-MBUS-006.2 -> docs/specs/mbus.md : THE SYSTEM SHALL scale a record's value by its value information's exponent into the unit its quantity is reported in.
- ADDED AC-MBUS-006.3 -> docs/specs/mbus.md : WHEN a record's value information is not in the decoder's table THE SYSTEM SHALL keep the record with an unknown quantity and its bytes, and SHALL decode the rest of the telegram.
- ADDED AC-MBUS-006.4 -> docs/specs/mbus.md : WHEN a telegram cannot be decoded THE SYSTEM SHALL complete the read through its error callback with a `ProtocolError` receipt, carrying the telegrams as received.
- ADDED AC-MBUS-006.5 -> docs/specs/mbus.md : THE SYSTEM SHALL report a readout complete only when its last telegram does not announce further records.
- ADDED AC-MBUS-006.6 -> docs/specs/mbus.md : THE SYSTEM SHALL select a record by quantity, storage number, tariff, subunit and function, defaulting to the instantaneous value of storage 0, tariff 0 and subunit 0, or by its exact information-block bytes, and SHALL return the first match in telegram order.
- ADDED AC-MBUS-006.7 -> docs/specs/mbus.md : THE SYSTEM SHALL convert a selected value into a unit the caller names when both measure the same dimension, and SHALL refuse a unit of another dimension.
- ADDED AC-MBUS-007.1 -> docs/specs/mbus.md : THE SYSTEM SHALL accumulate every completed read of one contract into a link summary readable at any time without blocking the read updating it.
- ADDED AC-MBUS-007.2 -> docs/specs/mbus.md : THE SYSTEM SHALL set the link state to `Online` on a success, to `Faulted` on no response, a garbled answer, an unavailable line, a timeout or a protocol error, and SHALL leave it unchanged on every locally decided outcome, on a full provider queue and on a request the provider does not support.
- ADDED AC-MBUS-008.1 -> docs/specs/mbus.md : THE SYSTEM SHALL mark the M-Bus assembly a shared assembly and SHALL route every M-Bus binding in a runtime through one shared handler.
- ADDED AC-MBUS-008.2 -> docs/specs/mbus.md : THE SYSTEM SHALL classify every public type the M-Bus packages ship as published surface or internal plumbing, and SHALL judge their declarations with the Dale analyzers.
- ADDED AC-TKIT-016.1 -> docs/specs/testkit.md : THE SYSTEM SHALL let a test answer a pending M-Bus read with telegrams given as bytes or as hexadecimal text, delivering them through the production callback chain and decoder and stamping the receipt from the virtual clock.
- ADDED AC-TKIT-016.2 -> docs/specs/testkit.md : THE SYSTEM SHALL let a test answer a pending M-Bus read with a provider response code, with or without the telegrams read before the failure.
- MODIFIED AC-EMIT-002.8 -> docs/specs/emission.md : THE SYSTEM SHALL declare a `[DefaultMinInterval]` of 30 s on each diagnostics summary it ships: `ModbusLinkSummary`, `ModbusTcpConnectionSummary`, `MBusLinkSummary`, `HttpClientSummary` and `HttpServerSummary`.

---

## Tasks

> Cut after ratification. The order the doc implies: the bench (outside this repo) → `MBUS` into
> `owns:` and the roster → decoder with its vectors → contract, handler and diagnostics → kit →
> example → distill and archive.
