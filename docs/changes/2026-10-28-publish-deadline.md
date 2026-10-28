---
slug: publish-deadline
status: proposed           # proposed | in-flight | parked | archived
blocked-on: none           # for parked docs: what's blocking + ref
areas: HOST,BIND,MODB      # area codes from docs/specs/_corpus.yml's `owns:`
author: jonasbertsch
created: 2026-10-28
updated: 2026-10-28
supersedes: none           # path of a superseded change doc, or none
---

# A publish carries its own deadline, and each Modbus RTU request carries its own (VION-376)

> Change doc — one in-flight change. The `Spec delta` below is distilled into the current-truth
> pages under `docs/specs/` in the implementation PR, then this doc is archived
> (`spec-change.ps1 archive publish-deadline`). Process: `docs/spec-process.md`. Never put change
> narrative inline in a spec page — it lives here.

## At a glance

### Summary

A Modbus RTU request reaches the HAL with no deadline, so a HAL serves it long after the block has
stopped waiting: on the desk Neuron a hung slave filled the HAL's queue and starved the block's later
reads. The handler already knows each request's deadline and keeps it to itself. This change gives
both publish message records an optional absolute deadline, gives the handler base's publish helpers
overloads that set it, and has the RTU handler set each read's and write's own. A publish without one
is unchanged. dale (step 2) turns the deadline into the MQTT 5 Message Expiry Interval.

### Spec implications

`host-vocabulary.md` § Publishing mints three criteria. `AC-HOST-003.4` is the vocabulary: both
publish forms carry an optional deadline, and their conversion into each other keeps it — tested
here. `AC-HOST-003.5` (the expiry interval each attempt gets) and `AC-HOST-003.6` (an attempt past
the deadline is dropped) are what a host does with it, `GAP` until dale's criterion exists, marked the
way `AC-HOST-003.1` marks its proof. `contracts.md` § Publishing from a provider face mints
`AC-BIND-011.6`: a deadline passed to a publish helper reaches the published message, and none is
set where none is passed. `modbus.md` § Modbus RTU mints `AC-MODB-015.11`: each read and write is
published with the instant the handler itself expires it. `AC-MODB-003.1`, `AC-MODB-003.3`,
`AC-MODB-015.3`, `AC-HOST-003.1` and `AC-BIND-016.1` are read and stay as written. No criterion is
reworded or removed.

### Decisions

- `D1` **(a) ratified** — **The carrier is the MQTT 5 Message Expiry Interval, and the wire is
  dale's.** Whole seconds left, rounded up, set at each attempt, nothing sent past the deadline.
  Brief, *Decisions already taken*. This repo carries the deadline to dale and no further.
- `D2` **(a) ratified** — **Opt-in and generic: offered where every handler publishes.** Brief,
  *Decisions already taken*; decision 0014 (integrators write their own handlers). RTU is the only
  user this round.
- `D3` **(a) ratified** — **The deadline is an absolute instant inside the process**, because dale
  computes the time left at each attempt and its retry re-sends the same message. Brief, *Decisions
  already taken*.
- `D4` **(c) proposed** — **The shape: an `init` property `DateTimeOffset? Deadline` on both
  `PublishMqttMessage` and `PublishMqttMessageRequest`, outside their positional parameter lists;
  `ToRequest` and `ToMessage` copy it.** § The shape. `null` means none, the default.
- `D5` **(c) proposed** — **Three new helper overloads on `ServiceProviderHandlerBase`, one per
  existing helper, each taking a required `DateTimeOffset deadline` right after the existing required
  parameters; the existing three keep their signatures and publish with no deadline.** § The shape.
- `D6` **(b)** — **The RTU handler's deadline is the pending entry's own `expiresAt`, computed before
  the publish rather than after it.** `publishedAt` is already read before the publish
  (`ModbusRtuHandler.cs:219`, `:386`); only the `expiresAt` line moves up (`:240`, `:405`). The
  published deadline and the handler's own expiry sweep therefore name one instant, so a request the
  HAL drops for expiry is one the block has already been told timed out.
- `D7` **(b)** — **The host criteria are stated over both publish forms.** dale's acknowledged path
  builds its MQTT message from `request.ToMessage()` (`../dale/Dale/Mqtt/PublishQueue.cs:91`, `:96`),
  so a deadline on the request form reaches the same builder as the fire-and-forget one. Step 2's
  brief has to cover both; § Affects dale.
- `D8` **(b)** — **The helper criterion is minted under `AC-BIND-011`, not `AC-BIND-016`.** The brief
  pointed at `AC-BIND-016.*` for the publish helpers; that family states the protocol types are
  published, and the helpers' behaviour is `AC-BIND-011`'s (§ Publishing from a provider face).

### Reviewer's questions

1. **(c) Is the shape `D4`/`D5` the contract dale and integrators build against?** It is the STOP A
   question. The alternatives are under § The shape, with why each was set aside.
2. **(c) The name: `Deadline`, or `ExpiresAt`?** `Deadline` follows the brief's word and the SDK's
   one precedent for an absolute instant on a public type (`OfferResult.Deadline`,
   `Vion.Dale.Sdk/Emission/OfferResult.cs:19`). `ExpiresAt` follows the RTU handler's own variable and
   MQTT's "message expiry". Recommendation: `Deadline` — it says what the sender means, and the
   expiry is one host's rendering of it.
3. **(c) `DateTimeOffset`, or a UTC `DateTime`?** The RTU handler's clock reads are UTC `DateTime`
   (`ModbusRtuHandler.cs:219`), and the SDK's request records carry `DateTime` timestamps; dale reads
   `TimeProvider.GetUtcNow()`, a `DateTimeOffset` (`PublishQueue.cs:322`). Recommendation:
   `DateTimeOffset` — an absolute instant with no `Kind` to get wrong across a library boundary an
   integrator writes, and the precedent above.
4. **(b) `AC-HOST-003.5` and `.6` state dale's behaviour from the ratified carrier.** If step 2 words
   its own criterion differently, the `GAP` tail is re-pointed to it and the text follows it in that
   round, by a `MODIFIED` line.

---

## Full design

### The shape

**The records.** Each publish record gains one member, declared in its body:

```csharp
public readonly record struct PublishMqttMessage(string Topic, ..., bool Retain = false, int AttemptNumber = 1)
{
    /// The instant after which no attempt at this message is worth making, or null for none.
    public DateTimeOffset? Deadline { get; init; }
    ...
}
```

and the same on `PublishMqttMessageRequest`. `ToRequest` and `ToMessage` set it with an object
initializer. What dale reads: `message.Deadline is { } deadline`.

- *Survives the retry.* dale's retry is `message with { AttemptNumber = ... }`
  (`PublishQueue.cs:304`). On a record struct, `with` copies the whole value and then runs the named
  `init` setters, so `Deadline` is copied — including by code compiled against a record that did not
  have it yet.
- *Equality.* The synthesized equality covers every field, the new one included, so two messages
  differing only in deadline are unequal. That is what the round-trip test has to see.
- *Opt-in.* The default is `null`; every existing construction, dale's dozen direct ones included
  (`grep -rn "new PublishMqttMessage(" --include=*.cs ../dale/Dale` → 12), compiles and publishes
  unchanged.

**The helpers.** Beside each existing helper, an overload with a required deadline:

```csharp
protected Guid Publish(string topic, byte[] payload, string schemaName, string contentType,
                       DateTimeOffset deadline, Guid? correlationId = null, string? responseTopic = null, bool retain = false)
protected Guid PublishJson<T>(string topic, T payload, string schemaName,
                              DateTimeOffset deadline, Guid? correlationId = null, string? responseTopic = null, bool retain = false)
protected Guid PublishJson<T>(string topic, T payload, JsonTypeInfo<T> typeInfo, string schemaName,
                              DateTimeOffset deadline, Guid? correlationId = null, string? responseTopic = null, bool retain = false)
```

The existing three forward to one private core with no deadline; the new three forward to it with
theirs. The deadline is required and non-nullable on purpose: a trailing optional `deadline = null`
on an overload beside the existing one would make every call that passes only the leading arguments
ambiguous (both candidates fill defaults; C# overload resolution has no tie-break for that — inferred
from the language rules, the build confirms it), and a nullable required one would make a positional
`null` in fifth place ambiguous against `Guid? correlationId`. A non-nullable `DateTimeOffset` in
fifth place converts to nothing the existing overloads take there, so every existing call binds as
before.

**The RTU handler.** `PublishRequest` (`ModbusRtuHandler.cs:489`) takes the deadline and calls the
typed `PublishJson` overload with it; both sites compute `expiresAt` before the call and pass
`new DateTimeOffset(expiresAt)` (a UTC `DateTime`, so offset zero).

### Binary compatibility

`Vion.Dale.Sdk` is always the host's copy; a plugin built earlier runs against the new one
(`docs/specs/plugin-loading.md`; `PluginLoadContext.cs:122-142`). The change only adds:

- `PublishMqttMessage` and `PublishMqttMessageRequest`: the positional constructors, the positional
  properties, `Deconstruct`, `ToRequest`, `ToMessage` and the equality members keep their signatures.
  One property with a getter and an `init` setter is added to each. A struct gaining a field is
  binary-compatible for managed callers: its layout is resolved when the caller is compiled to native
  code, not when it was built.
- `ServiceProviderHandlerBase`: the three existing `protected` helpers keep their signatures and their
  behaviour; three overloads are added.

The check that can show a removed or changed member is .NET package validation against the last
released packages, run locally on an uncommitted pack (`publicapi-manifest.json` lists type names
only, and no type is added). The brief names 0.14.2 as the baseline; 0.14.3 is released since
(`git tag` → `v0.14.3`) and is what dale references today (`../dale` → `Vion.Dale.Sdk 0.14.3`), so the
run takes 0.14.3, and is diffed against the same run on `origin/main` so the result isolates this
change.

### Alternatives set aside

- **A positional parameter on the records** (`..., int AttemptNumber = 1, DateTimeOffset? Deadline =
  null`). The source looks the same, but it replaces the constructor and `Deconstruct`: every plugin
  built earlier that constructs either record fails with `MissingMethodException`.
- **A second constructor on the records** carrying the deadline. Adds an overload set for one optional
  value, and a record struct's `with` already gives every caller the `init` form for free.
- **A trailing optional `deadline` parameter on the existing helpers.** Changes their signatures: every
  handler built against 0.14.x that publishes fails at its first publish on a host shipping the new
  SDK.
- **A `PublishOptions` value passed to one new overload per helper.** One field does not earn a type;
  if a second per-publish knob ever arrives, it is the place to start.
- **A relative timeout instead of an instant.** Ratified away (`D3`): the retry re-sends the same
  message, so a relative value would restart at each attempt.
- **A monotonic timestamp instead of a wall-clock instant.** It is immune to a clock step, but dale and
  the handler would have to share one `TimeProvider`'s timestamp, and the RTU handler's own expiry is
  wall-clock (`ModbusRtuHandler.cs:219`, `:240`); the published deadline would no longer name the
  instant the handler times the request out.

### Affects dale

What dale builds against, the released version it references, and what happens across versions — the
REPORT's *Affects others* carries the same.

- **Shape:** `PublishMqttMessage.Deadline` and `PublishMqttMessageRequest.Deadline`, both
  `DateTimeOffset?`, `null` for none (pending `D4` and questions 2–3).
- **Version:** the first release cut after this merges; it adds public API, so `0.15.0` under SemVer.
  Not cut by this change (`docs/releasing.md`).
- **Both forms:** the request form reaches `BuildApplicationMessage` through `ToMessage`, so step 2's
  interval applies to both, once on the request form since it is not retried.
- **An RTU library rebuilt against the new SDK on an older dale:** the handler's read and write path
  calls a helper overload, and sets a property, that dale's older `Vion.Dale.Sdk` does not have, so the
  first read or write fails with `MissingMethodException` when the handler's method is compiled.
  Inferred from the load rules (`Vion.Dale.Sdk` always the host's, the 0.x major gate never firing,
  `PluginLoadContext.cs:247-249`), not observed.
- **Two RTU copies on one gateway:** `Vion.Dale.Sdk.Modbus.Rtu` is `[DaleSharedAssembly]`, so the
  first plugin to load it decides the copy for every binding (`PluginLoadContext.cs:172-216`). A
  rebuilt RTU library loaded after one still on 0.14.x runs the old handler, and its requests carry no
  deadline, with no error or warning. The desk-unit check has to load only rebuilt RTU libraries, or
  load a rebuilt one first.

---

## Drift checkpoints

- 2026-10-28: The brief names 0.14.2 as the binary baseline; 0.14.3 has been released since and dale
  references it, so the baseline is 0.14.3.
- 2026-10-28: The brief points the helper criterion at `AC-BIND-016.*`; the helpers are
  `AC-BIND-011`'s (`D8`).

---

## Spec delta (to distill)

- ADDED AC-HOST-003.4 -> docs/specs/host-vocabulary.md : THE SYSTEM SHALL let a handler give `PublishMqttMessage` and `PublishMqttMessageRequest` an absolute deadline, none by default, and SHALL keep it when either form converts into the other.
- ADDED AC-HOST-003.5 -> docs/specs/host-vocabulary.md : WHEN a host makes an attempt at a publish carrying a deadline THE SYSTEM SHALL give it the whole seconds left until the deadline, rounded up, as its MQTT message expiry interval, and SHALL give a publish carrying none no expiry interval. GAP: proven by the private runtime, VION-376
- ADDED AC-HOST-003.6 -> docs/specs/host-vocabulary.md : IF a publish's deadline has passed when the host would make an attempt at it THEN THE SYSTEM SHALL drop it without sending it. GAP: proven by the private runtime, VION-376
- ADDED AC-BIND-011.6 -> docs/specs/contracts.md : WHERE a caller passes a deadline to a publish helper THE SYSTEM SHALL carry it on the published message, and SHALL publish with no deadline where the caller passes none.
- ADDED AC-MODB-015.11 -> docs/specs/modbus.md : THE SYSTEM SHALL publish each Modbus RTU read and write request with a deadline of the moment the handler publishes it plus the request's operation timeout, the instant at which the handler itself expires the request.

---

## Tasks

- `T-001` (`AC-HOST-003.4`): the `Deadline` member on both publish records and in both conversions;
  the round-trip test with a deadline and the default-is-none test in `HandlerProtocolShould`.
- `T-002` (`AC-BIND-011.6`): the three helper overloads; `ProviderPublishShould` rows for each, with a
  deadline and without.
- `T-003` (`AC-MODB-015.11`): the RTU handler passes each request's deadline; `ModbusRtuHandlerShould`
  for a read and a write, proven red on the pre-fix code.
- `T-004` (`AC-HOST-003.5`, `AC-HOST-003.6`): distill the GAP'd host criteria and the prose beside
  them.
- `T-005`: package validation against 0.14.3 on a local pack, uncommitted, on `origin/main` and on the
  branch; `modbus-smoke` with `-p:DaleLocalSource=true`; distill and archive.
