# Agent Instructions

This is a Dale LogicBlock library. LogicBlocks are actor-based IoT computation
units built on the Dale SDK by Vion-IoT.

## CLI

Use the `dale` CLI instead of raw dotnet commands:

```bash
dale build                # build the solution
dale test                 # run tests
dale dev                  # start the DevHost with web UI at localhost:5000
dale list                 # show logic blocks, contracts, properties, interfaces
dale list --verbose       # show full detail
dale pack                 # create .nupkg for deployment

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

## Headless control (for CI & agents)

The DevHost boots the *real* wired multi-block network (unlike TestKit, which stubs every collaborator), and
it can be driven and observed without the browser — to catch bugs that only appear in the wired,
real-messaging path (e.g. a block that never actually sends the request a unit test hand-feeds it).

**In-process (integration tests — the recommended way).** Add a `Vion.Dale.DevHost` reference to a test
project and drive the network through `IDevHost.Control` (`IDevHostControl`):

```csharp
await using var host = DevHostBuilder.Create()
    .WithDi<DependencyInjection>()
    .WithConfiguration(DevConfigurationBuilder.Create().AddLogicBlock<MyBlock>("my").AutoConnect().Build())
    .Build();
await host.StartAsync();

await host.Control.SetPropertyAsync("my", "SomeKnob", 16.0);          // set an input

// Read state — service properties AND measuring points (for asserting calculations):
var metric = host.Control.GetProperty("my", "SomeMetric");

// Wait for a future change (condition-based; the live runtime has no synchronous time-step):
var v = await host.Control.WaitForAsync(
    e => e is ServicePropertyChanged { Property: "SomeMetric" } sp ? sp.Value : null, TimeSpan.FromSeconds(5));

// Assert what another block actually received (the multi-block analogue of TestKit's Verify*):
var msgs = host.Control.RecordedMessages("other-block");

// Read the console programmatically:
foreach (var line in host.Control.RecentLogs()) TestContext.WriteLine(line.ToString());
```

**Over HTTP (external tools / scripts).** While `dale dev` runs, the same `/api` the web UI uses also serves
read endpoints: `GET /api/logicblocks`, `GET /api/state/{logicBlock}[/{property}]`, `GET /api/logs/recent`,
`GET /api/messages?logicBlock={name}`, alongside the existing `POST /api/dale/property/...` to set values.
