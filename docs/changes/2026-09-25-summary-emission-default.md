---
slug: summary-emission-default
status: proposed           # proposed | in-flight | parked | archived
blocked-on: none           # for parked docs: what's blocking + ref
areas: EMIT,MODB,HTTP
author: jonasbertsch
created: 2026-09-25
updated: 2026-09-25
supersedes: none           # path of a superseded change doc, or none
---

# A diagnostics summary published whole is slow unless its author asks otherwise

> Change doc — one in-flight change. The `Spec delta` below is distilled into the current-truth
> pages under `docs/specs/` in the implementation PR, then this doc is archived
> (`pwsh scripts/spec-change.ps1 archive <slug>`). Process: `docs/spec-process.md`. Never put
> change narrative inline in a spec page — it lives here.

## At a glance

### Summary

VION-106. A block that publishes one of the SDK's four diagnostics summaries whole, as a
`[ServiceProperty]` with no `MinInterval` written, publishes it four times a second today: every
transaction moves a counter, so the dedup floor never holds a value back, and the SDK's 250 ms is the
only brake. After this change a struct type can carry its own default interval, the four summaries
carry 30 s, and a member that writes nothing is gated at 30 s. Anything an author writes still wins.

### Spec implications

`emission.md` gains a rule — an omitted `MinInterval` is the value type's default where the type
declares one, else 250 ms — as `AC-EMIT-015.*`, and the opening definition of the knob defaults
says so. The criteria that speak of "the default" are re-read against that: the start-time failure
on an unreadable interval (`AC-EMIT-003.3`) covers the type's interval too, the two interval
diagnostics (`AC-EMIT-012.3`, `.4`) validate the type's declaration as well as a member's, the
immediate-ignores diagnostic (`AC-EMIT-012.5`) names the SDK default it compares against, and
introspection (`AC-EMIT-013.2`, `.4`) reports the policy the gate applies, type default included.
The worked paragraph *A value that changes on every transaction* is rewritten: the summaries default
slow, and a field whose every change matters is published as its own member. `modbus.md` and
`http.md` each gain a leaf per summary type stating that it carries the 30 s default, and `http.md`'s
two "declare `MinInterval` in seconds" passages are rewritten. No criterion is removed; the
introspection document keeps its shape and only the values it reports change.

### Decisions

- `D1` — The mechanism is a per-type emission default carried by the struct type, general enough for
  a consumer's own struct — ratified in the brief (constraint 1), not relitigated here.
- `D2` — 30 s, on `ModbusLinkSummary`, `ModbusTcpConnectionSummary`, `HttpClientSummary` and
  `HttpServerSummary` — ratified (constraint 2).
- `D3` — What the author wrote wins: a `MinInterval` on the attribute the gate reads its knobs from
  (`AC-EMIT-002.3`) beats the type's default, which replaces only the SDK's 250 ms — ratified
  (constraint 3).
- `D4` — "Only part of a struct carries news" is answered in the summaries' XML docs, not by a
  per-field mechanism — ratified (constraint 4).
- `D5` — The introspection document keeps its shape; a change needing `vion-contracts` is a STOP —
  ratified (constraint 5). The design below needs none.
- `D6` — The examples, `libraries/` and `templates/` are not touched; the post-release bump adopts
  the change — ratified (constraint 6).
- `D7`–`D14` are this session's proposals and decide-and-document calls, listed under the questions
  below with their class; none is taken until the operator answers.

### Reviewer's questions

Pre-classified per `docs/spec-process.md` § Lane 2. (a) ratified, (b) decide-and-document,
(c) propose-and-wait.

1. **(c) The default's form and name.** Options:
   - **A — `[DefaultMinInterval("30s")]`** on the struct: one positional argument, so an attribute
     without a value does not compile, and it carries only the knob anyone needs now.
   - **B — `[EmissionDefault(MinInterval = "30s")]`**: the member attributes' own knob vocabulary,
     room for `MinChange`/`Immediate` later — but an `[EmissionDefault]` with nothing set compiles,
     so it needs a further rule and analyzer, and the room has no consumer (§ 1).
   - **C — B with all three knobs.** `Immediate` or a deadband as a property of a type has no
     consumer, and a type-level `MinChange` still needs an `IChangeThreshold<T>`.

   **Recommendation: A** (`D7`). `Vion.Dale.Sdk.Core`, `[PublicApi]`,
   `AttributeTargets.Struct`, `AllowMultiple = false`. If a second knob is ever wanted, renaming
   pre-1.0 is the cheap path § 3 names. OUTCOME: (pending the operator)

2. **(c) How the runtime tells an omitted `MinInterval` from one written out.** Options:
   - **A — an internal "was written" read, public getter unchanged.** Both attributes keep
     `public string MinInterval { get; init; }` reading `"250ms"` when unset. A backing field records
     whether the `init` ran, and the internal `IThrottleConfigured.MinInterval` becomes `string?`,
     implemented explicitly as the declared value or `null`. No public signature moves.
   - **B — make the public property `string?`, `null` when unset.** The getter says what was
     written. But the first consumer has 11 tests that read an omitted `MinInterval` off the
     attribute and assert `"250ms"` (`git grep -n "Assert.Equal(DefaultMinInterval" -- '*.cs'` in
     logic-block-libraries: 11 lines, in the `*EmissionShould.cs` files); they go red on upgrade with
     nothing in the build saying why.

   **Recommendation: A** (`D8`). The public getter then answers "what the attribute says", not
   "what the gate applies", and its XML doc says that a type's default governs where it is unset.
   Either way the SDK's `"250ms"` gets one home in `Vion.Dale.Sdk` shared by the gate and
   introspection — `PropertyMetadataBuilder.cs:20` and the attribute initialisers stop holding their
   own — and the analyzers keep theirs (`EmissionAttributeHelper.cs:20`; the generator assembly
   cannot reference the SDK). OUTCOME: (pending the operator)

3. **(c) Which source a knob-free attribute leaves the member with.** The case: the implementing
   property carries `[ServiceProperty(Title = …)]` with no knob; the `[ServiceInterface]` property it
   binds through writes `"5s"`; the value type is a summary. Today the member gets 250 ms, because
   the implementation's attribute wins whole and the interface is never read
   (`LogicBlockBase.cs:974-979`). Options:
   - **A — the type's 30 s.** `AC-EMIT-002.3` stays whole-attribute; the omitted knob on the
     attribute the gate reads falls to the type's default, which is exactly what `D3` says it
     replaces.
   - **B — the interface's 5 s.** Per-knob fall-through: an omitted knob on the implementation falls
     to the interface's written one before the type's. It changes `AC-EMIT-002.3` for every member,
     not just summaries, and only for `MinInterval` — `Immediate` is a `bool` whose omission cannot
     be told from `false` — so the three knobs would inherit by different rules.

   **Recommendation: A** (`D9`). The trap underneath — a presentation-only attribute on the
   implementation silently discards the interface's knobs — exists today at 250 ms and is not this
   change's; a draft item proposes an analyzer for it. OUTCOME: (pending the operator)

4. **(c) The rule the new attribute puts on authors, and its analyzer** (§ 4). The one rule an author
   can break is writing an interval the gate cannot use. Options:
   - **A — extend `DALE036`/`DALE037`** to the type's declaration: same grammar, same floor, same
     remedy. Their message's subject becomes `'{0}'` naming the property or the type
     (`"Property '{0}' has …"` → `"'{0}' has …"`), and `AC-EMIT-012.3`/`.4` gain the site as a
     `[DataRow]`.
   - **B — a new `DALE049`** for the type's site alone: a second id for one rule.

   **Recommendation: A** (`D10`). The runtime counterpart is `AC-EMIT-003.3`, widened to the interval
   a stream resolves to, so a suppressed diagnostic still fails at start naming the member and
   service. OUTCOME: (pending the operator)

5. **(c) Whether the type default governs measuring-point streams too.** A struct is legal on
   `[ServiceMeasuringPoint]` (`StructServiceElementAnalyzer.cs:33`). Options: **A — both streams**,
   each resolving its own omitted interval independently (`AC-EMIT-002.1` holds); **B — the
   service-property stream only**, which makes the default a property of the stream rather than of
   the type. **Recommendation: A** (`D11`): "this value moves on every transaction" is a fact about
   the type, and a measuring point at 250 ms is the more expensive stream to get wrong, because it is
   stored. OUTCOME: (pending the operator)

6. **(b) Which value type is read.** The member's own type, or its underlying type where it is
   `Nullable<T>` — the reach `AC-EMIT-008.3` already gives deadbands. Not the element type of an
   `ImmutableArray<T>`: the array is the value that is gated, and its dedup is by content
   (`AC-EMIT-004.2`). (`D12`) OUTCOME: (pending the operator)

7. **(b) Criteria whose text is unchanged.** `AC-EMIT-002.2` keeps its words: "the knob defaults" is
   defined in the page's opening paragraph, which gains the type default. `AC-EMIT-002.4` keeps its
   meaning — no attribute at all still publishes ungated; the type default fills an omitted knob, not
   an omitted attribute. `AC-EMIT-013.3` already says "effective", which now includes the type's
   interval. `AC-EMIT-012.7` stays true: an omitted `MinInterval` is still never reported on; only its
   prose at `emission.md:313` ("indistinguishable from the default written out") is rewritten.
   (`D13`) OUTCOME: (pending the operator)

8. **(b) `DALE038` keeps comparing against the SDK's 250 ms.** `Immediate` makes any interval
   inert, the type's included, so a summary member with `Immediate = true` and no `MinInterval` is
   not reported; one with `MinInterval = "30s"` is, because that written knob is ignored. Echoing
   `"250ms"` stays the harmless redundancy it is today. `AC-EMIT-012.5`'s "the default" becomes "the
   SDK default". (`D14`) OUTCOME: (pending the operator)

---

## Full design

### 1. What happens today (read this session)

- **The attributes cannot say "omitted".** `ServicePropertyAttribute.cs:100` and
  `ServiceMeasuringPointAttribute.cs:75` initialise `MinInterval` to `"250ms"`; the gate reads it
  through the internal `IThrottleConfigured` (`IThrottleConfigured.cs:16`).
- **The gate.** `LogicBlockBase.ResolveThrottleConfigured` (`LogicBlockBase.cs:964-983`) takes the
  stream's attribute from the implementing property, else from the `[ServiceInterface]` property,
  whole. `ThrottlePolicy.FromConfigured` (`ThrottlePolicy.cs:26-38`) already receives the member's
  value type (`binding.TargetPropertyType`, `LogicBlockBase.cs:933`) and parses `cfg.MinInterval`.
  A `FormatException` from the parse is rethrown naming member and service (`LogicBlockBase.cs:935-942`).
- **Introspection.** `PropertyMetadataBuilder.ExtractRuntimeSplit` (`:381-388`) picks the same
  attribute the gate picks; `ExtractThrottle` (`:409-432`) returns no `runtime.throttle` when the
  interval equals its own `DefaultMinInterval = "250ms"` (`:20`, `:438-441`) with no deadband and no
  `Immediate`; otherwise it reports the attribute's `MinInterval` string. A second path (`:370`)
  reports an extra, non-interface property from itself.
- **The analyzers already tell omitted from written**: `EmissionAttributeHelper.GetExplicitMinInterval`
  (`EmissionAttributeHelper.cs:86`) reads the named argument or returns `null`. `DALE036`/`037`
  (`MinIntervalInvalidAnalyzer.cs:40-42`) and `DALE039` (`DeadbandWithoutThrottleAnalyzer.cs:49`)
  check only a written value; `DALE038` flags a written interval that is not 250 ms
  (`ImmediateIgnoresThrottleKnobsAnalyzer.cs:45-48`, `EmissionAttributeHelper.cs:176`).
- **No struct-level Dale attribute of this kind exists.** `AttributeTargets.Struct` is used today by
  the contract message markers (`CommandAttribute`, `StateUpdateAttribute`,
  `RequestResponseAttribute`) and the API marks.
- **The consumer's shape.** logic-block-libraries' `IModbusTcpDiagnostics` is a `[ServiceInterface]`
  declaring `MinInterval = EmissionDefaults.Diagnostics` (`"30s"`) on both Modbus summaries
  (`IModbusTcpDiagnostics.cs:40`, `:51`); its blocks implement it with no attribute of their own
  (`BoilerAskomaModbusTcp.cs:507-511`). Its "Interim ownership (VION-106)" paragraph expects an
  SDK-owned `[ServiceInterface]`, which this change does not ship (`D1`).

### 2. The mechanism (under the recommendations)

```csharp
/// <summary>The MinInterval a member of this type is gated at when its emission attribute sets none.</summary>
[PublicApi]
[AttributeUsage(AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class DefaultMinIntervalAttribute : Attribute
{
    public DefaultMinIntervalAttribute(string minInterval) { MinInterval = minInterval; }
    public string MinInterval { get; }
}

[PublicApi]
[DefaultMinInterval("30s")]
public readonly record struct ModbusLinkSummary(…);
```

Resolution, one function the gate and introspection both call, per stream:

```
written  = the stream's attribute (AC-EMIT-002.3) → its MinInterval if the init ran, else null
typeDef  = (Nullable underlying of) the member's value type → [DefaultMinInterval]?.MinInterval
interval = written ?? typeDef ?? "250ms"
```

Introspection reports `runtime.throttle` whenever the resolved policy is not the SDK default
throughout, carrying the resolved interval's spelling — so a summary member with no knob reports
`minInterval: "30s"`, and its badge agrees with its gate (`AC-EMIT-013.4`).

The attribute sits on `Vion.Dale.Sdk.Core`, which all three summary assemblies already reference for
`[StructField]`. Reflection reads it in whichever load context the struct was loaded in; the type
identity the gate compares is the shared `Vion.Dale.Sdk` one (`[DaleSharedAssembly]`).

### 3. What the summaries' docs say

Each of the four types' `<remarks>` says, in one paragraph: it changes with every transaction, so
published whole it is gated by its interval alone; it defaults to 30 s, and a member that writes its
own `MinInterval` sets another. `ModbusLinkSummary` adds: when every change of the verdict matters,
publish `State` — or a pill derived from it — as its own member beside the summary, so the summary
itself can stay slow. The derived pill is the pattern in `examples/…/ModbusTcpDebugClient.cs:494`
(`LinkHealth` from `Link.State`, `Connection.State` and whether the client is enabled), named in the
doc generically (§ 2: no example names in XML docs). `ModbusTcpConnectionSummary` says the same of its
`State`. The HTTP types' "declare one in seconds, such as `"30s"`" sentences are replaced by the
default. The member-summary rule applies: all of this is type `<remarks>`, where it renders.

### 4. Alternatives not taken

- **An analyzer alone** (warn on a summary member without a `MinInterval`) — rejected by the operator
  in the analysis: it leaves the default fast and every author writing the same line.
- **An SDK-owned `[ServiceInterface]`** for the Modbus diagnostics — rejected likewise: it fixes the
  consumers who adopt it and nobody else, and it is the only one of the three that fixes the
  interval in a block's service shape rather than in the type.
- **Raising the SDK-wide 250 ms** — out of scope (constraint 7); nothing here needs it.

### 5. Proof plan, and the masks

Every emission test runs on a controllable clock with
`WithEmissionPolicy(EmissionPolicyMode.FromAttributes)` registered (`AC-EMIT-001.2` masks otherwise),
and assigns a *different* summary each time, so the dedup floor (`AC-EMIT-004.1`) is not what holds
values back.

| Check | Shape | Expected | Fails without the change |
| --- | --- | --- | --- |
| Type default applies | test struct with `[DefaultMinInterval("30s")]`, member with no knob, assigned at 50 Hz for 60 s | first value plus at most one per 30 s (≤ 3) | ~240 publishes |
| Written beats type | same, `MinInterval = "250ms"` written on the member | ~4/s | only if omitted and written cannot be told apart |
| Interface beats type | consumer shape: `[ServiceInterface]` writing `"5s"`, no attribute on the implementation | one per 5 s | an interface fixture writing `"30s"` would pass either way — hence `"5s"` |
| Measuring point too (Q5 = A) | same struct on `[ServiceMeasuringPoint]` with no knob | ≤ 3 in 60 s | |
| Introspection agrees | summary member with no knob | `runtime.throttle.minInterval` = `"30s"` | no `runtime.throttle` |
| Each shipped type carries it | `[DataRow]` per type: `ModbusLinkSummary`, `ModbusTcpConnectionSummary`, `HttpClientSummary`, `HttpServerSummary` | the introspected member reports 30 s | a test struct proves the mechanism, not the four types |
| Invalid type default | `[DefaultMinInterval("fast")]` | block start fails naming member and service; `DALE036` at the type | |

Each gets its mutation run before its criterion counts (`docs/spec-process.md` § 5).

---

## Drift checkpoints

- 2026-09-25: The brief's site list was re-derived with its own command
  (`git grep -n "MinInterval" -- '*.cs' ':!*Test*' ':!examples/' ':!templates/' ':!libraries/'`); it
  also matches `Vion.Dale.DevHost.SmokeHost/LogicBlocks/ShowcaseBlock.cs:64`, `:76` (written
  intervals, unaffected) and `Vion.Dale.Sdk.Generators/Analyzers/DaleDiagnostics.cs:434-479` (the
  `DALE036`–`039` descriptors, whose message subject Q4 changes). `ServiceBinding.cs:36` is a doc
  comment and holds no logic.
- 2026-09-25: Both HTTP example blocks write `MinInterval = "30s"` — `HttpDebugClient.cs:157` and
  `HttpSimServer.cs:151`; the brief named one. The Modbus examples write none:
  `ModbusTcpDebugClient.cs:360-370` (three members) and `Em122ElectricityMeter.cs:187-189` (RTU).
- 2026-09-25: This repo has no standing upgrade-note file. Release notes are generated from merged
  PRs (`docs/releasing.md` § Cutting a release, `--generate-notes`), and the migration-page mechanism
  was reverted (`0bf5c6b2`, #217); `docs/migrations/` holds one page from 0.10.4. The upgrade note
  therefore rides the PR body's Summary via *Relay notes* below.
- 2026-09-25: The next free analyzer id is `DALE049` (`DALE048` is the highest in
  `DaleDiagnostics.cs:626`) — relevant only if Q4 is answered B.

---

## Spec delta (to distill)

> Written for the recommended answers. A different answer rewrites the lines it touches before any
> code.

- ADDED AC-EMIT-015.1 -> docs/specs/emission.md : WHERE a stream's emission attribute does not set `MinInterval` and the member's value type, or its underlying type where the member is nullable, declares `[DefaultMinInterval]` THE SYSTEM SHALL gate that stream at the type's interval.
- ADDED AC-EMIT-015.2 -> docs/specs/emission.md : WHERE a stream's emission attribute sets `MinInterval` THE SYSTEM SHALL gate that stream at the written interval, whatever default the member's value type declares.
- MODIFIED AC-EMIT-003.3 -> docs/specs/emission.md : WHEN the `MinInterval` a member's stream resolves to, written or its value type's default, is not a valid duration THE SYSTEM SHALL fail block initialization.
- MODIFIED AC-EMIT-012.3 -> docs/specs/emission.md : WHEN a member sets `MinInterval`, or a value type declares `[DefaultMinInterval]`, to a value the duration grammar cannot read THE SYSTEM SHALL report `DALE036` as an error.
- MODIFIED AC-EMIT-012.4 -> docs/specs/emission.md : WHEN a member sets `MinInterval`, or a value type declares `[DefaultMinInterval]`, to a positive duration below one millisecond THE SYSTEM SHALL report `DALE037` as a warning.
- MODIFIED AC-EMIT-012.5 -> docs/specs/emission.md : WHEN a member sets `Immediate` together with a `MinChange` or with a `MinInterval` other than the SDK default THE SYSTEM SHALL report `DALE038` as a warning naming the ignored knobs.
- MODIFIED AC-EMIT-013.2 -> docs/specs/emission.md : WHERE part of a stream's effective emission policy is the SDK default THE SYSTEM SHALL omit that part from the report — the whole `runtime.throttle` entry where the policy is the SDK default throughout, `minChange` where the member declares no deadband, and `immediate` where it is false — comparing the effective `MinInterval` as a duration rather than as a spelling.
- MODIFIED AC-EMIT-013.4 -> docs/specs/emission.md : THE SYSTEM SHALL report each stream's policy from the same attribute and the same value-type default the gate reads it from, so a member declaring both attributes reports two independent policies.
- ADDED AC-MODB-016.10 -> docs/specs/modbus.md : THE SYSTEM SHALL declare a default `MinInterval` of 30 s on the link summary.
- ADDED AC-MODB-017.4 -> docs/specs/modbus.md : THE SYSTEM SHALL declare a default `MinInterval` of 30 s on the TCP connection summary.
- ADDED AC-HTTP-020.8 -> docs/specs/http.md : THE SYSTEM SHALL declare a default `MinInterval` of 30 s on the client summary.
- ADDED AC-HTTP-021.9 -> docs/specs/http.md : THE SYSTEM SHALL declare a default `MinInterval` of 30 s on the server summary.

The four type leaves' numbers are provisional: each takes its umbrella's next free leaf, re-read
before minting.

---

## Tasks

- `T-001` (`AC-EMIT-015.1`, `AC-EMIT-015.2`, `AC-EMIT-003.3`): `DefaultMinIntervalAttribute`; the
  attributes remember whether `MinInterval` was written; one resolver used by `ThrottlePolicy`; the
  SDK default's one home. Tests in `Vion.Dale.Sdk.TestKit.Test` beside `InterfaceEmissionPolicyShould`
  and `DualAnnotatedEmissionShould`.
- `T-002` (`AC-EMIT-013.2`, `AC-EMIT-013.4`): introspection reports through the same resolver.
  `PropertyMetadataBuilderShould`.
- `T-003` (`AC-EMIT-012.3`, `.4`, `.5`): `DALE036`/`037` over the type's declaration; `DALE038`'s
  wording. `MinIntervalInvalidAnalyzerTests`, `ImmediateIgnoresThrottleKnobsAnalyzerTests`.
- `T-004` (`AC-MODB-016.10`, `AC-MODB-017.4`, `AC-HTTP-020.8`, `AC-HTTP-021.9`): the four types
  carry `[DefaultMinInterval("30s")]`; their `<remarks>` per § 3; one test, a `[DataRow]` per type.
- `T-005`: `emission.md` prose (opening definition, the *value that changes on every transaction*
  paragraph, `:313`), `http.md:436-442` and `:668-670`, `IThrottleConfigured`'s and the attributes'
  XML docs; distill; PublicApi snapshot follows from CI.
- `T-006`: archive.

---

## Relay notes for the PR body

> Filled as each consumer-visible change lands.

- _(none yet)_
