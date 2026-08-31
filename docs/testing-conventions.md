# Testing conventions

Read this before writing or changing a test.

## 1. Which framework — it depends which side of the package boundary you are on

| Where | Framework | Count today |
| --- | --- | --- |
| SDK-internal test projects (`Vion.Dale.*.Test`) | **MSTest** | 16 (all of them) |
| Consumer-facing — `examples/*.Test`, `libraries/*.Test`, `templates/*.Test` | **xunit.v3** | 10 |

Both are present in the solution on purpose: the second group models what a library author writes, and
the shipped `Vion.Dale.DevHost.Xunit` integration exists to serve them. A new SDK test project written
in xunit, or a new example test written in MSTest, is a finding — match the side you are on.

`Vion.Dale.DevHost.Xunit` is not a test project; it is the xunit integration package and references
`xunit.v3.extensibility.core`. Its own test project, `Vion.Dale.DevHost.Xunit.Test`, is MSTest, because
it tests SDK code.

Most SDK test projects reference the `MSTest` meta-package; `Vion.Dale.Cli.Test` references
`MSTest.TestAdapter` + `MSTest.TestFramework` separately. Both are MSTest — grep for either form when
counting.

## 2. A test pins behaviour, not the process that produced it

- **Assert state, not hop counts.** Timing and message interleaving are not part of the contract at
  runtime; a test that counts convergence hops encodes an implementation detail and breaks when the
  scheduler changes. This was the guidance given to the first consumer adopting stepping: *"They should
  assert the state, not the process details, right? At runtime timing is not deterministic, so their
  test should not rely on it"*.
- **A "no-throw" assertion is not a test.** Asserting that driving a contract does not throw passes
  when the message is silently dropped. Assert the **consuming block received it** — a positive
  round-trip, not the absence of an exception.
- **Assert what did not move, too.** A rejection test that only proves an exception was raised does not
  prove the write was refused; assert the value is unchanged.
- **A fix has a test proven red.** Revert the fix, watch the new test fail, restore. Say so in the PR
  description — there is no cheap gate for this, so the claim is the record.
- **Prefer parameterised rows over near-duplicate methods** — `[DataRow]` on the MSTest side (19 files
  use it), `[Theory]` + `[InlineData]` on the xunit side.

## 3. Analyzer tests must reproduce the real compilation

Analyzer unit tests build a small compilation from stub sources. That compilation is **not** what a
consumer's build looks like, and the difference has already shipped a dead analyzer:

- Contract interfaces emitted by `LogicClassGenerator` are **absent** from the compilation analyzers
  see in a real (Metalama-hosted) build. A stub interface that resolves cleanly in a test does not
  reproduce this. Where an analyzer keys off contract interfaces, include a test whose contract
  interface is genuinely unresolved — the test expects `CS0246` alongside the Dale diagnostic. Details
  and the required by-symbol-**and**-by-name resolution:
  [`sdk-surface-conventions.md`](sdk-surface-conventions.md) § 5.
- **An analyzer that is referenced is not necessarily running.** Proving it used to mean breaking a real
  declaration by hand and remembering to revert. The standing form is a committed probe: an invalid
  declaration excluded from the ordinary build, compiled by a test that shells out to `dotnet build` and
  requires the diagnostic to fail it (`AnalyzerWiringShould`, `Vion.Dale.Sdk.DigitalIo/AnalyzerWiring/`).
- **Test rules in combination, not only in isolation.** The supported-type gate is spread across
  `DALE003`, `DALE016` and `DALE008`; each one's tests passed while a newly supported value type was
  still rejected by a sibling rule, and it shipped broken.

## 4. Introspection is verified from the packed artifact

The introspection JSON that cloud-api reads is produced by `Vion.Dale.LogicBlockParser`, which
[`Vion.Dale.Sdk.targets`](../Vion.Dale.Sdk/build/Vion.Dale.Sdk.targets) runs
`BeforeTargets="IncludePublishedFilesInPack"` — that is, on **`dotnet pack`**.

- **`dotnet publish` does not regenerate it.** A `publish` leaves the previous `tools/publish/*.json`
  in place, so a stale file reads exactly like a migration that did not work.
- **`tools/publish/*.json` is gitignored build output** (`.gitignore` ignores `publish/`). There is no
  checked-in copy to inspect or regenerate.
- So: to verify what a consumer will actually receive, `dotnet pack` the example and read the JSON out
  of the produced package — *"check the introspection result … by looking at the json of the packed
  energy examples nugpk"*. Derive the expectations from the sources and check them off against the
  packed file, rather than reading the file and describing it.
- In-repo, pin the emitted shape with **golden assertions** at introspection level
  (`ContractCarriedServiceRelationsShould`, `Vion.Dale.DevHost.Test/Golden/`) so a wire change cannot
  land silently.

## 5. Determinism belongs to the clock mode, and the mode is checked

Scenarios run under either a stepped virtual clock or the real clock. A step that cannot work in the
active mode is **rejected**, not tolerated — see [`devhost-conventions.md`](devhost-conventions.md) § 3
for the rule and its history. When you add a step kind, decide its behaviour in both modes and make the
unsupported combination an error the runner reports.

- Under stepping, `advance` / `waitUntil` must return only once every message due in the advanced
  window has been processed. Sampling before quiescence is what made `ScenarioSteppingShould` flake in
  the parallel CI suite while passing 10/10 locally: the DevHost's `InFlight` bracketing covered the
  handler but not the whole mailbox run.
- **A flake that does not reproduce locally is still a real bug.** Windows timer coarseness hid the
  above entirely; the fix was verified by CI, not by a local loop. Do not close a flake as
  unreproducible.
- **Examples used in scenarios must be deterministic.** `Vion.Examples.Energy`'s `OpenMeteoService`
  calls `https://api.open-meteo.com/v1/forecast` live, and `PhotovoltaicsSimulation` consumes it
  through `WeatherDataService` — so that path cannot carry a scenario assertion. Assert around it, or
  parameterise the block. This is a known limit of that example, not a pattern to copy.

## 6. The DevHost smoke is part of the test suite, not an extra

After any change under `Vion.Dale.DevHost`, `Vion.Dale.DevHost.Web`, the scenario runner or stepping,
run the **`devhost-smoke` skill** and grow its fixture with the feature you added. Tier 1 is headless
and runs in the normal CI pass (`dotnet test Vion.Dale.DevHost.Test --filter "TestCategory=Smoke"`);
Tier 2 drives the real SPA. Details: [`devhost-conventions.md`](devhost-conventions.md) § 1.

## 7. Reach for the seam, not for reflection

Reading private fields out of a type to assert on them is a signal the type has no test seam. It is a
finding on new tests: *"i see reflection used in unit tests to get _serviceBinder etc, is there no
better way currently to validate the it?"* — either assert through the public introspection result, or
add the seam.

Two sites do it today and are the non-conforming precedent, not the pattern:
`Vion.Dale.Sdk.Test/Configuration/ConfigTimeStructuralGatingShould.cs:441` and
`Vion.Dale.Sdk.TestKit.Test/EmissionPolicyShould.cs:233`, both reaching
`LogicBlockBase._serviceBinder` by `BindingFlags.NonPublic`.

## 8. The machine baseline

```bash
dotnet build Vion.Dale.Sdk.sln
dotnet test Vion.Dale.Sdk.sln
pwsh scripts/cleanup-code.ps1 -Changed
```

Do not spend review effort on anything these three would catch.

A transient `CSC error LAMA0601: … Insufficient system resources` after many host boots is a process
leak, not a test failure: `dotnet build-server shutdown`, kill stray `dotnet` / `VBCSCompiler` /
`MSBuild`, retry.
