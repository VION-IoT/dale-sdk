---
slug: hw-contracts-are-json
status: archived
blocked-on: none
areas: IO, BIND, MODB
author: Fabien Graf
created: 2026-09-14
updated: 2026-09-15
supersedes: none
---

# The `hw/*` contracts become JSON

> Change doc — one in-flight change. The `Spec delta` below is distilled into the current-truth
> pages under `docs/specs/` in the implementation PR, then this doc is archived
> (`pwsh scripts/spec-change.ps1 archive <slug>`). Process: `docs/spec-process.md`. Never put
> change narrative inline in a spec page — it lives here.

## At a glance

### Summary

The five service-provider handlers that speak `hw/*` — digital in/out, analog in/out, Modbus RTU —
stop encoding FlatBuffers and encode JSON, through the source-generation context `Vion.Contracts`
ships for the purpose. It is what lets a NativeAOT hardware-abstraction layer and dale share one
serialization path instead of two that can drift, and what lets a HAL without a FlatBuffer toolchain
exist at all. A clean cut: no dual format, no sniffing, no fallback.

### Spec implications

`docs/specs/io.md` carries most of it: the decode prose under `AC-IO-005.2` (what a JSON decode
refuses; that the handler catches rather than letting the throw escape is `D6`'s and changes nothing a
block observes, so the page does not state it), `AC-IO-006.2`'s content
type, `AC-IO-005.3`'s and `AC-IO-005.5`'s prose (the payload no longer carries identity strings, and
the label is judged before the decode rather than before the verifier), `AC-IO-007.2` narrowed to
finite values and `AC-IO-007.3` refusing a non-finite command, the mirror's list of legitimate
differences (the two buffer-size literals are gone, the non-finite refusal joins) and the
test-discipline paragraph. `docs/specs/testkit.md`'s prose under `AC-TKIT-007.2` stops citing the value
rule for non-finite values, which a block can still set; the kit's rule is unchanged.

`docs/specs/contracts.md` carries `AC-BIND-011.3`: a publish declares the content type its caller
names, and nothing defaults it.

`docs/specs/modbus.md` gains `AC-MODB-015.10`: the RTU handler publishes each request as its payload
record's JSON document, labelled with the schema name and the JSON content type. The page was silent on
the encoding before, so nothing pinned the outbound request at all.

### Decisions

- `D1` — **Clean cut, no bridge.** No dual format, no content-type sniffing, no `[Obsolete]`
  FlatBuffers fallback. Rests on the author-sourced claim that `hw/*` FlatBuffers have never been in
  productive use; unverifiable from any repository, and the protection is coordinated majors plus
  profile pinning.
- `D2` — **The content type is a required argument, with no default anywhere.** Every `hw/*` publish
  goes through `PublishJson`, which names `MessageMimeTypes.Json` at the one site serving all five
  handlers. `ServiceProviderHandlerBase.Publish` takes a non-null `contentType`, and
  `PublishMqttMessage` / `PublishMqttMessageRequest` take `Payload` and `ContentType` without
  defaults — `Payload` loses its default because C# puts every optional parameter after the required
  ones — with `ContentType` still nullable, so a message with no body passes `null`. A default of
  either value relabels whatever a caller that omits it publishes: dale's `Remote`/`Func` publish
  (`RemoteFunctionInterfaceProxyHandler.cs:102` at dale `a846619`) sends FlatBuffer bytes through the
  record without naming a content type. Operator decision 2026-09-15, in review, replacing the
  flipped default the first round took.
- `D3` — **`MessageMimeTypes.FlatBuffer` and `Google.FlatBuffers` are retained.** dale↔dale
  `Remote`/`Func` still uses them. `GetFlatBufferPayload` stays on both message types as published
  surface with no in-repo caller left — a third-party provider-face author may still decode one, and
  nothing in this change's scope authorises removing it.
- `D4` — **Typed overloads are added beside the reflection-based ones, never replacing them.**
  `PublishJson<T>(…, JsonTypeInfo<T>, …)` and `GetJsonPayload<T>(JsonTypeInfo<T>)` on both
  `MqttMessageReceived` and `ServiceProviderMqttMessage`. The existing overloads are public surface
  and keep working.
- `D5` — **`JsonSerialization.DefaultOptions` keeps the non-generic `JsonStringEnumConverter`,** with
  a comment saying why. These options are a catch-all over whatever payload type a handler hands the
  reflection overloads, so no concrete enum type is known where they are declared; the generic
  converter would need one registration per enum, a list that goes stale the first time a handler
  declares an enum nobody added. The `hw/*` payloads do not come through these options at all — each
  of their enums pins its own string form with a type attribute, and the handlers go through
  `HwJsonContext`.
- `D6` — **The decode refuses rather than throws.** `GetJsonPayload` throws `JsonException` on an
  empty, truncated or wrong-typed document and `InvalidOperationException` on a bare `null` one. The
  four I/O handlers catch both and drop, preserving the debug-level refusal the FlatBuffers verifier
  gave. The Modbus RTU handler already classified a decode failure as `ProtocolError` and needed no
  new arm.
- `D7` — **Tests arrange from literal JSON.** An arrangement that serialized through the same context
  the handler decodes with would inherit the handler's own naming policy, and would pass a handler
  reading `Value` off a wire carrying `value` — the one thing these arrangements exist to catch.
  Outbound assertions are the exact document, not a round trip.

### Reviewer's questions

1. **`AC-IO-007.2` cannot be met on a JSON wire, and this is the change's one open point.** JSON has
   no literal for a non-finite `double`. `JsonSerializer` refuses to write `NaN` or either infinity
   (`ArgumentException`, not `JsonException`), and `{"value":NaN}` is not readable. `HwJsonContext` in
   `Vion.Contracts` 11.0.0 sets no `JsonNumberHandling`. A dale-sdk-local workaround — copying the
   context's options and adding `NumberHandling` — would make dale write documents the AOT HALs
   cannot read, which is worse than the break.
   **OUTCOME: a non-finite value does not cross; `AC-IO-007.2` narrows to finite values and
   `AC-IO-007.3` refuses a non-finite command.** Operator decision 2026-09-15, in review, with the
   author. It reverses the 2026-09-14 outcome, which kept the criterion whole by allowing the named
   floating-point literals — `Vion.Contracts` 11.0.1 on `HwJsonContext`, the same option on
   `JsonSerialization.DefaultOptions` — and so put a non-finite value on the wire as a quoted string
   in a field that is otherwise a number. The option is taken out of `DefaultOptions` here and out of
   `HwJsonContext` in a `Vion.Contracts` release this change pins. Outbound, the analog output handler
   drops a non-finite command with a warning rather than letting the serializer throw into the actor
   middleware (the operator chose that over the throw and over a refusal inside the block's own face).
   Inbound, a quoted literal is a value of the wrong type and `AC-IO-005.2` refuses it.
2. **Is the `JsonTypeInfo<T>` overload worth a criterion?** No, and deliberately. The wire it produces
   for the `hw/*` payloads is byte-identical to what the reflection overload produces — camelCase
   either way, the enums pinned by their own type attribute either way, `data` by the record's own
   converter either way. A mutation that made the overload ignore its type metadata and fall back to
   `DefaultOptions` reddens nothing, so the rule is not observable and minting it would mint a
   criterion no mutation can reach (`spec-process.md` § Implement). It is stated in `contracts.md`'s
   prose under `AC-BIND-011.4` instead.
   **OUTCOME: first accepted with no criterion; reversed 2026-09-15 in review — `AC-BIND-011.5` and
   `AC-BIND-012.8` minted.** The argument held only for the `hw/*` records, whose naming matches the
   shared options. Both overloads are public surface a handler author calls with metadata of their own,
   and a test context with a snake-case naming policy makes the fallback mutation observable on the
   publish and on the read alike.
3. **Does `AC-IO-005.2` still say the right thing?** Its EARS text is encoding-agnostic — "a payload
   that is not one the schema accepts" — and stays. What changed is its *reach*: JSON refuses a value
   of the wrong type, which FlatBuffers accepted whenever the layouts agreed. The prose said that gap
   needed the schema label to close; the label check (`AC-IO-005.5`) closes it first, and the decode
   now closes it again behind. Neither is redundant — only the label can refuse a *sibling* payload of
   identical shape.
   **OUTCOME: text unchanged, prose rewritten, no delta line.**
4. **Is `GetFlatBufferPayload` now dead surface?** In this repository, yes — `git grep -n
   "GetFlatBufferPayload"` returns only its two declarations after this change. `D3` keeps it.
   **OUTCOME: kept, and named here so the next reader does not rediscover it as an accident.**

---

## Full design

### What moved, and what did not

| Suffix | Direction | Payload | Document |
|---|---|---|---|
| `/hw/di/state` | HAL → dale | `DiStatePayload` | `{"value":true}` |
| `/hw/do/set` | dale → HAL | `SetDoPayload` | `{"value":true}` |
| `/hw/do/state` | HAL → dale | `DoStatePayload` | `{"value":true}` |
| `/hw/ai/state` | HAL → dale | `AiStatePayload` | `{"value":21.4}` |
| `/hw/ao/set` | dale → HAL | `SetAoPayload` | `{"value":42.5}` |
| `/hw/ao/state` | HAL → dale | `AoStatePayload` | `{"value":42.5}` |
| `/hw/modbus/get` | dale → HAL | `GetModbusPayload` | `{"functionCode":"ReadHoldingRegisters","unitIdentifier":3,"startingAddress":40001,"quantity":2}` |
| ↳ response | HAL → dale | `GetModbusResponsePayload` | `{"responseCode":"Ok","errorMessage":null,"data":"AAEAAg=="}` |
| `/hw/modbus/set` | dale → HAL | `SetModbusPayload` | `{"functionCode":"WriteMultipleRegisters","unitIdentifier":3,"address":40001,"data":"AAEAAg=="}` |
| ↳ response | HAL → dale | `SetModbusResponsePayload` | `{"responseCode":"Ok","errorMessage":null}` |

Unchanged: every topic string, QoS, retain, correlation data, the response-topic convention, and the
`schema` user property (still `nameof(<Record>)`). Unchanged for a block author: `IContractMessage`
and the four typed actor messages keep their shapes, so nothing a library author writes moves.

The identity fields the FlatBuffer state payloads carried — `hardware_block_instance_id` and
`endpoint_identifier` — are gone from the records and are not ported. Both were already in the topic,
and `AC-IO-005.3` already said this area reads the topic and never them.

### The enum namespace move is API-visible

`ModbusFunctionCode` moves from `Vion.Contracts.FlatBuffers.Hw.Modbus` to `Vion.Contracts.Hw.Modbus`.
It appears in *signatures* across `Vion.Dale.Sdk.Modbus.Rtu` — `ModbusRtu.cs`, `ActorMessages.cs`,
`IModbusRtuRequestFactory.cs`, `ModbusRtuRequestFactory.cs` — so the move is visible to anything
compiling against that package, not an internal detail. The member set is identical and no member is
renumbered, so the change is the namespace in a `using`.

`Vion.Dale.Sdk.Modbus.Tcp`'s `ModbusTcpClientProxy` re-aliases to the new namespace for the same
reason. Its sibling `ModbusTcpServerProxy` is **not** touched: it resolves `ModbusFunctionCode` from
FluentModbus (`using FluentModbus;` at `:7`, no alias), and the tell is
`ModbusFunctionCode.ReadWriteMultipleRegisters` at `:187` — a member `Vion.Contracts` has never had.
Two same-named enums; only the client proxy's is ours.

### Why the bump could not be its own green commit

`Vion.Contracts` 11.0.0 **removes** `Vion.Contracts.FlatBuffers.Hw` in the same major that adds
`Vion.Contracts.Hw`. The bump and the recode are therefore one atomic change. The bump is still its
own commit, for diagnosis rather than for bisect, and its message carries what the seven released
majors actually cost: 88 errors (20 `CS0234`, 68 `CS0246`) across exactly ten production files, every
one of them naming the removed namespace, and **no other namespace dale-sdk consumes broke at all**.
That is the answer to what the spec called "the largest unknown in Phase 0's estimate".

### Behavior table

Scope: the five handlers, the shared publish/read paths, and the two message types that carry a
content type. Rows are the observable behaviours the cut changes or newly reaches.

| # | Behavior (EARS) | Evidence | Test today | Rec | Why |
|---|---|---|---|---|---|
| 1 | WHEN a block sets an output THE SYSTEM SHALL publish a JSON document carrying the commanded value alone. | `DigitalOutputHandler.cs:88`, `AnalogOutputHandler.cs:88` | `PublishCommandPayloadEncodingValueAlone` | intended | the wire the HALs read |
| 2 | THE SYSTEM SHALL declare a command's content type as JSON. | `ServiceProviderHandlerBase.cs:235` | `PublishCommandLabelledWithPayloadSchemaAndContentType` | intended | `AC-IO-006.2` |
| 3 | THE SYSTEM SHALL declare a published message's content type as the one its caller names. | `ServiceProviderHandlerBase.cs:190`, `Mqtt/ActorMessages.cs:92,124` | `ProviderPublishShould.DeclareContentTypeCallerNames` | intended | `AC-BIND-011.3`, the default removed |
| 4 | WHEN a state document cannot be decoded THE SYSTEM SHALL drop it, delivering nothing to any block. | the four handlers' `catch` arms | `ForwardNothingWhenPayloadUndecodable` | intended | `AC-IO-005.2`, same rule, wider reach |
| 5 | WHEN a state document's value is of a type the member does not hold THE SYSTEM SHALL drop it. | same | `ForwardNothingWhenPayloadUndecodable`'s wrong-type rows, the analog suites' three named-literal rows included | intended | newly reachable; FlatBuffers accepted it |
| 6 | WHEN a state document carries no value member THE SYSTEM SHALL deliver the member's default. | `System.Text.Json` parameterized-constructor binding | none — stated in `io.md` prose | out-of-spec | unchanged from FlatBuffers' absent-field default; the wire cannot distinguish it from a publisher that meant the default |
| 7 | THE SYSTEM SHALL carry a signed zero to the far side. | probe: `{"value":-0}` round-trips | none | out-of-spec | an improvement that falls out of the encoding; `AC-IO-007.2` already covers "unaltered" |
| 8 | WHEN a block commands an analog output with a value that is not finite THE SYSTEM SHALL publish no command. | `AnalogOutputHandler.cs:91` | `AnalogOutputHandlerShould.PublishNothingWhenCommandValueNotFinite` | intended | `AC-IO-007.3`; JSON has no number for it |
| 9 | THE SYSTEM SHALL serialize a publish, and read a payload, through caller-supplied type metadata when given it. | `ServiceProviderHandlerBase.cs:243-251`, `MqttMessageExtensions.cs:57-67`, `ServiceProviderMqttMessage.cs:105-108` | `ProviderPublishShould.SerializeJsonPayloadThroughSuppliedTypeMetadata`, `ServiceProviderMqttMessageShould.ReadJsonPayloadThroughSuppliedTypeMetadata` | intended | reachable with metadata whose policy differs from the shared options; see Reviewer's question 2 |
| 10 | THE SYSTEM SHALL name the Modbus function and response codes by their member names on the wire. | `ModbusFunctionCode.cs`'s own `[JsonConverter]` | `ModbusRtuHandlerShould.PublishRequestAsLabelledJsonDocument` for the function code; the literal-JSON response arrangements for the response code | intended | the record's contract, carried not specified here |
| 11 | THE SYSTEM SHALL publish each Modbus RTU request as its payload record's JSON document, labelled with its schema name and the JSON content type. | `ModbusRtuHandler.cs:223-229`, `:388-394` | `ModbusRtuHandlerShould.PublishRequestAsLabelledJsonDocument` | intended | nothing asserted the request wire in either encoding |

**Row 8, and what it costs.** Under FlatBuffers a block could drive an analog output to `NaN` — a
plausible "I have no value" idiom — and a HAL could report `NaN` for a reading it cannot take. On the
JSON wire neither crosses. A block that needs to say "no value" says it some other way, and a HAL that
cannot take a reading does not publish one; the digital family has no counterpart, since a truth value
is always finite.

### Consolidation map

| Row | Criterion |
|---|---|
| 1, 2 | `AC-IO-006.2` (MODIFIED — content type) |
| 3 | `AC-BIND-011.3` (MODIFIED — no default) |
| 4, 5 | `AC-IO-005.2` (text unchanged; prose rewritten — the reach widened, the rule did not) |
| 6, 7 | no criterion — stated in `io.md`'s `AC-IO-007.2` prose; neither is a rule this area declares |
| 8 | `AC-IO-007.3` (ADDED); `AC-IO-007.2` (MODIFIED — finite values only) |
| 9 | `AC-BIND-011.5`, `AC-BIND-012.8` (ADDED) |
| 10 | no criterion — `Vion.Contracts` owns the record's wire form; `io.md` cites rather than restates |
| 11 | `AC-MODB-015.10` (ADDED) |

### Unmapped tests

None. Every test in the four rewritten suites maps to a row or to a criterion the cut does not touch
(registration, mapping, topic caching, correlation identifiers).

---

## Drift checkpoints

- `2026-09-14`: The brief required commit 1 to be the dependency bump alone, building and testing
  green before any handler edit. Impossible: `Vion.Contracts` 11.0.0 removes
  `Vion.Contracts.FlatBuffers.Hw` in the same major that adds `Vion.Contracts.Hw`, so the bump alone
  does not compile. Commit 1 is still the bump alone, as a diagnosis of what the majors cost, and its
  message says it does not build and why. Sibling sweep: N/A — one dependency, one pin.
- `2026-09-14`: The brief listed six comment sites as describing the SP wire as FlatBuffers. Two of
  them — `Configuration/Services/ServiceBindingInfo.cs:10` and `Messages/ActorMessages.cs:35` —
  describe service *property* and measuring-point state, which `architecture/concepts/wire-formats.md:57`
  records as JSON since decision `0038`. Those two were already false before this change and are
  corrected as drift, not as part of the cut. Two others
  (`ServiceProviderMqttMessage.cs:96`, `MqttMessageExtensions.cs:46`) describe `GetFlatBufferPayload`
  itself, are true, and are left alone. Sibling sweep: done —
  `git grep -n FlatBuffer -- 'Vion.Dale.Sdk/'` re-read line by line. (Widened 2026-09-15: the sweep
  stopped at `Vion.Dale.Sdk/` and missed two DevHost comments contrasting a scenario value with "a
  FlatBuffer frame" — `Mocking/ServiceProviderContractHandler.cs:113-114` and
  `Scenarios/ScenarioWireCodec.cs:19` — both now fixed; a whole-tree `git grep -n FlatBuffer` leaves
  only `GetFlatBufferPayload`, its content type, the plugin-loading tests and `CLAUDE.md`'s description
  of `Vion.Contracts`, all true.)
- `2026-09-14`: `AC-IO-007.2` cannot be met on a JSON wire. Recorded as Reviewer's question 1; the
  criterion is not reworded and the nine rows proving it stand red. Sibling sweep: done — the digital
  and Modbus families carry no floating-point value, so the break is analog-only, confirmed by the
  suite (`Vion.Dale.Sdk.DigitalIo.Test` 101 passed, `Vion.Dale.Sdk.Modbus.Rtu.Test` green).
- `2026-09-14`: `HandlerHarness.Truncated` became unused once the refusal rows moved from byte
  lengths to literal documents, and was deleted in both harnesses rather than left behind
  (`sdk-surface-conventions.md` § 3). Verified with `git grep -n "Truncated(" -- '*.Test'` → no hits.
- `2026-09-14`: `AC-IO-007.2` resolved on `Vion.Contracts` 11.0.1 rather than by rewording. The wire
  form turned out to be a **quoted** named literal, which no part of the design had stated: both
  harnesses were arranging non-finite documents by formatting the `double`, which under the
  invariant culture renders bare `NaN`, `Infinity` and `-Infinity` — not JSON unquoted — so three
  inbound rows still failed after the bump for a defect in the arrangement rather than in the handler.
  Sibling sweep: done — both harnesses fixed together, and `JsonSerializationShould` pins the spelling
  so neither can drift back. (Corrected 2026-09-15: this entry first said the formatting rendered the
  Unicode infinity sign, which only a culture such as en-US does.)
- `2026-09-14`: The amendment's item 2 asked for a regenerated `publicapi-manifest.json` whose diff
  shows the three added members, gated by `scripts/check.ps1`. Neither premise holds: the manifest
  has exactly two keys, `assemblies` and `types` (148 of them), so it records no members at all and
  cannot show an added overload; this change declares no new `[PublicApi]` type, so the manifest is
  provably unchanged; and `check.ps1` has no manifest step — CI's snapshot bot regenerates it onto
  the pull request head. `node` is additionally not installed on this machine, so the generator could
  not have been run locally either way. Sibling sweep: N/A — one manifest, one generator.
- `2026-09-15`: Review round, taken over by a second session. The operator reversed Reviewer's
  question 1 — a non-finite analog value does not cross — so the named-literal option leaves
  `JsonSerialization.DefaultOptions` and `JsonSerializationShould` is deleted with it: every one of its
  rows existed to pin the two paths agreeing on a non-finite value, and the finite row pins nothing a
  handler test does not. The harnesses' non-finite branches go too, since no row arranges such a
  document through them any more. Sibling sweep: done — `git grep -n "AllowNamedFloatingPointLiterals\|NaN\|Infinity"`
  over `Vion.Dale.Sdk*` and `docs/specs/` re-read; `testkit.md`'s `AC-TKIT-007.2` prose was the one
  page outside `io.md` citing the value rule for non-finite values.
- `2026-09-15`: `Vion.Contracts` 11.0.2 (PR #26, `3674f55`) takes the named literals out of
  `HwJsonContext`; this repo pins it. The analog suites' three named-literal rows under
  `ForwardNothingWhenPayloadUndecodable` were written first and run on 11.0.1, where all six failed
  (`Assert.IsEmpty failed … Actual: 1`), then passed on 11.0.2 unedited — the defect proof for the pin.
  `service-provider-sdk-dotnet` 10.1.0, `hal-sim` 5.0.0 and `hal-raspberry` 5.0.0 are released on
  11.0.1 and would still write a quoted literal for a non-finite reading; this side refuses it as an
  undecodable document, and moving them is the cross-repo spec's to schedule. Sibling sweep: done — one
  pin (`git grep -n 'Vion.Contracts"' -- '*.csproj' '*.props' '*.targets'`, one hit).

---

## Relay notes for the PR body

Consumer-visible in `v0.14.0`, the next breaking minor above `v0.13.0`:

- **The `hw/*` wire is JSON.** The five service-provider handlers publish and parse
  `Vion.Contracts.Hw` records through `HwJsonContext` instead of FlatBuffers. Topics, QoS, retain,
  correlation data and the `schema` user property are unchanged; no logic-block API moves, so a block
  author's source needs no edit.
- **A non-finite analog value does not cross the wire.** A block commanding `NaN` or an infinity on an
  analog output publishes nothing, and the handler logs a warning naming the contract; a state document
  spelling one as `"NaN"`, `"Infinity"` or `"-Infinity"` is refused like any other undecodable
  document. Every analog value on this wire is a JSON number.
- **`Vion.Contracts` floor is 11.0.2.** A consumer pinning `Vion.Contracts` directly below that gets
  a downgrade at restore, because a direct pin wins by nearest-wins.
- **`ModbusFunctionCode` moved namespace** to `Vion.Contracts.Hw.Modbus`. It appears in
  `Vion.Dale.Sdk.Modbus.Rtu` *signatures*, so anything compiling against that package changes a
  `using`. No member renamed, none renumbered.
- **The MQTT content type is a required argument** on `ServiceProviderHandlerBase.Publish`,
  `PublishMqttMessage` and `PublishMqttMessageRequest`, and `Payload` is required on the two records.
  Code that relied on the old FlatBuffer default stops compiling rather than changing its label; it
  names the content type it publishes, or `null` for a message with no body. dale's own publishes
  through the records are such code.
- **A logic-block library must be rebuilt against `Vion.Dale.Sdk.DigitalIo`, `.AnalogIo` and
  `.Modbus.Rtu` 0.14.0** before a JSON HAL is deployed where it runs: those packages reach a gateway
  as `[DaleSharedAssembly]` plugins pinned by the library, not by dale, so a stale library keeps the
  FlatBuffer codec and the mismatch is silent.

---

## Spec delta (to distill)

- ADDED AC-MODB-015.10 -> docs/specs/modbus.md : THE SYSTEM SHALL publish each request as its payload record's JSON document, labelled with that record's schema name and the JSON content type.
- MODIFIED AC-IO-007.2 -> docs/specs/io.md : THE SYSTEM SHALL carry any finite value its type can hold unaltered in both directions, rejecting and clamping none of them.
- ADDED AC-IO-007.3 -> docs/specs/io.md : WHEN a block commands an analog output with a value that is not finite THE SYSTEM SHALL publish no command.
- MODIFIED AC-IO-006.2 -> docs/specs/io.md : THE SYSTEM SHALL publish each command under a correlation identifier of its own, labelled with its payload type's schema name and the JSON content type, not retained, and carrying that payload type's encoding of the commanded value and nothing else.
- ADDED AC-BIND-011.5 -> docs/specs/contracts.md : THE SYSTEM SHALL serialize a JSON publish through the type metadata its caller supplies, in place of the shared options.
- ADDED AC-BIND-012.8 -> docs/specs/contracts.md : THE SYSTEM SHALL read a JSON payload through the type metadata its caller supplies, in place of the shared options.
- MODIFIED AC-BIND-011.3 -> docs/specs/contracts.md : THE SYSTEM SHALL declare a published message's content type as the one its caller names.

---

## Tasks

- `T-001` (`AC-BIND-011.3`): bump `Vion.Contracts` to 11.0.0, alone, recording what the majors cost.
- `T-002` (`AC-IO-006.2`, `AC-BIND-011.3`): cut the five handlers and the shared publish/read paths
  to JSON, add the `JsonTypeInfo<T>` overloads, flip the three defaults, sweep the comment drift, and
  rewrite the four I/O suites and the Modbus RTU arrangements.
- `T-003` (`AC-IO-006.2`, `AC-BIND-011.3`): distil the delta into `io.md` and `contracts.md`.
- `T-004` (`AC-IO-007.2`): bump to `Vion.Contracts` 11.0.1, take the same `NumberHandling` on
  `JsonSerialization.DefaultOptions`, and pin that the two serialization paths agree on a non-finite
  value in both directions. Reverted by `T-005`.
- `T-005` (`AC-IO-007.2`, `AC-IO-007.3`): take the named-literal option out of `DefaultOptions`, drop
  a non-finite analog command in the handler, narrow the value rule to finite values, and pin the
  `Vion.Contracts` release that takes the option out of `HwJsonContext`.
