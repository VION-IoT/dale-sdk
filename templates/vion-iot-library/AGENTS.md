# Agent Instructions

This is a Dale LogicBlock library. LogicBlocks are actor-based IoT computation
units built on the Dale SDK by Vion-IoT.

## Start here

The library ships one worked example, `Thermostat` — a self-contained control loop
(setpoint → heat/cool → temperature) that shows the core SDK surface with **no hardware**:
writable config, a colour-coded status pill (an enum with `[Severity]` + `[EnumLabel]`),
live unit-bearing measuring points, members that are **both** a property and a metric (the
cross-fill rule — metadata declared once on `[ServiceProperty]`, inherited by a bare
`[ServiceMeasuringPoint]`), a `TotalIncreasing` accumulator, and a `[Timer]`. Run `dale dev`
and open it. `scenarios/thermostat.scenario.json` drives it end to end — run it with
`dale scenario run thermostat` while `dale dev --stepped` is up, or open it in the Player. The
DevHost boots **folder-driven** (RFC 0008): the instance graph lives in `topologies/*.topology.json`,
replayable checks in `scenarios/*.scenario.json` — you don't configure blocks in `Program.cs`.

## CLI

Use the `dale` CLI instead of raw dotnet commands:

```bash
dale build                # build the solution
dale test                 # run tests
dale dev                  # start the DevHost with web UI at localhost:5000
dale dev --stepped        # … with a deterministic virtual clock (scenarios step exactly)
dale dev --headless       # … without a browser; prints a JSON readiness line (for CI/agents)
dale list                 # show logic blocks, contracts, properties, interfaces
dale list --verbose       # show full detail
dale pack                 # create .nupkg for deployment

dale scenario validate           # resolve every name path + topology against the wired host — offline; the CI gate
dale scenario run <id>           # drive a scenario and print the Player's report
dale scenario schema             # print the scenario JSON Schema (enriched with this topology's name paths)
dale scenario scaffold <id>      # graduate a scenario into a typed C# test
dale scenario open <id>          # open a scenario in the running Player (deep link)

dale add logicblock <Name>                                    # new LogicBlock class
dale add serviceproperty <Name> --type <type> --to <Block>    # add a writable property
dale add timer <Name> --interval <seconds> --to <Block>       # add a periodic callback

dale login                # authenticate with the cloud
dale upload               # pack and publish to the cloud
```

## SDK Conventions

LogicBlocks extend `LogicBlockBase` and use declarative attributes:

| Attribute | Purpose |
|-----------|---------|
| `[ServiceProperty]` | Observable property (public setter = writable + persistent by default). `Title`, `Description`, `Unit`, `Minimum`/`Maximum`, `WriteOnly` (string only) all configurable. |
| `[ServiceMeasuringPoint]` | Read-only metric (`private set`). `Title`, `Description`, `Unit`, `Kind` (Measurement / Total / TotalIncreasing) configurable. |
| `[Timer(seconds)]` | Periodic callback method. Method must be void + parameterless. |
| `[Persistent]` | Opts in private-set properties to persistence; `[Persistent(Exclude = true)]` opts out writable ones. |
| `[ServiceProviderContractBinding(Identifier = "Name")]` | Hardware I/O binding (digital/analog). Other named args: `DefaultName`, `Multiplicity` (`LinkMultiplicity`), `Tags`. |
| `[Presentation(...)]` | UI hints: `Group` (use `PropertyGroup.*` constants), `Importance` (Primary/Secondary/Normal/Hidden), `Order`, `Decimals`, `StatusIndicator` (enum properties), `UiHint` (use `UiHints.*` constants like Trigger / Sparkline / Slider), `Format` (moment.js format string for `DateTime`/`TimeSpan`). |
| `[LogicBlock(Name = "...", Icon = "...", Groups = new[] {...})]` | Block-level display metadata. |
| `[LogicBlockContract]` / `[Command]` / `[RequestResponse]` / `[StateUpdate]` | Inter-block messaging. |
| `[LogicBlockInterfaceBinding(typeof(IFoo))]` | Logic-block interface binding metadata: `Identifier`, `DefaultName`, `Multiplicity` (`LinkMultiplicity`), `Tags`. |
| `[Severity(StatusSeverity.X)]` / `[EnumLabel("…")]` | Per-enum-member annotations: severity for status-indicator colouring; display label for the dashboard. |

## Project Structure

```
{ProjectName}/                  # LogicBlock library (netstandard2.1)
{ProjectName}.Test/             # Unit tests (MSTest + Vion.Dale.Sdk.TestKit)
{ProjectName}.DevHost/          # Dev runtime with web UI — boots folder-driven (RFC 0008)
topologies/                     # *.topology.json — the instance graph (topology-as-data)
scenarios/                      # *.scenario.json — replayable, deterministic checks
```

## Code Style

- **ImplicitUsings: false** — all using statements must be explicit
- **Nullable: enabled**
- **LangVersion: latest**
- Target framework: **netstandard2.1** (library), **net10.0** (DevHost/tests)
- Constructor takes `ILogger logger` and passes to `base(logger)`
- Override `Ready()` for initialization logic (subscribe to I/O events, etc.)

## Common Patterns

### Adding a property with change tracking

```csharp
private double _temperature;

[ServiceProperty]
public double Temperature
{
    get => _temperature;
    set
    {
        if (_temperature != value)
        {
            _temperature = value;
            _logger.LogInformation("Temperature changed to {Value}", value);
        }
    }
}
```

### Persistence

`[ServiceProperty]` with a public setter is writable and persistent by default.
Properties with a private or no setter need `[Persistent]` to opt in.
Use `[Persistent(Exclude = true)]` to opt out a writable property.

```csharp
// Public setter — writable + persistent by default
[ServiceProperty]
public int Counter { get; set; }

// Public setter, opted out of persistence
[ServiceProperty]
[Persistent(Exclude = true)]
public int TransientValue { get; set; }

// Private setter — needs [Persistent] to survive restarts
[ServiceProperty]
[Persistent]
public int TotalEvents { get; private set; }
```

### Timer with logic

```csharp
[Timer(10)]
public void Poll()
{
    // Called every 10 seconds by the runtime
}
```

## DevHost

Run the DevHost to test logic blocks locally with a web UI:

```bash
dale dev
# Starts DevHost, web UI at http://localhost:5000
```

Works from the solution directory, library project directory, or DevHost directory.

### Boot style — folder-driven is the default

`Program.cs` boots **folder-driven** via `DevHostWebRunner.RunFolderDrivenAsync(...)`: the instance
graph comes from `topologies/*.topology.json` and the replayable checks from
`scenarios/*.scenario.json`, both discovered from disk. This is the default for a project DevHost —
it's the only path that gets the topology-as-data files, the topology-switch UI, scenario
recycle-on-run, and `dale dev --stepped` determinism, and it means adding a block or rewiring is a
topology edit, not a code change.

The lower-level code-declared form — `DevConfigurationBuilder.Create()...AddLogicBlock<T>()...Build()`
fed to `DevHostBuilder.WithConfiguration(...)` (and `RunFolderDrivenAsync` is built on top of it) —
is for **in-process tests**: a test project references `Vion.Dale.DevHost`, wires a fixed config in
C#, and drives `host.Control` directly (see "Verifying headlessly" below). Use folder-driven for the
runnable DevHost; use the code-config builder inside tests.

## Verifying headlessly (for CI & agents)

The DevHost boots the *real* wired network (unlike TestKit, which stubs every collaborator), so it catches
bugs that only appear in the wired, real-messaging path (e.g. a block that never actually sends the request a
unit test hand-feeds it). Three ways to drive it without a browser, in order of what to reach for:

**1. Scenario files (RFC 0008) — the committed, replayable artifact.** A `scenarios/*.scenario.json` file
describes setup → ordered steps → a watch list → human judgments, consumed identically by the Player, by
CI, and by agents (see `scenarios/thermostat.scenario.json`):

```bash
dale dev --stepped                # boot a deterministic virtual clock — scenarios step exactly, no wall-clock waiting
dale scenario validate            # resolve every name path + topology against the wired host — OFFLINE; the PR gate
dale scenario run thermostat      # execute it and print the Player's report
dale scenario scaffold thermostat # graduate it into a typed C# test you run with `dale test`
```

Under `--stepped` the time-bearing steps run in **virtual time**: `advance` jumps the clock (firing
`[Timer]`s), `settle` advances hop-by-hop until the watched signals stabilize, and `waitUntil` resolves
deterministically. `expect` steps auto-assert — `above` / `below` / `equals` (+ `tolerance`) / `notEquals` /
`oneOf`, and a relational `{ "path": "Block.Other" }` comparand — and fail the run loudly; `judge[]` items
are for human sign-off. An agent stages a human's verification view by committing a scenario and citing its
**id** in the PR.

**Authoring loop:** (1) declare a dedicated, minimal `topologies/*.topology.json` with just the blocks the
behaviour needs; (2) pin every non-deterministic input (live data, wall clock, RNG) — or omit the block from
the topology; (3) stage starting state with `setup` `set` steps (only writable `[ServiceProperty]`s — you
can't write a read-only measuring point; reach accumulated state by driving inputs over `advance` steps);
(4) drive + assert with `advance` / `settle` / `expect`; (5) run it, read the failure `detail`, adjust. Two
traps: a `settle` over a never-settling signal (a free-running counter, a clock-derived value) silently
burns its budget — scope it with `until`; and **green ≠ correct physics** — the runner checks
behaviour-matches-assertion, not that the assertion encodes the right physics, so keep a plain-language
`judge[]` item for first authoring.

**2. In-process `IDevHostControl`** — the building block the scenario runner sits on. Add a
`Vion.Dale.DevHost` reference to a test project and drive the wired network directly:

```csharp
await using var host = DevHostBuilder.Create()
    .WithDi<DependencyInjection>()
    .WithConfiguration(DevConfigurationBuilder.Create().WithTopologyName("thermostat").AddLogicBlock<Thermostat>("Thermostat").Build())
    .Build();
await host.StartAsync();

await host.Control.SetPropertyAsync("Thermostat", "TargetTemperature", 24.0);   // set an input

// Wait for an outcome — the live runtime has no synchronous time-step, so observe events:
var temperature = await host.Control.WaitForAsync(
    e => e is ServicePropertyChanged { Property: "CurrentTemperature" } sp ? sp.Value : null, TimeSpan.FromSeconds(5));

// Read state — service properties AND measuring points (the Status pill is a measuring point):
var status = host.Control.GetProperty("Thermostat", "Status");

// With multiple blocks, assert what a collaborator actually received (the wired analogue of TestKit's
// Verify*): host.Control.RecordedMessages("other-block"). Read the console: host.Control.RecentLogs().
```

(In real-clock mode the `[Timer(1)]` fires once a real second, so observe events with `WaitForAsync` rather
than expecting a synchronous step. For exact, instant stepping in a test, build with
`.WithDeterministicStepping()` and advance the virtual clock — the same mechanism `dale dev --stepped` uses.)

**3. Over HTTP (external tools / scripts).** While `dale dev` runs, the same `/api` the web UI uses serves
`GET /api/configuration`, `/api/logicblocks`, `/api/state/{logicBlock}[/{property}]`, `/api/logs/recent`,
`/api/messages?logicBlock={name}`, the scenario endpoints (`GET /api/scenarios`,
`POST /api/scenarios/{id}/apply`), and `POST /api/dale/property/...` to set values. Writing a read-only
member returns **400** (not a silent 200); `apply` recycles the host onto the scenario's topology with a
clean slate when needed (returns `{ "recycling": true }` — re-apply once it's back), and there is no `force`.
