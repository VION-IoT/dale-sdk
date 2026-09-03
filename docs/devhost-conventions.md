# DevHost conventions

Read this before changing `Vion.Dale.DevHost`, `Vion.Dale.DevHost.Web`, the scenario runner, or
deterministic stepping.

The SPA's own contract — no-build ES modules, the file map, the draft/dirty pattern, the keybindings
rule, vendored deps — lives in
[`Vion.Dale.DevHost.Web/CLAUDE.md`](../Vion.Dale.DevHost.Web/CLAUDE.md) and is not repeated here. This
file covers the behaviour and the verification loop around it.

## 1. The verify loop: it is demonstrated, not asserted

The DevHost is the surface the user *looks at*. A change here is shown working before the PR opens.
Nine separate sessions asked for exactly that: *"i want to test it myself with devhost ui before the
pr is opened"*, *"continue into implementation, let me review before pr with devhost"*, *"the watch
property select is empty, the setup (set) property select looks good. do the smoke test with ui"*.

The order that works:

1. **`devhost-smoke` skill, Tier 1** — headless, in the normal CI pass. Boots real web hosts and sweeps
   the HTTP/runtime surface.
2. **`devhost-smoke` skill, Tier 2** — the live SPA against the project-referenced
   `Vion.Dale.DevHost.SmokeHost`, driven with chrome-devtools. Unit tests cannot execute the page's JS;
   nothing else covers it. A subagent can run this.
3. **A realistic library, not only the fixture.** The SmokeHost's synthetic blocks cover every value
   shape, but they are not a realistic topology. Before handing the change over, boot it against
   `examples/Vion.Examples.Energy` — *"better run it gains the energy examples (with project
   reference), there is more realistic logic blocks"*.
4. **Then offer it to the user to drive**, naming what to look at.

**Grow the fixture with the feature.** When you add a DevHost surface, extend the SmokeHost and the
skill in the same change, or the smoke stops meaning "the whole thing works".

## 2. Building against the working tree

Examples reference the SDK as published packages. To run one against the working tree, build with
`-p:DaleLocalSource=true`, which swaps the `PackageReference`s for `ProjectReference`s:

```bash
dotnet run -p:DaleLocalSource=true --project examples/Vion.Examples.Energy/Vion.Examples.Energy.DevHost
```

This exists because temporarily hand-editing project references was a recurring cost. Do not
reintroduce that: if an example you need lacks the switch, add it to that example's `.csproj` in the
same shape the others use rather than editing references in place.

**Coverage is currently uneven** — `Emission`, `Gating`, `ModbusRtu`, `ModbusTcp` and `ToggleLight`
carry it in three projects, `PingPong` in four, `Energy`, `Presentation` and `RichTypes` in two, and
`libraries/Vion.Diagnostics` and `templates/vion-iot-library` in none.

## 3. A mode that cannot work is rejected, or made host-adaptive — never endured

There is one virtual clock with two mutually exclusive drivers — manual stepping and the scenario
runner — and scenarios run either stepped or against the real clock. The manual path already enforces
this: `DevHostController.StepConflict()` returns `409` when the host is not stepped, and again while a
scenario is running, each with a machine-readable `reason`.

Hold the runner to the same standard. **A step kind that cannot behave correctly in the active mode is
either refused with a clear message or given an honest meaning in both modes.** Not silently
mis-timed, not "works but not as you'd expect".

**Today every one of the seven kinds takes the second route**, so the runner carries no clock-mode
refusal at all: `advance` / `settle` / `waitUntil` are host-adaptive and say which mode they ran in,
and the one kind that could not be made honest — `wait` — was removed instead. What each kind
guarantees per mode is [`specs/scenarios.md`](specs/scenarios.md)'s (`AC-SCEN-011.*`). The rule above
governs the *next* kind added: decide its behaviour in both modes, and if one of them cannot be right,
refuse it at resolve time where the message can name the offending step.

This rule was written after two sightings in one session:

- `advance` under the real clock produced an error mid-run: *"running the load-management scenario with
  the real clock (not stepped) leads to an error in the advance step. is it not supported? then in
  should not be possible to run."*
- `wait` under stepping "worked" while meaning something different from what an author would assume —
  *"that's not great, a real trap"*. It was removed rather than documented.

A scenario carries no stepped flag; the mode is implied by the steps it uses. So the check belongs in
the runner, at resolve time, where it can name the offending step.

**Also make the active mode visible.** The stepped clock being on when the reader expected wall-clock
has cost a debugging round on its own (*"ah, the stepped clock fooled me, I was not expecting that to
be the default"*). Label the clock, not just the toggle.

## 4. SPA changes reuse the affordance that already exists

The recurring correction here is not "this is wrong" but "we already have one of those":

- *"yes, use existing components or patterns where possible"*
- *"the new property selects are nice. use that style also for contract select and logic block select
  and wire to select in topology authoring"*
- *"on the autocomplete, can we use something closer to the Ctrl+K picker? it displays the options
  already, even with metadata liek logic block and group and current value"*

Before adding a picker, a list, a modal or a filter, name the existing one you are reusing — the ⌘K
palette, the property select, the scenario/topology master pages — or say why it does not fit.

## 5. Discoverability is a requirement, not a polish item

Every one of these was a review finding, not a suggestion:

- **Hover-only affordances are not discoverable.** *"the insert buttons are hard to discover (only on
  hover). inserting at the bottom (the 80% case) should be always visible"* — the common action is
  always visible.
- **A long list is sorted and filterable.** *"the dropdowns get very long and are not alphabetically
  sorted, how about an autocomplete picker?"* A dropdown that can hold 25 entries needs a bounded
  height and a filter, and its new/manage actions must stay reachable.
- **Nothing is truncated to unreadability.** Twice: *"the values are truncated in the trace rows"*
  (06-24) and *"scenario editor wastes space, the setup identifiers are truncated"* (07-15, which then
  had to be issued as its own brief). Give the identifier the width, or add the same overflow
  affordance used elsewhere.
- **An affordance looks like what it does.** *"the triangle make me expect it would open a drop down
  selector, but it doesnt'"*.
- **Long operations show progress.** Host recycle and topology switch are seconds long; show busy state.
- **Platform-correct modifier labels.** *"at least on windows, display Ctrl instead of the mac symbol,
  most users use windows"*. `MOD_KEY` / `PALETTE_KEY_LABEL` exist for this.
- **Adding an interactive affordance means adding a keybinding and a `KEYBINDINGS` row** — the SPA
  CLAUDE.md's rule, and the user asked for it to be first-class: *"encode the key-binding as first-class
  feature … so that when new features are added, key bindings come up"*.
- **Shortcuts appear in the tooltip of the button they drive.**
- **Sensible defaults over typing.** *"when adding, propose a default name"*.

## 6. Colours come from the tokens

`app.css` uses `var(--…)` for every colour — 520 references, and **zero** colour literals except four
`rgba(0, 0, 0, α)` shadows and scrims, which are not themed values. Tokens live in `tokens.css`.
(Count with `grep -o 'var(--' app.css | wc -l`; a regex expecting `var(--name)` misses the one token
that carries a fallback, `var(--font-mono, monospace)`.)

This is fully conformant today and cheap to keep that way: a literal colour in a rule is a finding, and
it was a real one — *"the black borders look off in dardk mode and hardcoded"*. Check both themes
before claiming a visual change is done.

## 7. Scenario step kinds have four definition sites

Adding or renaming a scenario step means updating four places that must agree, and nothing checks the
agreement for you:

1. the C# model and runner — `Vion.Dale.DevHost/Scenarios/` (`ScenarioFile`, `ScenarioResolver`,
   `ScenarioRunner`);
2. the CLI's validator — `Vion.Dale.Cli/Commands/ScenarioFileChecks.cs`;
3. the JSON schema — `Vion.Dale.DevHost/Scenarios/scenario.schema.json` (the CLI's copy is a linked
   resource and follows; per-project `.dale/scenario.schema.json` are regenerated from the host);
4. the SPA's step forms — `Vion.Dale.DevHost.Web/wwwroot/scenario-forms.js` (and the editor in
   `components.js`).

Miss one and the failure is asymmetric and quiet: a schema that autocompletes a step the runner
rejects, or a UI that offers one the CLI's validation refuses.

## 8. Applying a scenario recycles the host

`POST /api/scenarios/{id}/apply` is **recycle-on-run**: a scenario runs against the topology it
declares, from a clean slate, so every run is reproducible.

- When the host is on a different topology, **or** the stepped generation is *dirty* (its clock has
  advanced from baseline, or it has already run a scenario), the host is recycled onto the scenario's
  topology first and the response is `202 { recycling: true, topology }`. **The caller polls until the
  host is back and re-applies.** A clean, matching host runs in place.
- **One active run per host** — `409` while another is active, unless `?restart=true`, which cancels
  the first.
- **There is no `force`.** It was removed because running against the wrong topology or a dirty clock
  silently produced misleading results.

Every client of this API — the SPA, the smoke skill, anything an agent writes — must handle the
recycle round-trip rather than assuming the first call took effect. A test that applies once and
asserts is testing the recycle, not the scenario.

Separately, on the drive path: a write to a **read-only or unknown** member returns `400` carrying
`reason` and `property`, not a silent no-op.

## 9. A contract pairing is a declared wire, and the host never transforms

`docs/specs/scenarios.md`. A topology can declare that two **service-provider contract endpoints** are one wire:

```json
"contractPairings": [
  { "a": { "logicBlockName": "IoBlock", "contractIdentifier": "ActiveOutput" },
    "b": { "logicBlockName": "IdealIo", "contractIdentifier": "OutputChannel" } }
]
```

The generic stand-in then re-delivers each side's captured outbound as the other side's inbound —
one actor hop, at the JSON layer the scenario codec already speaks. **Nothing in the host decides
anything.** Every behaviour a bench needs (a confirmation, a device reaction, a controller answer)
lives in an ordinary simulator block bound to a *provider face*; how to write one is
[`simulator-authoring.md`](simulator-authoring.md).

**Where it is declared** — three equivalent places, one meaning:

- the topology file's `contractPairings` (above), keyed on **(block, contract binding)** — never on
  endpoint triples, because every contract already has an auto-created endpoint;
- `DevConfigurationBuilder.PairContracts(blockA, "ContractA", blockB, "ContractB")` for a C# fixture;
- the SPA topology editor's **contract pairings** section (add / remove), which writes the same array.

**Which directions carry a value is derived, not declared.** The declaration is symmetric; `a→b`
materialises only when a's handler declares an `Outbound` that is *the same struct type* as b's
declared `Inbound`, and `b→a` likewise. So a digital output paired with its provider face carries the
command one way and the confirmation the other, while a digital input paired with `IDigitalInputProvider`
carries one way only. The wiring panel draws exactly what materialised (`⇄` / `→` / `←`) — read the
arrow there rather than assuming the declaration is bidirectional.

**What is refused, and where:**

| Refused | Where it fires |
|---|---|
| an endpoint naming an undeclared block; both endpoints coinciding | topology parse (`DevTopologyFile.Parse`) |
| a contract the block does not bind; the same pair declared twice | topology build (`DevTopologyLoader.Build`) — host-independent, so the CLI and a unit test see it too |
| **no type-identical direction** — the pairing can carry nothing | the running host: at boot, *and* at editor Save / `POST /api/topologies/validate`, which the host hands an introspection-backed handler resolver. The message names **both** declared wire types, because the diagnosis is "wrong type", not a field diff. |
| an endpoint whose handler declares no `[ScenarioWire]`, or is not loaded | same place, with the two cases worded apart (fix the handler vs. reference the library) |

The strict posture is deliberate: a *declared* pairing that can carry nothing is an authoring mistake
and must be loud. The scenario drive gate's stand-down is a different situation — absence there means
"not drivable", an expected state.

**Two invariants worth not breaking.** The forward happens in `Capture` **after** the output cache is
written, so `serviceProviderExpect` still reads the command a paired output wrote. And the *drive*
path must never consult the pairing table: a forward that re-entered it would let stand-ins originate
messages, and a closed loop would converge on stand-in recursion instead of on block cadence (
§4.7). Because every hop is a plain actor message, a paired loop is visible to the quiescence barrier —
a closed-loop bench runs **stepped and deterministic**.

The fixtures to read and to extend: `Vion.Dale.DevHost.SmokeHost/topologies/paired.topology.json`
(two blocks, two pairings, one of them one-way) and
`scenarios/paired-loop.scenario.json` (the whole loop end to end, stepped).

**Round-trip rule for the editor:** a topology without pairings must save byte-identically, so the
key is absent — not an empty array — when there are none. The client drops it with the last pairing
and the server normalises an empty list to none; both halves are pinned by tests.
