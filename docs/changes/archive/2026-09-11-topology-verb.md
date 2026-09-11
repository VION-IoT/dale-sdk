---
slug: topology-verb
status: archived
blocked-on: none           # for parked docs: what's blocking + ref
areas: CLI, SCEN
author: Claude Opus 5 (dispatched, VION-73)
created: 2026-09-11
updated: 2026-09-11
supersedes: none           # path of a superseded change doc, or none
---

# `dale topology validate` and `dale topology schema`

> Change doc — one in-flight change. The `Spec delta` below is distilled into the current-truth
> pages under `docs/specs/` in the implementation PR, then this doc is archived
> (`pwsh scripts/spec-change.ps1 archive <slug>`). Process: `docs/spec-process.md`. Never put
> change narrative inline in a spec page — it lives here.

## At a glance

### Summary

A hand-edited `*.topology.json` can be checked, and the generic topology schema obtained, with no
DevHost running. Two new subcommands under a `topology` group in `Vion.Dale.Cli` mirror the two
`scenario` verbs a consumer already gates on. The consumer repo hand-rolled a PowerShell guard
because the SDK shipped no offline equivalent; this is the SDK half of VION-73.

### Spec implications

Two pages, along the split [`specs/cli.md`](../../specs/cli.md)'s `scenario` section already states:
the **command surface** is `cli.md`'s, the **file's own rules** are `scenarios.md`'s.

- `cli.md` — a new `AC-CLI-020.*` group for the `topology` command (its two subcommands, the
  directory walk, the empty-directory refusal, the embedded schema, the `--out` trio), and one
  `MODIFIED` on `AC-CLI-002.1`, whose text enumerates the top-level commands by count and by name.
- `scenarios.md` — five leaves under `AC-SCEN-015.*` (*The offline validators*, renamed from *The offline validator*), which already owns
  the scenario-side mirror and the ship-the-canonical-file rule this change repeats for the
  topology schema.

No change to `topology.schema.json`, `DevTopologyFile`, `DevTopologyLoader` or the control API. The
schema file is a cross-repo contract with the dashboard
([`architecture/libraries/dale-sdk.md`](../../../../architecture/libraries/dale-sdk.md) § Key
invariants); this change *ships* it and does not evolve it.

### Decisions

- `D1` — The validator is a hand-rolled structural mirror over raw JSON, not JSON Schema
  validation — the CLI has no schema library and the rules that matter most (id matches filename, a
  duplicate instance name, a mapping naming an undeclared block) are not expressible in the schema.
- `D2` — `validate` contacts no host at all, and `schema` emits the embedded document unenriched —
  there is no topology-side analogue of the scenario schema's name-path enrichment, and the
  type-loadability half of a topology check (`AC-SCEN-013.3`) needs the loaded catalog, which is
  the host's.
- `D3` — A wire declared twice, in either order, is an error (brief question 1) — and it is a
  **mirror, not an addition**: `ContractPairingResolution.Resolve` refuses it when the topology is
  built, under `AC-SCEN-014.3`, with the same symmetric key. `DevTopologyFile.Parse` does not, which
  is what the brief's question was about and what this decision first generalised from. See *Drift
  checkpoints*.
- `D4` — A missing `$schema` is a **warning**, promoted to an error only by `--require-schema-ref`
  (brief question 2). The loader declares `$schema` optional, so a hard rule would contradict it;
  the opt-in flag is what lets the consumer's gate keep the strictness it has. This is the only rule
  the validator adds to the loader's set.
- `D5` — `topology schema` takes the same `--out` / `-O` / `-o` trio as `scenario schema`
  (`AC-CLI-010.10`), deprecated alias included, so the two schema verbs are one surface to a script
  that drives both. `-o` is **not** the output-format option on either.
- `D6` — The schema file is **linked** into the CLI as an `EmbeddedResource`, never copied — the
  precedent `AC-SCEN-015.5` set for the scenario schema, and the reason the consumer's committed
  copy could go stale in the first place.
- `D7` — `topology schema` emits the embedded file's **own text, byte for byte**, rather than
  re-serializing it from a `JsonNode`. There is nothing to enrich, and a round-trip reflows the
  source's one-line arrays and drops its final newline — which turns a consumer's regeneration into
  a whole-file diff instead of the hunks their copy has drifted by. Measured: see *Drift
  checkpoints*.

### Reviewer's questions

1. *(b) decide-and-document* — Should a duplicate contract pairing be an error, making the CLI
   validator stricter than `DevTopologyFile.Parse`? Decided `D3`: yes.
   **OUTCOME**: the answer stands and its reason does not. Review round 1 read
   `ContractPairingResolution.Resolve`, which refuses the duplicate at load under `AC-SCEN-014.3` —
   so the check is a mirror and the validator is not stricter than the host at all. The question was
   the wrong question; the code it produced is right. Corrected in `D3` and the first *Drift
   checkpoint* of that round.
2. *(b) decide-and-document* — Warning or opt-in error for a missing `$schema`? Decided `D4`: both,
   warning by default and error under `--require-schema-ref`. All 78 corpus files carry `$schema`,
   so the default path is silent today. **OUTCOME**: accepted as decided; no reviewer change.
3. *(a) ratified — cite* — Does `validate` need any host contact? No: brief *Constraints* 3 makes
   offline the point and enrichment optional. `D2` takes none. **OUTCOME**: accepted; the command
   is host-free and its tests prove it by running with nothing on the port.
4. *(b) decide-and-document* — Which page carries the validator's rules? `cli.md:286` states the
   split, and this change follows it: surface on `cli.md`, file rules on `scenarios.md`.
   **OUTCOME**: accepted as decided; the delta targets both pages accordingly.

---

## Full design

### The command

```
dale topology validate [--dir topologies] [--require-schema-ref]
dale topology schema   [--out <file>|-O <file>|-o <file>]
```

`Vion.Dale.Cli/Commands/TopologyCommand.cs` is the sibling of `ScenarioCommand.cs`: a static class
with `Create()`, registered in `Program.cs` next to `ScenarioCommand.Create()`. The checking core is
`Vion.Dale.Cli/Commands/TopologyFileChecks.cs`, the sibling of `ScenarioFileChecks.cs` — a
`Validate(fileName, json, options)` over raw JSON with no SDK types, returning every error at once.

`validate` walks `--dir` in ordinal name order, reports per file, and exits 1 where any file has an
error. A directory that does not exist, and one holding no `*.topology.json`, are both refusals —
the anti-vacuous floor `AC-CLI-010.11` already states for the scenario side, and the same floor the
consumer's script carries (`check-topology-files.ps1`: "finding ZERO topologies is a FAILURE").

`schema` prints the embedded generic schema, or writes it to `--out`, as the canonical file's own
text (`D7`) — which makes escaping a non-question: the bytes already are the ones the source file
holds.

### What the validator checks, and against what

The canonical rules live in `Vion.Dale.DevHost/Topologies/DevTopologyFile.cs` — `Parse` at `:94`,
`Load` at `:199`, strict unknown-member rejection configured at `:38` — and in
`Vion.Dale.DevHost/Topologies/topology.schema.json`. Read this session. The mirror:

| Rule | Canonical site | Mirrored |
| --- | --- | --- |
| valid JSON, a JSON object | `Parse:97-109` | yes |
| no unknown member, at every level | `SerializerOptions:38` (`UnmappedMemberHandling.Disallow`) + the schema's `additionalProperties: false` | yes, as a known-key set per object level |
| no duplicate JSON property | `SerializerOptions:39` (`AllowDuplicateProperties = false`) | yes, via `JsonDocumentOptions` — `JsonNodeOptions` carries no such knob |
| `id` required, slug, no `..`, not `schema` | `Parse:117-124` | yes |
| `id` matches the file name | `Load:208-211` | yes |
| at least one instance | `Parse:126-129` | yes |
| instance `typeFullName` and `name` required | `Parse:134-142` | yes |
| instance names unique | `Parse:143-146` | yes |
| instance names free of `.` | `Parse:147-150` | yes |
| interface mapping's four fields required | `Parse:155-159` | yes |
| interface mapping names declared instances | `Parse:162-171` | yes |
| pairing endpoint's two fields required | `ValidatePairingEndpoint:282-286` | yes |
| pairing endpoint names a declared instance | `ValidatePairingEndpoint:288-291` | yes |
| no self-pairing | `Parse:182-187` | yes |
| contract mapping's two required fields | the schema's `required` on `contractMappings.items` | yes |
| an instantiation-parameter value is a JSON scalar | the schema's `additionalProperties` on `instantiationParameters` (`AC-SCEN-013.8`) | yes |
| no duplicate wire | `ContractPairingResolution.cs:56-62`, reached from `DevTopologyLoader.cs:204` (`AC-SCEN-014.3`) | yes |
| contract mapping names a declared instance | `DevTopologyLoader.cs:175-179` (`AC-SCEN-013.5`, first half) | yes |
| a collection of the wrong JSON kind | `SerializerOptions:35-41` — the strict deserializer throws | yes |
| **`$schema` present** | nowhere — `Schema` is `string?`, `D4` | added as a warning / opt-in error |
| instance type is loadable and is a logic block | `AC-SCEN-013.3` — needs the loaded catalog | no, and cannot be |
| a contract mapping names a contract the block carries | `AC-SCEN-013.5`, second half — needs the catalog | no, and cannot be |
| which pairing directions materialise | `AC-SCEN-014.*` — CLR type identity of two `[ScenarioWire]` halves | no, and cannot be |

The last three are the half no offline text check can reach, which the consumer's own script header
already says in its own words. The DevHost's stepped-scenario gate proves the files *load*; this
verb is the fast PR lane in front of it.

The instance-name checks mirror `Parse`'s `else if` chain exactly, including its order: a duplicate
name suppresses the dot check for that instance, because the host reports it that way.

### Why not JSON Schema validation

The CLI's two package references are `System.CommandLine` and `Spectre.Console`, and the
no-SDK-dependency rule (`Vion.Dale.Cli/CLAUDE.md` § Key Design Decisions, and the csproj comment
above the `EmbeddedResource`) forbids reaching for `DevTopologyFile.Parse`. Adding a JSON Schema
library would buy the weakest half of the check: the schema cannot express *id matches the file
name*, *instance names are unique*, or *an interface mapping names a declared instance* — which are
exactly the errors a hand edit produces. A validator that only ran the schema would be green on a
file with a wrong id and a dangling mapping reference. So the mirror is hand-rolled, the way
`ScenarioFileChecks` is, and the schema ships alongside it for the editor to enforce what it can.

### The embedded schema

`Vion.Dale.Cli.csproj` gains a second `EmbeddedResource` beside the scenario one, linking
`..\Vion.Dale.DevHost\Topologies\topology.schema.json` with `LogicalName`
`Vion.Dale.Cli.topology.schema.json`. One physical file, embedded into both assemblies, so the CLI's
copy cannot drift from the host's — the same reasoning `AC-SCEN-015.5` carries for the scenario
schema, and the exact failure the consumer's committed copy shows today.

---

## Drift checkpoints

- 2026-09-11: *Full design* had `topology schema` re-serialize the embedded document through a
  `JsonNode` with relaxed escaping, mirroring `dale scenario schema`. Measured against the
  consumer's stale copy, that produced a **166-line whole-file diff** where the real drift is four
  hunks: the round-trip expands the source's one-line arrays (`"required": ["id",
  "logicBlockInstances"]`) and drops its final newline. The scenario verb has no choice — it
  enriches the document — but this one does, so it emits the resource's own text. `D7`, and
  `AC-CLI-020.4`'s text follows. Sibling sweep: N/A — `dale scenario schema` must keep
  re-serializing, since enrichment rewrites the document.
- 2026-09-11: the mirror table gained a row the design missed — an instantiation-parameter value
  must be a JSON scalar (`AC-SCEN-013.8`). It is offline-decidable and a hand-edited object or
  array there reaches the operator as a config-time gate that silently did not resolve.
- 2026-09-11 (review round 1): **`D3`'s premise was wrong.** A duplicate wire is refused at load by
  `ContractPairingResolution.cs:56-62`, with the same symmetric key the validator now uses, and
  `AC-SCEN-014.3` on the edited page already states it. `DevTopologyFile.Parse` does not refuse it —
  which is what the brief verified and what this doc generalised from the parser to the loader
  without reading the loader. The code stands unchanged; `D3`, the mirror table and the
  `docs/specs/scenarios.md` prose were rewritten, and the message now matches the host's wording. So
  the validator adds exactly one rule to the loader's set, `AC-SCEN-015.9`, not two.
- 2026-09-11 (review round 1): two mirror gaps closed. `ValidateContractMappings` did not resolve
  its block against the declared instances, though `DevTopologyLoader.cs:175-179` does and needs no
  catalog for it — a renamed instance missed in one `contractMappings` entry validated green and
  failed at load. And each of the four collections, given a JSON value of the wrong kind, was
  skipped in silence: the file passed *and* every check over that collection was dropped, which is
  the shape a hand edit takes when the brackets around a single entry go. Sibling sweep: all four
  collections and the `$schema` member, each with its own row.
- 2026-09-11 (review round 1): the wire key joined its two endpoints on `" <-> "`, and a contract
  identifier may carry a `.` — a collision was unreachable only because instance names are dot-free.
  It now joins on a character no JSON member name can hold.
- 2026-09-11 (review round 1): `AC-CLI-020.2` says *ordinal* name order and the walk sorted full
  paths with `OrdinalIgnoreCase`, inherited from `ScenarioCommand`. The code now sorts file names
  ordinally, and the test's two rows differ in case so they can tell the two orders apart. The
  sibling verb's own wording drift (`AC-CLI-010.5`) is left alone — it is not this change's.

---

## Spec delta (to distill)

- MODIFIED AC-CLI-002.1 -> docs/specs/cli.md : THE SYSTEM SHALL offer fourteen top-level commands — `new`, `build`, `test`, `dev`, `list`, `scenario`, `topology`, `add`, `pack`, `upload`, `login`, `logout`, `whoami` and `config` — and SHALL make `--output`, `--project` and `--verbose` available on every one of them and on their subcommands.
- ADDED AC-CLI-020.1 -> docs/specs/cli.md : THE SYSTEM SHALL offer two topology subcommands — validate and schema — and SHALL address no running host from either, so both answer with nothing on the port.
- ADDED AC-CLI-020.2 -> docs/specs/cli.md : WHEN every topology file in a directory is validated THE SYSTEM SHALL check them in ordinal name order and exit 1 where any has an error.
- ADDED AC-CLI-020.3 -> docs/specs/cli.md : WHEN the topologies directory does not exist or holds no topology file THE SYSTEM SHALL refuse naming it rather than report a successful validation.
- ADDED AC-CLI-020.4 -> docs/specs/cli.md : THE SYSTEM SHALL carry the generic topology schema inside itself and SHALL emit its text unaltered, so the schema can be produced with no host running and a regenerated copy differs from the canonical file in nothing but that file's own drift.
- ADDED AC-CLI-020.5 -> docs/specs/cli.md : THE SYSTEM SHALL take `topology schema`'s destination from `--out`, whose short form is `-O` and whose deprecated alias is `-o`, and SHALL leave `--output` meaning the tool's output format.
- ADDED AC-SCEN-015.6 -> docs/specs/scenarios.md : THE SYSTEM SHALL check a topology file offline against the structural rules its loader applies — the id and its match with the file name, the instances, the interface mappings, the contract mappings and the contract pairings — and SHALL report every error it found rather than the first.
- ADDED AC-SCEN-015.7 -> docs/specs/scenarios.md : THE SYSTEM SHALL refuse offline a member no topology file declares and a member declared twice, at every level of the document, so a hand edit's misspelling is reported without a host.
- ADDED AC-SCEN-015.8 -> docs/specs/scenarios.md : THE SYSTEM SHALL refuse offline a topology that declares one wire twice, whichever order each pairing names its two endpoints in.
- ADDED AC-SCEN-015.9 -> docs/specs/scenarios.md : WHERE a topology file carries no schema reference THE SYSTEM SHALL report that as a warning, and SHALL refuse the file for it only where the caller asked for the reference to be required.
- ADDED AC-SCEN-015.10 -> docs/specs/scenarios.md : THE SYSTEM SHALL ship the generic topology schema to the command-line tool as the canonical file itself rather than a copy of it.
- MODIFIED AC-SCEN-015.8 -> docs/specs/scenarios.md : THE SYSTEM SHALL refuse offline a topology that declares one wire twice, whichever order each pairing names its two endpoints in, as the loader refuses it.

---

## Relay notes for the PR body

- **`dale topology validate [--dir topologies] [--require-schema-ref]`** — checks every
  `*.topology.json` in a directory against the rules the host's loader applies, with no host
  running. Exits 1 on any error. One thing it reports that the host does not: a missing `$schema`
  reference, as a warning, or as an error under `--require-schema-ref`. Everything else it reports,
  the host refuses too — including a wire declared twice, which the loader refuses when it builds
  the topology.
- **`dale topology schema [--out <file>]`** — prints the generic topology schema, or writes it.
  `-O` is the short form and `-o` the deprecated alias, matching `dale scenario schema`. The
  conventional destination is `topologies/.dale/topology.schema.json`, what the files' `$schema`
  points at.
- **The schema the tool emits is the canonical file**, linked into the CLI rather than copied, so
  it cannot drift from the host's.
- `dale` now offers fourteen top-level commands; the committed help snapshot changes accordingly.

---

## Tasks

- `T-001` (`AC-CLI-020.4`, `AC-SCEN-015.10`): link the topology schema into the CLI as an embedded
  resource.
- `T-002` (`AC-SCEN-015.6`–`.9`): `TopologyFileChecks` — the structural mirror plus the two added
  checks, with its tests.
- `T-003` (`AC-CLI-020.1`–`.5`, `AC-CLI-002.1`): the `topology` command and its two subcommands,
  registered, with its tests.
- `T-004` (all): distill both pages, run the corpora and the red cases, archive.
