# Testing conventions

Read this before writing or changing a test. §1–8 are this repo's specific traps; §9–17 are the
authoring discipline (adapted from mesh's testing conventions), applied in full whenever a test is
written or rewritten — area passes ([`spec-process.md`](spec-process.md)) bring whole suites to it.

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
  requires the diagnostic to fail it (`AnalyzerWiringShould` +
  `Vion.Dale.Sdk.Generators.Test/AnalyzerWiring/`, linked into the project under test — never a source file
  of a shipped project).
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
`LogicBlockBase._serviceBinder` by `BindingFlags.NonPublic`. Their area's pass owes them seams.

The same rule in its other costume: **no test-only accessors** — never add a member to the SUT
whose sole purpose is letting tests inspect internal state. It widens the published surface for
non-production reasons (§ D1 territory) and couples tests to the current representation. If neither
the introspection result, an event, a return value, nor a collaborator can observe the behavior,
stop and surface the design issue rather than carving a hole in the SUT.

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

## 9. Coverage — every observable behavior, not every line

The discriminator is **observability**: a behavior is something a caller, collaborator, subscriber,
or the introspection/emission pipeline can detect — a distinct return value, a guard that changes
the outcome, conditional construction, a raised event, a side effect on a collaborator, a resilience
loop. A branch that changes nothing observable (one that only picks a different log message) is not
a behavior and needs no test. Walk the SUT **statement by statement, not branch by branch** — a
linear method calling four collaborators in order is doing four things, each independently
deletable. The commonly missed cases, all observable, all worth a test:

- **A sequence of side effects is a sequence of behaviors** — ask of each call whether deleting it
  changes something a collaborator can see.
- **Verify the constructed object, not just that the call happened.** `Verify(c => c.Send(It.IsAny<Foo>()))`
  proves a call occurred, not that the fields were populated — content assertion per §15.
- **Exception branches that change the outcome** (different return, skipped side effect,
  swallowed-vs-propagated) — but don't fabricate exotic failures for a `catch` that only logs.
- **Both sides of a conditional construction** — a provided test asserting the value is used *and*
  a not-provided test asserting the fallback.
- **Loop / multi-subscriber resilience** — register a throwing subscriber before a recording one
  and assert the recording one still ran.
- **A side effect that survives a branch** — when the SUT branches and then acts unconditionally,
  a row on *each* side of the branch pins that the effect stays unconditional.

Don't chase line or branch percentages, and don't assert negatives the language already guarantees
(exception unwinding reaching the next statement). A deliberate *decision* not to catch earns an
explicit throws-test exactly when "just catch and ignore it" is a plausible future fix.

## 10. Enumerate the behaviors before writing the tests

Read the SUT and produce a table of its observable behaviors and the test that will cover each —
**in the response, before any test code**:

| Behavior | Test |
|---|---|
| Not mapped → drive refused, value unchanged | `RefuseDriveWhenUnmapped` |
| Response without a matching request → ignored | **none — unobservable, say why** |

The table is the reviewable artifact: a reader disagrees with a row in seconds; finding the same
gap by reading fifty test bodies takes an hour. **Rows with no test are the point** — list them
with the reason (unobservable, covered elsewhere) rather than omitting them. Derive rows from the
SUT and the spec page, never from the tests already written. Produce it whenever the request is
about *coverage* (a new `…Should` class, "write tests for this", a rewrite); skip it when the
request names the behavior ("add a test for the timeout path").

This is the same artifact an area pass's extraction step produces
([`spec-process.md`](spec-process.md)) — there each row additionally carries `file:line` evidence
and, on Tier A pages, its AC id.

## 11. A test must be able to fail — the vacuous-test catalogue

If deleting the Arrange leaves the assertion passing, the test asserts what the Act alone produces.
The recurring shapes:

- **Idempotency / cache hit**: capture the first result and assert the second equals *it* — not a
  literal a fresh call also returns.
- **Reset / clear**: make the pre-state observably different from the post-state first, or the
  assertion reads the same on an untouched SUT.
- **In-place mutation**: when the SUT populates an object and hands it to a collaborator, assert
  through what the collaborator *received* (Callback capture), not through the test's own reference —
  the direct assert passes with the handoff deleted.
- **The assertion is whatever would actually fail.** Where the outcome is only observable as "it
  reached a terminal state", the awaited signal *is* the assert — put it under `// Assert` and do
  not follow it with a `Verify` the await already guarantees.

**Prove every new behavioral test red** (§2 states it for fixes): revert the fix or delete the
branch, watch it fail, restore — and **name the mutation in the PR** ("made the replay conditional →
`PublishAllStatesToLateSubscriber` fails"). A red-proof belongs to the assertion it ran against;
re-prove after rewriting it. This repo has paid for the alternative twice in one month: a
late-subscriber test green against the pre-fix code because the drive's own event satisfied the
waiter, and an `AdvanceTime` regression row that was a slower copy of the flush row — both exposed
only by the revert.

**Settle step-versus-field with the mutation.** Fields are the arguments of one call; steps are
calls in a sequence. Mutate once per candidate assertion: assertions that redden under *different*
mutations are different behaviors and belong in different tests; assertions that can only redden
together are one behavior in one test.

## 12. Naming

Form a sentence with the class name — `[Sut]Should` + `[ExpectedResult][Condition]`:
`DeliverWriteIssuedFromStopping`, `ThrowWhenPayloadEmpty`, `RefuseDriveWhenUnmapped`.

- **Name the behavior, not the collaborator** — `ReturnStoredTopology` ✓, `ReturnTopologyFromRepositoryProvider` ✗.
  Exception: routing SUTs, where the destination *is* the behavior.
- **Name the condition, not the exception type** — `ThrowWhenPortInUse` ✓; the throws-assert pins
  the type.
- **No specifics that churn** — no counts (`ListAllSevenFields` ✗), no format details, no magic
  numbers; the assertion owns the specifics.
- **Outcome, not mechanism** — `PublishLatestValue` ✓, `CollapseDuplicateKeys` ✗.
- **No articles or filler** — drop `The`, `A`, `Is`: `ThrowWhenPayloadEmpty`, never
  `ThrowWhenThePayloadIsEmpty`.
- **Numeric suffixes for same-role peers** — `_matchingHandler1Mock`, `serviceIdentifier2` — never
  position or novelty words (`first…`, `other…`), which don't scale past two.
- **`expected`/`actual` are prefixes on a meaningful base noun** (`expectedPayload`), used only when
  both sides of a comparison are variables; a result asserted against a literal gets a plain name.

## 13. Structure — Triple-A and its discipline

Every test carries `// Arrange`, `// Act`, `// Assert` — always, even when Arrange is empty
(both frameworks). Discipline:

- `// Act` holds only the interaction with the SUT. A conversion applied solely for comparability
  (`.ToString()`, `.ToList()`) belongs in `// Assert`; an awaited settlement of the SUT's async work
  belongs in `// Act`. When Act and Assert can't separate, `// Act / Assert` with the framework's
  throws-assert (MSTest `Assert.Throws*`, xunit `Assert.Throws`) — never `[ExpectedException]`,
  never `try/catch`, in Arrange included (consume an arranged failure with the throws-assert).
- **Declare locals next to their use**, ordered by when each value is needed — not front-loaded by
  category.
- **Inline expected values in the assertion.** Bind an `expected…` local only when a second reader
  needs it, something before the assert consumes it, or an `It.Is` predicate would bury it (§15).
- **Helpers earn their keep**: extract for duplication across multiple call sites or genuinely
  gnarly setup — never a single-use helper, never a wrapper around a one-line `Setup`/`Verify`, and
  the call to the SUT always stays inline in the test's own `// Act`. When in doubt, inline.

### File layout and ordering

One test file per SUT class, named and classed `[Sut]Should`, its path mirroring the source path
(`Vion.Dale.Sdk/Emission/Throttler.cs` → `Vion.Dale.Sdk.Test/Emission/ThrottlerShould.cs`). Order
tests to mirror the SUT's execution flow so a reviewer can scan SUT and tests top-down in parallel:
member order across public members; within a method, its branch order (guards first, then the main
path); for a linear method, happy path first, then variations. Ordering is organizational, not a
coverage lever. Shared helpers (builders, fakes used by several test classes) live in a
`TestHelpers/` folder at the test project root; single-class helpers stay private to the class.

### Setup

Initialise mocks and the SUT inline at field declaration when possible (`_sut`, `_loggerMock`);
otherwise `[TestInitialize]` (xunit: the constructor) with `_sut = null!` declarations. Only truly
common setup goes there — anything that varies belongs in the test's Arrange. Prefer uniform
construction over per-test SUT variation: vary the test's *inputs*, not the constructor args. For a
SUT needing a baseline state (an established connection), arrange it in setup and
`_mock.Invocations.Clear()` so tests observe only what they triggered. Default to **no**
`[TestCleanup]`/`Dispose` — add one only to fix a failure you can name.

### Parameterised rows

`[DataRow]` / `[InlineData]` rows are **value variations of one condition** — the method name stays
the assertion, the rows are instances. Rows must share one Arrange shape: a parameter selects
values, never structure — an `if` on the parameter means the rows are different scenarios, so
split them. Passthrough/transformation tests take **at least two distinct value sets** (one fixed
input passes spuriously if the SUT hardcodes it). Add `DisplayName` (xunit: explicit rows) when the
raw values don't identify the row. Object-typed rows use `[DynamicData]` (xunit:
`[MemberData]`) backed by a static member. Don't lump unrelated scenarios into one parameterised
test, and collapse a with-X/without-X pair into one row pair rather than a separate `Omit…` test.

## 14. Test data

- **Don't supply values that aren't asserted on.** Drop the parameter where you control the surface;
  pass `null`/`default` where you don't. `new InvalidOperationException()` unless a test reads the
  message.
- **`Guid.NewGuid().ToString()` for strings the SUT requires but the test doesn't care about** — it
  signals "any value works" where `"localhost"` makes a reader hunt for significance. Literal
  carve-outs only for attribute rows (compile-time constants), asserted values, and numerics.
- **`CancellationToken.None` when the token is plumbing** — verify with `It.IsAny<CancellationToken>()`;
  a real token only where the SUT's own logic branches on it.
- **Never fabricate calendar literals.** `DateTime.UtcNow` captured once into a field — or, wherever
  the SUT touches time at all, prefer the TestKit's stepped clock (§5 owns the clock rules).
- **Class level only when shared** by multiple tests; single-use values live in the test's Arrange.

## 15. Moq

- `new Mock<X>()` then `.Object` — not `Mock.Of<X>()`. Mock loggers; never assert on log calls
  (log text is not a contract; a log may serve as a synchronisation signal only where no other
  terminal-state observation exists).
- **Exact call counts** — `Times.Once` / `Times.Never` / `Times.Exactly(N)`; `AtLeastOnce` only when
  that genuinely is the contract.
- **Skip the `Verify` that a `Setup(...).Returns(...)` + returned-value assertion already proves** —
  wrong args would have returned `default` and failed the assert.
- **Verifying what the SUT handed a collaborator**, in order: the exact expected instance (value
  equality); an `It.Is<…>` predicate when one comparison settles it (comparand as an `expected…`
  local above the `Verify`; predicate inline, no `bool` helper); a `Callback` capture + field asserts
  when several independent fields matter or the argument is stale/overwritten by `Verify` time —
  then `Assert.IsNotNull(captured)` carries "the call happened", field asserts follow, and a
  `Times.Once` `Verify` closes with cardinality. Capture only the qualifying argument; never mirror
  whole invocations into a recorded log — ordering is almost always already pinned by the arguments.
- **No reflexive negatives**: `VerifyNoOtherCalls()` and `Times.Never` earn their place only when
  they alone pin a branch — and on background paths, never without a synchronisation point (§16).

## 16. Async

Prefer the stepped clock (§5, TestKit virtual time) — these rules govern the genuinely concurrent
remainder:

- `TaskCompletionSource` + `WaitAsync(timeout)` to coordinate; **never** `Thread.Sleep` /
  `Task.Delay` as synchronisation. Class-level timeout field; setup helpers return the wait handle
  with the timeout baked in.
- **Park a collaborator to freeze the SUT mid-operation**: have the mock signal entry, then return a
  never-completing task — overflow, collision and shutdown-mid-work cases become deterministic. To
  make something happen *during* a call, do it in the mock's `Callback` instead.
- **Never depend on beating a configured window** (batch, debounce, poll): park the SUT so the
  inputs are queued before the window opens. Shrinking the interval hides the race, it doesn't
  remove it.
- **A negative without a synchronisation point is vacuous** — anchor `Times.Never` on a real signal
  the SUT emits under the same conditions, or don't write it.
- No `Interlocked`/`lock` for ordinary test state — awaiting the SUT serializes its work onto the
  test's flow; reach for synchronisation only when the test itself starts concurrent work.

## 17. Citing spec ids

On a Tier A area ([`spec-process.md`](spec-process.md)), a test that proves an acceptance criterion
carries the id as a quoted literal — MSTest `[TestProperty("spec", "AC-EMIT-001.1")]`, xunit
`[Trait("spec", "AC-EMIT-001.1")]`, a committed scenario's `"specs": ["AC-SCEN-003.1"]` — which is
what `scripts/spec-trace.ps1` counts; a comment or method-name mention does not bind. The gate
matches any quoted `"AC-…"` literal, so an id belongs in exactly those three forms and in no other
string — an assert message carrying one would bind by accident. An id proven by **both** a unit
test and a scenario states which half each tier owns in the test class summary (a "Cross-tier"
clause; `spec-trace` warn-notes files missing it).
