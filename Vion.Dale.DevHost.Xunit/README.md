# Vion.Dale.DevHost.Xunit

xUnit.net v3 integration for [Vion Dale](https://github.com/vion-iot/dale-sdk) DevHost scenario
files (RFC 0006). It removes the boilerplate a consumer otherwise hand-rolls to run committed
`*.scenario.json` files as tests:

- **`[ScenarioFiles]`** — a theory data source that discovers every committed scenario and yields one
  `(string id, string topology)` row per file, display-named by title with `spec` traits. Each scenario
  becomes its own entry in Test Explorer / `--list-tests`.
- **`DevHostScenarioFixture`** — a fixture base that loads a topology file, optionally on a deterministic
  stepping clock, then builds and starts a host. The only thing you supply is your block catalog
  (`ConfigureDi`).
- **`RunScenarioAsync` / `ApplyScenarioAsync` / `AssertSucceeded`** — one-line run + assert over
  `ScenarioRunner`.

It is a separate package (not part of `Vion.Dale.DevHost`) so a non-test DevHost consumer is never
forced to take an xUnit dependency.

## Usage

```csharp
// One fixture names your block catalog — the only consumer-specific part of host construction.
public sealed class MyHostFixture : DevHostScenarioFixture
{
    protected override DevHostBuilder ConfigureDi(DevHostBuilder builder) =>
        builder.WithDi<MyLibrary.DependencyInjection>();
}

public class CommittedScenariosShould : IClassFixture<MyHostFixture>
{
    private readonly MyHostFixture _fixture;
    public CommittedScenariosShould(MyHostFixture fixture) => _fixture = fixture;

    [Theory]
    [ScenarioFiles]                       // one row per committed *.scenario.json
    public async Task RunsGreen(string id, string topology)
    {
        await using var host = await _fixture.LoadAsync(topology, stepped: true);
        (await host.RunScenarioAsync(id)).AssertSucceeded();
    }
}
```

For assertions the JSON scenario vocabulary can't express, use `ApplyScenarioAsync` as the
arrange/stimulate phase and then assert in C#:

```csharp
[Fact]
public async Task PeakShaving_LeavesConsumerServed()
{
    await using var host = await _fixture.LoadAsync("em-closed-loop", stepped: true);
    await host.ApplyScenarioAsync("peak-shaving");
    // ... arbitrary C# assertions on host.Control.GetProperty(...) ...
}
```

### Determinism

For reproducible, flake-free CI, build the host `stepped: true` (a `FakeTimeProvider` virtual clock) and
write scenarios using `advance` / `settle` / `waitUntil` rather than the wall-clock `wait`.

### Topologies

A scenario only runs against the topology it declares. `[ScenarioFiles]` carries each scenario's topology
as the second row value so the fixture can build the matching host. Keep a `ProjectReference` to the
library that declares your block types so the topology loader can resolve them.
