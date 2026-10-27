# Spec-driven development — how to run it

The operational playbook for this repo's spec corpus, its change docs, and the lanes work runs in.
The corpus config [`specs/_corpus.yml`](specs/_corpus.yml) carries what is this repo's rather than
the method's: which area codes it owns, where coverage is counted, and which repos it may cite.

Copied from the `vion-sdd` plugin's `templates/spec-process.md`; dale-sdk owns it from then on. Its
generic core is the method's — the corpus, ids and EARS, change docs, lanes 1 and 2 — and lane 3 is
the plugin's `pass` skill, not a section here. dale-sdk's own parts are § The roster, § Routing,
§ Gates and § What a pass in this repo adds.

## The corpus — `docs/specs/`

Current-truth pages: present tense, terse, written in published-surface vocabulary — a page's
evidence is the tests that cite its ids (the trace gate), never source references, so an
implementation change that leaves behaviour intact never touches a page. What the SDK guarantees
**today** — no history, no incident narrative, no design alternatives, no rosters of today's
instances (those live in git, the archived change docs and the process journal). One page per
**area**:

> **An area is one contract with its own anchor artifact** — a schema, manifest, golden file,
> diagnostic registry or committed scenario set that already pins it mechanically.

Plus [`specs/_invariants.md`](specs/_invariants.md) (`SYS-` ids, cross-cutting rules pages cite
instead of restating).

### The roster

Every code here is listed under `owns:` in `docs/specs/_corpus.yml`, the invariant codes included —
the lint fails a declaration whose area the config does not own. A new area enters by a lane-2
change that adds its code to `owns:` and its row here.

| Page | Area code | Pinned by |
| --- | --- | --- |
| [`_invariants.md`](specs/_invariants.md) | `API`, `REL` | the public-API manifest; the release roster |
| [`analyzers.md`](specs/analyzers.md) | `ANLZ` | the analyzer registry's DALE descriptors |
| [`emission.md`](specs/emission.md) | `EMIT` | the service-property and measuring-point attributes and their emission knobs |
| [`contracts.md`](specs/contracts.md) | `BIND` | the contract and provider-face declarations |
| [`config-gating.md`](specs/config-gating.md) | `GATE` | the instantiation and config-time gating attributes |
| [`introspection.md`](specs/introspection.md) | `INTRO` | the introspection JSON's golden files and the packed-artifact rule |
| [`scenarios.md`](specs/scenarios.md) | `SCEN` | the scenario schema and the committed scenarios |
| [`devhost-control.md`](specs/devhost-control.md) | `CTRL` | the DevHost's HTTP routes and its stdout contract |
| [`plugin-loading.md`](specs/plugin-loading.md) | `PLUG` | `[DaleSharedAssembly]` and the load-context rules |
| [`host-vocabulary.md`](specs/host-vocabulary.md) | `HOST` | the message-type declarations a host runtime and the SDK exchange |
| [`cli.md`](specs/cli.md) | `CLI` | the CLI help snapshot |
| [`block-lifecycle.md`](specs/block-lifecycle.md) | `LIFE` | the lifecycle messages and their ordering |
| [`modbus.md`](specs/modbus.md) | `MODB` | the Link and Connection structs, the committed scenarios, the `modbus-smoke` skill |
| [`io.md`](specs/io.md) | `IO` | the DigitalIo and AnalogIo contracts |
| [`testkit.md`](specs/testkit.md) | `TKIT` | the six test kits' public surface |
| [`http.md`](specs/http.md) | `HTTP` | the Http contract |

Exempt: examples, templates, the `Vion.Dale.DevHost.Web` SPA's internals, the SmokeHost fixture, and
the first-party libraries under `libraries/` — consumers of the SDK shipping from this repo, not SDK
contract; one that ever warrants specs gets block specs in place, outside this corpus.

### Page frontmatter — the trace ratchet

A page opts into the trace gate with frontmatter:

```markdown
---
trace: enforced
---
```

The marker is a **ratchet**: set when the area's page lands, never removed. `spec-trace` fails if a
marked page declares zero ids (the parse died — anti-vacuous floor) or if any declared id has no
referencing test.

## IDs & EARS

`SYS-<AREA>-NNN` (invariants) · `AC-<AREA>-NNN.M` (acceptance criteria, `.M` leaves under a `NNN`
umbrella). Area codes: the roster above, uppercase alphanumerics, and each one listed under `owns:`.
Every AC is one EARS sentence (ubiquitous / WHEN / WHILE / IF-THEN / WHERE) with a `SHALL`. No
`should`, `fast`, `performant` — `spec-lint` rejects them.

Every AC carries one of five labels, decided by its keyword — `(Ubiquitous)` no keyword;
`(Event-driven)` WHEN; `(State-driven)` WHILE; `(Unwanted)` IF … THEN; `(Optional)` WHERE. A
criterion combining keywords takes the label of its first keyword. `spec-lint` rejects any other
label.

**An id outside `owns:` is a citation, never a declaration.** A page declares only ids whose area
its repo owns; a criterion another repo owns is cited as `<repo>/AC-…`, with that repo listed under
`cites:`. A bare foreign id fails the lint, and a citation is never counted as declared and never
needs a test here — the repo that owns it proves it. So one repo's codes stay its own and a
neighbour can take a new code without asking. Outside this repo — a neighbour's page, a hub page, a
brief, a report — an id always carries `<repo>/`. An area code is unique within its repo only: two
repos may own the same code, no hub registry lists them, and taking a code sweeps no neighbour's
ids for a clash. Here the cited repo is `dale`: a rule only the host runtime can prove names its
proof as `dale/AC-…`, in its `GAP` tail.

**Minting is a consolidation.** Extraction and specification want different granularities: a
behaviour table over-produces rows on purpose, and the page states one criterion per **rule**, with
the fields, tokens or sites the rule ranges over as its test's data rows — never one criterion per
field. **A criterion is minted where a consumer's action or a failure's outcome turns on it.** A row
that only bounds a neighbour — an ordering, a comparison rule, an edge value — is a data row of that
neighbour's test, or `out-of-spec`; the table keeps its one row per observable, and only the page
coarsens. That is the default granularity, not the only one, and dale-sdk keeps the finer one it
has minted at since its first page: where a consumer can observe a bounding row on its own — an
ordering it schedules around, an edge value its configuration can reach — it may be a leaf of its
own rather than a data row. A family of schema mirrors folds into the rule they mirror; a
doc-comment defect is a fix without a criterion; a behaviour another page owns is cited there,
never re-minted. That holds in every lane. Roughly half the classified rows become criteria (230
rows became 135 in one area without losing one), so a round that produced a classified behaviour
table owes a **consolidation map** in its change doc: row → criterion, or row → the line saying why
it mints nothing; a row with neither is a blocker. A round with no classified rows, such as lane 2,
owes no map. **A `file` row appears in the map by name and is never folded into a criterion**; a
folded `file` row is a lost draft item. No criterion's subject is a test suite: a page states what
a consumer observes, and test discipline is [`testing-conventions.md`](testing-conventions.md)'s.
**A criterion's text states what every test citing it proves**: where a fold leaves a rule proven by
a citing test's data row and unstated by the criterion, the rule joins the criterion as a clause, by
a `MODIFIED` line — the citation rule is never widened to let a test cite a criterion for what only
its data rows prove (one folded tie-break was proven by a test citing a criterion that said nothing
about versions).

An AC is **covered** when its id appears as a **quoted string literal** in a test artifact matched by
one of the config's `tests:` globs, in one of this repo's three forms:

- MSTest: `[TestProperty("spec", "AC-EMIT-001.1")]` on the test method
- xunit.v3: `[Trait("spec", "AC-EMIT-001.1")]`
- a committed scenario: `"specs": ["AC-SCEN-003.1"]` in the `*.scenario.json`

A mention in a comment or method name does not count, so an id belongs in exactly those forms and
never in any other string (an assert message or an expectation array carrying one would bind by
accident; read the ids off the artifact under test instead). A bare umbrella id is covered by any of
its `.M` leaves; a leaf gets no such exemption. A test that pins an implementation **premise** rather
than a criterion cites no id by design and says so in its class summary — the rule and its reason
are [`testing-conventions.md`](testing-conventions.md) §17 — and `spec-trace` never sees such a
test.

**GAP rows** — a declared requirement whose test does not exist yet — carry the `GAP` marker on the
declaring line, with the reason or issue key: `` - `AC-PLUG-004.1` (Event-driven): WHEN … THE SYSTEM
SHALL …. GAP: test pending (VION-nn) ``. `spec-trace` exempts them from the orphan check and reports
the count instead — the in-repo backlog stays visible without reddening CI. Removing the marker is
how a landed test re-arms the gate for that id. If the reason is "no observable", the row is not a
requirement at all: drop it rather than mark it. Two mechanics of the marker cost a round each:

- **The marker binds only on the id's own line.** A declaring bullet keeps its id and its `GAP` tail
  on one line; a wrapped bullet with `GAP` on its second line fails the gate with no hint why.
- **Any acceptance id on a line without a `GAP` marker is read as a declaration.** Cite a
  neighbour's GAP'd id by family (`AC-ANLZ-018.*`, never in full) and refer to your own GAP'd
  criteria by description in the prose below the bullet, never by number — four of one page's eleven
  GAPs were un-GAP'd by their own explanatory prose on the first run.

Because the gate reads an id without the marker as proven the moment any test carries it, **a
criterion no test in the suite can reach is `GAP` however many tests name it**: one about a CI-only
gate read as proven on two option-description tests. A test citing a `GAP` id never lifts the marker
— `spec-trace` names such ids on a note line and does not fail on them, so a stale marker shows and
a criterion like that one keeps its `GAP`.

**A citation is for the criterion's text.** `spec-trace` checks that a cited id exists, not that its
sentence states what the test proves — so the check is a review obligation: the implementing session
reads each criterion against its assertion before citing it, and the checks read them again. A test
cited for behaviour the criterion does not state is a spec not carried, the same defect as a
criterion reworded on a page without its `MODIFIED` delta line. **An instruction to cite is not
evidence the citation fits** — an amendment once named the wrong test for a criterion and the session
cited it unread. And any claim that names a test as its guarantee, in a comment, a doc or a
checkpoint, is read from that test's **call sites**, not its name: a test that feeds one of two
parsers pins one parser, whatever it is called.

**Id holes.** A hole that opens before the page is published — a leaf dropped for want of a
reachable mutation, in the same PR — is closed by renumbering the leaves below it and their
citations in one pass over the page and the tests. After publication a hole is never renumbered,
because a consumer may cite the id: it is named in a change doc instead, with a `REMOVED` delta
line, which is what `spec-trace` looks for.

## Change docs — `docs/changes/`

Everything that changes **specified behaviour** goes through the lane its size names (§ Lanes); the
corpus stays lean current-truth and the narrative — why, alternatives, drift — lives with the
change. A feature-sized change scaffolds with `spec-change.ps1 new <slug>` and fills
[`changes/_template.md`](changes/_template.md)'s sections; tests cite the delta's ids (`spec-trace`
folds `in-flight` deltas in); every Spec-delta line is distilled into its target page; then
`spec-change.ps1 archive <slug>` — all in one PR by default.

Statuses: `proposed` (reviewed-but-not-started; `spec-trace` ignores it) → `in-flight` (first
implementation commit flips it; deltas now demand tests) → `archived` (moved to `changes/archive/`,
only after every delta line is distilled — the archive command refuses otherwise). `parked` for an
accepted-but-blocked doc, with `blocked-on:` naming why.

Delta grammar — one line per id, targets **repo-root-relative**:

```
<ADDED|MODIFIED|REMOVED> <ID> -> docs/specs/<page>.md : <payload>
```

`ADDED`/`MODIFIED` payload is the EARS text; `REMOVED` payload is the reason. The `ID` must be
greppable in the target after distill. Divergence discovered during implementation goes to the doc's
**Drift checkpoints**, never inline into a spec page (`spec-lint` warns on narrative markers added
to the corpus).

**A criterion's text on the page and on its delta line are one text.** Reword one, reword the other
in the same commit — a `MODIFIED` line for a criterion already delta'd as `ADDED`. `spec-change.ps1
archive` refuses a delta whose text the page no longer carries, and one change reworded a criterion
on the page with no delta line at all, so page and delta disagreed until review. A criterion the
delta declares unreachable carries its `GAP: <reason>` marker **on the delta line**; `spec-trace`
honours it there exactly as on a page.

**The archive commit is the last commit that touches the page or the delta.** A fix that lands after
it carries its criterion, its delta line and, where the round owes a consolidation map, its map row
in the same commit and re-runs the gate against a slug-renamed copy placed back under
`docs/changes/` (the archived file cannot be archived twice without doubling its relative links) —
or it waits for the next round. One change shipped a consumer-visible refusal in its last commit
with none of the three, and the review found it by reading `git show --stat` of the head against the
doc's own claim of a "second half".

## Lanes — how a change gets made

Three lanes, chosen by size at the **start** of the work, from three questions: does it change
specified behaviour · does it cross an area · is a design point open. No to the first is an ordinary
PR with the repo's ordinary review. One area and no open design point is **lane 1**; an open design
point, a second area, or behaviour reshaped rather than corrected is **lane 2**; a whole area brought
to current truth, or a design open across an area's whole surface, is **lane 3** — the `pass` skill.

Why lane 3 splits the roles at all: across one migration's fourteen passes the implementing sessions
ran at 2–8 MB of transcript and compacted once between all of them, while the coordinating sessions
ran to 28 MB and compacted up to four times each. Context depth degrades exactly the disciplines the
checks exist to enforce — so in lane 3 the coordinator does not implement, and the session that
implements does not work its own review findings.

### Starting a lane-1 or lane-2 session

Two ways in, and the lane does not decide which. **Inline**, when the operator is driving the session
that will do the work: the triage above is the first thing done and its answer is said out loud, so a
wrong lane is cheap to correct. **Dispatched** with `/vion-dispatch:spawn`, when the work is handed
to a fresh session: the brief is written first, and the session reads it and starts without asking.
Lane 3 only ever dispatches, because its coordinator does not implement.

A lane-1 or lane-2 brief is short — it is a pointer, and this file plus `CLAUDE.md` carry what it
would otherwise restate. It names the REPORT sections it needs. Two carry something this repo
defines when a brief names them: `Gates` is the pasted `/check` output
([`.claude/commands/check.md`](../.claude/commands/check.md)), and `Review` is the round's findings
and what was done with each — the accepted ones, with their reasons, are what the PR body's
*Reviewed at* line carries ([`.github/pull_request_template.md`](../.github/pull_request_template.md)).

**A STOP is where the operator decides, and the brief names it before the session starts.** A task
with no STOP line is reviewed on its PR and nowhere earlier. A session that reaches a STOP pauses
there — a dispatched one with a `partial` REPORT — and continues when it is answered; a session that
finds a decision its brief did not name as a STOP says so rather than deciding quietly.

**A count in a brief or a task line is a hypothesis.** Re-derive every number — files, folders,
suites — before acting on it, and record the correction where the work is recorded: the change doc's
*Drift checkpoints* in lanes 2 and 3, the PR body in lane 1. Four of one programme's phase-1 task
lines carried wrong counts, and one of them was wrong in the way that re-scopes a task.

**A brief carries intent; a number is in it only when it serves that.** What the session must
understand is what the work is for and where its edges are — a count is scaffolding, and a brief
padded with numbers has buried the point it exists to make. So: write the number only if the session
would act differently for a different value, and then **paste the command that produced it**. If it
is not worth a grep, leave it out or mark it `[assumed]` and say what to check. **Never carry a count
in from a subagent's summary** — an inventory subagent's counts have reached briefs as facts and cost
a second subagent to undo, when the grep that refutes them takes seconds. The `[assumed]` half is the
cheap half and it works: a task line asserting a command needed a new scope marker was flagged rather
than believed, and the scope turned out to exist already.

### Lane 1 — fix-sized

The default, and most backlog items. No change doc: the PR carries the fix, its test proven red
against the pre-fix code, and the edit to the touched spec page **in the same commit set** — a page
edit riding a fix is not change narrative, it is the distill. The review round runs with the touched
page as the spec (`/vion-git:pr <page>`). A fix that turns out non-local or design-bearing while you
implement it **stops and says so**: it becomes lane 2, never a silent absorption. A "bug" that is
really a feature band never rides a fix.

### Lane 2 — feature-sized

New or reshaped specified behaviour. One change doc, one implementing session. The review round runs
with the change doc as the spec (`/vion-git:pr <change doc>`). **Two-phase self-fires:** when the
kickoff leaves design points open, or the doc mints decisions beyond what the operator ratified,
STOP after the change doc for ratification before writing code. Pre-classify every open point:
(a) ratified — cite; (b) decide-and-document; (c) propose-and-wait.

The change doc is **implementer-owned**: the session flips it `in-flight`, appends Drift checkpoints,
distills, archives. No report-back block — the PR is the report.

### Lane 3 — a whole area in one round

The `pass` skill. Its recipe is that skill's, not this file's, so that the amendment every pass owes
it is one diff in one place. What a pass here owes beyond it is § What a pass in this repo adds.

## Routing — where work enters

- **Consumer feedback** → the `dale-sdk-feedback` skill → VION-62.
- **Multi-repo work** → architecture `/spec` → `/implement`, report-back included. Briefs to this
  repo point at spec pages and oblige a change doc when specified behavior moves — they do not
  restate design.
- **Single-repo work** → starts here: fix-sized straight to a PR, feature-sized as a change doc.
  When architecture's `/fix` picks a single-repo dale-sdk item, its brief is the issue key +
  constraints; the design lives in the change doc.
- **Engineering findings** — what a lane-3 extraction, a review or any other reading turns up →
  intended behavior into the spec page; small area-local defects fixed in the same PR; GAPs as
  marked rows on the page; the rest a draft Jira item under VION-62 in the PR body's `Draft items`
  section, which the operator files or drops. A draft takes the `dale-sdk-feedback` skill's shape.

## Gates

The three the plugin ships, then dale-sdk's own. Why each of dale-sdk's exists is its script's
header.

| What | Where | Fails when |
| --- | --- | --- |
| `spec-lint.ps1` | `spec-gates.yml`'s `vion-sdd` job + `/check` | a malformed or escape-hatch AC; an unknown EARS label; a bare foreign id or a citation naming an uncited repo; change-doc frontmatter or lifecycle broken (`archived` outside `archive/`, unknown status, `parked` with no reason); `-Diff <ref>` warns on narrative added to the corpus (`-Strict` fails) |
| `spec-trace.ps1` | `spec-gates.yml`'s `vion-sdd` job + `/check` | any owned id on a `trace: enforced` page (or an `in-flight` delta) with no quoted-literal test reference; a marked page parsing zero ids; the `tests:` globs matching zero files; an id-sequence hole that no change doc records with a `REMOVED <id> ->` delta line — prose naming the id is not that record, and neither is the `ADDED` line that minted it |
| `spec-change.ps1 archive` | on demand | any Spec-delta line not distilled into its target; an `ADDED`/`MODIFIED` line whose EARS text the target's declaring bullet no longer carries; a delta minting an id outside `owns:` |
| `scripts/test-style-lint.ps1` | `spec-gates.yml` + `/check` | a test citing a spec id with an article in its name or no Triple-A markers ([`testing-conventions.md`](testing-conventions.md) §12, §13); a test file citing an id a committed scenario also claims, with no "Cross-tier" clause (§17) |
| `scripts/doc-comment-lint.ps1` | `spec-gates.yml` + `/check` | a C# doc-comment block carrying more than one `<summary>` ([`sdk-surface-conventions.md`](sdk-surface-conventions.md) § 2) |
| `scripts/pragma-reason-lint.ps1` | `spec-gates.yml` + `/check` | a directive suppressing a DALE diagnostic with no reason reaching it ([`testing-conventions.md`](testing-conventions.md) § 8); zero sites parsed |
| `scripts/bom-lint.ps1` | `spec-gates.yml` + `/check` | a file of the kinds that never carry one here holding a UTF-8 byte-order mark or a NUL byte; zero files, or zero `.cs`, reaching the scan |
| `scripts/packed-msbuild-lint.ps1` | `spec-gates.yml` + `/check` | an MSBuild file packed under `build/`, `buildTransitive/` or `buildMultiTargeting/` that is not well-formed XML; a packed `Include` resolving to no file; zero project files, packed entries or resolved files |
| `scripts/sweep-residue-lint.ps1` | `spec-gates.yml` + `/check` | prose carrying a scripted sweep's residue — an orphaned `()`, a doubled space, a line ending on `(` |
| `scripts/self-reference-lint.ps1` | `spec-gates.yml` + `/check` | a Markdown table cell that is `this PR`, `this commit`, `this branch` or `the current PR`; zero Markdown files |
| `scripts/run-script-tests.ps1` | `spec-gates.yml` on every push to `main`, and on a pull request that changes a script, that workflow or a `.csproj` + `/check` | a `scripts/*.tests.ps1` self-test failing, or a gate script with neither a self-test nor an exemption-with-reason |
| `journal-lint` | `spec-gates.yml`'s `journal-lint` job + `/check`, from a shared-workflows checkout | a fragment in `docs/process-journal/` off the grammar its `README.md` states, or the folder's shape broken; the action owns the rules |
| `scripts/release-smoke.ps1` | `publish.yml` on a release tag, between the pack and either push; `release-smoke.yml` on dispatch; on demand ([`releasing.md`](releasing.md)) | the packed set failing to scaffold, build, test and pack a new project or to build `libraries/Vion.Diagnostics`; a `Vion.Dale.*` package restored from anywhere but the packed set; a `0.0.0*` version |

`spec-gates.yml` runs on every PR (it is file scans only — no build), because `publish.yml` skips its
build for a docs-only change and a docs-only PR must still be gated.

**Run them with `pwsh -File scripts/check.ps1`, or the `/check` command.** It derives them from
`spec-gates.yml` rather than repeating the table above — a gate added to the workflow with no local
invocation fails `check.ps1` instead of quietly not running — and prints one pass/fail line per gate,
carrying that gate's own summary line. `-Build` and `-Test` add the solution build and test suite,
off by default because this suite's whole point is being cheap enough to run on every change.
`-CiShape` runs in the Linux runner's shape: the repository's dot-directories hidden, a scan for path
literals in `scripts/*.ps1` whose casing disagrees with the git index, and `-p:Version=0.0.0-ci.1` on
the build and test.

Two gates' scripts live in other repositories, and `check.ps1` runs them from a checkout beside this
one. The plugin's `spec-lint` and `spec-trace` run from `../architecture`, or `-ArchitectureRoot`:
with no checkout they are **skipped**, because the pull request's `vion-sdd` job runs them; they are
partial when the plugin in the checkout is not the one at the origin's `main`, the one CI runs.
`journal-lint` runs from `../shared-workflows`, or `-SharedWorkflowsRoot`: with no checkout it
**fails**, because a skipped journal check reads as a green run; it is partial when the checkout is
not at the commit the workflow's ref names.

A gate that cannot run at the desk is reported **skipped** with the reason, and one that ran without
part of itself is reported **partial** naming what did not run. Neither is a pass. `spec-lint` is the
standing partial case: without the base ref fetched its narrative rule would compare nothing and
still report OK, so run it without `-Diff` and say so rather than either skipping its other rules or
reporting a vacuous green.

## What a pass in this repo adds

What a lane-3 round here owes beyond the `pass` skill's recipe: the places that recipe leaves to the
repo, filled with this repo's artifacts and tools.

**Extraction and the gate**

- The observability discriminator that decides what is a row is
  [`testing-conventions.md`](testing-conventions.md) §9; log text is not observable (§15).
- The published-surface manifest the self-check reads is `docs/snapshots/publicapi-manifest.json`,
  by the area's assemblies; an area that is `[InternalApi]` by design has no entries, and says so.
- A second opinion on the `⚠` rows opens this SDK's readers where they are on the machine: the
  private runtime, the gateways, and the first consumer.

**Implementation**

- The test side of the mutation discipline — when a surviving mutation indicts the fixture, and
  what a merged `[DataRow]` owes — is [`testing-conventions.md`](testing-conventions.md) §11.
- The suite comes to [`testing-conventions.md`](testing-conventions.md) §9–17: ids cited per §17,
  no assertion on log calls per §15.
- The naming and Triple-A gate is `scripts/test-style-lint.ps1`; run it before the REPORT.
- `scripts/doc-comment-lint.ps1` fails the double `<summary>` a fixture inserted above an attribute
  block leaves; re-read the insertion point for the bare half.
- A change that reaches the DevHost SPA is demonstrated with `devhost-smoke` Tier 2 on a live host;
  where no committed fixture shows it, the SmokeHost grows the member that can
  ([`devhost-conventions.md`](devhost-conventions.md) §1, [`testing-conventions.md`](testing-conventions.md) §6).
- A scripted sweep runs `scripts/sweep-residue-lint.ps1`, then re-reads the touched sentences.

**The REPORT's gates**

- Where a fixture asserts a build-time literal, the CI-shape run is `scripts/check.ps1 -CiShape -Test`
  ([`testing-conventions.md`](testing-conventions.md) §8).
- Stryker.NET runs only where the test project references a single mutatable project; it is never a
  gate and never a score.

**The check prompts' fill-ins**

- The completeness critic's discriminator is `testing-conventions.md` §9 and §15; its data rows are
  `[DataRow]`s; its citation sample spans the test roots and the scenario files; it may run
  `spec-trace` and `dotnet test` on one test project.
- The adversarial review runs on Windows with `pwsh -NoProfile -File scripts/<x>.ps1`, reviews
  against [`review-checks.md`](review-checks.md) and `testing-conventions.md` §9–17, reads
  `sweep-residue-lint` and `spec-lint -Diff origin/main`, and checks journal lines against
  `/vion-improve:journal`'s format.
- Its rewritten-suites priority adds the cross-tier clause where a scenario also claims the id, no
  log assertions, and merges as `[DataRow]`s.
- Its consumer-surfaces priority adds `docs/snapshots/publicapi-manifest.json`: unchanged, or
  changed only as intended.
