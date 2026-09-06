---
slug: unify-pass
status: archived
blocked-on: none           # for parked docs: what's blocking + ref
areas: ANLZ,EMIT,BIND,GATE,INTRO,SCEN,CTRL,PLUG,CLI,LIFE,MODB,IO,TKIT
author: unify-pass session (Opus)
created: 2026-09-06
updated: 2026-09-06
supersedes: none           # path of a superseded change doc, or none
---

# Unification pass, Tier 1 — the corpus's conventions, and two invariants

> Change doc — one in-flight change. The `Spec delta` below is distilled into the current-truth
> pages under `docs/specs/` in the implementation PR, then this doc is archived
> (`pwsh scripts/spec-change.ps1 archive <slug>`). Process: `docs/spec-process.md`. Never put
> change narrative inline in a spec page — it lives here.

## At a glance

### Summary

Thirteen area passes left the corpus's requirements at a similar level and its **conventions**
drifted across four generations. Tier 1 unifies the conventions and changes no criterion's meaning:
one EARS label vocabulary written into `spec-process.md` and enforced by `spec-lint`, one GAP-tail
shape, the citation block backfilled onto the four first-generation pages, bold out of criterion
text, `testkit.md`'s bullets wrapped, and `_invariants.md` populated with the two `SYS-` rules the
pages had been re-minting from their own side. No id is retired, merged or renumbered; **no
criterion sentence is reworded at all**; no test is touched.

### Spec implications

All thirteen pages plus `_invariants.md`. Thirty-seven criteria change their `(Label)` token and
nothing else — proved line by line: each changed line is byte-identical to its predecessor once the
label token is removed. Thirteen GAP tails take the target shape, each naming the same obstacle it
named before. Four pages gain the opening citation block the other nine carry. Four criteria lose
bold. `testkit.md`'s sixty non-GAP bullets wrap at 100 columns, joined text byte-identical.
`_invariants.md` gains `SYS-REL-001` and `SYS-API-001`, and `testkit.md`, `io.md` and `modbus.md`
cite them where they had narrated the consequence — which makes one sentence on `io.md` false, and
it is corrected in the same commit.

### Decisions

- `D1` — the label is derived **mechanically** from the criterion's first EARS keyword, and the
  sentence after the label is never edited. A relabel that needed a reworded sentence would be a
  Tier 2 change of meaning wearing a Tier 1 hat.
- `D2` — `spec-lint` enforces the vocabulary rather than prose asking for it. The drift exists
  because the declaration regex accepted any `(label):`; a gate over prose is the cheap rung.
- `D3` — a pure back-reference is a legal GAP tail (the grammar's second option) and is normalised
  to `the same <path> as <id>`, not expanded into a restatement of its anchor's reason.
- `D4` — the two `SYS-` rules are cited **by family** (`SYS-API-*`), not by full id. Forced, not
  chosen: see the checkpoint below.
- `D5` — `SYS-API-001` states "the architecture repository", not the brief's "documentation
  repository": `.github/workflows/publish.yml:259-262` opens the issue against
  `VION-IoT/architecture`. A spec page states what the code does.

### Reviewer's questions

1. *(c) propose-and-wait* — `D4`: the `SYS-` rules are cited as `SYS-REL-*` / `SYS-API-*` because a
   full id on a traced page is a declaration to `spec-trace` and neither rule has a test. The
   alternative belongs to Tier 2: give `_invariants.md` `trace: enforced`, let a roster test cite
   `SYS-REL-001`, and move the citations to the full id.
   **OUTCOME:** decided in-session as `D4`; the probe that forced it is in the checkpoints. Tier 2
   owns the reversal.
2. *(b) decide-and-document* — item 4 of the brief (three foreign ids inside criterion sentences)
   found **zero**. All five foreign ids on a declaration bullet sit in a GAP tail, where the tail
   grammar sanctions a back-reference.
   **OUTCOME:** nothing done, nothing needed; the scan and its count are in the inventory. Tier 1
   therefore carries no `MODIFIED` line at all.
3. *(b) decide-and-document* — the two GAP tails just below the two the brief named as essays —
   `AC-LIFE-020.3` (41 words) and `AC-CLI-010.13` (38) — are long but are single sentences of the
   sanctioned shape.
   **OUTCOME:** left as they are; named here so Tier 2 can take them if the operator wants a word
   budget rather than a shape.
4. *(a) ratified* — the consistency review's per-page tables are right and three of its **totals**
   are arithmetic errors (1246 criteria, 197 umbrellas, 70 GAP tails).
   **OUTCOME:** the per-page numbers are used as given; the corrected totals are in the checkpoints.
5. *(b) decide-and-document* — `spec-lint`'s new label check reads `$acStartRx`, which matches `AC-`
   ids only, so the `(Ubiquitous)` on a `SYS-` bullet is unchecked.
   **OUTCOME:** left as it is — widening the declaration regex to `SYS-` touches every corpus check
   at once and is beyond Tier 1. Named here so it is a decision rather than an oversight.

---

## Full design

### The inventory, re-verified in place

Every count below was re-run on this branch; the command is the row. **Before** is `main` at
`66a0df3`, materialised as a directory of `git show main:docs/specs/*` so one command runs against
both trees. A row whose *Before* differs from the consistency review is a drift checkpoint.

| What | Command | Before | After |
|---|---|---|---|
| criteria | `` cat docs/specs/*.md | awk '/^- `AC-[A-Z0-9]+-[0-9]+\.[0-9]+`/{n++} END{print n+0}' `` | 1126 | 1126 |
| umbrellas | the same `awk`, counting distinct `AC-<AREA>-NNN` | 206 | 206 |
| labels | `` grep -hoE '^- `AC-[^`]+` \([A-Za-z-]+\)' docs/specs/*.md | grep -oE '\([A-Za-z-]+\)$' | sort | uniq -c `` | U 707 · E 375 · S 27 · Cond 10 · Unw 7 · Opt 0 | U 699 · E 379 · S 17 · Cond 0 · Unw 2 · Opt 29 |
| GAP tails | `` grep -hE '^- `AC-.*GAP:' docs/specs/*.md | wc -l `` | 68 | 68 |
| GAP tails per page | `` grep -cE '^- `AC-.*GAP:' docs/specs/*.md `` | 0,0,12,1,31,0,3,2,2,3,1,11,0,0,2 | identical |
| `WHERE` on a bullet's first line | `` grep -hE '^- `AC-' docs/specs/*.md | grep -c 'WHERE' `` | 28 | 28 |
| bullets whose **joined** text leads on `WHERE` | the label deriver's tally | 29 | 29 |
| bold on a bullet's first line | `` grep -hE '^- `AC-' docs/specs/*.md | grep -c '\*\*' `` | 3 | 0 |
| bold anywhere in a **joined** bullet | the bold scan, all 1126 bullets | 4 | 0 |
| foreign `AC-` id in a criterion **sentence** | the foreign-id scan, 1126 bullets visited | 0 | 0 |
| foreign `AC-` id in a criterion **GAP tail** | the same scan without the tail stripped | 5 | 5 |
| `testkit.md` lines > 100 columns | `` awk 'length > 100' docs/specs/testkit.md | wc -l `` | 103 | 43 |
| `testkit.md` bullets on one line | the wrap scan | 63 of 63 | 3 of 63 (2 GAP + 1 already short) |
| ids `spec-trace` sees | `pwsh -NoProfile -File scripts/spec-trace.ps1` | 1069 referenced, 68 GAP | 1071 referenced, 70 GAP (68 on pages, unchanged, + the two in-flight delta lines) |

### 1. The relabels

Derived by script from the **first** EARS keyword in the joined bullet — `WHEN`, `WHILE`, `IF`,
`WHERE` as whole uppercase words, with the GAP tail excluded from the search. Every listed row was
read in full before the script ran.

**No false hits.** The screen is `label-audit`: it lists every bullet in the corpus whose first
keyword is not at character 0, because an EARS keyword leads and a keyword anywhere else is where a
quoted string, a back-ticked token or a lower-case "when" would hide. It returns **two**, both in
the table below, and both are genuine uppercase EARS keywords in the trailing clause of a composite
sentence — not quoted, not back-ticked, not lower case. So all 37 rows are real relabels.

The applier proves its own scope: for each line it asserts the original and the rewrite are
identical once the `(Label)` token is removed, and refuses the whole run unless the total is 37.

| # | id | page | before | after | keyword |
|---|---|---|---|---|---|
| 1 | `AC-ANLZ-002.1` | analyzers.md | Ubiquitous | Optional | `WHERE` (leads) |
| 2 | `AC-ANLZ-002.3` | analyzers.md | Ubiquitous | Optional | `WHERE` (leads) |
| 3 | `AC-ANLZ-002.5` | analyzers.md | Ubiquitous | Optional | `WHERE` (leads) |
| 4 | `AC-ANLZ-003.2` | analyzers.md | Ubiquitous | Event-driven | `WHEN` (leads) |
| 5 | `AC-ANLZ-003.5` | analyzers.md | Ubiquitous | Event-driven | `WHEN` (leads) |
| 6 | `AC-ANLZ-003.6` | analyzers.md | Ubiquitous | Event-driven | `WHEN` (leads) |
| 7 | `AC-LIFE-002.3` | block-lifecycle.md | Conditional | Optional | `WHERE` (leads) |
| 8 | `AC-LIFE-007.7` | block-lifecycle.md | Conditional | Optional | `WHERE` (leads) |
| 9 | `AC-LIFE-014.1` | block-lifecycle.md | Conditional | Optional | `WHERE` (leads) |
| 10 | `AC-LIFE-014.4` | block-lifecycle.md | Conditional | Optional | `WHERE` (leads) |
| 11 | `AC-LIFE-017.3` | block-lifecycle.md | Conditional | Optional | `WHERE` (leads) |
| 12 | `AC-CLI-001.4` | cli.md | State-driven | Optional | `WHERE` (leads) |
| 13 | `AC-CLI-001.5` | cli.md | State-driven | Optional | `WHERE` (leads) |
| 14 | `AC-CLI-001.9` | cli.md | State-driven | Optional | `WHERE` (leads) |
| 15 | `AC-CLI-005.7` | cli.md | State-driven | Optional | `WHERE` (leads) |
| 16 | `AC-CLI-009.6` | cli.md | State-driven | Optional | `WHERE` (leads) |
| 17 | `AC-CLI-011.2` | cli.md | State-driven | Optional | `WHERE` (leads) |
| 18 | `AC-CLI-011.8` | cli.md | State-driven | Optional | `WHERE` (leads) |
| 19 | `AC-CLI-011.10` | cli.md | State-driven | Optional | `WHERE` (leads) |
| 20 | `AC-CLI-012.2` | cli.md | Ubiquitous | Optional | `WHERE` (at char 224) |
| 21 | `AC-CLI-013.4` | cli.md | State-driven | Optional | `WHERE` (leads) |
| 22 | `AC-CLI-013.5` | cli.md | Ubiquitous | Event-driven | `WHEN` (at char 191) |
| 23 | `AC-CLI-017.5` | cli.md | State-driven | Optional | `WHERE` (leads) |
| 24 | `AC-GATE-005.2` | config-gating.md | State-driven | Event-driven | `WHEN` (leads) |
| 25 | `AC-GATE-012.5` | config-gating.md | Conditional | Optional | `WHERE` (leads) |
| 26 | `AC-CTRL-001.2` | devhost-control.md | Conditional | Optional | `WHERE` (leads) |
| 27 | `AC-CTRL-020.4` | devhost-control.md | Conditional | Optional | `WHERE` (leads) |
| 28 | `AC-EMIT-002.2` | emission.md | Unwanted | Optional | `WHERE` (leads) |
| 29 | `AC-EMIT-005.6` | emission.md | Unwanted | Optional | `WHERE` (leads) |
| 30 | `AC-EMIT-006.3` | emission.md | Conditional | Unwanted | `IF` (leads) |
| 31 | `AC-EMIT-005.5` | emission.md | Unwanted | Optional | `WHERE` (leads) |
| 32 | `AC-EMIT-013.2` | emission.md | Unwanted | Optional | `WHERE` (leads) |
| 33 | `AC-EMIT-013.3` | emission.md | Unwanted | Optional | `WHERE` (leads) |
| 34 | `AC-INTRO-004.4` | introspection.md | Unwanted | Optional | `WHERE` (leads) |
| 35 | `AC-INTRO-009.4` | introspection.md | Unwanted | Optional | `WHERE` (leads) |
| 36 | `AC-MODB-002.2` | modbus.md | Event-driven | State-driven | `WHILE` (leads) |
| 37 | `AC-PLUG-005.5` | plugin-loading.md | Conditional | Unwanted | `IF` (leads) |

**The six the brief asked to be listed one by one** — everything that is neither a `WHERE` becoming
`(Optional)` nor a `(Conditional)` becoming `(Unwanted)`:

- `AC-ANLZ-003.2`, `AC-ANLZ-003.5`, `AC-ANLZ-003.6` — `WHEN` leads each sentence and each was
  labelled `(Ubiquitous)`. Rows 4–6.
- `AC-CLI-013.5` — `WHEN` at character 191, in the sentence's second clause ("…and SHALL refuse
  before contacting anything WHEN no realm can be resolved"). The only keyword in the sentence.
- `AC-GATE-005.2` — `WHEN` leads; the `(State-driven)` label came from the sentence's lower-case
  "while", which is prose and not an EARS keyword.
- `AC-MODB-002.2` — `WHILE` leads and the label said `(Event-driven)`; the one row in the corpus
  that gains `(State-driven)`.

And the brief's own expectation, corrected: it predicted "roughly 30 `WHERE` → `(Optional)` and the
10 `(Conditional)` → `(Unwanted)`". Twenty-nine become `(Optional)`; of the ten `(Conditional)`
criteria **eight are `WHERE` and become `(Optional)`**, and only two are `IF`. `(Conditional)`
disappears either way.

### 2. The GAP tails

Target shape: `GAP: <one clause naming what cannot be observed>`, optionally `, which
docs/testing-conventions.md § N forbids` or `; the same <path> as <id>`. Thirteen tails moved; all
68 still exist and every one names the same obstacle it named before.

| # | id | page | before (first 50 chars) | after |
|---|---|---|---|---|
| 1 | `AC-ANLZ-002.4` | analyzers.md | GAP: [`../sdk-surface-conventions.md`](../../sdk-surf… | GAP: flipping the generated-code flag changes no diagnostic in this repository, so there is no observable to assert. |
| 2 | `AC-CLI-005.2` | cli.md | GAP: same spawned-process path as `AC-CLI-005.1`. | GAP: the same spawned-process path as `AC-CLI-005.1`. |
| 3 | `AC-CLI-005.4` | cli.md | GAP: same spawned-process path as `AC-CLI-005.1`. | GAP: the same spawned-process path as `AC-CLI-005.1`. |
| 4 | `AC-CLI-011.2` | cli.md | GAP: same path as `AC-CLI-011.1`. | GAP: the same pack-and-upload path as `AC-CLI-011.1`. |
| 5 | `AC-CLI-016.2` | cli.md | GAP: same browser-bound path as `AC-CLI-016.1`. | GAP: the same browser-bound path as `AC-CLI-016.1`. |
| 6 | `AC-CLI-016.3` | cli.md | GAP: same browser-bound path as `AC-CLI-016.1`. | GAP: the same browser-bound path as `AC-CLI-016.1`. |
| 7 | `AC-CLI-018.7` | cli.md | GAP: same browser-bound path as `AC-CLI-018.4`. | GAP: the same browser-bound path as `AC-CLI-018.4`. |
| 8 | `AC-BIND-005.5` | contracts.md | GAP: an assembly whose types fail to load cannot b… | GAP: no fixture can make an endpoint search's assembly load only some of its types inside the test process. |
| 9 | `AC-BIND-008.3` | contracts.md | GAP: an assembly whose types fail to load cannot b… | GAP: no fixture can make a contract search's assembly fail to enumerate inside the test process. |
| 10 | `AC-INTRO-017.2` | introspection.md | GAP: as `AC-INTRO-017.1`. | GAP: the same pack-and-consume path as `AC-INTRO-017.1`. |
| 11 | `AC-INTRO-017.3` | introspection.md | GAP: as `AC-INTRO-017.1`. | GAP: the same pack-and-consume path as `AC-INTRO-017.1`. |
| 12 | `AC-TKIT-002.4` | testkit.md | GAP: the kit's own test project cannot observe a m… | GAP: the kit's own test project cannot observe a mapping. |
| 13 | `AC-TKIT-006.3` | testkit.md | GAP: no shipped reader reads a received contract m… | GAP: no shipped reader reads a received contract message's identity. |

Rows 1, 12 and 13 lose text that was reasoning rather than obstacle. It is not deleted:

- `AC-ANLZ-002.4`'s `sdk-surface-conventions.md § 5` citation is now a paragraph under the bullets
  of that section.
- `AC-TKIT-002.4`'s four remaining clauses (the over-determined seam, the generated sender, the
  analyzer that does not travel through a project reference, the seven example suites) are now a
  paragraph under `AC-TKIT-002.2`'s.
- `AC-TKIT-006.3`'s reasoning was **already** in the prose below it — the paragraph beginning "The
  identity rule above reaches further than the raise helpers" states the same mechanism and ends
  "neither kit can test it, which is what the identity criterion's own marker says". Nothing was
  moved for that one.

Neither new paragraph names its criterion's id. `spec-trace` reads the `GAP` marker from the line
that declares the id, and any acceptance id on a traced page **without** the marker is a
declaration — so explanatory prose that names its own GAP'd criterion by number un-GAPs it.

Rows 8 and 9 were byte-identical before. Both rest on the same fact (an assembly cannot be made to
half-load inside the test process), and each now names its own search: the endpoint search that
degrades, and the contract search that refuses.

### 3. The citation blocks

`io.md:20-33`'s shape, backfilled onto the four first-generation pages. The ids were collected
mechanically — every `AC-` id on the page that is not its own area — and only one page had any.

| page | foreign ids the prose already relies on | what was written |
|---|---|---|
| `plugin-loading.md` | none | "Cited rather than restated: nothing." + the one-sentence "does not own" line (`scenarios.md:19-20`'s shape) |
| `emission.md` | none | the same |
| `config-gating.md` | none — it links `introspection.md` at `:81` for identifier stability, by page and without an id | the neighbour named by page, plus the "does not own" line |
| `introspection.md` | `AC-EMIT-013.1`, `AC-EMIT-013.4`, `AC-EMIT-013.6`, `AC-GATE-006.1`, `AC-GATE-007.7`, `AC-GATE-010.3`, `AC-GATE-010.4`, `AC-GATE-010.5` | a full block: `AC-EMIT-013.*`, `AC-GATE-006.1`, `AC-GATE-007.7`, `AC-GATE-010.*` |

Three of the four cite nothing foreign, as the brief's fallback anticipated. Neither page's block
adds a claim or a citation the page does not already make; the two globs stand for families the page
already names member by member.

### 4. Foreign ids inside criterion sentences

**None.** The brief expected three (two on `block-lifecycle.md`, one on `analyzers.md`). A scan over
all 1126 declaration bullets, each with its wrapped continuation lines folded in, finds no criterion
sentence carrying a foreign `AC-` id. Five bullets carry one **in a GAP tail**, which is where the
review's joiner counted them:

| id | page | foreign ids | where |
|---|---|---|---|
| `AC-ANLZ-014.1` | analyzers.md | `AC-GATE-011.1` (and `011.2`, `011.3`, `011.10` as leaf shorthands) | GAP tail |
| `AC-ANLZ-014.3` | analyzers.md | `AC-GATE-011.8` | GAP tail |
| `AC-ANLZ-015.1` | analyzers.md | `AC-EMIT-012.1` | GAP tail |
| `AC-ANLZ-015.2` | analyzers.md | `AC-EMIT-012.3` | GAP tail |
| `AC-LIFE-020.3` | block-lifecycle.md | `AC-CTRL-001.3`, `AC-CTRL-002.1` | GAP tail |

A GAP tail is not a criterion sentence, and the tail grammar this pass writes down explicitly
sanctions a back-reference. Moving these into prose would empty the tails of their reason, which the
brief forbids. So **Tier 1 rewords no criterion sentence**, and the Spec delta carries no `MODIFIED`
line.

### 5. The small drift

**Bold.** Four criteria carried `**…**` inside the sentence, not three: `AC-ANLZ-006.3`
(`**public**`), `AC-ANLZ-010.4` (`**and from its references**`), `AC-ANLZ-012.5` (`**effective**`)
and `AC-ANLZ-016.1` (`**property**`). The fourth sits on a wrapped continuation line, which the
review's line-scoped grep could not see. `analyzers.md` is still the only page that ever had bold in
criterion text, and its **prose** keeps its own (five lines, untouched — the remover is scoped to
declaration-bullet lines and their continuations, and each rewritten line is proved to be the
original minus exactly the `**` markers).

Whether the archive gate cares: **it does not normalise bold away.**
`spec-change.ps1`'s `Get-ComparableText` (`:92-99`) strips backticks, square brackets, generic type
arguments, a `GAP:` tail and — on the delta side only — a trailing parenthetical, then collapses
whitespace. `**` survives all of it. Removing bold from a criterion that carried a delta line would
therefore have broken the gate; none of these four does.

**`testkit.md`'s wrapping.** It was the only page where *every* bullet is one unwrapped line (63 of
63; the next-worst is `cli.md` at 32 of 133, and 31 of those 32 are GAP bullets that have no
choice). Sixty non-GAP bullets now wrap at 100 columns with the corpus's two-space continuation
indent. The two GAP bullets stay on one line each — `spec-trace` reads the marker from the line that
declares the id, so a wrapped GAP bullet fails the gate with no hint why. Every one of the 63
bullets' joined text is byte-identical before and after, asserted bullet by bullet against `main`'s
copy of the file.

### 6. `_invariants.md`

The file said "No page exists yet" while thirteen pages existed. It now carries the two rules the
pages had been re-minting from their own side, keeps its first paragraph and gains the pointer to
the minting rule. It does **not** get `trace: enforced` — `spec-trace.ps1:46` traces only enforced
pages, and Tier 2 decides whether a roster test cites `SYS-REL-001` and the file becomes traced.

`docs/spec-process.md` § IDs & EARS gains the minting rule itself, in two sentences. The retirements
it implies are Tier 2's and are named nowhere in the corpus.

The citation sites, one clause each:

| page | site | what it said | what it says now |
|---|---|---|---|
| `testkit.md` | under `AC-TKIT-013.4` | nothing — the criterion stood alone | "`AC-TKIT-013.4` states a repository-wide rule from a kit's side: the rule is `SYS-REL-*`…" |
| `testkit.md` | the `AC-TKIT-013.2` paragraph | "…a manifest drift is auto-committed rather than failed." | + "(the manifest rule is `SYS-API-*`, `_invariants.md`)" |
| `io.md` | the API-manifest paragraph | "**no criterion in this corpus states that gate** — not here and not on any other page — so it is named as evidence rather than cited." | "The manifest rule itself is `SYS-API-*` in `_invariants.md`, which this page cites rather than restating." |
| `modbus.md` | the no-analyzer paragraph | "…only the PublicApi manifest snapshot notices when the marked set changes." | + "and the manifest rule is `SYS-API-*` (`_invariants.md`)" |

`io.md`'s old sentence became false the moment `SYS-API-001` was written, which is why it is
corrected in the same commit rather than left for a reader to trip over.

---

## Drift checkpoints

- 2026-09-06 (a): the consistency review's **totals** are three arithmetic errors — its per-page
  tables sum to **1126 criteria** (not 1246), **206 umbrellas** (not 197) and **68 GAP tails** (not
  70). Every per-page number in the review checks out; only the sums are wrong. The brief inherited
  all three. `spec-trace` independently reports 68 GAP ids, which is the second reading of the third
  one.
- 2026-09-06 (b): the review's "the `WHERE` pattern is used 30 times" is 28 by its own command
  (`grep` over bullet first lines) and 29 once wrapped bullets are joined — the 29th is
  `AC-CLI-012.2`, whose `WHERE` sits on a continuation line.
- 2026-09-06 (c): the review's "3 of 1246 foreign citations sit inside a criterion sentence" is
  **refuted**: zero do. Two mechanisms produced the miscount, not one. Its joiner counted GAP tails as part of the
  criterion — five such tails, four on `analyzers.md` and one on `block-lifecycle.md`. And on
  `block-lifecycle.md` a second quirk compounded it: prose glued to a declaration bullet with no blank
  line between them (`:309-311` and `:424-426`), so a fold that stops at the next blank line swallowed
  the paragraph's foreign ids into the bullet. The blank lines are in as of Tier 2's first commit;
  the conclusion — zero foreign ids in a criterion sentence — held under both readings. Item 4 of the brief therefore had
  nothing to do, and Tier 1 rewords no criterion sentence.
- 2026-09-06 (d): bold inside criterion text is on **four** criteria, not three — the fourth
  (`AC-ANLZ-010.4`) is on a continuation line.
- 2026-09-06 (e): the brief's item 6 asks the pages to cite `SYS-REL-001` and `SYS-API-001` **in
  full**. They cannot in Tier 1. `spec-trace.ps1:49-54` reads every `(?:AC|SYS)-…` token on a traced
  page as a declared id, whatever line it is on, and a declared id with no quoted-literal test
  reference is an orphan. Probed: appending "the manifest rule is `SYS-API-001`" to `modbus.md` gave
  `spec-trace: FAIL - 1 id(s) with no test reference: SYS-API-001`; the same line with `SYS-API-*`
  gave `OK`. The pages cite by family, which is also the skill's own rule for citing a GAP'd id.
  Both rules carry a `GAP:` tail on their declaring bullets, so the marker is already right if Tier 2
  traces the file.
- 2026-09-06 (f): the brief's `SYS-API-001` text says the issue is opened "against the documentation
  repository". `.github/workflows/publish.yml:259-262` runs `gh issue create --repo
  VION-IoT/architecture`. The rule states the architecture repository (`D5`).
- 2026-09-06 (g): the two globs in `introspection.md`'s citation block put two **bare umbrella** ids
  into the traced set — `AC-EMIT-013` and `AC-GATE-010` — so `spec-trace` reports 1071 referenced
  ids where the brief expected an unchanged 1069. Both are covered by their own leaves (a bare
  umbrella is covered by any `.M`), the page GAP count is unchanged at 68 — the run reports 70 while
  this doc is in-flight, because a delta line carrying the marker is exempt-but-counted exactly as a
  page line is (`spec-trace.ps1:116-124`) — and no id was removed. The set
  difference was computed directly: added `AC-EMIT-013`, `AC-GATE-010`; removed none. Writing the six
  leaf ids out in full instead would hold the count at 1069 at the cost of the glob the brief asked
  for; the glob is `io.md`'s own idiom.
- 2026-09-06 (h): the first bold-removal script matched its target spans with PowerShell's `-like`,
  where `*` is a wildcard — `"*$t*"` for `**public**` became `***public***` and matched every line
  containing "public". It reported 55 "spans" and wrote the file before its count assertion threw.
  The tree was reverted with `git checkout` and the remover rewritten to be scoped to
  declaration-bullet lines. Nothing of it survives in the branch; recorded because it is the
  "whole-file tidy-up" failure the pass protocol names, reached through a wildcard rather than a
  regex.

---

## Spec delta (to distill)

> The machine-readable change, one line per id. Grammar:
> `<OP> <ID> -> <target> : <payload>`
>
> - `OP` ∈ `ADDED` | `MODIFIED` | `REMOVED`
> - `target` is **repo-root-relative** (normally `docs/specs/<page>.md`)
> - `payload`: for `ADDED`/`MODIFIED` the EARS text; for `REMOVED` the reason
>
> On the implementation PR each line is applied into the named target; `spec-change.ps1 archive`
> refuses until every line is applied. The `ID` must be an exact token greppable in the target
> after distill (backticks stripped) — a real `AC-`/`SYS-` id, never an ad-hoc label.

Two lines. The archive gate **can** target `_invariants.md`: `spec-change.ps1:119-123` accepts any
`.md` target, `SYS-REL-001` matches its `$specIdRx` (`:73`), and `Get-DeclaringBullet` (`:102-113`)
finds a `` - `SYS-…` `` bullet exactly as it finds an `AC-` one. The `GAP:` tails on both bullets are
stripped by the text comparison, so they do not need repeating here.

Nothing else is in the delta. No criterion sentence changed, so there is no `MODIFIED` line; no id
was retired, so there is no `REMOVED` line. The relabels, the GAP tails, the citation blocks, the
bold and the wrapping are all formatting or prose, and the archive gate compares EARS text.

- ADDED SYS-REL-001 -> docs/specs/_invariants.md : THE SYSTEM SHALL name every packable project of the repository in the release roster the version script clears from the local package cache, so a release publishes the same set the roster names. GAP: nothing reads the packable set out of MSBuild's own evaluation, and a regex over the csprojs is blind to the packable default.
- ADDED SYS-API-001 -> docs/specs/_invariants.md : THE SYSTEM SHALL carry every `[PublicApi]` type of every shipped package in the public-API manifest (`docs/snapshots/publicapi-manifest.json`), regenerated and auto-committed on a pull request and opened as an issue against the architecture repository on `main`, so that a change to the published surface is visible in the diff of one file. GAP: the regenerate-and-commit half runs only inside `.github/workflows/publish.yml`, which no in-process test can construct.

---

## Tasks

> One-commit tasks, each tagged with ≥1 AC id. Ephemeral — they live and die with this change doc.
> Plain list, no checkboxes — the PR's per-task commits are the completion record.

- `T-001` (no id — `spec-process.md` + `spec-lint.ps1` + `spec-lint.tests.ps1`): the label
  vocabulary written down and enforced, and the minting rule.
- `T-002` (37 criteria, no id changes): the relabels.
- `T-003` (13 GAP tails): one GAP-tail shape, and the two relocated reasonings.
- `T-004` (four pages): the citation blocks.
- `T-005` (`SYS-REL-001`, `SYS-API-001`): `_invariants.md`, its four citation sites, bold, wrapping.
- `T-006`: the change doc, the journal line, the archive.

---

## Relay notes for the PR body

Nothing consumer-visible: the corpus's conventions and two invariants. No code, no test, no id.

- One EARS label vocabulary — `(Ubiquitous)` / `(Event-driven)` / `(State-driven)` / `(Unwanted)` /
  `(Optional)` — written into `docs/spec-process.md` § IDs & EARS and enforced by `spec-lint`, with
  a self-test case. Thirty-seven criteria are relabelled from their own first EARS keyword;
  `(Conditional)` leaves the corpus and `(Optional)` enters it. **No criterion sentence changes.**
- One GAP-tail shape across the 68 tails: 13 rewritten, none un-GAP'd, every reason unchanged.
- The opening citation block backfilled onto `plugin-loading.md`, `emission.md`, `config-gating.md`
  and `introspection.md`; no page gains a citation it did not already make.
- `docs/specs/_invariants.md` carries `SYS-REL-001` (the release roster) and `SYS-API-001` (the
  public-API manifest) instead of saying no page exists yet; `testkit.md`, `io.md` and `modbus.md`
  cite them where they had narrated the consequence.
- Bold out of four criteria; `testkit.md`'s bullets wrapped at 100 columns like the other twelve
  pages'.
