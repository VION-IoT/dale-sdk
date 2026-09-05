# Finding ledger

Defects an area pass found and deliberately did **not** fix: too big for the pass, or reaching past the
area that found them. One line each, newest area last. Triaged in bulk at the retro
([`../retro/`](../retro/)) — an entry that gets scheduled becomes a Jira item and is struck here with its
key; an entry that is fixed is deleted with the PR that fixes it.

Not for: a small area-local defect (the pass fixes it), a stated behavior that merely surprises (the spec
page states it), or a missing test (that is a `GAP` marker on the page).

## `INTRO` — the introspection document and identifier stability (2026-09-02)

- **`dale list` cannot render a nested block's short name, and drops an endpoint whose identifier is
  empty.** The projection splits a block's identity on `.` (`Vion.Dale.Cli/Commands/ListCommand.cs:72`,
  `:153`), so a nested block lists as `Outer+NestedBlock` — and it filters an empty identifier out
  entirely (`:74`, `:76`), which contradicts the promise that `dale list` prints the identifiers as the
  document emits them. `AC-INTRO-014.3` removes the second input at the source; the first survives
  because `AC-INTRO-004.1` keeps a block's identity in CLR form deliberately. The projection is Tier B.
  *(INTRO pass row 88 — `CLI`.)*
- **Nothing warns when a library's `<PackageId>` and `<AssemblyName>` diverge after the identity
  change.** `AC-INTRO-001.2` makes the document's package identity the nuspec id, which is the id the
  platform registers; a project that changes only its assembly name now silently keeps its keys, and one
  that changes only its package id silently re-namespaces them. A `dale build` / `dale pack` warning is
  where an author would see it. *(INTRO pass, residue of row 2 — `CLI`.)*
- **`dale list` runs the introspection without the development-only exclusion**, so it lists blocks the
  packed artifact omits (`Vion.Dale.Cli/Helpers/ParserRunner.cs:250`–`:258` passes only `--package-id`).
  Whether the CLI should filter them or mark them in its output is a question about what `dale list` is
  for, which is decided when the CLI is specced; the introspection page says what the listing means in
  the meantime. *(INTRO pass amendment 2, M7 — `CLI`.)*
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

## `CTRL` — the development host's control surface and lifecycle (2026-09-04)

- **A clock-mode switch rebuilds the next generation by writing the process environment.** The
  supervisor sets `DALE_DEVHOST_STEPPED` so the rebuilt host's `WithWebUi` reads it
  (`Vion.Dale.DevHost.Web/DevHostWebRunner.cs:195-198`,
  `DevHostBuilderExtensions.cs:43`), because `Func<string?, IDevHost>` carries no mode parameter. It is
  process-global: a second host built in the same process — every test that builds one — inherits a
  mode its caller never asked for. A runner-held static would be the same global under another name,
  and a factory that takes the mode is a surface change. *(CTRL pass row 40 — `CTRL`.)*
- **The duration converter's read half answers 500 where every other bad body answers 400.**
  `Iso8601TimeSpanConverter.Read` lets `TimeSpan.Parse`'s `FormatException` escape
  (`Vion.Dale.DevHost.Web/Api/Serialization/Iso8601TimeSpanConverter.cs:107-123`), which the input
  pipeline does not translate. Unreachable today — every write body binds as `object` or `JsonElement`
  and is decoded by the control surface instead — and live the moment a typed duration reaches a
  request body. *(CTRL pass row 141 — `CTRL`.)*
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
- **Two shipped packages are outside the public-API snapshot.** `Vion.Dale.DevHost` and
  `Vion.Dale.DevHost.Web` are `IsPackable` (`Vion.Dale.DevHost.csproj:9-10`,
  `Vion.Dale.DevHost.Web.csproj:36-37`) and absent from `docs/snapshots/publicapi-manifest.json`'s 12
  assemblies, so a member removed from `IDevHostControl` moves no snapshot and a consumer's build is the
  first thing that notices. Whether the DevHost belongs in that manifest is a corpus decision, not this
  area's. *(CTRL pass row 201 — the retro.)*

## `LIFE` — the block's life inside its actor, and the pipeline that carries it (2026-09-04)

- **Two published message types nothing in this repository sends or receives.**
  `LinkLogicBlockInterfaceActors` and `SetRemoteFunctionInterfaceInstallationTopics`
  (`Vion.Dale.Sdk/Messages/ActorMessages.cs:65-75`) have no handler and no construction site here — the
  private runtime's remote-interface proxy handler is the only reader. The page carries them and
  specifies neither, because no in-repo test can reach one. *(LIFE pass row 5 — `LIFE`.)*
- **A contract handler's reference is minted whether or not the actor exists.** `LookupByName` builds a
  reference from a name alone (`Vion.Dale.ProtoActor/ActorSystem.cs:373-376`,
  `PidUtils.cs:7-10`), so a block whose handler class is absent from the host binds to nothing and every
  message that contract sends becomes a dead letter with a warning and no error. The consumer is real:
  the development host spawns one stand-in per discovered handler
  (`Vion.Dale.DevHost/DevLogicSystemInitializer.cs:390`), so a contract whose handler it did not
  discover is silently dead. A registry lookup at link time is a spawn-ordering contract shared with the
  private runtime, which is past this pass. *(LIFE pass row 26 — `LIFE`.)*
- **A `[Persistent]` property more than one level inside a block is silently not persisted.** Discovery
  walks a class-typed property's own properties and no further
  (`Vion.Dale.Sdk/Persistence/PersistentData.cs:264-295`), with no diagnostic at any door. Recursing
  needs a cycle guard, a key grammar for arbitrary depth and a decision about collections — a change doc
  of its own. *(LIFE pass row 89 — `LIFE`.)*
- **A block's actor name is ambiguous when its name or identifier contains the separator.** The name is
  a concatenation (`Vion.Dale.Sdk/Utils/LogicBlockUtils.cs:12`), so `logicblock_A_B_c_d` reads two ways.
  Every reader in the repository matches the prefix rather than splitting, so there is no harm to name
  today; the entry exists against the day one splits. *(LIFE pass row 137 — `LIFE`.)*
- **The SDK ships an OpenTelemetry meter no host in this repository wires.** `ActorVitalsMeter` is
  public and constructed only by its own tests; `AddDaleSdk` registers the vitals core and not the meter
  (`Vion.Dale.Sdk/ServiceCollectionExtensions.cs:12-28`), so a host that adds the SDK gets the core and
  no metrics until it builds the meter itself. Registering it there would start a meter in the
  development host, the TestKit and every example. *(LIFE pass row 208 — `LIFE`.)*
- **The two dependency-injection registrations are independent, and one in-repo host uses only one.**
  `AddDaleSdk` and `AddProtoActorSystem` can each be called without the other
  (`Vion.Dale.Sdk/ServiceCollectionExtensions.cs:12`,
  `Vion.Dale.ProtoActor/Extensions/ServiceCollectionExtensions.cs:9`); the introspecting parser calls
  only the first (`Vion.Dale.LogicBlockParser/Program.cs:117`) and never spawns an actor. Whether *an
  actor system without the SDK's registrations* is a supported composition is a decision rather than a
  defect — the pipeline documents what it takes from the container instead. *(LIFE pass row 216 —
  the operator.)*
- **`IActorContext.Headers` is published and nothing reads it.** The member is on the interface every
  block handler is handed (`Vion.Dale.Sdk/Abstractions/IActorContext.cs:8`) and implemented over the
  message's own headers (`Vion.Dale.ProtoActor/ActorContext.cs:37-40`); a repository-wide search finds
  no reader in the SDK, the development host, the TestKit or the examples. Removing it is a surface
  decision under `sdk-surface-conventions.md`, not a pass's. *(LIFE pass row 227 — `LIFE`.)*
- **A bound service the configuration gives no identifier is dropped at six sites for the instance's
  life.** The announcement omits it (`Vion.Dale.Sdk/Core/LogicBlockBase.cs:1180-1184`) and every value
  change, clear, flush and drain drops it in turn, each with its own warning, while the block reports
  itself healthy — `AC-LIFE-003.4` states it. Failing the configuration instead is the right shape, but
  its reader is the cloud's service allocation: the runtime builds the lookup from the configuration
  payload's service list, and whether a fielded configuration may lawfully omit a service the block
  binds is a contracts question no read in this repository answers. *(LIFE pass row 30 — `LIFE`.)*
- **A block whose configuration failed still starts, publishes and acknowledges.** The start arm has no
  configuration check (`Vion.Dale.Sdk/Core/LogicBlockBase.cs:263-277`), so such a block runs its start
  hook, publishes over whatever bindings the failed configuration registered, arms its periodic save and
  acknowledges — and the host reports itself started with the block's members reading their defaults.
  Refusing has three shapes and each has a reader: throwing makes the runtime's start time out and the
  development host's boot fail, undoing the design that keeps the host up with the failure on its health
  surface (`AC-CTRL-003.*`); acknowledging without starting mints an "acknowledged but inert" state the
  page would have to state; and a failure acknowledgement is a wire change on `StartLogicBlockResponse`.
  That is a decision, not a pass's. *(LIFE pass row 47 — `LIFE` + `CTRL`.)*
- **The development host restores nothing, so the start hook's persisted-value promise is one it cannot
  keep.** `AC-LIFE-012.2` says a member read in the start hook holds its restored value *on a host that
  restores*; the development host's start sequence sends no restore
  (`Vion.Dale.DevHost/DevLogicSystemInitializer.cs:175-205`), so a block author developing there reads a
  default and gets the operator's value in the field. Whether the host should send an empty restore for
  sequence parity — the way it sends the snapshot request it discards — is the host's decision.
  *(LIFE pass row 124 — `CTRL`.)*
- **`Vion.Dale.ProtoActor`, and three namespaces of `Vion.Dale.Sdk`, are outside the public-API
  snapshot.** The manifest covers 12 assemblies and `Vion.Dale.ProtoActor` is not among them although it
  is a shipped package a consumer's block depends on; `PublicApiConfig.cs` declares only `Core`,
  `Emission` and `Utils` as public namespaces, so the 28 message types, the 7 diagnostics types and the
  8 actor abstractions this page specifies move no snapshot when they change. The same shape as the two
  development-host packages above. *(LIFE pass row 217 — the retro.)*

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
- **A block's interface endpoints come from public properties only, and the walk cannot be widened
  without moving every consumer's artifact.** `DeclarativeInterfaceBinder.cs:86` walks public instance
  properties while the contract binder walks non-public ones too. This pass refuses the *declaration*
  that lands outside the walk (`AC-BIND-001.3`) rather than widening it, because widening mints
  endpoints into the introspection document every consumer uploads, whose reader is the cloud.
  *(BIND pass row 21 — `INTRO`.)*
- **The generator's candidate predicate matches any attribute whose name contains "Contract".**
  `Vion.Dale.Sdk.Generators/LogicClassGenerator.cs:36-40` runs on every class in every compilation and
  is confirmed semantically afterwards, so the output is correct — but the predicate is the incremental
  generator's cache key, so its breadth is a build-time cost every consumer pays with no functional
  observable. *(BIND pass row 186 — `ANLZ`.)*
- **The TestKit maps a contract an inclusion gate would have excluded.**
  `Vion.Dale.Sdk.TestKit/LogicBlockTestContextBuilder.cs:352-369` discovers contract identifiers from
  marked, writable properties and reads no `[IncludedWhen]`, which is two of the binder's three
  conditions — so a gated-out contract is mapped in a test and absent in a host.
  *(BIND pass row 191 — `TKIT`.)* **TKIT pass row 21:** parked again, and the page now states the
  discovery rule it follows from (`AC-TKIT-002.2`). Reading a gate means running the gate evaluator
  the binder owns, which is `BIND`'s surface and not a line in a kit; decision `0081` names
  "runtime/DevHost/TestKit" as one rule, so this is that rule's TestKit half and moves with it.
- **Every namespace of this area but two is outside the public-API ratchet.**
  `Vion.Dale.Sdk/PublicApiConfig.cs:6-8` declares only `Core`, `Emission` and `Utils`, so `DALE014`
  never asks for a mark in `Configuration.Contract`, `Configuration.Interfaces`, `CodeGeneration`,
  `Abstractions`, `Mqtt`, `Messages` or `Reflection`. The sharpest case is
  `ServiceProviderContractTypeAttribute`, an attribute every consumer authors that is on no manifest
  while its sibling `LogicBlockContractBase` in the same namespace is. Same shape as the `CTRL` and
  `LIFE` findings above. *(BIND pass row 195 — the retro.)*
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
- **A handler that survives a reconfiguration with no contract mappings keeps a stale map.** The
  runtime sends `LinkLogicBlockContractActors` only when there is at least one mapping
  (`LogicSystemConfigurationInitializer.cs:626-645`), while handler actors are root actors that
  outlive the logic-block actors a reconfiguration stops and recreates — so a handler whose mappings
  went to zero keeps references to actors that no longer exist, and has no way to be told "none". No
  SDK change can cure it: the message that would say so is one the runtime does not send.
  *(BIND pass, second opinion's unrowed observable 5 — the runtime, promoted by the operator.)*
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
- **`[StructField]` on a target other than a struct's constructor parameter is judged by nothing.** The
  attribute allows more targets than the one walk that reads it
  (`TypeRefBuilder.BuildStructFieldAnnotations`, and `StructFieldPresentationBuilder.Build` over the
  same constructor), so a misplaced declaration compiles and emits nothing. The two rules that judge
  the knob are scoped to that reader's parameters (`AnalyzerHelper.IsStructFieldParameter`), so they
  do not report the misplacement either. Narrowing `AttributeTargets` is a source-breaking
  change to a published attribute — the same shape as the message-struct entry `DALE047` just closed,
  and the same reason it became a diagnostic rather than a narrowing. *(ANLZ pass row 111 — `ANLZ`.)*
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
- **A package packed without the analyzer assembly loses all forty-six diagnostics in silence.**
  `Vion.Dale.Sdk.csproj:92` packs the DLL under `Condition="Exists(…)"`, so a build that did not
  produce it yields a package that restores, compiles clean and judges nothing; the only signal is
  previously-red code turning green. The assertion that a packed `Vion.Dale.Sdk` carries
  `analyzers/dotnet/cs/Vion.Dale.Sdk.Generators.dll` belongs in the post-pack artifact gate
  (`scripts/verify-packed-assembly-versions.ps1`, `.github/workflows/publish.yml:72-93`), which was
  minted for this class of silent-bad-package failure. *(ANLZ pass row 152 — the release process,
  [`../releasing.md`](../releasing.md); the operator promotes.)*
- **Nothing requires a `#pragma warning disable DALE*` to say why.** Twenty-three of the thirty
  suppressions in this repository carry no reason comment, and a suppression is a claim that the shape
  is intended — the one kind of claim that ages worst. A lint would be a new gate script and a
  twenty-three-site edit across five other areas' fixtures. *(ANLZ pass row 167 — the retro.)*
- **The generator's `Contract`-substring predicate runs on every class in every compilation.**
  `Vion.Dale.Sdk.Generators/LogicClassGenerator.cs:36-40` matches any class carrying an attribute whose
  name *contains* `Contract`, and the semantic pass afterwards makes the output correct — so there is
  no functional observable, only the incremental generator's cache key being wider than it needs.
  Measuring the cost needs a build-time benchmark. *(BIND pass row 186, ANLZ pass row 176 — `ANLZ`.)*
- **An inclusion gate on a property typed as a generated contract interface draws a false error.**
  `IncludedWhenPredicateAnalyzer.cs:211` resolves `[LogicInterface]` through `AllInterfaces` — by
  symbol only — and in a Metalama-hosted build a generated contract interface is an error type, so
  `DALE043` reports a legitimately gated binding as ungateable. This is
  [`../sdk-surface-conventions.md`](../sdk-surface-conventions.md) § 5's blind spot, live;
  `AC-ANLZ-014.4` states it and
  `UnresolvedContractInterfacePinTests.ReportGateOnPropertyWithUnresolvedInterface` pins the outcome.
  The remedy an author would reach for, `[LogicBlockInterfaceBinding(typeof(…))]`, names the same
  unresolved type. The fix is the by-name half of the two-way lookup `ServiceRelationAnalyzer.cs:236-293`
  already carries, and the rule it would change is `AC-GATE-011.3`.
  *(ANLZ pass row 185 — `GATE` + `ANLZ`.)*
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
- **A `[DataRow]` containing a `]` hides a test from `test-style-lint` and from any tool sharing its
  regex.** `scripts/test-style-lint.ps1:38`'s attribute-block pattern is `\[[^\]\r\n]*\]`, so a row
  such as `[DataRow("Mode in ['Eco', 'Fast']", …)]` breaks the run of attribute lines and the method
  below it is neither checked nor counted. One test in `Vion.Dale.Sdk.Generators.Test` is in that
  shape; its citation had to be added by hand. The gate under-reports rather than over-reports, which
  is the worse direction for a ratchet. *(ANLZ pass — the retro.)*

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
- **Forty-three public types are outside the API manifest.** The three assemblies expose 73 public
  types against 30 `[PublicApi]` marks, and the unmarked set is deliberate in part and accidental in
  part: the TestKit substitutes `IRequestQueue` and `IModbusTcpClientProxy` and pins them as manifest
  types of its own, the first consumer injects `IModbusDataConverter` in production, and eleven
  exception types a block's error callback receives carry no mark at all. Sorting which is which is a
  release-note surface review. *(MODB pass row 156 — the retro's surface review.)*
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

- **The two upload conflicts are told apart by the endpoint's message text.** `dale upload
  --skip-duplicate` treats a 409 as a skip only when the response message contains `version` and
  `already exists` (`Vion.Dale.Cli/Commands/UploadCommand.cs:304-308`), because the API returns the same
  status and the same `ConflictException` for a duplicate version and for a package-id conflict. Six
  invocations ride on the match — both release workflows in the first consumer
  (`release-test.yml:45`, `release-prod.yml:83`) and four in this repository
  (`upload-libraries.yml:59`, `:90`, `examples.yml:61`, `:120`) — so a wording change at the endpoint
  turns each of them into a hard failure, or turns a package-id conflict into a reported success. The
  fix is a distinguishable field on the answer, which is the platform API's to add, not this tool's.
  `AC-CLI-011.8` states the substring match as today's contract. *(CLI pass row 137 — the platform
  API.)*
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
- **The `login` help's `--environment` default is whatever the developer's own store says.** The
  option's default is read from `~/.dale/config.json` when the command tree is built, so `dale login
  -h` prints `[default: test]` on a machine logged into the test environment and
  `[default: production]` on a clean runner — and the committed help snapshot regenerated on such a
  machine drifts from the one CI regenerates (the snapshot bot corrected exactly that one line on the
  CLI pass's branch before it merged). The fix-up's "redirected home" did not reach it either: on
  Windows `Environment.SpecialFolder.UserProfile` ignores the `USERPROFILE` variable. A help text
  should not depend on stored state; the default shown is the resolution rule (stored, else
  `production`), and the snapshot regeneration runs against an explicitly empty store root. Small,
  area-local, and worth a test that pins the help line under an empty root. *(Found at the merge of
  the CLI pass — `CLI`.)*

## `TKIT` — the five test kits (2026-09-06)

- **A persistent value declared for a property the block does not persist is accepted silently.**
  `Vion.Dale.Sdk.TestKit/LogicBlockTestContextBuilder.cs:444-464` resolves the storage key from the
  block's own service-property bindings and falls back to `_direct.{PropertyName}` when the search
  misses, so a typo or a property that is not persistent restores under a key the block never reads
  and the test asserts against a default it believes it set. Catching it needs the binder's own view
  of which properties persist, which is `BIND`'s surface rather than a line here — the same reason
  the inclusion-gate entry above is parked. *(TKIT pass row 20 — `BIND`.)*
- **Four shipped SDK packages carry no analyzer reference.** `Vion.Dale.Sdk.Modbus.Core`,
  `Vion.Dale.Sdk.Modbus.Rtu`, `Vion.Dale.Sdk.Modbus.Tcp` and `Vion.Dale.Sdk.Http` each declare
  `[assembly: PublicApiNamespace]` and none of them references `Vion.Dale.Sdk.Generators`, so
  `DALE014` never asks them for a surface mark and the API manifest's diff — auto-committed on a
  pull request, not failed — is the only gate on their public types. The fix is the one
  `ProjectReference` element this pass added to the five kits
  (`Vion.Dale.Sdk.DigitalIo.csproj:43` is the shape), plus a wiring probe per package so the
  reference is proven live rather than merely present. Not fixed here because they are their own
  pages' packages. *(TKIT pass row 171's sweep — `ANLZ` with `MODB` and `HTTP`.)*
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
  [`../changes/archive/2026-09-06-tkit-pass.md`](../changes/archive/2026-09-06-tkit-pass.md).
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

- **A block cannot ask whether a face it holds is mapped.** `LogicBlockContractBase` drops a write on
  an unmapped contract silently and exposes no mapping state, so a block whose own diagnostics depend
  on knowing has no supported answer. The first consumer reads the protected `LogicBlockContractId`
  **by reflection** to get one — `logic-block-libraries` `Ecocoach.EnergyManagement/LogicBlocks/Shared/DigitalOutputWiringProbe.cs:21-27`,
  registered at `Ecocoach.EnergyManagement/DependencyInjection.cs:93` and consumed by
  `HeatPumpSgReady.cs:60,569` for its unwired-contact error state; the probe's own doc names the SDK
  gap as VION-130. The fix is a member **added** to `LogicBlockContractBase`, never a promotion of
  `LogicBlockContractId`: promoted, the consumer's `GetProperty(…, NonPublic)` returns null and every
  output silently reports unmapped, so a correctly wired pump raises its fault. The area-local
  alternative — an `IsMapped` on the four faces — changes a face's members, which is a wire-surface
  change with its own readers. *(IO pass row 16 — `BIND`.)*
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
- **A command that the far side refused is invisible to the block.** Every command this area publishes
  names a response topic (`Vion.Dale.Sdk.DigitalIo/Output/DigitalOutputHandler.cs:104`, published at
  `:75`) and nothing in the runtime subscribes it — `grep -rn '/response' dale/Dale --include=*.cs` is
  zero hits. The far side answers there on both paths: `hal-raspberry`
  `Vion.Hal.Raspberry.dotnet/Handlers/DigitalOutputHandler.cs:185-191` on success and `:206-213` with
  `RequestStatus.Error` for a malformed payload or a hardware write error. A block therefore sees only
  the retained state that a *successful* write produces, and a per-command failure reaches no one.
  Subscribing it is a new wire behaviour: a new message type, a new arm, and a decision about what a
  block observes. *(IO pass row 64 — `IO` with `BIND`.)*
- **The core SDK has the same unmarked public type the IO pass fixed in its own packages.**
  `Vion.Dale.Sdk/ServiceCollectionExtensions.cs:8-10` is public, carries neither `[PublicApi]` nor
  `[InternalApi]`, and sits in the undeclared root namespace — so `DALE014` never asks, exactly as it
  never asked about the two `DependencyInjection` classes. `Vion.Dale.Sdk` is packable and carries the
  analyzer, so the rule is live there too. *(Found while correcting IO pass row 50's Why — `BIND`.)*
- **`Vion.Contracts`' generated payload verifiers are unusable as published.** Every
  `Verify<Payload>Payload(ByteBuffer)` wrapper — all ten in 3.7.0 — passes an empty file identifier to
  `Verifier.VerifyBuffer`, which rejects any identifier that is not four characters, so the wrapper
  throws `ArgumentException: FlatBuffers: file identifier must be length4` on every non-empty buffer
  including a valid one, and returns `false` only on an empty one. The generated
  `<Payload>Verify.Verify` beneath it is public and correct, which is what the IO pass calls directly
  with a `null` identifier. Anyone reaching for the documented wrapper gets an exception.
  *(Found by the IO pass, probes P5 and V6 — `vion-contracts`.)*
- **`hal-sim` writes the two payload identity strings transposed.**
  `HalSim/FlatBufferPayloadFactory.cs:33,47,61,75` calls
  `Create*StatePayload(builder, endpointOffset, hwBlockOffset, value)` where the generated parameters
  are `(builder, hardware_block_instance_idOffset, endpoint_identifierOffset, value)`;
  `hal-raspberry` `Vion.Hal.Raspberry.dotnet/Handlers/DigitalInputHandler.cs:157` passes them the other
  way round. Nothing has noticed because the Dale SDK reads neither field — it takes the contract
  identity from the topic (`AC-IO-005.3`). *(Found by the IO pass's reader sweep — `hal-sim`.)*
