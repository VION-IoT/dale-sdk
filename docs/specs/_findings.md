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
