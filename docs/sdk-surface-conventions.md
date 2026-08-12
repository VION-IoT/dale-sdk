# SDK surface conventions

Read this before adding or changing anything a library author sees: an attribute, a public type or
member, an analyzer, or a shape that reaches the introspection JSON.

This repo is a **published SDK**. Every public name here becomes someone else's compile error later,
and every introspection identifier becomes a translation key in the cloud
([`identifier-stability.md`](identifier-stability.md)). The rules below exist because the cost of a
wrong surface is paid by consumers, not by this repo's tests.

## 1. The surface is the smallest thing that does the job

A new attribute, option, generator or convenience wrapper needs a consumer that exists **now**, not a
consumer that might. The recurring outcome of asking "is this needed?" in this repo has been deletion:
a source generator was dropped as *"overkill for one line of boilerplate"*, parameterised scenarios
were dropped because they *"can be done in user land via c#, no magic from SDK"*, and an explicit
`Order = X` attribute was dropped because *"declaration order is enough"*.

- **Syntactic sugar earns its keep or goes.** Where a general mechanism already covers a case, the
  special-cased shorthand is removed rather than kept for symmetry. This is why the four hard-coded
  HAL scenario steps became one generic `serviceProviderSet` / `serviceProviderExpect` pair
  (RFC 0010) — platform-authored service providers now use exactly the path a third-party author uses.
- **One line of boilerplate is not a reason to generate code.** Reach for the generator when the
  boilerplate is wrong-able, not when it is merely repetitive.
- **Convenience for one call site is not a feature.** Name the second consumer or don't add it.

## 2. XML docs say what it is and how to use it — nothing else

Attribute and public-member docs are the SDK's primary documentation surface: they are what a library
author reads in IDE tooltips, and they are published to the docs site. Keep them about the reader.

- **No RFC numbers, no analyzer IDs, no customer names, no design history.** Those belong in
  `docs/rfcs/`, the analyzer's own message, and git. The instruction that set this rule:
  *"keep the ServiceRelationAttribute xmldoc lighter, no rfc mentioning, no analyzer number, no
  customer specific examples. just explain what it is for and how to use it correctly"*.
- **Length is a cost.** *"technically correct but i find the remarks too verbose. focus on what is
  important"* — a `<remarks>` block that restates the summary in longer words is worse than no block.
- **Examples are generic.** `[ServiceRelation]`'s `<example>` uses a heat pump, not the consumer whose
  request prompted the feature.
- **Verify each claim against the code that implements it**, not against the neighbouring docstring.
  Docs in this repo have been wrong before, including inverted; a `<summary>` copied from a sibling
  member inherits its errors.

[`Vion.Dale.Sdk/Core/ServiceRelationAttribute.cs`](../Vion.Dale.Sdk/Core/ServiceRelationAttribute.cs)
is the reference shape: a summary that states the mechanism, `<para>` blocks for the three things a
first-time user gets wrong, and one generic `<example>`.

### How they actually render

XML docs on `[PublicApi]` types are a **shipping doc surface**: CI runs
[`scripts/generate-api-reference.cjs`](../scripts/generate-api-reference.cjs) and pushes
`api-reference.md` to the `documentation` repo. Three things govern how they land:

- **`cleanXmlText` strips every tag** except `<c>` (→ backticks) and `<see cref>` (→ the last name
  segment), then collapses all whitespace. `<para>`, `<b>`, `<list>` and `<example>` vanish, so a
  carefully structured docstring renders as one run-on paragraph.
- **A type's `<summary>` renders as a paragraph and its `<remarks>` as a `>` blockquote.** Those are
  the only two places long-form text survives.
- **A member renders as one bullet — `` - `Name` — <summary> `` — and its `<remarks>` is dropped
  entirely.** Property, method and field rendering all read `.summary` and nothing else.

So: keep member summaries to one line or they render as a giant bullet, put anything longer in the
**type's** `<remarks>`, and never put load-bearing text in a member's `<remarks>` — no reader of the
docs site will ever see it.

To check locally, do a **Release** build, then:

```bash
node scripts/generate-api-reference.cjs --root . --exclude "*.Test" --out /tmp/api-reference.md
```

**Delete `<Project>/bin/Release/frompkg` first.** The generator globs `bin/Release/**/<Assembly>.xml`
and takes the first candidate, so a leftover `frompkg` directory from an old `stage-xml-docs.ps1` run
wins over `bin/Release/<tfm>/` and the output silently shows months-old docs — which looks exactly
like "my edit didn't take". CI is unaffected; it builds clean.

## 3. Delete rather than deprecate — while that is still cheap

The SDK is pre-1.0 with a known consumer set. Dead surface is removed in the same change that
supersedes it: *"breaking is no problem, the devhost is the only user"*, *"if the sugar is not that
much of a win i'd prefer removing it"*, *"let's do it right as long as we can"*.

- When a mechanism is generalised, **delete the specific one and migrate its callers in the same PR**
  (RFC 0010 deleted the four HAL step kinds; RFC 0019 deleted `AutoDetectServiceRelationsForInterface`,
  `ServiceDeclaration<T>.DefineRelation` and the orphan example interfaces).
- **Thin wrappers belong to the caller, not the API.** If `IDevHostControl` gains a member only the SPA
  uses, the question is whether it should live in the UI instead.
- **Nullability and required-ness are decided while the surface is young.** A field that is
  `[JsonIgnore]`-when-null and nullable "for compatibility" needs a live consumer that depends on it;
  otherwise make it required. Breaking a controlled consumer set is a one-line bump.
- Where a removal is genuinely deferred, say so at the declaration and name what blocks it.

## 4. Every author-facing rule ships with an analyzer

A rule a library author can violate in C# is enforced by a Dale analyzer, not by prose or a runtime
throw alone. That is the standing expectation here — *"The analyzer should be there to surface bad
usage"*, *"it must be clear to users what formats are valid for which type, and the SDK analyzer
validating it, right?"* — and it is why
[`DaleDiagnostics.cs`](../Vion.Dale.Sdk.Generators/Analyzers/DaleDiagnostics.cs) has allocated
`DALE001`–`DALE045`, of which **43 are live**.

Adding one:

- **Next free ID, one entry per ID**, following the house descriptor pattern. **IDs are never reused
  once retired** — the file says so, and it holds: `DALE006` (the deleted `[StatusIndicator]`) and
  `DALE029` (the fixed Metalama `field`-keyword bug) each leave a comment where the descriptor was.
  That is why 45 IDs yield 43 descriptors.
- **One ID may carry rules at two severities.** `DALE045` is the precedent: one `Error` descriptor,
  and advisory findings emitted through the `Diagnostic.Create(descriptor, location,
  effectiveSeverity, …)` overload with `DiagnosticSeverity.Warning`. Prefer that over two descriptors
  sharing an ID — and know the consequence: configuring or suppressing the ID in `.editorconfig` then
  moves the errors and the warnings together.
- **A rule that needs the whole compilation** registers a compilation-end action and tags its
  descriptor `WellKnownDiagnosticTags.CompilationEnd` (as `DALE045` does for duplicate-`RelationType`
  detection). Without the tag the diagnostic is dropped in IDE live analysis.
- **A value type reaching a service member usually needs exempting in more than one analyzer.** The
  supported-type gate is not a single rule: `DALE003` (unsupported service-property type), `DALE016`
  (struct must be a flat readonly record) and `DALE008` (array must be `ImmutableArray<T>`) each judge
  a member independently, and a value type is judged by more than one of them. Adding a supported type
  to one and not the others ships a type that the SDK claims to support and the build rejects.
- **Test composed behaviour, not only each rule in isolation.** Per-analyzer tests that pass while the
  combination rejects valid code is the exact failure mode above.
- **Prove the analyzer fires in a real build.** See § 5 — an analyzer can pass every unit test and be
  completely dead in a consumer's compilation.

## 5. Analyzers see a different compilation than their tests do

**Contract interfaces emitted by `LogicClassGenerator` are not visible to analyzers in a real
build.** Every logic-block project references Metalama, which replaces the compiler task; in that
pipeline a `class ChargingPoint : IPing` whose `IPing` is generated resolves to an **error type**, and
`AllInterfaces` simply does not contain it. A symbol-only check against contract interfaces therefore
compiles, passes its stub-based unit tests, and no-ops for every real consumer.

An analyzer that keys off contract interfaces must resolve them **both ways**:

- **by symbol** — the only path that reaches a contract in a *referenced* assembly; and
- **by name** — base-list identifiers matched against this compilation's contracts'
  `BetweenInterface` / `AndInterface` strings.

Pin it with a test whose contract interface is genuinely unresolved (the test expects `CS0246`
alongside the Dale diagnostic). A test using a resolvable stub interface does not reproduce the real
build and will pass either way. `ServiceRelationAnalyzer` /
[`ServiceRelationAnalyzerTests`](../Vion.Dale.Sdk.Generators.Test/ServiceRelationAnalyzerTests.cs) is
the worked example — see `RelationBearingInterfaces` for the two-way lookup and the two `CS0246` tests
for the pin. The session that found this also tried flipping `ConfigureGeneratedCodeAnalysis` to
`Analyze` and reported no change, so don't spend a round there.

## 6. Names are explicit and match the platform's vocabulary

The same concept is named the same way in dale-sdk, the Dale runtime, cloud-api and the dashboard.
Where this repo invented a shorter name, it has been corrected: *"on naming: use serviceProvider, not
provider where possible, be explicit"*, and a scenario step field went `"block" -> logicBlock`.

- **No abbreviation of a platform noun.** `serviceProvider`, `logicBlock`, `measuringPoint` — not
  `provider`, `block`, `mp`.
- **Cross-repo terms win over local ones**: logic block *definition* vs *instance*, topology vs logic
  configuration. When in doubt, read `../architecture/systems/` and match it.
- **A type's name says what it does and where it applies.** *"is the ServiceProviderContractWire
  attribute only relevant for devhost ,not dale? if so, it should be clearer from the attribute name"*
  — a name that leaves its scope ambiguous is a finding.
- **Identifiers are translation keys.** Renaming a service, member, contract, interface, enum member or
  enum/struct type orphans authored translations in the cloud. Read
  [`identifier-stability.md`](identifier-stability.md) before any rename.
  **The decoupling knob does not cover everything**: `Identifier =` exists on
  `[ServiceProviderContractBinding]` and `[LogicBlockInterfaceBinding]`, so those C# names can change
  freely. Services, service properties and measuring points have **no** such override — their C# names
  *are* their identifiers, deliberately. So a rename there is a re-translation cost, not a refactor,
  and the PR should say so.

## 7. Silence is a defect

A wrong declaration must fail somewhere a human will see it. Three ways this repo has shipped silence,
each of which cost a round:

- **An empty `else if` that drops a declaration.** A relation whose interface matched zero or more than
  one implemented interface was discarded with no diagnostic — a typo'd relation type was invisible end
  to end. (The code is gone: RFC 0019 replaced it with bind-time throws plus `DALE045`. It is here
  because it took a consumer's bug report to find, not a test.)
- **A "no-throw" test standing in for a round-trip test.** Asserting a message does not throw passes
  when the message is silently dropped. Assert the *consumer received it*.
- **A serialized empty collection where content was expected.** *"contractMappings in the saved json
  should not be `[]`, it should contain the mappings"* — round-trip assertions catch this; smoke boots
  do not.

Fail-closed at bind time is the default; where a surface deliberately fails **open** (the DevHost live
view keeps a member visible when its predicate throws, matching the dashboard editor), that is a stated
decision and the catch logs a warning.

## 8. The public surface and its snapshots

- **A `[PublicApi]`-marked type is the gate.** `generate-api-reference.cjs` source-scans each project's
  `.cs`; an assembly with zero `[PublicApi]` types is skipped outright, and any assembly with at least
  one is in. There is no curated list and no opt-in file — `Vion.Dale.Sdk.Modbus.Core` is in the
  manifest on the strength of its 12 marked types alone, and declares no assembly-level attribute at
  all (its `PublicApiConfig.cs` is a local shim defining the attribute, because that project
  deliberately does not reference `Vion.Dale.Sdk`).
- **`[assembly: PublicApiNamespace]` does not gate anything.** It drives namespace grouping in the
  generated reference, and it is what `DALE014` (a public type in a declared namespace must be marked
  `[PublicApi]` or `[InternalApi]`) and `DALE015` (a declared namespace with no public types) key off.
  Twelve assemblies declare it; twelve are in the manifest; **the two sets are not the same twelve.**
- **So the question "can this change move the snapshot?" is answered by grepping for `[PublicApi]`,**
  not by looking for an opt-in. `Vion.Dale.DevHost`, `Vion.Dale.Cli`, `Vion.Dale.Plugin`,
  `Vion.Dale.ProtoActor` and `Vion.Dale.LogicBlockParser` are absent today because they contain **zero**
  marked types — not because they lack an opt-in. Mark one type in any of them and the manifest moves.
  (`IDevHostControl` has been described in briefs as `[PublicApi]`; it carries no such attribute, which
  is the only reason DevHost changes have not moved the snapshot.)
- **`--exclude "*.Test"` is load-bearing, not hygiene.** `Vion.Dale.Sdk.Generators.Test` has five
  `[PublicApi]` types and would otherwise ship in the manifest. The five **TestKits** are not excluded —
  they are published packages and belong there.
- **`docs/snapshots/publicapi-manifest.json` and `cli-help-snapshot.txt` are regenerated by CI and
  auto-committed onto the PR head.** Pull and reconcile before pushing again; never force-push over the
  bot. The manifest is *type-level*: removing a member changes no snapshot, so "no drift" does not mean
  "no breaking change".
- **The introspection JSON is the contract cloud-api reads.** Changing what
  `LogicBlockIntrospection` / `PropertyMetadataBuilder` emit is a wire change even when no C# signature
  moves; pin it with a golden assertion and verify it from a packed artifact (see
  [`testing-conventions.md`](testing-conventions.md) § 4).

## Known non-conforming code

Named rather than excused; the conventions above stand.

- `Vion.Dale.Cli/Models/DalePluginInfo.cs` is a hand-maintained mirror of the parser's publish-JSON
  shape, and it drifted from that shape badly enough to need a dedicated fix (#123). It duplicates
  `Vion.Contracts` introspection types by choice; the next time it drifts, re-decide that choice rather
  than re-syncing the copy.
- `examples/Vion.Examples.RichTypes`, `examples/Vion.Examples.Presentation` and
  `examples/Vion.Examples.Energy` carry `DaleLocalSource` in only two of their projects, while
  `Emission`, `Gating`, `ModbusRtu`, `ModbusTcp` and `ToggleLight` carry it in three;
  `libraries/Vion.Diagnostics` and `templates/vion-iot-library` carry it in none. The working-tree
  build path is therefore not uniformly available (see [`devhost-conventions.md`](devhost-conventions.md) § 2).
