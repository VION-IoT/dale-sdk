# Spec-driven development — how to run it

The operational playbook for this repo's spec corpus, change docs, and area passes. Rationale and
the adoption decisions (D1–D11): the archived change doc
[`changes/archive/2026-09-01-sdd-process.md`](changes/archive/2026-09-01-sdd-process.md). Modeled on
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
| Contract families | TestKit surfaces (virtual time) | `TKIT` | B |
| Contract families | Http | `HTTP` | B |

Plus [`specs/_invariants.md`](specs/_invariants.md) (`SYS-` ids, cross-cutting rules pages cite
instead of restating). **Tier A** pages carry EARS acceptance criteria with ids and the trace gate;
**Tier B** pages were scoped as prose, and both passed so far (`CLI`, `IO`) found enough observable
behaviour to carry ids and the trace gate as well — the tier now names the depth of the extraction,
not whether the page is traced; **Tier C** is exempt:
examples, templates, `Vion.Dale.DevHost.Web` SPA internals, the SmokeHost fixture, and
**first-party libraries** (`libraries/` — consumers of the SDK shipping from this repo, not SDK
contract; if one ever warrants specs, it gets lbl-style block specs in place, outside this corpus).
Final page names and merges within the roster are each pass's call; the codes above are reserved
either way.

### Page frontmatter — the trace ratchet

A page opts into the trace gate with frontmatter:

```markdown
---
trace: enforced
---
```

The marker is a **ratchet**: set when the area's pass lands, never removed. `spec-trace.ps1` fails
if a marked page declares zero ids (the parse died — anti-vacuous floor) or if any declared id has
no referencing test.

## IDs & EARS

`SYS-<AREA>-NNN` (invariants) · `AC-<AREA>-NNN.M` (acceptance criteria, `.M` leaves under a `NNN`
umbrella). Area codes: the roster above, uppercase alphanumerics. Every AC is one EARS sentence
(ubiquitous / WHEN / WHILE / IF-THEN / WHERE) with a `SHALL`. No `should`, `fast`, `performant` —
`spec-lint.ps1` rejects them.

An AC is **covered** when its id appears as a quoted string literal in a test artifact:

- MSTest: `[TestProperty("spec", "AC-EMIT-001.1")]` on the test method
- xunit.v3: `[Trait("spec", "AC-EMIT-001.1")]`
- a committed scenario: `"specs": ["AC-SCEN-003.1"]` in the `*.scenario.json`

`spec-trace` scans every `*.Test` directory in the repo (the xunit projects live nested under
`examples/`, `libraries/`, `templates/`) plus the SmokeHost. A mention in a comment or method name
does not count — the gate matches quoted `"AC-…"` literals, so an id belongs in exactly the three
forms above and never in any other string (an assert message or an expectation array carrying one
would bind by accident; read the ids off the artifact under test instead).
A bare umbrella id (`AC-EMIT-001`) is covered by any of its `.M` leaves.

**GAP rows** — a declared requirement whose test does not exist yet — carry the `GAP` marker on the
declaring line, with the reason or Jira key: `` - `AC-PLUG-004.1` (Event-driven): WHEN … THE SYSTEM
SHALL …. GAP: test pending (VION-nn) ``. `spec-trace` exempts them from the orphan check and
reports the count instead — the in-repo backlog stays visible without reddening CI. Removing the
marker is how a landed test re-arms the gate for that id. The marker is read from the line that
declares the id, so a declaring bullet keeps its id and its `GAP` tail on one line — a wrapped
bullet with `GAP` on its second line fails the gate.

An id proven by **both** a unit test and a scenario states which half each tier owns in the test
class summary ("Cross-tier" clause); `spec-trace` warn-notes files missing it.

**A citation is for the criterion's text.** `spec-trace` checks that a cited id exists, not that
its sentence states what the test proves — so the check is a review obligation: the pass session
reads each criterion against its assertion before citing it (and lists the pairs in its REPORT), and
the coordinator's checks read them again. A test cited for behavior the criterion does not state
is a spec not carried, the same defect as a criterion reworded on a page without its `MODIFIED`
delta line.

**Premise tests** — a test that pins an implementation *premise* rather than a criterion: two
parsers agreeing on every conformance vector, two code paths a design relies on staying in step —
cites no id by design. There is no consumer-observable requirement to hang it on, and minting one
would be a criterion no mutation reddens. Its class summary says so, the pass's REPORT lists it,
and `spec-trace` never sees it.

## Change docs — `docs/changes/`

Everything that changes **specified behavior** goes through a change doc; the corpus stays lean
current-truth and the narrative (why, alternatives, drift) lives with the change. Two lanes:

- **Fix-sized** (most backlog items): no change doc. The PR updates the touched spec page in the
  same commit set — a page edit riding a fix is not "change narrative", it is the distill.
- **Feature-sized** (new or reshaped specified behavior): scaffold with
  `pwsh scripts/spec-change.ps1 new <slug>`, fill [`changes/_template.md`](changes/_template.md)'s
  sections, implement (tests cite the delta's ids — `spec-trace` folds `in-flight` deltas in),
  distill every Spec-delta line into its target page, then
  `pwsh scripts/spec-change.ps1 archive <slug>` — all in one PR by default. **Two-phase
  self-fires:** when the kickoff leaves design points open, or the doc mints decisions beyond what
  the operator ratified, STOP after the change doc for ratification before writing code. Pre-classify
  every open point: (a) ratified — cite; (b) decide-and-document; (c) propose-and-wait.

Statuses: `proposed` (reviewed-but-not-started; `spec-trace` ignores it) → `in-flight` (first
implementation commit flips it; deltas now demand tests) → `archived` (moved to
`changes/archive/`, only after every delta line is distilled — the archive command refuses
otherwise). `parked` for an accepted-but-blocked doc, with `blocked-on:` naming why.

**In an area whose pass hasn't run yet**, a feature-sized change still takes this lane — RFCs are
frozen, so there is nowhere else. Its distill creates or extends a **partial** spec page for the
area (no `trace: enforced` yet, so the rest of the area stays ungated); the area's eventual pass
completes the page and sets the marker.

Delta grammar — one line per id, targets **repo-root-relative**:

```
<ADDED|MODIFIED|REMOVED> <ID> -> docs/specs/<page>.md : <payload>
```

`ADDED`/`MODIFIED` payload is the EARS text; `REMOVED` payload is the reason. The `ID` must be
greppable in the target after distill. Divergence discovered during implementation goes to the
doc's **Drift checkpoints**, never inline into a spec page (`spec-lint` warns on narrative markers
added to the corpus).

## Area passes — how the corpus gets seeded

Migration runs one area at a time; un-passed areas live under the old rules until their pass — the
repo is never half-migrated. One pass, in order:

1. **Extract** — an agent drafts the area's behavior table from code and tests: one row per
   observable behavior — EARS sentence + `file:line` evidence + citing test or explicit `GAP`.
   Old RFC prose is a hint at best; every row needs evidence.
2. **Review** — the maintainer classifies rows, each already carrying the extractor's
   recommendation: *intended* → spec · *fix* → a small, area-local defect, fixed in the pass with
   its test proven red on the pre-fix code · *park* → too big for the pass: one line in
   `docs/specs/_findings.md`, the migration's finding ledger, triaged in bulk at the retro ·
   *out-of-spec* → explicitly dropped · *propose* → a wire shape or a public member's semantics the brief
   pre-classified as propose-and-wait: the recommendation on the row, fixed if the operator accepts
   it, parked if not. Jira is not a pass output; only a finding the operator
   actively schedules becomes an item.
3. **Rewrite** — the area's whole test suite is brought to the settled style
   ([`testing-conventions.md`](testing-conventions.md)); Tier A tests cite their AC ids; every AC
   has the one mutation that reddens its test, written with the test — an AC no mutation can
   redden is reworded, merged or dropped, never minted. The page states one criterion per **rule**,
   with the fields, tokens or sites it ranges over as the test's `[DataRow]`s: extraction
   over-produces rows on purpose, and the change doc's consolidation map (row → criterion) is what
   proves no classified row was lost.
4. **Land** — the spec page (with `trace: enforced`) enters `docs/specs/`; the trace gate covers it
   from now on; changes to the area require a change doc from now on.
5. **Delete** — the RFCs the page absorbed are removed in the same PR, and the reference sweep
   runs: no living doc, skill, or code comment cites them (`grep -r "RFC 00"` clean outside
   `docs/process-journal.md`, `docs/retro/`, `docs/changes/archive/`, and `docs/rfcs/` itself while
   it still exists). Append-only logs keep their citations. A scripted sweep deletes only the spans
   it matched, proves each rewritten line is the original minus those spans, keeps the file's line
   endings, and applies no whole-file tidy-up; `scripts/sweep-residue-lint.ps1` fails on the residue
   shapes a sweep leaves (an orphaned `()`, a doubled space, a Markdown line ending on `(`), and
   every touched sentence is re-read for the shapes no grep can see. A ledger line that absorbs a
   deleted RFC's item names the deleted origin and points at the archived change doc's absorption
   heading — never a `§` anchor into the file that is gone.
6. **Style gate** — `scripts/test-style-lint.ps1` holds every cited test to §12/§13 of
   `testing-conventions.md`; projects a pass cites from without owning are exempt in the script,
   with a reason, until their own pass. Stryker.NET is optional and only runnable where a test
   project references a single mutatable project (its MTP runner is preview-only and it crashes on
   multi-reference projects — two of fourteen areas); where it runs, survivors are read by hand,
   never a gate, never a score.

The protocol is packaged as the `spec-pass` skill; each pass is one change doc + one band-sized
PR. Order: plugin ABI (done — the pilot) → emission (done) → config gating (done) → introspection +
identifiers (done) → scenario/stepping/pairing (done) → DevHost control (done) → remainder. `docs/rfcs/` is frozen meanwhile (do not
cite it in new work) and disappears with the last pass.

## Dispatching a pass to a fresh session

An area pass — or any ratified feature-sized change doc — is implemented in a fresh session,
dispatched with the architecture repo's launcher exactly as `/implement` and `/fix` dispatch
(never reimplement the launch inline; the launcher encodes hard-won constraints). The pass
protocol itself is packaged as the **`spec-pass` skill** (`.claude/skills/spec-pass/`) — pass
briefs point at it and carry only the per-area facts (scope, anchors, RFCs to absorb, page path,
attempt number); learning between passes lands as skill diffs, never as longer briefs. The skill
retires with the migration:

1. Write the brief to `C:\_gh\architecture\.claude\briefs\brief-<slug>-dale-sdk.md` — the
   gitignored home the launcher's permission model expects. The brief is a **pointer, not a
   restatement**: the change-doc path + "decisions and review resolutions are binding;
   contradictions go to Drift checkpoints, not silent divergence" + its PR shape and every open
   point pre-classified + house discipline (branch, tests cite ids, `/cleanup` once pre-PR,
   `/vion-code-review branch` before the PR) + a read-only note per additional dir naming the
   SPECIFIC files to look things up in. For an area pass, scope and anchors are **folders,
   projects and descriptor ranges the session enumerates itself** — never a transcribed file or
   descriptor list: two passes running, the brief's counts were the thing that was wrong. The test
   scope is stated the same way — the test project's folders, with the suites another page owns
   named by their citations — never a list of files: the brief that listed suites missed three in
   the area's own project. **Before the launch line, a fresh-context Opus `Explore` subagent checks
   the brief against the code** — every file, folder, count, ownership claim and omission, reported
   as corrections — and the brief is rewritten before dispatch: four minutes and one subagent; the
   CTRL brief carried 13 wrong claims and 9 omissions until it ran.
2. Emit the launch line in a single `bash` fence (the fence is the copy button):

   ```
   pwsh -NoProfile -File "C:\_gh\architecture\scripts\launch-session.ps1" -Repo "C:\_gh\dale-sdk" -Brief "C:\_gh\architecture\.claude\briefs\brief-<slug>-dale-sdk.md" -Model <opus|sonnet> -Effort <medium|high> -Name "<slug>: dale-sdk" -SessionId "<uuid>" -AddDirs "C:\_gh\architecture<,read-only dep>" -PermissionMode bypassPermissions
   ```

   Model rubric: Opus unless the slice is mechanical (then Sonnet); High effort for
   design-bearing/correctness-sensitive work, Medium for a contained change. The permission mode
   is **per process**: a launch or an `-AmendFile` resume without the flag starts in default mode
   and stops at a startup prompt before its first turn (CLI 2.1.258 ignores
   `settings.local.json`'s `defaultMode`; the launcher reads it when the flag is omitted), and a
   default-mode session holds every inbound peer message. Pass the flag on every launch and every
   resume.
3. Ratification round-trips use the launcher's `-AmendFile` + the printed `-SessionId` — the
   two-phase STOP maps onto it directly.

The change doc is **implementer-owned**: the dispatched session flips it `in-flight`, appends
Drift checkpoints, distills, archives. No report-back block — the PR is the report.

### Checks and amendments — the coordinator's side of a pass

After a pass session's Phase B REPORT, and before any PR, the coordinator runs two fresh-context
**Opus** subagents: a completeness critic (reads the area's code first, then the page and the
table, and reports misses by the sweep that should have caught them) and an adversarial review of
the branch diff (`/vion-code-review branch` with the change doc as the spec). Both read every cited
criterion's text against the test that cites it, and both start from the REPORT's self-check
preamble (`spec-pass` skill, Phase B step 10); both also read the doc's Reviewer's questions for an
`OUTCOME` left pending and its prose for a retired name a rename pass replaced (a `(→ …)` marker
after a current name) — two shapes a pass's own self-check does not see. Before the classification,
the coordinator's second opinion on the ⚠ rows opens the readers' repositories where they are on
the machine (the private runtime, the gateways, the first consumer) rather than trusting a `Why`
cell's reader — five reader claims fell that way in one pass, three changing a row's class. A REPORT
may arrive as a cross-session message rather than as the session's last transcript text; the
coordinator saves it from the message and says so, because the transcript's last text may be a
one-word answer. Both checks also read the change doc for its `## Relay notes for the PR body`
section, because the PR body quotes it verbatim and a pass whose notes live only in its REPORT has
none to quote. The two check prompts are filled kit files
(`C:\_gh\architecture\.claude\briefs\sdd-kit\prompts\critic-<slug>.md`, `review-<slug>.md`), prepared while
the pass session runs its final gates and kept as the record of what each check was asked; the next
area's scoping inventory and brief check run in the fix-up window, when the coordinator is otherwise
idle. The coordinator's watch wakes a session on two signals, not one: a last transcript text that
starts with an API error, and a last text that has not changed across an interval longer than the
phase's longest task — a hung turn writes no record and sends no idle notice, and one hung for six
hours before "the response stopped arriving" surfaced; the watch itself does not run while the
machine sleeps, so the wall clock is the only tell. A check — the critic, the review, the second
opinion — states the call site of every premise a finding rests on, and a finding whose premise is
a static's mutability, a gate's existence or a probe's outcome names the line it read: three of one
round's premises were refuted at the call site by the fix-up session (a setter that is write-once,
a criterion that exists on no page, a probe the prose had right), each costing the item its first
paragraph. An amendment's count is written with the command that produced it — one said "eight
decode sites" for four, and the session had read the four before the coordinator's correction
arrived. Findings go back as one
numbered amendment per round: the amend file `C:\_gh\architecture\.claude\briefs\amend-<slug>-N.md` is the artifact,
written first; the cross-session message that points at it is only a notification, and it reaches
the session only when **both** sessions run in bypass mode. The sender checks its own mode before
sending, then verifies within a minute in the recipient's transcript — a
`<cross-session-message from-name=…>` entry is delivery, a `Held peer message` entry is not. Held,
or nothing after two minutes: the operator pastes the file into the tab, or — tab closed, never
while it is open — resumes with `-AmendFile … -PermissionMode bypassPermissions`. The INTRO pass
lost ten hours to a relay that was held and expired unseen. **The amendment is worked by a fresh
session**: the pass session retires at its Phase B REPORT and the amend file is the new session's
brief (the fix-up shape). Four passes in a row showed the session that wrote Phase B producing
amendment items done wrongly, with checkpoints that did not read true — context depth degrades
exactly the disciplines an amendment asks for — and each needed a further fresh round; retiring at
the REPORT costs one session's ramp-up and saves it. The coordinator closes the round with
targeted reads at the call sites, and dispatches a further Opus check only when a read finds a
blocker. Each amendment item names one artifact and the proof it
owes — a two-clause item gets its numbered half done — and states its premise as a hypothesis: a
check reading a branch at one commit gets the shape right and the constants wrong, so the session
verifies the mechanism at the call site before implementing (two of one amendment's twenty-five
items were refuted from the tree, and both were still worth doing; three of another's eighteen
fell the same way — a write-once setter, a criterion on no page, a probe the prose had right — and
each still produced the test or the sentence its item asked for). The coordinator fills the
scorecard from the checks' findings before the PR merges.

## Routing — where work enters

- **Consumer feedback** → the `dale-sdk-feedback` skill → VION-62. Unchanged.
- **Multi-repo work** → architecture `/spec` → `/implement`, report-back included. Briefs to this
  repo point at spec pages and oblige a change doc when specified behavior moves — they do not
  restate design.
- **Single-repo work** → starts here: fix-sized straight to a PR, feature-sized as a change doc.
  When architecture's `/fix` picks a single-repo dale-sdk item, its brief is the issue key +
  constraints; the design lives in the change doc.
- **Extraction finds** → intended behavior into the spec page; small area-local defects fixed in
  the pass; the rest to `docs/specs/_findings.md`; GAPs as marked rows in the page. Jira only for
  what the operator schedules.

## Gates

| What | Where | Fails when |
| --- | --- | --- |
| `scripts/spec-lint.ps1` | `spec-gates.yml` + on demand | malformed/escape-hatch ACs in `docs/specs/`; change-doc frontmatter or lifecycle broken (`archived` outside `archive/`, unknown status); `-Diff <ref>` warns on narrative added to the corpus (`-Strict` fails) |
| `scripts/spec-trace.ps1` | `spec-gates.yml` + on demand | any id on a `trace: enforced` page (or an `in-flight` delta) with no quoted-literal test reference; a marked page parsing zero ids; an id-sequence hole (a leaf missing below its umbrella's highest) that no archived change doc names |
| `scripts/bom-lint.ps1` | `spec-gates.yml` + on demand | a `.md`, `.js`, `.mjs`, `.cjs`, `.json`, `.yml`, `.html`, `.css`, `.targets` or `.props` file carrying a UTF-8 byte-order mark — no file of these kinds has one here, and a helper writing `utf-8-sig` once stamped 46 — **or a NUL byte**: one makes git call the whole file binary, so the line-ending policy never normalises it and every `grep` and reference sweep skips it (`wwwroot/components.js` carried one inside a comment, with 22 RFC citations behind it) |
| `scripts/journal-lint.ps1` | `spec-gates.yml` + on demand | a line under `docs/process-journal.md`'s `## Entries` that is not one dated entry in the header's shape and vocabulary, two entries sharing a line (an append that did not end the previous one), or an entry dated below the one above it |
| `scripts/sweep-residue-lint.ps1` | `spec-gates.yml` + on demand | prose — Markdown outside code, tables and headings; `//` and `///` comment text in C# and JavaScript — carrying what a scripted reference sweep leaves behind: an empty `()` on its own, two spaces inside a sentence, a Markdown line ending on `(`; frozen RFCs, the append-only logs, snapshots and vendored scripts are out of scope |
| `scripts/spec-change.ps1 archive` | on demand | any Spec-delta line not distilled into its target, or an `ADDED`/`MODIFIED` line whose EARS text the target's declaring bullet no longer carries (backticks, brackets, type arguments, wrapping, a `GAP` tail and a trailing parenthetical set aside) |
| `scripts/doc-comment-lint.ps1` | `spec-gates.yml` + on demand | a C# doc-comment block carrying more than one `<summary>` — one declaration took two doc comments, the one above the insertion anchor is bare ([`sdk-surface-conventions.md`](sdk-surface-conventions.md) § 2) |
| `scripts/test-style-lint.ps1` | `spec-gates.yml` + on demand | a test citing a spec id carries an article in its name or no Triple-A markers (`testing-conventions.md` §12/§13); projects a pass cites from without owning are exempt in the script, with a reason, until their pass |
| `scripts/run-script-tests.ps1` | `spec-gates.yml` + on demand | any `scripts/*.tests.ps1` self-test fails, or a gate script has neither a self-test nor an exemption-with-reason |

`spec-gates.yml` runs on every PR (it is file-greps only — no build), because `publish.yml`
ignores `docs/**` and a docs-only PR must still be gated.
