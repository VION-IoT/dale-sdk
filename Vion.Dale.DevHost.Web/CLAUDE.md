# CLAUDE.md — Vion.Dale.DevHost.Web

The DevHost web UI ("Explorer"). Design contract: [docs/devhost-ui/mockups](../docs/devhost-ui/mockups/README.md)
(screen states + design vocabulary) and [RFC 0006](../docs/rfcs/0006-scenario-files.md) (scenario
files / Player, R3+).

## The no-build discipline (read before touching wwwroot)

The UI is **browser-native ES modules over a vendored Vue ESM build. There is no bundler, no npm,
no node_modules, no TypeScript, no `.vue` SFC files — and none may be introduced.** The whole UI
ships as static files embedded in this assembly (`<EmbeddedResource Include="wwwroot\**\*"/>`),
served by `WebHostService` via `EmbeddedFileProvider`. Consumers get UI changes by NuGet upgrade
with zero project changes.

Rules that keep it working:

- **Components are plain objects with `template:` strings**, compiled at runtime by the vendored
  full build (`vue.esm-browser.prod.js` — the *full* build; the runtime-only build cannot compile
  templates). Use `setup()` + the Composition API.
- **No HTML entities for JS operators in templates.** `&&` inside a template string is fine as a
  nested `v-if`/computed instead — entity decoding differs between in-DOM and string templates, so
  avoid the question entirely: put boolean logic in `setup()` computeds, not in template
  expressions.
- **Flat file names in wwwroot** (no subdirectories). `EmbeddedFileProvider` maps `/` to `.` in
  manifest resource names; flat names sidestep the folder-mangling pitfalls.
- **Vendored deps are pinned files, updated manually**: download the exact published dist file,
  overwrite, update the version in `THIRD-PARTY-NOTICES.txt`. Never reference a CDN — the UI must
  work offline, and `WebControlEndpointsShould.ServeVendoredAssets_AndNeverReferenceACdn` fails the
  build of any external reference in `index.html`. New vendored files must be added to that test's
  asset list.
- **wwwroot is excluded from the ReSharper style gate** (`scripts/cleanup-code.ps1 --exclude`) —
  hand-format JS/CSS sensibly; the C# profile never touches it.

## File map

| File | Role |
|---|---|
| `index.html` | Slim shell: classic-script globals (signalr, dayjs UMD) + `<script type="module" src="app.js">` |
| `app.js` | Entry: dayjs plugin registration, `initStore()`, mount |
| `store.js` | The one reactive store: config, live values (coalesced SignalR), HAL values, collapse state, API writes. Components never call `fetch`/SignalR directly. |
| `format.js` | Pure policy: schema helpers, value/temporal formatting, enum labels, group ordering, collapse-default policy. No DOM, no store — unit-testable by reading. |
| `components.js` | All components, top-down: value cell → controls → docs → row → group → block → rail → App |
| `app.css` | All styles (design language: docs/devhost-ui/mockups/01) |
| `vue.esm-browser.prod.js`, `signalr.min.js`, `dayjs*.js` | Vendored pinned deps (`THIRD-PARTY-NOTICES.txt`) |

## Conventions

- **Draft + dirty pattern** for every writable control: local `text` ref + `dirty` flag; the live
  value flows into the control only while `!dirty` (a `watch`). This is the R0 edit-clobber
  guarantee — never bind a control's value directly to the store.
- **Client state keys are name paths** (`blockName/serviceIdentifier/...`), never per-run GUIDs —
  service ids regenerate every run; localStorage state must survive restarts.
- **Wire keys are camelCased** by the serializer; introspection identifiers are PascalCase. Any
  lookup joining the two must be case-insensitive (see `primeInitialValues`).
- **Rendering policy lives in `format.js`**, view wiring in `components.js`. A change to "how is a
  value displayed / grouped / ordered" goes in format.js so the Player (R3) reuses it.

## Verify loop

Run a real consumer-shaped host and look at it — unit tests can't execute the page's JS:

1. Temporarily switch `examples/Vion.Examples.RichTypes` to project references (see the e2e-harness
   pattern in `Vion.Dale.DevHost.Test.csproj`: SDK + Generators-as-analyzer + Metalama package) —
   do not commit that switch.
2. `dotnet run` its DevHost (port 5000; `DALE_DEVHOST_NO_BROWSER=1` for headless) and drive the
   page (browser or preview tooling). RichBlock exercises every value shape: enums with German
   labels, status pills, structs, arrays, nullables, durations, relative timestamps, a 2 s timer.
3. `Vion.Dale.DevHost.Test` guards the contract surface (endpoints, assets, no-CDN invariant);
   behavioral checks are this manual loop.
