# Finding ledger

Defects found and deliberately **not** fixed where they were found: too big for the round, or reaching
past the area that found them. The fourteen area passes filled it; any lane adds to it the same way
([`../spec-process.md`](../spec-process.md) § Routing). One line each, newest last. Triaged in bulk at
the retro ([`../retro/`](../retro/)) — an entry that gets scheduled becomes a Jira item and is struck
here with its key; an entry that is fixed is deleted with the PR that fixes it.

**"Struck" means removed, not left behind as a stub.** Both dispositions take the line out of this
file, and the difference is only where the record goes: a scheduled entry's **Jira key** is recorded
in the filing PR and in whatever table triaged it, a fixed one's record is the fix. Two consequences
worth stating, because `T-009` had to decide both from this paragraph alone. A stub keeps the entry
count from ever falling and keeps a triage round's rows from ever reading *resolved*, which is what
makes such a table readable at a glance. And a page that points here for an entry being removed must
be edited in the same PR to name the key instead — the pointer is the task's to fix, not the next
reader's.

**A round that triages this file in bulk builds its own checker.** `sdd-closeout` had one —
`scripts/ledger-buckets.ps1`, keying an entry to its disposition row on the entry's bold lead — and
it was deleted with that round when the doc archived, by its own instruction: a tool whose default
names a closed table checks something nobody can act on. Rebuild it against the open round's table
rather than reviving a default that resolves to the archive.

Not for: a small area-local defect (the round that finds it fixes it), a stated behavior that merely
surprises (the spec page states it), or a missing test (that is a `GAP` marker on the page).

## `INTRO` — the introspection document and identifier stability (2026-09-02)

- **`dale list` cannot render a nested block's short name.** The projection splits a block's
  identity on `.` and `+` (`Vion.Dale.Cli/Commands/ListCommand.cs:63-71`), so a nested block lists
  as `Outer+NestedBlock`. It survives because `AC-INTRO-004.1` keeps a block's identity in CLR form
  deliberately. The projection is Tier B. **CLI pass row 88:** the second half of this line — an
  endpoint whose identifier is empty being filtered out of `--output json` but not the table — is
  fixed; both modes report the same bindings, and `AC-INTRO-014.3` refuses a blank identifier at the
  source. *(INTRO pass row 88 — `CLI`.)*
- **Nothing warns when a library's `<PackageId>` and `<AssemblyName>` diverge after the identity
  change.** `AC-INTRO-001.2` makes the document's package identity the nuspec id, which is the id the
  platform registers; a project that changes only its assembly name now silently keeps its keys, and one
  that changes only its package id silently re-namespaces them. A `dale build` / `dale pack` warning is
  where an author would see it. *(INTRO pass, residue of row 2 — `CLI`.)*
  **Runtime review (2026-09-06):** the runtime is a reader of the package half only —
  `Dale/Plugin/PluginLoader.cs:50-65` groups loaded libraries by `PackageId` and refuses two
  versions of one, and `:94` keys the load table on it — so a project that changes only its assembly
  name keeps its runtime identity while its introspection keys re-namespace, and nothing on either
  side notices.
- **`dale list` runs the introspection without the development-only exclusion**, so it lists blocks the
  packed artifact omits (`Vion.Dale.Cli/Helpers/ParserRunner.cs:250`–`:258` passes only `--package-id`).
  Whether the CLI should filter them or mark them in its output is a question about what `dale list` is
  for, which is decided when the CLI is specced; the introspection page says what the listing means in
  the meantime. *(INTRO pass amendment 2, M7 — `CLI`.)*
  **Runtime review (2026-09-06):** the runtime is now the gate the listing is not —
  `Dale/Configuration/LogicSystem/LogicSystemConfigurationInitializer.cs:80-84` refuses a whole
  configuration that binds a development-only contract (VION-131) and
  `Dale/Reflection/LoadedTypeScanner.cs:87-89` never stands up a `[DevelopmentOnlyHandler]` on a
  gateway — so a block `dale list` shows and the artifact omits is one a fielded configuration is
  refused for, not one that quietly misbehaves.
- **A blank or a colliding endpoint `Identifier =` draws no compile-time diagnostic.** The bind-time
  refusal (`AC-INTRO-014.3`, `AC-INTRO-014.4`) is the only guard, so an author learns of it at
  `dotnet pack` rather than in the editor. A collision check is a whole-type analysis across two
  attribute families and two declaration levels — `DALE043`/`DALE044`-sized work in the analyzer
  registry, which failed the pass's size guard. *(INTRO pass amendment 2, rows 68 and 69's
  compile-time half — `ANLZ`.)* **ANLZ pass row 173:** measured and parked again — the check is a
  whole-type analysis over two attribute families and two declaration levels, and it would fire on
  eight deliberate `INTRO` fixtures in `Vion.Dale.Sdk.Test/TestHelpers/IntrospectionBlocks.cs` (`:82`
  and `:97` blank, six `Identifier = "Shared"` collisions), each needing a suppression. The first
  consumer declares no `Identifier =` at all.
  **Runtime review (2026-09-06):** the runtime keys on the identifier —
  `Dale/Configuration/LogicSystem/LogicSystemConfigurationInitializer.cs:437` builds each block's
  contract lookup with `.ToDictionary(m => m.ContractIdentifier, …)` — so two mappings sharing one
  identifier are a duplicate-key throw at configuration time rather than a document ambiguity.

## `CTRL` — the development host's control surface and lifecycle (2026-09-04)

- **A clock-mode switch rebuilds the next generation by writing the process environment.** The
  supervisor sets `DALE_DEVHOST_STEPPED` so the rebuilt host's `WithWebUi` reads it
  (`Vion.Dale.DevHost.Web/DevHostWebRunner.cs:195-198`,
  `DevHostBuilderExtensions.cs:43`), because `Func<string?, IDevHost>` carries no mode parameter. It is
  process-global: a second host built in the same process — every test that builds one — inherits a
  mode its caller never asked for. A runner-held static would be the same global under another name,
  and a factory that takes the mode is a surface change. *(CTRL pass row 40 — `CTRL`.)*
- **A topology's validation errors are served by splitting a joined message.**
  `TopologiesController.InvalidTopology` splits `InvalidDataException.Message` on `"; "`
  (`Vion.Dale.DevHost.Web/Api/Controllers/TopologiesController.cs:140-148`), so an error containing that
  separator is served as two fragments and the editor shows one with no subject. The fix is a
  structured exception on the topology types, which are `SCEN`'s. *(CTRL pass row 168 — `SCEN`.)*
- **A client that connects before the first generation's actors exist is never primed.** The web host
  starts before the logic system initializes (`Vion.Dale.DevHost/DevHost.cs:66-71` then `:74-96`), so a
  hub connection in that window replays to actors that do not exist and to an empty stand-in list, with
  no error either side. The SPA covers itself with its own snapshot fetch (`wwwroot/store.js:492`); a
  hand-written client relying on the replay alone sees nothing. Closing it means a readiness gate on the
  hub. *(CTRL pass row 184 — `CTRL`.)*
- **The scenario and topology routes refuse without a reason token.** Every conflict carries one and
  every refusal the control surface raises carries one, but the file-serving and file-saving answers do
  not: not-found for an unknown scenario or topology (`Vion.Dale.DevHost.Web/Api/Controllers/ScenariosController.cs:68`,
  `:81`, `:103`, `TopologiesController.cs:66`, `:126`), the missing embedded schema
  (`ScenariosController.cs:54`, `TopologiesController.cs:40`), the refused save
  (`ScenariosController.cs:191`, `TopologiesController.cs:86`) and the structurally-invalid file and
  id-mismatch answers (`ScenariosController.cs:112`, `:117`). `AC-CTRL-016.1` states the rule over the
  refusals that do carry one; extending it over these adds a token family the Explorer's client would
  key on, which is a wire decision rather than an amendment's. *(CTRL pass amendment 2, item 3 —
  `CTRL`.)*
- **Three shipped packages are outside the public-API snapshot.** `Vion.Dale.DevHost` and
  `Vion.Dale.DevHost.Web` are `IsPackable` (`Vion.Dale.DevHost.csproj:10`,
  `Vion.Dale.DevHost.Web.csproj:39` — the `:9-10` and `:36-37` this entry cited are the package-metadata
  comment above each) and absent from `docs/snapshots/publicapi-manifest.json`'s 12
  assemblies, so a member removed from `IDevHostControl` moves no snapshot and a consumer's build is the
  first thing that notices. **`Vion.Dale.DevHost.Xunit` is the third**, `IsPackable` at
  `Vion.Dale.DevHost.Xunit.csproj:11`, on the release roster (`scripts/set-version.ps1:266`) and named
  by neither this entry nor reviewer's question 2.
  **Decided** — decision 0145 puts all three inside the ratchet. What is left is the classification,
  measured off the built assemblies at `bf6c939`: `Vion.Dale.DevHost` **101** unmarked public types
  across 5 namespaces (`Control` 34, the root 25, `Scenarios` 21, `Topologies` 15, `Mocking` 6),
  `.Web` 14 across 6, `.Xunit` 3 across 1 — **118** in all, none of the three declaring a published
  namespace or referencing the analyzer. That is package-sized, not fix-sized: `T-008` did the Modbus
  half and left this one here with its number. *(CTRL pass row 201 — a change doc; decision 0145.)*

## `LIFE` — the block's life inside its actor, and the pipeline that carries it (2026-09-04)

- **Two published message types nothing in this repository sends or receives.**
  `LinkLogicBlockInterfaceActors` and `SetRemoteFunctionInterfaceInstallationTopics`
  (`Vion.Dale.Sdk/Messages/ActorMessages.cs:65-75`) have no handler and no construction site here — the
  private runtime's remote-interface proxy handler is the only reader. The page carries them and
  specifies neither, because no in-repo test can reach one. *(LIFE pass row 5 — `LIFE`.)*
  **Runtime review (2026-09-06):** both ends are live in the runtime —
  `Dale/Configuration/LogicSystem/LogicSystemConfigurationInitializer.cs:855-856` sends
  `LinkLogicBlockInterfaceActors` and `SetRemoteFunctionInterfaceInstallationTopics` to the
  remote-interface proxy handler, which handles them at
  `Dale/Mqtt/Handlers/RemoteFunctionInterfaceProxyHandler.cs:41,44` — so the page carries two types
  whose only sender and only reader are one file apart in a repository this one cannot test against.
- **A contract handler's reference is minted whether or not the actor exists.** `LookupByName` builds a
  reference from a name alone (`Vion.Dale.ProtoActor/ActorSystem.cs:373-376`,
  `PidUtils.cs:7-10`), so a block whose handler class is absent from the host binds to nothing and every
  message that contract sends becomes a dead letter with a warning and no error. The consumer is real:
  the development host spawns one stand-in per discovered handler
  (`Vion.Dale.DevHost/DevLogicSystemInitializer.cs:390`), so a contract whose handler it did not
  discover is silently dead. A registry lookup at link time is a spawn-ordering contract shared with the
  private runtime, which is past this pass. *(LIFE pass row 26 — `LIFE`.)*
  **Runtime review (2026-09-06):** the runtime makes the silence worse, not better —
  `Dale/Configuration/LogicSystem/LogicSystemConfigurationInitializer.cs:641-645` looks each handler
  up by `actorType.Name` over a deliberately degrading assembly scan, so a shared assembly that
  fails to enumerate drops a handler from the scan and every contract mapped to it binds to a
  live-looking reference that reaches no one.
- **A `[Persistent]` property more than one level inside a block is silently not persisted.** Discovery
  walks a class-typed property's own properties and no further
  (`Vion.Dale.Sdk/Persistence/PersistentData.cs:264-295`), with no diagnostic at any door. Recursing
  needs a cycle guard, a key grammar for arbitrary depth and a decision about collections — a change doc
  of its own. *(LIFE pass row 89 — `LIFE`.)*
- **A block's actor name is ambiguous when its name or identifier contains the separator.** The name is
  a concatenation (`Vion.Dale.Sdk/Utils/LogicBlockUtils.cs:12`), so `logicblock_A_B_c_d` reads two ways.
  Every reader in the repository matches the prefix rather than splitting, so there is no harm to name
  today; the entry exists against the day one splits. *(LIFE pass row 137 — `LIFE`.)*
  **Runtime review (2026-09-06):** the runtime is one more prefix-matcher rather than a splitter —
  `Dale/Configuration/LogicSystem/LogicSystemConfigurationInitializer.cs:362` matches
  `^(logicblock_)` by regex — so the "no harm today" holds across the fielded host too.
- **The two dependency-injection registrations are independent, and one in-repo host uses only one.**
  `AddDaleSdk` and `AddProtoActorSystem` can each be called without the other
  (`Vion.Dale.Sdk/ServiceCollectionExtensions.cs:12`,
  `Vion.Dale.ProtoActor/Extensions/ServiceCollectionExtensions.cs:9`); the introspecting parser calls
  only the first (`Vion.Dale.LogicBlockParser/Program.cs:117`) and never spawns an actor. Whether *an
  actor system without the SDK's registrations* is a supported composition is a decision rather than a
  defect — the pipeline documents what it takes from the container instead. *(LIFE pass row 216 —
  the operator.)*
  **Runtime review (2026-09-06):** the fielded composition calls both, in that order —
  `Dale/Program.cs:271` `AddProtoActorSystem()` then `:293` `AddDaleSdk()` — so "an actor system
  without the SDK's registrations" is the parser's composition alone.
- **A bound service the configuration gives no identifier is dropped at six sites for the instance's
  life.** The announcement omits it (`Vion.Dale.Sdk/Core/LogicBlockBase.cs:1180-1184`) and every value
  change, clear, flush and drain drops it in turn, each with its own warning, while the block reports
  itself healthy — `AC-LIFE-003.4` states it. Failing the configuration instead is the right shape, but
  its reader is the cloud's service allocation: the runtime builds the lookup from the configuration
  payload's service list, and whether a fielded configuration may lawfully omit a service the block
  binds is a contracts question no read in this repository answers. *(LIFE pass row 30 — `LIFE`.)*
  **Runtime review (2026-09-06):** the reader this line defers to is one file —
  `Dale/Configuration/LogicSystem/LogicSystemConfigurationInitializer.cs:434` builds
  `serviceIdLookup` straight from the configuration payload's service list — so "may a fielded
  configuration lawfully omit a service the block binds" is a question the cloud's allocation
  answers and the runtime merely forwards.
- **A block whose configuration failed still starts, publishes and acknowledges.** The start arm has no
  configuration check (`Vion.Dale.Sdk/Core/LogicBlockBase.cs:263-277`), so such a block runs its start
  hook, publishes over whatever bindings the failed configuration registered, arms its periodic save and
  acknowledges — and the host reports itself started with the block's members reading their defaults.
  Refusing has three shapes and each has a reader: throwing makes the runtime's start time out and the
  development host's boot fail, undoing the design that keeps the host up with the failure on its health
  surface (`AC-CTRL-003.*`); acknowledging without starting mints an "acknowledged but inert" state the
  page would have to state; and a failure acknowledgement is a wire change on `StartLogicBlockResponse`.
  That is a decision, not a pass's. *(LIFE pass row 47 — `LIFE` + `CTRL`.)*
  **Runtime review (2026-09-06):** the runtime is the reader the "throwing" shape would break —
  `Dale/Configuration/LogicSystem/LogicSystemConfigurationInitializer.cs:256` awaits
  `StartLogicBlockResponse` on a timeout now bound to the configuration (VION-134) — so a block that
  refused to start would stall the gateway's whole boot rather than surface one failed block.
- **The development host restores nothing, so the start hook's persisted-value promise is one it cannot
  keep.** `AC-LIFE-012.2` says a member read in the start hook holds its restored value *on a host that
  restores*; the development host's start sequence sends no restore
  (`Vion.Dale.DevHost/DevLogicSystemInitializer.cs:175-205`), so a block author developing there reads a
  default and gets the operator's value in the field. Whether the host should send an empty restore for
  sequence parity — the way it sends the snapshot request it discards — is the host's decision.
  *(LIFE pass row 124 — `CTRL`.)*
  **Runtime review (2026-09-06):** the asymmetry is real and one-sided — the runtime does restore:
  `Dale/Configuration/LogicSystem/LogicSystemConfigurationInitializer.cs:135` calls
  `RestorePersistentDataAsync`, which sends one `RestorePersistentDataRequest` per block at
  `:920-934` — so the development host is the only host on which `AC-LIFE-012.2`'s promise is empty.
- **`Vion.Dale.ProtoActor`, and three namespaces of `Vion.Dale.Sdk`, are outside the public-API
  snapshot.** The manifest covers 12 assemblies and `Vion.Dale.ProtoActor` is not among them although it
  is a shipped package a consumer's block depends on; `PublicApiConfig.cs` declares only `Core`,
  `Emission` and `Utils` as public namespaces, so the 28 message types, the 7 diagnostics types and the
  8 actor abstractions this page specifies move no snapshot when they change. The same shape
  as the development-host packages above, and `Vion.Dale.Plugin`'s 2 public types are in it too.
  **Decided** — decision 0145 puts them inside the ratchet. Measured at `bf6c939`: `Vion.Dale.ProtoActor`
  10 unmarked across 2 namespaces, `Vion.Dale.Plugin` 2 across 1. The three SDK namespaces hold **47**
  unmarked types of the 217 the `BIND` entry below counts — `Messages` 28, `Abstractions` 12,
  `Diagnostics` 7. The 8 above is what *this page* specifies, not what `Abstractions` holds; the other
  four are public types the page does not reach, and the ratchet asks about all twelve. The three would
  be declared one namespace at a time: declaring the SDK **root** instead arms all twenty at once,
  because `DALE014` matches a declaration as a prefix.
  *(LIFE pass row 217 — a change doc; decision 0145.)*

## `BIND` — contracts, endpoints and the provider face (2026-09-04)

- **Two published code-generation attributes have no reader in any repository.**
  `Vion.Dale.Sdk/CodeGeneration/LogicFunctionImplementationAttribute.cs` and
  `LogicFunctionMatchingInterfaceAttribute.cs` are read by nothing — the second's only reader was a
  private binder method this pass deleted, and the first was applied nowhere even before that. The
  second is applied on **14** declarations in the SDK's own `Examples/FunctionInterfaces/V1/*`, the
  pre-generator design. The reader count is verified zero across dale-sdk, the private runtime, both
  HALs and the first consumer. Removing them is a public-surface removal that also rewrites shipped
  example files, and it is entangled with the `Examples/` packaging question below.
  *(BIND pass rows 33, 34 — the retro.)*
- **The two discoveries of this area do not share a rule, and the contract side is the one out of
  step.** The interface factory scans every loaded assembly and takes the types that loaded from one it
  could not enumerate (`Vion.Dale.Sdk/Configuration/Interfaces/InterfaceFactory.cs:82-99`); the
  contract factory considers only the assembly that declares the type it is looking for and the
  assemblies referencing that one — for a consumer-declared contract, the consumer's package rather
  than the SDK's — and refuses the whole configuration when one of them cannot be enumerated
  (`Vion.Dale.Sdk/Reflection/AssemblyExtensions.cs:47`, `:56-59`, `:100-103`). The private runtime
  rejects the all-or-nothing helper twice in its own comments and degrades instead
  (`Dale/Program.cs:115-118`, `LogicSystemConfigurationInitializer.cs:635-640`), so the shape to
  converge on is the runtime's degrading scan rather than the SDK's helper.
  *(BIND pass row 36 — `PLUG`, promoted by the operator.)*
- **The contract-message envelope takes any payload while the inter-block one takes a struct.**
  `Vion.Dale.Sdk/Messages/ActorMessages.cs:188` carries no struct constraint, unlike `:167-168`. The
  laxity is unreachable in practice — every sender constrains at its own entry, the private runtime has
  no contract dispatch at all, and the development host's codec builds the message from a declared wire
  struct — so there is no wrong outcome behind it, only a published shape that says less than it
  means. *(BIND pass row 62 — `BIND`.)*
  **Runtime review (2026-09-06):** the one construction site outside this repository confirms the
  laxity is unreachable — the first consumer's
  `Ecocoach.Dale.Contracts.Ppc.Simulation/PpcProviderBase.cs:64` builds
  `ContractMessage<TToConsumer>` from a type parameter its own base already constrains.
- **A contract mapped to a handler class the host never spawned sends into nothing.** The handler
  reference is minted from a name whether or not an actor of that name exists (`AC-LIFE-017.1`), so a
  contract binds, maps and sends, and every message reaches no one. This is the contract-side half of
  the `LIFE` finding above; the fix is a registry lookup at link time, which is the runtime's
  spawn-ordering contract. *(BIND pass row 83 — `LIFE`, beside its row 26.)*
- **A second registration request registers again, while the client aborts the duplicate.**
  `Vion.Dale.Sdk/Abstractions/ServiceProviderHandlerBase.cs`'s registration arm has no guard and the
  answer is unconditional, while the runtime's client aborts a duplicate registration
  (`Dale/Mqtt/MqttClient.cs:248-253`). The divergence is unreachable — the runtime sends the request
  exactly once, at boot — so it is a shape that disagrees rather than a defect that fires.
  *(BIND pass row 106 — `BIND`.)*
- **`RegisterServiceProvider` is a member-less published record with no reader anywhere.**
  `Vion.Dale.Sdk/Mqtt/ActorMessages.cs:150` is undocumented, constructed nowhere and handled nowhere —
  zero occurrences in dale-sdk, the private runtime, both HALs and the first consumer. Deleting it is a
  public-surface removal of the same class as the two code-generation attributes above.
  *(BIND pass row 140 — the retro.)*
- **A cast integer reaching the multiplicity token conversion fails a whole pack run.**
  `Vion.Dale.Sdk/Core/LinkMultiplicityWire.cs:24` throws `ArgumentOutOfRangeException` for a value
  outside the four, which is the right answer to a cast typo — but it is unhandled inside the
  introspection walk, so one such declaration costs the artifact rather than the member.
  *(BIND pass row 168 — the retro.)*
- **A contract-type token has no uniqueness guard.** `DALE048` now refuses an empty or whitespace
  token (`AC-ANLZ-005.4`), but two interfaces in one compilation may still declare the same one, and it
  reaches the document as two contract types with one name — the token being a cloud-facing stable
  identifier (`identifier-stability.md`). Uniqueness is a whole-compilation analysis of `DALE043`'s
  size, and it owes two things this ledger line has to carry: `WellKnownDiagnosticTags.CompilationEnd`
  (`AC-ANLZ-001.4`), without which the IDE drops it, and a decision about
  `ServiceRelationAnalyzer.cs:50-52`'s current-assembly boundary, which makes a duplicate against a
  *referenced* library invisible either way. *(BIND pass row 173, ANLZ pass row 175 — `ANLZ`.)*
  **Runtime review (2026-09-06):** a duplicate does not stay soft in the field —
  `Dale/Configuration/LogicSystem/LogicSystemConfigurationInitializer.cs:437` and `:632` both build
  `ToDictionary` maps keyed on the contract identity, so the second occurrence is a duplicate-key
  throw at configuration time rather than a second contract type wearing one name.
- **A block's interface endpoints come from public properties only, and the walk cannot be widened
  without moving every consumer's artifact.** `DeclarativeInterfaceBinder.cs:86` walks public instance
  properties while the contract binder walks non-public ones too. This pass refuses the *declaration*
  that lands outside the walk (`AC-BIND-001.3`) rather than widening it, because widening mints
  endpoints into the introspection document every consumer uploads, whose reader is the cloud.
  *(BIND pass row 21 — `INTRO`.)*
- **The TestKit maps a contract an inclusion gate would have excluded.**
  `Vion.Dale.Sdk.TestKit/LogicBlockTestContextBuilder.cs:352-369` discovers contract identifiers from
  marked, writable properties and reads no `[IncludedWhen]`, which is two of the binder's three
  conditions — so a gated-out contract is mapped in a test and absent in a host.
  *(BIND pass row 191 — `TKIT`.)* **TKIT pass row 21:** parked again, and the page now states the
  discovery rule it follows from (`AC-TKIT-002.2`). Reading a gate means running the gate evaluator
  the binder owns, which is `BIND`'s surface and not a line in a kit; decision `0081` names
  "runtime/DevHost/TestKit" as one rule, so this is that rule's TestKit half and moves with it.
  **Runtime review (2026-09-06):** decision `0081`'s "runtime/DevHost/TestKit" rule is honoured on
  two of the three — `Vion.Dale.Sdk/Configuration/Contract/DeclarativeContractBinder.cs:51` runs
  `InclusionGate.IsIncluded` before binding, and the runtime names that as the reason it forwards
  `[InstantiationParameter]` values untouched
  (`Dale/Configuration/LogicSystem/LogicSystemConfigurationInitializer.cs:443`) — and the divergence
  is live for the first consumer, which gates fielded blocks and proves the gating with this kit in
  the room
  (`Ecocoach.EnergyManagement.Test/LogicBlocks/ChargingStationEvtec/ChargingStationEvtecGatingShould.cs`).
- **Seventeen namespaces of `Vion.Dale.Sdk` are outside the public-API ratchet, not four.**
  `Vion.Dale.Sdk/PublicApiConfig.cs:6-8` declares only `Core`, `Emission` and `Utils` of the assembly's
  twenty, so `DALE014` never asks for a mark in the other seventeen — `Configuration.Contract`,
  `Configuration.Interfaces`, `Configuration.Services`, `Configuration.Timers`, `CodeGeneration`,
  `Abstractions`, `Mqtt`, `Messages`, `Diagnostics`, `Reflection`, `Introspection`, `Persistence`,
  `Configuration`, the root namespace and the three under `Examples`. The sharpest case is
  `ServiceProviderContractTypeAttribute`, an attribute every consumer authors that is on no manifest
  while its sibling `LogicBlockContractBase` in the same namespace is. **Decided** — decision 0145 puts
  them inside the ratchet, and **217** unmarked author-declared public types measured at `bf6c939` is
  why that is a change doc rather than a fix: `Messages` 28, `Mqtt` 23, `Configuration.Interfaces` 15,
  `Abstractions` 12, `Configuration.Services` 12. Eight further exported types are **not classifiable
  at all**: the four C# 14 `extension` blocks under `Mqtt/` and `Reflection/` each emit a `<G>$` grouping
  type and a `<M>$` beneath it, and no author can write an attribute on either. Whether `DALE014` asks
  for a mark on them is unknown — no declared namespace holds an extension block today — and it is the
  same question the delegate above turned out to be, waiting on the next package. Two shapes govern the sequencing. Declaring the **root** namespace arms
  all twenty at once, because `DALE014` matches a declaration as a prefix — so the `IO` row below is
  not separable from this one. And the 97 types under `Examples.*` are **out of scope by that
  decision**: they are the packaging question the next row raises, not the ratchet's.
  *(BIND pass row 195 — a change doc; decision 0145.)*
- **Eight example contract files and one example logic block ship inside the SDK assembly.**
  `Vion.Dale.Sdk/Examples/FunctionInterfaces/` holds four plus four under `V1/`, and
  `Examples/LogicBlocks/ChargingStationMultiPointSimulation.cs` one block, all outside every
  `[PublicApiNamespace]` and every `[PublicApi]` mark. The four under `V1/` are the pre-generator
  design nothing reads. Whether any of them belongs in a shipped package is a packaging decision.
  *(BIND pass row 196 — the retro.)*
- **`RegistrationSecret` belongs to no roster area.** `Vion.Dale.Sdk/Mqtt/RegistrationSecret.cs:11-50`
  is `[PublicApi]`, has no caller in this repository, and its one verified reader is the gateway's
  registration handshake (`Dale/Mqtt/Handlers/RegistrationHandler.cs:27`). It lives under `Mqtt/`
  because that is where the handshake's topics are, not because it is part of any contract this page
  specifies. *(BIND pass row 197 — the retro.)*
- **`Vion.Dale.Sdk.Reflection.AssemblyExtensions.GetConcreteType` now has no caller.** The singular
  entry was the contract factory's only use, and this pass moved that to the plural one so the factory
  can name every candidate. It stays because deleting a public method of a namespace outside the
  ratchet is a surface removal this pass was not asked to make. *(BIND pass, `DC-13` — the retro.)*

## `ANLZ` — the DALE diagnostic registry and the analyzers that report it (2026-09-04)

- **A preset attribute is judged by every rule but `DALE019`.** `AnalyzerHelper.cs:58`/`:66` compare
  an attribute class's own full name for equality, so a class deriving from `ServicePropertyAttribute`
  — the documented way to carry a unit, `[Kilowatts]` — is invisible to the other 43 rules while the
  runtime honours it (`Vion.Dale.Sdk.Test/Core/AttributeInheritanceShould.cs`). `AC-ANLZ-002.2` states
  the limitation and `SharedWalkTests` pins it. Widening the match to the base chain re-aims all 36
  analyzers at once, and the readers are every consumer with preset attributes: the SDK's own
  `examples/Vion.Examples.Presentation/Conventions/` ships sixteen, and the first consumer none.
  *(ANLZ pass row 17 — `ANLZ`, the operator promotes.)*
- **A relation-bearing component declared on a base block in a referenced assembly draws no warning.**
  `ServiceRelationAnalyzer.cs:50-52` filters to the current assembly and `:192` reads the type's own
  members, so a library that ships a base logic block gets `AC-ANLZ-021.4` in the base's build and a
  consumer inheriting from it gets nothing. Widening means judging a referenced assembly's
  declarations, which is `AC-ANLZ-002.3`'s stated boundary rather than a defect in this rule.
  *(ANLZ pass row 66 — `ANLZ`.)*
- **`[StructField]` on a parameter other than a wire struct's constructor parameter is judged by
  nothing.** The `INTRO` pass narrowed `AttributeUsage` to `AttributeTargets.Parameter`
  (`Vion.Dale.Sdk/Core/StructFieldAttribute.cs:20`), which closed the property half of this line;
  any method parameter and any non-wire struct's constructor parameter still take the attribute
  while the one walk that reads it (`TypeRefBuilder.BuildStructFieldAnnotations`, and
  `StructFieldPresentationBuilder.Build` over the same constructor) sees neither, so a misplaced
  declaration compiles and emits nothing. The two rules that judge the knob are scoped to that
  reader's parameters (`AnalyzerHelper.IsStructFieldParameter`), so they do not report the
  misplacement either. Narrowing further is a source-breaking change to a published attribute — the
  same shape as the message-struct entry `DALE047` just closed, and the same reason it became a
  diagnostic rather than a narrowing. *(ANLZ pass row 111 — `ANLZ`.)*
- **A `MinInterval` at the tick-representation boundary configures a negative interval, unreported.**
  `EmissionAttributeHelper.cs:267` and `Vion.Dale.Sdk/Emission/DurationParser.cs:120-121` carry the
  identical `> long.MaxValue` comparison against a `double` that *equals* `long.MaxValue`, so the cast
  wraps. `MinInterval = "922337203685477.6"` draws no diagnostic and makes the gate's elapsed test
  unconditionally true. The analyzer mirrors the runtime exactly, so a one-sided fix would reject a
  token the runtime accepts; both halves move together and the runtime's is `emission.md`'s.
  *(ANLZ pass row 118 — `EMIT` + `ANLZ`.)*
- **`DALE046` judges a struct type only on its first occurrence in a wire graph.**
  `ScenarioWireTypeAnalyzer.cs:104-107` never releases its `visited` set, so a struct reached down two
  branches is skipped the second time. No shape I could construct makes the outcome differ — a type
  already judged representable is representable, and one already judged otherwise returned — so this
  is recorded rather than fixed. *(ANLZ pass row 150 — `ANLZ`.)*
- **A `PackagePath` ending in a separator packs two different artifacts by runner.**
  `Vion.Dale.Sdk.csproj:92` (`analyzers\dotnet\cs\`) and `:87` (`tools\net10.0\`) end in a
  separator, which `dotnet pack` doubles on Linux and not on Windows: the CI artifact carries
  `analyzers/dotnet/cs//Vion.Dale.Sdk.Generators.dll` and 43 `tools/net10.0//` entries, a locally
  packed one carries a single slash. NuGet resolves both, so nothing is broken — but the two
  artifacts are not byte comparable, and any tool reading entry names exactly has to know
  (`scripts/verify-packed-assembly-versions.ps1` collapses repeated separators for that reason).
  Dropping the trailing separator changes a released package's layout, which is
  [`../releasing.md`](../releasing.md)'s. *(Found by `T-007`'s artifact gate on its first real run —
  the release process.)*
- **The generator's `Contract`-substring predicate runs on every class in every compilation.**
  `Vion.Dale.Sdk.Generators/LogicClassGenerator.cs:36-40` matches any class carrying an attribute whose
  name *contains* `Contract`, and the semantic pass afterwards makes the output correct — so there is
  no functional observable, only the incremental generator's cache key being wider than it needs.
  Measuring the cost needs a build-time benchmark. *(BIND pass row 186, ANLZ pass row 176 — `ANLZ`.)*
- **`AnalyzerReleases.Shipped.md` / `Unshipped.md` do not exist and `RS2008` is suppressed.**
  `Vion.Dale.Sdk.Generators.csproj:19`. The rules ship to every consumer through
  `Vion.Dale.Sdk.csproj:92`, so the suppression's comment was corrected to say that adopting release
  tracking is an open decision rather than a settled "this is internal".
  [`../sdk-surface-conventions.md`](../sdk-surface-conventions.md) § 4 covers next-free-id,
  never-reuse, the two-severity precedent, the `CompilationEnd` tag and the supported-type gate, and
  says nothing about release tracking. *(ANLZ pass, reviewer's question 6 — the retro.)*
- **`LogicClassGeneratorERR` has no registry id.** The generator reports an `Error` a consumer cannot
  configure or suppress through any `DALE` prefix (`DiagnosticsExtensions.cs`, `AC-ANLZ-019.1`). Its
  four call sites all fire on a missing or broken embedded template — a build of the SDK itself going
  wrong, not an authoring mistake — which is why this is a question about the surface rather than a
  defect. *(ANLZ pass, reviewer's question 3 — the retro.)*
- **`DALE013` fires on a documented `[PublicApi]` in a project that generates no documentation file.**
  The rule reads `GetDocumentationCommentXml`, which is empty for every type in a project without
  `<GenerateDocumentationFile>` — so the diagnostic asks for a summary that is already there and the
  only answer is a suppression. Measured on `Vion.Dale.Sdk.Test`, the first analyzer-armed project to
  declare a `[PublicApi]`: 1 occurrence, 0 under `-p:GenerateDocumentationFile=true`. Thirteen of the eighteen roster packages set `<GenerateDocumentationFile>`, and they are the twelve inside the ratchet plus `Vion.Dale.DevHost.Xunit`. So the five that do **not** — `Vion.Dale.DevHost`, `.Web`, `Vion.Dale.Plugin`, `Vion.Dale.ProtoActor` and `Vion.Dale.Cli` — are five of the **six** packages outside the ratchet, `DevHost.Xunit` being the sixth and the exception. Four of the five are packages a later pass will arm, and each meets this on its first mark; `Vion.Dale.Cli` is out of the ratchet's scope by decision 0145 and meets it never.
  Setting the property on a project changes what that whole project warns about (CS1591 on every
  undocumented public member), so it belongs to the pass that arms the package, not to a one-liner here.
  Third unsatisfiable-diagnostic shape in this family, after the delegate below.
  *(Found by `T-008`'s review round, after the operator's ruling — `ANLZ`.)*
- **The manifest generator's type scan does not see a `delegate`.**
  `scripts/generate-api-reference.cjs:122` matches `class|interface|enum|struct|record …|extension`,
  so a `[PublicApi]` delegate carries a mark that reaches neither the manifest nor the generated
  reference — silence of the kind `sdk-surface-conventions.md` § 7 names. It costs nothing today:
  the repository declares two public delegates (`ModbusServerBufferAccessor`,
  `DevTopologyLoader.TopologyHostCheck`) and neither is published. Not fixed with the marks that
  found it because a delegate's declaration puts its return type where the pattern reads the name,
  and this script has no test harness at all — it would be the first. *(Found by `T-008` while
  arming `DALE014` over `Vion.Dale.Sdk.Modbus.Core`, after the operator's ruling — `ANLZ`.)*
- **An analyzer `ProjectReference` flipped to `ReferenceOutputAssembly="true"` is caught by nothing.**
  Twelve shipped packages now carry the analyzer that way — the seven SDK/IO/protocol ones and the five
  kits — and **nothing guards the flag at all**. The nearest thing,
  `AnalyzerWiringShould.LeaveBuildOutputsOfDependencyGraphUntouched`, fingerprints `bin`/`obj` across
  `ProbeBuildGraph` (`AnalyzerWiringShould.cs:66-72`) before and after the probe builds, so a flipped
  flag leaves both fingerprints carrying the same wrong content and the test green. Nothing inspects the
  packed artifacts for a `Vion.Dale.Sdk.Generators.dll` that should not be in `lib/`, and
  `verify-packed-assembly-versions.ps1` *lists* a foreign assembly as unchecked rather than failing on
  it. That is the 0.11.1 shape one attribute away, on twelve packages. Pre-existing — the HTTP pass and
  the `TKIT` pass added the same reference without extending the graph either — and the honest fix is a
  required-absent rule in the packed-artifact gate, not a longer probe graph.
  *(Found by `T-008`'s review round, after the operator's ruling — `ANLZ` with `TKIT`.)*

## `MODB` — the Modbus protocol bindings (2026-09-05)

- **The default outcome for an unrecognised exception is `TransportError`.**
  `ModbusOutcomeClassifier.cs:43` classifies anything it does not name as a wire fault, so a local
  failure — an `ObjectDisposedException` from a proxy disposed under a request, say — moves
  `Link.State` to `Faulted` and closes the socket though nothing touched the wire. The sketch's own
  case (a negative `DefaultOperationTimeout` reaching the cancellation source) is unreachable after
  this pass's `AC-MODB-003.4`. Narrowing the default is not area-local: the first consumer routes
  eleven error callbacks through its own `ReachedTheWire()` partition
  (`Ecocoach.EnergyManagement/LogicBlocks/Shared/ModbusOutcomes.cs:57-59`) and publishes `Link` at 25
  sites, so a reclassification flips every fielded block from its wire arm to its quiet one. The
  narrower question worth answering first is whether `ObjectDisposedException` alone should be
  `Cancelled`. *(MODB pass row 44 — `MODB`.)*
- **The proxy seam takes two types for one protocol field.** `IModbusTcpClientProxy`'s four bit
  operations take an `int` unit identifier and its four register operations a `byte`
  (`ModbusTcpClientProxy.cs:69`, `:87`, `:105`, `:122` against `:141`, `:159`, `:177`, `:193`), and
  the wrapper casts at every register call site. The `(byte)` truncation is unreachable because
  `ValidateUnitIdentifier` runs first, so this is a shape defect rather than a live one — but it is
  four signatures on a published interface plus the TestKit's `FakeModbusTcpClientProxy`, which
  reimplements the same split. Belongs with the surface review below. *(MODB pass row 48 — the
  retro's surface review.)*
- **A value width below two bytes divides by zero.** `ModbusDataConverter.cs:38`
  (`ushort.MaxValue / registersPerValue`, zero when `bytesPerCount < 2`) and `ModbusValidator.cs:22`
  (`byteCount % bytesPerValue`) are unreachable from any SDK call site — the only arguments are the
  constants 2, 4 and 8 — but both types are public and the first consumer injects
  `IModbusDataConverter` in production (`PyranometerHuaweiSmartLogger.cs:46`, `:375`). Hardening them
  is a decision about a published surface, not an area-local guard. *(MODB pass row 85 — the retro's
  surface review.)*
- **One surface, two instant types.** `ILogicBlockModbusTcpServer.LastClientWriteAt` is a
  `DateTimeOffset?` while every client-side diagnostic instant is a `DateTime?` in UTC
  (`ModbusLinkSummary.cs:59`, `ModbusTcpConnectionSummary.cs:48`), and a block publishes them side by
  side. The development host is **not** a differing reader — both serialise as `date-time` and render
  through one path — so the cost is the consumer's alone: it re-declares the type verbatim
  (`SimulatorDeviceHost.cs:36-38`) and reads it at four block sites. Changing either is
  source-breaking on a published property type. *(MODB pass row 116 — `MODB`.)*
- **`Vion.Dale.Sdk.Modbus.Rtu` ships no `AddDaleModbusRtuSdk` extension, so a development host
  hand-constructs its `IConfigureServices`.** `.Tcp` has `AddDaleModbusTcpSdk`
  (`ServiceCollectionExtensions.cs:25`) and `.Core` has `AddDaleModbusCoreSdk`; RTU has only
  `DependencyInjection : IConfigureServices`, which the runtime discovers by reflection and a DevHost
  cannot. So the SDK's own example writes
  `new Dale.Sdk.Modbus.Rtu.DependencyInjection().ConfigureServices(services)`
  (`examples/Vion.Examples.ModbusRtu/Vion.Examples.ModbusRtu.DevHost/Program.cs:27`) where the TCP
  example writes one call. `T-008` published the type because that hand-call is the only way in, which
  makes RTU the one package whose `IConfigureServices` is surface where `.DigitalIo`'s and `.AnalogIo`'s
  identical class is `[InternalApi]`. Adding the extension would make all three agree and let the type
  go back to plumbing — a new published member, so a ratchet move rather than a repair.
  *(Found by `T-008`'s review round, after the operator's ruling — `MODB`.)*
- **Two consumer-facing exceptions live in an implementation namespace.** `IpAddressNotSetException`
  (`ModbusTcpClientWrapper.cs:1216`) and `ConnectionTimeoutException` (`ModbusTcpClientProxy.cs:265`)
  are public, unmarked, and declared inside files named for internal classes in
  `…Client.Implementation`. A block's error callback receives both. Nothing in either consumer
  repository or in the examples catches them today, so the move is safe here — but a namespace change
  on a public type is source-breaking for a consumer outside them, and neither type has a manifest row
  that would flag it. *(MODB pass row 157 — `MODB`.)*
- **A factory-created Modbus client or server is never reclaimed.** Both factories are singletons
  holding the root provider (`Vion.Dale.Sdk.Modbus.Tcp/ServiceCollectionExtensions.cs:28`, `:34`), so
  an instance they create rides the root container rather than the block's scope and is disposed at
  process exit. The readers are not symmetric: the client factory has **zero** call sites in the first
  consumer, but the SDK's own example creates two clients through it
  (`ModbusTcpDebugClient.cs:386`, `:390-391`); the server factory has **twelve** `Create()` sites
  across 21 consumer files, one of them in production (`TradingSourceVgt.cs:374`), and every creator
  already disposes its own wrapper from `Stopping()` — a fielded dependency on today's lifetime.
  Resolving from the ambient block scope is a DI-lifetime change on a published registration.
  `AC-MODB-018.3` states the lifetime as it is. *(MODB pass rows 150 and 151 — the SDK's DI owner.)*
- **The reuse-address knob has no same-version-redeploy repro.** The server binds with
  `ExclusiveAddressUse = false`, which is the conventional .NET spelling for rebinding over a
  lingering socket, but which of that and raw `SO_REUSEADDR` the observed `EADDRINUSE` actually needed
  was never confirmed against a real redeploy, and the rebind-before-release regression the design
  asked for was never written — the suite has a provider test that binds and accepts, not one that
  rebinds. Both are OS- and timing-dependent, which is why neither is a portable unit test.
  *(MODB pass, absorbed from the deleted RFC 0018 — the absorption is recorded under Reviewer's
  question 3 of [`../changes/archive/2026-09-04-modb-pass.md`](../changes/archive/2026-09-04-modb-pass.md)
  — `MODB`.)*
- **Whether a newer FluentModbus makes the reuse-address provider unnecessary is unasked.** The
  provider exists because the pinned version's built-in listener sets no socket options. A version
  that exposed `ExclusiveAddressUse` directly would retire it. *(MODB pass, absorbed from the deleted
  RFC 0018 — the absorption is recorded under Reviewer's question 3 of
  [`../changes/archive/2026-09-04-modb-pass.md`](../changes/archive/2026-09-04-modb-pass.md) — the
  retro.)*
- **Three server features were deferred at design time and no consumer has asked since.** Array
  overloads mirroring the client's `count` signatures, multi-unit register maps, and a consumer-facing
  request-validator hook beyond the extent-derived one. Each layers onto today's surface without
  breaking it. *(MODB pass, absorbed from the deleted RFC 0007 — the absorption is recorded under
  Reviewer's question 3 of
  [`../changes/archive/2026-09-04-modb-pass.md`](../changes/archive/2026-09-04-modb-pass.md) —
  `MODB`.)*
## `CLI` — the `dale` command-line tool (2026-09-05)

- **One thirty-second ceiling covers every cloud request, the package upload included.**
  `DaleHttpClient` sets `Timeout = 30 s` on the shared client (`:129`) and `dale upload` posts the
  whole `.nupkg` as a multipart body read into memory (`UploadCommand.cs:413-414`). None of the four
  workflow uploads has reported a timeout, so nothing is broken today — but it is a size-and-link
  limit nobody chose for that one request, and the failure it produces reads like a server fault
  (`Request timed out: POST …`). Giving the upload its own bound is a small change with no observable
  a test can stand on (`AC-CLI-017.6` is `GAP` for the same reason), which is why it is here rather
  than in the pass. *(CLI pass row 189 — `CLI`.)*
- **`dale dev` announces an address it never checked.** It prints `http://localhost:5000`
  unconditionally (`DevCommand.DescribeStartup:153`, printed at `:109`); nothing in the tool reads a port, checks one, or sets
  `ASPNETCORE_URLS`, and `scenario run`, `validate`, `schema` and `open` all default to the same
  number. With 5000 already taken the developer is sent to someone else's server. Answering it needs
  the host's readiness handshake — which is `CTRL`'s, not this tool's — and changing the default is a
  surface change across five commands. The page states the printed address as the configured default
  rather than a bound one. *(CLI pass row 106 — `CLI` with `CTRL`.)*
- **The bundled template has no gate that runs `dale new`.** Its only proof is
  `VionIotLibraryTemplate.Test/ThermostatShould.cs`, built by the main solution against the *source*
  template, so the pack-time reference rewrite and the `dotnet new install` path are unproven end to
  end. The publish workflow already installs the packed tool for the help snapshot
  (`publish.yml:148-155`), so the step exists to hang a smoke on — but the fixture, the cleanup and
  the failure modes are a change doc's worth of work. *(CLI pass row 212 — the release process.)*

## `TKIT` — the five test kits (2026-09-06)

- **A persistent value declared for a property the block does not persist is accepted silently.**
  `Vion.Dale.Sdk.TestKit/LogicBlockTestContextBuilder.cs:444-464` resolves the storage key from the
  block's own service-property bindings and falls back to `_direct.{PropertyName}` when the search
  misses, so a typo or a property that is not persistent restores under a key the block never reads
  and the test asserts against a default it believes it set. Catching it needs the binder's own view
  of which properties persist, which is `BIND`'s surface rather than a line here — the same reason
  the inclusion-gate entry above is parked. *(TKIT pass row 20 — `BIND`.)*
- **Two downstream test projects cannot be proven against a same-PR kit change.**
  `templates/vion-iot-library/VionIotLibraryTemplate.Test` and
  `libraries/Vion.Diagnostics/Vion.Diagnostics.Test` carry no `DaleLocalSource` switch, where 9 test
  projects across 7 example families do, so a kit signature change can only be proven against them
  after publishing. Adding the switch to the template reaches its shipped content — a `dale new`
  output would carry it — and to the first-party library lane, which is
  [`../releasing.md`](../releasing.md)'s to decide. *(TKIT pass row 177 — the release lane.)*
- **The five kit test projects do not agree on their test-platform reference.**
  `Vion.Dale.Sdk.TestKit.Test` and `Vion.Dale.Sdk.Modbus.Tcp.TestKit.Test` reference
  `Microsoft.NET.Test.Sdk` 18.0.1; the digital, analog and RTU ones reference the MSTest package
  alone. All five run green and all five carry `[DoNotParallelize]` (MSTest's props set
  `IsTestProject`, which is what `Directory.Build.props:41-43` keys on — the pass's brief had this
  the other way round and the refutation is in the archived change doc). So this is a consistency
  question about the test-platform migration rather than a defect, and it is stated for the operator
  rather than decided by a pass. *(TKIT pass row 185 — `TKIT`, for the operator.)*
- **The SDK ships no test context for a service-provider handler.** Absorbed from the deleted RFC
  0002, whose four gaps are all still live; the absorption is recorded under Reviewer's question 7 of
  [`../changes/archive/2026-09-05-tkit-pass.md`](../changes/archive/2026-09-05-tkit-pass.md).
  `LogicBlockTestContext<TLogicBlock>` constrains its type parameter to `LogicBlockBase`, so a
  handler cannot be hosted by it, and three suites inside the SDK hand-roll a recording actor context
  in three different shapes — `Vion.Dale.Sdk.DigitalIo.Test/TestHelpers/HandlerHarness.cs`,
  `ContractHarness.cs` and `Vion.Dale.Sdk.Test/TestHelpers/LifecycleHarness.cs`'s
  `RecordingActorContext`. A minimal shipped context is 350-450 lines of new published surface across
  three or four types, because `ServiceProviderMqttMessage`'s internal constructor takes an
  `MqttMessageReceived` that is itself internal, so the kit needs a factory and not only the
  `InternalsVisibleTo` it already has; and the RFC's own `ActorTestContextBase` extraction is a
  base-class insertion under a published generic with 126 consumer files behind it. Its own change
  doc, not a pass. Two facts the RFC does not carry: that internal parameter, and that the first
  consumer's four `ServiceProviderHandlerBase` subclasses have no test today — its own Ppc test kit
  bypasses the handler stack by its own documentation, which is both the strongest argument for the
  feature and the reason nothing regresses while it waits. *(TKIT pass, Reviewer's question 7 —
  `TKIT`.)*

## `IO` — the digital and analog I/O contract bindings (2026-09-05)

- **A state payload of the wrong schema decodes as a value nothing sent.** The IO pass added a
  schema-verifier guard on every inbound decode, which refuses an empty or truncated payload; it
  cannot refuse a *well-formed* payload of another type, because the layouts agree. The hole is
  directional: an analog payload delivered on a digital topic forwards a value (probe: `AiStatePayload`
  carrying `4.2` → `true`, carrying `0.0` → `false`), while the reverse is refused. Only the `schema`
  MQTT user property distinguishes it — every publisher on this wire sets one
  (`hal-raspberry` `Vion.Hal.Raspberry.dotnet/Handlers/DigitalOutputHandler.cs:83,102,139`) and the far
  side already checks one (`service-provider-sdk-dotnet`
  `Vion.ServiceProvider.Sdk/Infrastructure/MqttApplicationMessageExtensions.cs:182-191`, throwing
  `InvalidPayloadSchemaException`). `MqttMessageReceived` carries `UserProperties`
  (`Vion.Dale.Sdk/Mqtt/ActorMessages.cs:79-84`) and `ServiceProviderMqttMessage` does not expose them:
  a small accessor on that `[PublicApi]` struct, then a one-line check in each of this area's four
  decode sites. *(IO pass rows 28 and 69 — `BIND`.)*
  **Runtime review (2026-09-06):** the runtime is a reader — `Dale/Mqtt/MqttClient.cs:135-139`
  builds every inbound `MqttMessageReceived` with `e.ApplicationMessage.GetUserProperties()` — so
  the `schema` property is already populated on the message this area decodes, and the accessor
  `ServiceProviderMqttMessage` lacks is one line from a live value.
- **A command that the far side refused is invisible to the block.** Every command this area publishes
  names a response topic (`Vion.Dale.Sdk.DigitalIo/Output/DigitalOutputHandler.cs:104`, published at
  `:75`) and nothing in the runtime subscribes it — `grep -rn '/response' dale/Dale --include=*.cs` is
  zero hits. The far side answers there on both paths: `hal-raspberry`
  `Vion.Hal.Raspberry.dotnet/Handlers/DigitalOutputHandler.cs:185-191` on success and `:206-213` with
  `RequestStatus.Error` for a malformed payload or a hardware write error. A block therefore sees only
  the retained state that a *successful* write produces, and a per-command failure reaches no one.
  Subscribing it is a new wire behaviour: a new message type, a new arm, and a decision about what a
  block observes. *(IO pass row 64 — `IO` with `BIND`.)*
  **Runtime review (2026-09-06):** re-verified against the runtime — `git grep -n "/response" --
  '*.cs' '*.json' '*.md'` there is zero hits, while
  `Dale/Mqtt/Handlers/ServicePropertyHandler.cs:195-217` publishes *to* a response topic for a
  service-property set — so the runtime knows the pattern and subscribes none of this area's
  answers.
- **The core SDK has the same unmarked public type the IO pass fixed in its own packages.**
  `Vion.Dale.Sdk/ServiceCollectionExtensions.cs:8-10` is public, carries neither `[PublicApi]` nor
  `[InternalApi]`, and sits in the undeclared root namespace — so `DALE014` never asks, exactly as it
  never asked about the two `DependencyInjection` classes. `Vion.Dale.Sdk` is packable and carries the
  analyzer, so the rule is live there too. **Decided** — decision 0145 — but not separately fixable:
  the root namespace is the only declaration that reaches this type, `DALE014` matches a declaration as
  a prefix, and declaring the root therefore arms all twenty of the assembly's namespaces. Three types
  sit in that root namespace and 217 are behind the same declaration, so this row lands with the `BIND`
  row above or not at all. *(Found while correcting IO pass row 50's Why — `BIND`; decision 0145.)*

## `HTTP` — the logic-block HTTP client (2026-09-06)

- **A per-request timeout does not bound the response body.** The token reaches the exchange up to
  the response headers and no further: the delegate that reads the body runs without it
  (`Vion.Dale.Sdk.Http/HttpRequestExecutor.cs:301` against `:139`), and the serializer reads and
  deserializes with none (`HttpContentSerializer.cs:25-26`). For `SendRequest` the callback reads the
  body after the executor returned and disposed the source, so not even the client's ceiling is
  between the block and a stalled body. The consumer who would meet it is a block streaming a large
  response from a server that answers headers promptly and then stops. Not fixed here because
  threading the token changes the exception class on that path — a stalled body would start arriving
  as a cancellation rather than as whatever the stream raises — which is a change to what a callback
  receives today, not an area-local repair. A failure raised on that unbounded stretch does at least
  keep its own class: the relabel predicate (`HttpRequestExecutor.cs:351`) asks whether what failed
  was a cancellation, not only whether the source had fired, so a body that will not parse after the
  bound elapsed still arrives as a `JsonException` (`AC-HTTP-006.1`). *(HTTP pass row 45 — `HTTP`;
  the clause added by the fix-up round.)*
- **A callback lost before the block's first message stays lost.** A block that issues a request from
  its constructor may have its answer arrive before it has an actor; the dispatcher refuses the
  self-send (`AC-LIFE-006.3`) and the package turns that refusal into a log line, so neither callback
  runs and the block waits forever (`AC-HTTP-005.2` states it). Neither cure belongs to this package:
  re-queuing needs an actor it does not have, and refusing at issue time needs to know whether the
  block has one, which `IActorDispatcher`'s two members do not expose. The HTTP pass corrected the log
  message, which had named disposal as the cause, and stated the outcome on the page.
  *(HTTP pass row 18 — `LIFE`.)*
- **The package ships no HTTP test kit.** `ILogicBlockHttpClient` mocks cleanly, but there is no fake
  harness with the byte-level fidelity `FakeModbusTcpHarness` gives, and `AC-TKIT-013.1` names five
  kits of which this is not one. Raised by the first consumer while evaluating the package for a real
  device: `logic-block-libraries/docs/notes/2026-09-04-emu-m-center-integration-options.md:75-77`
  ("no fake harness comparable to `FakeModbusTcpHarness`"). A sixth kit is its own change doc.
  *(HTTP pass row 64a — `HTTP`, raised by `logic-block-libraries`.)*
- **The package surfaces no link or connection diagnostics.** There is no HTTP analogue of
  `ModbusLink` / `ModbusSocket`: the transport is `IHttpClientFactory`'s pooled handler
  (`AC-HTTP-002.1`) and nothing reads its state, so a device family cannot mirror
  `_families/modbus-tcp-device`'s link criterion. Raised by the same note, `:78-79`. A feature band
  rather than a defect, and it would need the package to own the primary handler.
  *(HTTP pass row 64b — `HTTP`, raised by `logic-block-libraries`.)*

## `RELEASE` — the packed artifact and the checks that never read it (2026-09-10)

- **No check in this repository consumes the packed package, so a release can be unusable with every
  gate green.** The SDK's own projects reference each other with `ProjectReference` and `examples/`
  reference a *published* package, so nothing here has ever restored the artifact this repository
  produces. `0.12.0` is the proof: a solution build, the whole test suite, the style gate and
  `verify-packages` were all green while `build/Vion.Dale.Sdk.targets` was not well-formed XML, and
  the package shipped to nuget.org failing every consumer's first build with `MSB4024`. The
  0.12.1 hotfix closes the specific class — `scripts/packed-msbuild-lint.ps1` parses every file
  packed under a NuGet build folder, on every pull request, before a package exists, deriving what
  it scans from the declarations rather than a list (its own header states the shapes it still
  cannot resolve) — and closes nothing beyond it: well-formed XML is necessary and not sufficient, and no file scan can see a
  package that restores but does not work. `verify-packages` is not the place for the rest either.
  It runs *after* both pushes, so it can report a bad release and cannot prevent one, and it reads
  assembly versions out of the artifact rather than consuming it. What would prevent one is a
  **pre-public release regression suite** that restores the packed package as a real consumer and
  exercises it at runtime. That is its own change doc, not a hotfix, and the operator ruled it out
  of this one (2026-09-10) precisely so that half of it is not built here: a check that looks like
  verification and is not is the shape this incident already demonstrated.
  *(0.12.1 hotfix — the burned 0.12.0 release; operator ruling relayed by the sdd-closeout
  coordinator.)*
