---
slug: mbus-service-provider
status: proposed           # proposed | in-flight | parked | archived
blocked-on: none           # for parked docs: what's blocking + ref
areas: MBUS, TKIT, EMIT
author: Jonas Bertsch (drafted in a Claude session)
created: 2026-11-05
updated: 2026-11-10
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
service provider that owns the master, shaped like `IModbusRtu`. The provider returns the telegrams
exactly as received, and the SDK checks, decodes and scales them. A block picks values **by meaning**
(quantity, storage, tariff, subunit), or by code where meaning isn't enough. v1 has reads only, primary
addressing, one read in flight per binding, and a kit that replays captured telegrams. It is driven by
ecocoach's legacy fleet (84 lines, 1 317 meters) and the AVM project's five meters (VION-436/438).

### Spec implications

A new page `docs/specs/mbus.md` under a new area code `MBUS`. Its code goes into
`docs/specs/_corpus.yml`'s `owns:` and its row into `docs/spec-process.md` § The roster in the first
implementation commit, as § The roster requires. The page specifies:

- the block-facing contract and the handler;
- frame checks, decoding and record selection;
- the link diagnostics;
- the wire, because its records live in this package in v1 (`D4`).

`docs/specs/testkit.md` gains a seventh kit (`AC-TKIT-016.*`), and every place that says "six kits"
moves to seven: `spec-process.md:49`, `testkit.md:579`, `analyzers.md:486`. `AC-EMIT-002.8` is
`MODIFIED` to name `MBusLinkSummary`. `_invariants.md`'s release roster (`SYS-REL-001`,
`scripts/set-version.ps1`) names both new packages. `contracts.md` is cited, not changed: binding, the
handler base, link maps and publishing are `AC-BIND-*` as they stand.

### Decisions

**(a) Ratified by the operator:**

- `D1` — **Wire option A: the provider sends raw telegrams and the SDK decodes them** (lean
  2026-11-05, settled 2026-11-10 by the ST bench). An ST provider can return every telegram raw: the
  first through `FB_MBUS_RawData`, and telegrams 2…n through `Tc2_MBus`'s frame buffer, with the
  library pinned (§ ST feasibility). A provider may also return fewer telegrams than asked; the readout
  then reports itself incomplete (§ First telegram only).
- `D2` — **Unused features stay out of v1**: no writes, no secondary addressing or scan, no rewind
  options, no per-request baud (§ Forward view).
- `D3` — **One read in flight per binding.** A `Read` while that binding's previous read is pending
  invokes **neither callback** (2026-11-10). It is counted in `Link` and logged at Debug, and the
  pending read's callbacks still arrive. Precedent: a disabled Modbus client invokes neither callback
  (`AC-MODB-002.2`).
- `D4` — **Single-repo for v1.** The wire records live in this package. Moving them into
  `Vion.Contracts` for a C# provider is a follow-up that may need its own cross-repo spec. The move is
  wire-neutral: the `schema` user property is the record's name (`AC-MBUS-005.1`).
- `D5` — **The ST provider is outside this process.** It is ecocoach's, in another GitHub org. This
  doc owes it a wire it can implement (§ The wire); it does not specify it.
- `D6` — **The decoding experience is selectors by meaning plus code** (2026-11-10):
  - `MBusSelector.Of(quantity, storage, tariff, subunit, function)` names a record by meaning;
  - `MBusSelector.ByCode(...)` names it by its DIF/VIF bytes, for records whose extension bytes change
    their meaning and for records outside the table;
  - `readout.Value(selector, unit)` converts within a quantity's units;
  - `Describe()` prints every record with a selector that finds it.

  There are no decoding helpers in v1 (§ The decoding experience).
- `D7` — **The default operation timeout is 300 s** (2026-11-10). It includes the provider's line
  queue, so it is sized to one pass of a full 40-meter KL6781 line of six-telegram meters, about 240 s,
  with margin (§ Timeouts).
- `D8` — **An M-Bus-named diagnostics set and one exception** (2026-11-10). `MBusReceipt`,
  `MBusOutcome` and `MBusLinkSummary` mirror the Modbus types field for field. Every failure reaches
  the error callback as one `MBusException`, whose `Outcome` equals the receipt's, so the receipt is
  the discriminator decision 0119 intends. There is no dependency on any Modbus package.

**(b) Decided here, each with its precedent:**

- `D9` — **The wire mirrors RTU's.**
  - Routing key `/hw/mbus`, action `/hw/mbus/get`, and dale subscribes to `/hw/mbus/get/dale/response`.
  - The records are `GetMBusPayload` and `GetMBusResponsePayload`, with `responseCode` and
    `errorMessage` as on `GetModbusResponsePayload`, and frames in base64.
  - Precedent: `Vion.Contracts/Hw/Modbus/*` (vion-contracts `7eb4d0b`) and `ModbusRtuHandler.cs:69-72`.
  - `/hw/mbus` and `/hw/modbus` are not substrings of each other, so dale's `topic.Contains` dispatch
    keeps them apart (decision 0014's routing-segment rule).
- `D10` — **Checking and decoding happen block-side, in the contract's own implementation.** The
  handler carries bytes only, as RTU's does (`ActorMessages.cs:49`, `byte[]? Data`). A frame that
  fails its checks or can't be decoded corrects the outcome to `ProtocolError` before it reaches the
  link summary, the pattern `AC-MODB-015.8` states for RTU's conversion.
- `D11` — **The block surface copies `IModbusClient`'s shape.**
  - The settings are `IsEnabled` (default false), `DefaultOperationTimeout`, `MaxQueuedAge` and `Link`
    (`IModbusClient.cs:60-87`). The callbacks are `(result, receipt)` and `(exception, receipt)`.
  - `Read(int primaryAddress, int maxTelegrams, …)` takes plain arguments, as `unitIdentifier` is
    (`IModbusClient.cs:605`). The count has no default: a default of one would silently starve a
    multi-telegram meter.
  - An out-of-range argument is refused through the error callback, never inline, per the refusal rule
    `modbus.md` § What a call is refused for states.
  - The draft's address and options types are dropped: an overload is binary-safe, so the features in
    § Forward view arrive as overloads.
- `D12` — **One shared handler per runtime**, with RTU's pending limit, expiry sweep and link-map
  routing. It is a copy of `ModbusRtuHandler`, the named precedent, not a generic base lifted into
  `Vion.Dale.Sdk`. A base in core would widen this change into RTU's code. Extract it when a third
  request/response contract arrives.
- `D13` — **Values are `double`, scaled into the quantity's unit.**
  - The scaling uses the VIF's exponent and the multiplicative and additive correction extensions
    (VIFE `E111 0nnn`, `E111 10nn`). The EM530 sends six record types that need them (§ The decoding
    experience).
  - Block properties are `double`, and Modbus delivers `float`/`double`.
  - A 12-digit BCD value is exact in a `double`. A binary counter past 2^53 is not, and the record's
    raw bytes remain for it.
- `D14` — **Only `Ok` carries telegrams, and a failure carries no partial data.** The legacy treats a
  broken-off read as an error too (`Tc2_MBus` error 120). The one exception is a `ProtocolError`,
  whose exception carries the telegrams as received, so a block can log the capture.
- `D15` — **The SDK checks every frame before decoding it.** The checks are:
  - start, length, checksum and stop byte;
  - CI 72h;
  - the address field equals the address asked;
  - the identification is the same in every telegram of one read.

  A failure is a `ProtocolError`. eco-prod has three duplicate-primary-address clusters live
  (investigation hub § 2.6), where a reply from the wrong meter must not read as data.
- `D16` — **The status helper reads the standard's error bits only.** `HasError` is application status
  10b/11b, or the permanent or temporary error bit. The EM530 sends status `20h`, a manufacturer bit, in
  every telegram (bench log, `HDR` lines).
- `D17` — **`NotSupported` ships in v1.** It is the one forward hook that can't be added later: a v1
  provider has to answer what it doesn't know, or a later contract version meets it with a timeout
  (§ Forward view).
- `D18` — **The contract-type token is `MBus`, chosen once.** It is the one identifier whose rename
  breaks pairing (`docs/identifier-stability.md`). The packages are `Vion.Dale.Sdk.MBus` and
  `Vion.Dale.Sdk.MBus.TestKit`.

### Reviewer's questions

1. **(c) Is a first-telegram v1 enough for AVM?** The EM5XX's export and T1/T2 billing points sit in
   telegrams 3 and 5.
   **OUTCOME (2026-11-10): moot.** On the bench, F1 and F3 hold, so an ST provider can serve them raw.
   Whether ecocoach's first provider reads every telegram or starts first-telegram is its own choice
   (`D5`); the contract serves both.
2. **(c) Is this the decoding experience to build?**
   **OUTCOME (2026-11-10): yes**, selectors by meaning plus code (`D6`).
3. **(c) The default operation timeout?**
   **OUTCOME (2026-11-10): 300 s** (`D7`), up from the draft's 60 and then 120.
4. **(c) Own diagnostics and exception types, or a shared set?**
   **OUTCOME (2026-11-10): an M-Bus-named set with one exception** (`D8`).
5. **(b) Deadline in the payload?**
   **OUTCOME (2026-11-10): MQTT expiry only.** F4 holds: `Tc3_IotBase`'s `FB_IotMqtt5Client` hands ST
   `pubExpiryInterval` with the seconds remaining. A message published with 60 s and delivered 15 s
   later read 45 (`mbus-st-bench` `30c2c5d`).
6. **(b) For the PR's reviewer: is the decoder's v1 table the right size?**
   - It is EN 13757-3's primary VIF table, plus the FD and FB extension entries the five targets send,
     plus the combinable correction extensions (§ The decoding experience).
   - Anything else decodes as a record with no quantity, reachable by code.
   - The quantity and unit enums are pinned against the targets' captures in `T-002`, not against the
     whole standard.

---

## Full design

### Where this comes from

- **ecocoach's proposal and research.** `logic-block-libraries` PR #195,
  `docs/notes/2026-11-04-mbus-service-provider-proposal.md` and its investigation hub; the telegram map
  (hub § 2.9) is at `97f84c3a`. Its block-facing sketch, its three diagnostic tiers and its
  line-configuration principle carry over. Option B (the provider decodes) was replaced by A, and the
  surface was cut to the consumers that exist now.
- **The ST bench**, 2026-11-10: `mbus-st-bench` `aadb307` (runs), `8f82de0` (README). It answered the
  provider-side questions on a real CX8200, KL6781 and EM530 (§ ST feasibility).
- **Two review rounds**, 2026-11-10: the author's pass and an independent design review that walked the
  EM530 capture through the API. Their findings are folded in, and the operator decided `D3`, `D6`,
  `D7` and `D8`.

### Three objectives, and how each choice serves them

| Objective | What serves it |
|---|---|
| **Block-author DX** | One call reads a meter. Values are picked by meaning with a selector declared once, and arrive scaled in the unit the block publishes. `Describe()` turns a capture into ready-to-paste selectors. A selector that finds nothing on a complete readout says so once, in the log. The kit replays captured telegrams through the production decoder. A busy line never makes a block's values flicker (`D3`). |
| **SDK consistency** | The settings, callbacks, receipt, link summary, enablement, refusal rule, shared handler and wire all mirror RTU's (`D9`–`D12`). The kit's names mirror the RTU kit's. Surface marks, the analyzer wiring, `[DaleSharedAssembly]`, an example and a kit come as with every protocol package. |
| **Provider DX** | The wire is flat: two fields in, an array of base64 strings out. There is no vocabulary to map and nothing to decode. Frames go back as they came off the line. A provider may start with the first telegram only, and it answers anything newer than itself `NotSupported`. It is testable with any MQTT 5 client. Line configuration stays the provider's own. |

### The wire — what a provider implements

Topic, under the contract's base `{installationTopic}/{serviceProviderId}/{service}/{contract}`:

| Direction | Topic suffix | Payload |
|---|---|---|
| dale → provider | `/hw/mbus/get` | `GetMBusPayload`, with an MQTT 5 response topic, correlation data and message expiry |
| provider → dale | the request's response topic (`/hw/mbus/get/dale/response`) | `GetMBusResponsePayload`, correlation data echoed |

JSON follows the `Hw` rules (decision 0227): camelCase, enums by member name, bytes in base64, the
`schema` user property set to the record's name, content type `application/json`.

```json
// request: primary address 1, a meter that sends five telegrams
{ "primaryAddress": 1, "maxTelegrams": 5 }

// answer: every RSP_UD long frame as received, from 68h to 16h inclusive, one base64 string each
{ "responseCode": "Ok", "errorMessage": null, "telegrams": [ "aGlpaAgBchNmAAA2HN0C0CAAAAcD…", "…" ] }

// failure: no telegrams
{ "responseCode": "NoResponse", "errorMessage": "no answer after 2 repetitions", "telegrams": null }
```

| `responseCode` | Meaning | Outcome | Link state |
|---|---|---|---|
| `Ok` | the meter answered; 1…`maxTelegrams` telegrams follow | `Success`, or `ProtocolError` if a frame fails the checks (`D15`) | `Online` / `Faulted` |
| `NoResponse` | silent after the provider's repetitions | `NoResponse` | `Faulted` |
| `Garbled` | checksum, framing, length or collision fault on the line | `Garbled` | `Faulted` |
| `LineUnavailable` | terminal or port not ready | `LineUnavailable` | `Faulted` |
| `NotSupported` | an action, or a request member, this provider doesn't implement | `Invalid` | unchanged |

**What a provider must do** (carried on the page as provider obligations, not SDK criteria):

1. **One request at a time per line, first in, first out.** The queue holds at least one request per
   meter on the line (41 on a KL6781). `D3` keeps it from needing more.
2. **Run the link layer.**
   - SND_NKE first, expecting E5h.
   - Then REQ_UD2, toggling the frame-count bit (FCB) per EN 13757-2, until a telegram arrives without
     the more-records-follow marker (DIF 1Fh) or `maxTelegrams` telegrams are in hand.
   - Repetitions follow its own setting; the standard suggests up to two.
3. **Return every frame verbatim.** It decodes nothing, and the SDK reads completeness from the last
   telegram. A provider that can read fewer telegrams than asked answers `Ok` with the ones it read.
4. **Drop a request whose MQTT message expiry has passed before its turn.** A dropped request gets no
   answer: dale has already timed it out.
5. **Answer every other request exactly once.** That includes a request on an action under `/hw/mbus/`
   it doesn't implement, or one carrying a member it doesn't know: answer those `NotSupported` rather
   than ignoring the member (`D17`).
6. **Keep line settings as its own service properties** (baud, repetitions, limits). The contract
   carries none, as RTU's carries no serial settings.
7. **Report line health through the platform's component health**, one sub-component per line. This
   is guidance: the SDK neither sees nor checks it.

Size: a long frame is at most 261 bytes, about 350 base64 characters; five EM530 telegrams are about
700 characters of JSON. A provider is testable before any dale exists. Publish a request from any
MQTT 5 client that sets a response topic and correlation data (MQTTX does), and compare the frames
against the meter's datasheet or a libmbus read.

### The block API

```csharp
namespace Vion.Dale.Sdk.MBus;

[PublicApi]
[ServiceProviderContractType("MBus")]
public interface IMBus
{
    bool IsEnabled { get; set; }                     // default false
    TimeSpan DefaultOperationTimeout { get; set; }   // default 300 s; includes the provider's line queue
    TimeSpan? MaxQueuedAge { get; set; }             // default 30 s; null = no limit
    MBusLinkSummary Link { get; }

    void Read(int primaryAddress,                    // 0–250
              int maxTelegrams,                      // ≥ 1: how many telegrams this meter sends
              IActorDispatcher dispatcher,
              Action<MBusReadout, MBusReceipt> successCallback,
              Action<Exception, MBusReceipt>? errorCallback = null,
              TimeSpan? operationTimeout = null);
}
```

An EM5XX block, written against the bench's EM530 capture (record codes from `mbus-st-bench`
`logs/2026-11-10-all.txt`; subunit meanings per Carlo Gavazzi's frame table, hub § 2.9), the way the
Modbus RTU example is written (`Em122ElectricityMeter.cs:200-227`):

```csharp
[ServiceProviderContractBinding(Identifier = "MBus", DefaultName = "M-Bus")]
public IMBus MBus { get; set; } = null!;

private static readonly MBusSelector ImportSel    = MBusSelector.Of(MBusQuantity.Energy);              // 07 03, t1
private static readonly MBusSelector PowerSel     = MBusSelector.Of(MBusQuantity.Power);               // 04 2A, t1
private static readonly MBusSelector CurrentL1Sel = MBusSelector.Of(MBusQuantity.Current, subunit: 1); // 84 40 FD 59, t1
private static readonly MBusSelector ImportT1Sel  = MBusSelector.Of(MBusQuantity.Energy, subunit: 6);  // 84 80 C0 40 05, t5
private static readonly MBusSelector FrequencySel = MBusSelector.Of(MBusQuantity.Frequency);           // 02 FB 2E, t3

protected override void Ready()
{
    MBus.IsEnabled = true;
}

[Timer(60)]
public void Poll()
{
    MBus.Read(PrimaryAddress, maxTelegrams: 5, this,
              (readout, receipt) =>
              {
                  ImportEnergy = readout.Value(ImportSel, MBusUnit.KilowattHour);   // null if absent
                  ActivePower  = readout.Value(PowerSel, MBusUnit.Kilowatt);
                  CurrentL1    = readout.Value(CurrentL1Sel, MBusUnit.Ampere);
                  ImportT1     = readout.Value(ImportT1Sel, MBusUnit.KilowattHour);
                  Frequency    = readout.Value(FrequencySel, MBusUnit.Hertz);
                  MeterError   = readout.Header.Status.HasError;
                  Link         = MBus.Link;
              },
              OnError);
}
```

A heat meter's due-day pair reads the same way: `readout.Value(MBusSelector.Of(MBusQuantity.Energy,
storage: 1), MBusUnit.KilowattHour)` for the value, and `readout.Find(MBusSelector.Of(
MBusQuantity.DateTime, storage: 1))?.Timestamp` for its date.

**Outcomes.** `MBusOutcome` takes Modbus's names where the meaning is the same and adds M-Bus's own:

- the same as Modbus: `Success`, `Timeout`, `ProtocolError`, `Expired`, `Dropped`, `Invalid`;
- M-Bus's own: `NoResponse`, `Garbled`, `LineUnavailable`.

The link state is `Online` on `Success`. It is `Faulted` on `NoResponse`, `Garbled`, `LineUnavailable`,
`Timeout` and `ProtocolError`. `Expired`, `Dropped` and `Invalid` leave it unchanged, as do the
outcomes decided locally and a `NotSupported` provider, because none of them says anything about the
meter. This is RTU's split (`AC-MODB-016.2`), except that a silent meter is `Faulted` here. On RTU a
gateway's "no answer" (0x0B) maps to `DeviceError` and so to `Online` (`ModbusRtuHandler.cs:188`); that
is a separate RTU item, not this change.

**The exception (`D8`).** `MBusException` carries:

- `Outcome`, the same as the receipt's;
- `ResponseCode`, the provider's, where one arrived;
- for a `ProtocolError`, `Telegrams` as received.

Its message is rendered in the invariant culture, as Modbus's are (`AC-MODB-001.9`). Every failure is
logged at Error, once per read, as RTU's are (`ModbusRtu.cs:1252`, `AC-MODB-001.6`). A busy read is
logged at Debug, as RTU's skipped request is (`ModbusRtu.cs:1249`).

**The link summary.** `MBusLinkSummary` has `ModbusLinkSummary`'s shape:

- state, last contact and last failure;
- a counter per outcome, plus `BusyCount` (`D3`);
- the 15-minute windowed round-trip and queued-wait figures.

It declares the 30 s `[DefaultMinInterval]` (`AC-EMIT-002.8`, `MODIFIED`). Its accumulator mirrors
`ModbusLinkAccumulator` and takes no dependency on the Modbus packages (`D8`). A rising `BusyCount` is
the line-load signal ecocoach's proposal wanted (its § 8).

**One read in flight (`D3`).** A `Read` while the binding's previous read is pending is dropped. It
publishes nothing, invokes neither callback, adds one to `BusyCount`, and is logged at Debug. The
pending read's callbacks arrive as they would have. The slot is freed by every completion: success,
failure, timeout, expiry, or a refusal.

- **It bounds the queue.** The provider's queue holds at most one entry per binding, and fairness
  between meters emerges as round-robin.
- **It is the legacy's own behaviour.** Each legacy meter waits for `NOT bBusy` and tries again only
  at its next interval (`MBusManager.TcPOU:314`, from an independent read of `RealtimeSystem`
  `ff8ee4a99`).
- **It is documented on `Read`.** The one way to be surprised by it is to issue two reads per tick on
  one binding. M-Bus has no reason to: one read returns the whole meter.

**Timeouts (`D7`).** As on RTU, the operation timeout starts when the handler publishes
(`AC-MODB-015.3`). Here it therefore includes the provider's queue, which with `D3` holds at most one
request per meter on the line. The XML docs say so, because `IModbusClient`'s say the timeout covers
"the wire only" (`IModbusClient.cs:38-40`).

- **The bench measured the EM530 at 2400 Bd through `Tc2_MBus`:** 1.27 s for SND_NKE plus one telegram,
  and 3.75 s for five telegrams. The library leaves about half a second between telegrams, so this is
  above the investigation's line-time model of 2.4–2.6 s.
- **What that means for a pass of a line:**
  - AVM's HV1, 32 EM5XX: about 120 s;
  - a full KL6781 line of 40 six-telegram meters: about 240 s.

  The 300 s default covers both with margin, and a dead provider surfaces as `Timeout` after five
  minutes.
- **This is the line's existing speed, not a regression.** The legacy runs on the same library, so
  HV1's 60 s interval is already stretched to about two minutes today.
- **Sweep and deadline.** The handler's expiry sweep runs about once a second, as RTU's does
  (`AC-MODB-015.4`). The request's deadline is its publish instant plus its operation timeout
  (`AC-MODB-015.11`'s rule), and it lets a provider drop a stale request instead of spending line time
  on it.

### The decoding experience

The generic EN 13757-3 decode is not device-specific, and every block gets it. What stays with the
block is the device-specific part: which records mean what for this meter (the legacy hard-coded it by
record position, which ecocoach lists as must-not-port), and the vendor's quirks. The EM530 capture
shows what that means in practice:

| Telegram | What it carries | How it is told apart |
|---|---|---|
| 1 | import energy `07 03`; reactive energy `04 FB 82 75`; power `04 2A`; reactive and apparent power `04 FB 97 72`, `04 FB B7 72`; power factor `02 FD BA 73`; voltages `FD 48`; currents L1–L3 `FD 59` | **subunits** 1–3 for the phases |
| 2 | per-phase power, reactive power, apparent power and power factor | subunits 1–3 |
| 3 | voltages L-N (subunits 1–3) and L-L (subunits 5–7); energy `05` and reactive energy at subunits 4–5; frequency `02 FB 2E` | subunits |
| 4 | per-phase energy (subunits 1–3); power at subunits 4–5; operating time `04 A6 74` | subunits |
| 5 | energy at subunits 6–7 (import T1/T2); current at subunit 4 | subunits |

All five telegrams carry identification 00006613, manufacturer GAV, medium 02 and status 20h.

Three things follow:

1. **A wrong scale would be silent.** Five of those record types carry a correction extension (`75`,
   `72`, `73`, `74`). Without it, operating time reads 9261 h instead of 92.61 h, which is exactly what
   `Tc2_MBus` 3.5.4 reports for that record in the bench log.
2. **Subunit is how this meter says "which one".** Phases, L-L against L-N, and tariffs are all
   subunits. The selector names subunit as a first-class qualifier, and `Describe()` prints it.
3. **The newer extension tables matter.** Reactive and apparent power, power factor and frequency come
   from the FB and FD tables, and `Tc2_MBus` 3.5.4 labels some of them "Reserved".

**Layer 1 — the readout, by meaning.**

- **The header** comes from the first telegram (CI 72h):
  - identification, as its 8 BCD digits in text, keeping leading zeros;
  - manufacturer, three letters;
  - version, medium and access number, as bytes;
  - `Status`, an `MBusStatus` with `HasError` (`D16`), `PowerLow` and the raw `Value`.
- **Each record** carries:
  - `Quantity`, an `MBusQuantity?`, which is null when the decoder's table doesn't know the VIF;
  - `Function`, `Storage`, `Tariff` and `Subunit`;
  - the value, as one of three: `Number` (a `double`, already scaled into the quantity's unit,
    including the correction extensions), `Timestamp` (date and time types F/G/I/J) or `Text`;
  - its bytes, as received: `Dib`, `Vib` and `Data`.
- **Record error codes.** A record carrying one of EN 13757-3's record error codes (VIFE `E000 xxxx`
  in a response) is kept with no value.
- **Fillers and manufacturer data.** Idle fillers (2Fh) are skipped. Manufacturer-specific data after
  DIF 0Fh/1Fh is kept as the telegram's bytes and not decoded.

**The decoder's v1 table** (question 6) is EN 13757-3's primary VIF table, plus the FD and FB entries
the five targets send, plus the multiplicative and additive correction extensions. Every other VIFE
counts as **meaning-changing**: for example 3Bh/3Ch (accumulation of positive or negative contributions
only), limit values, or "per hour". A record that carries one keeps its quantity, but `Of()` never
matches it. It is reached by code, so a selector by meaning can't silently return a record that isn't
the plain value.

**The readout** joins the records of every telegram in order. It offers `Find(selector)` and
`Value(selector, unit)`, plus `Header`, `Telegrams`, `Records`, `IsComplete` and `Describe()`.

- **`MBusSelector.Of(quantity, storage = 0, tariff = 0, subunit = 0, function = Instantaneous)`**
  names a record by meaning; the defaults pick "the current value". A block declares its selectors once,
  as `static readonly` fields, which reads like the meter's datasheet.
- **`Value(selector, unit)`** returns a `double?`. It is `null` when nothing matches, and it converts
  within a quantity's units:
  - Wh, kWh and MWh for energy (a meter reporting in J converts too);
  - varh and kvarh for reactive energy;
  - m³ and L for volume;
  - W and kW for power; var and kvar; VA and kVA;
  - m³/h and L/h for volume flow;
  - V, A, Hz and h;
  - °C only for an absolute temperature, and K only for a temperature difference, so nothing shifts by
    273.15.

  A unit the matched record can't be expressed in throws an `ArgumentException` naming the selector,
  the record and the unit. That happens inside the success callback, where the middleware contains it
  (`AC-LIFE-014.2`). A mismatch is a fixed property of the block's declarations, so the first test that
  replays a capture finds it.
- **A miss is logged.** A selector that matches nothing on a *complete* readout is logged at Warning,
  once per selector per contract, naming the selector and the meter. A null forever would otherwise be
  silent (`docs/sdk-surface-conventions.md` § 7). On an incomplete readout a miss is expected and not
  logged.

**Layer 2 — by code.** `MBusSelector.ByCode("84 80 C0 40 05")` matches a record by its exact DIB and
VIB bytes, as a datasheet lists them. It is the way to records with a meaning-changing extension and to
records outside the table. Every record keeps its `Dib`, `Vib` and `Data`.

**Layer 3 — the telegram.** `MBusTelegram.Raw` is the whole frame, for anything else. It is the escape
hatch, not the expected path; no v1 consumer needs it beyond logging a capture.

**The authoring loop:**

1. **Capture a telegram.** The provider's log has it. So does the block, via `readout.Telegrams[i].Raw`
   as hex, or via the `MBusException` of a `ProtocolError`. The bench log already holds all five EM530
   telegrams, and public vectors exist for a Supercal 531, an Elster TMP-A water module and a Gavazzi
   EM111 (ecocoach research, `vendor-target-meters.md` §§ 2.8, 5.3, 6.8).
2. **Describe it in a test.** `MBusTelegram.Parse("68 69 69 68 08 01 72 …").Describe()` prints one line
   per record:
   - its quantity or "—";
   - its value and unit;
   - storage, tariff, subunit and function;
   - its DIB and VIB bytes;
   - a selector that finds that record: `Of(...)` where that is unambiguous, `ByCode(...)` otherwise.
3. **Write the block with those selectors**, and test it by replaying the capture through the kit.

**Frame checks (`D15`).** Each of these completes the read through the error callback with a
`ProtocolError` and the telegrams as received:

- an `Ok` response with no telegram, or with more than were asked for;
- a bad start, length, checksum or stop byte;
- a CI other than 72h;
- an address field other than the one asked;
- an identification that changes between telegrams.

### TestKit — `Vion.Dale.Sdk.MBus.TestKit`

The kit's names and shapes follow the RTU kit (`IModbusRtuExtensions.cs:32-112`,
`LogicBlockTestContextExtensions.cs:21`):

- **`mBus.SimulateReadResponse(testContext, params string[] telegramsHex)`** answers the pending read
  through the production callback chain, including the frame checks and the decoder, stamped from the
  virtual clock.
- **`mBus.SimulateReadError(testContext, MBusOutcome outcome)`** completes the pending read through the
  error callback with that outcome. One exception type means the outcome is all a test needs (`D8`).
- **`testContext.VerifyMBusReadSent(mBus?, primaryAddress?, maxTelegrams?, times?)`** asserts that a
  read was sent.

One difference from the RTU kit is stated on purpose. RTU's simulations answer the most recent matching
request and leave it answerable again (`AC-TKIT-009.3`). Here a simulation answers the pending read and
completes it, because `D3` allows only one read in flight. A second simulation with no read pending is
refused, naming what the block must do first. There are no byte-array overloads and no telegram builder
in v1 (`D2`): hex captures are the consumers' path.

### Package layout

The layout follows `Vion.Dale.Sdk.Modbus.Rtu`:

- **`Vion.Dale.Sdk.MBus`**, targeting `netstandard2.1`, contains:
  - the contract and its implementation, including the frame checks and the decoder (`D10`);
  - the handler (`[InternalApi]`) and the actor messages;
  - the readout, selector, quantity, unit and status types;
  - the diagnostics and the exception;
  - the wire records with their own source-generated JSON context (`internal` in v1; `D4`);
  - a public `DependencyInjection : IConfigureServices`, as RTU's (`modbus.md:679-683`).
- **`[assembly: DaleSharedAssembly]`**, because the actor messages cross the plugin boundary
  (`AC-MODB-018.4`'s rule). Accepted consequence: the first plugin a gateway loads fixes this assembly's
  version for all of them (`architecture/systems/dale.md`, plugin sandboxing). A decoder fix therefore
  reaches a gateway when the libraries on it are rebuilt against it.
- **The usual package obligations:**
  - the analyzer `ProjectReference` and a declared published namespace, so `DALE014` asks for surface
    marks;
  - `[PublicApi]`/`[InternalApi]` on every public type;
  - the manifest snapshot;
  - the release roster (`docs/sdk-surface-conventions.md` §§ 4, 8).
- **`Vion.Dale.Sdk.MBus.TestKit`**: § TestKit.
- **`examples/Vion.Examples.MBus`**: the EM5XX block above and a test replaying the bench capture, like
  `examples/Vion.Examples.ModbusRtu`. A DevHost stand-in is deferred (§ Simulation).

### ST feasibility — the bench, and what it decided

**The question** was whether an ST provider on `Tc2_MBus` can return telegrams 2…n raw. The library
documents `FB_MBUS_RawData` as "only the first telegram", and its multi-telegram FB returns decoded
records (ecocoach research, `vendor-mbus-standard-kl6781.md` § 8.4). The bench is
`C:\_gh\mbus-st-bench` (`aadb307` runs, `8f82de0` README). Its README carries the program, the
checklist and how to repeat it.

**Setup.** The test CX8200 `BTN-000tr9nu` (VION image, TwinCAT 4026), with a KL6781 directly on the
CX8200's own K-bus, and a Carlo Gavazzi EM530 at primary address 1, 2400 Bd. `Tc2_MBus` 3.5.4, built
with XAE 3.1.4026.28. The evidence is the clean run `logs/2026-11-10-all.txt` and its `.report.md`,
from cleared journals with no sequence gaps. That repository is local to the operator's machine and has
no remote yet.

| | Question | Result | What it shows |
|---|---|---|---|
| **F1** | What does `FB_MBUS_RawData` return? | **holds** | `arrData` holds the whole frame, 68h…16h. `iLen` is the frame length after a REQ_UD2, but one more (112 for 111) when the read began with SND_NKE. **A provider takes the length from the frame's L field (L + 6), never from `iLen`.** Whether it refuses a CI other than 72h stays open: the EM530 answers only 72h |
| **F2** | Does a repeated `FB_MBUS_RawData` step to the next telegram? | **fails** | It sends every REQ_UD2 with FCB = 1 (`10 7B 01 7C 16`), so the meter repeats telegram 1 with its access number moving (208…214). The after-power run confirms it. That makes RawData a safe first-telegram reader |
| **F3** | Does `arrMBusLongFrame` hold each telegram whole while `FB_MBUS_General_Ext` reads? | **holds** | `General_Ext` toggles the FCB (7Bh, 5Bh, …) and reads 5 telegrams. Each sits whole at offset 0 of `arrMBusLongFrame` from its arrival until the next one, 46–81 cycles of 10 ms. A separate 10 ms task copied all five. The values match the library's decoded records up to a power of ten; the gap is the correction extensions `Tc2_MBus` ignores (§ The decoding experience) |
| **F4** | Can ST read an MQTT 5 message's expiry? | **holds** | question 5's outcome |
| **E4** | How long does a read take? | measured | SND_NKE + 1 telegram 1.27 s; 1 telegram 0.79 s (111 B); 5 telegrams 3.75 s (519 B) |

**Not covered:** a CI other than 72h, and a maximum-length frame (L = 255 is 261 bytes;
`arrMBusLongFrame` is `ARRAY[1..260]`, an inference, so a frame that long may not fit).

**A note on the bench's logs.** The standalone `logs/2026-11-10-e3.report.md` reads "F3 fails". That
log starts mid-journal, and the copy lines are delta-encoded ("the changed range of the copy",
`MBusBench/Bench/POUs/PRG_Copy.TcPOU:7`), so the analyser had no base to rebuild frames from. The
clean run is the evidence.

**What this means for an ST provider:**

- **Full reads** use `FB_MBUS_General_Ext`, with a task of ≤ 100 ms copying `arrMBusLongFrame` on
  change; a frame stays in place for at least 0.46 s.
  - `cMBus_MaxTelegrams` is raised above 5 for the Superstatic's 6 telegrams.
  - `Tc2_MBus` is pinned to 3.5.4, and E3 is re-run on every library update, because the member is
    internal.
- **First-telegram reads** use `FB_MBUS_RawData`, a documented FB.
- **Either way**, the frame length comes from L, not from `iLen`.

A side finding for the TwinCAT HAL spec: a KL6781 ran on the CX8200's own K-bus on the VION image,
which bears on VION-385.

### First telegram only — a provider's option

A provider may serve the first telegram only. This needs no contract change:

- **The block asks for what the meter sends** (`maxTelegrams: 5` for an EM5XX). The provider answers
  `Ok` with telegram 1, the readout is `IsComplete == false`, and `Value()` for a record in a later
  telegram returns `null`.
- **The block therefore publishes "not valid", never a zero.** Boot zeros are on ecocoach's
  must-not-port list (hub § 2.7).
- **A block can tell why a readout is incomplete.** Fewer telegrams than it asked for means the
  provider stopped; as many as it asked for, with more announced, means the meter has more.
- **When the provider learns to read further, the same block gets every value, unchanged.** No
  contract version, no block release.
- **The provider declares its ceiling** as a read-only service property (e.g. `MBusMaxTelegrams = 1`),
  so commissioning can see why values are missing. The SDK doesn't read it.

**The fleet says it covers most meters** (hub § 2.9, `logic-block-libraries` `97f84c3a`):

- **1 099 of 1 317 meters** read telegram 1 only in the legacy.
- **The 218 that read later telegrams** are the EM5XX, the Supercal 531, the Celsa ECMxxMM and the
  Schneider iEM3135.

**What it costs at AVM:**

- **EM5XX:** export (t3) and import T1/T2 (t5), which are configured billing points; per-phase power,
  voltage and frequency.
- **Supercal 531:** power, flow and temperatures, which are monitoring only.

The E-Mob energy manager's grid input uses only telegram-1 values. Since F3 holds, the shortcut is an
option, not a necessity: it trades the library pin for those values.

### Forward view — the deferred features, and how each arrives without breaking v1

**The rules every addition follows**, so that v1 blocks and v1 providers keep working:

1. **The wire only grows.**
   - A new operation is a new action under `/hw/mbus/`; writes take `/set`, as RTU's
     `/hw/modbus/set` does.
   - A new capability of an existing operation is a new optional request member, whose absence means
     v1 behaviour.
   - Response codes and outcomes are appended, never renamed. They travel by name (decision 0227), and
     vion-contracts' rule is that topics are append-only (hub § 2.7, vion-contracts
     `CLAUDE.md:108-133`).
2. **A v1 provider answers a newer request with a refusal, not silence** (`D17`). `NotSupported` maps
   to an `Invalid` outcome, which leaves the link state alone, because a deployment mismatch says
   nothing about the meter. A block learns at its first call that its provider is too old.
3. **The block surface only grows, by overloads and new members.**
   - Consumers never implement `IMBus`; only the SDK and its kit do, so a new member breaks no one.
   - No existing member's signature changes within a major version: an already-built plugin calling
     it would fail with `MissingMethodException`.
   - A library that uses a newer member needs the gateway's loaded `Vion.Dale.Sdk.MBus` to be at least
     that version. That is the first-loaded-wins rule for shared assemblies, which
     `Vion.Dale.Sdk.Modbus.Rtu` lives with today.
4. **The contract-type token stays `MBus`.** Only a breaking change would need a new one, and rules
   1–3 exist to avoid it.
5. **Work on the line, rather than on one meter, stays with the provider.** A scan, a line-wide baud
   change or an address sweep occupies the line far longer than one read and acts on every meter. It
   is a provider operation, driven through the provider's own service properties, never a block call.

**Each deferred feature:**

| Feature | Block API | Wire | Provider | Comes back when |
|---|---|---|---|---|
| **Writes** (SND_UD) | `IMBus.Send(primaryAddress, ciField, data, dispatcher, success?, error?, timeout?)`, shaped like RTU's writes (a write may omit its success callback, `AC-MODB-001.5`). It shares the binding's single-flight slot. Typed helpers follow as extension methods: data-set selection, set primary address, application reset | `/hw/mbus/set`: `SetMBusPayload { primaryAddress, ciField, data }` → `SetMBusResponsePayload { responseCode, errorMessage }`; `Ok` = E5h received | frames it (68h L L 68h C A CI data CS 16h). The legacy's caller-built checksum and stop byte are must-not-port (hub § 2.7) | Hager ECM381D or K'Electric KEPx is ported (`EM/…HagerECM381D.TcPOU:256-279`, `EM/…KElectricKEPx.TcPOU:232-253`), or the EM5XX's data selection is wanted from the block |
| **Secondary addressing** | a `Read(MBusSecondaryAddress address, int maxTelegrams, …)` overload; no wildcards | optional `secondaryAddress { identification, manufacturer, version, medium }` in place of `primaryAddress` | selects (SND_UD, CI 52h, to address 253, E5h) and reads as one queue item, so nothing interleaves | a site with duplicate or unset primaries; eco-prod has three duplicate-address clusters live (hub § 2.6) |
| **Scan, commissioning** | none: not a block call (rule 5) | none in the contract | a provider operation, triggered through its own service properties, results as its own read-only properties. An empty primary scan takes ≈ 2.7 min at 2400 Bd (ecocoach proposal § 11) | commissioning tooling is planned |
| **Rewind, telegram selection, per-request baud** | one `Read(…, MBusReadOptions options, …)` overload when the first of them arrives, its options as init-only properties: `Rewind` (`LinkReset` = SND_NKE, the v1 behaviour; `ApplicationReset(subcode?)`; `None`), later `Baud` | optional `rewind`, `baud`; absent = v1 behaviour | sends the rewind before REQ_UD2; switches the terminal's baud per request, as `Tc2_MBus`'s `eBaudrate` does | a meter needs other than SND_NKE (the Supercal 531's selectable telegrams would also let a first-telegram provider serve chosen values); a line mixes rates (all 84 surveyed lines run 2400 Bd) |
| **Wire records in `Vion.Contracts`** | none | none: same names and shape (`D4`) | a C# provider references them | a C# provider is built |
| **Decoding helpers for manufacturer data** | an `IMBusDataConverter` following `IModbusDataConverter` (BCD, integers, real32, date/time types, LVAR) | — | — | a block decodes a manufacturer block (DIF 0Fh) or an unknown record's bytes; Celsa is the likely first |
| **Wireless M-Bus** | a separate contract type, not `IMBus`: receive-only, push-shaped, with keys | its own | its own | a project needs it. The record layer is shared, so v1 keeps the decoder free of the wired transport |
| **A telegram builder in the kit** | `MBusTelegramBuilder` | — | — | a test can't get a capture |
| **A simulation tier** | § Simulation | — | — | its own change |

### Simulation — a path for request/response contracts (not v1)

Modbus RTU has the same gap today, so this section is written for both contracts. It decides nothing
for v1 beyond what keeps the path open. Facts are from a read of this repository at `ec62d764`.

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
  (`ScenarioWireTypeAnalyzer.cs:10-14, 119-142`).
- Pairing fans out to every block mapped to the endpoint, so it has no correlation back to the one
  block that asked.
- A stand-in never originates a message (`AC-SCEN-014.10`), but a timeout has to come from somewhere.

dale-sdk RFC 0020 § 6, "Phase 2", had already sketched a request/response face (`IModbusRtuProvider`,
with expiry on the virtual clock). The area pass that absorbed the RFC dropped it, and no spec mentions
it (`git show 75e9650b^:docs/rfcs/0020-contract-pairing.md`, lines 357–386).

**The options:**

| | Mechanism | Fidelity | Stepped | Serves both | Against |
|---|---|---|---|---|---|
| **a1** | A development-only provider face per contract (`IModbusRtuProvider`, `IMBusProvider`). A DevHost stand-in keeps the callback, a correlation map and the expiry, and routes the simulator's answer back | block-side decoding and callbacks | yes | one mechanism, one face each | re-implements the handler's rules, which can drift; the stand-in originates the timeout, against `AC-SCEN-014.10` |
| **a2** | The same faces, but the **real handler runs**. A development-only MQTT loopback actor serves the handler's registration and publishes, hands each request's payload to the paired simulator block, and returns the simulator's response record | real handler, wire records, correlation, outcomes and timeouts; only the broker is bypassed | yes; the sweep runs on the virtual clock | yes, for any handler on the service-provider base | runs production handler code in a host that isn't a runtime |
| **b** | A stand-in answering from fixtures or a new scenario step | decoding only | stepped only | partly | puts device behaviour in the host, against `scenarios.md:459-462` |
| **c** | Decision 0139's transport tier: a fake service provider on real MQTT beside a real dale | highest | no | yes | not a DevHost tier; possible today with no SDK change |
| **d** | Bridge RTU requests to a hosted Modbus TCP simulator | decoding only | yes | Modbus only | doesn't model the HAL's error codes |

**The path forward: a2, with a1 as the fallback.**

- **Why a2.** It is one mechanism for every request/response contract and stays deterministic under
  stepping. The behaviour stays in a simulator block, and every hop is an actor message
  (`AC-SCEN-014.12`). The real handler originates the timeout, as in production.
- **What a simulator author writes.** A block binding `IMBusProvider` receives `GetMBusPayload` and
  answers with `GetMBusResponsePayload`, exactly as a real provider does. For M-Bus that means serving
  captured telegrams per address, so a bench exercises the production decoder on field bytes.
- **The constraints it keeps.** The face is development-only twice over: `DevelopmentOnly = true` on
  the contract type, and `[DevelopmentOnlyHandler]` with an empty routing key on its handler
  (`AC-BIND-015.1`–`.3`, the `DigitalOutputProviderHandler.cs:18-38` precedent). There is no
  production pairing (0139).
- **What has to be solved first:**
  - `InstallationTopic` is process-wide and set once (`MqttConfiguration.cs:17-19`);
  - the real handler and a stand-in can collide on an actor name;
  - the registration acknowledgement has to be served.
- **What it decides.** It moves 0139's fidelity line, "pairing bypasses the MQTT hop", so it needs an
  amendment to decision 0139 and a change doc of its own before any code. RTU goes first: its handler
  exists, and the ABB port asked for it (ecocoach's RTU feedback, point 1).

**What v1 does to keep this open, at no cost:**

- the handler has no DevHost-specific branches;
- the wire records are all a simulator needs to speak;
- decoding runs block-side (`D10`), so any tier that delivers telegrams exercises it;
- the kit's replay is the unit-test tier until then.

### Alternatives considered

- **B — the provider decodes into a vocabulary that the contract pins as enums.** This was ecocoach's
  preference. Rejected for three reasons:
  - every provider maps the layer that cost the legacy most;
  - a vocabulary change touches every provider;
  - ST would hand-build nested JSON of up to 6 × 40 records.

  The bench adds a fourth: `Tc2_MBus` 3.5.4 ignores the correction extensions and labels a third of
  the EM530's records "Reserved", so a provider built on its decode would pass wrong values on.
- **`B′` — records with string units, like Beckhoff's `ST_MBus_Data`** (operator's point,
  2026-11-05). It keeps `Vion.Contracts` stable. It was the fallback in case the bench failed; since F3
  holds, it is kept only as the record of the path not taken.
- **Raw or records (ecocoach's § 6.4).** Two handler paths, with the vocabulary on the wire anyway.
- **One request per telegram, or a frame tunnel.** Rejected for the ecocoach proposal's reasons
  (§ 6.5): a non-portable rewind, provider state per meter, and the link layer pulled into the handler.
- **A provider-scheduled subscription** (the block declares an interval, the provider pushes
  readouts). It would fit a slow line, but it is a second contract shape for one protocol. `D3` gets
  the same fairness with RTU's shape.
- **Address and options types from v1** (an earlier draft of this doc). Dropped: overloads keep later
  additions binary-safe, and plain arguments match `IModbusClient` (`D11`).
- **A busy read reported through the error callback.** Rejected (`D3`): a typical `OnError` that marks
  values "not valid" would make every value flicker on each busy cycle of a congested line.
- **A generic request/response base now.** See `D12`.

---

## Drift checkpoints

- 2026-11-10: the ST bench (`mbus-st-bench` `aadb307`) answered F1–F4: F1 holds, F2 fails, F3 holds,
  F4 holds. `D1` no longer depends on it, and questions 1 and 5 are closed. Question 3's figures were
  re-based on the measured EM530 read. Sibling sweep: the Timeouts paragraph, the block API sketch and
  provider obligation 4, in the same commit.
- 2026-11-10: the review round and the operator's answers reshaped the design before any code:
  - `D3` refined: a busy read invokes neither callback;
  - `D6`–`D8` ratified, and the timeout moved to 300 s;
  - `D11` reverts the draft's address and options types to plain arguments, with a required telegram
    count;
  - cut: `IMBusDataConverter` (into § Forward view), `FindAll`, `QueueFull`, `InvalidRequest`, and
    partial readouts on failure;
  - added: correction-extension scaling, meaning-changing extensions reachable only by code, frame and
    identity checks, record error codes, the status bits, a logged selector miss, and kit names that
    mirror the RTU kit.

  Sibling sweep: every section and the whole spec delta, re-cut in the same commit.

---

## Spec delta (to distill)

> `MBUS` enters `owns:` in the first implementation commit (`T-001`). Until then this delta mints ids
> in an area the corpus does not yet own; `spec-change.ps1 archive` checks them only at the end.

- ADDED AC-MBUS-001.1 -> docs/specs/mbus.md : THE SYSTEM SHALL offer an M-Bus read on a service-provider contract type whose token is `MBus`, SHALL return from it immediately and SHALL deliver the result later through the `IActorDispatcher` the call was given.
- ADDED AC-MBUS-001.2 -> docs/specs/mbus.md : THE SYSTEM SHALL hand an `MBusReceipt` to both the success and the error callback of every read, carrying the wall-clock instant the response or failure was observed, the same instant on the monotonic scale, the round trip, the queued wait and the outcome.
- ADDED AC-MBUS-001.3 -> docs/specs/mbus.md : WHEN a primary address below 0 or above 250, or a telegram count below 1, is passed THE SYSTEM SHALL complete the read through its error callback with an `Invalid` receipt, without publishing it.
- ADDED AC-MBUS-001.4 -> docs/specs/mbus.md : THE SYSTEM SHALL complete every failed read through the error callback with an `MBusException` whose outcome equals the receipt's.
- ADDED AC-MBUS-001.5 -> docs/specs/mbus.md : THE SYSTEM SHALL render every number and duration in an M-Bus exception message and in a description of a telegram or readout in the invariant culture.
- ADDED AC-MBUS-002.1 -> docs/specs/mbus.md : THE SYSTEM SHALL default an M-Bus contract to disabled.
- ADDED AC-MBUS-002.2 -> docs/specs/mbus.md : WHILE an M-Bus contract is disabled THE SYSTEM SHALL execute no read and SHALL invoke neither callback.
- ADDED AC-MBUS-002.3 -> docs/specs/mbus.md : THE SYSTEM SHALL complete a read it has already accepted when the contract is disabled afterwards.
- ADDED AC-MBUS-003.1 -> docs/specs/mbus.md : WHEN a read is requested while the same contract's previous read has not completed THE SYSTEM SHALL publish nothing, SHALL invoke neither of the new read's callbacks and SHALL count it in the link summary as busy.
- ADDED AC-MBUS-003.2 -> docs/specs/mbus.md : WHEN a read completes, fails, times out, expires or is refused THE SYSTEM SHALL accept the same contract's next read.
- ADDED AC-MBUS-004.1 -> docs/specs/mbus.md : THE SYSTEM SHALL default the operation timeout to 300 seconds and the maximum queued age to 30 seconds, and SHALL accept `null` as no queued-age limit.
- ADDED AC-MBUS-004.2 -> docs/specs/mbus.md : WHEN an operation timeout or a maximum queued age, set on a contract or passed with a single read, is zero, negative, or longer than `int.MaxValue` milliseconds THE SYSTEM SHALL throw `ArgumentOutOfRangeException`.
- ADDED AC-MBUS-004.3 -> docs/specs/mbus.md : THE SYSTEM SHALL start the operation timeout when the handler publishes the read, and SHALL measure the maximum queued age over the hop from the block to the handler.
- ADDED AC-MBUS-004.4 -> docs/specs/mbus.md : WHEN a read has waited strictly longer than the maximum queued age when the handler takes it THE SYSTEM SHALL complete it through its error callback with an `Expired` receipt, without publishing it.
- ADDED AC-MBUS-004.5 -> docs/specs/mbus.md : WHEN no response to a published read has arrived when its operation timeout has passed THE SYSTEM SHALL complete it through its error callback with a `Timeout` receipt within about a second.
- ADDED AC-MBUS-004.6 -> docs/specs/mbus.md : THE SYSTEM SHALL publish each read with a deadline of the moment the handler publishes it plus the read's operation timeout.
- ADDED AC-MBUS-005.1 -> docs/specs/mbus.md : THE SYSTEM SHALL publish a read as a `GetMBusPayload` JSON document carrying the primary address and the telegram count, labelled with the record's name as its schema and with the JSON content type.
- ADDED AC-MBUS-005.2 -> docs/specs/mbus.md : WHEN a response with the code `Ok` arrives and every telegram it carries passes the frame checks THE SYSTEM SHALL decode the telegrams in order and SHALL deliver the readout to the success callback.
- ADDED AC-MBUS-005.3 -> docs/specs/mbus.md : WHEN a response with the code `NoResponse`, `Garbled`, `LineUnavailable` or `NotSupported` arrives THE SYSTEM SHALL complete the read through its error callback with the outcome `NoResponse`, `Garbled`, `LineUnavailable` or `Invalid` respectively, and SHALL carry the provider's code on the exception.
- ADDED AC-MBUS-005.4 -> docs/specs/mbus.md : WHEN a read arrives for a contract with no service-provider mapping THE SYSTEM SHALL complete it with an `Invalid` receipt.
- ADDED AC-MBUS-005.5 -> docs/specs/mbus.md : WHEN a response arrives whose correlation identifier matches no pending read THE SYSTEM SHALL ignore it.
- ADDED AC-MBUS-005.6 -> docs/specs/mbus.md : WHEN the shared pending-request limit is reached THE SYSTEM SHALL complete the arriving read with a `Dropped` receipt, without publishing it.
- ADDED AC-MBUS-005.7 -> docs/specs/mbus.md : THE SYSTEM SHALL route every M-Bus binding in a runtime through one shared handler, and SHALL mark the M-Bus assembly a shared assembly.
- ADDED AC-MBUS-006.1 -> docs/specs/mbus.md : WHEN an `Ok` response carries no telegram or more telegrams than asked, or a telegram with a wrong start, length, checksum or stop byte, a CI other than 72h, an address other than the one asked, or an identification other than the first telegram's THE SYSTEM SHALL complete the read through its error callback with a `ProtocolError` receipt and the telegrams as received.
- ADDED AC-MBUS-006.2 -> docs/specs/mbus.md : THE SYSTEM SHALL decode a telegram into its fixed header and its data records, and SHALL keep each record's data information block, value information block and data bytes as received.
- ADDED AC-MBUS-006.3 -> docs/specs/mbus.md : THE SYSTEM SHALL scale a record's value by its value information's exponent and by its multiplicative and additive correction extensions into the unit its quantity is reported in.
- ADDED AC-MBUS-006.4 -> docs/specs/mbus.md : WHEN a record's value information is not in the decoder's table THE SYSTEM SHALL keep the record with no quantity and its bytes, and SHALL decode the rest of the telegram.
- ADDED AC-MBUS-006.5 -> docs/specs/mbus.md : WHEN a record carries a record error code THE SYSTEM SHALL keep the record with no value.
- ADDED AC-MBUS-006.6 -> docs/specs/mbus.md : THE SYSTEM SHALL report a readout complete only when its last telegram does not announce further records.
- ADDED AC-MBUS-006.7 -> docs/specs/mbus.md : THE SYSTEM SHALL take a readout's header from its first telegram.
- ADDED AC-MBUS-006.8 -> docs/specs/mbus.md : THE SYSTEM SHALL report a meter error from the status byte when its application status reads 10b or 11b or its permanent or temporary error bit is set, and SHALL report none for the manufacturer-specific bits alone.
- ADDED AC-MBUS-007.1 -> docs/specs/mbus.md : THE SYSTEM SHALL select a record by quantity, storage number, tariff, subunit and function, defaulting to the instantaneous value of storage 0, tariff 0 and subunit 0 and passing over records with a meaning-changing extension, or by its exact information-block bytes, and SHALL return the first match in telegram order.
- ADDED AC-MBUS-007.2 -> docs/specs/mbus.md : WHEN no record matches a selector THE SYSTEM SHALL return no value.
- ADDED AC-MBUS-007.3 -> docs/specs/mbus.md : THE SYSTEM SHALL convert a selected value into a unit the caller names among its quantity's units, and SHALL throw an `ArgumentException` naming the selector, the record and the unit when the record cannot be expressed in it.
- ADDED AC-MBUS-007.4 -> docs/specs/mbus.md : WHEN a selector matches nothing on a complete readout THE SYSTEM SHALL log it once per selector and contract. GAP: a log call, which `../testing-conventions.md` § 15 forbids asserting on.
- ADDED AC-MBUS-007.5 -> docs/specs/mbus.md : THE SYSTEM SHALL describe a telegram or readout one line per record, each with a selector that finds that record.
- ADDED AC-MBUS-008.1 -> docs/specs/mbus.md : THE SYSTEM SHALL accumulate every completed read of one contract into a link summary readable at any time without blocking the read updating it.
- ADDED AC-MBUS-008.2 -> docs/specs/mbus.md : THE SYSTEM SHALL set the link state to `Online` on a success, to `Faulted` on no response, a garbled answer, an unavailable line, a timeout or a protocol error, and SHALL leave it unchanged on every other outcome.
- ADDED AC-MBUS-008.3 -> docs/specs/mbus.md : THE SYSTEM SHALL keep a lifetime counter for every outcome but success and for busy reads, SHALL feed round-trip figures only from reads that were published and answered, and SHALL feed queued-wait figures only from reads that reached the handler.
- ADDED AC-MBUS-008.4 -> docs/specs/mbus.md : THE SYSTEM SHALL report, for round trip and for queued wait separately, the mean, the maximum and the number of reads over at least the last 15 minutes and less than the last 16 on the contract's clock, or over its whole life where that is shorter.
- ADDED AC-MBUS-009.1 -> docs/specs/mbus.md : THE SYSTEM SHALL classify every public type the M-Bus packages ship as published surface or internal plumbing, and SHALL judge their declarations with the Dale analyzers.
- ADDED AC-TKIT-016.1 -> docs/specs/testkit.md : THE SYSTEM SHALL let a test answer a pending M-Bus read with telegrams given as hexadecimal text, delivering them through the production callback chain, frame checks and decoder and stamping the receipt from the virtual clock.
- ADDED AC-TKIT-016.2 -> docs/specs/testkit.md : THE SYSTEM SHALL let a test complete a pending M-Bus read through its error callback with an outcome the test names.
- ADDED AC-TKIT-016.3 -> docs/specs/testkit.md : THE SYSTEM SHALL let a test verify that a block sent an M-Bus read, optionally to a primary address, for a telegram count and a number of times.
- ADDED AC-TKIT-016.4 -> docs/specs/testkit.md : WHEN a simulation finds no pending M-Bus read THE SYSTEM SHALL refuse it, naming what the block must do first.
- MODIFIED AC-EMIT-002.8 -> docs/specs/emission.md : THE SYSTEM SHALL declare a `[DefaultMinInterval]` of 30 s on each diagnostics summary it ships: `ModbusLinkSummary`, `ModbusTcpConnectionSummary`, `MBusLinkSummary`, `HttpClientSummary` and `HttpServerSummary`.

---

## Tasks

> One-commit tasks, each tagged with ≥1 AC id. They live and die with this change doc.

- `T-001` (`AC-MBUS-009.1`, `AC-MBUS-005.7`): add `MBUS` to `owns:` and the roster, then scaffold the
  packages. The packages are `Vion.Dale.Sdk.MBus` and its test project, with the analyzer reference,
  the published namespace and the shared-assembly marker, plus the solution entries and the
  wiring probe.
- `T-002` (`AC-MBUS-006.2`, `006.3`, `006.4`, `006.5`, `006.7`, `006.8`): the decoder. Pin its table,
  `MBusQuantity` and `MBusUnit` against the five targets' captures: the EM530 bench capture, the public
  531, TMP-A and EM111 vectors, and AVM captures as they come. Tests are vector-driven.
- `T-003` (`AC-MBUS-006.1`, `006.6`): frame and identity checks, and completeness.
- `T-004` (`AC-MBUS-007.1`, `007.2`, `007.3`, `007.4`, `007.5`, `001.5`): the readout, selectors,
  `Value` with unit conversion, the miss log, and `Describe`.
- `T-005` (`AC-MBUS-001.1`–`001.4`, `002.*`, `003.*`, `004.*`, `005.1`–`005.6`): the contract, the
  handler, the wire records and their JSON context, and `MBusException`.
- `T-006` (`AC-MBUS-008.*`, `AC-EMIT-002.8`): the receipt, outcome, link summary and accumulator.
- `T-007` (`AC-TKIT-016.*`): `Vion.Dale.Sdk.MBus.TestKit` and its test project; "six kits" becomes
  seven in the three files that say so.
- `T-008` (`SYS-REL-001`): `examples/Vion.Examples.MBus` (the EM5XX block, replaying the bench
  capture), and both packages on the release roster.
- `T-009`: distill every delta line into `docs/specs/mbus.md`, `testkit.md` and `emission.md`, then
  `spec-change.ps1 archive mbus-service-provider`.
