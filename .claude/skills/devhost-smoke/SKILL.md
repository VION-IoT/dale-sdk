---
name: devhost-smoke
description: Smoke-test the Vion Dale DevHost end-to-end. Tier 1 (headless, CI) covers the HTTP/runtime surface; Tier 2 (live, chrome-devtools) drives the no-build SPA UI on a real server. Use after changing anything under Vion.Dale.DevHost / Vion.Dale.DevHost.Web / the scenario runner / deterministic stepping, or when asked to verify the DevHost is healthy. A subagent can run Tier 2 after DevHost work.
---

# DevHost smoke test

Two tiers. Tier 1 is the fast headless gate (run it always); Tier 2 drives the actual browser UI (run it when changes touch the web UI / SignalR, or for full confidence). Together they cover the whole DevHost.

## Tier 1 — headless / HTTP+runtime (fast, CI)

```bash
dotnet test Vion.Dale.DevHost.Test/Vion.Dale.DevHost.Test.csproj --filter "TestCategory=Smoke" --nologo
```

Expect **8 tests passed** — ~4 s of test execution, but **~25 s cold wall**: `dotnet test` rebuilds and runs an up-to-date check across the dependency graph, and that build tax (not the tests) dominates. For repeat runs in the same session, build once then append **`--no-build`** → ~5 s. This boots real web hosts (Kestrel + the full API/SignalR pipeline + a wired logic-block network) and sweeps the assembled surface over HTTP:

- **core sweep** (`DevHostSmokeShould`): boot/introspection (`/api/configuration`, `/api/logicblocks`), state read, **writable set + read-back**, **read-only write rejected with 400** (not a silent 200), **`stepped:true`**, a **scenario run to `succeeded`**, **manual virtual-clock advance** firing a `[Timer]`, a **HAL-input scenario** (`serviceProviderSet` + `waitUntil` → the mocked input reaches the block), and a **HAL-output scenario** (`advance` fires the `IoBlock`'s timer to mirror inputs onto its outputs, then `serviceProviderExpect` ASSERTs those outputs — the full mocked-input → block → mocked-output loop) that also boots the SmokeHost's `IoBlock`;
- **struct contract** (`ServiceProviderStructContractShould`): `serviceProviderSet` drives a third-party-shaped value contract whose payload is a multi-field struct with a 1-level nested struct + an enum into `GridBlock`, which surfaces every field (incl. the nested ones) — the RFC 0010 / DF-27 struct unblock, the non-HAL case end to end;
- **lifecycle + step types**: recycle-on-run (`RecycleOnRunShould`), explicit topology switch (`TopologyFilesShould`), host reset/recycle (`RunControlShould`), and the deterministic `waitUntil` path (`ScenarioSteppingShould`).

Between them the smoke exercises every important scenario step type (`set` / `advance` / `settle` / `expect` / `waitUntil` / `serviceProviderSet` / `serviceProviderExpect`; only the discouraged non-deterministic `wait` is omitted).

It does **not** drive the SPA web UI (the host *serves* it but the headless test never loads the page or runs its JS). A green Tier 1 is not proof the UI works — that's Tier 2.

**If it fails:** read the failed assertion and, for a scenario failure, the printed run-report JSON — `validationErrors` (name paths are case-sensitive: `counter` ≠ `Counter`) and each step's `detail` pinpoint it. Narrow with `--filter "FullyQualifiedName~DevHostSmoke"` (core) vs `~RecycleOnRun` / `~SupervisedRunner` (lifecycle). For per-feature depth, drop the filter and run the whole `Vion.Dale.DevHost.Test` project.

## Tier 2 — live UI on the SmokeHost (chrome-devtools)

`Vion.Dale.DevHost.SmokeHost` is a committed, project-referencing fixture (synthetic blocks covering every DevHost feature: value shapes, presentation groups, read-only members, a timer, HAL contracts, inter-block wiring; two topologies; two scenarios). It boots a **real** DevHost server against local source — **no temp project-ref switching**. A subagent can run this after DevHost work.

1. **Boot it** (stepped + headless), with the working directory set to the project dir (folder-driven discovery is cwd-relative):

   ```powershell
   $dir = "$(git rev-parse --show-toplevel)\Vion.Dale.DevHost.SmokeHost"
   dotnet build $dir --nologo
   $env:DALE_DEVHOST_STEPPED = "1"; $env:DALE_DEVHOST_NO_BROWSER = "1"
   Start-Process dotnet -ArgumentList "$dir\bin\Debug\net10.0\Vion.Dale.DevHost.SmokeHost.dll" -WorkingDirectory $dir -WindowStyle Hidden
   ```

   Poll `http://localhost:5000/api/control/status` until it responds (`stepped:true`). (Boot-to-ready is ~1 s — the ~50 s is the `dotnet build`. If the solution is already built, e.g. you just ran Tier 1 or `dotnet build`, skip `dotnet build $dir` and boot the existing DLL.)

2. **Drive the UI** with the chrome-devtools MCP. If no page is connected, open one (`new_page`); if you genuinely can't drive a browser, fall back to Tier 1 + API checks against `localhost:5000` and say so. Navigate to `http://localhost:5000` and reload with `ignoreCache` (dodges stale assets). A leftover route or pins from a previous session are harmless — reload to the root, or use an isolated context / clear localStorage for a clean view. Then verify:
   - The page loads and the Explorer lists the four blocks (Showcase, IO Device, Signal Source, Signal Sink).
   - Step the clock first (`+10s`) so the live metrics populate, then confirm the value shapes render on `ShowcaseBlock` — status pill (enum), struct, **sparkline** (array; empty until stepped), duration, a bounded number field (writable double, min/max — `uiHint=slider` is advisory, the control is a number field, not a range slider), bool toggle. Also expand a member's docs and confirm the RFC 0004 chips render: `Sollwert` shows **throttle 1s · Δ0.1** + **persistent**, and `Cycles` shows **immediate**.
   - Pin a member in a block (the pin control) → it appears in the watch panel; unpin → it's removed.
   - Open the **`showcase-tour`** scenario in the Player and click **Run** → green (stepped, so near-instant). Open **`io-control`** and Run → green (it drives the digital + analog HAL inputs, the block observes them, and after an `advance` it asserts the mirrored digital + analog HAL outputs — the full input → block → output loop). Moving between scenarios is the Player's scenario switching.
   - Switch topology from **`⛁ topology`** to `minimal` → the host recycles and the header shows the new topology. (Equivalently, open **`minimal-subset`** and Run → recycle-on-run onto `minimal` through the Player.)
   - **Author a topology (RFC 0013 editor):** ⌘K → **new topology** (or the topology panel's **+ new**) → add `SignalSourceBlock` (name `src`) + `SignalSinkBlock` (name `sink`) → **⚡ AutoConnect** wires `src.ISignalSource → sink.ISignalSink` and the residue clears → set id `rig` → **validate** (green) → **save & switch** → the host recycles onto `rig` and the chip shows it. Confirms the catalog endpoint (`/api/logic-block-definitions`), the client matching/AutoConnect/residue, and the save→validate→switch round-trip. Tear down: delete the authored `Vion.Dale.DevHost.SmokeHost/topologies/rig.topology.json`.

3. **Tear down**: stop the process —
   ```powershell
   Get-NetTCPConnection -LocalPort 5000 -State Listen -EA SilentlyContinue | ForEach-Object { Stop-Process -Id $_.OwningProcess -Force -EA SilentlyContinue }
   ```

## CI

The Tier-1 `Smoke` tests run in the normal `dotnet test` pass, so this is also the regression gate for the read-only-write rejection, recycle-on-run, and no-`force` behaviors. The SmokeHost is built by the solution (a compile guard); its live behavior is Tier 2. A pipeline can run just the fast headless smoke with the `--filter "TestCategory=Smoke"` above.
