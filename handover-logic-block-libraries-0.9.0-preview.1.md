# Handover prompt — logic-block-libraries adoption of Vion.Dale SDK v0.9.0-preview.1

> Uncommitted scratch file. Paste the prompt below into a fresh session in the
> in-house **logic-block-libraries** repo once **v0.9.0-preview.1** is published.
> Source: dale-sdk PR #81 (RFC 0008), branch `feat/deterministic-stepping-exact`.

---

**Adopt Vion.Dale SDK v0.9.0-preview.1 — RFC 0008 (deterministic DevHost stepping, scenarios, topology-as-data)**

You're in the in-house **logic-block-libraries** repo, which consumes the Vion.Dale SDK as NuGet packages. A new preview, **v0.9.0-preview.1**, ships RFC 0008. The SDK change is `VION-IoT/dale-sdk` PR #81; the scenario-authoring cookbook is that repo's `docs/rfcs/0008-unified-scenario-topology.md` §11. Adopt it:

**1. Upgrade.** Bump every `Vion.Dale.*` `PackageReference` to `0.9.0-preview.1` (Sdk, DevHost, DevHost.Web, ProtoActor, Plugin, IO, TestKits — whatever you reference). Restore + build + run your full gate (`build`, `test`, `scenario-validate`).

**2. Breaking changes — find and fix:**
- **`force` / `?force=true` on scenario apply is removed.** Search for any script/CI/agent that calls `POST /api/scenarios/{id}/apply?force=true` and drop `force`. Apply is now **recycle-on-run**: on a topology mismatch *or* a dirty stepped host it returns `{ "recycling": true }` and recycles the host onto the scenario's declared topology with a clean slate; poll `/api/control/status` until it's back, then re-apply (a clean, matching host runs in place immediately). The Player handles this automatically; scripts/agents must handle the `recycling` response.
- **Writing a read-only / unknown service property now returns HTTP 400** (was a silent 200). Fix any tooling that wrote measuring points or private-setter properties and ignored the result.

**3. Adopt deterministic scenarios (the win).** Run the DevHost stepped (`dale dev --stepped` / `DALE_DEVHOST_STEPPED=1`): your scenarios' `advance` / `settle` / `waitUntil` run in **virtual time** — instant and bit-reproducible, no wall-clock flake. New vocabulary you can use: `expect` auto-assertions (above/below/equals+tolerance/notEquals/oneOf + relational `{path}` comparands), `settle` with scoped `until`, `digitalInput`/`analogInput` HAL steps. Your existing `scenario-validate` gate + `scenarios/` dir + DevHost still work — this enriches them. Read the cookbook (§11) for the authoring workflow + footguns (dedicated topology, pin non-deterministic inputs, stage stateful blocks, keep a `judge[]` for the "green ≠ correct physics" trap).

**4. CI gate flip (the payoff).** Scenarios now run deterministically + instantly on the PR gate, so you can **retire the nightly wall-clock headless lane** (`headless-nightly.yml` cron + the `kind=headless-integration` trait filter) and move scenario *execution* onto the fast deterministic PR gate. Keep `scenario-validate`.

**5. Consider a smoke.** dale-sdk ships a `devhost-smoke` skill (Tier 1: `dotnet test --filter TestCategory=Smoke`; Tier 2: a live chrome-devtools UI pass on a committed project-ref fixture). Consider an equivalent for your DevHost — it catches assembled-system regressions unit tests miss.

**Report back:** what broke on the bump (especially `force` / read-only-write usages), whether the nightly→deterministic gate flip is feasible, and any preview issues to feed back to dale-sdk PR #81.

---

## Where things stand (dale-sdk side)

- **dale-sdk:** PR #81 open and CI-ready. After review/merge, tag **`v0.9.0-preview.1`** → CI publishes to the private feed + nuget.org; then `scripts/set-version.ps1 -Scope references` bumps template/example refs.
- **Consumer-facing discoverability** (cookbook → public docs, `dale new` sample scenario, CLI help) remains the post-release deliverable — worth doing alongside the preview so the handover above has docs to point at.
