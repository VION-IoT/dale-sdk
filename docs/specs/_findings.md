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
  compile-time half — `ANLZ`.)*

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
  contract factory filters by reference and refuses the whole configuration when an assembly cannot be
  enumerated (`Vion.Dale.Sdk/Reflection/AssemblyExtensions.cs:56-59`, `:100-103`). The private runtime
  rejects the all-or-nothing helper twice in its own comments and degrades instead
  (`Dale/Program.cs:115-118`, `LogicSystemConfigurationInitializer.cs:635-640`), so the shape to
  converge on is the runtime's degrading scan rather than the SDK's helper.
  *(BIND pass row 36 — `PLUG`, promoted by the operator.)*
- **A message struct declared beside its contract class compiles, is read by nothing and emits
  nothing.** `[Command]`, `[StateUpdate]` and `[RequestResponse]` declare `AttributeTargets.Struct`
  with no nesting constraint, while the generator reads only the struct types nested in the contract
  class (`Vion.Dale.Sdk.Generators/LogicClassGenerator.cs:188-196`). No `DALE` code covers the shape.
  Narrowing the attribute targets is a source-breaking change to a published attribute; the diagnostic
  belongs to the analyzer registry. *(BIND pass row 46 — `ANLZ`.)*
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
- **A contract-type token has no emptiness and no uniqueness guard.**
  `Vion.Dale.Sdk/Configuration/Contract/ServiceProviderContractTypeAttribute.cs:28-31` assigns the
  token with no validation, and no `DALE` code covers it, although the token is a cloud-facing stable
  identifier (`identifier-stability.md`). A duplicate reaches the document as two contract types with
  one name. Uniqueness is a whole-compilation analysis of `DALE043`'s size.
  *(BIND pass row 173 — `ANLZ`.)*
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
  *(BIND pass row 191 — `TKIT`.)*
- **The TestKit discovers a contract's service registrations from the block's own properties, where a
  host discovers them from the plugin.** `LogicBlockTestContextBuilder.cs:326-345` walks the block
  under test for marked property types and runs every `IConfigureServices` in their assemblies, so a
  block whose contract needs a dependency the block does not itself hold is untestable.
  *(BIND pass row 193 — `TKIT`.)*
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
