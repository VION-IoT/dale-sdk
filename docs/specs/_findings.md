# Finding ledger

Defects an area pass found and deliberately did **not** fix: too big for the pass, or reaching past the
area that found them. One line each, newest area last. Triaged in bulk at the retro
([`../retro/`](../retro/)) — an entry that gets scheduled becomes a Jira item and is struck here with its
key; an entry that is fixed is deleted with the PR that fixes it.

Not for: a small area-local defect (the pass fixes it), a stated behavior that merely surprises (the spec
page states it), or a missing test (that is a `GAP` marker on the page).

## `GATE` — config-time structural gating (2026-09-02)

- **A gated member's predicate reaches the wire unchecked.** `Definition`-mode binding short-circuits
  before parsing, so a predicate that cannot parse or whose reference does not resolve is emitted into
  the introspection JSON verbatim and `dotnet pack` succeeds — shipping an artifact whose every
  activation then fails closed. DALE043 is the compile-time catch, but it is suppressible and absent
  from any project that does not wire the analyzer pack. Parse-checking on the pack path is a change to
  what `dotnet pack` refuses, not an area-local fix. *(GATE pass row 64.)*
- **A development host reports itself started over a block that failed to initialize.**
  `DevLogicSystemInitializer` sends the configuration to the actor and returns, so a configuration
  failure — an undecodable instantiation-parameter value, or any other initialization throw — surfaces
  only as one `[EXCEPTION CAUGHT]` log line. The host reports success, and the operator sees a block
  whose properties all read null beside a service list computed from the definition defaults. The
  unresolvable-identifier half is fixed in the loader (`AC-GATE-012.8`); the general case is the
  development host's start-and-health surface, which `CTRL` owns. *(GATE pass row 66.)*
- **A catalog parameter default cannot distinguish "no default" from "null".** `LogicBlockDefinition.FromType`
  reads each `[InstantiationParameter]`'s default off an instance, and is called without one for a block the
  host cannot construct (its constructor takes dependencies). Every default then reports as null — the same
  value a parameter whose declared default genuinely is null reports, which the editor and the SPA both read
  as "unknown" and fail open on. Telling the two apart needs either a separate "known" flag on the catalog
  entry or construction through the DI scope, both of which are the development host's catalog contract.
  *(GATE pass, amendment 2 — M8, CTRL.)*
- **A topology file cannot express a null instantiation-parameter value.** `topology.schema.json` types
  `instantiationParameters` values as `["boolean", "integer", "string"]`, so the JSON null that
  `AC-GATE-002.7` accepts for a nullable parameter is unreachable from a topology — a nullable parameter can
  only take its declared default there. The schema is the cross-repo topology-exchange contract, so widening
  it is a change to that contract rather than an area-local fix. The limitation is stated on the spec page.
  *(GATE pass, amendment 2 — M9, cross-repo.)*
