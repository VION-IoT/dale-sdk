# RFC 0008 — Stepped DevHost host enabler (Phase 2: deterministic Player + CLI/agent runs)

Status: **Design approved** (jonas.bertsch, 2026-06-17). Implements RFC 0008
([0008-unified-scenario-topology.md](0008-unified-scenario-topology.md)) §6.1 / §9 Phase 2 for the
**web/headless** runtime. Branch: `feat/deterministic-stepping-exact`.

## Why

Phase 1 made scenarios run deterministically **in-process** — the stepper (DI-injected `TimeProvider`
+ engine-owned `IVirtualSchedule` + the exact quiescence barrier; `advance` / `settle` / `expect` /
`until`). But the stepper only engages when a **controllable clock** is registered, which today only
tests and the consumer's `HeadlessHost` do by hand. The web/headless DevHost (`DevHostWebRunner`)
always boots on `TimeProvider.System`, so Player and CLI/agent scenario runs are wall-clock.

This enabler brings determinism to the two web-fronted runtimes:

- **Player** — run a scenario *stepped* (the deterministic why-loop), instant and exact.
- **CLI / agent (HTTP)** — `dale scenario run` against a `dale dev --stepped` host runs deterministically.

The CI **regression gate** (the consumer's in-process `CommittedScenariosShould`) is a *separate*,
post-publish, consumer-side adoption that reuses Part 1's API — **not built here** (the consumer
references a published SDK; it adopts after release and flips its `kind=headless-integration` trait).

## Decisions

- **Clock source: depend on `Microsoft.Extensions.TimeProvider.Testing`** in `Vion.Dale.DevHost` and
  register a `FakeTimeProvider`. Stepping is now a first-class DevHost feature; the canonical
  controllable clock beats reimplementing one. This reverses the spike-era "keep the test-only
  assembly out" note in `DeterministicStepper` — but the stepper's **structural detection** (reflection
  on `Advance(TimeSpan)` + `GetUtcNow()`) stays exactly as is, so nothing else changes.
- **Boot-time mode, not a runtime toggle.** `--stepped` is chosen at launch. The free-run **pump** +
  runtime stepped/free-run **toggle** (live feel while stepped) are deferred ("Part 4").
- **No new HTTP step endpoints.** Scenario runs already execute server-side against the in-process
  control, so a stepped-booted host makes them deterministic. Interactive manual `advance`/`settle`
  over HTTP belongs to the deferred pump/toggle work.

## Parts (each a verified, committed increment; Part 1 → 2 → 3)

### Part 1 — `DevHostBuilder.WithDeterministicStepping()`

- Add the `Microsoft.Extensions.TimeProvider.Testing` `PackageReference` to `Vion.Dale.DevHost`.
- New builder method `WithDeterministicStepping(DateTimeOffset? startUtc = null)` — registers
  `new FakeTimeProvider(startUtc ?? DeterministicEpoch)` as the `TimeProvider` singleton through the
  existing `ConfigureServices` seam (`AddDaleSdk` uses `TryAddSingleton(TimeProvider.System)`, so an
  explicit registration wins). `DeterministicEpoch` = `2026-01-01T00:00:00Z` (matches the stepping
  tests' `NewClock`).
- Effect: `control.IsStepped == true`; the existing stepper drives the `FakeTimeProvider`.
- Refactor: dale-sdk's own stepping tests replace their hand-wired
  `ConfigureServices(s => s.AddSingleton<TimeProvider>(new FakeTimeProvider(...)))` with the one call
  (proves the API + DRYs the fixtures).
- Tests: in-process unit test — a host built with `WithDeterministicStepping()` reports `IsStepped`,
  and a scenario with `advance` / `settle` runs deterministically and near-instantly.

### Part 2 — `dale dev --stepped` (web/headless stepped boot)

- CLI: add a `--stepped` option to `dale dev` (`DevCommand`) → sets `DALE_DEVHOST_STEPPED=1` for the
  spawned process, alongside the existing `DALE_DEVHOST_NO_BROWSER` plumbing.
- DevHost.Web: `DevHostWebRunner` reads `DALE_DEVHOST_STEPPED` (new public const `SteppedEnvVar`);
  when set, applies `WithDeterministicStepping()` to the builder in the factory path (both the
  `RunAsync(Func<…>)` supervised loop and `RunFolderDrivenAsync`'s `configure`/`Factory`).
- Effect: server-side scenario runs (Player + `dale scenario run`) are deterministic. No new HTTP
  endpoints.
- Caveat (expected): in stepped mode the clock is frozen *between* runs (timers idle) — live watching
  uses default mode; the "live feel" pump is the deferred Part 4.
- Tests: a headless web-host test — boot stepped, run a committed scenario over the control surface,
  assert deterministic success + virtual-time in the report.

### Part 3 — Player surfaces stepped mode

- API: expose `IsStepped` on the control status (`GET /api/control/status` in `DevHostController`).
- UI (`wwwroot`, no-build ES modules): the store reads the flag; the Player shows a **"stepped /
  deterministic" badge** near the run controls and renders **virtual-time** (already in the run-report
  step detail) on the timeline.
- Tests: extend the web-contract test for the new status field; **manual Player verification** via
  temporary project references + chrome-devtools MCP (the `Vion.Dale.DevHost.Web/CLAUDE.md` verify loop) —
  the project-reference switch is **not committed**.

## Out of scope (deferred)

- Free-run **pump** + runtime stepped/free-run **toggle** ("Part 4").
- Interactive manual `advance`/`settle` over HTTP (step buttons).
- Consumer-side gate move (post-publish: consumer's `HeadlessHost` adopts `WithDeterministicStepping()`,
  flips the `kind=headless-integration` trait, retires `headless-nightly.yml`).

## Discipline

Each part ends green (`dotnet test Vion.Dale.Sdk.sln`) + `pwsh scripts/cleanup-code.ps1 -Verify`
clean, with a conventional commit. The no-build DevHost UI is excluded from the style gate.
