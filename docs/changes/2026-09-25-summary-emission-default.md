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
carry 30 s, and each emission knob resolves on its own — the implementation's attribute, then the
interface's, then (for the interval) the value type's, then the SDK's — so an attribute redeclared on
a block for its title no longer throws away the knobs its interface declares.

### Spec implications

`emission.md` § *Which knobs govern a member* is rewritten from whole-attribute to per-knob
resolution: `AC-EMIT-002.1`, `.2`, `.3` and `.5` are reworded, and new leaves state the type's
default interval (`002.6`), that an assigned knob is final whatever its value (`002.7`), and that the
SDK's four summaries declare 30 s (`002.8`). The page's opening definition of the defaults gains the
type's interval. The start-time failure on an unreadable interval (`003.3`) covers the type's
interval; the two interval diagnostics (`012.3`, `.4`) validate the type's declaration as well as a
member's; the immediate-ignores diagnostic (`012.5`) names the SDK default it compares against;
introspection (`013.2`, `.4`, `.5`) reports the policy the gate resolves. The worked paragraph *A
value that changes on every transaction* is rewritten. `modbus.md` and `http.md` mint nothing: their
prose cites `AC-EMIT-002.8`, and `http.md`'s two "declare `MinInterval` in seconds" passages are
rewritten. No criterion is removed; the introspection document keeps its shape and only the values
it reports change.

### Decisions

- `D1` — The mechanism is a per-type emission default carried by the struct type, general enough for
  a consumer's own struct — the brief, constraint 1.
- `D2` — 30 s, on `ModbusLinkSummary`, `ModbusTcpConnectionSummary`, `HttpClientSummary` and
  `HttpServerSummary` — the brief, constraint 2; stated as one criterion (amendment 1, item 8).
- `D3` — **Knobs resolve per knob** (amendment 1, item 3, which replaces the brief's constraint 3).
  For each stream and each knob, the first place that assigned it wins: the stream's attribute on
  the implementing property; else the stream's attribute on the `[ServiceInterface]` property it is
  bound through; else, for `MinInterval` only, the value type's `[DefaultMinInterval]`; else the SDK
  default (250 ms, no deadband, not immediate). A stream with neither attribute is published
  ungated, as today.
- `D4` — **"Assigned" means assigned, whatever the value** (amendment 1, item 3). `MinChange = ""`,
  `MinInterval = "250ms"` and `Immediate = false` all stop the fall-through; that is how an
  implementation cancels what its interface declares. An assigned `""` still means no deadband.
- `D5` — "Only part of a struct carries news" is answered in the summaries' XML docs — the brief,
  constraint 4.
- `D6` — The introspection document keeps its shape — the brief, constraint 5. Nothing here needs
  `vion-contracts`.
- `D7` — `[DefaultMinInterval("30s")]`: `Vion.Dale.Sdk.Core`, `[PublicApi]`, structs only, not
  repeatable (amendment 1, item 1).
- `D8` — Each attribute records internally whether each knob was assigned; the public getters are
  unchanged (amendment 1, item 2).
- `D9` — `DALE036`/`DALE037` extend to the type's declaration (amendment 1, item 4).
- `D10` — The type default governs both streams (amendment 1, item 5).
- `D11` — The new rules fill a knob and do not gate; the page's existing rules then apply unchanged
  (amendment 1, item 7).
- `D12` — The examples, `libraries/` and `templates/` are not touched — the brief, constraint 6.

### Reviewer's questions

All ratified by amendment 1 (`amend-VION-106-dale-sdk-1.md`, 2026-09-25) against the proposal at
`839353ee`.

1. **(a) The default's form and name** → `[DefaultMinInterval("30s")]` (`D7`). OUTCOME: accepted as
   proposed, amendment 1 item 1.
2. **(a) Telling an assigned knob from an omitted one** → recorded inside each attribute, public
   getters unchanged (`D8`). OUTCOME: accepted, amendment 1 item 2 — the coordinator's pick, because
   per-knob resolution needs the record for all three knobs, and the public alternative changes two
   property types and fails 11 asserts in the first consumer.
3. **(a) Which source a knob-free attribute leaves the member with** → neither proposed option: per
   knob (`D3`, `D4`). OUTCOME: changed, amendment 1 item 3. The analyzer draft item proposed for the
   old trap is dropped — the trap is gone.
4. **(a) The rule on authors** → `DALE036`/`DALE037` over the type's declaration (`D9`). OUTCOME:
   accepted, amendment 1 item 4.
5. **(a) Measuring points too** → both streams (`D10`). OUTCOME: accepted, amendment 1 item 5.
6. **(a) Which value type is read** → the member's own, or its underlying type where nullable; never
   an `ImmutableArray<T>`'s element. OUTCOME: accepted, amendment 1 item 6.
7. **(a) Which criteria keep their words** → re-derived under `D3`: `002.4`, `012.7` and `013.3` keep
   them; `002.1`, `.2`, `.3`, `.5`, `003.3`, `012.3`, `.4`, `.5`, `013.2`, `.4` and `.5` change (§ Spec
   delta). OUTCOME: re-derived per amendment 1 item 6.
8. **(a) `DALE038` keeps comparing against the SDK's 250 ms.** OUTCOME: accepted, amendment 1 item 6.

---

## Full design

### 1. What happens today (read this session)

- **The attributes cannot say "omitted".** `ServicePropertyAttribute.cs:100` and
  `ServiceMeasuringPointAttribute.cs:75` initialise `MinInterval` to `"250ms"`; `MinChange` is
  `null` and `Immediate` `false` by default. The gate reads them through the internal
  `IThrottleConfigured` (`IThrottleConfigured.cs:16`).
- **The gate takes a whole attribute.** `LogicBlockBase.ResolveThrottleConfigured`
  (`LogicBlockBase.cs:964-983`) takes the stream's attribute from the implementing property if it
  has one, whatever it assigns (`:974-979`), else from the `[ServiceInterface]` property. The threshold
  search probes that one property's assembly (`:928`). `ThrottlePolicy.FromConfigured`
  (`ThrottlePolicy.cs:26-38`) receives the member's value type (`binding.TargetPropertyType`,
  `LogicBlockBase.cs:933`). A `FormatException` from the parse is rethrown naming member and service
  (`:935-942`).
- **Introspection mirrors it.** `PropertyMetadataBuilder.ExtractRuntimeSplit` (`:381-388`) picks the
  attribute the gate picks; `ExtractThrottle` (`:409-432`) omits `runtime.throttle` when the interval
  equals its own `DefaultMinInterval = "250ms"` (`:20`, `:438-441`) with no deadband and no
  `Immediate`, else reports that attribute's strings. `ExtractRuntime` (`:365-371`) handles a member
  with no interface.
- **The binder** binds an interface member's measuring point only when the interface property carries
  `[ServiceMeasuringPoint]` (`DeclarativeServiceBinder.cs:120-130`); extra members bind from the
  block's own attributes (`:136-158`).
- **The analyzers already tell omitted from assigned**: `EmissionAttributeHelper.GetExplicitMinInterval`
  (`EmissionAttributeHelper.cs:86`). `DALE036`/`037` (`MinIntervalInvalidAnalyzer.cs:40-42`) and
  `DALE039` (`DeadbandWithoutThrottleAnalyzer.cs:49`) check only an assigned value.
- **The first consumer copies knobs by hand** to get around the whole-attribute rule:
  `ElectricityMeterEmuMCenter.cs:186-209` and `ElectricityMeterVirtual.cs:224-233` in
  logic-block-libraries (amendment 1's two spot checks). Its `IModbusTcpDiagnostics` declares
  `MinInterval = "30s"` on both Modbus summaries (`:40`, `:51`), with blocks that implement it bare
  (`BoilerAskomaModbusTcp.cs:507-511`).

### 2. The mechanism

```csharp
[PublicApi]
[AttributeUsage(AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class DefaultMinIntervalAttribute : Attribute
{
    public DefaultMinIntervalAttribute(string minInterval) { MinInterval = minInterval; }
    public string MinInterval { get; }
}
```

- **Each attribute records its assignments.** The three knobs keep their public `get; init;` shape and
  defaults; each `init` also records that it ran. The record is read through an internal interface
  the two attributes implement explicitly, so no public member is added.
- **One resolver**, `Vion.Dale.Sdk/Emission/EmissionKnobs.cs`, takes the implementing property, the
  interface property (or none), the stream and the value type, and returns the three resolved knobs
  plus the property the `MinChange` came from — or nothing when neither property carries the stream's
  attribute (`AC-EMIT-002.4`). Per knob: the implementation's attribute if it assigned it, else the
  interface's if it assigned it, else — for `MinInterval` — the value type's `[DefaultMinInterval]`
  (the nullable's underlying type where the member is nullable), else the SDK default. The SDK's
  `"250ms"` lives there once; the gate and introspection both call the resolver, so the badge cannot
  disagree with the gate (`AC-EMIT-013.4`). The analyzers keep their own copy
  (`EmissionAttributeHelper.cs:20`; the generator assembly cannot reference the SDK).
- **The gate** hands the resolved knobs to `ThrottlePolicy.FromConfigured` unchanged, and probes the
  assembly that declares the `MinChange`'s property for a custom threshold (`AC-EMIT-002.5`).
- **Introspection** omits `runtime.throttle` where all three resolved knobs are the SDK's defaults,
  and otherwise reports the resolved strings — a summary member with no knob reports
  `minInterval: "30s"`.
- **Load context.** The attribute type lives in `Vion.Dale.Sdk`, which a plugin shares with its host
  (`AC-PLUG-003.1`, `docs/specs/plugin-loading.md:69-71`), so the type the resolver asks for is the
  type the summary's assembly was compiled against.

### 3. What the summaries' docs say

Each of the four types' `<remarks>` says, in one paragraph: it changes with every transaction, so
published whole it is gated by its interval alone; it defaults to 30 s, and a member that assigns its
own `MinInterval` sets another. `ModbusLinkSummary` adds that when every change of the verdict
matters, `State` — or a status derived from it — is published as its own member beside the summary,
so the summary itself can stay slow (the shape of `ModbusTcpDebugClient.cs:494`, named generically per
`sdk-surface-conventions.md` § 2). `ModbusTcpConnectionSummary` says the same of its `State`. The HTTP
types' "declare one in seconds, such as `"30s"`" sentences give way to the default.

### 4. Alternatives not taken

- **An analyzer alone**, and **an SDK-owned `[ServiceInterface]`** — rejected by the operator in the
  analysis (brief, § The analysis).
- **Whole-attribute precedence with the type's default filling the gap** (this doc's first proposal,
  Q3 A) — rejected by amendment 1: it keeps the trap where a presentation-only redeclaration discards
  the interface's knobs.
- **Raising the SDK-wide 250 ms** — out of scope (brief, constraint 7).

### 5. Proof plan, and the masks

Every emission test runs on a controllable clock with
`WithEmissionPolicy(EmissionPolicyMode.FromAttributes)` registered (`AC-EMIT-001.2` masks otherwise),
and assigns a *different* value each time, so the dedup floor (`AC-EMIT-004.1`) is not what holds
values back. Where an implementation and an interface both speak, they speak different values.

| Check | Shape | Expected | Today |
| --- | --- | --- | --- |
| Type default applies | struct with `[DefaultMinInterval("30s")]`, member with no knob, a new value every 20 ms for 60 s | first value plus at most one per 30 s | ~240 |
| Nullable reaches it | the same struct as `T?` | as above | ~240 |
| Measuring point too | the same struct on `[ServiceMeasuringPoint]` with no knob | as above | ~240 |
| Assigned beats type | `MinInterval = "250ms"` assigned on the member | 4/s | — |
| Knob-free impl over interface | impl `[ServiceProperty(Title = …)]`, interface `MinInterval = "5s"` | one per 5 s | 250 ms |
| Interface beats type | summary-typed member, bare impl, interface `"5s"` | one per 5 s | — |
| Mixed sources | impl assigns only `MinChange`, interface only `MinInterval = "5s"` | both apply | impl's alone |
| `Immediate` from interface | impl knob-free, interface `Immediate = true` | every value | throttled |
| `""` cancels | impl `MinChange = ""`, interface declares a deadband | no deadband | — |
| Streams never mix | interface `[ServiceProperty(MinInterval = "5s")]` + knob-free `[ServiceMeasuringPoint]` | measuring point at 250 ms | same (regression guard) |
| Threshold from interface's assembly | interface declares `MinChange` for a custom type | resolves from the interface's assembly | same |
| Introspection agrees | the resolved policy of each of the above shapes | `runtime.throttle` as the gate resolves it | — |
| Each shipped type carries it | `[DataRow]` per type | `[DefaultMinInterval]` reads `"30s"` | absent |
| Invalid type default | `[DefaultMinInterval("fast")]` | start fails naming member and service; `DALE036` at the type | — |

Each gets its mutation run before its criterion counts (`docs/spec-process.md` § 5).

---

## Drift checkpoints

- 2026-09-25: The brief's site list was re-derived with its own command
  (`git grep -n "MinInterval" -- '*.cs' ':!*Test*' ':!examples/' ':!templates/' ':!libraries/'`); it
  also matches `Vion.Dale.DevHost.SmokeHost/LogicBlocks/ShowcaseBlock.cs:64`, `:76` (assigned
  intervals, unaffected) and `Vion.Dale.Sdk.Generators/Analyzers/DaleDiagnostics.cs:434-479` (the
  `DALE036`–`039` descriptors). `ServiceBinding.cs:36` is a doc comment and holds no logic.
- 2026-09-25: Both HTTP example blocks assign `MinInterval = "30s"` — `HttpDebugClient.cs:157` and
  `HttpSimServer.cs:151`; the brief named one. The Modbus examples assign none:
  `ModbusTcpDebugClient.cs:360-370` (three members) and `Em122ElectricityMeter.cs:187-189` (RTU).
- 2026-09-25: This repo has no standing upgrade-note file. Release notes are generated from merged
  PRs (`docs/releasing.md` § Cutting a release, `--generate-notes`), and the migration-page mechanism
  was reverted (`0bf5c6b2`, #217); `docs/migrations/` holds one page from 0.10.4. The upgrade note
  therefore rides the PR body's Summary via *Relay notes* below.
- 2026-09-25: Amendment 1 ratified the doc with Q3 changed to per-knob resolution; the questions,
  § Full design, the proof plan, the delta and the tasks above were rewritten to it. `AC-EMIT-002.8`
  sits under `002` as the amendment asks; it reads as the rule's one instance the SDK ships, which is
  what § *Which knobs govern a member* is about.
- 2026-09-25: The gate reads the type default off the binding's target type and introspection off the
  implementing property's type. They are the same type for every declarative binding
  (`ServiceBuilderBase.cs:44-50`, `DeclarativeServiceBinder.cs:120-130`); they differ only for an
  explicit `ServiceBuilder.BindProperty` over a nested path (`ServiceBuilder.cs:31-33`), where
  introspection already describes the root property's type rather than the bound leaf's.

---

## Spec delta (to distill)

- MODIFIED AC-EMIT-002.1 -> docs/specs/emission.md : THE SYSTEM SHALL gate a member's service-property stream and its measuring-point stream independently, resolving each stream's knobs only from that stream's own attributes.
- MODIFIED AC-EMIT-002.2 -> docs/specs/emission.md : WHERE no attribute a stream reads assigns a knob THE SYSTEM SHALL take that knob's default for the stream, and SHALL NOT take the knob from an attribute of the member's other stream.
- MODIFIED AC-EMIT-002.3 -> docs/specs/emission.md : WHEN the implementing property's attribute for a stream assigns a knob THE SYSTEM SHALL take that knob from it, and otherwise SHALL take the knob from the stream's attribute on the `[ServiceInterface]` property the member is bound through, where that attribute assigns it.
- MODIFIED AC-EMIT-002.5 -> docs/specs/emission.md : THE SYSTEM SHALL search for a member's custom change threshold in the assembly that declares the property whose attribute assigned that stream's `MinChange`.
- ADDED AC-EMIT-002.6 -> docs/specs/emission.md : WHERE no attribute a stream reads assigns `MinInterval` and the member's value type, or its underlying type where the member is nullable, declares `[DefaultMinInterval]` THE SYSTEM SHALL take the type's interval as that stream's `MinInterval`.
- ADDED AC-EMIT-002.7 -> docs/specs/emission.md : THE SYSTEM SHALL treat a knob as assigned whenever an attribute assigns it, whatever the value, so neither an interface's knob nor a type's default replaces it.
- ADDED AC-EMIT-002.8 -> docs/specs/emission.md : THE SYSTEM SHALL declare a `[DefaultMinInterval]` of 30 s on each diagnostics summary it ships: `ModbusLinkSummary`, `ModbusTcpConnectionSummary`, `HttpClientSummary` and `HttpServerSummary`.
- MODIFIED AC-EMIT-003.3 -> docs/specs/emission.md : WHEN the `MinInterval` a stream resolves to is not a valid duration THE SYSTEM SHALL fail block initialization.
- MODIFIED AC-EMIT-012.3 -> docs/specs/emission.md : WHEN a member sets `MinInterval`, or a value type declares `[DefaultMinInterval]`, to a value the duration grammar cannot read THE SYSTEM SHALL report `DALE036` as an error.
- MODIFIED AC-EMIT-012.4 -> docs/specs/emission.md : WHEN a member sets `MinInterval`, or a value type declares `[DefaultMinInterval]`, to a positive duration below one millisecond THE SYSTEM SHALL report `DALE037` as a warning.
- MODIFIED AC-EMIT-012.5 -> docs/specs/emission.md : WHEN a member sets `Immediate` together with a `MinChange` or with a `MinInterval` other than the SDK default THE SYSTEM SHALL report `DALE038` as a warning naming the ignored knobs.
- MODIFIED AC-EMIT-013.2 -> docs/specs/emission.md : WHERE part of a stream's resolved emission policy is the SDK default THE SYSTEM SHALL omit that part from the report — the whole `runtime.throttle` entry where the policy is the SDK default throughout, `minChange` where the stream has no deadband, and `immediate` where it is false — comparing the resolved `MinInterval` as a duration rather than as a spelling.
- MODIFIED AC-EMIT-013.4 -> docs/specs/emission.md : THE SYSTEM SHALL report each stream's policy resolved knob by knob from the same attributes and the same value-type default the gate resolves it from, so a member declaring both attributes reports two independent policies.
- MODIFIED AC-EMIT-013.5 -> docs/specs/emission.md : THE SYSTEM SHALL treat an empty `MinChange` as no deadband, in the reported policy as in the gate.

---

## Tasks

- `T-001` (`AC-EMIT-002.1`, `.2`, `.3`, `.5`, `.7`): the attributes record assignments; `EmissionKnobs`
  resolves per knob; the gate uses it. Tests in `Vion.Dale.Sdk.TestKit.Test`
  (`InterfaceEmissionPolicyShould`, `DualAnnotatedEmissionShould`, `CustomThresholdEmissionPolicyShould`).
- `T-002` (`AC-EMIT-002.6`, `AC-EMIT-003.3`): `DefaultMinIntervalAttribute` and its place in the
  resolver. A `TypeDefaultEmissionShould` beside them.
- `T-003` (`AC-EMIT-013.2`, `.4`, `.5`): introspection through the resolver.
  `PropertyMetadataBuilderShould`.
- `T-004` (`AC-EMIT-012.3`, `.4`, `.5`): `DALE036`/`037` over the type's declaration; `DALE038`'s
  wording. `MinIntervalInvalidAnalyzerTests`.
- `T-005` (`AC-EMIT-002.8`): the four types carry `[DefaultMinInterval("30s")]`; their `<remarks>`
  per § 3; one test, a `[DataRow]` per type.
- `T-006`: `emission.md` prose, `http.md` and `modbus.md` passages, `IThrottleConfigured`'s and the
  attributes' XML docs; distill; archive.

---

## Relay notes for the PR body

> Filled as each consumer-visible change lands.

- _(none yet)_
