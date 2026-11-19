---
slug: shared-handler-registration
status: archived
blocked-on: none           # for parked docs: what's blocking + ref
areas: PLUG, ANLZ, MBUS
author: Jonas Bertsch (drafted in a Claude session)
created: 2026-11-19
updated: 2026-11-20
supersedes: none           # path of a superseded change doc, or none
---

# A shared assembly that declares handler actors ships an `IConfigureServices`, and the build says so

> Change doc — one in-flight change. The `Spec delta` below is distilled into the current-truth
> pages under `docs/specs/` in the implementation PR, then this doc is archived
> (`spec-change.ps1 archive <slug>`). Process: `docs/spec-process.md`. Never put change narrative
> inline in a spec page — it lives here.

## At a glance

### Summary

The host runtime reports an error for an assembly marked `[DaleSharedAssembly]` that declares
handler actors but no `IConfigureServices` (`dale/AC-LOAD-005.3`). No SDK page, doc comment or
analyzer states that rule. So `Vion.Dale.Sdk.MBus` shipped in 0.17.0 without one, and every gateway
that loads it logs the error. This change adds a `DALE049` diagnostic that refuses that shape at
compile time, in this repo and in any consumer's shared library. It also gives the M-Bus package the
registration it lacks.

### Spec implications

- [`plugin-loading.md`](../../specs/plugin-loading.md) § 3 gains one criterion, `AC-PLUG-010.1`:
  `DALE049`'s firing rule. That page owns `[DaleSharedAssembly]`, and the rule is about what a marked
  assembly owes.
- The page also gains a sentence citing the runtime's half, `dale/AC-LOAD-005.3`.
- [`analyzers.md`](../../specs/analyzers.md) names `DALE049` in its "Cited rather than restated"
  paragraph. It does the same for `config-gating.md`'s `DALE043` and `DALE044`. No `ANLZ` criterion
  changes: `AC-ANLZ-001.4` (the compilation-end tag) and `AC-ANLZ-001.6` (what `Error` means) already
  cover the new id.
- [`mbus.md`](../../specs/mbus.md) § The published surface lists the registration among the plumbing.
  That is prose; no `MBUS` criterion changes (`D6`).

### Decisions

**(a) Ratified by the operator:**

- `D1` — **The rule is enforced at compile time by a new diagnostic, and the M-Bus package gains its
  registration in the same change** (operator, 2026-11-19, in session). This reverses the archived
  M-Bus change's implementation decision "No `DependencyInjection`"
  ([`2026-11-05-mbus-service-provider.md:980-982`](2026-11-05-mbus-service-provider.md)).
  That decision was sound against everything the SDK documents, and it missed the runtime's rule
  (§ How it happened).

**(b) Decided here, each with its rationale:**

- `D2` — **The diagnostic mirrors the runtime's test exactly.** It counts:
  - an assembly that applies the SDK's own `DaleSharedAssemblyAttribute`, recognised by full name as
    `AC-PLUG-005.8` recognises it;
  - a concrete class declared in source that implements `IServiceProviderHandlerActor`. Development-only
    handlers are included, because the runtime's scan counts them too
    (`dale/Dale/Libraries/LibraryServiceRegistration.cs:138`);
  - the absence of any concrete class declared in source that implements `IConfigureServices`, at any
    accessibility. The runtime reads `GetTypes()`, which includes non-public types (`:134`).

  The diagnostic predicts the runtime's report, so every divergence would be either a false alarm or
  a miss.
- `D3` — **One diagnostic per assembly**, located at the `[assembly: DaleSharedAssembly]` application
  and naming every handler type it found. The runtime reports one line per assembly in the same shape
  (`:146`), and the fix is one class per assembly. The check needs the whole compilation, so it is a
  compilation action and its descriptor is tagged `CompilationEnd` (`AC-ANLZ-001.4`).
- `D4` — **The firing rule is stated on `plugin-loading.md`**, not on `analyzers.md`. The roster
  gives `PLUG` `[DaleSharedAssembly]` (`docs/spec-process.md` § The roster), and `analyzers.md` cites
  rather than restates a rule another page owns.
- `D5` — **M-Bus's registration is an `[InternalApi]` `DependencyInjection` in `ConfigureServices.cs`
  that registers `MBusHandler` as transient**, as the I/O packages register theirs
  (`Vion.Dale.Sdk.DigitalIo/ConfigureServices.cs:18-19`, `AnalogIo/ConfigureServices.cs:18-19`).
  - **Not published.** RTU's is published because a development host constructs it by hand
    (`modbus.md`, § The published surface's second judgment). The M-Bus example has no development
    host, so no consumer constructs one (§ 1 of `sdk-surface-conventions.md`).
  - **The registration is a discovery entry, not how the handler gets built.** A handler actor is
    built with `ActivatorUtilities` (`Vion.Dale.ProtoActor/ActorSystem.cs:303`).
  - **It validates in every container that invokes it.** `MBusHandler` takes a `TimeProvider` and a
    logger (`MBusHandler.cs:50`), and each of those containers calls `AddDaleSdk()`, which registers
    the clock: the runtime (`dale/Dale/Program.cs:503`), the parser
    (`Vion.Dale.LogicBlockParser/Program.cs:118`) and the development host
    (`Vion.Dale.DevHost/DevHostBuilder.cs:165`).
- `D6` — **No M-Bus criterion is minted for the registration.** Under `DALE049` the package's own
  build is the proof, so a criterion saying "the M-Bus package ships an `IConfigureServices`" would
  restate `AC-PLUG-010.1` for one package (over-determined, with no mutation of its own).
- `D7` — **A committed probe proves the rule fires in a real build** (`sdk-surface-conventions.md`
  § 4, last bullet). A concrete internal handler is linked into `Vion.Dale.Sdk.Modbus.Core` under a
  probe property, and `AnalyzerWiringShould` requires `DALE049`. Modbus.Core is chosen because it is
  shared and declares no handler and no registration
  (`Vion.Dale.Sdk.Modbus.Core/AssemblyAttributes.cs`). The probe reuses the existing probe
  machinery, and no shipped project gains a source file that exists only to fail (§ 5).
- `D8` — **`DaleSharedAssemblyAttribute`'s remarks gain one sentence**: an assembly that declares
  handler actors also contains an `IConfigureServices` implementation. The sentence carries no
  diagnostic id (§ 2 of `sdk-surface-conventions.md`).
- `D9` — **The runtime's side is not changed here.** dale's message says the handlers "will not
  resolve". For `MBusHandler` that is not so, because its dependencies are core's (`D5`); the
  runtime's creation-time report (`dale/Dale/Program.cs:385-388`) already names a handler that cannot
  be built. Whether dale keeps, rewords or drops its check is dale's call. It goes in the PR body's
  `Draft items`, for the operator to file or drop (§ Routing of `spec-process.md`).

**(c) Proposed, waiting for the operator:** question 1, answered `Error` (2026-11-19).

### Reviewer's questions

1. **(c) Severity: `Error` or `Warning`?**
   - **Recommendation: `Error`.** This repository sets no `TreatWarningsAsErrors` (none in
     `Directory.Build.props` or the handler packages' `.csproj`), so at `Warning` the next package
     missing its registration still builds. That is the failure this change exists to stop.
   - **Known consumers are unaffected.** `logic-block-libraries` has two shared assemblies:
     `Ecocoach.Dale.Contracts.Ppc` declares handlers and ships a `DependencyInjection`, and
     `Ecocoach.EnergyManagement.Common` declares no handler. Neither would be reported.
   - **The cost of `Error`.** A consumer this machine cannot see, with a shared handler assembly and
     no registration, would fail its build when it upgrades. That is a source break, not a binary
     one, so package validation does not see it ([`releasing.md`](../../releasing.md) § Every release is
     additive within its major says what the gate does not cover). The ANLZ pass added `DALE048` at
     `Error` within major 0 (`2902b933`).
   - **Alternative: `Warning`.** No consumer build breaks, but this repo is guarded only by review.

   **OUTCOME (2026-11-19, operator, in session): `Error`.** The delta's "fail its build" wording
   stands.
2. **(b) Development-only handlers count (`D2`).** The I/O packages' provider handlers are
   `[DevelopmentOnlyHandler]` and never run on a production host (`io.md`). The runtime counts them
   anyway, and the I/O packages already ship registrations, so nothing in the tree changes. Confirm
   that mirroring the runtime is preferred over a narrower rule.

   **OUTCOME (2026-11-20): kept as decided in `D2`.** The operator ratified question 1 and raised
   nothing here. It is open to the pull request's review, and the `development-only` data row is what
   a change would move.
3. **(b) No M-Bus criterion (`D6`).** Confirm that the package build failing under `DALE049` is
   enough proof, rather than a per-package surface test that names the registration, as RTU's test
   does (`ModbusRtuPackageSurfaceShould.cs:67`).

   **OUTCOME (2026-11-20): kept as decided in `D6`.** The operator raised nothing here, and it is open
   to the pull request's review. The ordinary build in `AnalyzerWiringShould` now builds the M-Bus
   package and fails on any error it reports, `DALE049` included, so the package's own build is
   checked by a test as well as by the solution build.

---

## Full design

### How it happened

- **The runtime's rule.** `RegisterSharedAssemblyServices` logs at `Error` when a shared assembly
  has concrete handler actors and no concrete `IConfigureServices`
  (`dale/Dale/Libraries/LibraryServiceRegistration.cs:134-147`; spec `dale/AC-LOAD-005.3`). dale
  added the check on 2026-08-26 (`2510a71`, VION-124) and narrowed it to assemblies with handler
  actors, the case where it judged "the message is true".
- **What the SDK said.** Nothing. `DaleSharedAssemblyAttribute`'s remarks list the two sanctioned
  uses and stop. `plugin-loading.md` § Eager loading says only that the runtime invokes a shared
  library's `IConfigureServices`. No surface test checks for one.
  `MBusPackageSurfaceShould` checks the surface marks and the shared mark only.
- **The M-Bus decision.** The design named "a public `DependencyInjection : IConfigureServices`, as
  RTU's" (`2026-11-05-mbus-service-provider.md:590`). Implementation found nothing to register and
  dropped it as "surface with no job" (`:980-982`; PR #282's body). Read against the SDK's own docs,
  that was correct.
- **The effect.** On a gateway running a 0.17.0 library that references M-Bus, the runtime logs:
  "Shared assembly Vion.Dale.Sdk.MBus declares handler actors but contains no implementation of
  IConfigureServices — those handlers will not resolve until the assembly registers them
  (HandlerActors=Vion.Dale.Sdk.MBus.MBusHandler)". The handler is very likely built and running
  anyway (`D5`, `D9`). That is inferred from the code and not checked on a gateway. The check: M-Bus
  reads complete, and there is no "MQTT handler actor Vion.Dale.Sdk.MBus.MBusHandler could not be
  created" line.

This is the second time the two repositories have disagreed on this check: VION-124 was the first,
when it fired on `Vion.Dale.Sdk.Modbus.Core`, which has nothing to register.

### The analyzer

```csharp
// DaleDiagnostics.cs — next free id; 006 and 029 are retired, 048 is the last live one
public static readonly DiagnosticDescriptor DALE049_SharedHandlerAssemblyWithoutRegistration = new(
    "DALE049",
    "Shared assembly declares handler actors but no IConfigureServices",
    "Assembly '{0}' is marked [DaleSharedAssembly] and declares handler actors ({1}) but no IConfigureServices implementation. " +
    "The host runtime reports such an assembly at startup. Add a class implementing IConfigureServices to this assembly.",
    Category,
    DiagnosticSeverity.Error,          // question 1
    true,
    customTags: WellKnownDiagnosticTags.CompilationEnd);
```

`SharedHandlerRegistrationAnalyzer` registers one compilation action, as
`PublicApiDocumentationAnalyzer` does (`:29-34`):

1. Look for the SDK's own `DaleSharedAssemblyAttribute` on `compilation.Assembly.GetAttributes()`,
   by full name. With no mark, return.
2. Walk the source-declared named types, nested types included, as `PublicApiDocumentationAnalyzer`
   walks them. Collect the non-abstract classes implementing `Vion.Dale.Sdk.Abstractions.IServiceProviderHandlerActor`
   and note whether any non-abstract class implements `Vion.Dale.Sdk.Core.IConfigureServices`.
3. If there are handlers and no registration, report once, at the attribute's
   `ApplicationSyntaxReference`, naming the handlers in declaration order.

Both interfaces live in `Vion.Dale.Sdk`, a referenced assembly, so they resolve by symbol in every
build. Unlike the contract-interface checks of § 5 of `sdk-surface-conventions.md`, Metalama's
generated types do not touch this check.

### Alternatives considered

- **A per-package surface test.** The next package's author would have to remember to write it,
  which is the same mistake again. The analyzer reaches every package that carries the analyzer
  reference, and every SDK package carries it today, with a committed wiring probe per package
  (`AC-ANLZ-018.2`'s family).
- **A solution-wide reflection test over every shipped assembly.** It needs the roster maintained by
  hand and protects no consumer. The analyzer ships in `Vion.Dale.Sdk` (`AC-ANLZ-018.1`), so a
  consumer's own shared handler library gets the same check.
- **Changing only the runtime.** It would remove the noise but leave the rule unstated on the SDK
  side, and it needs a dale release on every gateway. It is recorded as a draft item (`D9`), not
  instead of this change.

### What it does not cover

- A new package that leaves out the analyzer reference escapes every `DALE` rule, this one included.
  Every SDK package carries that reference today (`sdk-surface-conventions.md` § Known
  non-conforming code, "The analyzer pack does not run over every SDK project"), and a new package
  that drops it is a gap this change does not close.
- A logic-block library with no `IConfigureServices` at all (`dale/AC-LOAD-004.2`) is a different
  rule, and this change does not touch it.

### Reaching gateways

The fix reaches a gateway only when the libraries there are rebuilt against the release that carries
it. The first plugin loaded fixes which version of a shared assembly every plugin uses
(`2026-11-05-mbus-service-provider.md:592-594`).

---

## Drift checkpoints

> One line per divergence discovered during implementation:
> `YYYY-MM-DD: <what changed and why>`. Never inline in a spec page. A checkpoint that fixes a
> CLASS bug states the sibling sweep (done / N/A / handed off).

- 2026-11-20, `T-001`: the message names the handler classes **ordered by name**, not in declaration
  order as § The analyzer had it. Walking the declared types gives no order a reader can see across
  files, and a sorted list is the same on every build.
- 2026-11-20, `T-001`: the tests compile the stubs as a **referenced assembly**, because the mark
  counts only where another assembly defines it (`D2`, `AC-PLUG-005.8`). The ordinary harness injects
  the stubs as source, and the analyzer would treat them as the assembly's own attribute. The
  referenced-assembly harness that `CrossAssemblyAnalyzerTests` already had moved to
  `Helpers/MetadataCompilation.cs`, since two test classes now use it. The stubs gain
  `DaleSharedAssemblyAttribute`, `IConfigureServices`, `IServiceProviderHandlerActor`,
  `ServiceProviderHandlerBase` and `DevelopmentOnlyHandlerAttribute`.
- 2026-11-20, `T-001`: defect proof for `T-002`, run and observed. With the analyzer in place,
  `dotnet build Vion.Dale.Sdk.MBus/Vion.Dale.Sdk.MBus.csproj` fails with
  `AssemblyAttributes.cs(3,12): error DALE049: Assembly 'Vion.Dale.Sdk.MBus' is marked
  [DaleSharedAssembly] and declares handler actors (Vion.Dale.Sdk.MBus.MBusHandler) but no
  IConfigureServices implementation. …`
- 2026-11-20, `T-003`: the probe gets a **build set of its own**
  (`-p:DaleSharedHandlerAnalyzerWiringProbe=true`) rather than riding the Modbus probe's property.
  `DALE049` is an error, so under the shared property Modbus.Core would fail to compile and take the
  Rtu and Tcp builds, and so their `DALE014` rows, down with it. The probe implements
  `IServiceProviderHandlerActor` directly, the smallest concrete handler the real SDK accepts.

---

## Spec delta (to distill)

> The machine-readable change, one line per id. Grammar:
> `<OP> <ID> -> <target> : <payload>`
>
> - `OP` ∈ `ADDED` | `MODIFIED` | `REMOVED`
> - `target` is **repo-root-relative** (normally `docs/specs/<page>.md`)
> - `payload`: for `ADDED`/`MODIFIED` the EARS text; for `REMOVED` the reason
>
> On the implementation PR each line is applied into the named target; `spec-change.ps1 archive`
> refuses until every line is applied. The `ID` must be an exact token greppable in the target
> after distill (backticks stripped) — a real `AC-`/`SYS-` id in an area this repo owns, never an
> ad-hoc label and never another repo's area. A criterion the delta declares unreachable carries
> its `GAP: <reason>` marker on the delta line.

- ADDED AC-PLUG-010.1 -> docs/specs/plugin-loading.md : WHEN an assembly that applies the SDK's `[DaleSharedAssembly]` declares a concrete class implementing `IServiceProviderHandlerActor` and no concrete class implementing `IConfigureServices` THE SYSTEM SHALL fail its build with `DALE049`, reported once for that assembly and naming every such handler class, and SHALL report `DALE049` for no other assembly.

- MODIFIED AC-PLUG-010.1 -> docs/specs/plugin-loading.md : WHEN an assembly that applies the SDK's `[DaleSharedAssembly]` declares a concrete class implementing `IServiceProviderHandlerActor` and no concrete class implementing `IConfigureServices`, each of any accessibility and development-only handlers included, THE SYSTEM SHALL fail its build with `DALE049`, reported once at the mark and naming every such handler class, and SHALL report `DALE049` for no other assembly.

"Fail its build" is question 1's outcome, `Error`. The `MODIFIED` line states as clauses what the
tests' data rows prove beyond the `ADDED` text: accessibility, development-only handlers, and the
location.

---

## Tasks

> One-commit tasks, each tagged with ≥1 AC id. Ephemeral — they live and die with this change doc.
> Plain list, no checkboxes — the PR's per-task commits are the completion record.

- `T-001` (`AC-PLUG-010.1`): the `DALE049` descriptor and the analyzer, with its tests.
  - The firing case, with its data rows for the cases that report nothing: unmarked; marked with an
    attribute the assembly declares itself; no handler; an abstract handler only; a handler beside a
    registration; a handler beside an internal registration.
  - A development-only handler counts.
  - Mutation: remove the compilation action's report, and the firing test goes red. Run and observe
    it.
  - Flip this doc to `in-flight`. Build `Vion.Dale.Sdk.MBus` and record that it fails with `DALE049`
    as the defect proof for `T-002`.
- `T-002` (`AC-PLUG-010.1`): `Vion.Dale.Sdk.MBus/ConfigureServices.cs`, an `[InternalApi]`
  `DependencyInjection` registering `MBusHandler` (`D5`). After it, the solution builds clean.
- `T-003` (`AC-PLUG-010.1`): the real-build probe (`D7`). Add a probe handler under
  `Vion.Dale.Sdk.Generators.Test/AnalyzerWiring/`, a conditional link into
  `Vion.Dale.Sdk.Modbus.Core`, and an `AnalyzerWiringShould` case requiring `DALE049`. Mutation:
  delete the analyzer from the generator assembly, and the case goes red.
- `T-004`: the `DaleSharedAssemblyAttribute` remark (`D8`). Distill into `plugin-loading.md`, add the
  `analyzers.md` citation and the `mbus.md` plumbing list, then archive.
