# VionIotLibraryTemplate

A Vion IoT Library template for creating LogicBlocks on the Vion Dale SDK.

## Getting started

1. **Run the DevHost** (the local dev runtime + web UI):
   - From the CLI: `dale dev` — opens `http://localhost:5000`
   - Or in the IDE: set `VionIotLibraryTemplate.DevHost` as the startup project and press `F5`
     - **Visual Studio:** right-click the project → **Set as Startup Project**
     - **Rider:** pick it from the run-configuration dropdown (top-right)

2. **Watch the worked example.** The library ships one block, `Thermostat` — a self-contained
   control loop (setpoint → heat/cool → temperature) with no hardware. Open it in the UI and watch
   `CurrentTemperature` track `TargetTemperature` while the Status pill changes colour.

3. **Add your own blocks:**
   - Implement them in the `VionIotLibraryTemplate` project.
   - Register them in `DependencyInjection.cs`.
   - The DevHost boots **folder-driven** (RFC 0008, topology-as-data): it discovers the instance graph
     from `topologies/*.topology.json` and replayable checks from `scenarios/*.scenario.json`. Add a new
     block instance to `topologies/default.topology.json` (or delete that file and let the DevHost
     regenerate it from your registered blocks on the next boot). You don't edit `Program.cs`.

## Verifying — scenarios

`scenarios/thermostat.scenario.json` drives the example end to end: it stages a cold start, asks for
24 °C, steps the virtual clock, and asserts the room heats and the energy meter ticks. Scenarios are the
committed, replayable artifact — the same file the Player, CI, and agents all run.

```bash
dale dev --stepped                 # boot a deterministic virtual clock (scenarios step exactly — no waiting)
dale scenario run thermostat       # run the scenario and print the report
dale scenario validate             # resolve every name path + topology against the wired host — offline; the CI gate
```

Author your own the same way: declare a minimal topology, stage starting state in `setup`, then drive +
assert with `advance` / `expect` / `settle`. See [AGENTS.md](AGENTS.md) for the authoring loop, the SDK
attribute reference, and the full `dale` command list.
