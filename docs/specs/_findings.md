# Finding ledger

Defects an area pass found and deliberately did **not** fix: too big for the pass, or reaching past the
area that found them. One line each, newest area last. Triaged in bulk at the retro
([`../retro/`](../retro/)) — an entry that gets scheduled becomes a Jira item and is struck here with its
key; an entry that is fixed is deleted with the PR that fixes it.

Not for: a small area-local defect (the pass fixes it), a stated behavior that merely surprises (the spec
page states it), or a missing test (that is a `GAP` marker on the page).

## `GATE` — config-time structural gating (2026-09-02)

- **A development host reports itself started over a block that failed to initialize.** The
  configuration is sent to the actor and the send returns; the actor sends no acknowledgement, so an
  initialization throw surfaces only as one `[EXCEPTION CAUGHT]` log line while the host reports
  success and the operator sees a block whose properties all read null. The two ways a configuration
  is wrong before it reaches the actor — an unresolvable identifier and an undecodable value — are
  both refused at the topology loader now (`AC-GATE-012.8`); what remains is the general case, which
  is the development host's start-and-health surface. *(GATE pass row 66, narrowed by amendment
  4b — `CTRL`.)*

## `INTRO` — the introspection document and identifier stability (2026-09-02)

- **The development host's parameter-editor schema carries a bound that is not a number.** The
  catalog builds an `[InstantiationParameter]`'s editor schema straight off the paired
  `[ServiceProperty]`'s bounds and tests them with `double.IsInfinity`
  (`Vion.Dale.DevHost/Topologies/LogicBlockDefinition.cs:149`,`:154`), so a `NaN` bound passes the test
  and is then cast to `long` — an undefined conversion that renders as a garbage limit. It is the same
  one-sided shape `AC-INTRO-007.3` closed in the introspection producer, at a site this pass does not
  own: the criterion is `AC-GATE-010.6`'s field and the test would live in the development host's
  suite. One line to fix, and it needs the owning area's criterion. *(INTRO pass, sibling sweep of row
  30 — `CTRL`.)*
- **`dale list` cannot render a nested block's short name, and drops an endpoint whose identifier is
  empty.** The projection splits a block's identity on `.` (`Vion.Dale.Cli/Commands/ListCommand.cs:72`,
  `:153`), so a nested block lists as `Outer+NestedBlock` — and it filters an empty identifier out
  entirely (`:74`, `:76`), which contradicts the promise that `dale list` prints the identifiers as the
  document emits them. `AC-INTRO-014.3` removes the second input at the source; the first survives
  because `AC-INTRO-004.1` keeps a block's identity in CLR form deliberately. The projection is Tier B.
  *(INTRO pass row 88 — `CLI`.)*
- **Nothing warns when a library's `<PackageId>` and `<AssemblyName>` diverge after the identity
  change.** `AC-INTRO-001.2` makes the document's package identity the nuspec id, which is the id the
  platform registers; a project that changes only its assembly name now silently keeps its keys, and one
  that changes only its package id silently re-namespaces them. A `dale build` / `dale pack` warning is
  where an author would see it. *(INTRO pass, residue of row 2 — `CLI`.)*
- **`dale list` runs the introspection without the development-only exclusion**, so it lists blocks the
  packed artifact omits (`Vion.Dale.Cli/Helpers/ParserRunner.cs:250`–`:258` passes only `--package-id`).
  Whether the CLI should filter them or mark them in its output is a question about what `dale list` is
  for, which is decided when the CLI is specced; the introspection page says what the listing means in
  the meantime. *(INTRO pass amendment 2, M7 — `CLI`.)*
