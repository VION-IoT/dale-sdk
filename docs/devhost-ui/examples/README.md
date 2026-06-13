# RFC 0006 worked examples

Copy-pasteable reference files for the [scenario + topology stack](../../rfcs/0006-scenario-files.md)
(RFC 0006). These are the canonical shapes the prose describes — a real consumer's first adoption found the
SDK's own example files the single most useful reference, so they live here, linked from the RFC, and
standardized on one convention.

```
scenarios/
  .dale/scenario.schema.json      generic scenario schema (snapshot of the shipped one)
  charging-smoke.scenario.json    a full scenario: setup, ordered steps, waitUntil, watch, judge
topologies/
  .dale/topology.schema.json      generic topology schema (snapshot of the shipped one)
  energy-demo.topology.json       a dev-profile topology (instances, interface + contract mappings)
```

## Conventions shown here

- **`$schema` is the per-project `./.dale/…` form** — every file points at the schema committed next to it
  (`./.dale/scenario.schema.json`, `./.dale/topology.schema.json`), so editors give completion and catch
  wrong field names. Generate the scenario schema for your own topology with
  `dale dev --export-config config.json` then `dale scenario schema --config config.json -o scenarios/.dale/scenario.schema.json`
  (offline — no host needed). The topology schema is generic; copy this one or fetch it from
  `GET /api/topologies/schema`.
- **Name paths** use the two-segment `Block.Property` form where unambiguous and the three-segment
  `Block.Service.Property` form for multi-service blocks (see
  `ChargingStationMultiPoint.ChargingPoint1.ActivePowerConsuming`).
- **Topology contract-mapping fields** use the `mapped*` names — the same shape `dale dev --export-config`
  emits (converged in RFC revision 6).
- **A watch-only scenario** (no `steps`) is legal and is the recommended exploration starting point — drop
  the `setup`/`steps`/`judge` from `charging-smoke` and keep `watch` to stage the relevant signals without
  driving anything.

## Snapshots

The two `.dale/*.schema.json` files are snapshots of the schemas shipped in `Vion.Dale.DevHost`
(`Scenarios/scenario.schema.json`, `Topologies/topology.schema.json`) and served at
`GET /api/scenarios/schema` and `GET /api/topologies/schema`. Refresh them from those sources if the
vocabulary changes.

The block/property names match the `energy-demo` topology built from the `Vion.Examples.Energy` library.
