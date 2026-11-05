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
It is driven by ecocoach's legacy fleet (84 lines, 1 317 meters) and the AVM project's five meters
(VION-436/438). It answers [ecocoach's proposal](#where-this-comes-from) from the SDK's side.

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

- `D1` — **Wire option A: the provider sends raw telegrams and the SDK decodes them**, conditional on
  ST being able to read multi-telegram meters raw (§ ST feasibility). A failed bench reopens the
  choice (question 1).
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
  callbacks are `(result, receipt)` and `(exception, receipt)`, per decision 0119. The address is an
  `int` refused outside its range, as `unitIdentifier` is (`IModbusClient.cs:605`, `AC-MODB-004.1`).
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

1. **(c) Can ST read multi-telegram meters raw?** This is a STOP: the doc stays `proposed` until the
   bench in § ST feasibility answers. If it can't, the options are (i) ST drives the KL6781 link layer
   itself, or (ii) fall back to `B′`, records with string units (§ Alternatives). The recommendation is
   (i) if the bench shows the process-image handshake is tractable, else (ii).
2. **(c) Is this the decoding experience to build?** § The decoding experience proposes it: three
   layers, a selector a block declares once, values converted into the unit the block publishes, and
   `Describe()` for authoring. The operator asked for this to be defined, so it waits for the operator.
3. **(c) Default operation timeout of 60 s.** The timeout includes the provider's queue (§ Timeouts).
   60 s covers one pass of a full single-telegram KL6781 line at 2400 Bd. Alternatives: 30 s, with
   blocks on busy lines overriding it, or have the provider report its own queue wait.
4. **(c) Own diagnostics and exception types, or a shared set?** v1 proposes M-Bus-named receipt,
   outcome, link summary and exceptions, with no dependency on any Modbus package. The alternative is a
   transport-neutral set in core, which would also be a guideline for the next protocol (CAN, KNX). But
   moving Modbus's published types there is breaking, and that is its own change.
5. **(b) Deadline in the payload?** If the bench shows ST can't read a received message's expiry, the
   request gains a relative `expiresInMs` field. Otherwise the deadline rides only as MQTT message
   expiry, as RTU's does (`AC-MODB-015.11`).

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
| **Provider DX** | The wire is flat: two fields in, an array of base64 strings out. There is no vocabulary to map and nothing to decode, and the provider returns frames as they came off the line. It is testable with any MQTT 5 client (§ The wire). Line configuration stays the provider's own. |

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
| `InvalidRequest` | Address outside 0–250, or more telegrams than the provider supports |

A failure may still carry the telegrams read before it, which is how a partial read reaches `D12`.

**What a provider must do** (carried on the page as provider obligations, not as SDK criteria):

1. **One request at a time per line, first in, first out.**
2. **Run the link layer.**
   - SND_NKE first, expecting E5h.
   - Then REQ_UD2, toggling the frame-count bit (FCB) per EN 13757-2, until a telegram arrives without
     the more-records-follow marker (DIF 1Fh) or `maxTelegrams` telegrams are in hand.
   - Repetitions follow its own setting; the standard suggests up to two.
3. **Return every frame verbatim.** It decodes nothing, and it does not report whether the read is
   complete: the SDK reads that from the last telegram.
4. **Drop a request whose deadline passed before its turn** (the MQTT message expiry, or
   `expiresInMs` if question 5 adds it). A dropped request gets no answer: dale has already timed it
   out.
5. **Answer every other request exactly once.**
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
    TimeSpan DefaultOperationTimeout { get; set; }   // default 60 s (question 3)
    TimeSpan? MaxQueuedAge { get; set; }             // default 30 s; null = no limit
    MBusLinkSummary Link { get; }

    void Read(int primaryAddress,                    // 0–250
              int maxTelegrams,                      // ≥ 1: what this meter sends
              IActorDispatcher dispatcher,
              Action<MBusReadout, MBusReceipt> successCallback,
              Action<Exception, MBusReceipt>? errorCallback = null,
              TimeSpan? operationTimeout = null);
}
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
    MBus.Read(PrimaryAddress, maxTelegrams: 2, this,
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
The ecocoach research puts one single-telegram read at 2400 Bd at about 1.25 s. A full 41-slot KL6781
line therefore takes about 51 s per pass, which is what the 60 s default covers (question 3). The
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

### ST feasibility — the bench that decides `D1`

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

**How the results decide `D1`:**

| Result | Decision |
|---|---|
| F2 holds | A, on a documented FB. Done. |
| F2 fails, F3 holds | A, on a documented-but-internal member. Pin the library version in the provider and re-run E3 on every library update. |
| Both fail | Question 1: ST drives the KL6781 itself, or `B′`. |

### Deferred — not in v1, each with what brings it back

| Item | Comes back when |
|---|---|
| Writes (`SND_UD`, CI 51h) | Hager ECM381D and K'Electric KEPx write a data-set selection (`EM/…HagerECM381D.TcPOU:256-279`, `EM/…KElectricKEPx.TcPOU:232-253`); either is ported |
| Secondary addressing, scan, commissioning | a site needs it; the legacy fleet is primary-only (0–213 on eco-prod) and has no scan code |
| Rewind options (application reset, none) | a meter needs other than SND_NKE; every legacy meter sends SND_NKE |
| Per-request baud | a line mixes rates; all 84 surveyed lines run 2400 Bd |
| A telegram builder in the kit | a test can't get a capture |
| A DevHost stand-in for request/response contracts | its own change; it would serve RTU too (decision 0139 tiers) |
| Moving wire records and helpers to `Vion.Contracts` | a C# provider is built (`D4`) |
| Wireless M-Bus | a project needs it; the decoder is kept free of the wired transport so the record layer carries over |

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

  It is the fallback if the bench fails and ST driving the KL6781 itself is too costly (question 1).
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

- _(none yet — append as implementation diverges from Full design)_

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
- ADDED AC-MBUS-004.1 -> docs/specs/mbus.md : THE SYSTEM SHALL default the operation timeout to 60 seconds and the maximum queued age to 30 seconds, and SHALL accept `null` as no queued-age limit.
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
- ADDED AC-MBUS-007.2 -> docs/specs/mbus.md : THE SYSTEM SHALL set the link state to `Online` on a success, to `Faulted` on no response, a garbled answer, an unavailable line, a timeout or a protocol error, and SHALL leave it unchanged on every locally decided outcome and on a full provider queue.
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
