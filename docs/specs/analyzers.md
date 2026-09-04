---
trace: enforced
---

# Analyzer diagnostics: what the SDK refuses at compile time

What a logic-block author is told before the code runs. The SDK ships a registry of `DALE`
diagnostics inside its own NuGet package, under `analyzers/dotnet/cs`, so every consumer that
references `Vion.Dale.Sdk` receives them without asking. Area code `ANLZ`. Process:
[`../spec-process.md`](../spec-process.md).

The standing expectation this page serves is
[`../sdk-surface-conventions.md`](../sdk-surface-conventions.md) § 4: **a rule a library author can
violate in C# is enforced by an analyzer, not by prose or a runtime throw alone.** That doc and
§ 5 own the author disciplines — how to add a diagnostic, and why an analyzer sees a different
compilation than its tests do. This page states what the registry and its analyzers *do*.

**Cited rather than restated.** Where a neighbouring page states a diagnostic's firing rule, this
page names the id and stops: [`config-gating.md`](config-gating.md) for what `[IncludedWhen]` and
`[InstantiationParameter]` accept (`AC-GATE-011.1`–`011.11`, `DALE043` and `DALE044`);
[`emission.md`](emission.md) for the throttle and deadband knobs
(`AC-EMIT-012.1`–`012.7`, `DALE034`–`DALE039`); [`block-lifecycle.md`](block-lifecycle.md) for the
timer binder's own refusals (`AC-LIFE-007.2`); [`contracts.md`](contracts.md) for what a contract
declaration means (`DALE001`, `009`, `010`, `011`, `019`, `020`, `025`, `045`);
[`introspection.md`](introspection.md) for the wiring probe (`AC-INTRO-017.4`) and endpoint
identity; [`scenarios.md`](scenarios.md) for the scenario wire (`DALE046`).

Three ids are stated **here** because no other page states them: `DALE041` and `DALE042`, which
judge a `[Presentation(VisibleWhen = …)]` predicate — visibility hides a member that still exists
and still publishes, so there is no runtime refusal for a diagnostic to be the door onto — and
`DALE045`, which [`contracts.md`](contracts.md) names as this area's.

## The registry is a contract with the consumer

Ids are allocated in sequence from `DALE001` and never reused, so the allocated range runs ahead of
the live set: `DALE006` and `DALE029` are retired and leave a comment where the descriptor was. The
live set is grep-enumerable from `Vion.Dale.Sdk.Generators/Analyzers/DaleDiagnostics.cs` — one
`public static readonly DiagnosticDescriptor` per id; this page states the rules the set obeys, never
the roster.

- `AC-ANLZ-001.1` (Ubiquitous): THE SYSTEM SHALL report every Dale authoring diagnostic under the
  single category `Vion.Dale.Usage`.
- `AC-ANLZ-001.2` (Ubiquitous): THE SYSTEM SHALL never reuse a retired diagnostic id, and SHALL accept
  a suppression naming one without effect.
- `AC-ANLZ-001.3` (Ubiquitous): THE SYSTEM SHALL enable every diagnostic by default, so a consumer
  that configures nothing receives all of them.
- `AC-ANLZ-001.4` (Ubiquitous): THE SYSTEM SHALL tag every diagnostic reported from a
  whole-compilation analysis with `WellKnownDiagnosticTags.CompilationEnd`, without which the IDE
  drops it from live analysis.
- `AC-ANLZ-001.5` (Ubiquitous): THE SYSTEM SHALL carry at most one descriptor per id, so an id whose
  rules differ in severity reports the advisory ones through the `effectiveSeverity` overload — and
  configuring or suppressing that id in `.editorconfig` moves both severities together.
- `AC-ANLZ-001.6` (Ubiquitous): THE SYSTEM SHALL fail a build only for a diagnostic reported at
  `Error` severity, leaving every `Warning` and the one `Info` to be read rather than obeyed.

Two ids carry rules at two severities today: `DALE045` and `DALE044`. That is the shape § 4 prefers
over two descriptors sharing an id, and `AC-ANLZ-001.5`'s second clause is the price — a consumer
who suppresses the warning half loses the error half with it.

## What every analyzer can and cannot see

Almost every rule walks a type's properties, and the walk is deliberately the one the declarative
binders use: it exists to agree with them, so that a diagnostic and a bind-time refusal are two
doors onto one rule rather than two rules.

- `AC-ANLZ-002.1` (Ubiquitous): WHERE a rule walks a type's members THE SYSTEM SHALL walk the public,
  non-static properties of the type and of its base chain up to `object`, yielding the most-derived
  declaration of a shadowed name first.
- `AC-ANLZ-002.2` (Ubiquitous): THE SYSTEM SHALL match every attribute it keys off by
  fully-qualified metadata name, so an alias or a `using` rename does not hide a declaration and an
  attribute *derived* from a Dale attribute is not matched.
- `AC-ANLZ-002.3` (Ubiquitous): WHERE a rule analyses the whole compilation THE SYSTEM SHALL judge
  only the types declared in the compilation's own assembly.
- `AC-ANLZ-002.4` (Ubiquitous): THE SYSTEM SHALL analyse no compiler-generated code. GAP: [`../sdk-surface-conventions.md`](../sdk-surface-conventions.md) § 5 records that flipping the flag changes no diagnostic in this repository, so there is no observable to assert.
- `AC-ANLZ-002.5` (Ubiquitous): WHERE a member may declare both `[ServiceProperty]` and
  `[ServiceMeasuringPoint]` THE SYSTEM SHALL judge each attribute's own knobs and report at the
  attribute that declares them.
- `AC-ANLZ-002.6` (Ubiquitous): THE SYSTEM SHALL judge a declaration's own attributes whatever its
  accessibility, while a rule that walks a *type* sees only what the binders see.

`AC-ANLZ-002.2`'s last clause is a live limitation, not a design: a preset attribute — a class
deriving from `ServicePropertyAttribute` so that `[Kilowatts]` carries a unit — is honoured by the
runtime and matched by exactly one rule, `DALE019`, whose job is to catch two of them on one member.
Every other rule is blind to it. Widening the match re-aims all of them at once, which is why it is
a ledger line rather than a fix.

`AC-ANLZ-002.6`'s two halves are what make a gate's error message confusing on a non-public
`[InstantiationParameter]`: the parameter's own declaration is judged, the type walk that would
resolve a gate to it is not. `AC-ANLZ-014.2` closes that with a message on the parameter itself.

## The supported-type gate is three rules, not one

A value type reaching a service member is judged independently by `DALE003` (is the type supported
at all), `DALE008` (is a collection an `ImmutableArray<T>`) and `DALE016` (is a user struct a flat
readonly record struct). Adding a type to one and not the others ships a type the SDK claims to
support and the build rejects — § 4 records that this has already happened once.

- `AC-ANLZ-003.1` (Ubiquitous): THE SYSTEM SHALL accept as a service-element type `bool`, `string`,
  `byte`, `short`, `ushort`, `int`, `uint`, `long`, `float`, `double`, `DateTime`, `TimeSpan`,
  `Guid`, any enum, any flat readonly record struct, `ImmutableArray<T>` of any of those, and the
  nullable form of any value type among them — and SHALL report `DALE003` naming that accepted set for
  any other type.
- `AC-ANLZ-003.2` (Ubiquitous): WHEN one declaration violates more than one supported-type rule THE
  SYSTEM SHALL report each rule independently.
- `AC-ANLZ-003.3` (Ubiquitous): THE SYSTEM SHALL name, in each supported-type diagnostic's message,
  every type that rule accepts.
- `AC-ANLZ-003.4` (Ubiquitous): THE SYSTEM SHALL treat a struct as a valid service element only where
  it is a readonly record struct whose positional parameters are all primitives, enums, strings,
  `TimeSpan`, `Guid`, or nullables of those — recognising a record struct loaded from metadata by its
  synthesized `Deconstruct` method, which `IsRecord` does not report.
- `AC-ANLZ-003.5` (Ubiquitous): WHEN a service-element property is typed `string` in a
  nullable-disabled context THE SYSTEM SHALL report `DALE017`, the compiler having no way to tell the
  author's intent.
- `AC-ANLZ-003.6` (Ubiquitous): WHEN a service-element property is an auto-implemented
  `ImmutableArray<T>` with no initializer THE SYSTEM SHALL report `DALE018`, and SHALL exempt an
  interface member, an abstract property and a property with an explicit getter — none of which can
  carry an initializer, or whose value the analyzer cannot read.
- `AC-ANLZ-003.7` (Event-driven): WHEN a service element's type is an array or one of the general
  collection shapes — `T[]`, `List<T>`, `IList<T>`, `ICollection<T>`, `IEnumerable<T>`,
  `IReadOnlyList<T>`, `IReadOnlyCollection<T>` — THE SYSTEM SHALL report `DALE008` naming the member,
  the attribute it is declared under and the type, and SHALL accept `ImmutableArray<T>`.

`AC-ANLZ-003.3` is why the messages are worth a criterion of their own: the message is the only
place an author is told the whole set, and both of them were short by a type the SDK ships.

## Contract properties

- `AC-ANLZ-004.1` (Event-driven): WHEN a property typed as a service-provider contract has no setter
  THE SYSTEM SHALL report `DALE001`.
- `AC-ANLZ-004.2` (Event-driven): WHEN such a property is declared on an interface THE SYSTEM SHALL
  report nothing, and WHEN it is declared `abstract` THE SYSTEM SHALL report `DALE001`.
- `AC-ANLZ-004.3` (Ubiquitous): THE SYSTEM SHALL accept a setter of any accessibility, including one
  declared on a base class.
- `AC-ANLZ-004.4` (Event-driven): WHEN the property's type does not resolve THE SYSTEM SHALL report
  nothing, the compiler's own error being the whole story.

`AC-ANLZ-004.2`'s first half is a rule about *remedies*: the message prescribes
`{ get; private set; }`, which is a compile error on an interface, and a diagnostic whose fix does
not compile is worse than none. `MeasuringPointAnalyzer` and `ImmutableArrayInitializationAnalyzer`
carry the same guard for the same reason. The abstract half is the opposite case — an override
cannot add an accessor its base does not declare, so the abstract declaration is where the author
must act.

## Contract and message declarations

The firing rules for a contract's role names and its messages are [`contracts.md`](contracts.md)'s;
what this page states is which declarations the analyzers reach, and the two ids minted here.

- `AC-ANLZ-005.1` (Event-driven): WHEN a `[LogicBlockContract]`'s `BetweenInterface` or
  `AndInterface` does not start with `I` THE SYSTEM SHALL report `DALE009` naming which one.
- `AC-ANLZ-005.2` (Event-driven): WHEN a message nested in a contract names a `From` or `To` that is
  neither of the contract's two role names THE SYSTEM SHALL report `DALE010`.
- `AC-ANLZ-005.3` (Event-driven): WHEN a `[RequestResponse]`'s `ResponseType` is not a struct nested
  in the same contract class THE SYSTEM SHALL report `DALE011`.
- `AC-ANLZ-005.4` (Event-driven): WHEN a `[ServiceProviderContractType]` token is empty or whitespace
  THE SYSTEM SHALL report `DALE048`.
- `AC-ANLZ-005.5` (Event-driven): WHEN a `[Command]`, `[StateUpdate]` or `[RequestResponse]` struct
  is declared anywhere but nested inside a `[LogicBlockContract]` class THE SYSTEM SHALL report
  `DALE047`.
- `AC-ANLZ-005.6` (Ubiquitous): THE SYSTEM SHALL compare a contract's role names as strings, so the
  rules that read them hold when the interfaces those names denote do not resolve.

`AC-ANLZ-005.5` closes a shape that compiled, generated nothing and was diagnosed by nothing: the
three message attributes allow any struct target, and the generator reads only the structs nested in
a contract class. `AC-ANLZ-005.4` closes the other half of the same silence — the token is the
stable cloud-facing identifier of a contract type, and the attribute validates nothing.

`AC-ANLZ-005.6` is § 5's rule seen from the other side. A role name is a string in an attribute, not
a symbol, so `DALE009` and `DALE010` are unaffected by the error-type problem that makes a
symbol-only check no-op in a real build.

## Attribute stacking and interface conflicts

- `AC-ANLZ-006.1` (Event-driven): WHEN two attributes deriving from one platform base appear on a
  single property THE SYSTEM SHALL report `DALE019` once per attribute, and SHALL accept
  `[ServiceProperty]` beside `[ServiceMeasuringPoint]`, which are distinct bases.
- `AC-ANLZ-006.2` (Event-driven): WHEN a non-abstract class implements two interfaces declaring one
  property name with conflicting `Unit` values THE SYSTEM SHALL report `DALE020`.
- `AC-ANLZ-006.3` (Event-driven): WHEN a **public** implementing declaration carries its own
  `[ServiceProperty]` or `[ServiceMeasuringPoint]` THE SYSTEM SHALL report nothing, wherever in the
  base chain that declaration sits — and SHALL still report where the only such declaration is an
  explicit interface implementation, which the service binder's own walk does not reach.
- `AC-ANLZ-006.4` (Ubiquitous): THE SYSTEM SHALL read a declaration's `Unit` from whichever of its
  emission attributes states one.
- `AC-ANLZ-006.5` (Event-driven): WHEN one property's `[ServiceProperty]` and
  `[ServiceMeasuringPoint]` set the same cross-filled field to different non-empty values THE SYSTEM
  SHALL report `DALE025` once per conflicting field.

`AC-ANLZ-006.3` and `AC-ANLZ-006.4` are the same defect twice: a rule that read the type's own
members, and a rule that read the first of two attributes. Both told an author to write something
they had already written.

## Timers

The timer binder's own refusals are [`block-lifecycle.md`](block-lifecycle.md)'s (`AC-LIFE-007.2`);
these are the compile-time doors onto them, and each is over-determined with the binder by design.

- `AC-ANLZ-007.1` (Event-driven): WHEN a `[Timer]` method is not `void` or takes parameters THE
  SYSTEM SHALL report `DALE002` naming both faults, whatever the method's accessibility.
- `AC-ANLZ-007.2` (Event-driven): WHEN a `[Timer]`'s interval is not a finite number of at least one
  clock tick and no longer than a clock can wait THE SYSTEM SHALL report `DALE005`.
- `AC-ANLZ-007.3` (Event-driven): WHEN two `[Timer]` methods a type carries, its base declarations
  included, resolve to one identifier THE SYSTEM SHALL report `DALE012` once per collision, at the
  type that declares the second of the two — treating an `override` and the virtual it overrides as
  one timer, and a `new` declaration as two.

`AC-ANLZ-007.2` states the binder's whole refusal set as one positive condition on purpose: written
as `<= 0`, the guard let not-a-number through, because every comparison against it is false. Infinity
and an interval longer than a clock can wait were never asked about at all, and neither was the
floor — a positive interval too short to reach one clock tick, which the binder's conversion to a
`TimeSpan` truncates to no delay at all. The floor is the one clause the compile-time door mirrors by
arithmetic rather than by sharing a constant: it multiplies the ticks out the way that conversion
does. Both doors stay open by design, and `AC-LIFE-007.2`'s is the one that still holds if a host's
conversion ever rounds differently from the one this mirrors.

`AC-ANLZ-007.3`'s base-chain clause is what makes the diagnostic worth having: the binder collects
`[Timer]` methods across the whole chain, so a base and a derived declaration sharing an identifier
both reach the callback map and one silently never ticks — which is the bug `DALE012` was minted for
and could not see.

## Measuring points and persistence

- `AC-ANLZ-008.1` (Event-driven): WHEN a `[ServiceMeasuringPoint]` that is not also a
  `[ServiceProperty]` has a public setter THE SYSTEM SHALL report `DALE004`, exempting an interface
  declaration and not an abstract one.
- `AC-ANLZ-008.2` (Event-driven): WHEN a `[Persistent]` property has no setter and does not set
  `Exclude` THE SYSTEM SHALL report `DALE007`.

## Presentation hints

Each of these guards a hint the renderer silently ignores when it is misplaced — the whole family
exists because the failure is invisible in the running system.

- `AC-ANLZ-009.1` (Ubiquitous): THE SYSTEM SHALL judge a `[Presentation]` hint on any property,
  whether or not that property also carries a service attribute.
- `AC-ANLZ-009.2` (Event-driven): WHEN `Decimals` is written on a non-numeric property THE SYSTEM
  SHALL report `DALE021`, distinguishing an unwritten `Decimals` from an explicit `Decimals = 0`.
- `AC-ANLZ-009.3` (Event-driven): WHEN `UiHint` is the trigger hint on a property that is not a
  writable `bool` THE SYSTEM SHALL report `DALE023` naming which half failed.
- `AC-ANLZ-009.4` (Event-driven): WHEN `StatusIndicator` is set on a property whose type is not an
  enum or a nullable enum THE SYSTEM SHALL report `DALE024`.
- `AC-ANLZ-009.5` (Event-driven): WHEN `Format` is set on a property that is neither `DateTime` nor
  `TimeSpan` THE SYSTEM SHALL report `DALE027`, and WHEN a format sentinel names the wrong one of the
  two THE SYSTEM SHALL report `DALE028`.
- `AC-ANLZ-009.6` (Event-driven): WHEN `Importance` is `Primary` or `Secondary` on a supported
  composite type THE SYSTEM SHALL report `DALE032`, and SHALL leave an unsupported type to the
  supported-type gate.

## Group keys

- `AC-ANLZ-010.1` (Event-driven): WHEN a literal `Group` key matches no constant the compilation can
  see THE SYSTEM SHALL report `DALE026`.
- `AC-ANLZ-010.2` (Ubiquitous): THE SYSTEM SHALL accept a symbolic constant reference as a `Group`
  key without judging its value.
- `AC-ANLZ-010.3` (Ubiquitous): THE SYSTEM SHALL treat `DALE026` as suppressible for a deliberate one-off key, the group vocabulary being open. GAP: the mechanism is `AC-ANLZ-020.1`'s; this line records that this rule in particular is designed to be suppressed rather than obeyed.
- `AC-ANLZ-010.4` (Ubiquitous): THE SYSTEM SHALL collect group-key constants from the compilation's
  own assembly **and from its references**.
- `AC-ANLZ-010.5` (Ubiquitous): THE SYSTEM SHALL collect them only from a static class named exactly
  `PropertyGroup`, in any namespace and nested in a type or not.

`AC-ANLZ-010.4` is § 5's blind spot one level out, and it had been firing on every consumer since
the rule shipped: the platform's own `PropertyGroup` is a source declaration inside the SDK and
metadata for everyone else, so `[Presentation(Group = "status")]` — a key the platform ships —
warned in a consumer's build and was silent in ours. `AC-ANLZ-010.5` is the deliberate half: an
integrator declares their own `PropertyGroup` class in their own namespace, and a class merely
*ending* in the name is not read.

## StringFormat, redaction and access flags

- `AC-ANLZ-011.1` (Event-driven): WHEN `StringFormat` is set on a member that is not `string` or
  `string?`, or is set to a format reserved for a CLR type-kind on one that is, THE SYSTEM SHALL
  report `DALE033`.
- `AC-ANLZ-011.2` (Ubiquitous): THE SYSTEM SHALL judge the `StringFormat` of every emission attribute
  a member declares, reporting at the attribute that declares it.
- `AC-ANLZ-011.3` (Ubiquitous): THE SYSTEM SHALL judge a `[StructField]`'s `StringFormat` by the same
  rule, and SHALL judge a `[StructField]` only where the introspector reads it — on a parameter of a
  struct's constructor.
- `AC-ANLZ-011.4` (Event-driven): WHEN `WriteOnly` is set on a service property, or on a struct field
  within that same scope, that is not `string` or `string?` THE SYSTEM SHALL report `DALE022` or
  `DALE040`.
- `AC-ANLZ-011.5` (Event-driven): WHEN `[ServiceProperty]` sets both `ReadOnly` and `WriteOnly` THE
  SYSTEM SHALL report `DALE030`.

`AC-ANLZ-011.2` is the first-attribute-wins defect the emission family had already been fixed for,
found in one more place; `AC-ANLZ-011.3` is the target nothing reached — the knob is declared on
`[StructField]` and emitted into the schema, and no rule read it.

## Public-API documentation

- `AC-ANLZ-012.1` (Event-driven): WHEN a `[PublicApi]` type has no `<summary>` documentation THE
  SYSTEM SHALL report `DALE013`.
- `AC-ANLZ-012.2` (Event-driven): WHEN a public type in a declared public-API namespace carries
  neither `[PublicApi]` nor `[InternalApi]` THE SYSTEM SHALL report `DALE014`.
- `AC-ANLZ-012.3` (Event-driven): WHEN a declared public-API namespace matches no public type THE
  SYSTEM SHALL report `DALE015`.
- `AC-ANLZ-012.4` (Ubiquitous): THE SYSTEM SHALL judge a nested public type by these rules exactly as
  it judges a top-level one, and only for types declared in source.
- `AC-ANLZ-012.5` (Ubiquitous): THE SYSTEM SHALL judge a type by its **effective** accessibility for
  the mark rule alone, so a public type nested in a non-public one is not asked for a mark and does
  not keep its namespace off the stale list — while a `[PublicApi]` the author wrote is asked for its
  documentation whatever encloses it.
- `AC-ANLZ-012.6` (Ubiquitous): THE SYSTEM SHALL credit every declared namespace a type's own
  namespace matches, not one of them.

`AC-ANLZ-012.6` had a sharper edge than "the wrong one": the declarations are held in an unordered
set, so which of two overlapping ones was credited — and which was then reported stale — was not
determined by anything an author could see. Its cost is stated rather than hidden: once every prefix
is credited, `DALE015` can no longer report a declaration that another subsumes.

## Visibility and gating predicates

The **grammar** both predicate families share is [`config-gating.md`](config-gating.md)'s, and so
are the inclusion gate's own rules (`AC-GATE-011.1`–`011.11`). The visibility predicate's
authoring diagnostics are stated here, because no other page states them: `[Presentation(VisibleWhen
= …)]` only hides a member that still exists and still publishes, so it has no runtime refusal to
be the compile-time door onto.

- `AC-ANLZ-013.1` (Event-driven): WHEN a visibility predicate does not parse, or references a name
  that does not resolve to a service property of the annotated member's own service or of a named
  sibling service, THE SYSTEM SHALL report `DALE041`.
- `AC-ANLZ-013.2` (Ubiquitous): THE SYSTEM SHALL judge the predicates of an abstract logic block,
  resolving each against the service the declaring block itself carries.
- `AC-ANLZ-013.3` (Event-driven): WHEN a visibility predicate resolves but breaks the type discipline
  — a reference outside bool, enum, an integer kind and string; a write-only reference; a relational
  operator on a non-integer; a non-homogeneous list; an unquoted enum member; or a bare reference
  that is not a bool — THE SYSTEM SHALL report `DALE042`.
- `AC-ANLZ-013.4` (Ubiquitous): THE SYSTEM SHALL resolve a visibility predicate against every service
  element a block carries, its components' included, whether those are declared in source or read out
  of a referenced assembly — and SHALL report only on a predicate declared in source, one read from
  metadata having been judged when its own assembly was built.
- `AC-ANLZ-013.5` (Ubiquitous): THE SYSTEM SHALL parse and type-check a visibility predicate and
  SHALL never evaluate it, so a predicate that parses and type-checks draws no diagnostic whatever it
  would evaluate to.
- `AC-ANLZ-014.1` (Ubiquitous): THE SYSTEM SHALL judge an inclusion gate wherever the attribute may be written — a property, a method, or a class — and report where the gate is never evaluated. GAP: stated and tested as `AC-GATE-011.1`, `011.2`, `011.3` and `011.10`, whose tests live in this project; this line records that all three declared targets are read.
- `AC-ANLZ-014.2` (Event-driven): WHEN an `[InstantiationParameter]` is declared on a property that is
  not public THE SYSTEM SHALL report `DALE044` as a warning naming the accessibility.
- `AC-ANLZ-014.3` (Event-driven): WHEN an `[InstantiationParameter]` is assigned outside its declaring type's constructor or object initializer THE SYSTEM SHALL report `DALE044`, and SHALL judge only an assignment written in the declaring type itself. GAP: the first clause is `AC-GATE-011.8`'s and tested there; the second guards against a second report from another type's code, which no single-compilation fixture distinguishes from silence.
- `AC-ANLZ-014.4` (Ubiquitous): THE SYSTEM SHALL resolve a gated property's interface binding by
  symbol, so a gate on a property typed as a *generated* contract interface is reported as
  ungateable in a build where that interface is an error type.

`AC-ANLZ-013.2` matters to anyone shipping a library of base blocks: skipping abstract declarations
meant a library's own predicates were validated in its consumers' builds and never in its own.
A predicate must resolve where it is written, so one naming a property only a subclass declares is
reported at the abstract declaration — which is the rule, not an accident of it.

`AC-ANLZ-014.4` states a limitation rather than a guarantee, and it is the one place on this page
where the code is known to be wrong. § 5's error-type problem reaches the gateable test: in a
Metalama-hosted build a generated contract interface is not in `AllInterfaces`, so a legitimately
gated binding draws an error saying it cannot be gated. The remedy an author would reach for —
naming the interface in `[LogicBlockInterfaceBinding(typeof(…))]` — is written in terms of the same
unresolved type. The fix is the by-name half of the two-way lookup `ServiceRelationAnalyzer` already
carries; it is recorded in [`_findings.md`](_findings.md).

## Service relations

`[ServiceRelation]` declares an edge the cloud draws between two services. Its declaration
discipline has no other page: [`contracts.md`](contracts.md) names `DALE045` as this area's, and
[`introspection.md`](introspection.md) states what a relation carries once it is emitted. This is the
one id whose rules split across two severities.

- `AC-ANLZ-021.1` (Event-driven): WHEN `[ServiceRelation]` is declared on a class without
  `[LogicBlockContract]`, or names an `OutwardsInterface` that is neither of the contract's two role
  names, or leaves either of them empty or whitespace, THE SYSTEM SHALL report `DALE045` as an error.
- `AC-ANLZ-021.2` (Event-driven): WHEN one contract declares a `RelationType` twice THE SYSTEM SHALL
  report `DALE045` as an error on the duplicate alone, and SHALL not count that declaration towards
  the cross-contract check.
- `AC-ANLZ-021.3` (Event-driven): WHEN two contracts in one compilation declare the same
  `RelationType` THE SYSTEM SHALL report `DALE045` as a warning on each.
- `AC-ANLZ-021.4` (Event-driven): WHEN a logic block holds a public property whose type implements a
  relation-bearing contract interface but carries no service surface THE SYSTEM SHALL report
  `DALE045` as a warning, once per property however many such interfaces that type implements.
- `AC-ANLZ-021.5` (Ubiquitous): THE SYSTEM SHALL resolve a relation-bearing interface both by symbol
  and by the name written in a base list, so a contract interface that does not resolve is still
  matched.

`AC-ANLZ-021.4` is the silent case the whole id exists for: the endpoint wires normally and emits no
relation half, so the block works and the cloud draws no edge. `AC-ANLZ-021.5` is § 5's rule as a
criterion — the by-symbol path alone reaches a contract in a *referenced* assembly and nothing else,
and this is the worked example that convention doc points at.

## Emission knobs

The six emission diagnostics are [`emission.md`](emission.md)'s rules
(`AC-EMIT-012.1`–`012.7`), and their tests live in this project and cite those ids. Two facts about
how the analyzers reach them are this page's.

- `AC-ANLZ-015.1` (Ubiquitous): THE SYSTEM SHALL resolve the change thresholds a compilation can see once per compilation, across its own assembly and every referenced assembly that references the SDK. GAP: proven by `AC-EMIT-012.1`'s cross-assembly tests, which live in this project; the once-per-compilation half is a performance choice with no observable.
- `AC-ANLZ-015.2` (Ubiquitous): THE SYSTEM SHALL accept a duration token the way the runtime's parser accepts it — surrounding whitespace, a leading sign, an upper-case unit — and reject what it rejects. GAP: stated and tested as `AC-EMIT-012.3`; this line records that the compile-time mirror is bound to the runtime's grammar and may not drift from it.

## Observability

- `AC-ANLZ-016.1` (Event-driven): WHEN a computed observable property's getter reads a **property**
  of a struct-typed field or property the type owns THE SYSTEM SHALL report `DALE031`, at most once
  per pair, covering the null-conditional form as well as the plain one.
- `AC-ANLZ-016.2` (Ubiquitous): THE SYSTEM SHALL report nothing for a struct field read, a method
  call, a `nameof`, a whole-value read, a reference-typed instance, a static, a local, a read through
  another object, a `readonly` field, a get-only property, or a field a base type declares.

Every exemption in `AC-ANLZ-016.2` is a claim about someone else's compiler, and one of them was
wrong for a year: a struct *field* read is tracked, and `DALE031` was telling authors their value
went stale when it did not. An exemption test proves only that the rule stays quiet, never that the
exempted shape works.

## Scenario wire

- `AC-ANLZ-017.1` (Event-driven): WHEN a `[ScenarioWire]` direction names a type the scenario codec
  cannot build THE SYSTEM SHALL report `DALE046` naming the member path, once per direction.
- `AC-ANLZ-017.2` (Event-driven): WHEN the named type does not resolve THE SYSTEM SHALL report
  nothing.

## Reaching a consumer's build

An analyzer that is referenced is not necessarily running, and
[`../testing-conventions.md`](../testing-conventions.md) § 3 is why that has its own standing gate.

- `AC-ANLZ-018.1` (Ubiquitous): THE SYSTEM SHALL ship the analyzer assembly inside the `Vion.Dale.Sdk` package under `analyzers/dotnet/cs`, so a consumer referencing the package receives every diagnostic. GAP: observable only from a packed artifact, which is the post-pack gate's subject rather than a test's.
- `AC-ANLZ-018.2` (Ubiquitous): THE SYSTEM SHALL judge the declarations of every project that references the analyzer assembly as an analyzer, and no others. GAP: which projects those are is a build-graph fact, grep-enumerable from the csprojs; `AC-ANLZ-018.4` proves the mechanism on two of them.
- `AC-ANLZ-018.3` (Event-driven): WHEN the analyzer assembly is absent at pack time THE SYSTEM SHALL produce a package carrying no analyzers and no warning. GAP: today's behaviour, recorded in [`_findings.md`](_findings.md) — the assertion that a packed artifact carries the analyzers belongs in the post-pack gate, which owns the packaging path.
- `AC-ANLZ-018.4` (Ubiquitous): THE SYSTEM SHALL fail a build of a probed project when the
  analyzer-wiring probe is linked in, and SHALL keep the probe out of an ordinary build.
- `AC-ANLZ-018.5` (Ubiquitous): THE SYSTEM SHALL compile the predicate parser into the analyzer assembly and the runtime assembly from one source. GAP: a build-graph fact; the two compilations agreeing is pinned by the vendored conformance vectors, which are premise tests by design and cite no criterion.

The probe's own hazard is stated once here and guarded in its suite: it shells a real `dotnet build`,
and a child build that carries no version stamp will overwrite the outputs `dotnet pack` then ships.
Release 0.11.1 is what that costs.

## The generator's own diagnostics

The source generator reports outside this registry, and an author needs to know the difference: a
`SourceGenerator`-category diagnostic has no `DALE` id, no tag, and is absent from any build where
the generator does not run.

- `AC-ANLZ-019.1` (Ubiquitous): THE SYSTEM SHALL report the source generator's own findings under the category `SourceGenerator`, outside the `DALE` registry. GAP: observable only by driving the generator itself, which this project's analyzer harness does not do.
- `AC-ANLZ-019.2` (Ubiquitous): THE SYSTEM SHALL report them with a constant message format, so a brace in a message is not read as a placeholder. GAP: the descriptors are private to the generator and reachable only through it; the shape is fixed at the declaration.

## Suppression

- `AC-ANLZ-020.1` (Ubiquitous): THE SYSTEM SHALL leave every diagnostic configurable and suppressible
  by the ordinary mechanisms — `#pragma warning disable`, `[SuppressMessage]`, `NoWarn` and an
  `.editorconfig` severity entry — carrying no tag that opts one out.

A suppression is the author's declaration that the shape is intended, and this repository's own
fixtures are the worked examples — thirty-two `#pragma warning disable DALE…` sites across fourteen
files as this page is written, each beside the rule it violates on purpose, and each carrying its
reason where the fixture does not say it. The set is grep-enumerable rather than listed here, because
it changes with every fixture. A test project that suppresses a diagnostic is asserting that the
runtime door behind it still works.

## The test discipline

Not restated here. [`../testing-conventions.md`](../testing-conventions.md) § 3 owns it: an analyzer
test builds a small compilation from stub sources, that compilation is not what a consumer's build
looks like, and the difference has already shipped a dead analyzer. Two obligations follow from it
and are visible in this project — a `CS0246` pin wherever a rule resolves a contract interface, and
the committed wiring probe of `AC-ANLZ-018.4`.

## Carried, not specified

- Roslyn's own analysis model — action kinds, concurrency, the compilation-start cache.
- The predicate dialect and its grammar ([`config-gating.md`](config-gating.md), decision 0077).
- The emission policy the six knob diagnostics mirror ([`emission.md`](emission.md)).
- The generated surface the contract diagnostics guard ([`contracts.md`](contracts.md)).
- The documentation site that reads the XML docs `DALE013`–`DALE015` police.
