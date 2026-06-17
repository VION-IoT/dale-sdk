---
description: Smoke-test the DevHost end-to-end (headless/HTTP surface) — run after changing DevHost / DevHost.Web / the scenario runner / deterministic stepping, or to verify the DevHost is healthy
---

Smoke-test the **assembled, running** DevHost — boot a real web host (Kestrel + the full API/SignalR pipeline + a wired logic-block network) and sweep the major paths over HTTP. This catches wiring/integration regressions the per-class unit tests don't.

1. From the repo root, run:

   ```bash
   dotnet test Vion.Dale.DevHost.Test/Vion.Dale.DevHost.Test.csproj --filter "TestCategory=Smoke" --nologo
   ```

   Expect **3 tests passed** (~2 s execution; ~10 s wall-clock the first time, for the incremental build). That green means the DevHost boots, serves, steps, and runs scenarios.

2. **What a green run proves.** 3 `Smoke`-tagged tests — the first is the core HTTP sweep (`DevHostSmokeShould`, whose internal assertions cover boot/introspection, state read, writable set + read-back, **read-only write rejected with 400** (not a silent 200), `stepped:true`, a scenario run to `succeeded`, and manual virtual-clock advance firing a `[Timer]`); the other two are the supervised lifecycle (`RecycleOnRunShould` = recycle-on-run onto a clean slate + re-run; `RunControlShould.SupervisedRunner_…` = topology switch + host reset on the same port).

3. **Scope — what it does NOT prove.** This is the **headless / HTTP+runtime** surface only. The no-build SPA web UI is *served* but never *driven* (no browser request, no page JS), so a green smoke is **not** proof the UI works (the Run button, SignalR→DOM, etc.). To cover that, do the live UI pass (step 5).

4. **If it fails**, triage — don't rerun-and-hope. Read the failed assertion and, for a scenario failure, the printed run-report JSON: `validationErrors` (name paths are case-sensitive — `counter` ≠ `Counter`) and each step's `detail` pinpoint it. Narrow with `--filter "FullyQualifiedName~DevHostSmoke"` (core) vs `~RecycleOnRun` / `~SupervisedRunner` (lifecycle). For per-feature depth, drop the filter and run the whole `Vion.Dale.DevHost.Test` project.

5. **Optional — live UI pass** (completes "the whole DevHost"; needs a browser/agent, not headless CI): boot a real consumer host against local source (temp project-ref harness — see `Vion.Dale.DevHost.Web/CLAUDE.md` "Verify loop") with `DALE_DEVHOST_STEPPED=1 DALE_DEVHOST_NO_BROWSER=1`, then drive `localhost:5000` with the chrome-devtools MCP: the page loads, **Run** on a scenario goes green, `+1s`/`+10s` advance the clock. Revert the csproj switch when done (never commit it).

The `Smoke` tests also run in the normal `dotnet test` CI pass, so this doubles as the regression gate for the read-only-write rejection, recycle-on-run, and no-`force` behaviors.
