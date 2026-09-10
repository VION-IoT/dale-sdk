# Spec-driven development — how to run it

The operational playbook for this repo's spec corpus, its change docs, and the three lanes work runs
in. Rationale and the adoption decisions (D1–D11): the archived change doc
[`changes/archive/2026-09-01-sdd-process.md`](changes/archive/2026-09-01-sdd-process.md). The lanes
and what the migration's fourteen area passes measured about them:
[`changes/2026-09-07-sdd-closeout.md`](changes/2026-09-07-sdd-closeout.md) `D3`. Modeled on
`logic-block-libraries/docs/spec-process/SPEC-PROCESS.md`, adapted to an SDK: the unit is not a
logic block but a **contract**.

## The corpus — `docs/specs/`

Current-truth pages: present tense, terse, written in published-surface vocabulary — a page's
evidence is the tests that cite its ids (the trace gate), never source references, so an
implementation change that leaves behavior intact never touches a page. What the SDK guarantees
**today** — no history, no incident narrative, no design alternatives, no rosters of today's
instances (those live in git, the archived change docs, and the process journal). One page per
**area**:

> **An area is one contract with its own anchor artifact** — a schema, manifest, golden file,
> diagnostic registry, or committed scenario set that already pins it mechanically.

| Group | Page (anchor) | Area code | Tier |
| --- | --- | --- | --- |
| Authoring contracts | authoring surface + analyzer registry (44 live DALE descriptors; ids run to DALE046 — DALE006 and DALE029 are retired) | `ANLZ` | A |
| Authoring contracts | service properties & measuring points incl. emission policy (throttler semantics, dual-annotation) | `EMIT` | A |
| Authoring contracts | contracts & provider faces | `BIND` | A |
| Authoring contracts | instantiation & config-time gating | `GATE` | A |
| Wire contracts | introspection JSON + identifier stability (golden files, packed-artifact rule) | `INTRO` | A |
| Wire contracts | scenario & topology files + stepping semantics (scenario schema, committed scenarios) | `SCEN` | A |
| Wire contracts | DevHost control API ([`specs/devhost-control.md`](specs/devhost-control.md)) | `CTRL` | A |
| Wire contracts | plugin loading ABI (`[DaleSharedAssembly]`, ALC rules) | `PLUG` | A |
| Wire contracts | CLI surface (help snapshot) | `CLI` | B |
| Runtime semantics | block lifecycle (start/stop ordering, teardown delivery) | `LIFE` | A |
| Runtime semantics | Modbus family — Core binding model, TCP client + server, RTU, link policy (link verdicts, socket lifetime; anchors: modbus-smoke, the Link/Connection structs, committed scenarios) | `MODB` | A |
| Contract families | DigitalIo / AnalogIo | `IO` | B |
| Contract families | the five test kits ([`specs/testkit.md`](specs/testkit.md)) | `TKIT` | B |
| Contract families | Http | `HTTP` | B |

Plus [`specs/_invariants.md`](specs/_invariants.md) (`SYS-` ids, cross-cutting rules pages cite
instead of restating). **Tier A** pages carry EARS acceptance criteria with ids and the trace gate;
**Tier B** pages were scoped as prose, and all four (`CLI`, `IO`, `TKIT`, `HTTP`) found enough
observable behaviour to carry ids and the trace gate as well — the tier records the depth the
extraction reached, not whether the page is traced, and every roster page is traced today.
**Tier C** is exempt: examples, templates, `Vion.Dale.DevHost.Web` SPA internals, the SmokeHost
fixture, and **first-party libraries** (`libraries/` — consumers of the SDK shipping from this repo,
not SDK contract; if one ever warrants specs, it gets lbl-style block specs in place, outside this
corpus).

### Page frontmatter — the trace ratchet

A page opts into the trace gate with frontmatter:

```markdown
---
trace: enforced
---
```

The marker is a **ratchet**: set when the area's page lands, never removed. `spec-trace.ps1` fails
if a marked page declares zero ids (the parse died — anti-vacuous floor) or if any declared id has
no referencing test.

## IDs & EARS

`SYS-<AREA>-NNN` (invariants) · `AC-<AREA>-NNN.M` (acceptance criteria, `.M` leaves under a `NNN`
umbrella). Area codes: the roster above, uppercase alphanumerics. Every AC is one EARS sentence
(ubiquitous / WHEN / WHILE / IF-THEN / WHERE) with a `SHALL`. No `should`, `fast`, `performant` —
`spec-lint.ps1` rejects them.

Every AC carries one of five labels, decided by its keyword — `(Ubiquitous)` no keyword;
`(Event-driven)` WHEN; `(State-driven)` WHILE; `(Unwanted)` IF … THEN; `(Optional)` WHERE. A
criterion combining keywords takes the label of its first keyword. `spec-lint` rejects any other
label.

**Minting is a consolidation.** Extraction and specification want different granularities: a
behavior table over-produces rows on purpose, and the page states one criterion per **rule**, with
the fields, tokens or sites the rule ranges over as its test's `[DataRow]`s — never one criterion per
field. A family of schema mirrors folds into the rule they mirror; a doc-comment defect is a fix
without a criterion; a behavior another page owns is cited there, never re-minted. Roughly half the
classified rows become criteria (230 rows became 135 in one area without losing one), so a
**consolidation map** — row → criterion, or row → the line saying why it mints nothing — goes in the
change doc: a row with neither is a blocker. **A `park` row appears in the map by name and is never
folded into a criterion**; a folded park is a lost ledger line, and one round wrote its ledger line
late for exactly that. No criterion's subject is a test suite: a page states what a consumer
observes, and test discipline is [`testing-conventions.md`](testing-conventions.md)'s.

An AC is **covered** when its id appears as a quoted string literal in a test artifact:

- MSTest: `[TestProperty("spec", "AC-EMIT-001.1")]` on the test method
- xunit.v3: `[Trait("spec", "AC-EMIT-001.1")]`
- a committed scenario: `"specs": ["AC-SCEN-003.1"]` in the `*.scenario.json`

`spec-trace` scans every `*.Test` directory in the repo (the xunit projects live nested under
`examples/`, `libraries/`, `templates/`) plus the SmokeHost. A mention in a comment or method name
does not count — the gate matches quoted `"AC-…"` literals, so an id belongs in exactly the three
forms above and never in any other string (an assert message or an expectation array carrying one
would bind by accident; read the ids off the artifact under test instead).
A bare umbrella id (`AC-EMIT-001`) is covered by any of its `.M` leaves. A test that pins an
implementation **premise** rather than a criterion cites no id by design and says so in its class
summary — the rule and its reason are [`testing-conventions.md`](testing-conventions.md) §17, and
`spec-trace` never sees such a test.

**GAP rows** — a declared requirement whose test does not exist yet — carry the `GAP` marker on the
declaring line, with the reason or Jira key: `` - `AC-PLUG-004.1` (Event-driven): WHEN … THE SYSTEM
SHALL …. GAP: test pending (VION-nn) ``. `spec-trace` exempts them from the orphan check and
reports the count instead — the in-repo backlog stays visible without reddening CI. Removing the
marker is how a landed test re-arms the gate for that id. If the reason is "no observable", the row
is not a requirement at all: drop it rather than mark it. Two mechanics of the marker cost a round
each:

- **The marker binds only on the id's own line.** A declaring bullet keeps its id and its `GAP` tail
  on one line; a wrapped bullet with `GAP` on its second line fails the gate with no hint why.
- **Any acceptance id on a line without a `GAP` marker is read as a declaration.** Cite a
  neighbour's GAP'd id by family (`AC-ANLZ-018.*`, never in full) and refer to your own GAP'd
  criteria by description in the prose below the bullet, never by number — four of one page's eleven
  GAPs were un-GAP'd by their own explanatory prose on the first run.

Because the gate un-GAPs an id the moment any test carries it, **a criterion no test in the suite can
reach is `GAP` however many tests name it**: one about a CI-only gate read as proven on two
option-description tests.

An id proven by **both** a unit test and a scenario states which half each tier owns in the test
class summary ("Cross-tier" clause); `spec-trace` warn-notes files missing it.

**A citation is for the criterion's text.** `spec-trace` checks that a cited id exists, not that
its sentence states what the test proves — so the check is a review obligation: the implementing
session reads each criterion against its assertion before citing it (and lists the pairs in its
REPORT), and the checks read them again. A test cited for behavior the criterion does not state
is a spec not carried, the same defect as a criterion reworded on a page without its `MODIFIED`
delta line. **An instruction to cite is not evidence the citation fits** — an amendment once named
the wrong test for a criterion and the session cited it unread. And any claim that names a test as
its guarantee, in a comment, a doc or a checkpoint, is read from that test's **call sites**, not its
name: a test that feeds one of two parsers pins one parser, whatever it is called.

**Id holes.** A hole that opens before the page is published — a leaf dropped for want of a
reachable mutation, in the same PR — is closed by renumbering the leaves below it and their
citations in one pass over the page and the tests. After publication a hole is never renumbered,
because a consumer may cite the id: it is named in a change doc instead, which is what `spec-trace`
looks for.

## Change docs — `docs/changes/`

Everything that changes **specified behavior** goes through the lane its size names (§ Lanes); the
corpus stays lean current-truth and the narrative — why, alternatives, drift — lives with the
change. A feature-sized change scaffolds with `pwsh scripts/spec-change.ps1 new <slug>` and fills
[`changes/_template.md`](changes/_template.md)'s sections; tests cite the delta's ids (`spec-trace`
folds `in-flight` deltas in); every Spec-delta line is distilled into its target page; then
`pwsh scripts/spec-change.ps1 archive <slug>` — all in one PR by default.

Statuses: `proposed` (reviewed-but-not-started; `spec-trace` ignores it) → `in-flight` (first
implementation commit flips it; deltas now demand tests) → `archived` (moved to
`changes/archive/`, only after every delta line is distilled — the archive command refuses
otherwise). `parked` for an accepted-but-blocked doc, with `blocked-on:` naming why.

Delta grammar — one line per id, targets **repo-root-relative**:

```
<ADDED|MODIFIED|REMOVED> <ID> -> docs/specs/<page>.md : <payload>
```

`ADDED`/`MODIFIED` payload is the EARS text; `REMOVED` payload is the reason. The `ID` must be
greppable in the target after distill. Divergence discovered during implementation goes to the
doc's **Drift checkpoints**, never inline into a spec page (`spec-lint` warns on narrative markers
added to the corpus).

**A criterion's text on the page and on its delta line are one text.** Reword one, reword the other
in the same commit — a `MODIFIED` line for a criterion already delta'd as `ADDED`. `spec-change.ps1
archive` refuses a delta whose text the page no longer carries, and one change reworded a criterion
on the page with no delta line at all, so page and delta disagreed until review. A criterion the
delta declares unreachable carries its `GAP: <reason>` marker **on the delta line**; `spec-trace`
honours it there exactly as on a page.

**The archive commit is the last commit that touches the page or the delta.** A fix that lands after
it carries its criterion, its delta line and its map row in the same commit and re-runs the gate
against a slug-renamed copy placed back under `docs/changes/` (the archived file cannot be archived
twice without doubling its relative links) — or it waits for the next round. One change shipped a
consumer-visible refusal in its last commit with none of the three, and the review found it by
reading `git show --stat` of the head against the doc's own claim of a "second half".

## Lanes — how a change gets made

Three lanes, chosen by size at the **start** of the work, from three questions: does it change
specified behaviour · does it cross an area · is a design point open. No to the first is an ordinary
PR with the repo's ordinary review. One area and no open design point is **lane 1**; an open design
point, a second area, or behaviour reshaped rather than corrected is **lane 2**; a whole contract
brought to current truth, or a design open across an area's whole surface, is **lane 3**.

Why lane 3 splits the roles at all: across the migration's fourteen passes the implementing sessions
ran at 2–8 MB of transcript and compacted once between all of them, while the coordinating sessions
ran to 28 MB and compacted up to four times each. Context depth degrades exactly the disciplines the
checks exist to enforce — so in lane 3 the coordinator does not implement, and the session that
implements does not work its own review findings.

### Starting a lane-1 or lane-2 session

Two ways in, and the lane does not decide which. **Inline**, when the operator is driving the
session that will do the work: the triage above is the first thing done and its answer is said out
loud, so a wrong lane is cheap to correct. **`/vion-dispatch:spawn`**, when the work is handed to a
fresh session: the brief is written first, and the session reads it and starts without asking.
Lane 3 only ever dispatches, because its coordinator does not implement (§ 2 Dispatch), and its
brief is the one with a recipe of its own (§ 1 The brief).

A lane-1 or lane-2 brief is short — it is a pointer, and this file plus `CLAUDE.md` carry what it
would otherwise restate. Its front matter differs from lane 3's in two places:

- `sections:` is `Deviations, Questions, Friction, Affects others, Gates, Review` — the plugin's
  four plus the two this repo owes. `Gates` is the pasted `/check` output
  ([`.claude/commands/check.md`](../.claude/commands/check.md)); `Review` is the round's findings
  and what was done with each, the same text the PR body carries, in the shape
  [`vion-code-review.md`](../.claude/commands/vion-code-review.md) § 7 sets out.
- `pr:` is `expected`. Only lane 3 stops before the PR, because there the REPORT is what the
  operator reads first.

**A STOP is where the operator decides, and the brief names it before the session starts.** A task
with no STOP line is reviewed on its PR and nowhere earlier. A session that reaches a STOP emits a
`partial` REPORT there and continues when it is answered; a session that finds a decision its brief
did not name as a STOP says so rather than deciding quietly.

**A dispatched worker is steered** — `steer: yes` — whether or not a STOP is expected (operator,
2026-09-09). The unsteered mode suppresses `--remote-control`, so the session does not appear in the
app or on the phone, and where the launch carries a stored token that cannot be turned on afterwards
— the tab is then the only surface the session has for its whole life.

**A count in a brief or a task line is a hypothesis.** Re-derive every number — files, sites, ledger
entries, suites — before acting on it, and record the correction where the work is recorded: the
change doc's *Drift checkpoints* in lanes 2 and 3, the PR body in lane 1. Four of the SDD closeout's
phase-1 task lines carried wrong counts, and one of them was wrong in the way that re-scopes a task.
Lane 3 adds a coordinator-side check on top of this — a fresh reader over the brief before dispatch
(§ 1 The brief) — which is a second reader, never a substitute for the session's own re-derivation.

### Lane 1 — fix-sized

The default, and most backlog items. No change doc: the PR carries the fix, its test proven red
against the pre-fix code, and the edit to the touched spec page **in the same commit set** — a page
edit riding a fix is not change narrative, it is the distill. Before the PR: `/cleanup` once and a
**fresh-context read-only review subagent** (`/vion-code-review branch`, with the touched page as the
spec), its findings applied or accepted, and the round written into the PR body. A fix that turns out non-local or design-bearing while you implement it **stops and says so**:
it becomes lane 2, never a silent absorption. A "bug" that is really a feature band never rides a
fix.

### Lane 2 — feature-sized

New or reshaped specified behaviour. One change doc, one implementing session. Before the PR, the
same round lane 1 runs: `/cleanup` once and a **fresh-context read-only review subagent**
(`/vion-code-review branch`, with the change doc as the spec), its findings applied or accepted, and
the round written into the PR body. **Two-phase self-fires:** when the kickoff leaves design points
open, or the doc mints decisions beyond what the operator ratified, STOP after the change doc for
ratification before writing code. Pre-classify every open point: (a) ratified — cite;
(b) decide-and-document; (c) propose-and-wait.

The change doc is **implementer-owned**: the session flips it `in-flight`, appends Drift
checkpoints, distills, archives. No report-back block — the PR is the report.

### Lane 3 — a whole contract in one round

The area pass generalised: a coordinator, a brief checked before dispatch, one implementing session,
two fresh-context checks, and a **fresh** session for the fix-up. What follows is what each role
owes.

A round the operator discards is **rerun, never hand-patched**: the correction goes into this section
and the branch dies with the attempt. Hand-fixing a discarded round's output leaves the next round to
repay the same debt.

#### 1. The brief

Written by `/vion-dispatch:spawn` from the plugin's `templates/brief.md`, so it carries the envelope's
front matter (`key`, `unit`, `sections`, `pr`, `branch`, `deps`, `steer`) — lane 3's `pr:` is
`stop-before`, because the REPORT comes first and the PR opens on the operator's go. It lands at
`C:\_gh\architecture\.claude\briefs\brief-<slug>-dale-sdk.md` — the gitignored home the
launcher's permission model expects — and is a **pointer, not a restatement**: the change-doc path;
"decisions and review resolutions are binding; contradictions go to Drift checkpoints, not silent
divergence"; the PR shape; every open point pre-classified; house discipline (branch, tests cite ids,
`/cleanup` once pre-PR, `/vion-code-review branch` before the PR); and a read-only note per
additional directory naming the **specific** files to look things up in.

Scope — and test scope — is stated as **folders, projects and descriptor ranges the session
enumerates itself**, never a transcribed file, descriptor or suite list. Twice, the brief's counts
were the thing that was wrong; the brief that listed test suites missed three in the area's own
project. The session counts them and records a Drift checkpoint where the brief differs. An anchor
kind that turns out empty — an attribute with no named parameters — is a Drift checkpoint, not a row.

**A count in a brief is a hypothesis until a second reader confirms it.** Before the launch line a
fresh-context Opus `Explore` subagent reads the brief against the code — every file, folder, count,
ownership claim and omission, reported as corrections — and the brief is rewritten before dispatch.
Four minutes and one subagent: one brief carried thirteen wrong claims and nine omissions until it
ran, and an inventory claiming thirty-nine analyzers with the severities transposed lost eighteen
claims the same way. A premise about what the build decides — which projects pack, what an assembly
carries, what a property evaluates to — comes from the build system's evaluation, never from a regex
over its inputs.

**A hedge in a brief is a STOP.** *"Appears to … — verify"* has named an assumption; when it fails,
record the deviation in the change doc and ask. One session improvised "resolve by evaluating" when
the promised AST was not there — evaluation short-circuits, so the check never reached the name it
existed to find.

#### 2. Dispatch

Dispatched to a fresh session with `/vion-dispatch:spawn`, exactly as `/implement` and `/fix` dispatch
— **never reimplement the launch inline**; the launcher encodes hard-won constraints. The command
writes the brief, runs the pre-flight, and launches: a dirty tree, a wrong branch or another live
session in the checkout is refused with the standard three-answer question (*Worktree*, *Wait*,
*Override*), and the launched session posts an idle notice when it goes quiet. The permission mode is
the launcher's job — it reads the target repo's own `defaultMode` — so nothing is hand-added here.
Ratification round-trips still use the launcher's `-AmendFile` plus the printed `-SessionId`; the
two-phase STOP maps onto it directly.

Model rubric: Opus unless the slice is mechanical (then Sonnet); High effort for
design-bearing/correctness-sensitive work, Medium for a contained change.

The mechanics live in
[`architecture/plugins/vion-dispatch/README.md`](https://github.com/VION-IoT/architecture/blob/main/plugins/vion-dispatch/README.md)
(§ launch-session.ps1 for the parameter set and the composition constraints, § Pre-flight for what is
detected and refused), and the VION procedure around them in
[`architecture/runbooks/session-orchestration.md`](https://github.com/VION-IoT/architecture/blob/main/runbooks/session-orchestration.md).
Where a path is needed it is `plugins/vion-dispatch/scripts/launch-session.ps1`; the old
`architecture/scripts/launch-session.ps1` is a deprecated forwarding shim and is never cited.

#### 3. Extract before you specify

Reading and one document: no production edits, no test edits, no fixes, no Jira writes. The
deliverable is the behavior table in the change doc, status `proposed`.

**Anchor inventory first.** Enumerate the area's machine-readable surface mechanically — the
attributes, descriptors, schema fields and manifest entries — and list it in the change doc. That
list is the completeness checklist for the sweeps; a sweep without it starts from a blank page and
misses silently. For every attribute, compare its `AttributeTargets` with the targets its readers
actually walk: a target nothing reads is a row (one area had that shape twice — a method target and
a property target that compiled, emitted nothing and warned about nothing).

Then four sweeps:

- **Statement sweep** — walk the scope statement by statement, not branch by branch
  ([`testing-conventions.md`](testing-conventions.md) §9's observability discriminator decides what
  is a row). Every claim comes from code read **this session**; old prose and comments are hints at
  best.
- **Consumer sweep** — the statement sweep finds what the code *does* and misses what a consumer
  *depends on*. Walk every in-repo consumer, then read the area once more as the author of a plugin,
  library or topology would: what the *layout* of my input has to be; who *owns* an instance and what
  follows (dependency resolution, lifetime, identity); what happens when one of my files is *broken*
  (tolerated, aborted, what state is left); which *mechanics* of a marker, attribute or contract my
  declaration has to satisfy. An example that references a published package is a consumer to read,
  not a test bed — it cannot prove a same-PR SDK fix.
- **Edge-value sweep** — for every knob, token or input the area accepts, walk the edge values and
  write what each *observably* does: negative, zero, empty, whitespace, non-finite (`NaN`, infinity),
  out of range or overflow, an equivalent spelling (`"250"` vs `"250ms"`, case, culture), a value the
  compile-time validator accepts that the runtime then ignores or inverts. The pass that skipped this
  sweep had six of its ten critic misses here.
- **State-interaction sweep** — where the area is a multi-step decision (a gate, a resolution chain,
  a lifecycle), take each path once more *while another state is pending*: a drop while a value is
  held, a stop while a flush is armed, a reset while a run is active. Each combination whose outcome
  differs from the naive reading is a row — these leave a consumer stale forever and never show in a
  statement walk. **A behaviour stated for one side of a symmetric mechanism is a prompt to read the
  other**: both of one pass's critic misses sat beside a row stated for the other side (a topic
  pinned on the publish side and read live on the receive side; a contract message ignored by the
  input handler and not by the output handler). And where an initialization can **fail closed**, walk
  every later message the instance can still receive — a stop, a snapshot, a restore, a write, a
  second configuration — and write a row wherever the answer differs from a healthy instance's: four
  of one pass's nine critic misses were exactly these.

**The table** is the deliverable — one row per observable behavior, all six columns required:

| # | Behavior (EARS) | Evidence | Test today | Rec | Why |
|---|---|---|---|---|---|
| 1 | WHEN a plugin binds to an assembly marked `[DaleSharedAssembly]` THE SYSTEM SHALL resolve the shared instance. | `PluginLoadContext.cs:340` | `PluginLoadContextShould.…` | intended | the cross-plugin type-identity contract |

- **Behavior** — written for the **spec page**, so in **published-surface vocabulary only**: public
  types and members, attribute names, exception types, file and wire formats. No private method
  names, no `file:line`, no mechanism words a refactor would falsify. The altitude test is *could a
  consumer observe the difference?* — a sentence an implementation change would force you to reword
  is at the wrong altitude.
- **Evidence** — `file:line`, read, not recalled. This column is where implementation detail lives;
  it dies with the archived doc. **A probe is evidence only for the shape it ran:** record the
  probe's fixture shape as written (`get;` is not `get; init;`) and the surface it read (the
  definition view is not the live view) beside the result. A probe over a different shape than the
  row names is a guess wearing evidence's clothes — one session wrote off a correct reading on such
  a probe and paid three amendments for it.
- **Test today** — the existing test proving it, or `GAP`.
- **Rec** — one of five: `intended` (→ page) · `fix` (a defect, small and area-local → the criterion
  worded for the correct behavior, fixed in this round) · `park` (too big or too far-reaching → one
  line in `docs/specs/_findings.md`) · `out-of-spec` (implementation shape, not contract) ·
  `propose`, where the brief pre-classified a class of rows as propose-and-wait (a wire shape, a
  public member's semantics): the recommendation rides the row, implemented as a `fix` if the
  operator accepts it and written to the ledger as a `park` if not, so that neither value misstates
  it. One-line **Why** each. A row that *names a harm* names the consumer that suffers it by
  `file:line`, or it is a guess wearing evidence's clothes. A `park` argued from a member's
  *history* — added last, newer than its siblings — has no evidence column: recency is not a reason
  to treat a member differently (an operator overruled one such park; the fix was four lines and
  retired two special cases). **The code doing a thing is evidence of behavior, not of intent:** a
  surprising row gets `fix` or `park`, or the operator's explicit `intended` — never an `intended`
  that only means "this is what it does". A `fix`/`park`/`propose` rec is flagged `⚠` and gets a
  two-line failure sketch under the table; a `propose` sketch ends with the recommendation.

**Map every existing test in scope** to a row; tests mapping to no row go in an *unmapped tests* list
(merge or delete candidates for the implementation step).

**Self-check** against the anchor inventory and the PublicApi manifest's entries for the area's
assemblies (where the area has any — some are `[InternalApi]` by design): every member visited or
explicitly out of scope, with the counts stated. *Visited by a row* is not *covered by a criterion*,
so the self-check is re-read after minting: every classified `intended`/`fix` row maps to a criterion
id or to a checkpoint saying why not. Then the **reverse question, per anchor instance**: *which
observable behaviours have neither a row nor a criterion?* Counting rows against criteria never asks
it, and two behaviours reached a page that way — a lifecycle entry point, and the one silent success
a pass had left. Ask it of the change doc's own Reviewer's questions too: a question that describes
an observable and recommends its disposition is an observable someone noticed and nobody tabled.

Commit the change doc (status stays `proposed`), push, and STOP with exactly this report shape:
row/GAP/⚠/unmapped counts · the ⚠ rows verbatim · the unmapped-test list · the line *"Classify: reply
with row numbers to override, or 'accept recs' to take all recommendations."*

#### 4. The operator gate

The operator classifies. The coordinator may add a **second opinion on the ⚠ rows** first, and that
second opinion **reads the readers**: where a `Why` cell names a reader whose repository is on the
machine — the private runtime, the gateways, the first consumer — it opens that repository, because a
reader named from memory is a hypothesis about someone else's code. One second opinion found five
reader claims wrong and three of them changed a row's class: a runtime with no arm at all for the
message type the row said it dispatched, a marker the row called unread that the runtime's scan
reads, and a helper the row would route a discovery through that the runtime rejects in its own
comments. The test for a `propose` row is unchanged — a silent wrong outcome made visible, no
consumer depending on today's behaviour, small and area-local — but its second clause is answered by
reading, and a wire change whose every reader was read and found not to read the field is a `fix`
with a relay note, not a `park`.

No Jira is filed by default: `fix` rows are fixed in this round, `park` rows go to the ledger, and
Jira is for the finding the operator actively schedules. Do not implement without the
classification.

#### 5. Implement

Ids, minting and the archive rule are § IDs & EARS and § Change docs. Beyond them:

- **Write the mutation with the test, before the criterion counts.** For every criterion: the test,
  and the one mutation of the code under it that reddens *that test and no other claim*, run and
  observed. A criterion no mutation can redden is not a requirement — reword it to what *is*
  observable, merge it into the one it converges on, or drop it; never mint it. A criterion two
  guards enforce is **over-determined**: say so on its line, mutate both, and keep it. A behaviour
  that holds by accident of structure — two dictionaries that happen to be separate — has no
  reachable mutation until one line states the rule: write that line, then mint. The PR body carries
  the `test → mutation` list, one line each. The test side of this discipline —
  when a surviving mutation indicts the fixture, and what a merged `[DataRow]` owes — is
  [`testing-conventions.md`](testing-conventions.md) §11.
- **A window no seam constructs has no deterministic test.** A race between two adjacent statements
  on one thread and a pool continuation is **reported as a surviving mutation**, with the observable
  that *is* carried stated beside it: one drain race's fix is the order, and the carried observable
  is that a queued request is dropped, never run. An amendment item owing a test for a race names the
  observable it expects, so the session can report an absence instead of manufacturing a proxy.
- **Fix rows** get the test for the *correct* behavior, proven red against the pre-fix code — that
  red run is the defect proof; name it in the mutation list — then the minimal fix. Four disciplines
  a fix owes beyond its own lines, each paid for by a round:
  - a fix that makes previously inert inputs **live** re-checks **every validator** over those inputs
    (the change that unblocked measuring-point knobs left all six analyzers blind to them);
  - a fix that names a defect **shape** sweeps the shape, not the symbol — grep for the expression,
    not the one call site you noticed;
  - a guard added to a `switch` belongs to **every arm**: route every arm through one guard, or the
    arm you did not test skips it;
  - a fix that says *match rule R* enumerates R's properties — membership, naming, each declaration
    level — and covers every one in the same round (one change matched membership, then property
    naming, then class-level naming: three rounds for one rule).
- **A fix closes one path and opens its sibling.** Before the REPORT, re-run the state-interaction
  **and edge-value** sweeps over every fixed path's neighbours — the restart beside the double start,
  the duplicate reference beside the duplicate answer, the second map beside the first. Three of one
  pass's four critic misses sat exactly there, on paths the extraction had swept before the fixes
  existed; another's one miss was the offset three freshly guarded slices read from, an edge value
  beside the edges just fixed. **A behaviour the classification *adds* is a path nothing ever swept**
  — its other output mode, its options, its JSON document — and three of one pass's six critic misses
  sat on exactly such a path.
- **A deviation from the classification's letter is checkpointed with the criterion it rests on and
  the test that carries that criterion.** A reason is not a proof: one session deviated because a
  synchronous observe would block teardown, and the criterion that reason rested on had six citations
  and no test constructing the case.
- **Size guard.** If a fix turns out non-local or design-bearing while you implement it, STOP and
  report — it becomes a `park` row or its own change doc, never a silent absorption.
- **The page** states current-truth prose plus the AC declarations, frontmatter `trace: enforced`.
  Prose states rules, never rosters: a list of today's instances drifts, "grep-enumerable" does not.
- **The suite** comes to [`testing-conventions.md`](testing-conventions.md) §9–17 in full — ids cited
  via the quoted-literal forms (§17), unmapped tests merged or deleted per their list, no assertion
  on log calls (§15). Names and Triple-A markers are **gated, not remembered**:
  `pwsh scripts/test-style-lint.ps1` fails on any cited test with an article in its name or no
  markers, so run it **before** the REPORT and budget the rename round — it caught fourteen names in
  one round that the session wrote after reading §12, because the natural phrasing of an assertion
  carries an article. A project you cite from without owning is on the script's exempt list with its
  reason; **one citation never exempts a project**, and a file you *author* in an exempt project
  conforms anyway (run the lint once with the exemption removed locally, fix what it lists, commit
  nothing of the exemption change).
- **The rename round invalidates every test name the change doc carries** — the behavior table's Test
  column, the unmapped list, the mutation list. After it, resolve every `Class.Method` token in the
  doc — and the shorthand `` `.Method` `` continuation form, whose class is carried from the token
  before it — against a declaration **with a script**, paste both counts, and rewrite the stale cells
  from the declarations. A per-class declaration list before and after the round is what works;
  matching bodies by similarity does not on a rewritten suite (104 stale names survived two rounds
  that way, and four more survived a resolver that read only the full form).
- **A scripted fixture edit asserts its match count**, or it can be applied, reported and absent.
- **A test deleted because another gate covers it** names the gate **and its failure mode** in the
  commit ([`testing-conventions.md`](testing-conventions.md) §9).
- **A fixture inserted above an attribute block steals the doc comment above the anchor** — the new
  declaration gets two `<summary>` blocks and the old one none. `scripts/doc-comment-lint.ps1` fails
  the double; re-read the insertion point for the bare half.
- **A change that reaches the DevHost SPA is demonstrated, not reasoned** — `devhost-smoke` Tier 2 on
  a live host, evidence pasted into the change doc, and if no committed fixture can show it, grow the
  SmokeHost with the member that can ([`devhost-conventions.md`](devhost-conventions.md) § 1,
  [`testing-conventions.md`](testing-conventions.md) § 6).
- **Distill every delta line into the page**, then `pwsh scripts/spec-change.ps1 archive <slug>`.

##### Sweep discipline

Any scripted sweep over the corpus or the tree — a rename, a reference removal, a mirrored edit —
deletes **only the spans it matched** and proves each rewritten line is the original minus exactly
those spans (assert the match count, diff the rest). It reads and writes bytes with the file's own
line endings (`newline=''`), and it applies **no whole-file tidy-up** after the edit: two sweeps in
one round reset the tree, one by stripping every empty `()` in the repo, the other by rewriting every
CRLF file to LF. `scripts/sweep-residue-lint.ps1` names the shapes a sweep leaves behind (an orphaned
`()`, a doubled space, a Markdown line ending on `(`); run it, then re-read every touched sentence
anyway, because a regex sweep cannot see the sentence it leaves without a subject, the bare `§`
pointing at a deleted file, or the stub a reflow left mid-paragraph. A file `grep` calls binary is
skipped by every sweep — check with `grep -a` / `grep -c` before trusting a zero. Vendored files
(their header says so) are exempt. A mirrored edit **covers the files the change itself created**:
four articles one round mirrored into its own new test files were invisible to a checkpoint that
diffed only the two shipped packages. `docs/rfcs/` is gone, and `sweep-residue-lint.ps1` keeps it in
its out-of-scope pattern deliberately, as a rule that stays correct with nothing to match.

#### 6. The REPORT

Commit per task, push, and STOP with the REPORT — **no PR yet**; the checks run first and the PR
opens on the operator's go. Its **mandatory preamble is the self-check, in writing**, because the
checks read it first:

1. every gate line in the REPORT and the scorecard is **pasted from the terminal**, never typed;
2. for every test added or changed, the cited criterion's *text* was read against the assertion —
   list the pairs;
3. every sentence in the change doc that a later checkpoint disproved has been corrected in place or
   annotated;
4. every demonstrated observation (a Tier 2 row) is a pasted observation;
5. every number and tense in the sections above the append was re-read, and **every count is pasted
   with the command that produced it** — re-reading a number one wrote is not counting it. An
   amendment appends to the change doc and nothing re-reads what stands above it: a `37` for a
   33-method file and a future-tense plan survived two rounds that way, and 106 criteria against 105
   on the page and 466 cited tests against 452 were recounted by a reviewer with one grep each;
6. every `OUTCOME` line under the doc's Reviewer's questions carries its outcome — a
   `(pending classification)` that survived implementation is a placeholder nothing re-read (seven
   survived one round, its archive, two checks and a fresh session);
7. the change doc carries a `## Relay notes for the PR body` section naming every consumer-visible
   change, **written as each landed** — it is what the PR body's Summary quotes, under the shape
   [`.github/pull_request_template.md`](../.github/pull_request_template.md) sets for every PR here.
   One round reached its close-out with the notes in two REPORTs and none in the doc.

Then: commands run plus results · the `test → mutation` list · the GAP list · premise tests left
uncited, with reasons · park rows written to the ledger · friction one-liners in
[`process-journal.md`](process-journal.md)'s format.

**Gates, all of them, results verbatim** — every line a paste, including the ones whose numbers did
not move; a number carried from an earlier run is stale by default and a composed one is the same
defect: `dotnet build` + `dotnet test` on the full solution, `scripts/spec-lint.ps1`,
`scripts/spec-trace.ps1`, `scripts/test-style-lint.ps1`, `scripts/doc-comment-lint.ps1`,
`scripts/pragma-reason-lint.ps1`, `scripts/bom-lint.ps1`, `scripts/journal-lint.ps1`,
`scripts/sweep-residue-lint.ps1`, `scripts/run-script-tests.ps1`, and `/cleanup` once. Where a
fixture asserts a build-time literal, add a run under CI's shape
([`testing-conventions.md`](testing-conventions.md) § 8). Stryker.NET is
**optional**: run it only where the test project references a single mutatable project (it cannot run
otherwise — MTP runner in preview, multi-reference crash), read survivors by hand, never a gate and
never a score.

The envelope, the base sections (`Deviations`, `Questions`, `Friction`, `Affects others`) and the
style a report is filled in are the plugin's — see
[`vion-dispatch/README.md`](https://github.com/VION-IoT/architecture/blob/main/plugins/vion-dispatch/README.md)
§ The envelope, and do not restate them here. Everything this section demands is carried as **extra
sections**, which a lane 3 brief lists in its `sections:` line and the coordinator copies from here
verbatim: `Self-check`, `Gates`, `Test to mutation`, `GAP`, `Park rows`, `Relay notes`. The session
ends with `/vion-dispatch:report` and the `Stop` hook files the report under the coordinator repo's
`.claude/briefs/reports/`; "no PR yet" is the brief's `pr: stop-before`.

#### 7. The two checks, and the fix-up session

After the REPORT and before any PR, the coordinator runs two fresh-context **Opus** subagents
concurrently: a **completeness critic** (reads the area's code first, then the page and the table,
and reports misses by the sweep that should have caught them) and an **adversarial review** of the
branch diff (`/vion-code-review branch` with the change doc as the spec). Both read every cited
criterion's text against the test that cites it, both start from the REPORT's self-check preamble,
and both read the doc's Reviewer's questions for an `OUTCOME` left pending and its prose for a
retired name a rename replaced (a `(→ …)` marker after a current name) — two shapes the session's own
self-check does not see. Both also read the doc for its `## Relay notes for the PR body` section,
because the PR body's Summary quotes it and a doc whose notes live only in a REPORT has none to quote.
The prompt shapes are the appendix below.

These checks run **after** the session's own sibling sweep and never instead of it: the sweep finds
the neighbour the fixer thinks of, and one pass's five critic misses all sat on neighbours the sweep
had visited and not seen. A REPORT's numbers are read against the head they were taken at — a carried
count does not announce itself as stale, it announces itself as an interesting difference, and one
round went into explaining a CI-versus-plain gap of three tests that was three tests added in the
last commit.

**A finding is a hypothesis until the tree confirms it.** A check reading a branch at one commit gets
the shape right and the constants wrong: an "empty schedule" that was the framework's 60 s periodic
event; a "CRLF file" that was LF; a write-once setter called mutable; a criterion said to exist on no
page. Every finding states the call site of the premise it rests on, and an amendment's count is
written with the command that produced it — one said "eight decode sites" for four. The session
verifies the mechanism at the call site before implementing, annotates a refuted premise in the
checkpoint, and still tests the behaviour the item names when it is real: both refuted items of one
round were worth doing, and three of another's eighteen produced their test or their sentence anyway.
An item with two clauses owes two proofs — the half without a number is the half that gets skipped.

Findings return as **one numbered amendment per round**, and **the amendment is worked by a fresh
session**: the implementing session retires at its REPORT, and the amend file
`C:\_gh\architecture\.claude\briefs\amend-<slug>-N.md` is the fresh session's brief (the fix-up shape
— item, artifact, proof — with the same self-check preamble). Four rounds in a row showed the
implementing session producing amendment items done wrongly, with checkpoints that did not read true;
retiring at the REPORT costs one session's ramp-up and saves a further round. The coordinator closes
the round with targeted reads of every item at its call site, and dispatches a further Opus check
only when a targeted read finds a blocker. A further check is a **second round**, scoped
`branch:<the REPORT's hash>` ([`vion-code-review.md`](../.claude/commands/vion-code-review.md) § 1a),
so it reads what the fix-up session moved and not the round the amendment already dispositioned.

**The relay.** The **file** is the artifact, in both directions. A REPORT is filed by the `Stop` hook
under the coordinator repo's `.claude/briefs/reports/`, and `/vion-dispatch:ingest` reads the latest
file for a key and unit — so nothing depends on the transcript's last text, which may be a one-word
answer. A notification is one line (`/vion-dispatch:send-report`); losing it loses nothing.
Inbound delivery is the launcher's job: it passes `--settings '{"crossSessionInbound":"accept"}'` to
every session it opens, so an amendment is delivered rather than held — measured in the plugin
README's "Verified on" table, which also records that the permission-mode pair decides delivery only
where no such setting applies. See
[`vion-dispatch/README.md`](https://github.com/VION-IoT/architecture/blob/main/plugins/vion-dispatch/README.md)
§ Cross-session messaging; the coordinator's own inbound is a workstation setting, not a launcher one.
Lane 3's own rule stands on top of that: **the amend file is the fresh session's brief**, written
first, and the message only points at it. Held, or nothing after two minutes: the operator pastes the
file into the tab, or — tab closed, never while it is open — resumes with `-AmendFile …`. One round
lost ten hours to a relay that was held and expired unseen, so the sender still greps the recipient's
transcript within a minute — a `<cross-session-message from-name=…>` entry is delivery, a
`Held peer message` entry is not. The coordinator's watch wakes a session on **two** signals, not
one: a last transcript text that starts with an API error, and a last
text that has not changed across an interval longer than the phase's longest task — a hung turn
writes no record and sends no idle notice, and one hung for six hours before "the response stopped
arriving" surfaced. The watch does not run while the machine sleeps, so the wall clock is the only
tell.

#### 8. The scorecard

The coordinator fills it into the change doc before the PR merges:

| Measure | Value |
|---|---|
| Gates (build/test/lint/trace/style/doc-comment/bom/self-tests/cleanup/CI) | green / what failed |
| Completeness-critic misses | count + rows added, by the sweep that should have caught them |
| Evidence errors found in review | count |
| Mutation evidence | named-mutation list complete? · over-determined criteria stated? |
| Operator corrections (table + PR) | count |
| Cost | sessions × model · amendments · wall time |

#### Appendix — the two check prompts

Dispatched as `Agent`, `subagent_type: Explore`, `model: opus`, both in the same response so they run
concurrently. Replace the `<…>` parts; keep the method and the closing format — they are what makes
results comparable between rounds, and misses-per-sweep is what feeds the scorecard.

**They stay here rather than becoming `.claude/commands/` files** (decided by `T-014`, the task that
rewrote the review command; the closeout doc's reviewer's question 4 carries the reasoning). Three
reasons, in the order that decided it. The adversarial review below is not a second prompt at all —
the first instruction of its task is to read
[`vion-code-review.md`](../.claude/commands/vion-code-review.md) and follow it exactly, so a command file for it would be a wrapper around a command, and a second shelf the review
rubric could drift off. The completeness critic genuinely has no overlap, but its `<…>` parts are the
round's prose — the area's one-sentence definition, its edge values, its parity rule, the neighbouring
pages — which a slash command's arguments cannot carry and a coordinator writes by hand either way.
And `.claude/commands/` is the shelf for what the **operator types**; both of these are dispatched by
a coordinator to a subagent and never typed. A template read where the recipe that uses it is written
is cheaper than a command nobody invokes.

**Completeness critic:**

```text
You are a fresh-context completeness critic for the repo at C:\_gh\dale-sdk, branch <branch>
(checked out; committed, clean at <hash>). Read-only: no edits, no git writes (no checkout,
stash, reset, commit or add); read files from the working tree. You may run
`pwsh -NoProfile -File scripts/spec-trace.ps1` and `dotnet test` on a single test project if a
claim needs it.

A spec page was just written for <area/contract>: <one-sentence definition>. Scope:
<the folders and types from the brief>. Out of scope — other pages own them and this page cites
them: <the neighbouring areas and their pages>.

Your ONE job: find observable behaviors of that code the page MISSES. Assume the page is
incomplete and hunt — you are the second opinion on the author's blind spot. Minting is a
CONSOLIDATION (one criterion per rule, fields as [DataRow]s), so a behavior stated once for a
family of fields is covered; a behavior stated for no field is a miss.

Method:
1. Read the code independently and thoroughly FIRST, before comparing. Build your own list of
   observable behaviors — what <each consumer> can detect: <the surfaces, edge values and state
   interactions, listed concretely>. The observability discriminator is
   docs/testing-conventions.md section 9; log text is not observable (section 15). Pay particular
   attention to edge values (<list>), to <the parity rule, if any>, and to state interactions
   (<list>).
2. THEN read docs/specs/<page>.md and the change doc <path> (the behavior table, the
   consolidation map, the drift checkpoints; also docs/specs/_findings.md — a parked row counts as
   classified). The operator's classification is <path>: <summary of the decisions>.
3. Report ONLY genuine misses: behaviors on your list absent from the page's criteria AND from the
   table's classified rows (out-of-spec and park rows count as covered; a row folded into a
   criterion by the consolidation map counts as covered). For each: the behavior stated
   observably, evidence file:line, why it matters to a consumer, WHICH SWEEP should have caught it
   (statement / consumer / edge-value / state-interaction), and THE MECHANISM YOU VERIFIED AT THE
   CALL SITE — the line you read that makes the behavior happen, quoted; a mechanism you inferred
   rather than read is marked (inferred). Also flag any criterion whose sentence does not match
   what the BRANCH's code does at its seam (quote the seam), any classified intended/fix row with
   neither a criterion nor a consolidation-map entry, any OUTCOME line left at a placeholder, any
   sentence naming a retired suite by its successor (a "(→ …)" marker after a current name), a
   missing or empty "## Relay notes for the PR body" section, and any test that expects a
   culture-dependent rendering rather than the invariant text. And the citation obligation: for
   every test citing an id, whether the criterion's TEXT states what the test proves (spec-trace
   checks only that the id exists) — sample at least 30 across <the test roots and scenario
   files>, reporting each mismatch with the test name, the id, what the text says and what the
   test asserts. If the change doc carries demonstrated (Tier 2) rows, note any whose paste shows
   a scripted DOM write rather than a click or typed input — such a row is not an observation.
4. Be honest about count: zero misses is a valid result. Distinguish "miss" (observable, absent)
   from "nit" (wording).

Your final message: the list of misses (or "none"), each with evidence, the sweep and the verified
mechanism; then the seam mismatches and unmapped classified rows (or "none"); then the
citation-text mismatches (or "none"); then the demonstrated-row note (or "none"); then one line:
total misses N, unmapped rows U, citation mismatches M. Under 1000 words. Nothing else.
```

**Adversarial review:**

```text
You are a fresh-context, read-only code reviewer for the repo at C:\_gh\dale-sdk (Windows;
PowerShell 7 is `pwsh`). Another agent produced the change under review; you did not write it. Do
not edit anything; no git writes. You may run `dotnet test` on a single test project and the
repo's gate scripts (`pwsh -NoProfile -File scripts/<x>.ps1`); they are fast. Report findings
only.

Read C:\_gh\dale-sdk\.claude\commands\vion-code-review.md and follow it exactly, with:
- Scope: branch — <branch> (checked out, committed and pushed at <hash>; N commits, M files).
- Spec (statement of intent): <the change doc> (the classified behavior table, the consolidation
  map, the drift checkpoints, the test-to-mutation list, the demonstrated evidence),
  docs/specs/<page>.md (the distilled page, K criteria), docs/specs/_findings.md (the ledger),
  docs/spec-process.md (the process contract), and the operator's classification <path>.
- Notes (deliberate, operator-decided — do not flag as mistakes): <every flagged row's decision
  and its shape; the decisions; docs corrected; exemptions narrowed; premise tests kept uncited>.
- The machine baseline was run by the implementing session and pasted in its REPORT: <the pasted
  numbers>. Spot-check cheaply rather than re-running everything.

Review priorities beyond the command's D1–D10 and P1–P4:
1. The fixes: minimal, correct, matching the classification, red-first per the mutation list?
   <one clause per fix row: what parity or narrowing it is, where the sibling site is, what the
   message must name, whether every site of the shape moved>.
2. The rewritten suites — <list> — sections 9–17 conformance, the pre-existing coverage survived
   (the change doc's test map and unmapped list are the checklist — merges as [DataRow]s with
   their mutation re-derived, moves not deletions), cross-area citations kept and not re-minted,
   the cross-tier clause present where a scenario also claims the id, no test-only accessor, no
   reflection into private state, no log assertions, no Thread.Sleep/Task.Delay as
   synchronisation, no wall-clock bound standing in for an observable claim, no substring
   assertion on a rendered number, no spec id as a literal in an expectation array.
3. Process obligations: (a) for every test citing an id, read the criterion's TEXT against the
   assertion — sample at least 30, each mismatch a [blocker] "spec not carried"; (b) verify the
   REPORT's self-check preamble — re-run the gate scripts and compare the numbers; the
   consolidation map covers every classified intended/fix row (a row with neither is a [blocker]);
   (c) the delta lines versus the page (containment was enforced at archive time — check no
   criterion was reworded afterwards; name the commit order); (d) every demonstrated row is a
   pasted observation made through the UI's own controls; (e) stale or composed numbers anywhere
   in the change doc, and every test name it carries resolves to a declaration after the rename
   round — in the `Class.Method` shape and the `` `.Method` `` continuation shape alike; (f) every
   OUTCOME line carries its outcome and no sentence names a retired suite by its successor; (g)
   the "## Relay notes for the PR body" section exists and names every consumer-visible change;
   (h) a number rendered into a message is formatted in the invariant culture and its test expects
   the invariant text.
4. The sweeps: sweep-residue-lint green AND the shapes it cannot see read by you on the touched
   lines — a sentence left without a subject, a bare § pointing at a deleted file, a stub left by
   a reflow; vendored files untouched; the page carries current-truth prose only
   (`scripts/spec-lint.ps1 -Diff main`); GAP markers only where the reason holds.
5. Wire and consumer surfaces: <the area's files, schemas, HTTP or CLI surfaces> changed exactly
   as classified; docs/snapshots/publicapi-manifest.json unchanged or changed only as intended;
   the process-journal lines follow the file's format and record rather than prescribe; the
   ledger's new lines are parks with reasons and owners (a fixed row has no ledger line).

Every finding states the mechanism you verified at the call site — the line that makes the failure
happen, quoted — or is marked (inferred). Report per the command's format ([blocker] /
[convention] / [judgment] / [nit], file:line, the concrete failure, the rule, D-number or
P-number), ranked; then a "Clean" list of what you verified. Under 1000 words. Your final message
is the review report only.
```

## Routing — where work enters

- **Consumer feedback** → the `dale-sdk-feedback` skill → VION-62. Unchanged.
- **Multi-repo work** → architecture `/spec` → `/implement`, report-back included. Briefs to this
  repo point at spec pages and oblige a change doc when specified behavior moves — they do not
  restate design.
- **Single-repo work** → starts here: fix-sized straight to a PR, feature-sized as a change doc.
  When architecture's `/fix` picks a single-repo dale-sdk item, its brief is the issue key +
  constraints; the design lives in the change doc.
- **Engineering findings** — what a lane-3 extraction, a review or any other reading turns up →
  intended behavior into the spec page; small area-local defects fixed in the same PR; the rest one
  line in `docs/specs/_findings.md`, the in-repo ledger; GAPs as marked rows on the page. Jira only
  for what the operator schedules, and a scheduled item cites **spec ids and page sections, never
  `file:line`** — ids are stable and the trace gate keeps them alive.

## Gates

| What | Where | Fails when |
| --- | --- | --- |
| `scripts/spec-lint.ps1` | `spec-gates.yml` + on demand | malformed/escape-hatch ACs in `docs/specs/`; change-doc frontmatter or lifecycle broken (`archived` outside `archive/`, unknown status); `-Diff <ref>` warns on narrative added to the corpus (`-Strict` fails) |
| `scripts/spec-trace.ps1` | `spec-gates.yml` + on demand | any id on a `trace: enforced` page (or an `in-flight` delta) with no quoted-literal test reference; a marked page parsing zero ids; an id-sequence hole (a leaf missing below its umbrella's highest) that no change doc records with a `REMOVED <id> ->` delta line — prose naming the id is not that record, and neither is the `ADDED` line that minted it |
| `scripts/bom-lint.ps1` | `spec-gates.yml` + on demand | a `.cs`, `.md`, `.js`, `.mjs`, `.cjs`, `.json`, `.yml`, `.yaml`, `.html`, `.css`, `.targets` or `.props` file carrying a UTF-8 byte-order mark — no file of these kinds has one here, and a helper writing `utf-8-sig` once stamped 46 — **or a NUL byte**: one makes git call the whole file binary, so the line-ending policy never normalises it and every `grep` and reference sweep skips it (`wwwroot/components.js` carried one inside a comment, with 22 RFC citations behind it); **and zero files, or zero `.cs`, reaching the scan** — the anti-vacuous floors `pragma-reason-lint` also carries, the second because dropping `.cs` leaves every other kind still checked. A floor only catches a count reaching zero, so the report also prints the file, kind and `.cs` tallies: a scan narrowed to a pathspec that still returns C# clears both floors and shows only in the numbers. C# joined the kinds once its marked files were normalised; `.csproj`, the solution, `.DotSettings` and the `.scriban` generator template stay mixed and out of scope — an IDE rewrites the first three on its own terms, and the template's mark is stripped by the `StreamReader` that renders it |
| `scripts/journal-lint.ps1` | `spec-gates.yml` + on demand | a line under `docs/process-journal.md`'s `## Entries` that is not one dated entry in the header's shape and vocabulary, two entries sharing a line (an append that did not end the previous one), or an entry dated below the one above it |
| `scripts/sweep-residue-lint.ps1` | `spec-gates.yml` + on demand | prose — Markdown outside code, tables and headings; `//` and `///` comment text in C# and JavaScript — carrying what a scripted reference sweep leaves behind: an empty `()` on its own, two spaces inside a sentence, a Markdown line ending on `(`; frozen RFCs, the append-only logs, snapshots and vendored scripts are out of scope |
| `scripts/spec-change.ps1 archive` | on demand | any Spec-delta line not distilled into its target, or an `ADDED`/`MODIFIED` line whose EARS text the target's declaring bullet no longer carries (backticks, brackets, type arguments, wrapping, a `GAP` tail and a trailing parenthetical set aside) |
| `scripts/doc-comment-lint.ps1` | `spec-gates.yml` + on demand | a C# doc-comment block carrying more than one `<summary>` — one declaration took two doc comments, the one above the insertion anchor is bare ([`sdk-surface-conventions.md`](sdk-surface-conventions.md) § 2) |
| `scripts/test-style-lint.ps1` | `spec-gates.yml` + on demand | a test citing a spec id carries an article in its name or no Triple-A markers (`testing-conventions.md` §12/§13); projects cited from without ownership are exempt in the script, with a reason |
| `scripts/pragma-reason-lint.ps1` | `spec-gates.yml` + on demand | a directive suppressing a DALE diagnostic — one naming a `DALE####` id, or a bare `#pragma warning disable` that suppresses every diagnostic — with no reason after it and no `//` run or `/* */` block reaching it from above, or one whose comment is left with nothing to say once the directive's own words are struck ([`testing-conventions.md`](testing-conventions.md) § 8: a deliberately illegal fixture carries its reason, so the zero-`DALE`-warning count stays a signal); a `///` run above counts only where it names one of the ids being disabled; **and zero sites parsed**, the anti-vacuous floor `test-style-lint` also carries |
| `scripts/run-script-tests.ps1` | `spec-gates.yml` + on demand | any `scripts/*.tests.ps1` self-test fails, or a gate script has neither a self-test nor an exemption-with-reason |

`spec-gates.yml` runs on every PR (it is file-greps only — no build), because `publish.yml`
ignores `docs/**` and a docs-only PR must still be gated.

**Run the nine with `pwsh -File scripts/check.ps1`, or the `/check` command.** It derives them
from `spec-gates.yml` rather than repeating the table above — a gate added to the workflow with
no local invocation fails `check.ps1` instead of quietly not running — and prints one pass/fail
line per gate, carrying that gate's own summary line. `-Build` and `-Test` add the solution
build and test suite, off by default because this suite's whole point is being cheap enough to
run on every change. `-CiShape` runs in the Linux runner's shape: the repository's
dot-directories hidden, a scan for path literals in `scripts/*.ps1` whose casing disagrees with
the git index, and `-p:Version=0.0.0-ci.1` on the build and test.

A gate that cannot run at the desk is reported **skipped** with the reason, and one that ran
without part of itself is reported **partial** naming what did not run. Neither is a pass. The
live case is `spec-lint`: without `origin/main` fetched its narrative rule would compare nothing
and still report OK, so `check.ps1` runs the gate without `-Diff` and says so rather than either
skipping eight working rules or reporting a vacuous green.
