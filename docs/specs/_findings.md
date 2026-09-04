# Finding ledger

Defects an area pass found and deliberately did **not** fix: too big for the pass, or reaching past the
area that found them. One line each, newest area last. Triaged in bulk at the retro
([`../retro/`](../retro/)) — an entry that gets scheduled becomes a Jira item and is struck here with its
key; an entry that is fixed is deleted with the PR that fixes it.

Not for: a small area-local defect (the pass fixes it), a stated behavior that merely surprises (the spec
page states it), or a missing test (that is a `GAP` marker on the page).

## `INTRO` — the introspection document and identifier stability (2026-09-02)

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
- **A blank or a colliding endpoint `Identifier =` draws no compile-time diagnostic.** The bind-time
  refusal (`AC-INTRO-014.3`, `AC-INTRO-014.4`) is the only guard, so an author learns of it at
  `dotnet pack` rather than in the editor. A collision check is a whole-type analysis across two
  attribute families and two declaration levels — `DALE043`/`DALE044`-sized work in the analyzer
  registry, which failed the pass's size guard. *(INTRO pass amendment 2, rows 68 and 69's
  compile-time half — `ANLZ`.)*

## `CTRL` — the development host's control surface and lifecycle (2026-09-04)

- **A clock-mode switch rebuilds the next generation by writing the process environment.** The
  supervisor sets `DALE_DEVHOST_STEPPED` so the rebuilt host's `WithWebUi` reads it
  (`Vion.Dale.DevHost.Web/DevHostWebRunner.cs:195-198`,
  `DevHostBuilderExtensions.cs:43`), because `Func<string?, IDevHost>` carries no mode parameter. It is
  process-global: a second host built in the same process — every test that builds one — inherits a
  mode its caller never asked for. A runner-held static would be the same global under another name,
  and a factory that takes the mode is a surface change. *(CTRL pass row 40 — `CTRL`.)*
- **The duration converter's read half answers 500 where every other bad body answers 400.**
  `Iso8601TimeSpanConverter.Read` lets `TimeSpan.Parse`'s `FormatException` escape
  (`Vion.Dale.DevHost.Web/Api/Serialization/Iso8601TimeSpanConverter.cs:107-123`), which the input
  pipeline does not translate. Unreachable today — every write body binds as `object` or `JsonElement`
  and is decoded by the control surface instead — and live the moment a typed duration reaches a
  request body. *(CTRL pass row 141 — `CTRL`.)*
- **A topology's validation errors are served by splitting a joined message.**
  `TopologiesController.InvalidTopology` splits `InvalidDataException.Message` on `"; "`
  (`Vion.Dale.DevHost.Web/Api/Controllers/TopologiesController.cs:140-148`), so an error containing that
  separator is served as two fragments and the editor shows one with no subject. The fix is a
  structured exception on the topology types, which are `SCEN`'s. *(CTRL pass row 168 — `SCEN`.)*
- **A client that connects before the first generation's actors exist is never primed.** The web host
  starts before the logic system initializes (`Vion.Dale.DevHost/DevHost.cs:66-71` then `:74-96`), so a
  hub connection in that window replays to actors that do not exist and to an empty stand-in list, with
  no error either side. The SPA covers itself with its own snapshot fetch (`wwwroot/store.js:492`); a
  hand-written client relying on the replay alone sees nothing. Closing it means a readiness gate on the
  hub. *(CTRL pass row 184 — `CTRL`.)*
- **Two shipped packages are outside the public-API snapshot.** `Vion.Dale.DevHost` and
  `Vion.Dale.DevHost.Web` are `IsPackable` (`Vion.Dale.DevHost.csproj:9-10`,
  `Vion.Dale.DevHost.Web.csproj:36-37`) and absent from `docs/snapshots/publicapi-manifest.json`'s 12
  assemblies, so a member removed from `IDevHostControl` moves no snapshot and a consumer's build is the
  first thing that notices. Whether the DevHost belongs in that manifest is a corpus decision, not this
  area's. *(CTRL pass row 201 — the retro.)*
