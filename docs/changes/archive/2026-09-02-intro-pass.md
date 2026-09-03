---
slug: intro-pass
status: archived
blocked-on: none           # for parked docs: what's blocking + ref
areas: INTRO
author: spec-pass (INTRO, attempt 1)
created: 2026-09-02
updated: 2026-09-02
supersedes: none           # path of a superseded change doc, or none
---

# INTRO area pass — the introspection document and identifier stability

> Change doc — one in-flight change. The `Spec delta` below is distilled into the current-truth
> pages under `docs/specs/` in the implementation PR, then this doc is archived
> (`pwsh scripts/spec-change.ps1 archive <slug>`). Process: `docs/spec-process.md`. Never put
> change narrative inline in a spec page — it lives here.

## At a glance

### Summary

The fourth SDD area pass. Turns `INTRO` — everything that decides what the introspection JSON
carries, and which C# name keys each element — into `docs/specs/introspection.md` with a rewritten,
id-cited suite. Absorbs RFCs 0017 and 0019 and folds `docs/identifier-stability.md` into the page.
The pack-time producer is the artifact the cloud reads, so every wire claim here was read out of a
real `Vion.Dale.LogicBlockParser` run over a probe plugin, never described from source.

### Spec implications

Creates `docs/specs/introspection.md` (`trace: enforced`): the document's envelope and per-block
record, identifier derivation for every element kind, the three sibling documents
(`schema` / `presentation` / `runtime`) and what routes into each, struct-field presentation,
service relations, the parser's command line and exit codes, and the pack-time hook.

Neighbours are cited, never re-minted: `AC-EMIT-013.*` owns `runtime.throttle`, `AC-EMIT-002.2` the
per-stream knob rule, `AC-GATE-010.*` the gate and instantiation-parameter fields, `AC-GATE-005.9`
the empty-gate recording. `docs/identifier-stability.md` becomes a short pointer to the page carrying
only advice. `docs/testing-conventions.md` § 4's golden-file list is corrected (Drift checkpoints).

### Consumer-visible change — relay this in the PR body

Seven things a consumer or a downstream service sees differently after this pass. Nothing else on the
page changes what an existing, conforming library emits.

**To cloud-api and the dashboard — the document's own values:**

- **`packageId` is the nuspec package id**, not the plugin assembly's name. MSBuild defaults the one to
  the other, and no known library sets them apart, so every current artifact is byte-identical; a library
  that sets `<AssemblyName>` or `<PackageId>` alone now reports the id the platform registers it under.
- **A service property's schema no longer carries `x-kind`.** The measuring-point kind describes the
  series, so it rides the measuring point's schema alone. Please confirm nothing reads `x-kind` off a
  service property — the DevHost's own badge did, and does not now.
- **A component service's members arrive in declaration order**, base class before derived, as a block's
  own members always have. They used to arrive in binder-insertion order. Order only.
- **An array whose elements may be null now says so**: `ImmutableArray<string?>` emits
  `items.type: ["string","null"]` where it emitted `"string"`. Only that shape changes, and only towards
  what the codec already required.

**To library authors — declarations that used to compile and now do not:**

- **A logic block missing from its plugin's `IConfigureServices` fails the pack**, naming the type.
  Register every concrete block, or make the type abstract. Verified against `logic-block-libraries`:
  every concrete block there is registered.
- **`[Presentation]` is properties-only** and **`[StructField]` is constructor-parameters-only.** Both
  were declarable in a second place that no reader ever looked at, so a declaration there compiled,
  emitted nothing and warned about nothing. A compile break only for a declaration that never did
  anything; nothing in this repo, the examples, the template or `logic-block-libraries` had one.
- **A blank or duplicated endpoint identifier fails the pack and block start**, naming the declarations.
  A blank one addressed nothing; a duplicated one silently dropped whichever binding bound first.

### Decisions

- `D1` — RFCs 0017 and 0019 are absorbed and deleted; the rows of 0019 that are bind-time semantics
  (`DALE045`, the derivation's throws) are mapped and cited, not rewritten (brief point 1).
- `D2` — `docs/identifier-stability.md`'s derivation rules become criteria with tests; the doc keeps
  only what is advice (brief point 2). The translation-key *grammar* stays the cloud's (decision
  0110) and is out of scope: the page states the identifier, not the key.
- `D3` — the PublicApi manifest and the API-reference generator are out of scope except as the
  self-check anchor (brief point 4). No row showed the manifest's shape being read as a contract.
- `D4` — the CLI mirror `DalePluginInfo` is **field-complete against `Vion.Contracts` 3.7.0** this
  session (row 86): no drift, so brief point 5's `fix`/`park` fork does not arise.
- `D5` — the pack-time tool still has no test project. Recommendation in Q3; sized and parked.
- `D6` — a row that would change what the document *carries* is `park`, not `fix` (brief point 6),
  and each names its cloud-side or in-repo reader by `file:line`.

### Reviewer's questions

1. **(ratified — cite)** RFC absorption and the bind-time split. Brief point 1. **OUTCOME:** applied;
   34 living reference sites counted (Drift checkpoints), 0019's `DALE045` rows mapped to `ANLZ`, its
   derivation throws to `BIND`.
2. **(ratified — cite)** `identifier-stability.md` folds into the page. Brief point 2. **OUTCOME:**
   applied; 22 living links counted.
3. **(decide-and-document)** How the packed-artifact path gets pinned, given
   `Vion.Dale.LogicBlockParser` has no test project. **Recommendation: `park`.** The
   `AnalyzerWiringShould` precedent shells out from an existing test project to a committed probe;
   here the probe must be a *published* `netstandard2.1` plugin, so the fixture is a whole csproj
   plus a publish step and the test's runtime is a `dotnet publish` (~20 s cold, measured this
   session). That is bigger than a fix. What this pass does instead: the emitted shape is read from a
   real parser run (*What the probes ran*), and the in-repo assertions pin the same shape at
   introspection level. Sized: one `Vion.Dale.LogicBlockParser.Test` project, one probe-plugin
   csproj, ~4 tests (exit 0 + shape, exit 1 + no file, `--exclude-development-only`, byte-identical
   re-run) — roughly a day, and it wants the `dale build`/pack lane's opinion.
4. **(decide-and-document)** PublicApi manifest / API-reference generator out of scope. **OUTCOME:**
   confirmed, `D3`; recorded as a drift checkpoint. Note the producer namespace
   `Vion.Dale.Sdk.Introspection` is not a `[PublicApiNamespace]` at all
   (`Vion.Dale.Sdk/PublicApiConfig.cs:6`–`:8`), so the manifest anchors this area's *attributes*
   only — 32 of its 119 types.
5. **(decide-and-document)** CLI mirror drift. **OUTCOME:** no drift (`D4`, row 86). Two *reader*
   defects in the CLI are rows 87 and 88 and are Tier B (`CLI`), so they are `park`.
6. **(propose-and-wait)** Four rows change what the document carries and are `park` pending the
   operator: **row 2** (`packageId` is the plugin **assembly** name, not the nuspec `<PackageId>`
   that `identifier-stability.md` and decision 0111 name), **row 14** (`typeFullName` carries the CLR
   `+` for a nested block while every other type name in the document is `.`-separated), **row 27**
   (`schema.x-kind` rides a dual-annotated member's *property* document, and the DevHost SPA badges
   it), and **row 6** (a concrete logic block missing from DI is dropped from the artifact with exit
   0 — recommended `fix`, but it newly fails packs that succeed today, so the operator decides).
   Each names its reader in the table.
7. **(propose-and-wait)** Row 52 narrows `[Presentation]`'s `AttributeTargets` from
   `Property | Method` to `Property`. It is a compile break for any consumer who wrote it on a
   method — where it has never done anything. Nothing in this repo and nothing in
   `C:\_gh\logic-block-libraries` (1040 `[Presentation]` declarations) writes it on a method.
   Recommended `fix`; flagged because it is a published-surface narrowing.

### Outcomes — coordinator close-out (2026-09-03)

1. **Q1, Q2 → applied** as recorded above.
2. **Q3 → taken, not parked.** The operator's word: "add the tests if cheap". The in-solution seam —
   two fixture libraries project-referenced into `Vion.Dale.LogicBlockParser.Test` — needed no publish
   step and came in under the 30-minute box; 18 parser tests, the two parser-level fixes proven red
   through them. The three MSBuild-targets criteria stay `GAP` with that reason.
3. **Q4, Q5 → confirmed** as recorded above.
4. **Q6 → row 2 fix** (`--package-id "$(PackageId)"` from both parser invocations, assembly-name
   fallback), **row 14 intended, restated** (the block's `typeFullName` is the CLR full type name — the
   identifier a host loads by; nested blocks were never a design intent but are supported as the CLR
   spells them; every other type name is the display form — and the two-spellings defect found under
   that rule fixed), **row 27 fix**, **row 6 fix** (the first consumer verified: every concrete block
   registered).
5. **Q7 → fix**; `[StructField]` narrowed the same way after the completeness critic (M1).
6. **Ratified at close-out, from amendment 2:** M3 fix (the position map applied per owning type —
   a component service's member order changes once), M4 fix (the array element's nullability read
   from the compiler's flag bytes at every depth), M7 park (`dale list` without the development-only
   exclusion — `CLI`'s, filter-or-mark decided when the CLI is specced). Round 3 ran in a fresh
   session per the skill's rule, after two rounds in which spec-not-carried and a stale count recurred.

---

## Full design

### Anchor inventory (step 2 — the completeness checklist for the sweeps)

Counted this session, never transcribed from the brief.

**A1 — the schema authority.** `Vion.Contracts` **3.7.0**, namespace `Vion.Contracts.Introspection`
(read by reflection off
`~/.nuget/packages/vion.contracts/3.7.0/lib/netstandard2.1/Vion.Contracts.dll`; nothing there is
changed by this pass). **8 types, 36 fields**:

| Type | Fields |
| --- | --- |
| `DalePluginInfo` | `PackageId`, `PackageVersion`, `Annotations`, `LogicBlocks` |
| `LogicBlockIntrospectionResult` | `TypeFullName`, `Interfaces`, `Contracts`, `Services`, `Annotations` |
| `…+InterfaceInfo` | `Identifier`, `InterfaceTypeFullNames`, `MatchingInterfaceTypeFullNames`, `Annotations` |
| `…+ContractInfo` | `Identifier`, `ContractTypeFullName`, `MatchingContractType`, `Annotations` |
| `…+ServiceInfo` | `Identifier`, `IncludedWhen`, `InterfaceTypeFullNames`, `Properties`, `MeasuringPoints`, `InwardRelations`, `OutwardRelations` |
| `…+ServicePropertyInfo` | `Identifier`, `Schema`, `Presentation`, `Runtime` |
| `…+ServiceMeasuringPointInfo` | `Identifier`, `Schema`, `Presentation`, `Runtime` |
| `…+ServiceRelationInfo` | `RelationType`, `InterfaceIdentifier`, `InterfaceTypeFullName`, `Annotations` |

**A2 — the sibling-document models**, `Vion.Contracts.TypeRef`: `TypeSchema` (3), `TypeAnnotations`
(9), `Presentation` (11), `RuntimeMetadata` (2), `ThrottleMetadata` (3), and the five `TypeRef`
shapes (`Primitive`, `Enum`, `Struct`, `Array`, `Nullable`) plus `StructField` (2) — **28 fields
across 11 types**, excluding the three `IsEmpty` computed flags.

**A3 — the producer**, `Vion.Dale.Sdk/Introspection/`: **5 files, 1642 lines** —
`LogicBlockIntrospection.cs` (668), `PropertyMetadataBuilder.cs` (435), `TypeRefBuilder.cs` (393),
`StructFieldPresentationBuilder.cs` (109), `MockActorContext.cs` (37).

**A4 — the attributes whose named parameters reach the document as an identifier, a display string
or a presentation hint.** 16 attributes, **62 members** (emission knobs excluded — `EMIT` owns them;
gating attributes excluded — `GATE` owns them):

| Attribute | Members reaching the document |
| --- | --- |
| `LogicBlockAttribute` | `Name`, `Icon`, `Groups` |
| `ServicePropertyAttribute` | `Title`, `Description`, `Unit`, `StringFormat`, `Minimum`, `Maximum`, `WriteOnly`, `ReadOnly` |
| `ServiceMeasuringPointAttribute` | `Title`, `Description`, `Unit`, `StringFormat`, `Minimum`, `Maximum`, `Kind` |
| `PresentationAttribute` | `DisplayName`, `Group`, `Order`, `Importance`, `StatusIndicator`, `Decimals`, `UiHint`, `Format`, `VisibleWhen` |
| `StructFieldAttribute` | `Title`, `Description`, `Unit`, `StringFormat`, `Minimum`, `Maximum`, `WriteOnly` |
| `EnumLabelAttribute` | `Label` |
| `SeverityAttribute` | `Severity` |
| `LogicBlockInterfaceBindingAttribute` | `ForInterface`, `Identifier`, `DefaultName`, `Tags`, `Multiplicity` |
| `ServiceProviderContractBindingAttribute` | `Identifier`, `DefaultName`, `Tags`, `Multiplicity` |
| `ServiceProviderContractTypeAttribute` | `ServiceProviderContractType`, `Consumers`, `DevelopmentOnly` |
| `LogicBlockContractAttribute` | `BetweenInterface`, `AndInterface`, `BetweenDefaultName`, `AndDefaultName`, `Direction` |
| `ServiceRelationAttribute` | `RelationType`, `OutwardsInterface` |
| `PersistentAttribute` | `Exclude` |
| `LogicInterfaceAttribute` | `MatchingInterface`, `SenderInterface`, `ContractType` |
| `ServiceInterfaceAttribute` | **none** — a bare marker (Drift checkpoint) |
| `TimerAttribute` | `Identifier`, `IntervalSeconds` — **neither reaches the document** (Drift checkpoint) |

**A5 — DALE descriptors whose subject is a presentation or an identifier rule**, enumerated from
`Vion.Dale.Sdk.Generators/Analyzers/DaleDiagnostics.cs` (47 living descriptors in the file; **15** in
this area). `ANLZ`'s to rewrite, mine to cite: `DALE013`, `DALE014`, `DALE015` (public-surface
markers), `DALE021` (Decimals), `DALE023` (UiHint trigger), `DALE024` (StatusIndicator), `DALE026`
(literal group key), `DALE027`, `DALE028` (Format), `DALE032` (Importance), `DALE033` (StringFormat),
`DALE040` (`[StructField(WriteOnly)]`), `DALE041`, `DALE042` (VisibleWhen), `DALE045`
(ServiceRelation). `DALE001`, `DALE003`, `DALE016`, `DALE022` and `DALE025` are other areas' but are
the compile-time half of rows 29, 32, 33, 37, 38 and 76 and are cited there.

**A6 — the authoring constants that reach the document verbatim**: `PropertyGroup` **7**, `UiHints`
**6**, `Formats` **11**, `StringFormats` **5** — **29 constants**.

**A7 — the parser's command line** (`Vion.Dale.LogicBlockParser/Program.cs`, 345 lines): **2
positional arguments**, **1 option** (`--exclude-development-only`), **2 exit codes** (0 / 1), **3
argument-validation refusals**, **1 stdout notice prefix** (`Vion Dale: `), **1 environment variable**
(`DALE_PARSER_VERBOSE`).

**A8 — the pack-time hook**, `Vion.Dale.Sdk/build/Vion.Dale.Sdk.targets`: **3 targets**
(`PublishBeforeParseLogicBlocks`, `RunVionDaleLogicBlockParser`, `IncludePublishedFilesInPack`), **3
properties**, **1 opt-out** (`SkipVionDaleLogicBlockParser`), **1 analyzer `ItemGroup`**.

**A9 — the PublicApi manifest** (`docs/snapshots/publicapi-manifest.json`): 12 assemblies, **119
types**; **32** are this area's (`Vion.Dale.Sdk.Core.*` attributes and constants plus
`Configuration.Contract.LogicBlockContractBase`). The producer's own namespace is not a
`[PublicApiNamespace]`, so `LogicBlockIntrospection` and `MockActorContext` are absent by design.

**A10 — consumers walked: 11.** `Vion.Dale.Cli/Models/DalePluginInfo.cs`,
`Vion.Dale.Cli/Commands/ListCommand.cs`, `Vion.Dale.Cli/Commands/BuildCommand.cs`,
`Vion.Dale.DevHost/Control/DevHostIntrospection.cs`,
`Vion.Dale.DevHost/Topologies/LogicBlockDefinition.cs`, `Vion.Dale.DevHost/DevConfigurationBuilder.cs`,
the definitions controller under `Vion.Dale.DevHost.Web/Api/`,
`Vion.Dale.DevHost.Web/wwwroot/components.js`, `…/predicates.js`, the cloud (read-only, in
`C:\_gh\architecture`), and the two Tier C examples.

### Behavior table (steps 3–6)

All six columns. `Evidence` is `file:line` read this session, or **(probe N)** for a row executed
against the working tree — see *What the probes ran* for each probe's fixture shape and the surface
it read. `⚠` rows have a failure sketch under the table.

#### The document's envelope — `Vion.Dale.LogicBlockParser/Program.cs`

| # | Behavior (EARS) | Evidence | Test today | Rec | Why |
|---|---|---|---|---|---|
| 1 | WHEN a plugin assembly is introspected THE SYSTEM SHALL emit one document carrying the package identity, the package version and one record per logic block. | `Program.cs:174`–`:181` | GAP | intended | the artifact `dotnet pack` puts in the nupkg and the cloud reads |
| 2 | ⚠ WHEN a plugin assembly is introspected THE SYSTEM SHALL report the assembly's simple name as the document's package identity. | `Program.cs:317` (`assembly.GetName().Name`); **(probe 1)** `<PackageId>Acme.IntroProbe.PackageId</PackageId>` with `<AssemblyName>IntroProbe.Assembly</AssemblyName>` emits `"packageId": "IntroProbe.Assembly"` | GAP | park | `identifier-stability.md:88` and decision `0111` name the nuspec `<PackageId>` as the global namespace, and `dale upload` registers the library under `project.PackageId` (`Vion.Dale.Cli/Helpers/ProjectDiscovery.cs:186`) — so the two disagree wherever a library sets either property. Changing the emitted value is a wire change cloud-api's extraction reads (`architecture/flows/metadata-translation.md`, step 1) |
| 3 | WHEN the plugin assembly declares an informational version THE SYSTEM SHALL report it with any build-metadata suffix removed, and SHALL report `0.0.0` when it declares none. | `Program.cs:322`; **(probe 1)** `<Version>4.5.6-probe.1</Version>` emits `"packageVersion": "4.5.6-probe.1"` | GAP | intended | the version cloud-api version-guards the translation-catalog sync against |
| 4 | THE SYSTEM SHALL emit an empty annotation map at document level. | `Program.cs:178`; **(probe 1)** `"annotations": {}` | GAP | intended | a reserved slot the producer never fills; a consumer that read its absence as a version signal would be wrong |
| 5 | THE SYSTEM SHALL introspect every non-abstract `LogicBlockBase` subclass of the plugin assembly, ordered by full type name. | `Program.cs:327` | GAP | intended | the block order is the document's order, and it must not depend on reflection iteration |
| 6 | ⚠ WHEN a logic-block type is not registered in the plugin's `IConfigureServices` THE SYSTEM SHALL fail the run, naming that type. | `Program.cs:137`–`:141`, `:157`–`:163`; **(probe 2)** `IntroProbe.ThrowingBlock` unregistered → logged, absent from the artifact, **exit 0** | GAP | fix | a `dotnet pack` that succeeds while shipping an artifact missing a block is the worst of both: `dale upload` sends it, the cloud catalogs only the blocks it can see, and the missing one is discovered by its absence in the dashboard |
| 7 | WHEN introspecting a logic block throws THE SYSTEM SHALL fail the run with exit code 1, write no document, and report the originating exception rather than a reflection wrapper. | `Program.cs:145`–`:150`, `LogicBlockIntrospection.cs:140`–`:147`; **(probe 2)** a zero-field struct member → `NotSupportedException` with the producer stack, `EXITCODE=1`, no output file | GAP | intended | `dotnet pack` must not produce a half-written artifact, and the author needs the reason, not "Exception has been thrown by the target of an invocation" |
| 8 | WHEN the plugin-path or the output-path argument is missing or empty THE SYSTEM SHALL fail with exit code 1 and print the usage line. | `Program.cs:86`–`:98` | GAP | intended | the tool is invoked by MSBuild, so a bad invocation must fail the build rather than quietly write nothing |
| 9 | WHEN the named plugin assembly does not exist THE SYSTEM SHALL fail with exit code 1. | `Program.cs:110`–`:114` | GAP | intended | same |
| 10 | THE SYSTEM SHALL accept its own options in any position and case-insensitively, and SHALL exclude every option-shaped argument from the positional ones. | `Program.cs:78`–`:81` | GAP | intended | the positional arguments are also handed to the generic host builder, which would try to bind an option it does not know |
| 11 | WHEN the development-only exclusion is requested THE SYSTEM SHALL leave out every logic block that binds a development-only contract and SHALL name each excluded block and its bindings on standard output. | `Program.cs:169`–`:172`, `:189`–`:230`; **(probe 3)** `Vion Dale: 1 logic block(s) are development-only …` / `  IntroProbe.DevOnlyProbe — binds Face (DigitalOutputProvider)` | `LogicBlockIntrospectionShould.ReportEveryDevelopmentOnlyContractOfASimulatorBlock` (the predicate only) | intended | bench surface must not reach the cloud, and a pack log that did not say which blocks it dropped would make the omission look like a bug |
| 12 | THE SYSTEM SHALL prefix every such notice line with a stable marker. | `Program.cs:32`, `:236` | GAP | intended | `dale upload` captures the pack output rather than inheriting it and repeats the lines carrying the prefix |
| 13 | THE SYSTEM SHALL emit a byte-identical document for repeated runs over one assembly. | `LogicBlockIntrospection.cs:520`–`:588`; **(probe 1)** three separate processes, one MD5 | `LogicBlockIntrospectionShould.SerializeTheStatusAndLabelMapsInSortedKeyOrder`, `StructFieldPresentationShould.SerializeTheStructFieldPresentationInSortedKeyOrder` | intended | VION-77: .NET randomizes string hashing per process, so an unsorted map made every pack write a different file and every export undiffable |
| 14 | **MODIFIED — the operator reversed Phase A's direction.** THE SYSTEM SHALL report a logic block's identity as its CLR full type name, and every other type name in the document in display form. | `LogicBlockIntrospection.cs:39` (`Type.FullName`) against `ReflectionHelper.GetDisplayFullName` (`:287`) everywhere else; **(probe 2)** a nested block emits `"IntroProbe.Outer+NestedBlock"` | GAP | intended | a block's type name is what a host **loads the type by** (`Vion.Dale.DevHost/Topologies/DevTopologyLoader.cs:230` resolves it through `Type.GetType`), so it is the CLR's spelling or it resolves nothing. Nested blocks were never a design intent but are supported. The split is stated on the page; the `dale list` short-name split is row 88's, in the ledger |
| 15 | THE SYSTEM SHALL write the document indented, with camelCase member names and camelCase annotation keys, and enum values as their member names. | `Program.cs:36`–`:46` | GAP | intended | the annotation *dictionary* keys are camelCased at this boundary, so the producer's `DefaultName` reaches the file as `defaultName` — the wire form decision `0110` quotes verbatim |

#### The block record — `LogicBlockIntrospection.cs`

| # | Behavior (EARS) | Evidence | Test today | Rec | Why |
|---|---|---|---|---|---|
| 16 | WHEN a logic block declares a name THE SYSTEM SHALL report it under the annotation key `DefaultName`. | `:646`–`:653`; **(probe 1)** `"defaultName": "Probe & <b>markup</b> — Grüße"` | `LogicBlockIntrospectionShould.ReadBlockLevelAnnotations` | intended | the attribute member is `Name`; the wire key predates it and downstream readers key on it |
| 17 | WHEN a logic block declares an icon or groups THE SYSTEM SHALL report them, and SHALL omit each annotation whose value is absent or empty. | `:646`–`:665`; **(probe 1)** `[LogicBlock(Name = "", Icon = "", Groups = new string[0])]` emits `"annotations": {}` | `…ReadBlockLevelAnnotations`, `…ReturnEmptyAnnotationsWhenNoLogicBlockAttribute` | intended | an empty block name is not a display string, and an empty annotation would be catalogued as a translatable key with an empty source string |
| 18 | WHEN a logic block carries no `[LogicBlock]` THE SYSTEM SHALL report an empty annotation map. | `:641`–`:645` | `…ReturnEmptyAnnotationsWhenNoLogicBlockAttribute` | intended | the attribute is optional; its absence is not an error |
| 19 | THE SYSTEM SHALL carry a display string into the document verbatim, whatever characters it contains. | **(probe 1)** `Title = "Tür & <i>Wärme</i> — 20 °C"` and `Description = "Ünïcödé — em-dash — and \"quotes\""` emit unchanged | GAP | intended | display strings are the translation sources; normalising one would change the digest the cloud pins staleness on |

#### Services and their identifiers

| # | Behavior (EARS) | Evidence | Test today | Rec | Why |
|---|---|---|---|---|---|
| 20 | THE SYSTEM SHALL identify a logic block's root service by the block's short class name. | `DeclarativeServiceBinder.cs:21` | `ContractCarriedServiceRelationsShould.PinTheEmittedRelationShapePerService` (incidentally) | intended | the root service identifier is a translation-key part, and no attribute overrides it |
| 21 | THE SYSTEM SHALL identify a component service by the name of the property holding it. | `DeclarativeServiceBinder.cs:76`; **(probe 1)** properties `Sub` and `sub` emit services `Sub` and `sub` | `ContractCarriedServiceRelationsShould.AttachComponentHalvesToTheComponentServiceWithItsWiringIdentifier` | intended | the same rule one level down: renaming the property mints a new key for every title under it |
| 22 | THE SYSTEM SHALL distinguish service identifiers case-sensitively. | `ServiceBinder.cs:268`–`:281` (ordinal dictionary); **(probe 1)** `Sub` and `sub` are two services | GAP | intended | two spellings must not merge into one node, and the cloud's key grammar is verbatim |
| 23 | THE SYSTEM SHALL report on each service the service-interface types its bound members came through, by display full name, without repetition. | `:340`–`:356` | GAP | intended | the interface list is how a consumer recognises a service's shared shape across blocks |
| 24 | THE SYSTEM SHALL report a service's properties and measuring points in base-to-derived declaration order. | `:64`–`:100`, `:359`–`:381`; **(probe 1)** the probe block's fifteen properties in source order | `LogicBlockIntrospectionOrderingShould.EmitPropertiesInBaseToDerivedOrder` | intended | reflection order is unspecified, so without this the document differed run to run — the other half of row 13 |
| 25 | THE SYSTEM SHALL identify a service property and a measuring point by the C# property name, with no override. | `:421`, `:441`, `:458`, `:479` | GAP | intended | `identifier-stability.md`'s deliberate gap: services and members have no `Identifier =` knob, and none is planned (decision `0110`) |
| 26 | WHEN one property carries both `[ServiceProperty]` and `[ServiceMeasuringPoint]` THE SYSTEM SHALL report it as one identifier in each of the two lists, carrying one title and one description. | `PropertyMetadataBuilder.cs:212`–`:216`; **(probe 1)** `Dual` appears once under `properties` and once under `measuringPoints`, both `"title": "Dual title"` | `LogicBlockIntrospectionOrderingShould.EmitVisibleWhenIntoBothThePropertyAndMeasuringPointDocsOfADualAnnotatedMember` | intended | `identifier-stability.md:41` states it: one translatable member, one title key |
| 27 | ⚠ WHEN one property carries both emission attributes THE SYSTEM SHALL report the measuring-point kind on the measuring point's schema and not on the property's. | `PropertyMetadataBuilder.cs:257` — `ExtractTypeAnnotations` is never told which stream it serves; **(probe 1)** `Dual`'s *property* schema carries `"x-kind": "measurement"` | GAP | park | `Vion.Dale.DevHost.Web/wwwroot/components.js:68`,`:79` renders a `MeasuringPointKind = …` badge off `schema['x-kind']` for a property as readily as for a measuring point, so a property tile claims a knob describing the other stream — the shape `AC-EMIT-013.4` closed for `runtime.throttle`, still open on `schema`. Omitting a field is a wire change |

#### The schema document — `TypeRefBuilder.cs`, `PropertyMetadataBuilder.cs`

| # | Behavior (EARS) | Evidence | Test today | Rec | Why |
|---|---|---|---|---|---|
| 28 | THE SYSTEM SHALL report a schema for every service property and every measuring point. | `:509` (`fullDoc["schema"]!`) | `TypeRefBuilderShould` (37 cases) | intended | `schema` is the one sibling the introspection contract makes mandatory; a consumer may not null-check it |
| 29 | THE SYSTEM SHALL take a member's title, description, unit and string format from either emission attribute, preferring the service property's. | `PropertyMetadataBuilder.cs:220`–`:226` | `LogicBlockIntrospectionShould.ReadServicePropertySchemaAnnotations`, `…ReadMeasuringPointSchemaAnnotations` | intended | the cross-fill is what lets a dual-annotated member state its display strings once; `DALE025` reports a conflict |
| 30 | ⚠ THE SYSTEM SHALL report a declared bound only where it is finite, treating every non-finite value as absent. | `PropertyMetadataBuilder.cs:230`–`:243` (`IsNegativeInfinity` for the minimum, `IsPositiveInfinity` for the maximum — one-sided per bound); **(probe 4)** `Minimum = double.NaN` alone, and `Minimum = +∞, Maximum = -∞` alone, each fail the whole run with `ArgumentException: .NET number values such as positive and negative infinity cannot be written as valid JSON`, exit 1, no file | GAP | fix | the *other* infinity and every NaN pass the sentinel test and reach a serializer that cannot write them. One member's bound loses the whole artifact, and the message names neither the property nor the block — it advises changing a `JsonSerializer` option, which is the SDK author's lever, not the library author's |
| 31 | THE SYSTEM SHALL report a member as read-only when it is a measuring point without a service property, when its implementing property has no public setter, when the service property opts in, or when it is an instantiation parameter. | `PropertyMetadataBuilder.cs:257` | `TypeRefBuilderShould.EmitsReadOnlyOnMeasuringPointSchema`, `…EmitsReadOnlyOnServicePropertyWithPrivateSetter`, `…EmitsReadOnlyOnServicePropertyWithReadOnlyOptIn`, `…OmitsReadOnlyOnServicePropertySchema` | intended | the fourth clause is `AC-GATE-010.3`'s — cite, do not re-mint |
| 32 | THE SYSTEM SHALL report write-only from the service-property attribute alone. | `PropertyMetadataBuilder.cs:261` | GAP | intended | a measuring point is published, never written, so a write-only measuring point has no meaning; `DALE022` restricts the flag to strings |
| 33 | THE SYSTEM SHALL build a member's schema from a bool, byte, short, ushort, int, uint, long, float, double, `DateTime`, `TimeSpan`, `Guid`, string, enum, flat readonly record struct, immutable array of those, or a nullable of any value type or string, and SHALL refuse any other type. | `TypeRefBuilder.cs:160`–`:212`; **(probe 2)** the refusal reaches the pack path as row 7's exit 1 | `TypeRefBuilderShould.EmitsPrimitiveSchemaFor…` (×6), `…EmitsNullable…` (×4), `…EmitsArraySchemaForImmutableArrayOfDouble`, `…EmitsStructSchemaForLocation`, `…EmitsEnumSchemaForCurrentAlarm` | intended | `DALE003` / `DALE016` refuse the same set at compile time, so the runtime throw is the pack-path backstop for an assembly built without the analyzer |
| 34 | THE SYSTEM SHALL report an enum as its short type name and its member-name strings, never its ordinals. | `TypeRefBuilder.cs:196`–`:199`; **(probe 1)** `{"type":"string","title":"NoLabels","enum":["Alpha","Beta"]}` | `LogicBlockIntrospectionShould.ReadEnumMembersInSchema`, `TypeRefBuilderShould.EmitsEnumSchemaForCurrentAlarm` | intended | the short type name is the enum's translation-key part (decision `0110`), and ordinals would break on a reordered enum |
| 35 | THE SYSTEM SHALL report a struct as its short type name and its positional-constructor parameters in declaration order, each keyed camelCase, with only the non-nullable ones required and no additional properties. | `TypeRefBuilder.cs:266`–`:308` | `TypeRefBuilderShould.EmitsStructSchemaForLocation`, `…EmitsAdditionalPropertiesFalseOnStruct`, `…RequiresOnlyNonNullableStructFields` | intended | the camelCase field name is the struct field's translation-key part; requiring a nullable field would reject a legitimately absent value |
| 36 | WHEN a struct declares more than one constructor THE SYSTEM SHALL enumerate its fields from the one with the most parameters. | `TypeRefBuilder.cs:268`–`:270`; **(probe 1)** `TwoCtors(double A, double B)` with a one-argument overload emits both fields | GAP | intended | the compiler emits the primary positional constructor with every field; a convenience overload describes a subset |
| 37 | WHEN a struct used as a member's type has no positional constructor THE SYSTEM SHALL refuse the introspection naming that struct. | `TypeRefBuilder.cs:268`–`:271`; **(probe 2)** `readonly record struct Empty()` → `NotSupportedException`, exit 1, no file | GAP | intended | `DALE016` refuses it at compile time with the same rule; the throw is the pack-path backstop |
| 38 | THE SYSTEM SHALL report struct-field annotations, member labels and severities for the fields of a member's own struct type and no deeper. | `TypeRefBuilder.cs:37`–`:50` and `StructFieldPresentationBuilder.cs:52`–`:69` both walk the top-level constructor only; **(probe 3)** `Branch(Leaf Child, double Amp)` emits `child.volt` with no title and no unit, and a fields node carrying only `child` | GAP | intended | `DALE016` refuses a non-flat struct at compile time (`AnalyzerHelper.AllStructFieldsArePrimitiveOrEnum`, `:211`), so a conforming build never nests one; the page states the flat rule rather than the partial walk |
| 39 | THE SYSTEM SHALL report a struct field's authored title, description, unit, string format, bounds and write-only flag inline on that field's schema, and SHALL omit a field that declares none. | `TypeRefBuilder.cs:51`–`:72`; **(probe 1)** `Mixed.Watt` emits `{"title":"Scalar title","x-unit":"kW"}`, `Mixed.Note` emits neither | `TypeRefBuilderShould.EmitsStructFieldAnnotationsForScheduledSetpoint`, `…EmitsWriteOnlyOnlyForTheSecretStructField` | intended | the inline slot is where a scalar field's title belongs; duplicating it in the fields node would leave two sources with no precedence rule |
| 40 | THE SYSTEM SHALL detect a nullable reference member from the compiler-emitted nullability annotation, falling back to the declaring constructor's and then the declaring type's context. | `TypeRefBuilder.cs:330`–`:381`; **(probe 1)** `string? Note` emits `"type":["string","null"]` | `TypeRefBuilderShould.EmitsNullableStringSchemaForOptionalErrorMessage`, `…EmitsNonNullableStringSchemaForNonNullName` | intended | without it the outbound codec throws on a null string field and the whole property publish is dropped |
| 41 | THE SYSTEM SHALL report a declared minimum above a declared maximum unchanged. | `PropertyMetadataBuilder.cs:230`–`:243`; **(probe 4)** `Minimum = 10, Maximum = 1` emits `"minimum": 10, "maximum": 1` | GAP | intended | the bounds are what an editor renders, not what the block enforces — the same boundary `AC-GATE-010.6` states for a parameter |
| 42 | THE SYSTEM SHALL report an empty authored title, description, unit or string format as an empty value rather than omitting it. | **(probe 4)** `Title = "", Description = "", Unit = "", StringFormat = ""` emit `"title": "", "description": "", "x-unit": ""` | GAP | intended | display strings ride verbatim (row 19); the only emptiness filter in the document is on the three block-level annotations (row 17) |

#### The presentation document

| # | Behavior (EARS) | Evidence | Test today | Rec | Why |
|---|---|---|---|---|---|
| 43 | WHEN a member has no presentation to report THE SYSTEM SHALL report a null presentation document. | `PropertyMetadataBuilder.cs:346`, `LogicBlockIntrospection.cs:509`–`:515`; **(probe 1)** `Marked` emits `"presentation": null` | `LogicBlockIntrospectionShould.NotIncludeAbsentPresentationKeys` | intended | an empty object and a null are different to a consumer that null-checks before reading a key |
| 44 | THE SYSTEM SHALL report a member's display name, group, order, importance, UI hint, decimals, format and visibility predicate as declared. | `PropertyMetadataBuilder.cs:292`–`:340` | `LogicBlockIntrospectionShould.ReadImportanceAnnotation`, `…ReadGroupAnnotation`, `…ReadDisplayNameOverridingDefaultName`, `…ReadDisplayOrderAnnotation`, `…ReadUIHintAnnotation`, `PropertyMetadataBuilderShould.EmitVisibleWhenFromThePresentationAttribute` | intended | the keys the DevHost SPA renders (`components.js:63`–`:86`) and the dashboard mirrors |
| 45 | THE SYSTEM SHALL treat the integer sentinel as "unset" for order and for decimals, reporting neither. | `PropertyMetadataBuilder.cs:311`–`:312`; **(probe 4)** `Order = int.MinValue, Decimals = int.MinValue` emit neither key | GAP | intended | attribute parameters cannot be nullable, so the sentinel is the only way to say "not specified" — and an author who writes it gets the same answer as one who omits the knob |
| 46 | THE SYSTEM SHALL omit importance where it is the default. | `PropertyMetadataBuilder.cs:316`; **(probe 4)** `Importance = Importance.Normal` emits no key | GAP | intended | every member has an importance, so reporting it on all of them would say nothing |
| 47 | WHEN a member is declared a status indicator and declares no explicit UI hint THE SYSTEM SHALL report the status-indicator hint. | `PropertyMetadataBuilder.cs:306` | `LogicBlockIntrospectionShould.ReadStatusIndicatorAnnotation`, `TypeRefBuilderShould.EmitsUIHintStatusIndicatorFromStatusIndicatorAttribute`, `…OmitsUIHintWhenNoUIHintNorStatusIndicator` | intended | a dashboard detects a status tile by an explicit hint rather than inferring it from the presence of severities, which an enum can legitimately lack |
| 48 | WHEN a member is declared a status indicator THE SYSTEM SHALL report the severity of each member of its enum type, lower-cased, and SHALL report none otherwise. | `PropertyMetadataBuilder.cs:352`–`:359`, `:113`–`:135`; **(probe 1)** `SevLoud` emits `"statusMappings":{"Bad":"error","Good":"success"}`, `SevQuiet` emits none | `LogicBlockIntrospectionShould.ReadStatusMappingsFromStatusIndicatorProperty` | intended | the flag is what routes the member to a status tile, and severities without a tile have nothing to colour |
| 49 | THE SYSTEM SHALL read severities through a nullable enum and SHALL NOT read them through an array of enum. | `PropertyMetadataBuilder.cs:118`–`:122` peels `Nullable<T>` only; **(probe 2)** a nullable-enum member emits `statusMappings`, an immutable-array-of-enum member emits none while still emitting the status-indicator hint | GAP | intended | `DALE024` warns for exactly the array case and says the mappings will be ignored (`StatusIndicatorRequiresEnumAnalyzer.cs:43`–`:52`, which unwraps `Nullable<T>` only), so the omission is that diagnostic's promise kept |
| 50 | THE SYSTEM SHALL report each declared enum-member label without requiring any flag, reading through a nullable and through an array of enum, and SHALL omit an unlabelled member. | `PropertyMetadataBuilder.cs:143`–`:172`; **(probe 1)** an `ImmutableArray<Sev>` member emits `"enumLabels":{"Good":"Fine"}`; an enum with no labels emits no key | `TypeRefBuilderShould.EmitsEnumLabelsFromEnumLabelAttribute`, `…OmitsEnumLabelsOnNonEnumProperty` | intended | an unlabelled member is still translatable — its source string is the raw member name (`identifier-stability.md:44`, decision `0110`) — so the label map is an override list, not a catalogue |
| 51 | THE SYSTEM SHALL report a declared label for every enum member that carries one, whatever its value, including an empty one, a duplicated one and one on a combined flags member. | **(probe 1)** `DupLabels` emits `{"One":"Same","Two":"Same"}` and `FlagsWithLabel` emits `{"Read":"Read side","ReadWrite":"Both"}`; **(probe 4)** an empty label emits `{"EmptyLabel":""}` | GAP | intended | the map is keyed by member name, so labels cannot collide; the label is a translation *source*, and rewriting one would change the digest the cloud pins staleness on |
| 52 | ⚠ THE SYSTEM SHALL accept a presentation declaration on a service property or a measuring point and nowhere else. | `PresentationAttribute.cs:11` declares `AttributeTargets.Property \| AttributeTargets.Method`; the only producer read is `PropertyMetadataBuilder.cs:292` off a `PropertyInfo`, and all nine presentation analyzers register `SymbolKind.Property` | GAP | fix | a declaration the surface accepts that nothing ever reads: `[Presentation]` on a `[Timer]` method compiles, emits nothing and draws no diagnostic. Nothing in this repo and nothing in `C:\_gh\logic-block-libraries` (1040 declarations) writes it on a method, so narrowing the target costs nobody |
| 53 | THE SYSTEM SHALL carry an empty display name, group, UI hint or format verbatim. | **(probe 4)** `Group = PropertyGroup.None` emits `"group": ""`; empty display name, UI hint and format emit `""` each | GAP | intended | the same verbatim rule as row 42; `PropertyGroup.None` is the shipped constant for "no group" and its wire form is the empty key |
| 54 | WHEN a member's schema title carries its own type identity THE SYSTEM SHALL route the authored title to the presentation display name instead. | `PropertyMetadataBuilder.cs:99`–`:107`, `:298`; **(probe 1)** an enum member emits `"title":"NoLabels"` in schema and `"displayName":"No labels"` in presentation | `TypeRefBuilderShould.RoutesEnumPropertyTitleToPresentationDisplayName`, `…RoutesStructPropertyTitleToPresentationDisplayName` | intended | an enum's or struct's `schema.title` is the CLR type name, which is the cloud's key for that type's labels; without the reroute the member's own title would be silently dropped |
| 55 | THE SYSTEM SHALL apply that same rule to a struct field, reporting its authored title under the fields node only where the field's own schema title carries type identity. | `StructFieldPresentationBuilder.cs:76`–`:83`; **(probe 1)** `Mixed` emits a display name for its enum and nullable-enum fields and none for its scalar field | `StructFieldPresentationShould.CarryAnEnumFieldsAuthoredTitleOnEveryEmissionPath`, `…LeaveAScalarFieldsAuthoredTitleInlineRatherThanDuplicatingIt`, `…LeaveTheEnumFieldsSchemaTitleCarryingItsTypeIdentity` | intended | one rule, one level down; VION-105 |
| 56 | THE SYSTEM SHALL report a struct field's enum-member labels and severities under the fields node without requiring the status-indicator flag. | `StructFieldPresentationBuilder.cs:85`–`:92`; **(probe 1)** both enum-typed fields emit `enumLabels` and `statusMappings` | `StructFieldPresentationShould.CarryEnumLabelsAndSeveritiesOnEveryEmissionPath`, `…LabelAFieldThatCarriesNoStructFieldAttributeAtAll`, `…KeepThePropertyLevelPresentationItRidesAlongside` | intended | the flag exists because it also routes a *property* to a status tile — a meaning a struct field does not have |
| 57 | WHEN a member's struct has nothing to carry beside its schema THE SYSTEM SHALL report no fields node. | `StructFieldPresentationBuilder.cs:69`, `:95`; **(probe 1)** `TwoCtors` emits a presentation without a fields node | `StructFieldPresentationShould.LeaveAStructWithNothingToCarryWithoutAFieldsNodeAtAll` | intended | otherwise an otherwise-empty presentation stops serializing to null and row 43 breaks |
| 58 | THE SYSTEM SHALL report the status-mapping, enum-label and struct-field maps in ordinal key order. | `LogicBlockIntrospection.cs:520`–`:588`; **(probe 1)** `SevLoud` emits `Bad` before `Good`; `Mixed` emits `maybe` before `state` | `…SerializeTheStatusAndLabelMapsInSortedKeyOrder`, `StructFieldPresentationShould.SerializeTheStructFieldPresentationInSortedKeyOrder` | intended | row 13's mechanism: these are the only maps in the document built from immutable dictionaries |
| 59 | The producer never fills the presentation category. | `PropertyMetadataBuilder.cs:325`–`:329` | GAP | out-of-spec | a codec field kept for compatibility that this producer always leaves null; categories folded into group, and no consumer-observable behavior turns on it |

#### Interface-bound members

| # | Behavior (EARS) | Evidence | Test today | Rec | Why |
|---|---|---|---|---|---|
| 60 | WHEN a member is bound through a service interface THE SYSTEM SHALL build its schema from the interface's declaration and its presentation and runtime from the implementing property. | `LogicBlockIntrospection.cs:428`–`:448`, `PropertyMetadataBuilder.cs:60`–`:86`; **(probe 1)** `Shared` emits `"title":"From interface"` in schema and `"displayName":"Class display name"` in presentation | `PropertyMetadataBuilderShould.MergePresentationFromInterfaceAndClassPerField` | intended | the interface owns the data contract a family of blocks shares; the class owns what its own instance looks like |
| 61 | WHEN both the interface and the implementing property declare presentation THE SYSTEM SHALL take each field from the class where the class sets it and from the interface otherwise. | `PropertyMetadataBuilder.cs:176`–`:200` | `…MergePresentationFromInterfaceAndClassPerField`, `…InheritEntirePresentationWhenClassDeclaresNone`, `…CascadeVisibleWhenFromTheInterfaceWhenTheClassDeclaresNone`, `…PreferTheClassVisibleWhenOverTheInterface` | intended | shared UI semantics declared once, per-instance detail overridden |
| 62 | THE SYSTEM SHALL decide a member's writability from the implementing property, not from the interface. | `PropertyMetadataBuilder.cs:69`–`:72` | GAP | intended | the implementing property is what a set-value request actually writes; the interface only declares intent |

#### The runtime document

| # | Behavior (EARS) | Evidence | Test today | Rec | Why |
|---|---|---|---|---|---|
| 63 | THE SYSTEM SHALL report a member as persistent when it opts in without excluding itself, and SHALL report a null runtime document when there is nothing to report. | `PropertyMetadataBuilder.cs:378`–`:386`; **(probe 1)** `Marked` emits `"runtime": null` | GAP | intended | the same null-versus-empty distinction as row 43 |
| 64 | THE SYSTEM SHALL report a member's emission policy per stream. | `PropertyMetadataBuilder.cs:389`–`:435`; **(probe 1)** `Dual`'s property document carries `"minInterval":"1s"`, its measuring-point document `"5s"` | `LogicBlockIntrospectionOrderingShould.EmitEachStreamsOwnThrottleNodeForDualAnnotatedMember`, `…EmitThrottleNodeForInterfaceInheritedPolicy`, `…OmitUnsetFieldsOfReportedPolicy`, `…OmitThrottleNodeOfStreamDeclaringNoKnobs` | intended | `AC-EMIT-013.1`–`AC-EMIT-013.6` — cite, do not re-mint |
| 65 | THE SYSTEM SHALL report an instantiation parameter's marker and default on the runtime document. | `LogicBlockIntrospection.cs:617`–`:632` | `ConfigGatingMetadataShould.ReportInstantiationParameterMarkerAndDeclaredDefault`, `…ReportEnumParameterDefaultAsMemberName`, `…ReportNullDefaultForParameterDeclaredWithout` | intended | `AC-GATE-010.4`, `AC-GATE-010.5` — cite, do not re-mint |

#### Interface and contract bindings, and their identifiers

| # | Behavior (EARS) | Evidence | Test today | Rec | Why |
|---|---|---|---|---|---|
| 66 | THE SYSTEM SHALL identify an interface binding by its declared identifier, and where none is declared by the holding property's name joined to the interface's name for a property-based binding and by the bare interface name for a class-implemented one. | `DeclarativeInterfaceBinder.cs:119`, `:147`; **(probe 2)** `Endpoint_IToggleable`, `Endpoint_IPing` | `ContractCarriedServiceRelationsShould.CarryAClassLevelIdentifierOverrideIntoTheHalf`, `…CarryAPropertyLevelIdentifierOverrideIntoTheHalf` | intended | one of the two places `identifier-stability.md` offers a decoupling knob; the derived form is what a topology names |
| 67 | THE SYSTEM SHALL identify a contract binding by its declared identifier, and by the holding property's name where none is declared. | `DeclarativeContractBinder.cs:46`; **(probe 2)** `Relay` | `LogicBlockIntrospectionShould.UsePropertyNameAsContractIdentifierWhenNotSpecified`, `…IntrospectContractsWithIdentifiers` | intended | the other decoupling knob |
| 68 | ⚠ WHEN a binding declares an identifier that is empty or blank THE SYSTEM SHALL refuse the introspection naming the member. | `DeclarativeInterfaceBinder.cs:147` and `DeclarativeContractBinder.cs:46` both use `??`, which guards null only; **(probe 2)** `Identifier = ""` and `Identifier = "   "` emit `"identifier": ""` and `"identifier": "   "` | GAP | fix | an endpoint with no name cannot be wired, and `Vion.Dale.Cli/Commands/ListCommand.cs:74`,`:76` filters an empty identifier out of `dale list` entirely — so the author is told the endpoint does not exist while the artifact says it does — and it becomes a translation key with an empty part. The same two attributes' `DefaultName` already uses `!string.IsNullOrEmpty` (`DeclarativeInterfaceBinder.cs:253`, `DeclarativeContractBinder.cs:83`), so the surface is inconsistent with itself |
| 69 | ⚠ WHEN two bindings of one block resolve to one identifier THE SYSTEM SHALL refuse the introspection naming both members. | `LogicBlockIntrospection.cs:96`–`:104` — both `AddContract` and `AddInterface` assign into a dictionary; **(probe 2)** a class-level and a property-level binding pinned to one identifier emit **one** endpoint, and two contract bindings pinned to one identifier emit **one**, exit 0, no diagnostic | GAP | fix | the loser is gone from the artifact while the block still binds it, so a topology wired from the catalog reaches an endpoint that is not the one it named. Worse for relations: **(probe 2)** the root service's derived half still names the shared identifier, which now resolves to the survivor — an edge in the cloud graph pointing at a different endpoint than the one it was derived from |
| 70 | THE SYSTEM SHALL report an interface binding's logic-interface type and its matching counterpart type by display full name. | `LogicBlockIntrospection.cs:152`–`:168` | `ContractCarriedServiceRelationsShould.EmitTheEndpointsLogicInterfaceTypeAndAnEmptyAnnotationBag` | intended | the pair is how the platform decides which endpoints may be linked |
| 71 | THE SYSTEM SHALL report an interface binding's default name, tags and multiplicity, omitting each where it is unset or default, and SHALL report the contract's name alongside. | `DeclarativeInterfaceBinder.cs:246`–`:265`, `FunctionInterfaceMetaData.cs:24`–`:52`, `LogicBlockIntrospection.cs:170`–`:196` | `LogicBlockIntrospectionShould.ReadContractNameOnBetweenSideInterface`, `…ReadInterfaceMultiplicityAnnotation`, `…ReadInterfaceDependencyTagsAnnotation` | intended | the annotation bag is the wiring editor's whole vocabulary for an endpoint |
| 72 | WHEN a logic interface's contract declares role default names THE SYSTEM SHALL report this side's under one key and the counterpart's under another, omitting each where unset. | `LogicBlockIntrospection.cs:200`–`:212` | `…ReadRoleDefaultNamesOnBetweenSide`, `…ReadRoleDefaultNamesOnAndSide` | intended | the two role names are translated per binding block — no key aliasing anywhere in the scheme (decision `0110`) |
| 73 | THE SYSTEM SHALL resolve a contract's declared direction to an inbound or an outbound arrow for the side the endpoint is on, and to none where the contract declares none. | `LogicBlockIntrospection.cs:215`–`:225` | `…ResolveOutboundDirectionOnBetweenSide`, `…ResolveInboundDirectionOnAndSide` | intended | resolving here rather than in each client keeps two renderers from disagreeing about which way an edge points |
| 74 | THE SYSTEM SHALL report a contract binding's contract-type interface by display full name and its contract-type token, and SHALL refuse a bound contract whose type declares no such interface. | `LogicBlockIntrospection.cs:227`–`:246` | `…IntrospectContractMatchingContractType` | intended | the token is the identifier the platform pairs a binding to its handler through — `identifier-stability.md:60` calls it the one identifier that is not a translation key |
| 75 | THE SYSTEM SHALL report a contract binding's default name, tags, multiplicity, provider-side consumer limit, handler-actor name and development-only flag, omitting each where it is unset or default. | `LogicBlockIntrospection.cs:246`–`:271`, `ContractMetaData.cs:24`–`:51` | `…IntrospectContractDefaultNameAnnotation`, `…IntrospectContractMultiplicityAndConsumersAnnotations`, `…IntrospectContractHandlerActorNameAnnotation`, `…IntrospectDevelopmentOnlyAnnotationOnEveryProviderFace`, `…OmitTheDevelopmentOnlyAnnotationOnOrdinaryContracts`, `…IntrospectContractTagsAnnotation`, `…ReportADevelopmentOnlyContractEvenWhenItsBindingIsGated` | intended | the same wiring vocabulary as row 71, plus the flag row 11 filters on |
| 76 | WHEN a contract property has no setter THE SYSTEM SHALL refuse the introspection naming the property and the block. | `DeclarativeContractBinder.cs:22`–`:27` | GAP | intended | the binder is what constructs a contract, so a property it cannot assign would stay null for the life of the block; `DALE001` says the same at compile time |

#### Service relations — RFC 0019 absorbed

| # | Behavior (EARS) | Evidence | Test today | Rec | Why |
|---|---|---|---|---|---|
| 77 | WHEN a bound endpoint's contract declares a service relation THE SYSTEM SHALL report one relation half per declaration on the service owning that endpoint, carrying the endpoint's own wiring identifier. | `DeclarativeInterfaceBinder.cs:176`–`:227`; **(probe 2)** the component service carries `{"relationType":"LightToToggle","interfaceIdentifier":"Endpoint_IToggleable"}` | `ContractCarriedServiceRelationsShould.DeriveBothHalvesOfOneRelationOnADualRoleClass`, `…DeriveOneHalfPerDeclarationWhenAContractCarriesSeveral`, `…AttachComponentHalvesToTheComponentServiceWithItsWiringIdentifier`, `…PinTheEmittedRelationShapePerService` | intended | the half is registered by the code path that just minted the identifier, so a relation can never name an endpoint the block does not answer to |
| 78 | THE SYSTEM SHALL report a half as outward where the endpoint's interface is the one the declaration names, and as inward otherwise. | `DeclarativeInterfaceBinder.cs:223`–`:224` | `…DeriveBothHalvesOfOneRelationOnADualRoleClass`, `…PinTheEmittedRelationShapeForADualRoleClass` | intended | the edge's direction in the cloud graph |
| 79 | WHEN an endpoint's holding property carries no service surface THE SYSTEM SHALL report no relation half for it. | `DeclarativeInterfaceBinder.cs:112`, `:212`–`:215`, `ServiceSurface.cs:23`–`:26`; **(probe 2)** three non-service-bearing endpoint properties contribute no halves | `…EmitNoHalfForANonServiceBearingComponent` | intended | there is no node in the cloud graph to anchor the edge to; `DALE045` reports the omission at compile time |
| 80 | THE SYSTEM SHALL report a relation half with an empty annotation map. | `LogicBlockIntrospection.cs:316`–`:334` | `…EmitTheEndpointsLogicInterfaceTypeAndAnEmptyAnnotationBag` | intended | a reserved slot, like row 4 |
| 81 | WHEN a service relation names an interface that is neither side of its contract, or sits on a class carrying no contract declaration, THE SYSTEM SHALL refuse the introspection. | `DeclarativeInterfaceBinder.cs:190`–`:208` | `…ThrowWhenOutwardsInterfaceNamesNeitherContractSide`, `…ThrowWhenTheRelationCarrierIsNotALogicBlockContract` | intended | bind-time semantics, `BIND`/`ANLZ`'s to own (`DALE045`); cited here because the refusal is what a `dotnet pack` reports |
| 82 | WHEN a gated component is excluded from a configured instance THE SYSTEM SHALL report no relation half for its endpoints, and SHALL report them in the definition view. | `DeclarativeInterfaceBinder.cs:90`–`:93` | `…EmitGatedComponentHalvesInDefinitionModeAndOmitThemWhenGatedOutLive` | intended | the definition-view rule is `AC-GATE-006.1`'s — cite, do not re-mint; the relation half is this page's |

#### The pack-time hook

| # | Behavior (EARS) | Evidence | Test today | Rec | Why |
|---|---|---|---|---|---|
| 83 | WHEN a logic-block library is packed THE SYSTEM SHALL publish the project, run the introspection over the published assembly excluding development-only blocks, and fail the pack if that run fails. | `Vion.Dale.Sdk.targets:17`–`:56` (`ContinueOnError="false"`) | GAP: no test project exists for the pack path — Q3 | intended | the artifact must be built from the *published* closure, or a plugin's own dependencies are missing while it is introspected |
| 84 | THE SYSTEM SHALL write the document beside the published output under the project's name and pack the whole published folder. | `Vion.Dale.Sdk.targets:5`–`:6`, `:63`–`:80` | GAP: Q3 | intended | the runtime loads the plugin from that folder and the cloud reads the document from it |
| 85 | THE SYSTEM SHALL skip the whole introspection step when the consuming project opts out. | `Vion.Dale.Sdk.targets:25` (`SkipVionDaleLogicBlockParser`) | GAP: Q3 | intended | a project that is not a logic-block library — a development host, a test host — references the SDK and must still pack |
| 86 | THE SYSTEM SHALL supply the source generator and analyzers to every consuming project. | `Vion.Dale.Sdk.targets:9`–`:12` | `AnalyzerWiringShould` (`docs/testing-conventions.md` § 3) | intended | every compile-time half of this page's rules depends on it |

#### Consumers

| # | Behavior (EARS) | Evidence | Test today | Rec | Why |
|---|---|---|---|---|---|
| 87 | THE SYSTEM SHALL let a reader that cannot reference the contracts package deserialize the document with every member optional. | `Vion.Dale.Cli/Models/DalePluginInfo.cs:1`–`:135` — **field-complete against A1 this session**: all 8 types, all 36 fields, no `required` | `Vion.Dale.Cli.Test/Models/DalePluginInfoDeserializationTests.cs` (`CLI`, Tier B — mapped, not rewritten) | intended | the CLI shells out to whichever parser the consumer's SDK version ships, so it must read older and newer documents alike |
| 88 | ⚠ The `dale list` projection drops an endpoint whose identifier is empty, and derives a block's short name by splitting its full name on `.`. | `Vion.Dale.Cli/Commands/ListCommand.cs:72`, `:74`, `:76`, `:153` | GAP | park | `identifier-stability.md:52` promises `dale list` prints the identifiers "exactly as introspection emits them", which is false for an empty one, and a nested block lists as `Outer+NestedBlock`. Rows 68 and 14 remove both inputs; the projection itself is `CLI`'s (Tier B) |
| 89 | THE SYSTEM SHALL let a development host read a member's writability from its schema and an instantiation parameter's marker and default from its runtime document, and SHALL pass the presentation document through untouched. | `Vion.Dale.DevHost/Control/DevHostIntrospection.cs:208`, `:551`–`:553`, `:681`–`:692` | `CTRL`'s suite — mapped, not rewritten | intended | the presentation document is opaque passthrough at every hop: cloud-api stores it as a bare node and the dale runtime never parses it |
| 90 | THE SYSTEM SHALL let a client render a member from the presentation document alone. | `Vion.Dale.DevHost.Web/wwwroot/components.js:63`–`:86` reads `displayName`, `group`, `order`, `importance`, `uiHint`, `decimals`, `format`, `visibleWhen`, `statusMappings`, `enumLabels`, `fields` | `CTRL`'s suite | intended | every key row 44 emits has a live reader, which is what makes the opaque node a contract rather than a bag |

---

### Failure sketches for the ⚠ rows

- **Row 2 — package identity.** A library sets `<PackageId>Acme.Chargers</PackageId>` and leaves
  `<AssemblyName>` at the project name `Acme.Chargers.Library`. `dale upload` registers the library
  under `Acme.Chargers` (`ProjectDiscovery.cs:186`) while the artifact inside the nupkg says
  `"packageId": "Acme.Chargers.Library"`. Whichever of the two the extraction uses, the other is
  wrong, and `identifier-stability.md:88`'s "changing your own PackageId re-namespaces every key" is
  advice about the wrong property.
- **Row 6 — a block missing from DI.** An author adds a block and forgets
  `services.AddTransient<NewBlock>()`. `dotnet pack` succeeds, `dale upload` succeeds, the dashboard
  offers every block but the new one, and the only trace is one `fail:` line in a pack log nobody
  re-reads.
- **Row 14 — nested block identity.** A library nests a block inside a static class for grouping.
  `dale list` shows `Outer+NestedBlock` as its name; the cloud's translation key for the block's own
  display name carries a `+` no other part of the grammar produces.
- **Row 27 — the kind on the wrong stream.** A grid meter surfaces power as live state *and* a chart.
  The development host's property tile shows a `MeasuringPointKind = Measurement` badge next to a
  writable service property, describing a policy that belongs to the chart beside it.
- **Row 30 — a non-finite bound.** One member of one block is declared with a NaN minimum — accepted
  by the compiler, unpoliced by any analyzer. The whole library stops packing, with an exception that
  names no member and tells the author to change a `JsonSerializer` option they do not own.
- **Row 52 — a presentation declaration on a method.** An author puts
  `[Presentation(DisplayName = "Reset")]` on a `[Timer]` method expecting it to label the action. It
  compiles, emits nothing, and warns about nothing.
- **Row 68 — a blank identifier.** A binding is declared `Identifier = ""` — a placeholder left in,
  or a constant that resolved to empty. The artifact carries an endpoint named `""`; `dale list` says
  the block has no such endpoint; a topology cannot name it; the cloud catalogs a translation key
  whose interface part is empty.
- **Row 69 — an identifier collision.** A block pins a class-level binding to `"Relay"` and later a
  property-level binding to `"Relay"` as well. The artifact carries one endpoint; the block binds
  two; the derived relation half for the first now names the second. Nothing anywhere reports it.
- **Row 88 — the `dale list` projection.** Two reader defects in a Tier B project, both made
  unobservable by rows 68 and 14; the projections themselves belong to `CLI`'s pass.

### What the probes ran

Every row marked **(probe N)** was executed this session against the working tree, never inferred.
One throwaway plugin outside the repo, read through `Vion.Dale.LogicBlockParser` — the tool
`Vion.Dale.Sdk.targets` runs on `dotnet pack` — in four configurations.

**Fixture shape, shared by all four.** `IntroProbe.csproj`, `netstandard2.1`, `ProjectReference` to
the working tree's `Vion.Dale.Sdk` and `Vion.Dale.Sdk.DigitalIo`,
`<AssemblyName>IntroProbe.Assembly</AssemblyName>`,
`<PackageId>Acme.IntroProbe.PackageId</PackageId>`, `<Version>4.5.6-probe.1</Version>`.
`dotnet publish`, then
`dotnet Vion.Dale.LogicBlockParser.dll <publish>/IntroProbe.Assembly.dll <out>.json`.
**Surface read: the packed artifact** — the emitted JSON file, not the definition view and not the
live view.

**A `ProjectReference` to `Vion.Dale.Sdk` runs no analyzer.** Every probe is therefore a reading of
the *runtime* producer with the compile-time half absent; where a row's rule also has a diagnostic
(rows 33, 37, 38, 49, 52, 76) the diagnostic is cited from its analyzer source, never from the probe.

- **Probe 1 — presentation and schema shapes** (`ProbeBlock`, `Outer.NestedBlock`, `EmptyNameBlock`).
  Rows 2–4, 13, 16–17, 19, 21–22, 24, 26–27, 34, 36, 39–40, 43, 48, 50–51, 54–58, 60, 63–64.
- **Probe 2 — wiring, identifiers and refusals** (`WiringProbe`, `Point`, `BareEndpoint`,
  `ThrowingBlock`). Rows 6, 7, 14, 33, 37, 49, 66–70, 77, 79.
- **Probe 3 — nested structs and the development-only filter** (`NestedStructProbe`, `DevOnlyProbe`).
  Rows 11, 38.
- **Probe 4 — edge values** (`EdgeProbe`). Rows 30, 41–42, 45–46, 51, 53.

Pasted results the rows rest on:

**Determinism (row 13)** — three separate parser processes over one published assembly:

```
68ec75b66dbffb7afd86611d24c123ce *…/out.json
68ec75b66dbffb7afd86611d24c123ce *…/out2.json
68ec75b66dbffb7afd86611d24c123ce *…/out3.json
```

**Package identity (row 2)** — the probe declares `<PackageId>Acme.IntroProbe.PackageId</PackageId>`:

```
packageId      = 'IntroProbe.Assembly'
packageVersion = '4.5.6-probe.1'
annotations    = {}
```

**Nested block identity (row 14)** — the same run's log line and its artifact disagree:

```
info: Program[0]
      IntroProbe.Outer.NestedBlock             <- the log (GetDisplayFullName)
"typeFullName": "IntroProbe.Outer+NestedBlock"  <- the artifact (Type.FullName)
```

**Identifier collisions and blank identifiers (rows 68, 69)** — five declared interface bindings and
four declared contract bindings on one block:

```
contract identifiers : [('', 'Empty contract id'), ('Relay', 'Named relay'), ('Twin', 'Second twin')]
interface identifiers: [('Collide', 'Property level'), ('', None), ('   ', None),
                        ('Endpoint_IToggleable', None), ('Endpoint_IPing', None)]
```

`TwinA` (`Identifier = "Twin"`, `DefaultName = "First twin"`) and the class-level
`[LogicBlockInterfaceBinding(typeof(IToggleable), Identifier = "Collide", DefaultName = "Class level")]`
are both absent; the run exits 0. The root service's derived relation half still reads
`"interfaceIdentifier": "Collide"`.

**Non-finite bounds (row 30)** — `[ServiceProperty(Title = "NaN bounds", Minimum = double.NaN,
Maximum = double.NaN)]` on one member of one block, everything else unchanged:

```
Dale Logic Block Parser failed with error:
Message: .NET number values such as positive and negative infinity cannot be written as valid JSON.
         To make it work when using 'JsonSerializer', consider specifying
         'JsonNumberHandling.AllowNamedFloatingPointLiterals' (…)
Type: ArgumentException
Stack trace:    at System.Text.Json.ThrowHelper.ThrowArgumentException_ValueNotSupported()
   at System.Text.Json.Utf8JsonWriter.WriteNumberValue(Double value)
   at System.Text.Json.Nodes.JsonObject.WriteTo(…)
   …
EXIT=1
```

`Minimum = double.PositiveInfinity, Maximum = double.NegativeInfinity` alone reproduces it. No output
file is written in either case.

**A block missing from DI (row 6), and a block that throws (row 7)**:

```
fail: Program[0]
      Failed to instantiate the following logic blocks because they are not registered in the DI:
      IntroProbe.ThrowingBlock
info: Program[0]
      Instantiated and parsed the following 4 logic blocks: …
      The results have been saved to the file: …\out-w.json.
EXIT=0
```

```
fail: Program[0]
      Failed to process logic block IntroProbe.WiringProbe
      System.NotSupportedException: Struct 'IntroProbe.Empty' has no positional constructor.
      Only positional readonly record structs are supported as service-element types.
         at Vion.Dale.Sdk.Introspection.TypeRefBuilder.BuildStructTypeRef(Type) in …\TypeRefBuilder.cs:line 294
         at Vion.Dale.Sdk.Introspection.TypeRefBuilder.Build(Type, Boolean) in …\TypeRefBuilder.cs:line 205
         …
         at Vion.Dale.LogicBlockParser.Program.RunParser(String[]) in …\Program.cs:line 149
EXITCODE=1
```

`ls out-w.json` after that run: *No such file or directory*.

**The development-only filter (row 11)**:

```
Vion Dale: 1 logic block(s) are development-only — not part of the production artifact:
Vion Dale:   IntroProbe.DevOnlyProbe — binds Face (DigitalOutputProvider)
Vion Dale: The assembly is packed unchanged; only the introspection JSON the cloud reads is filtered.
```

`DevOnlyProbe` is present in the plain run's block list and absent from the filtered one; every other
block is byte-identical between the two runs.

**Nested struct annotations (row 38)** — `Branch(Leaf Child, double Amp)` over
`Leaf(double Volt, InnerSev State)`, where `Volt` carries
`[StructField(Title = "Leaf title", Unit = "V")]` and `InnerSev` carries labels and severities:

```
"child": { "type":"object","title":"Leaf",
           "properties": { "volt":  {"type":"number","format":"double"},   <- no title, no x-unit
                           "state": {"type":"string","title":"InnerSev","enum":["Ok","Nok"]} } }
"presentation": { "displayName":"Nested struct", "fields": { "child": {"displayName":"Branch title"} } }
```

**Dual annotation and the two streams (rows 26, 27, 64)**:

```
P Dual  schema : {…,"title":"Dual title","description":"Dual description","x-unit":"kW",
                    "readOnly":true,"x-kind":"measurement"}
        runtime: {"throttle":{"minInterval":"1s"}}
M Dual  schema : {…,"title":"Dual title","description":"Dual description","x-unit":"kW",
                    "readOnly":true,"x-kind":"measurement"}
        runtime: {"throttle":{"minInterval":"5s"}}
```

**Severities through a nullable versus through an array (row 49)**:

```
SevArray    S: {"type":"array","items":{"type":"string","title":"ArraySev","enum":["Good","Bad"]}}
            P: {"displayName":"Sev array","uiHint":"statusIndicator","enumLabels":{"Good":"Fine"}}
NullableSev S: {"type":["string","null"],"title":"ArraySev","enum":["Good","Bad",null]}
            P: {"displayName":"Nullable sev","uiHint":"statusIndicator",
                "statusMappings":{"Bad":"error","Good":"success"},"enumLabels":{"Good":"Fine"}}
```

**Edge values (rows 41–42, 45–46, 51, 53)**:

```
EmptyGroup   P: {"group": ""}
Sentinels    P: {"displayName":"","uiHint":"","format":""}    <- order / decimals / importance absent
EmptyStrings S: {"type":"integer","format":"int32","title":"","description":"","x-unit":""}
Labels       P: {"displayName":"Edge labels","enumLabels":{"EmptyLabel":"","SpaceLabel":"   "}}
Inverted     S: {"type":"number","format":"double","title":"Min above max","minimum":10,"maximum":1}
```

### Step 8 — every existing test in scope, mapped

**119 test methods across 7 test classes in 5 files** at extraction (`RichTypesLogicBlock.cs` is a
fixture, not a test class) plus the introspection-level golden assertion. The **today** column is the
same `[TestMethod]` count after Phase B and the two amendments rewrote the suite — **154**.

| Class | Tests (extraction → today) | Rows |
| --- | --- | --- |
| `TypeRefBuilderShould` | 37 → 33 | 28, 31, 33–35, 39–40, 47, 50, 54 |
| `LogicBlockIntrospectionShould` | 36 → 75 | 11, 13, 16–18, 29, 34, 44, 47–48, 58, 67, 71–75 |
| `PropertyMetadataBuilderShould` | 12 → 12 | 44, 61, 64 |
| `ContractCarriedServiceRelationsShould` | 12 → 12 | 20–21, 66, 70, 77–82 |
| `ConfigGatingMetadataShould` | 8 → 8 | 31, 65 — **`GATE`'s criteria throughout**; the class is rewritten in place and keeps every `AC-GATE-010.*` and `AC-GATE-005.9` citation |
| `StructFieldPresentationShould` | 8 → 8 | 55–58 |
| `LogicBlockIntrospectionOrderingShould` | 6 → 6 | 24, 26, 64 |

**Unmapped tests: 0.** Every test in scope maps to a row. The table scheduled three merges for Phase B,
none a deletion; all three are done, and the amendment-2 checkpoint below records what each became:

- `TypeRefBuilderShould.EmitsPrimitiveSchemaForBool` … `ForUInt` (6 near-identical cases) → one
  data-driven case per primitive kind under row 33.
- `LogicBlockIntrospectionShould.ReadGroupAnnotation` and `ReadDisplayGroupAnnotation` assert the same
  key from two fixtures → one case under row 44.
- `TypeRefBuilderShould.EmitsStructFieldAnnotationsForNullableStruct` and
  `EmitsIdenticalFieldSchemasForNullableAndNonNullableStruct` overlap on row 35's nullable half.

**Mapped, not rewritten** (other areas' suites, cited from this page's prose only):
`Vion.Dale.Sdk.Modbus.Tcp.TestKit.Test/ModbusDiagnosticsIntrospectionShould.cs` (`MODB`),
`Vion.Dale.Cli.Test/Models/DalePluginInfoDeserializationTests.cs` (`CLI`, Tier B — row 87), the nine
presentation-analyzer suites in `Vion.Dale.Sdk.Generators.Test/` (`ANLZ` — rows 49, 52), and the
DevHost definition and catalog tests (`CTRL` — rows 89, 90).

### Step 9 — self-check

- **A1 schema fields**: 36 enumerated, **36 visited** — envelope 4 (rows 1–4); block record 5 (rows
  5, 14, 16–18, with `Interfaces` / `Contracts` / `Services` as rows 20–24 and 66–82); interface 4
  (rows 66, 70–73); contract 4 (rows 67, 74–75); service 7 (rows 20–24, with `IncludedWhen` cited to
  `AC-GATE-010.1`); property and measuring point 4 + 4 (rows 25, 28, 43, 63); relation 4 (rows
  77–80). **Corrected in amendment 2:** the interface record's two type-name fields were visited by
  row 70 and stated by nothing — this line claimed the field set covered when the *page* did not carry
  it. `AC-INTRO-015.1` states it now, and the same correction applies to the relation record's
  interface type (`AC-INTRO-016.7`) and to the mandatory schema field (`AC-INTRO-007.1`): a field is
  visited by a row and covered by a criterion, and this self-check had conflated the two.
- **A2 sibling-model fields**: 28 enumerated, **28 visited** — `TypeAnnotations` 9 (rows 29–32, 42),
  `Presentation` 11 (rows 44–48, 50, 53–56, 59), `RuntimeMetadata` and `ThrottleMetadata` 5 (rows
  63–65, cited to `AC-EMIT-013.*`), `TypeSchema` and the `TypeRef` shapes and `StructField` (rows
  33–35, 39).
- **A3 producer**: 5 files, **all visited**. `MockActorContext.cs` has **no row of its own** — it is
  the null actor context the definition view runs on, and no behavior of the document distinguishes
  it from any other context; its one throwing method (`LookupByName`) is never called from a binder.
  Recorded here rather than minted as a row.
- **A4 attribute members**: 62 enumerated, **62 visited**. Two anchor kinds came back empty and are
  Drift checkpoints rather than rows.
- **A5 diagnostics**: 15 enumerated, **2 of them cited** — `DALE024` (row 49) and `DALE045` (rows 79,
  81). Five descriptors from *outside* the set are cited as the compile-time half of a row:
  `DALE001` (76), `DALE003` (33), `DALE016` (33, 37, 38), `DALE022` (32), `DALE025` (29). The
  thirteen in-set descriptors with no citation — `DALE013`–`DALE015`, `DALE021`, `DALE023`,
  `DALE026`–`DALE028`, `DALE032`, `DALE033`, `DALE040`, `DALE041`, `DALE042` — police *authoring*,
  and each names a hint the producer emits verbatim (rows 44, 53) that a renderer then ignores.
  They belong to `ANLZ` in full; recorded so that pass does not read the omission as a gap.
- **A6 constants**: 29 enumerated, **all visited** as the subject of rows 44, 47 and 53
  (`PropertyGroup.None` is the one whose wire form surprises).
- **A7 parser command line**: 2 arguments, 1 option, 2 exit codes, 3 refusals, 1 prefix, 1
  environment variable — **all visited** (rows 8–12; `DALE_PARSER_VERBOSE` in row 7's evidence).
- **A8 pack hook**: 3 targets, 1 opt-out, 1 analyzer group — **all visited** (rows 83–86).
- **A9 PublicApi manifest**: 119 types scanned, **32 in this area, all visited** through their members
  in A4. The producer's own types are absent from the manifest by design (Q4).
- **A10 consumers**: 11 walked, **8 with rows** (87–90, plus rows 2, 27, 68 and 88, each naming its
  reader by `file:line`). The two Tier C examples and `predicates.js` carry no criterion of their own
  — `predicates.js` evaluates `visibleWhen` against the vendored conformance vector and is the client
  half of row 44, and the examples reference a published package so they cannot prove a same-PR fix.
- **Rows**: **90 total** — **80 `intended`**, **5 ⚠ `fix`** (6, 30, 52, 68, 69), **4 ⚠ `park`**
  (2, 14, 27, 88), **1 `out-of-spec`** (59). **41 rows carry `GAP`**; 49 have a test today. Counts
  recomputed from the committed table, not tallied by hand.

---

## Drift checkpoints

> One line per divergence discovered during implementation:
> `YYYY-MM-DD: <what changed and why>`. Never inline in a spec page. A checkpoint that fixes a
> CLASS bug states the sibling sweep (done / N/A / handed off).

- 2026-09-02: The brief lists `Vion.Dale.Sdk.Test/Introspection/` as "six classes". Counted this
  session: **six `[TestClass]`es in five files** — `PropertyMetadataBuilderShould.cs` carries two
  (`PropertyMetadataBuilderShould` and `LogicBlockIntrospectionOrderingShould`) and
  `RichTypesLogicBlock.cs` is a fixture with no test. The brief is right on classes and would have
  been wrong on files.
- 2026-09-02: `docs/testing-conventions.md` § 4 names `Vion.Dale.DevHost.Test/Golden/` as an
  introspection-level golden assertion. Verified: the folder holds `feature-rig.topology.json` and
  `feature-tour.scenario.json`, and `GoldenRegressionShould` PUTs each to the DevHost API and reads
  it back — a scenario/topology round trip, which is `SCEN`'s. The brief's hedge holds; § 4 is
  corrected in this PR to name only `ContractCarriedServiceRelationsShould`.
- 2026-09-02: Two of the brief's anchor kinds came back **empty**, and are checkpoints rather than
  rows: `ServiceInterfaceAttribute` declares no named parameter at all (a bare marker), and
  `TimerAttribute`'s `Identifier` and `IntervalSeconds` reach no field of the introspection document
  — `DeclarativeTimerBinder` registers timers with the configuration builder and
  `LogicBlockIntrospection` reads none of them back.
- 2026-09-02: The brief scopes "the DALE descriptors whose subject is a presentation or identifier
  rule". Fifteen match; **two** earn a citation (`DALE024`, `DALE045`), and five descriptors from
  outside the set are cited instead as the compile-time half of a row (`DALE001`, `DALE003`,
  `DALE016`, `DALE022`, `DALE025`). The thirteen in-set descriptors left uncited police an authoring
  mistake whose wire consequence is "the producer emits it and a renderer ignores it", which this
  page states as the emission rule rather than as a diagnostic. Recorded so `ANLZ`'s pass does not
  read the omission as a gap.
- 2026-09-02: The brief says the reference sweep is "on the order of thirty-five living sites".
  Counted before starting: **34** living sites for RFC 0017 / RFC 0019 outside the two RFCs
  themselves, and **22** living links to `docs/identifier-stability.md`. One of the 34 is a
  still-living RFC (`docs/rfcs/0020-contract-pairing.md`) — re-pointed with one line, not absorbed —
  and one is a dated retro record (`docs/retro/2026-08-12-review-mining-round.md`), which is history
  and is left alone. `Vion.Dale.DevHost.Web/wwwroot/predicates.js` and `components.js` are **not**
  vendored (the vendored file is `predicate-conformance.json`, whose two copies stay untouched), so
  their comments are in the sweep.
- 2026-09-02: `Vion.Dale.Cli/Models/DalePluginInfo.cs` is named in `docs/sdk-surface-conventions.md`
  § Known non-conforming code as a hand-maintained mirror. Checked field by field against
  `Vion.Contracts` 3.7.0 this session: **no drift** — all 8 types and all 36 fields present, every
  member optional as the doc comment claims. Brief point 5's fork does not arise; the two CLI defects
  found are in the *readers*, not the mirror (row 88).
- 2026-09-02: The brief's edge-value sweep asks for "a field whose constructor-parameter name differs
  from its property's". Not expressible: a positional record struct's property takes its
  constructor parameter's name by construction, so the two cannot differ. Recorded as N/A rather than
  left as a silent omission.
- 2026-09-02: The brief's edge-value sweep asks for "a recursive struct". Not expressible either — a
  value type cannot contain itself. The nested case is row 38.

### Phase B — after the operator's classification

- 2026-09-02: **Row 14 reversed.** Phase A read the block identity's `+` as an inconsistency with every
  other type name in the document and recommended `park`. The operator's classification restates it as
  `intended`: the block's type name is the identifier a host *loads the type by*, so it must be the CLR
  spelling, while every other type name is descriptive and takes the display form. `AC-INTRO-004.1` and
  `AC-INTRO-004.2` state the split. No code change on the block's own identity.
- 2026-09-02: **The test written for the restated row 14 found a defect the extraction missed.** The
  service-interface list spelled the same field two ways: the property half used `Type.FullName` and the
  measuring-point half `GetDisplayFullName` (`LogicBlockIntrospection.cs:346`–`:355`), so one nested
  service interface reached the document as `Outer+IReading` through a property binding and as
  `Outer.IReading` through a measuring point — one field, two spellings, decided by which member kind
  happened to bind. Both halves take the display form now, which is `AC-INTRO-004.2` made true rather
  than a new proposal. `ReportServiceInterfaceTypeNamesInTheirDisplayForm` is red on the pre-fix code.
  **Sibling sweep: done** — the remaining `Type.FullName` reads in the producer are the block identity
  (`AC-INTRO-004.1`, deliberate) and exception messages.
- 2026-09-02: **Row 30's shape reaches one site this pass does not own.**
  `Vion.Dale.DevHost/Topologies/LogicBlockDefinition.cs:149`,`:154` tests an instantiation parameter's
  editor bounds with `double.IsInfinity`, which a `NaN` passes, and then casts to `long`. Same one-sided
  shape, different area's field (`AC-GATE-010.6`) and different area's suite. **Sibling sweep: handed
  off** — one ledger line, `CTRL`'s. The two sites this page owns are both fixed.
- 2026-09-02: **Q3 answered yes, and cheaply.** The spike was time-boxed at 30 minutes and took under
  it: two `netstandard2.1` fixture libraries `ProjectReference`d into `Vion.Dale.LogicBlockParser.Test`
  land beside the whole SDK closure in that project's output directory, which is the single directory
  the parser's load context needs — no publish step in the test at all. The test shells out to the
  parser's own build output, because a project reference does not copy an executable's
  `runtimeconfig.json`. 16 tests, 3 s. The unregistered-block case needs its **own** assembly, because
  after row 6's fix a plugin carrying one fails the whole run — which is precisely what that test
  asserts.
- 2026-09-02: **The `--package-id` sweep found two invocation sites, not one.** The brief named the
  targets and the CLI's parser runner; both are the whole set — `RunParserDll` is the only place in the
  repository that spawns the parser, and `Vion.Dale.Sdk.targets` the only place MSBuild does.
- 2026-09-02: **Rows 68 and 69's compile-time half is parked, per the size guard.** The amendment
  allowed an analyzer rule for a blank or colliding `Identifier =` "only if it fits the size guard". It
  does not: a collision check is a whole-type analysis across two attribute families and two
  declaration levels, which is `DALE043`/`DALE044`-sized work in `ANLZ`'s area. The bind-time refusal
  ships; the diagnostic is `ANLZ`'s.
- 2026-09-02: **The style gate's rename round cost 18 names**, exactly the budget the skill warns about
  — the natural phrasing of an assertion carries an article (`OmitABoundThatIsNotFinite`,
  `RefuseAPluginPathThatDoesNotExist`). Renamed before the REPORT, not after.
- 2026-09-02: **`spec-trace` reads the `GAP` marker line-wise**, so a criterion carrying one is a single
  long line on the page, and a criterion whose id also appears in ordinary prose loses the exemption.
  `AC-INTRO-017.3` failed the gate on the second rule until the prose stopped repeating its id.
- 2026-09-02: **Twelve of the pass's own tests pin the presentation attribute's default values rather
  than a criterion.** They are premise tests — the sentinels `AC-INTRO-009.3` and `AC-INTRO-009.4` rest
  on — and stay uncited by design, per `docs/spec-process.md`'s category. Said so in
  `PresentationAttributeShould`'s class summary; listed in the REPORT.

### Tier 2 — the SPA change, demonstrated

The reference sweep edited one string the SPA **renders**: the `visibleWhen` badge's tooltip in
`Vion.Dale.DevHost.Web/wwwroot/components.js` lost its `RFC 0017:` prefix. A live `SmokeHost` on the
`default` topology, stepped, read back through the browser:

| Question | Answer from the running host |
| --- | --- |
| The badge's rendered tooltip, off `ShowcaseBlock.PrimaryCurrentToWriteA`'s expanded docs | `{"cls":"badge visiblewhen","text":"visibleWhen: DirectMeasurement == false","title":"shown only while this predicate holds (evaluated live against sibling properties)"}` |
| The SPA's own evaluator against the vendored conformance vector | `{"passed":true,"failures":[]}` |
| The live hide/show still reactive (`DirectMeasurement` off → on → off, reading `hidden-importance` on both CT rows) | `before [false,false]` · `afterOn {checked:true, hidden:[true,true]}` · `afterOff {checked:false, hidden:[false,false]}` |

`predicates.js`'s header comment lost the same reference; the vector self-test above is what proves
the evaluator it describes is untouched. Both `predicate-conformance.json` copies are vendored and
were not edited.

### The test → mutation list

Every row measured this session: the mutation applied, the suite run, the failures read, the mutation
reverted. Each mutation reddens the tests named beside it **and no others**.

| Test | Mutation that reddens it |
| --- | --- |
| `LogicBlockIntrospectionShould.OmitNonFiniteBound` | `PropertyMetadataBuilder.FiniteBound` returns `declared` unchanged |
| `…OmitNonFiniteStructFieldBound` | `TypeRefBuilder.FiniteBound` returns `declared` unchanged |
| `…SerializeDocumentDeclaringNonFiniteBound` | **over-determined**: either `FiniteBound` reverted reddens it, and both were measured separately |
| `…ReportMeasuringPointKindOnMeasuringPointStreamOnly` | drop the `stream == ServiceElementStream.MeasuringPoint` conjunct |
| `…RefuseBindingWhoseIdentifierBlank` (both rows) | `BindingIdentifiers.Claim`'s blank guard made unreachable |
| `…RefuseTwoInterfaceBindingsResolvingToOneIdentifier`, `…RefuseTwoContractBindingsResolvingToOneIdentifier` | `BindingIdentifiers.Claim`'s collision guard made unreachable |
| `…RefuseBindingWhoseIdentifierBlank` (contract row), `…RefuseTwoContractBindingsResolvingToOneIdentifier` | the contract binder's `Claim` call removed — the second, independent mutation proving both binders route through the rule |
| `…ReportServiceInterfaceTypeNamesInTheirDisplayForm` | the property half of `GetServiceInterfaceTypeFullNames` back to `Type.FullName` (this is the row's red-before-fix proof) |
| `PresentationAttributeShould.BeDeclarableOnPropertiesOnly` | `AttributeTargets.Property` back to `Property \| Method` |
| `LogicBlockParserShould.ReportSuppliedPackageIdentity` | `ResolvePackageId` always returns the assembly name |
| `…FallBackToAssemblyNameWhenSuppliedPackageIdentityBlank` | `ResolvePackageId` drops its blank check (`suppliedPackageId ?? assemblyName`) — this test was **green** on the pre-fix code, so its own mutation is what proves it |
| `…RefusePluginWhoseConcreteBlockUnregistered` | the `return 1` removed, restoring log-and-continue |

The eight SDK tests and the two parser tests above were run **red against the pre-fix code** before
their fix — eight failures out of 58 in the introspection class, two out of 16 in the parser class,
and nothing else in either.

**Amendment 2's additions.** Same discipline: applied, run, read, reverted.

| Test | Mutation that reddens it |
| --- | --- |
| `StructFieldAttributeShould.BeDeclarableOnConstructorParametersOnly` | `AttributeTargets.Parameter` back to `Parameter \| Property` |
| `LogicBlockIntrospectionShould.CarryEmptyAuthoredStructFieldTitle` | the title test back to `is { Length: > 0 }` |
| `…ReportSchemaForEveryMemberOfBothKinds` | a measuring point's `Schema` emitted as `null!` — **over-determined**: it reddens seventeen tests, because seventeen presuppose it |
| `…MintSeparateIdentifierNamespacesForContractAndInterfaceBindings` | `BindingIdentifiers.NamespacedKey` returns the bare identifier |
| `…DistinguishEndpointIdentifiersDifferingOnlyInCase` | the block's identifier namespace built `OrdinalIgnoreCase` |
| `…RefuseTwoInterfaceBindingsResolvingToOneIdentifier` | a class-level binding named by the block instead of by its interface |
| `…ReportNoRelationHalfForComponentPropertyHoldingNull` | the service list joined with the relation keys, so a half revives its service |
| `…ReportComponentServiceMembersInBaseToDerivedDeclarationOrder` | every member's position read as `int.MaxValue` (also reddens `EmitPropertiesInBaseToDerivedOrder`, which is the same rule on the root service) |
| `…ReportArrayElementNullability` (nullable row), `…ReportArrayElementNullabilityAtEveryNestingDepth` | the array element built with a fresh, empty nullability walk |
| `LogicBlockParserShould.RefuseUnregisteredBlockEvenWhenExclusionWouldDropIt` | the unregistered guard reading `&& !excludeDevelopmentOnly` |
| `…IntrospectNoAbstractLogicBlock` | the `!type.IsAbstract` filter dropped |
| `…RefuseMissingPluginPath` | the refusal no longer naming the path |

**Amendment 3's additions.** Same discipline: applied, run, read, reverted. Counts pasted from the
runs — the SDK class is 504 tests, the parser class 18.

| Test | Mutation that reddens it |
| --- | --- |
| `LogicBlockIntrospectionShould.ReportInterfaceBindingTypeAndItsMatchingCounterpart` (the logic-interface half) | `InterfaceTypeFullNames` built from `MatchingLogicInterfaceType` — 1 failed, 499 passed |
| `…ReportInterfaceBindingTypeAndItsMatchingCounterpart` (the matching-counterpart half) | `MatchingInterfaceTypeFullNames` built from `LogicInterfaceType` — 1 failed, 499 passed |
| `…ReadBlockLevelAnnotations` | the `annotations["Groups"]` write deleted — 1 failed, 499 passed |
| `TypeRefBuilderShould.EmitIdenticalFieldSchemasWhateverStructNullability` | the `Nullable<T>` peel dropped from `TypeRefBuilder.ExtractStructType` — 2 failed, 498 passed: **over-determined** with the merged test's nullable row, which is the same claim from seven named keys |
| `LogicBlockParserShould.IntrospectNoAbstractLogicBlock` | re-proven after its rewrite: the `!type.IsAbstract` filter dropped — 1 failed, 17 passed; and the registration refusal short-circuited — 3 failed, 15 passed, this one among them, where the pre-round form passed that same mutation (1 of 1), which is what the added assertions buy |

### Gates

Every line pasted from the terminal.

```
Build succeeded.
    24 Warning(s)
```

All 24 are `NU1900` from the unreachable private feeds; `dotnet build … | grep warning | grep -v NU1900`
returns nothing, so the SDK's own build carries zero `DALE` warnings.

```
Passed!  - Failed:     0, Passed:   488, Skipped:     4, Total:   492, Duration: 1 s - Vion.Dale.Sdk.Test.dll (net10.0)
Passed!  - Failed:     0, Passed:    16, Skipped:     0, Total:    16, Duration: 4 s - Vion.Dale.LogicBlockParser.Test.dll (net10.0)
```

28 test assemblies pass, none fails (`dotnet test Vion.Dale.Sdk.sln`).

```
spec-lint: OK
spec-trace: OK - 251 id(s) all referenced by tests (4 traced page(s); 6 GAP id(s) awaiting tests: AC-EMIT-002.4, AC-EMIT-009.4, AC-GATE-012.5, AC-INTRO-017.1, AC-INTRO-017.2, AC-INTRO-017.3)
test-style-lint: OK - 347 cited test(s) conform (88 file(s) in exempt projects skipped)
doc-comment-lint: OK - 2729 doc block(s) in 841 file(s), none carries a second <summary>
run-script-tests: OK - 5 self-test(s) passed; 7 script(s) exempt
```

```
cleanupcode applied changes - review with 'git diff' and commit:
  modified vs HEAD (a local run also lists edits you made yourself):
 .../ProbeBlocks.cs                                 |  24 ++--
 .../UnregisteredBlocks.cs                          |  14 +-
 .../LogicBlockParserShould.cs                      |  12 +-
 Vion.Dale.LogicBlockParser/Program.cs              |   4 +-
 .../Introspection/LogicBlockIntrospectionShould.cs |   5 +-
 .../TestHelpers/IntrospectionBlocks.cs             | 155 +++++++++++----------
 Vion.Dale.Sdk/Configuration/BindingIdentifiers.cs  |   2 +-
 .../Introspection/LogicBlockIntrospection.cs       |   7 +-
 .../Introspection/PropertyMetadataBuilder.cs       |  14 +-
 Vion.Dale.Sdk/Introspection/TypeRefBuilder.cs      |  21 +--
 10 files changed, 141 insertions(+), 117 deletions(-)
```

Stryker.NET was not run: `Vion.Dale.Sdk.Test` references four mutatable projects, which the runner
cannot handle.

### Amendment 2 — after the completeness critic and `/vion-code-review`

**The id holes the REPORT explained and the doc did not (item 3).**

- 2026-09-03: `AC-INTRO-002.2` does not exist because it was **merged into `AC-INTRO-002.1`**. It would
  have said "a refused run writes no document", and the same mutation — the `return 1` removed — reddens
  both halves, so it was not a second requirement. `AC-INTRO-002.1` carries the clause.
- 2026-09-03: `AC-INTRO-007.1` was left as prose in Phase B on the reasoning that every schema criterion
  presupposes it. That reasoning lived only in the REPORT, which is not a durable record, and it was
  half wrong: a mutation *does* exist — a member kind's schema emitted as null — and it reddens the
  criterion's own test. It is minted. The mutation reddens seventeen tests, because seventeen tests
  presuppose it; the criterion is **over-determined** and is kept, as the binary rule requires.
- 2026-09-03: `AC-INTRO-015.1` was a genuine miss, not a decision. The step-9 self-check claimed the
  interface record's four fields visited while nothing stated the two type-name fields, and the test that
  asserts one of them cited only the empty-annotation criterion. Minted, cited from that test and from a
  new one over the endpoint shape. The self-check's counts below are corrected with it.

**The critic's misses, and what each cost.**

- 2026-09-03: **M1** — `[StructField]` was declarable on a property, which no reader walks and no
  diagnostic judges: row 52's shape one level down. Narrowed to `AttributeTargets.Parameter`
  (`AC-INTRO-008.8`). The amendment's verification said nothing in this repo writes it there; that was
  true of the literal `[property: StructField]` spelling and **not** of a derived preset —
  `AttributeInheritanceShould.Subject.TestStructFieldProperty` carried one on a plain property. It was a
  carrier for an attribute-inheritance assertion with no wire meaning, and it now carries the same
  assertion on a positional constructor parameter, where the attribute is read.
- 2026-09-03: **M3** — a component service's members came out in binder-insertion order, because every
  service looked its members up in the *block's* position map. The map is a pure function of a type; the
  fix is to look each member up under the type that owns it, cached per type for the one introspection.
  `AC-INTRO-003.3` is kept as written and is now true of every service. `ReflectedType` is the
  component's concrete type for every binding the binders produce, so the brief's stop-and-record branch
  did not arise. **Relay note:** a component service's member order changes once.
- 2026-09-03: **M4** — an array element's reference nullability was hardcoded absent, so
  `ImmutableArray<string?>` reached the wire with a non-nullable item schema and the outbound codec
  refused every null element. `NullabilityInfoContext` does not exist on `netstandard2.1`, which is why
  the builder reads the compiler's flag bytes itself; those bytes are **one flag per position of a
  pre-order walk of the member's type**, so the reader walks the same positions the type does. Nested
  arrays fall out of the same walk and are tested. **Relay note:** the item schema of an array with
  nullable elements now says so. One half of the brief's test list is **not expressible**: a struct field
  cannot be an array at all — `DALE003`/`DALE016` restrict a flat struct's fields to primitives, enums,
  strings and nullables of those — so there is no struct-field-array shape to assert. The parameter entry
  point is exercised by the existing `string?` field tests.
- 2026-09-03: **M5** — an authored struct-field title that is empty was dropped at the one place a title
  changes documents, while the same empty title on a scalar field landed inline. Carried
  (`AC-INTRO-011.5`).
- 2026-09-03: **M6** — the two binding kinds minted into separate namespaces **by accident**: each binder
  happened to allocate its own dictionary, so nothing expressed the rule and no mutation could reach it.
  One namespace per block now, with the binding kind in the key, so `AC-INTRO-014.5` is a line a reviewer
  reads and a mutation deletes. Case-sensitivity is the other half of the same criterion.
- 2026-09-03: **M8's test was vacuous when first written, and the mutation is what caught it.** The
  fixture edit that gives the forgotten block a development-only binding silently matched nothing — the
  style cleanup had reordered that class's members after the edit was written — so the test asserted an
  unregistered block fails the run without the block being development-only at all, which is
  `AC-INTRO-002.1` again. The provider face is on the fixture now, and the mutation that reddens the test
  is the guard reading `!excludeDevelopmentOnly`. A `str.replace` with no assertion is how a fixture edit
  can be applied, reported and absent.
- 2026-09-03: **M9** — a component property holding null contributes no service, so its endpoint's
  relation half is registered and then dropped by the join. `AC-INTRO-016.8` states it. The first test
  written for it passed under every mutation of the join, because it asserted only that no *service*
  appeared; it asserts that no *half* names the endpoint now, which is what the criterion claims.
- 2026-09-03: **M7 → parked** by the operator (`CLI`, Tier B): `dale list` runs the introspection without
  the development-only exclusion, so it lists blocks the packed artifact omits. The page's `dale list`
  sentences say so rather than implying the artifact's contents.

**Conventions.**

- 2026-09-03: **A UTF-8 BOM had been prepended to 46 files that had none on `main`** — every file this
  pass touched with a Python helper. The cause is `io.open(..., encoding='utf-8-sig')`, which *strips* a
  BOM on read and *writes* one on write; it was chosen to read the files that legitimately carry one.
  Stripped from all 46; the files that already carried one on `main` are untouched. Two of them
  (`TestAttributeStubs.cs`, `DeclarativeServiceBinder.cs`) had differed from `main` by the BOM alone and
  now show their one real line each. Every helper in this round reads and writes with `newline=''` and
  plain `utf-8` instead.
- 2026-09-03: `TypeRefBuilderShould` had been brought to § 17 and not to § 12 — 34 names in the
  third-person form that does not make a sentence with the class, several naming the collaborator rather
  than the outcome. The lint gates articles only, so nothing caught it. All renamed.
- 2026-09-03: The three merge candidates the Phase A table scheduled are done: five primitive cases into
  one `[DataRow]` set, the two group-annotation cases into one, and the nullable/non-nullable struct pair
  into one — the last of which is also `AC-INTRO-008.9`, whose claim the pair was making without stating.
  The merged `[DataRow]` form asserts seven named keys per member and not the two documents against each
  other, so it does not carry `008.9`'s own sentence; amendment 3 restores that comparison beside it.
- 2026-09-03: Four duplicated citations removed. They came from running the citation pass twice: the
  first run renamed the methods it cited, so the second run's map missed them and it re-cited the rest.

**Judgments.**

- 2026-09-03: `SerializeDocumentDeclaringNonFiniteBound` asserted `IsNotNull(…ToJsonString())`, which only
  a throw can fail. It asserts the serialized text now — the finite bound present, no `NaN` and no
  `Infinity` anywhere in it.
- 2026-09-03: `RefuseTwoInterfaceBindingsResolvingToOneIdentifier` asserted the block's name, which the
  message's own "on logic block 'X'" clause satisfies alone. The refusal now names a class-level binding
  by the interface it binds — a class-level declaration has no member name, and repeating the block's
  told a reader nothing — and the test asserts both declarations.
- 2026-09-03: `LogicBlockParserShould` wrote a GUID-named document per test into the build output and
  removed none. One temp directory per run, removed in `[ClassCleanup]`.
- 2026-09-03: `SkipVionDaleLogicBlockParser` on both fixture plugins was inert — a `ProjectReference`
  never imports the SDK's `build/` targets, so the hook it suppresses was never going to fire. Removed
  rather than re-explained.
- 2026-09-03: `AC-INTRO-002.1`'s "non-abstract" had no fixture. The unregistered plugin carries an
  abstract block now, also unregistered, and the mutation "drop the `IsAbstract` filter" reddens the test
  that says its absence is not a refusal.

### Amendment 3 — the fresh-session fix-up

The round after the two amendment rounds, run from fresh context because the same defect classes had
recurred twice (a criterion cited for text it does not state, a stale count, a claim outrunning its
evidence). **No criterion's text moved this round, so the *Spec delta* gains no `Amendment 3`
heading** — every page edit below is prose or ordering.

- 2026-09-03: `AC-INTRO-015.1` was cited by a test that does not prove it.
  `ContractCarriedServiceRelationsShould.EmitEndpointLogicInterfaceTypeAndEmptyAnnotationBag` reads a
  *relation half's* `InterfaceTypeFullName` and its empty annotation bag — `AC-INTRO-016.7` and
  `AC-INTRO-016.4`, which it also cites — while `015.1` is about an *interface binding's* two type
  lists. Amendment 2's item 1 asked for the citation on a wrong reading of that assertion; the tag is
  removed. `015.1`'s one proving test is
  `LogicBlockIntrospectionShould.ReportInterfaceBindingTypeAndItsMatchingCounterpart`, which the
  mutation ledger did not name at all — it does now, with one mutation per half.
- 2026-09-03: `AC-INTRO-008.9` said more than its test proved. The merge of the nullable/non-nullable
  pair kept the seven named per-field keys and dropped the one assertion that is the criterion's own
  sentence: the two members' `properties` documents compared whole. Restored beside the merged test as
  `EmitIdenticalFieldSchemasWhateverStructNullability`. Its mutation is **over-determined** with the
  merged test's nullable row — the only branch in the producer that can make the two documents diverge
  is the `Nullable<T>` peel in `TypeRefBuilder.ExtractStructType`, so one mutation reddens both. The
  restored test still earns its place: it is the only one that fails when a key neither row names
  (a field `title`, `type`, `format`, or `lon`'s description) diverges.
- 2026-09-03: `AC-INTRO-004.3`'s "and SHALL report its icon and its groups under their own" had no
  assertion behind the groups clause. `TestLogicBlock` declares `Groups` now and
  `ReadBlockLevelAnnotations` asserts them; the mutation is the `annotations["Groups"]` write deleted.
  The same test's `AC-INTRO-004.4` citation is dropped with it: it asserts three annotations *present*
  and no omission at all, which is `OmitBlockAnnotationsDeclaredEmpty`'s claim and only its.
- 2026-09-03: One incident-narrative sentence survived amendment 2's item 29 — the `AC-INTRO-007.3`
  paragraph still ended on what a non-finite bound "used to" do. Rewritten to what the producer does
  today; the history is here, above.
- 2026-09-03: The step-8 map's `TypeRefBuilderShould | 37` was the extraction-time count read as if it
  were current, and the merges had taken the class to 33. Every row now carries both counts, recounted
  with `grep -c '^\s*\[TestMethod\]'` per file — 119 at extraction, 154 today — and the header's
  "6 test classes" is 7, which is what the table has always listed and what its own row sum needs.
- 2026-09-03: The merge paragraph under that table still announced the three merges in the future
  tense. It points at the amendment-2 checkpoint that records them, and that checkpoint now says what
  the third merge cost `AC-INTRO-008.9`.
- 2026-09-03: `LogicBlockParserShould.IntrospectNoAbstractLogicBlock` asserted only that the run's
  output does not name the abstract block, which a run that died before the registration check
  satisfies too. It asserts the exit code and the named concrete block beside it. Both forms were run
  against the same mutation — the registration refusal short-circuited: the pre-round form passed
  (1 passed of 1), the strengthened form fails (3 failed, 15 passed, this test among them).
- 2026-09-03: Both fixture plugins' `.csproj` kept a blank line before `</PropertyGroup>` where
  `SkipVionDaleLogicBlockParser` had been removed.
- 2026-09-03: `AC-INTRO-002.9` sat before `AC-INTRO-002.8` on the page. Numeric order.
- 2026-09-03: The merged `EmitStructFieldAnnotationsWhateverMemberNullability` carried a before/after
  comment ("the nullable wrapper once stripped the field metadata"), which
  `docs/comment-conventions.md` refuses for the same reason the page does. Stated in the present tense.
- 2026-09-03: The journal's amendment-2 line claimed M6 and M9 both became "one line a reviewer reads
  and a mutation deletes". True of M6 only; M9 moved no production line. Corrected there.

---

## Spec delta (to distill)

> One line per id, applied into `docs/specs/introspection.md`. Every line's text is the page's own
> text: the list below was generated from the page, so the two cannot disagree.

- ADDED AC-INTRO-001.1 -> docs/specs/introspection.md : WHEN a plugin assembly is introspected THE SYSTEM SHALL emit one document carrying the package identity, the package version and one record per logic block.
- ADDED AC-INTRO-001.2 -> docs/specs/introspection.md : WHEN the pack supplies a package identity THE SYSTEM SHALL report it as the document's package identity, and SHALL report the plugin assembly's simple name where no identity is supplied or the supplied one is blank.
- ADDED AC-INTRO-001.3 -> docs/specs/introspection.md : WHEN the plugin assembly declares an informational version THE SYSTEM SHALL report it with any build-metadata suffix removed.
- ADDED AC-INTRO-001.4 -> docs/specs/introspection.md : THE SYSTEM SHALL report an empty annotation map at document level.
- ADDED AC-INTRO-001.5 -> docs/specs/introspection.md : THE SYSTEM SHALL introspect every non-abstract logic block of the plugin assembly, ordered by full type name.
- ADDED AC-INTRO-001.6 -> docs/specs/introspection.md : THE SYSTEM SHALL write the document with camelCase member names and camelCase annotation keys, and every enum value as its member name.
- ADDED AC-INTRO-002.1 -> docs/specs/introspection.md : WHEN a non-abstract logic block of the plugin assembly is not registered in the plugin's service registration THE SYSTEM SHALL fail the run naming that type, and SHALL write no document.
- ADDED AC-INTRO-002.3 -> docs/specs/introspection.md : WHEN the plugin path or the output path is missing or empty THE SYSTEM SHALL fail the run and print its usage.
- ADDED AC-INTRO-002.4 -> docs/specs/introspection.md : WHEN the named plugin assembly does not exist THE SYSTEM SHALL fail the run.
- ADDED AC-INTRO-002.5 -> docs/specs/introspection.md : THE SYSTEM SHALL accept its options in any position and case-insensitively, and SHALL treat neither an option nor an option's value as a positional argument.
- ADDED AC-INTRO-002.6 -> docs/specs/introspection.md : WHEN the development-only exclusion is requested THE SYSTEM SHALL leave out every logic block that binds a development-only contract and SHALL name each excluded block and its bindings on standard output.
- ADDED AC-INTRO-002.7 -> docs/specs/introspection.md : THE SYSTEM SHALL prefix every such notice with a stable marker.
- ADDED AC-INTRO-002.8 -> docs/specs/introspection.md : WHEN introspecting a logic block throws THE SYSTEM SHALL report the originating exception rather than a reflection wrapper.
- ADDED AC-INTRO-003.1 -> docs/specs/introspection.md : THE SYSTEM SHALL emit a byte-identical document for repeated runs over one assembly.
- ADDED AC-INTRO-003.2 -> docs/specs/introspection.md : THE SYSTEM SHALL report the status-mapping, enum-label and struct-field maps in ordinal key order.
- ADDED AC-INTRO-003.3 -> docs/specs/introspection.md : THE SYSTEM SHALL report a service's properties and measuring points in base-to-derived declaration order.
- ADDED AC-INTRO-004.1 -> docs/specs/introspection.md : THE SYSTEM SHALL report a logic block's identity as its CLR full type name, so a nested block's identity carries the CLR nesting separator.
- ADDED AC-INTRO-004.2 -> docs/specs/introspection.md : THE SYSTEM SHALL report every other type name in the document in display form, with the nesting separator written the way source spells it.
- ADDED AC-INTRO-004.3 -> docs/specs/introspection.md : WHEN a logic block declares a name THE SYSTEM SHALL report it as the block's default-name annotation.
- ADDED AC-INTRO-004.4 -> docs/specs/introspection.md : WHEN a logic block declares an icon or groups THE SYSTEM SHALL report them, and SHALL omit each annotation whose declared value is empty.
- ADDED AC-INTRO-004.5 -> docs/specs/introspection.md : WHEN a logic block declares no annotations at all THE SYSTEM SHALL report an empty annotation map.
- ADDED AC-INTRO-004.6 -> docs/specs/introspection.md : THE SYSTEM SHALL carry a display string into the document verbatim, whatever characters it contains.
- ADDED AC-INTRO-005.1 -> docs/specs/introspection.md : THE SYSTEM SHALL identify a logic block's root service by the block's short class name.
- ADDED AC-INTRO-005.2 -> docs/specs/introspection.md : THE SYSTEM SHALL identify a component service by the name of the property holding it.
- ADDED AC-INTRO-005.3 -> docs/specs/introspection.md : THE SYSTEM SHALL distinguish service identifiers case-sensitively.
- ADDED AC-INTRO-005.4 -> docs/specs/introspection.md : THE SYSTEM SHALL report on each service the service-interface types its bound members came through, without repetition.
- ADDED AC-INTRO-005.5 -> docs/specs/introspection.md : THE SYSTEM SHALL identify a service property and a measuring point by its C# property name.
- ADDED AC-INTRO-006.1 -> docs/specs/introspection.md : THE SYSTEM SHALL report a member declaring both as one identifier in each of the two member lists, carrying one title and one description.
- ADDED AC-INTRO-006.2 -> docs/specs/introspection.md : THE SYSTEM SHALL report the measuring-point kind on the measuring point's schema and not on the service property's.
- ADDED AC-INTRO-006.3 -> docs/specs/introspection.md : THE SYSTEM SHALL report a measuring point's kind as its wire token.
- ADDED AC-INTRO-007.2 -> docs/specs/introspection.md : THE SYSTEM SHALL take a member's title, description, unit and string format from either of its emission declarations, preferring the service property's.
- ADDED AC-INTRO-007.3 -> docs/specs/introspection.md : THE SYSTEM SHALL report a declared bound only where it is finite.
- ADDED AC-INTRO-007.4 -> docs/specs/introspection.md : THE SYSTEM SHALL report a member as read-only when it is a measuring point without a service property, when its implementing property has no public setter, when its declaration opts in, or when it is an instantiation parameter.
- ADDED AC-INTRO-007.5 -> docs/specs/introspection.md : THE SYSTEM SHALL report write-only from a member's service-property declaration alone.
- ADDED AC-INTRO-007.6 -> docs/specs/introspection.md : THE SYSTEM SHALL build a member's schema from a bool, a byte, a short, a ushort, an int, a uint, a long, a float, a double, a date-time, a duration, a globally unique identifier, a string, an enum, a flat readonly record struct, an immutable array of those, or a nullable of any value type or string, and SHALL refuse any other type naming it.
- ADDED AC-INTRO-007.7 -> docs/specs/introspection.md : THE SYSTEM SHALL report a declared minimum above a declared maximum unchanged.
- ADDED AC-INTRO-007.8 -> docs/specs/introspection.md : THE SYSTEM SHALL report an authored title, description, unit or string format that is empty as an empty value rather than omitting it.
- ADDED AC-INTRO-008.1 -> docs/specs/introspection.md : THE SYSTEM SHALL report an enum as its short type name and its member-name strings, never its ordinals.
- ADDED AC-INTRO-008.2 -> docs/specs/introspection.md : THE SYSTEM SHALL report a struct as its short type name and its positional-constructor parameters in declaration order, each keyed camelCase, requiring only the non-nullable ones and permitting no additional members.
- ADDED AC-INTRO-008.3 -> docs/specs/introspection.md : WHEN a struct declares more than one constructor THE SYSTEM SHALL enumerate its fields from the one with the most parameters.
- ADDED AC-INTRO-008.4 -> docs/specs/introspection.md : WHEN a struct used as a member's type has no positional constructor THE SYSTEM SHALL refuse the introspection naming that struct.
- ADDED AC-INTRO-008.5 -> docs/specs/introspection.md : THE SYSTEM SHALL report struct-field annotations, member labels and severities for the fields of a member's own struct type and no deeper.
- ADDED AC-INTRO-008.6 -> docs/specs/introspection.md : THE SYSTEM SHALL report a struct field's authored title, description, unit, string format, bounds and write-only flag on that field's own schema, and SHALL omit a field that declares none.
- ADDED AC-INTRO-008.7 -> docs/specs/introspection.md : THE SYSTEM SHALL detect a nullable reference member from the compiler-emitted nullability annotation, falling back to the declaring constructor's and then the declaring type's.
- ADDED AC-INTRO-009.1 -> docs/specs/introspection.md : WHEN a member has no presentation to report THE SYSTEM SHALL report no presentation document rather than an empty one.
- ADDED AC-INTRO-009.2 -> docs/specs/introspection.md : THE SYSTEM SHALL report a member's display name, group, order, importance, UI hint, decimals, format and visibility predicate as declared.
- ADDED AC-INTRO-009.3 -> docs/specs/introspection.md : THE SYSTEM SHALL treat the integer sentinel as unset for order and for decimals, reporting neither.
- ADDED AC-INTRO-009.4 -> docs/specs/introspection.md : WHERE a member's importance is the default THE SYSTEM SHALL omit it.
- ADDED AC-INTRO-009.5 -> docs/specs/introspection.md : WHEN a member is declared a status indicator and declares no explicit UI hint THE SYSTEM SHALL report the status-indicator hint.
- ADDED AC-INTRO-009.6 -> docs/specs/introspection.md : THE SYSTEM SHALL accept a presentation declaration on a service property or a measuring point and nowhere else.
- ADDED AC-INTRO-010.1 -> docs/specs/introspection.md : WHEN a member is declared a status indicator THE SYSTEM SHALL report the severity of each member of its enum type, and SHALL report none otherwise.
- ADDED AC-INTRO-010.2 -> docs/specs/introspection.md : THE SYSTEM SHALL read severities through a nullable enum and SHALL NOT read them through an array of enum.
- ADDED AC-INTRO-010.3 -> docs/specs/introspection.md : THE SYSTEM SHALL report each declared enum-member label without requiring any flag, reading through a nullable enum and through an array of enum, and SHALL omit an unlabelled member.
- ADDED AC-INTRO-010.4 -> docs/specs/introspection.md : THE SYSTEM SHALL report a declared label whatever its value, including an empty one, one repeated on another member, and one on a combined flags member.
- ADDED AC-INTRO-011.1 -> docs/specs/introspection.md : THE SYSTEM SHALL report a struct field's authored title beside the schema only where that field's own schema title carries type identity, leaving a scalar field's title inline.
- ADDED AC-INTRO-011.2 -> docs/specs/introspection.md : THE SYSTEM SHALL report a struct field's enum-member labels and severities without requiring the status-indicator flag.
- ADDED AC-INTRO-011.3 -> docs/specs/introspection.md : WHEN a member's struct has nothing to carry beside its schema THE SYSTEM SHALL report no struct-field presentation at all.
- ADDED AC-INTRO-011.4 -> docs/specs/introspection.md : THE SYSTEM SHALL leave a member's own presentation intact beside the struct-field presentation it carries.
- ADDED AC-INTRO-012.1 -> docs/specs/introspection.md : WHEN a member's schema title carries its own type identity THE SYSTEM SHALL route the authored title to the presentation display name instead.
- ADDED AC-INTRO-012.2 -> docs/specs/introspection.md : THE SYSTEM SHALL build an interface-bound member's schema from the interface's declaration and its presentation and runtime from the implementing property.
- ADDED AC-INTRO-012.3 -> docs/specs/introspection.md : THE SYSTEM SHALL take each presentation field from the implementing property where it sets one and from the interface otherwise.
- ADDED AC-INTRO-012.4 -> docs/specs/introspection.md : THE SYSTEM SHALL decide a member's writability from the implementing property.
- ADDED AC-INTRO-013.1 -> docs/specs/introspection.md : THE SYSTEM SHALL report a member as persistent when it opts in without excluding itself, and SHALL report no runtime document where there is nothing to report.
- ADDED AC-INTRO-014.1 -> docs/specs/introspection.md : THE SYSTEM SHALL identify an interface binding by its declared identifier, and where none is declared by the holding property's name joined to the interface's name for a property-based binding and by the bare interface name for a class-implemented one.
- ADDED AC-INTRO-014.2 -> docs/specs/introspection.md : THE SYSTEM SHALL identify a contract binding by its declared identifier, and by the holding property's name where none is declared.
- ADDED AC-INTRO-014.3 -> docs/specs/introspection.md : WHEN a binding declares an identifier that is empty or blank THE SYSTEM SHALL refuse the introspection naming the member.
- ADDED AC-INTRO-014.4 -> docs/specs/introspection.md : WHEN two bindings of one logic block resolve to one identifier THE SYSTEM SHALL refuse the introspection naming both members.
- ADDED AC-INTRO-015.2 -> docs/specs/introspection.md : THE SYSTEM SHALL report an interface binding's default name, tags and multiplicity, omitting each where it is unset or default, and SHALL report its contract's name.
- ADDED AC-INTRO-015.3 -> docs/specs/introspection.md : WHEN a logic interface's contract declares role default names THE SYSTEM SHALL report this side's and the counterpart's separately, omitting each where unset.
- ADDED AC-INTRO-015.4 -> docs/specs/introspection.md : THE SYSTEM SHALL resolve a contract's declared direction to an inbound or an outbound arrow for the side the endpoint is on, and to none where the contract declares none.
- ADDED AC-INTRO-015.5 -> docs/specs/introspection.md : THE SYSTEM SHALL report a contract binding's contract type and its contract-type token.
- ADDED AC-INTRO-015.6 -> docs/specs/introspection.md : THE SYSTEM SHALL report a contract binding's default name, tags, multiplicity, provider-side consumer limit, handler-actor name and development-only flag, omitting each where it is unset or default.
- ADDED AC-INTRO-016.1 -> docs/specs/introspection.md : WHEN a bound endpoint's contract declares a service relation THE SYSTEM SHALL report one relation half per declaration on the service owning that endpoint, carrying the endpoint's own identifier.
- ADDED AC-INTRO-016.2 -> docs/specs/introspection.md : THE SYSTEM SHALL report a half as outward where the endpoint's interface is the one the declaration names, and as inward otherwise.
- ADDED AC-INTRO-016.3 -> docs/specs/introspection.md : WHEN an endpoint's holding property carries no service surface THE SYSTEM SHALL report no relation half for it.
- ADDED AC-INTRO-016.4 -> docs/specs/introspection.md : THE SYSTEM SHALL report a relation half with an empty annotation map.
- ADDED AC-INTRO-016.5 -> docs/specs/introspection.md : WHEN a service relation names an interface that is neither side of its contract, or is declared on a class carrying no contract declaration, THE SYSTEM SHALL refuse the introspection.
- ADDED AC-INTRO-016.6 -> docs/specs/introspection.md : THE SYSTEM SHALL report the halves of a gated component in the definition view and none of them for an instance whose configuration excludes it.
- ADDED AC-INTRO-017.1 -> docs/specs/introspection.md : WHEN a logic-block library is packed THE SYSTEM SHALL publish the project, run the introspection over the published assembly excluding development-only blocks, and fail the pack if that run fails. GAP: a targets test is a pack-and-consume round trip, which nothing in this repository has a harness for; the parser half it drives is covered by the document and refusal criteria above.
- ADDED AC-INTRO-017.2 -> docs/specs/introspection.md : THE SYSTEM SHALL write the document beside the published output under the project's name and pack the whole published folder. GAP: as `AC-INTRO-017.1`.
- ADDED AC-INTRO-017.3 -> docs/specs/introspection.md : THE SYSTEM SHALL skip the introspection entirely for a project that opts out. GAP: as `AC-INTRO-017.1`.
- ADDED AC-INTRO-017.4 -> docs/specs/introspection.md : THE SYSTEM SHALL supply the source generator and analyzers to every consuming project.


### Amendment 2

> Applied by hand into the page, which is already distilled: `spec-change.ps1 archive` does not re-run
> on an archived doc, so these lines and the page's own text were generated from the page together.

- MODIFIED AC-INTRO-002.4 -> docs/specs/introspection.md : WHEN the named plugin assembly does not exist THE SYSTEM SHALL fail the run, naming the path it looked at.
- ADDED AC-INTRO-002.9 -> docs/specs/introspection.md : THE SYSTEM SHALL apply the development-only exclusion to the document alone, so an excluded block is still introspected and every refusal above still applies to it.
- MODIFIED AC-INTRO-004.3 -> docs/specs/introspection.md : WHEN a logic block declares a name THE SYSTEM SHALL report it as the block's default-name annotation, and SHALL report its icon and its groups under their own.
- MODIFIED AC-INTRO-004.4 -> docs/specs/introspection.md : WHERE a logic block's declared name, icon or group set is empty THE SYSTEM SHALL omit that annotation.
- ADDED AC-INTRO-006.4 -> docs/specs/introspection.md : THE SYSTEM SHALL report a member declaring both streams with the same presentation document on each.
- ADDED AC-INTRO-007.1 -> docs/specs/introspection.md : THE SYSTEM SHALL report a schema for every service property and every measuring point.
- MODIFIED AC-INTRO-007.2 -> docs/specs/introspection.md : THE SYSTEM SHALL take a member's title, description, unit and string format from either of its emission declarations, preferring the service property's, and SHALL report them on the member's own schema — on an array member's root and not on its element schema.
- ADDED AC-INTRO-007.9 -> docs/specs/introspection.md : THE SYSTEM SHALL report the format a member's CLR type implies — a date-time for a `DateTime`, a duration for a `TimeSpan`, a unique identifier for a `Guid` — at every depth its schema reaches.
- MODIFIED AC-INTRO-008.7 -> docs/specs/introspection.md : THE SYSTEM SHALL read the declared nullability of every reference position of a member's type — the member itself and each element it nests — from the compiler-emitted annotation, falling back to the declaring constructor's and then the declaring type's.
- ADDED AC-INTRO-008.8 -> docs/specs/introspection.md : THE SYSTEM SHALL accept a struct-field declaration on a positional constructor parameter and nowhere else.
- ADDED AC-INTRO-008.9 -> docs/specs/introspection.md : THE SYSTEM SHALL report the same field schemas for a nullable struct member as for a non-nullable one of the same type, widening only the member's own type.
- MODIFIED AC-INTRO-010.1 -> docs/specs/introspection.md : WHEN a member is declared a status indicator THE SYSTEM SHALL report the severity of each member of its enum type as its lower-cased wire token, and SHALL report none otherwise.
- MODIFIED AC-INTRO-010.3 -> docs/specs/introspection.md : THE SYSTEM SHALL report each declared enum-member label without requiring any flag, reading through a nullable enum and through an array of enum, and SHALL omit an unlabelled member and every member whose type is not an enum.
- ADDED AC-INTRO-011.5 -> docs/specs/introspection.md : THE SYSTEM SHALL report an authored struct-field title that is empty rather than omitting it.
- MODIFIED AC-INTRO-014.4 -> docs/specs/introspection.md : WHEN two bindings of one logic block and of the same kind resolve to one identifier THE SYSTEM SHALL refuse the introspection naming both declarations.
- ADDED AC-INTRO-014.5 -> docs/specs/introspection.md : THE SYSTEM SHALL mint contract-binding and interface-binding identifiers in separate namespaces, each distinguished case-sensitively.
- ADDED AC-INTRO-015.1 -> docs/specs/introspection.md : THE SYSTEM SHALL report an interface binding's logic-interface type and its matching counterpart type by display full name.
- MODIFIED AC-INTRO-015.6 -> docs/specs/introspection.md : THE SYSTEM SHALL report a contract binding's handler-actor name always, and its default name, tags, multiplicity, provider-side consumer limit and development-only flag only where each is set or non-default.
- ADDED AC-INTRO-016.7 -> docs/specs/introspection.md : THE SYSTEM SHALL report on each relation half the logic-interface type of the endpoint it was derived from.
- ADDED AC-INTRO-016.8 -> docs/specs/introspection.md : WHEN a component property holds null THE SYSTEM SHALL report no relation half for its endpoints, having reported no service for it to hang on.
- MODIFIED AC-INTRO-017.1 -> docs/specs/introspection.md : WHEN a logic-block library is packed THE SYSTEM SHALL publish the project, run the introspection over the published assembly supplying the project's package id and excluding development-only blocks, and fail the pack if that run fails. GAP: a targets test is a pack-and-consume round trip, which nothing in this repository has a harness for; the parser half it drives is covered by the document and refusal criteria above.

---

## Tasks

- `T-001` (`AC-INTRO-001.2`, `AC-INTRO-002.1`, `AC-INTRO-006.2`, `AC-INTRO-007.3`, `AC-INTRO-009.6`,
  `AC-INTRO-014.3`, `AC-INTRO-014.4`): the seven classified fixes, each proven red before its fix and
  measured against the one mutation that reddens it; the parser test project that answers Q3.
- `T-002` (`AC-INTRO-004.2`, `AC-INTRO-005.*`, `AC-INTRO-007.5`, `AC-INTRO-007.6`, `AC-INTRO-008.5`,
  `AC-INTRO-009.1`, `AC-INTRO-010.4`, `AC-INTRO-012.4`, `AC-INTRO-013.1`): the GAP-closing tests, and
  the display-form defect the first of them uncovered.
- `T-003` (every id): the suite's citations and its §12/§13 rename round.
- `T-004` (every id): the spec page, the two RFCs absorbed and deleted, the reference sweep, the
  identifier-stability fold and its link re-points, `testing-conventions.md` § 4, the ledger.


---

## Scorecard (pass 4 — attempt 1, one classification relay, two amendments, one fresh-session round, zero reverts)

| Measure | Value |
|---|---|
| Gates (build/test/lint/trace/style/doc-comment/self-tests/cleanup/CI) | green — every line below is a paste from the fix-up REPORT on `204112e`, and the coordinator re-ran the read-only gates on the same head with identical numbers. `Build succeeded.` · `24 Warning(s)` (all `NU1900`; `grep -c "warning DALE"` → `0`) · `0 Error(s)`. `dotnet test Vion.Dale.Sdk.sln`: 28 assemblies, every one `Failed: 0` — `Passed: 500, Skipped: 4, Total: 504` (`Vion.Dale.Sdk.Test`), `Passed: 18` (`Vion.Dale.LogicBlockParser.Test`), `Passed: 382` (`…Generators.Test`), `Passed: 254` (`…DevHost.Test`). `spec-lint: OK` · `spec-lint: OK` (`-Diff main`) · `spec-trace: OK - 262 id(s) all referenced by tests (4 traced page(s); 6 GAP id(s) awaiting tests: AC-EMIT-002.4, AC-EMIT-009.4, AC-GATE-012.5, AC-INTRO-017.1, AC-INTRO-017.2, AC-INTRO-017.3)` · `test-style-lint: OK - 354 cited test(s) conform (88 file(s) in exempt projects skipped)` · `doc-comment-lint: OK - 2750 doc block(s) in 842 file(s), none carries a second <summary>` · `run-script-tests: OK - 5 self-test(s) passed; 7 script(s) exempt` · `cleanupcode applied changes` on `9 files changed, 110 insertions(+), 24 deletions(-)` (the round's own edits; a second run reproduces the stat). BOM scan against `main`: 0 files with a new byte-order mark. CI: on the PR. |
| Completeness-critic misses | **10** — by sweep: statement 3 (M1 `[StructField]` on a property, M2 a relation half's interface type, M10 the pack hook's package id), consumer 1 (M7 `dale list` without the exclusion), edge-value 3 (M4 array element nullability, M5 an empty title on an enum field, M6 identifier namespaces per kind), state-interaction 3 (M3 component-service order, M8 exclusion versus the checks, M9 a null component's half). Closed as 4 fixes (M1, M3, M4, M5), 5 criteria (M2, M6, M8, M9, M10), 1 park (M7). Plus 9 citation-text mismatches and 4 criterion-versus-branch flags, all resolved. |
| Evidence errors found in review | **5** — the step-9 self-check's "interface 4 (rows 66, 70–73)" claiming two fields covered that no criterion stated (round 1); the test map's `37` for a merged file, the future-tense merge paragraph, and a journal line crediting M9 with a one-line rule the range did not contain (round 2, all three surviving one amendment because nothing re-reads a change doc's earlier sections after an append); and one the coordinator caused — amendment 2 asked for `AC-INTRO-015.1` to be cited from a test that proves the relation-half criteria, and the session cited it without re-reading the assertion (round 2). |
| Mutation evidence | complete — 12 Phase B rows, 12 amendment-2 rows, 5 amendment-3 rows, each applied, run, read and reverted. Over-determined criteria stated: `AC-INTRO-007.1` (its mutation reddens seventeen tests), `SerializeDocumentDeclaringNonFiniteBound` (either `FiniteBound`), `EmitIdenticalFieldSchemasWhateverStructNullability` (shares its mutation with the `[DataRow]` nullable row). Twelve premise tests in `PresentationAttributeShould` stay uncited by design. The discipline caught two vacuous first drafts (M8's fixture edit that matched nothing; M9's test asserting the wrong absence) and two criteria with no reachable mutation until the behaviour became a rule in one place (`AC-INTRO-014.5`, `AC-INTRO-016.8`). |
| Operator corrections (table + PR) | at the classification gate: 2 — row 14 reversed from `park` to `intended` (the type name is the identifier a host loads by) and Q3 taken rather than parked; coordinator rounds: 3 — round 1 (critic 10 misses + 9 mismatches + 4 flags; review 1 blocker · 3 conventions · 1 nit · 6 judgments), round 2 (focused review 2 blockers · 3 judgments · 4 nits), round 3 (targeted reads, 0 findings). One operator correction of the coordinator's own record: the ten-hour stall's cause was the sender's permission mode, not the operator's absence. |
| Cost | 2 Opus sessions (the pass session at high effort, resumed twice — once after the ten-hour stall, once to put it into bypass mode; the fresh fix-up session) · 1 classification relay + 2 amendments · 3 Opus check subagents (critic, review, focused review) · 0 reverts · ~27 h wall, of which ~10 h stalled on undelivered cross-session messages. |