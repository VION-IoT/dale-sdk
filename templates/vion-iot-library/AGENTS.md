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
and open it. `scenarios/thermostat.scenario.json` drives it end to end (RFC 0006) — run it with
`dale scenario run thermostat` while `dale dev` is up, or open it in the Player.

## CLI

Use the `dale` CLI instead of raw dotnet commands:

```bash
dale build                # build the solution
dale test                 # run tests
dale dev                  # start the DevHost with web UI at localhost:5000
dale list                 # show logic blocks, contracts, properties, interfaces
dale list --verbose       # show full detail
dale pack                 # create .nupkg for deployment

dale scenario validate           # check scenarios/*.scenario.json against the wired host (RFC 0006)
dale scenario run <id>           # drive a scenario against `dale dev` and report the result
dale scenario scaffold <id>      # graduate a scenario into a typed C# test

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
{ProjectName}.DevHost/          # In-memory dev runtime with web UI
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

## Verifying headlessly (for CI & agents)

The DevHost boots the *real* wired network (unlike TestKit, which stubs every collaborator), so it catches
bugs that only appear in the wired, real-messaging path (e.g. a block that never actually sends the request a
unit test hand-feeds it). Three ways to drive it without a browser, in order of what to reach for:

**1. Scenario files (RFC 0006) — the committed, replayable artifact.** A `scenarios/*.scenario.json` file
describes setup → ordered stimuli → a watch list → human judgments, consumed identically by the Player, by
CI, and by agents (see `scenarios/thermostat.scenario.json`):

```bash
dale scenario validate            # resolve every name path + topology against the wired host — OFFLINE; the PR gate
dale scenario run thermostat      # execute it against `dale dev --headless` and print the Player's report
```

An agent stages a human's verification view simply by committing a scenario and citing its **id** in the PR.
For in-CI *execution* (beyond `validate`), hand-write a one-line xunit theory over
`ScenarioRunner.RunAsync(id, host.Control, scenariosDir)` — the generic `[ScenarioFiles]` attribute is not
shipped yet.

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

(This is wall-clock — the `[Timer(1)]` fires once a real second — so observe events with `WaitForAsync`
rather than expecting a synchronous step.)

**3. Over HTTP (external tools / scripts).** While `dale dev` runs, the same `/api` the web UI uses serves
`GET /api/configuration`, `/api/logicblocks`, `/api/state/{logicBlock}[/{property}]`, `/api/logs/recent`,
`/api/messages?logicBlock={name}`, the scenario endpoints (`GET /api/scenarios`,
`POST /api/scenarios/{id}/apply`), and `POST /api/dale/property/...` to set values.
