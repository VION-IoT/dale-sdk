# RFC 0011: Drive service-provider value contracts from the DevHost UI

Status: **Parked** (knowledge capture — not scheduled; no solution prescribed yet).
Author: jonas.bertsch. Date: 2026-06-22.

> **Purpose of this document.** RFC 0010 made every `[ServiceProviderContractType]` **value** contract
> drivable/assertable from a committed *scenario* (`serviceProviderSet` / `serviceProviderExpect`). The
> *live* DevHost UI's manual I/O panel did **not** follow — it is still hardwired to the four built-in
> HAL families. This RFC records **what we already know** about closing that gap so the design does not
> have to be re-derived when it is picked up. It deliberately does **not** choose a solution.

## Motivation

In the running DevHost UI, a block's "wiring" panel lets a developer **interactively** poke I/O: toggle a
digital input, type an analog input, read back a digital/analog output (see the four HAL families today).
This is the manual counterpart to a committed scenario — invaluable for exploratory testing.

After RFC 0010, the DevHost wires a stand-in for **every** value contract (PPC and other third-party
contracts included), so a custom contract now *appears* in the wiring panel — but cannot be driven there,
because the panel only knows how to render a `bool` toggle (DI) and a `number` field (AI). Before the
RFC-0010 correctness fix it was worse: a custom contract was **mislabeled** (silently bucketed into the
`AO` branch) and shown a **fabricated `0`** read-out. That fix (below) makes the panel honest; this RFC is
about making it *useful*.

The goal: let a developer interactively drive (and observe) **any** value contract in the UI, the way they
drive a digital/analog input today — including the struct-payload contracts (e.g. PPC) that are the whole
point of RFC 0010.

## What we already know (carry-forward knowledge)

### 1. The driveable boundary already exists — `[ScenarioWire]`

A contract is UI-driveable exactly when it is scenario-driveable: it has a `[ScenarioWire]` codec (a
**value** contract). The same JSON→struct path (`ScenarioWireCodec`) that backs `serviceProviderSet` backs
a UI drive. The RFC 0010 scope boundary (§4) carries over unchanged:

| mechanism | UI-driveable here? |
|---|---|
| value contract (`[ScenarioWire]`) — di/do/ai/ao, PPC | **yes (this RFC)** |
| request/response (`Modbus RTU`, `Action` callbacks) | no — no `[ScenarioWire]`; needs a response-fixture model |
| direct-DI client (`HTTP`, `Modbus-TCP`) | no — off the contract plane; TestKit/Ref* territory |

So "what shows a drive control in the panel" should be gated on the same `[ScenarioWire]`-ness, not on a
hardcoded type list.

### 2. Payloads are unrestricted, but a UI editor only needs a conventional subset

Wire structs are arbitrary CLR types — a UI form can realistically cover a **conventional subset**:
scalars, simple structs, **1-level (and likely N-level) nested structs, and arrays**. That subset covers
most real contracts (PPC included) and is an acceptable limit. The scenario path **already** drives
arbitrary nesting (the codec does `JsonSerializer.Deserialize(structType, …)` — record-struct constructors,
nested structs, enums-by-name, camelCase). So the *mechanism* exists; the UI just needs an editor for the
subset.

### 3. The editor is blocked on the wire-struct schema — the same gap as DF-25

Generating a form for a struct/nested-struct payload needs the **wire struct's JSON Schema**. The UI
already has a struct editor (the one a struct-typed `[ServiceProperty]` uses, e.g. ShowcaseBlock's
`CurrentPosition` — view/edit). But introspection schematizes service **properties**, not contract **wire
structs**. That is the **same gap as DF-25** (the typed-value enrichment that would give `serviceProviderSet`
editor autocomplete/validation). **Couple them:** DF-25 unblocks *both* the scenario value-typing and the
UI form editor — schematize the wire struct once, reuse the existing struct editor.

### 4. The lease / cadence problem — the real differentiator

Some contracts carry a **lease**: the value must be **re-sent every X seconds** or the recipient marks it
stale (PPC's demand is the concrete case). This is the part that makes "just add a struct editor"
insufficient:

- **In a scenario** this is handled by scripting repeated `serviceProviderSet` (and, on a stepped host,
  deterministic virtual-time cadence). Reproducible.
- **In the live UI** (real, free-running time) a one-shot manual drive goes stale immediately and the panel
  is unusable for leased contracts.

So the UI needs a **"hold & re-send every X s"** affordance. The useful reframing: **holding a value makes
the DevHost stand-in behave as a live producer maintaining the lease** — a faithful simulation of the real
upstream. Known considerations:

- The mechanism belongs in the **DevHost stand-in** (a sticky value + a real-clock timer; the handler
  already keeps `_lastInbound`), not in the SPA — the SPA just sets value + interval + on/off.
- The interval is ideally **declared by the contract** (a lease/cadence hint on `[ServiceProviderContractType]`
  or the wire struct) so the UI defaults it correctly, falling back to a user-set interval.
- It is **free-run only**: on a stepped host, cadence is virtual and belongs in the scenario (the same
  deterministic-vs-real-time split the runner already makes for `waitUntil`).

### 5. Two planes — do not conflate

RFC 0010 owns the **deterministic / committed / CI** plane (scenarios — done, works for structs). This RFC
is the **real-time / interactive / lease-aware** plane (live UI driving). They are different planes with
different time models; the design should keep them separate, not fold lease-cadence into the scenario format.

## Current state (what RFC 0010 left in place)

- **Correctness fix shipped with RFC 0010:** the wiring panel now classifies contracts honestly —
  `contractTypeShort` maps the four HAL families to `DI/DO/AI/AO` and **every other value contract to a
  generic `SP`** (with the real `matchingContractType` in the badge tooltip), rendered as **"scenario-driven"**
  rather than a fabricated read-out / dead control. (`format.js`, `components.js`, `app.css`.)
- **No interactive drive** for `SP` contracts yet — that is this RFC.

## Out of scope / non-goals

- Request/response contracts (Modbus RTU) and direct-DI clients (HTTP, Modbus-TCP) — they are out of the
  value-contract boundary (RFC 0010 §4).
- Protocol mocking / non-conventional payloads (opaque types, callbacks).
- Changing the committed scenario format — scenarios already cover structs (`serviceProviderSet`).

## Open questions (left for the design, not answered here)

1. How is a contract's **lease/cadence** declared (attribute on the contract type? the wire struct? a
   convention)? What is the default when undeclared?
2. Wire-struct **schema introspection** — fold into DF-25, or stand alone? How is the existing
   service-property struct editor reused for a contract drive?
3. How does a **held** drive interact with scenario runs, recycle-on-run, and topology switches (stop on
   recycle? survive?)?
4. **Observing** struct/array *output* contracts in the UI (and in `serviceProviderExpect` — RFC 0010 reads
   only scalars today; a struct output reads as null). Field-level display/assert?
5. Should `SP` drive be one **"drive (JSON)"** affordance (paste/edit the wire JSON) as a first step before
   a full schema-driven form?

## Relation to other work

- **RFC 0010** — the scenario-side mechanism this builds on (`[ScenarioWire]`, `ScenarioWireCodec`, the
  generic stand-in, the output cache).
- **DF-25** — typed `serviceProviderSet` value schema; shares the wire-struct-schema dependency (§3).
- **RFC 0008** — the deterministic-stepping / real-time split that frames the two-planes point (§5).
