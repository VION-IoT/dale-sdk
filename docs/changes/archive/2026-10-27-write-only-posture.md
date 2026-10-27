---
slug: write-only-posture
status: archived
blocked-on: none           # for parked docs: what's blocking + ref
areas: LIFE,CTRL
author: jonasbertsch
created: 2026-10-27
updated: 2026-10-27
supersedes: none           # path of a superseded change doc, or none
---

# The write-only posture, stated end to end, and kept true on the gateway and in the DevHost (VION-379)

> Change doc — one in-flight change. The `Spec delta` below is distilled into the current-truth
> pages under `docs/specs/` in the implementation PR, then this doc is archived
> (`spec-change.ps1 archive write-only-posture`). Process: `docs/spec-process.md`. Never put change
> narrative inline in a spec page — it lives here.

## At a glance

### Summary

`[ServiceProperty(WriteOnly = true)]` and `[StructField(WriteOnly = true)]` promise "the redaction
sentinel on the publish-state channel" and nothing else, and no spec page says who redacts, what
happens to an echoed sentinel, how the value is stored or whether it is logged. The first consumer to
take a password (the Easee operator integration) could not decide whether to accept one. This change
states the posture on the corpus and makes the SDK's own code keep it: two gateway log sites printed
the value, and the DevHost showed it in cleartext and stored an echoed `"***"` literally.

### Spec implications

`block-lifecycle.md` gains a section, *Write-only values*, with the end-to-end posture: who redacts
and resolves (the host runtime, cited as `dale/…`), storage at rest (plaintext, the author's opt-out),
and logging (the SDK's lines carry no value; a block's own lines are its own). It mints
`AC-LIFE-021.1` (a secret persists unredacted, tested) and two log-line criteria, `AC-LIFE-021.2` and
`AC-LIFE-021.3`, which are `GAP` under `testing-conventions.md` §15. `devhost-control.md` gains a
section of the same name and `AC-CTRL-021.1`–`.4`: the DevHost redacts what it serves outside its
process, resolves an echoed sentinel on a JSON write, and keeps cleartext in process.
`AC-CTRL-009.2` stays as written. No criterion is reworded or removed. `introspection.md`'s
`AC-INTRO-007.5` and `AC-INTRO-008.6` are read and untouched: they state the flag, which is
unchanged.

### Decisions

- `D1` **(a) ratified** — **The posture is decision 0075's and dale's; this corpus states it and
  changes nothing about it.** Redaction outbound, resolve inbound, at the host runtime
  (`dale/AC-PROP-002.1`, `dale/AC-PROP-002.2`, `dale/AC-PROP-003.1`, `dale/AC-PROP-003.2`); nothing
  encrypted at rest (`AC-LIFE-008.1`, `dale/AC-PERS-001.1`). Brief constraint 1.
- `D2` **(a) ratified** — **Wherever dale-sdk redacts or resolves, it calls `Vion.Contracts`'
  `WriteOnlyCodec`**, keyed off the property's schema. The DevHost is the only place it does either
  (`D5`). Brief constraint 2.
- `D3` **(b)** — **The gateway's two log sites drop the value rather than plumb a schema to it.**
  The restore logs `'{Key}' = {Value}`; it now logs the key. The dead-letter subscription logs the
  whole message; it now logs the message's type name, its sender and its target. Neither line needs
  a value to do its job, and dropping it is total: an opt-in `[Persistent]` property has no schema to
  redact against (`PersistentData.cs:199-233` never reads `[ServiceProperty]`), and a dead letter can
  be any message at all. Plumbing a schema would have covered only what a schema knows.
- `D4` **(b)** — **The posture lands on `block-lifecycle.md`, the DevHost half on
  `devhost-control.md`.** No area owns "secrets": the flag is `INTRO`'s, the redaction dale's, the
  persistence and the actor pipeline `LIFE`'s, and none of those has an anchor artifact a new area
  would bring. `LIFE` owns the block's persistence and the pipeline the two log sites sit on, so the
  end-to-end statement goes there and the DevHost's half on the DevHost's page.
- `D5` **(b)** — **The DevHost redacts what leaves its process and resolves what comes back; in
  process it keeps cleartext.** § The DevHost's posture. Redacted: the state routes, the
  captured-message route, the push wire and its own log lines. Resolved: a JSON write value. In
  process — a read, the observation stream, the scenario runner and the run report it writes —
  cleartext, so `AC-CTRL-009.2` stays as written and a scenario can set and assert a secret.
- `D6` **(b)** — **The log criteria are `GAP`, evidenced by an observed run** (`testing-conventions.md`
  §15), not by a test on a logger seam. The host is a scratch probe in `Vion.Dale.Sdk.Test` driving
  the real `LogicBlockBase` → `PersistentData` restore through `LifecycleHarness`, and one in
  `Vion.Dale.ProtoActor.Test` dead-lettering into the real `ActorSystem` — the same assemblies dale
  loads. The DevHost cannot host the persistence half (`MockPersistentDataHandler.cs:21-23` drops
  snapshots). The probes are not committed; their output before and after is in the PR's
  Verification.
- `D7` **(b)** — **Storage at rest gets a tested criterion, not only prose.** `AC-LIFE-021.1`: the
  snapshot a block hands its host carries a write-only value unredacted. It is the one storage fact
  this repo can prove, and it is the fact an integrator decides on.
- `D8` **(b)** — **The SmokeHost's `ShowcaseBlock` grows a write-only string and a credential
  struct with a write-only member**, the consumer's shape. It is the fixture of the Tier 1 tests and
  of the Tier 2 check (`devhost-conventions.md` §1).

### Reviewer's questions

1. **(b)** The run-report route serves cleartext. A report is the runner's in-process account of what
   it compared and watched, and the scenario file it ran already holds whatever secret it set or
   asserted. Redacting a report means redacting inside free-text step details. Is that exemption
   acceptable? OUTCOME: accepted as decided here (`D5`); the page states it. The operator may
   overturn it on the pull request.
2. **(b)** Resolve applies to every JSON write value, so a scenario's `set` step that writes `"***"`
   keeps the held secret just as an HTTP write does. A CLR value from an in-process caller passes
   through unchanged (`AC-CTRL-009.3`'s second clause). Is the line drawn at JSON-or-not right, rather
   than at HTTP-or-not? OUTCOME: accepted as decided here (`D5`); `AC-CTRL-021.2` says JSON.
3. **(b)** A measuring point is published unredacted by dale (`dale/Dale/Mqtt/Handlers/
   ServiceMeasuringPointHandler.cs:118` calls no codec), so a write-only member that is also a
   `[ServiceMeasuringPoint]`, or a measuring point whose struct has a write-only member, publishes the
   secret in cleartext. No analyzer refuses either. The page states this. The fix — an analyzer — is a
   draft item, not this change. OUTCOME: stated on `block-lifecycle.md` and in the XML docs, and
   drafted for VION-62.
4. **(b)** The HTTP package logs a request's full URL (`HttpRequestExecutor.cs:517-530`), so a
   credential in a query string or user-info reaches the log stream. The page says to carry a
   credential in a header or body, whose values it never logs. Scrubbing the URL is a draft item.
   OUTCOME: stated on `block-lifecycle.md`, and drafted for VION-62.

---

## Full design

### What was found

The sweep from the brief, re-run on `12cc4b7c`:

```
grep -rnE 'Log(Trace|Debug|Information|Warning|Error|Critical)\(' --include=*.cs . \
  | grep -v '\.Test/\|examples/\|/bin/\|/obj/\|SmokeHost\|templates/' \
  | grep -iE '\{(Value|Payload|Message|Request|Response|Values|NewValue|OldValue|State|Data|Entry|Snapshot|Content|Body|Json)\}'
```

ten hits. Widened by reading every log call in `Vion.Dale.Sdk`, `Vion.Dale.ProtoActor` and
`Vion.Dale.Plugin` (67), and every `[LoggerMessage]` template outside tests and examples
(`git grep -h -A1 'LoggerMessage(' -- '*.cs' ':!*.Test/*' ':!examples/*'`, placeholders tallied):

| Site | Carries | Disposition |
| --- | --- | --- |
| `Vion.Dale.Sdk/Persistence/PersistentData.cs:350` | a restored service property's value, a struct's every member | `D3`: key only |
| `PersistentData.cs:360` | a restored opt-in property's value, a `[ServiceProperty(WriteOnly)] [Persistent]` one included | `D3`: key only |
| `PersistentData.cs:128` | the exception of a failed restore | kept: observed to name the JSON path and type, not the value (§ Observed run) |
| `Vion.Dale.ProtoActor/ActorSystem.cs:84` | the whole dead-lettered message — a record struct's `ToString` prints `Value` | `D3`: type name |
| `Vion.Dale.ProtoActor/ActorMiddleware.cs:57`, `:73`, `:112` | message type names and headers; `:73` the handler's exception | kept: the SDK's own throws on the set path name members and types (`ServiceBinder.cs:60`, `:79`); a block setter's exception is the block's |
| `Vion.Dale.Sdk/Configuration/Services/ServiceBinder.cs:158`, `:181`, `:209`, `:231`; `Core/LogicBlockBase.cs:1146` | a getter's or codec's exception message | kept: the same |
| `[LoggerMessage]` `{Value}` in `AnalogIo` and `DigitalIo` (18) | a contract's bool or double | kept: a contract value is never a service property, and `WriteOnly` is string-only |
| `[LoggerMessage]` `{Url}`, `{HeaderKey}` in `Http` | the full request URL, a header's name | kept, stated on the page; Reviewer's question 4 |
| `[LoggerMessage]` in `Modbus.Tcp`, `Modbus.Rtu` | endpoints, unit ids, request names | kept: no service-property value |
| `Vion.Dale.DevHost/Mocking/MockServicePropertyHandler.cs:29`, `:81` | a changed value, a set's value | `D5`: redacted |
| `Vion.Dale.DevHost.Web/Services/DevHostEventBroadcaster.cs:72` | a failed broadcast's payload | `D5`: the payload is redacted before it is built |
| `Vion.Dale.DevHost/Mocking/MockServiceMeasuringPointHandler.cs:29` | a measuring point's value | kept: a measuring point is unredacted on the gateway too |

The brief's two DevHost file-write sites were checked. A topology file
(`DevTopologyLoader.cs:457`) carries instantiation-parameter values and no service-property value,
and an instantiation parameter is never write-only (`AC-GATE-011.8`, `DALE044`). A scenario file
(`ScenarioStore.cs:173`) is written byte for byte as the editor sends it. The editor's "use current
value" copies the live value into an `expect` or `waitUntil` (`components.js:2655-2662`), so before
this change the cleartext secret could land in a saved scenario that way. After it, the browser holds
the sentinel, so the sentinel is what lands.

### The gateway posture, as the page states it

Who redacts: dale, at its `ServicePropertyHandler`, through `WriteOnlyCodec`
(`dale/Dale/Mqtt/Handlers/ServicePropertyHandler.cs:188`, `:301`, `:354`). The SDK hands dale the
real value in every message, and the block's setter receives the real value, resolved by dale.

Storage: `PersistentData` captures a write-only property like any other writable one; the probe's
snapshot shows the top-level secret and the struct member in cleartext. The user-facing page
(`documentation/docs/sdk/properties.md` § Secrets) already says so, and the two agree.

Logging: after `D3`, no SDK log line on the gateway carries a value a write-only position could
hold. dale's set log redacts, and its reports name no value. A block's own log calls, and the
libraries it hands the value to, are outside what the SDK sees.

### The DevHost's posture

The operator's context: the DevHost is not a security boundary. It runs at the developer's desk, the
developer typed the secret, and it persists nothing. What it owes a block author is fidelity: the
browser should see what a dashboard would see from a gateway, and the echo round trip should behave
as it does on a gateway, so the redacted shape and the echo are met at the desk first. Today it
fails both: the SPA already expects redaction (`components.js:185-189`) and so always shows
`⚠ <cleartext>`, and the struct editor primes itself from the last published value and echoes it
back whole, which after a redaction would store `"***"` — the failure 0075 was written for.

Mechanism: one new type, `WriteOnlyRedaction` in `Vion.Dale.DevHost.Control`, a per-generation
singleton built from `DevHostIntrospection`. For a service id and a property it parses the
property's JSON schema once (`TypeSchemaSerialization.FromJsonSchema`, as `NormalizeValue` already
does) and caches whether it has any write-only position. With none, a value passes through as it is,
so nothing else on the wire changes shape. With some, it encodes the CLR value with
`PropertyValueCodec.ClrToJson` and hands it to `WriteOnlyCodec.Redact`. Callers:

- `DevHostController`'s two state routes and the captured-message route (the three
  service-property message records and the DevHost's own set wrapper are rebuilt with the value
  redacted before `ToString`);
- `DevHostEventBroadcaster.OnServicePropertyChanged`, which feeds the push wire and the replay on
  connect alike;
- `MockServicePropertyHandler`'s two value-carrying log lines.

Resolve is in `DevHostControl.NormalizeValue`, the one place a JSON write value is decoded: after the
parse, `WriteOnlyCodec.Resolve(schema, incoming, current)`, with `current` the value cache's entry
for the member, encoded the same way. The cache is fed by the block's change and its
acknowledgement, so it holds what the block last published or answered. The block's start publishes
every property, so a write always finds a held value.

Member addressing on the state routes (a bare name, or a dotted `service.member` path) is
`DevHostControl.GetProperty`'s. It moves to `DevHostIntrospection.TryResolveMember`, so the read and
the redaction resolve a name the same way (solved once).

`IDevHostControl` is unchanged. `WriteOnlyRedaction` is public because the web project reaches it
(the DevHost grants internals only to its tests), and the web host registers the generation's
instance as it does `DevHostIntrospection`. No DevHost type is `[PublicApi]`, so the manifest does
not move.

Alternatives considered. **Show cleartext and never send a sentinel**: honest for a local tool, and
the brief allows it, but the browser would then never meet the shape a gateway gives it, and the
SPA's existing redaction display would stay dead code. **Redact at the event source, before the
value cache**: one site instead of four, but it breaks `AC-CTRL-009.2` (a read after a write would
return `"***"`) and every scenario that asserts a secret. **Resolve on the HTTP route only**: the
route has no value cache or schema in hand without new surface on `IDevHostControl`, and a scenario
file echoing a sentinel is the same input (Reviewer's question 2).

### Test → mutation

| Test | Criterion | Mutation that reddens it |
| --- | --- | --- |
| `PersistentDataShould.PersistWriteOnlyValuesUnredacted` | `AC-LIFE-021.1` | redact the captured value through `WriteOnlyCodec` in `PersistentData`'s capture |
| `WriteOnlyValuesShould.RedactWriteOnlyPositionsOnStateRoutes` | `AC-CTRL-021.1` | drop the `RedactMember` calls in `DevHostController.GetState` |
| `WriteOnlyValuesShould.ServeClearedWriteOnlyValueAsNull` | `AC-CTRL-021.1` | redact a null to the sentinel |
| `WriteOnlyValuesShould.RedactWriteOnlyPositionsOnPushWire` | `AC-CTRL-021.1` | drop the `Redact` call in `DevHostEventBroadcaster` |
| `WriteOnlyValuesShould.RedactWriteOnlyPositionsOnCapturedMessageRoute` | `AC-CTRL-021.1` | drop the `RedactMessage` call in `DevHostController.GetMessages` |
| `WriteOnlyValuesShould.ServeCapturedMessageRouteAfterWrongTypedWriteToSecret` | `AC-CTRL-021.1` | let an encode failure in `Redact` propagate (the pre-fix code; observed 500) |
| `WriteOnlyValuesShould.KeepHeldSecretWhenWriteEchoesSentinel`, `…KeepHeldMemberSecretWhenStructWriteEchoesSentinel` | `AC-CTRL-021.2` | drop the `Resolve` call in `NormalizeValue` |
| `WriteOnlyValuesShould.ClearSecretWhenWriteCarriesNull` | `AC-CTRL-021.2` | resolve a null position as an echo |
| `WriteOnlyValuesShould.ReportWriteOnlyValueUnredactedInProcess` | `AC-CTRL-021.3` | redact on the value cache instead of on the routes |

The mutations are run and recorded under Drift checkpoints.

---

## Drift checkpoints

- 2026-10-27: The state route's per-block map is keyed camelCase on the wire (`apiKey`, `account`) by
  the web serializer, not by the bare member name; the tests read it that way. No design change.
- 2026-10-27: `AC-CTRL-021.1`'s null clause got a test of its own,
  `WriteOnlyValuesShould.ServeClearedWriteOnlyValueAsNull`. None of the three redaction-site mutations
  can redden it, because a null passes through either way. Its mutation is a whole-value strip that
  turns null into the sentinel (`M1b`).
- 2026-10-27: Mutations run against `c5292cdf` with `python -I mutate.py` (scratchpad), one at a time
  over `--filter FullyQualifiedName~WriteOnlyValuesShould`, file restored from git after each:
  `M1` state routes unredacted → `RedactWriteOnlyPositionsOnStateRoutes`; `M1b` null redacted →
  `ServeClearedWriteOnlyValueAsNull`; `M2` push wire unredacted → `RedactWriteOnlyPositionsOnPushWire`;
  `M3` message route unredacted → `RedactWriteOnlyPositionsOnCapturedMessageRoute`; `M4` no resolve →
  both `KeepHeld…` tests; `M5` null resolved as echo → `ClearSecretWhenWriteCarriesNull`; `M6` cache
  redacted → five, `ReportWriteOnlyValueUnredactedInProcess` among them. `AC-LIFE-021.1`: a top-level
  redaction at capture, and a struct-only one, each redden `PersistWriteOnlyValuesUnredacted`.
- 2026-10-27: The probe's snapshot shows a member carrying both `[ServiceProperty]` and
  `[Persistent]` persisted twice, under `Account.FlaggedAndPersistent` and
  `_direct.Account.FlaggedAndPersistent`. This existed before the change and is outside its scope, so it
  is a draft item. Sibling sweep: N/A.
- 2026-10-27: Review round 1 at `cd3cea5f`, blocker 1: `WriteOnlyRedaction.Redact` threw when an
  in-process caller handed a secret a value of another type, which `PropertyValueCodec.ClrToJson`
  cannot encode. The mock handler's log line then threw before the send, and every later
  `GET /api/messages` answered 500 while the captured message stayed buffered. `Redact` now fails
  closed: such a value goes out as the sentinel, whole. Test:
  `WriteOnlyValuesShould.ServeCapturedMessageRouteAfterWrongTypedWriteToSecret`, observed red (500)
  before the fix. Sibling sweep: `Resolve` encodes only the cache's value, which the block itself
  published, so it cannot be wrong-typed. Done.
- 2026-10-27: The DevHost logs a redacted struct as the `JsonObject` it is
  (`[userName, operator], [password, ***]`) rather than as the record's `ToString`. It is cosmetic and
  accepted.

---

## Spec delta (to distill)

- ADDED AC-LIFE-021.1 -> docs/specs/block-lifecycle.md : THE SYSTEM SHALL persist a write-only service property, and a struct-typed one with write-only members, as it persists any writable service property, capturing the value the block holds unredacted.
- ADDED AC-LIFE-021.2 -> docs/specs/block-lifecycle.md : THE SYSTEM SHALL log a restored persisted entry by its key alone, naming no value. GAP: log text, which no test asserts on (testing-conventions.md §15); evidenced by a run observed at Trace and recorded under the pull request's Verification (VION-379).
- ADDED AC-LIFE-021.3 -> docs/specs/block-lifecycle.md : THE SYSTEM SHALL log a dead-lettered message by its type name, its sender and its target, naming none of its values. GAP: log text, which no test asserts on (testing-conventions.md §15); evidenced by a run observed at Trace and recorded under the pull request's Verification (VION-379).
- ADDED AC-CTRL-021.1 -> docs/specs/devhost-control.md : THE SYSTEM SHALL serve a service property's value on the state routes, the captured-message route and the push wire with each write-only position replaced by the redaction sentinel and a null one left null.
- ADDED AC-CTRL-021.2 -> docs/specs/devhost-control.md : WHEN a JSON write value holds the redaction sentinel at a write-only position THE SYSTEM SHALL write in its place the value last published or acknowledged for that property, leaving a null position null.
- ADDED AC-CTRL-021.3 -> docs/specs/devhost-control.md : THE SYSTEM SHALL report a write-only value unredacted in process, on a read and on the observation stream.
- ADDED AC-CTRL-021.4 -> docs/specs/devhost-control.md : THE SYSTEM SHALL write a service property's value into its own log lines with each write-only position redacted. GAP: log text, which no test asserts on (testing-conventions.md §15); evidenced by a run observed at Information and recorded under the pull request's Verification (VION-379).

---

## Tasks

- `T-001` (`AC-LIFE-021.2`, `AC-LIFE-021.3`): drop the value from the restore log and the
  dead-letter log.
- `T-002` (`AC-LIFE-021.1`): the persisted-secret test and its fixture.
- `T-003` (`AC-CTRL-021.1`, `AC-CTRL-021.3`, `AC-CTRL-021.4`): `WriteOnlyRedaction`, wired into the
  state routes, the captured-message route, the push wire and the mock handler's log lines; the
  SmokeHost's secret members.
- `T-004` (`AC-CTRL-021.2`): resolve an echoed sentinel in a JSON write value.
- `T-005`: distill into `block-lifecycle.md` and `devhost-control.md`; rewrite the `WriteOnly` XML
  docs and the two examples' comments to what the pages state.
- `T-006`: archive.
