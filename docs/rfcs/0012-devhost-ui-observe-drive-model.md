# RFC 0012: DevHost UI — an observe/drive model (Explore · Verify · one clock)

Status: **Accepted** (model approved 2026-06-23; phased implementation — see Roadmap).
Author: jonas.bertsch. Date: 2026-06-23.

> **Purpose of this document.** The DevHost web UI ("Explorer", `Vion.Dale.DevHost.Web/wwwroot`)
> grew a four-way view toggle and a global clock cluster *after* its design mockups were drawn and
> *while* deterministic stepping (RFC 0008) was being bolted on. The result reads awkwardly: manual
> clock-stepping and running a scenario look like the same thing, a finished run's trace is thrown
> away, the header is an undivided strip of controls, and switching topology is a silent multi-second
> freeze. This RFC **rethinks the UI's information-architecture** around what a human (or agent)
> actually does at the DevHost — *observe and drive a wired network* — and specifies the redesign that
> realizes it. It is the design contract that the implementation plan is generated from.

This RFC supersedes the **wall-clock framing** of the `docs/devhost-ui/mockups/*` set (drawn 2026-06-11,
pre-stepping) and takes RFC 0008 (deterministic stepping) as the behavioural contract for the clock. It
builds on, and does not contradict, the mockups' **layout/density** grammar (identifiers-first,
value-as-control, pinned-baseline, the three-register guard taxonomy) and RFC 0006 (scenario files /
the Player).

---

## 1. Motivation

### 1.1 The reframe: the UI is older than the model it now runs

The mockups (`docs/devhost-ui/mockups/01–06`) describe a **wall-clock** Explorer + Player. Deterministic
stepping (RFC 0008 / `0008-stepped-host-enabler.md`) landed days later — *one virtual clock with two
drivers* (the manual stepper and the scenario runner) — and was surfaced as an extra header chip that was
never mocked. **All five reported pain points live in that seam.**

### 1.2 The five pain points (observed on a live `dale dev --stepped` host over the real corpus)

1. **Stepping vs running read the same — and it's worse than "the same."** Manual stepping is a global
   header cluster (`⏱ stepped · ↦ · +1s · +10s`); running a scenario is a button inside the Player.
   Both drive *one* virtual clock via *one* stepper, coordinated only by a server-side 409 surfaced as a
   transient toast. Worse: manually advancing **dirties the generation** (`DevHostControl.HasAdvancedFromBaseline`),
   which forces a **host recycle** on the next scenario run — and skips the scenario's `setup` (median 3,
   max 12 staging entries) entirely. The two affordances are not just look-alikes; one silently sabotages
   the other.
2. **Trace viewing is the lowest-hanging fruit in the whole pass.** A run produces a `watchTrace` — a
   clean `[{phase, stepIndex, virtualElapsedMs, values:{signal→value}}]` time series, deterministic on
   virtual time (`Vion.Dale.DevHost/Scenarios/ScenarioRunReport.cs`, `WatchSample`). The backend serves
   it; **the SPA references it nowhere.** The Player shows only the *final* sample as static tiles — the
   progression (`Inactive→Applied`, `null→2.863`) is discarded. It "flashes by" not because it is large
   but because a stepped run finishes in ~0.5 s wall-clock and **nothing pins it for after-the-fact
   review.**
3. **The header is one undivided strip of ~13 controls.** `store.view` is `explorer | topology | gallery
   | player`; the three view buttons reuse the *same* CSS class as `⏸ pause`, `↻ reset`, and the theme
   toggle (`components.js`, `setView`). Navigation, clock-control, and status are peers in one row; the
   default Explorer view has **no button** (you return to it by toggling the active one off). Pause/resume
   only makes sense on a real clock; step/advance only when stepped — both sit in the same strip.
4. **Topology switch is a silent full recycle.** `store.switchTopology` sets `connected=false` and waits
   for the reconnect poll. The engine does a full teardown→rebuild (fixed 250 ms port-release floor +
   actor-system restart) and **emits no progress event a browser can observe**
   (`Vion.Dale.DevHost.Web/DevHostWebRunner.cs`). Small topologies swap fast; `all-surfaces` (13 blocks)
   on a cold host is the multi-second freeze with zero feedback.
5. **Observe-vs-author — already most of the way there.** The Player shows the scenario file read-only;
   pins/baseline/sparklines are observe features; scenarios are *richly* self-documenting (every scenario
   has a title + narrative description, every step a prose `label`, every run a `judge[]` checklist with
   `spec` ids). With authoring now an agent's job (topology + scenario JSON are agent-edited), the human
   UI's leverage is **surfacing that narrative against the trace**, not adding editors.

### 1.3 The corpus reality (sizes the design)

Measured across 15 topologies + 36 scenarios in `logic-block-libraries`:

| dimension | min | median | max |
|---|---|---|---|
| watch[] signals / scenario | 2 | **4** | 6 |
| steps / scenario | 0 | **5** | 18 |
| setup entries / scenario | 0 | 3 | 12 |
| judge[] items / scenario | 1 | 1 | 4 |
| blocks / topology | 1 | 7 | 13 |

A typical trace is **~5 steps × 4 signals (~20 cells)**, worst case 18×4 (~72). Watched value types:
~69 % numeric, ~16 % boolean/enum, ~9 % whole struct/array. Sign carries meaning
(`AllocatedActivePowerKw` = `+charge / −discharge`; grid = `+import / −export`).

### 1.4 What the codebase already gives us (so this is mostly presentation work)

- **The backend is ahead of the UI on the trace.** `watchTrace` + per-step `ScenarioStepResult`
  (`Index, Kind, Label, Spec, Target, Argument, Status, ElapsedMs, VirtualElapsedMs, Detail`) are served,
  deterministic, and display-ready. The engine's `OnProgress` per-transition hook exists (wired to the
  run registry) though not bridged to SignalR.
- **The two clock drivers are genuinely separate primitives** (`AdvanceToNextEventAsync` = step one hop;
  `AdvanceAsync` = advance N seconds; the runner drives both itself). Making them *look* different is
  well-supported.
- **The design system is ready.** The `Sparkline` SVG primitive is already ported (`components.js`); the
  dashboard's throttled range-slider is the model for a scrubber; the status families
  (ottoman/amber/sepia/jordy/jagged-ice in `tokens.css`) cover every run/step/judge state — **no new
  colors, no charting dependency, no build step.**

---

## 2. The model: one subject, two activities, one clock

A human at the DevHost is doing exactly one of **two activities** with the **same** wired network, over
the **same** virtual clock. The redesign collapses today's four sibling "views" onto that axis:

- **Explore** (today's `explorer`) — *poke inputs by hand and observe live state.* Block rail + values +
  watch panel + baseline diff. **Manual clock-stepping belongs here** — stepping the clock yourself *is*
  exploration.
- **Verify** (today's `player`) — *run a committed scenario and review the result.* Scenario list → run →
  the **trace viewer**. The runner owns the clock here.

Everything the two activities share moves *out* of the tab strip into a persistent **shell**:

- **Topology** stops being a "view" and becomes **context** — a shell chip showing the loaded topology,
  with switch (and recycle progress) behind it, plus a slide-over for the blocks/links/contracts detail.
- **The one virtual clock** is a persistent shell readout (`t=…` + host-mode badge) whose *controls* are
  **context-aware** (§4).
- **Gallery** demotes to a secondary/utility entry — it is a library-author presentation-QA aid ("authoring
  gaps on N of M"), not an observe path.

**Editing is an extension point, not a workspace.** Authoring surfaces (a future scenario edit mode, an
Explore→scenario capture/promote action, a topology clone-and-tweak) attach to the artifact they edit as
**opt-in, bounded, gateable sub-modes** — never peers of Explore/Verify, never the default state of one
(§7). This is *why* the model demotes topology to context and keeps Explore/Verify as *activities*: editors
slot in without competing with the observe/drive default, and a deployment can switch them off entirely
(`DALE_DEVHOST_READONLY_SCENARIOS`).

```
┌─ shell ─────────────────────────────────────────────────────────────────┐
│  DALE DevHost   [ Explore | Verify ]        topology ▾ · t=00:00:00 · ●  │
└──────────────────────────────────────────────────────────────────────────┘
   Explore  → block rail · live values · watch panel · baseline ·           
              manual clock (step / +Ns when stepped; pause/resume on real)  
   Verify   → scenario list · Run all / Step · TRACE VIEWER · judge          
              (the runner owns & locks the clock during a run)              
   context → topology switch + recycle progress + blocks/links slide-over   
   utility → gallery (presentation-authoring QA)                            
```

---

## 3. Information architecture & header (PP3)

**Principle: the global shell holds only navigation + shared context; activity-specific controls move
into their workspace's toolbar.** This is what de-clutters the strip — most of today's header controls are
actually Explore-specific.

**Global shell (always visible):**
- **Brand** (left).
- **Primary nav = top tabs**: an `Explore | Verify` segmented control with a real active state (built fresh
  from the existing `.editor-tabs` underline vocabulary; there is no segmented-control component in either
  repo). Explore is the default and has its own selectable affordance — it is no longer "the absence of the
  other three." Top tabs are the right pattern for exactly two primary activities, and they keep Explore's
  left column free for its block rail (rather than double-railing it with a vertical activity bar).
- **Jump = the `⌘K` palette, promoted** (`store.paletteOpen`, already present): the navigation *accelerant*
  — jump straight to a scenario, block, or topology by name. This is the fast path that fits agent-authored
  files (you know the name; you type it), and the reason the spine can stay a lean two-tab bar.
- **Topology context chip**: current topology name; click → switch list + recycle progress (§6) and the
  blocks/links/contracts slide-over. **Gallery** and any future utilities live in an overflow/utility menu
  off the shell, not as a primary tab.
- **Clock readout**: `t=<virtual time>` + a host-mode badge (`stepped` / `real`); its controls are
  context-aware (§4).
- **Connection** dot + **theme** toggle.

**Workspace toolbars (local to the activity):**
- **Explore**: the block/property **filter**, the **baseline** control + changed-counters, the
  blocks/props **counts**, and the **manual clock controls** (step / +Ns, or pause/resume on a real clock).
- **Verify**: a **scenario filter** (over the 36-scenario list), and — once a scenario is open — its
  **run controls** (`Run all` / `Step`), the **scrubber**, and the result status.

`reset` (recycle on the same topology) and `switch` (recycle onto a different topology) are siblings —
both host-lifecycle actions — and live together in the **topology context** surface, not the global strip.

---

## 4. The clock model (PP1)

One virtual clock, two drivers, made structurally distinct:

- **Manual bare-clock stepping is Explore-only.** On a stepped host, Explore exposes `step` (advance to
  the next scheduled event — `AdvanceToNextEventAsync`) and `+1s / +10s` (`AdvanceAsync`). On a real-clock
  host, Explore instead exposes `pause / resume` (the wall-clock freeze). The shell never shows bare-clock
  step controls while Verify is the active workspace.
- **Scenario running is Verify-only, with `Run all` and `Step` co-located** (realizing mockup
  `04-scenario-player.html`). Here **`Step` steps the *scenario*** — runs setup-if-needed then advances to
  the next acknowledged step — *not* the bare clock (resolved sub-decision §8a). So "step through this
  scenario" finally does what users expect: it stages `setup` first.
- **The runner owns the clock during a run — visibly.** While a Verify run is active, the shell clock
  reads `running · t=… (owned)` and Explore's manual controls render **disabled with a reason**, not live.
  The mutual exclusion (today a server 409 → surprise toast) becomes a *shown state*. This realizes the
  stepped-host RFC's "controls disable while a run is active."
- **The cross-effect is surfaced, not silent.** Because manual advancing dirties the generation
  (`HasAdvancedFromBaseline`) and forces a recycle-on-run, Explore's clock controls carry a quiet hint
  that hand-advancing will cause the next scenario run to recycle for a clean slate — turning a hidden gotcha
  into expected behaviour.
- **Clock mode (`stepped ⇄ real`) is switchable at runtime, via a recycle.** Today the mode is fixed at
  boot (`DALE_DEVHOST_STEPPED`, set by `dale dev --stepped`). A `stepped ⇄ real` toggle in the shell clock
  cluster — available in **both** Explore and Verify (in Verify: "run this scenario deterministically vs
  live", per RFC 0008) — rebuilds the generation in the other mode. A reload/recycle is acceptable and is
  exactly the mechanism: it rides the existing host-recycle machinery (§6) and surfaces the same host-busy
  progress.

**Backend (surgical):**
- Add **structured reason codes** to the three semantically-distinct 409s (today prose-only, forcing the
  SPA to string-match): `notStepped` / `runDrivingClock` / `runAlreadyActive`, so the UI renders the right
  disabled/locked state deterministically. No behaviour change.
- Make the host builder accept the **clock mode per generation** and add a small `POST /api/control/clock-mode
  { stepped: bool }` that parks the mode and triggers a recycle (sibling to topology-switch / reset). This is
  a touch more than the other surgical adds, but reuses the recycle machinery wholesale.

---

## 5. The trace viewer (PP2) — the Verify centerpiece

The single highest-value deliverable. It turns the discarded `watchTrace` + per-step report into a
followable, scrubbable artifact, realizing mockup 04's center column.

**Data.** `watchTrace` is a `WatchSample[]`: `{ phase ("start"|"steps"), stepIndex (-1 for start, then 0..n),
virtualElapsedMs (deterministic; null on a real clock), values: { namePath → value } }`. One sample after
`setup`, one after each step (pass or fail). Display-ready; observation-only (never an assertion target).

**Layout — a timeline ribbon over time-aligned signal lanes (form C).** The form emphasizes the *temporal*
nature of the data: step durations are spatial, so an instant `set` is a tick while a 4-second `waitUntil`
is visibly wide.

- **One shared X-axis = continuous virtual time** (`virtualElapsedMs`). Instant (zero-duration) steps get a
  **minimum segment width** so they stay labelable/clickable. On a real-clock host (null virtual time) fall
  back to per-step `ElapsedMs`, else the sample index.
- **A step ribbon on top:** one labeled segment per step, **width ∝ virtual duration**, colored by `Status`
  (green ok / red failed / grey skipped), captioned with the step's prose `label` — *this is the annotated
  axis* (the old flashing step list becomes the ruler). Phase-grouped where labels carry "PHASE N"; per-step
  ✓/✗ + the rich `Detail` ("satisfied after 8 hops / 4.2 virtual s") on hover/expand.
- **Signal lanes beneath, time-aligned to the same axis** — three renderers (the corpus needs exactly
  these):
  1. **Numeric** (~69 %): a **sign-aware, zero-baseline** line drawn as an **honest stairstep** (value held
     until the next sample, then a jump — never an interpolated curve we don't have). Reuses the existing
     `Sparkline` primitive fed `watchTrace` samples; a dashed reference line where a step asserts a limit
     (mockup 04).
  2. **Boolean / enum** (~16 %): a **discrete state band** (`Inactive → Applied` as colored segments).
  3. **Struct / array** (~9 %): an **expander** (e.g. 3-phase `{L1,L2,L3}`) — respecting declared struct
     field order (the known cloud-jsonb reordering hazard; the DevHost renders order correctly).
- **A draggable playhead scrubs the time axis** (reusing the dashboard's throttled local-mirror + flush
  slider pattern, matching the existing draft+dirty convention). Landing snapshots every lane and the value
  tiles to that moment — the "follow it after the fact / scrub the trace" requirement, independent of the
  0.5 s run duration.
- **The judge checklist sits alongside the trace** so a human ticks "did it converge without oscillation?"
  *while scrubbing the very signal that answers it* (judge ↔ trace co-location).

*Aesthetic:* render this in the **deterministic-instrument signature** (§13) — scope-style channels + an
optional reference/ghost overlay — treated as soft direction, not a hard requirement.

**Constraints honoured.** Reuses `Sparkline` + the status color families; **no new tokens, no charting
dependency, no build step**; sizes are tiny (≤72 cells).

**Honest limit.** `watchTrace` samples only at **step boundaries** — a 60 s `advance` is one point; the
engine computes intra-step hop motion but discards it (only the `Detail` string survives). v1 is
step-granular and that is sufficient — the stairstep rendering is truthful about it. The time-axis form
makes the missing resolution *visible* (rather than hiding it behind evenly-spaced step columns), so the
deferred per-hop sampling becomes a clean future upgrade: the lines simply get denser, no UI change. True
per-hop scrubbing would require that engine change to emit per-hop samples — **deferred** (§9).

---

## 6. Host-busy / recycle progress (PP4)

A recycle is a **full host teardown → rebuild** — fixed 250 ms port-release floor + actor-system restart
(scales with topology size, same cost as a cold boot minus process start). It **cannot be materially sped
up**; the fix is *affirmative, determinate progress*. **One shared host-busy affordance serves every
host-lifecycle action** — topology switch, reset, and the clock-mode toggle (§4) are all recycles and all
flip the same state.

- **Client-optimistic state.** We always know we triggered the action, so flip the topology context chip to
  `recycling…` immediately, disable the rest of the workspace, and keep it until the new generation answers.
  No backend dependency for the basic affordance.
- **Backend (surgical): a generation counter on `/api/control/status`.** Today it returns
  `{ paused, canReset, stepped, virtualTimeUtc }`; the supervisor already tracks a `generation` (it is in
  the headless readiness line). Surfacing it lets the SPA show *deterministic* `recycling… (gen N→N+1)` and
  know precisely when the fresh generation is up (rather than inferring from a reconnect blip). No behaviour
  change.
- `GET /api/topologies` already exposes `canSwitch` + `current`, so the control disables up front on an
  unsupervised host.
- **The only other long action is a wall-clock scenario run** — that gets a run-in-progress state in
  Verify's run controls. (Live `OnProgress`→SignalR streaming stays deferred per §9, since stepped runs are
  instant.)

---

## 7. Observe-lean & editing extension points (PP5)

Both workspaces are observe/drive by default; the scenario file stays read-only; the leverage is surfacing
the agent-authored narrative (titles, step labels, judge items, spec ids) against the trace. Editing, *if
and when* added, attaches as an opt-in bounded sub-mode and is gateable off
(`DALE_DEVHOST_READONLY_SCENARIOS=1`, already honoured by `ScenarioStore`):

- **Scenario — capture/promote, in Explore.** "Save as scenario" / record promotes the current setup +
  pinned signals into a draft `*.scenario.json` that opens in Verify (mockup 02 "save as scenario", mockup
  06 "Explorer promotion / recorder"). Authoring-by-doing — the observe-native path.
- **Scenario — direct edit, in Verify.** A `view ⇄ edit` toggle on the existing read-only `{ } scenario
  file` panel, schema-driven (the committed `scenario.schema.json`), writing via the existing
  `PUT /api/scenarios/{id}` (`ScenarioStore.Save`), round-tripping back to a run. A mode you switch the
  open scenario into, not a third tab.
- **Topology — clone & tweak, in the topology slide-over.** "Duplicate this topology, add/rewire a block,
  save as a new file" → appears in the switcher. Lowest priority; topologies are agent- or
  `dale dev --export-topology`–generated.
- **Gallery** stays as the library-author presentation-QA utility.

---

## 8. Resolved sub-decisions

- **(a) Verify "Step" steps the *scenario*, not the bare clock.** It runs `setup` if not yet applied, then
  advances to the next acknowledged step — distinct from Explore's bare-clock `step`. This matches the user
  expectation that triggered PP1 ("advance" should step *through the scenario*) and mockup 04's co-located
  `Run all` / `Step`.
- **(b) Topology detail is a slide-over from the context chip**, not a panel embedded in Explore. Keeps the
  blocks/links/contracts/topology-files surface available from anywhere without spending a primary-nav slot
  on a mostly-static view.

---

## 9. Scope

**In scope (this RFC, phased — §11):** the Explore/Verify IA + two-zone shell; the context-aware clock +
co-located Verify `Run all`/`Step` + visible run-lock; the **runtime `stepped ⇄ real` clock-mode toggle**
(via recycle); the trace viewer (form C); a shared **host-busy/recycle progress** affordance; the
observe-lean tidy + editing *extension points* (hooks, not full editors); the deterministic-instrument
**signature** as soft aesthetic direction (§13, non-binding). Surgical backend additions: generation counter
on `/api/control/status`; structured 409 reason codes; per-generation clock mode +
`POST /api/control/clock-mode`.

**Out of scope / deferred:**
- **Per-hop `watchTrace` resolution** (an engine change to sub-sample within `advance`/`settle` steps) —
  the trace stays step-granular for v1.
- **Bridging `OnProgress` → SignalR** for live run-following — runs are ~0.5 s on a stepped host, so
  post-hoc scrub matters more than live streaming; revisit for real-clock "live feel" runs.
- **Full in-UI scenario/topology editors and the recorder** — only the *extension points* (§7) are
  designed here.
- **A graph/diagram rendering of the topology** beyond the existing list/links slide-over.

---

## 10. Constraints (no-build & design system)

- **No-build discipline holds** (`Vion.Dale.DevHost.Web/CLAUDE.md`): browser-native ES modules over the
  vendored Vue full build; components are plain objects with `template:` strings; flat filenames; no
  bundler/npm/TS/`.vue`. New UI is hand-formatted JS/CSS (wwwroot is excluded from the C# style gate).
- **Reuse, don't add deps:** the `Sparkline` SVG primitive (`components.js`), the status color families,
  the `.editor-tabs` underline (segmented control), the `.group-section` chevron disclosure, the
  `.watch-tile` + sparkline + delta vocabulary. A scrubber is a styled `<input type=range step=1>`.
- **Token edits go in the `app.css` local alias shim**, never the 144-token base in `tokens.css` (a
  hand-synced flatten of the dashboard's generated `tokens.scss`; any base-token change is a manual
  two-repo sync). Optional, only if the trace viewer needs them: a type scale, a shadow token, a motion/
  keyframe token — added to the shim.
- **Rendering policy lives in `format.js`**, view wiring in `components.js`; trace formatting (lane kind
  selection, sign-aware coloring, value/temporal formatting) belongs in `format.js` so it is unit-testable
  by reading.

---

## 11. Prioritized roadmap

1. **Trace viewer in Verify** (form C, scope aesthetic + optional ghost overlay §13) — highest value, pure
   SPA, data already served. Step ribbon over time-aligned signal lanes + playhead scrub + judge co-location.
2. **Context-aware clock + co-located `Run all`/`Step` + visible run-lock** (PP1). *Backend:* structured
   409 reason codes.
3. **Two-zone shell + `Explore | Verify` nav**; demote topology/gallery; Explore gets a real tab; move
   filter/baseline/counts into the Explore toolbar (PP3).
4. **Host-busy/recycle progress + runtime clock-mode toggle** — one shared `recycling…` + workspace disable
   for switch/reset/clock-mode. *Backend:* generation counter on `/api/control/status`; per-generation clock
   mode + `POST /api/control/clock-mode` (PP4).
5. **Polish** — failure taxonomy as three visual registers (red mechanical w/ file+step pointer ≠ amber
   human verdict, mockup 05), guard-state legibility, observe-lean tidy + editing extension-point hooks
   (PP5).

Each phase is independently shippable and independently verifiable.

---

## 12. Verification

Per `Vion.Dale.DevHost.Web/CLAUDE.md`, every change here runs the **`devhost-smoke`** skill, both tiers,
and grows its fixture:

- **Tier 1 (headless, CI):** the HTTP/runtime surface — including any new endpoints/fields (generation
  counter, structured 409 codes).
- **Tier 2 (live UI):** drive the SPA against the project-referenced **`Vion.Dale.DevHost.SmokeHost`**
  (real local source — the only fixture that exercises *this* code, since consumer DevHosts reference the
  published NuGet package). Grow `SmokeHost` with a scenario whose `watchTrace` covers all three lane
  kinds (numeric, boolean/enum, struct) so the trace viewer is smoke-covered.
- Spot-check realistic data by booting a consumer DevHost (e.g. the EnergyManagement DevHost over the
  `logic-block-libraries` corpus) once the package ships.

---

## 13. Signature — the recognizable edge (soft direction, not requirements)

The redesign should carry one deliberate identity: **the DevHost as a deterministic instrument — a logic
analyzer for logic blocks.** This is *intent*, not a spec; the mechanics are the implementor's call. The
edge falls out of what is genuinely rare about this tool — bit-reproducible runs on a controllable virtual
clock, over hardware-shaped signals — rather than being decoration laid on top.

Two flavours give it that edge at **near-zero marginal cost** over the form-C trace viewer (§5):

- **Scope mode** — the trace wears the aesthetic of an oscilloscope / logic analyzer (the instrument the
  embedded/energy engineers who use this already trust): channel lanes, a draggable time cursor with a
  crosshair + a live monospace readout per channel, and (nice-to-have) dual-cursor measurements (`Δt`,
  `Δvalue`). It is the *most functional* framing of the trace, not a costume — it never trades efficiency
  for flair.
- **Ghost / reference trace** — because runs are deterministic, draw a faint reference waveform behind the
  live one (the committed baseline, or the previous run) and mark where behaviour diverged. A diff of
  behaviour-over-time that only a deterministic engine can offer, and what the judge-assist workflow already
  wants.

**Explicitly non-binding.** No exact visuals, gestures, measurement set, or overlay source are mandated —
pick what reads best in the no-build UI. v1 may ship the scope aesthetic without the ghost overlay, or
vice-versa; either still lands the identity. The only ask: the trace should feel like a precise instrument,
not a log.

**Optional layers** (later, implementor's discretion — out of scope here, recorded so the identity has room
to grow):
- **REPL `⌘K`** — promote the command palette into a command line speaking the scenario verbs (`set …`,
  `run …`, `goto t=…`, `step`); cheap, high-identity, pairs with the palette-accelerated navigation (§3).
- **Living wires** — a sign-aware signal-flow rendering of the topology that re-animates as the trace is
  scrubbed; the showiest option, gated on staying restrained and data-driven.

---

## References

- **Current SPA:** `Vion.Dale.DevHost.Web/wwwroot/{store.js, components.js, format.js, app.css, tokens.css}`
  — `store.view`/`setView`, the `topbar` header, the `stepped-chip` cluster, `PlayerPanel` /
  `ScenarioWatchTile` / `PlayerStep`, `Sparkline`, `switchTopology` / `applyScenario` (recycle-on-run) /
  `pollRun`.
- **Engine / data contract:** `Vion.Dale.DevHost/Scenarios/ScenarioRunReport.cs` (`ScenarioRunReport`,
  `ScenarioStepResult`, `WatchSample`), `ScenarioRunner.cs` (the run lifecycle + `SampleWatch`),
  `Vion.Dale.DevHost/Control/DevHostControl.cs` (`IsStepped`, `HasAdvancedFromBaseline`),
  `Vion.Dale.DevHost.Web/DevHostWebRunner.cs` (the recycle supervisor + `generation`), the control/
  scenario/topology API controllers (the 409s, recycle-on-run, `/api/control/status`).
- **Design contract:** `docs/devhost-ui/mockups/{README.md, 02-explorer-pinned-baseline.html,
  04-scenario-player.html, 05-player-guard-states.html, 06-scenario-unification.html}`.
- **Behavioural contract for the clock:** `docs/rfcs/0008-unified-scenario-topology.md`,
  `0008-stepped-host-enabler.md`; scenario files: `0006-scenario-files.md`; control surface:
  `0003-headless-devhost-control.md`.
- **Design system source-of-truth:** `dashboard/src/css/tokens.scss`, `VionSparkline.vue`, `VionSlider.vue`,
  `VionBadge.vue` / `VionState.vue` (patterns reimplemented no-build).
- **Corpus:** `logic-block-libraries/{topologies,scenarios}/*` (+ the `.dale/*.schema.json` for step kinds /
  watch semantics).
