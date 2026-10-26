---
slug: vion-sdd-adoption
status: proposed           # proposed | in-flight | parked | archived
blocked-on: none           # for parked docs: what's blocking + ref
areas: process             # the process itself, as in 2026-09-01-sdd-process.md
author: jonasbertsch (drafted with Claude in the session `sdk: sdd plugin migration`)
created: 2026-10-26
updated: 2026-10-26
supersedes: none           # path of a superseded change doc, or none
---

# dale-sdk on the vion-sdd plugin: the gates from architecture, the in-repo copies retired

> Change doc — a process change that moves no criterion. Archived by the session that lands the
> last task (`spec-change.ps1 archive vion-sdd-adoption`, the plugin's copy once `T-006` has retired
> this repo's). Process: `docs/spec-process.md`. Never put change narrative inline in a spec page —
> it lives here.

## At a glance

### Summary

dale-sdk is where the SDD method started and where the `vion-sdd` plugin was cut from, yet it still
runs its own copies: three gate scripts, a change-doc template, and a 72 kB playbook whose lane-3
recipe is now the plugin's `pass` skill. dale and hardware-integration already run the plugin. Here
dale-sdk becomes a consumer too: CI reaches the plugin with a GitHub App token, because a public
repository may not use a private one's action; the desk runs it from the architecture checkout; the
in-repo copies retire. The corpus is untouched.

### Spec implications

None. No criterion is added, reworded or removed: the corpus passes the plugin's two gates as it
stands, with the same counts as this repo's own gates (§ The corpus under the plugin's gates). What
changes is process. `docs/specs/_corpus.yml` is new. `docs/spec-process.md`,
`docs/changes/_template.md`, `docs/testing-conventions.md`, `CLAUDE.md` and
`.claude/commands/check.md` are rewritten in part. `.github/workflows/spec-gates.yml`,
`scripts/check.ps1` and `scripts/test-style-lint.ps1` change with their self-tests. Three gate
scripts and their self-tests go. Every page's `Process: ../spec-process.md` link, and every code
comment pointing at `spec-process.md` § IDs & EARS, stays valid: those sections keep their names.

### Decisions

- `D1` **(a) ratified** — **CI reaches the plugin through a checkout made with a GitHub App
  token.** dale-sdk checks out `plugins/vion-sdd` from architecture in a job of its own and runs the
  plugin's composite action from that checkout: the action dale and hardware-integration run, at
  architecture's `main` (operator, 2026-10-26, session `sdk: sdd plugin migration`). Rejected, in
  that session: the gates moved or published to the public `shared-workflows`, which re-points three
  private consumers and splits the plugin's authorship across two repositories; the whole plugin
  made public in a repository of its own; a personal access token, which has a person behind it —
  the reason dale's `T-017` rejected the token route. § Why a checkout, and why an App.
- `D2` — **The corpus config is the one drafted below, and its header is the plugin's template's.**
  `owns:` lists the fifteen roster codes and the two `SYS-` families `_invariants.md` declares;
  `tests:` is the scan `spec-trace.ps1` hardcodes today, written as globs; `cites: [dale]` covers the
  page citations this repo already makes. It reproduces today's gate results exactly
  (§ The corpus under the plugin's gates), so adopting it changes what the gates see by nothing.
- `D3` — **The three gate scripts and their self-tests retire.** `scripts/spec-lint.ps1`,
  `spec-trace.ps1`, `spec-change.ps1` and their `*.tests.ps1`. Nothing in this repository calls them
  once `T-004` re-points `check.ps1`; the plugin's copies are a superset, except one rule (`D6`).
- `D4` — **`docs/spec-process.md` is re-based on the plugin's `templates/spec-process.md`.** The
  generic sections take the template's text. dale-sdk keeps what is its own: the roster, the finer
  minting granularity it has always used (the template provides for exactly that), the three citation
  forms of its frameworks, the dispatch envelope of a lane-1 or lane-2 brief, § Routing, its gate
  rows and the `check.ps1` paragraph — and gains **§ What a pass in this repo adds**, for the
  SDK-specific rules of the lane-3 recipe, the place dale's § dale's rig and obligations is in dale.
  The recipe itself is the plugin's: dale-sdk's lane-3 text has changed by two pointer lines since
  2026-09-18, when dale's migration began cutting the plugin from it (`git log --since=2026-09-18 --
  docs/spec-process.md`: `f1193e4f`; `b4bf8214` and `842aa259` touched the roster, § IDs & EARS and
  § Gates), while the plugin's has taken its consumers' rounds (plugin `CHANGELOG.md` § 0.1.0).
- `D5` — **The roster's Tier column goes.** Every roster page is traced (`spec-process.md:42-45`),
  so the tier records only how deep an extraction once reached, which git and the archived pass docs
  already record. What Tier C exempted stays, as the template's "what is exempt" paragraph.
- `D6` — **The cross-tier check moves into `test-style-lint.ps1`, as a failure.** It is the one rule
  this repo's `spec-trace` has and the plugin's does not (`scripts/spec-trace.ps1:166-224`): a test
  file citing an id a committed scenario also claims carries a "Cross-tier" clause. It is test
  discipline (`testing-conventions.md:483-484`), which is the style gate's subject, not the trace's.
  It fails rather than warns because `test-style-lint` is a failing gate and the tree carries no
  instance today: this repo's `spec-trace` on `4a0b0fd3` prints no cross-tier note.
- `D7` — **`check.ps1` runs the plugin's gates from an architecture checkout, on journal-lint's
  rules.** A checkout beside this one, or `-ArchitectureRoot`; with none the two gates **fail**
  rather than skip (`check.ps1:14-20`, `194-196` for the shared-workflows precedent). They are
  **partial** when the plugin they would run differs from the one CI runs — its tree, not the
  checkout's commit, because architecture's `main` moves far more often than the plugin does: 127
  commits since 2026-09-26, 7 of them under `plugins/vion-sdd` (`git log --oneline
  --since=2026-09-26 origin/main [-- plugins/vion-sdd] | wc -l`). A commit rule would make most desk
  runs partial with the rules identical to CI's, and a state everyone learns to ignore is no state.
  § The desk.
- `D8` — **Archived change docs are not swept.** Their links to retired sections and scripts are
  history; `sweep-residue-lint` and `self-reference-lint` already hold `docs/changes/archive/` out of
  scope.
- `D9` — **The new job is not a required check.** Branch protection on `main` requires `ci` alone
  (`gh api repos/VION-IoT/dale-sdk/branches/main/protection`: `contexts: ["ci"]`); the `spec-gates`
  job is not required today, and the new job takes the same standing.
- `D10` — **`CLAUDE.md` names the plugin's two skills and the `pass` skill's four roles, as dale's
  does, and ends no larger than it starts.** It is 11,223 bytes against a 10 kB budget before this
  change; the rows it loses (the lane-3 and launcher wording) pay for the rows it gains.

### Reviewer's questions

1. **(c) Which GitHub App mints the token?** (i) A dedicated App with `contents: read`, installed on
   architecture alone — recommended: the release lane's App keeps its installation to itself, and a
   leaked dale-sdk secret reads one repository. (ii) `COMPONENT_RELEASE_APP`, artifacts-mender's,
   with architecture added to its installation — no new App, but one key then serves two lanes.
   Either way its client id and private key become two dale-sdk secrets (proposed:
   `SDD_READ_APP_CLIENT_ID`, `SDD_READ_APP_PRIVATE_KEY`). Creating or installing an App and adding the
   secrets is an organisation-admin step: `T-001`, routes to a human.
2. **(b) `D6`'s cross-tier rule fails rather than warns.** The alternatives are porting it warn-only,
   as it is today, or dropping it; flagged because it turns a note into a gate.
3. **(b) `D5` drops the Tier column.** `testing-conventions.md:476` says "On a Tier A area", which
   becomes "on a traced page" — every page.
4. **(b) The action floats with architecture's `main`, as at dale and hardware-integration.** A
   plugin change can therefore redden dale-sdk's CI with no dale-sdk commit; the other consumers
   accept that, and `D7`'s partial state makes a desk run that differs from CI's visible. Pinning a
   commit would trade that for a bump per plugin change.

---

## Full design

### Why a checkout, and why an App

GitHub lets a private repository share its actions with the organisation's other repositories, and
qualifies it: "Access is allowed only from private repositories" (docs.github.com, *Managing GitHub
Actions settings for a repository*).
architecture is private and dale-sdk public (`gh repo view VION-IoT/<repo> --json visibility`), so
the line dale and hardware-integration use, `uses: VION-IoT/architecture/plugins/vion-sdd/action@main`
(dale `.github/workflows/dale.yml:144`, hardware-integration `.github/workflows/checks.yml:42`),
cannot run here. dale's migration left exactly this open: "Whether a public repository in the
organization may reference it the same way is unproven until dale-sdk opts into the plugin" (dale
`docs/changes/archive/2026-09-18-sdd-migration.md` § Drift checkpoints, 2026-09-24).

A checkout of a private repository needs a credential. This repository's CI already holds two:
`ARCHITECTURE_REPO_PAT`, which opens issues in architecture when the public API or the CLI help
changes (`publish.yml:336-404`), and `DOCS_REPO_PAT`, which pushes the API reference to
documentation (`publish.yml:420-436`). Both are personal access tokens, set on 2026-05-12
(`gh secret list -R VION-IoT/dale-sdk`), and both run on a push to `main` only. A pull-request gate
runs on every pull request, so it takes an **App installation token** instead: minted per run, valid
for an hour, scoped to one repository with `contents: read`, with no person behind it. The
organisation already mints them that way: artifacts-mender's
`.github/workflows/vion-gateway-suite-windows.yml:185-199`, with `actions/create-github-app-token`
pinned by commit because it is handed the App's private key.

### The job

A second job in `.github/workflows/spec-gates.yml`, beside `spec-gates` and `journal-lint`:

```yaml
  vion-sdd:
    if: github.actor != 'github-actions[bot]'
    runs-on: ubuntu-latest
    permissions:
      contents: read
    steps:
      - uses: actions/checkout@v4
        with:
          fetch-depth: 0                      # the lint's narrative rule needs the merge base

      - name: A read token for architecture
        id: token
        uses: actions/create-github-app-token@<commit>   # pinned by commit: it holds the private key
        with:
          client-id: ${{ secrets.SDD_READ_APP_CLIENT_ID }}
          private-key: ${{ secrets.SDD_READ_APP_PRIVATE_KEY }}
          owner: ${{ github.repository_owner }}
          repositories: architecture
          permission-contents: read

      - name: The vion-sdd plugin, from architecture's main
        uses: actions/checkout@v4
        with:
          repository: VION-IoT/architecture
          ref: main
          token: ${{ steps.token.outputs.token }}
          path: .vion-sdd
          sparse-checkout: plugins/vion-sdd
          persist-credentials: false

      - uses: ./.vion-sdd/plugins/vion-sdd/action
        with:
          base: ${{ github.base_ref || 'main' }}
```

What each choice holds:

- **A job of its own**, so the `spec-gates` job's file-walking lints never see `.vion-sdd/` and the
  plugin's markdown is never held to this repository's prose rules.
- **The same triggers and bot guard as `spec-gates`** (`spec-gates.yml:9-17`). On a push to `main`
  the action's merge-base check passes, the narrative rule compares nothing, and the lint's other
  rules and the trace run — today's push-to-main shape (`spec-gates.yml:55-63`).
- **`ubuntu-latest`**, as every job in this repository's workflows runs (`grep -n runs-on
  .github/workflows/*.yml`); dale and hardware-integration run this action on self-hosted runners,
  which nothing here uses.
- **The corpus walk finds one config.** The plugin's scripts discover `_corpus.yml` by walking the
  tree and refuse two; architecture tracks none (`git -C ../architecture ls-files | grep -c
  _corpus.yml` → `0`), and the action's fixtures live in its suites' temp folders.
- **The token reads one repository and is not kept**: `repositories: architecture`,
  `permission-contents: read`, `persist-credentials: false`, a sparse checkout of the plugin alone.
- **It fails closed.** A missing, revoked or expired key reddens the job; it never skips. A pull
  request from a fork gets no secrets and goes red the same way, and `close-external-prs.yml` closes
  it anyway.

### The desk

`check.ps1` derives its gate list from `spec-gates.yml` (`check.ps1:113-131`) and learns one more
step shape: `uses: ./.vion-sdd/plugins/vion-sdd/action`. It reports that step as the two gates it
runs, `spec-lint` and `spec-trace`, under those names, so the lines a PR body carries read as they
do today and `check.tests.ps1`'s cases for them (Cases 1b, 3b, 6, 6b) keep their subject. Each runs
from `<ArchitectureRoot>/plugins/vion-sdd/scripts/` with today's arguments (`check.ps1:95-97`):
`-RepoRoot`, and `-Diff <base>` for the lint when the base resolves.

`-ArchitectureRoot` defaults to `../architecture`, the checkout dale's `/check` runs the same scripts
from (dale `.claude/commands/check.md:9,13`). The states follow journal-lint's, for the reason that
journal-lint gives (`check.ps1:16-20`):

| at the desk | `spec-lint` / `spec-trace` |
| --- | --- |
| no checkout at the root | **FAIL**, naming the root and `-ArchitectureRoot` — a skipped gate reads as a green one |
| `HEAD:plugins/vion-sdd` is not the tree of the commit `git ls-remote origin refs/heads/main` names | runs, **PARTIAL**: its rules differ from CI's |
| that commit is not in the checkout, so its tree cannot be read | runs, **PARTIAL**, naming `git -C <root> fetch origin` |
| uncommitted changes under `plugins/vion-sdd` (`git -C <root> status --porcelain -- plugins/vion-sdd`) | runs, **PARTIAL**: it ran rules CI does not have |
| base ref does not resolve | the lint runs without `-Diff`, **PARTIAL**, as today |

`check.ps1` reads and never fetches, as it does for shared-workflows: a gate run that writes into
another repository's refs is a side effect nobody asked for.

The plugin's `change` skill runs the same scripts from `${CLAUDE_PLUGIN_ROOT}`, the installed
plugin's copy, which can sit at yet another commit. The `check.ps1` run is the one the PR body
carries (`CLAUDE.md` § Pre-PR obligations).

### The corpus under the plugin's gates

`docs/specs/_corpus.yml` below its header, which is copied from the plugin's `templates/corpus.yml`
as dale's and hardware-integration's are:

```yaml
owns: [ANLZ, EMIT, BIND, GATE, INTRO, SCEN, CTRL, PLUG, HOST, CLI, LIFE, MODB, IO, TKIT, HTTP, API, REL]
tests:
  - "**/*.Test/**/*.cs"
  - "**/*.Test/**/*.scenario.json"
  - "Vion.Dale.DevHost.SmokeHost/**/*.cs"
  - "Vion.Dale.DevHost.SmokeHost/**/*.scenario.json"
cites: [dale]
```

- `owns:` — the roster's fifteen codes, and `API` and `REL`, the `SYS-` families `_invariants.md`
  declares (`` grep -ohE '`(AC|SYS)-[A-Z0-9]+-' docs/specs/*.md | sort | uniq -c ``).
- `tests:` — what `spec-trace.ps1` scans today: every `*.Test` directory anywhere, plus the SmokeHost,
  for `*.cs` and `*.scenario.json` (`scripts/spec-trace.ps1:166-169`, `178`, `197`).
- `cites: [dale]` — the pages carry thirteen `dale/AC-…` citations, the `HOST` page's proofs only the
  host runtime can give (`grep -ohE '\b[a-z][a-z0-9-]+/(AC|SYS)-[A-Z0-9]+-[0-9]+(\.[0-9]+)?'
  docs/specs/*.md`).

Run over `git archive 4a0b0fd3` with that file at `docs/specs/_corpus.yml`, against architecture at
`f5e259b`:

```text
pwsh -NoProfile -File ../architecture/plugins/vion-sdd/scripts/spec-lint.ps1 -RepoRoot <export>
spec-lint: OK
pwsh -NoProfile -File ../architecture/plugins/vion-sdd/scripts/spec-trace.ps1 -RepoRoot <export>
spec-trace: OK - 1223 id(s) all referenced by tests (16 traced page(s), 414 file(s) scanned; 73 GAP id(s) exempt: …)
pwsh -NoProfile -File scripts/spec-trace.ps1 -RepoRoot <export>
spec-trace: OK - 1223 id(s) all referenced by tests (16 traced page(s); 73 GAP id(s) awaiting tests: …)
```

Neither run printed a note: no `GAP` id is cited by a test, and no file misses a cross-tier clause.

What the plugin's scripts do that this repository's do not, read off `diff -u` of each pair: the lint
fails a bare id outside `owns:` and a citation naming a repository outside `cites:`, and skips fenced
blocks; the trace counts owned ids only, fails when `tests:` matches no file, and names a `GAP` id a
test cites; the archive refuses a delta minting outside `owns:`, flips the frontmatter's `status:`
alone, and rewrites links to the archived path. What this repository's do that the plugin's do not:
the cross-tier note alone (`D6`).

### Where each piece of the in-repo tooling goes

| Piece (at `4a0b0fd3`) | Goes to | Task |
| --- | --- | --- |
| `scripts/spec-lint.ps1`, `spec-trace.ps1`, `spec-change.ps1` and their self-tests | deleted; the plugin's scripts, through the `vion-sdd` job, `check.ps1` and the `change` skill | `T-006` |
| the cross-tier note, `spec-trace.ps1:166-224` | `scripts/test-style-lint.ps1`, failing | `T-005` |
| the `Spec lint` and `Spec trace` steps, `spec-gates.yml:55-68` | the `vion-sdd` job | `T-003`, `T-006` |
| `docs/changes/_template.md` | the plugin's `templates/change-doc.md`, three lines away from it | `T-007` |
| `spec-process.md` § The corpus, its Tier column and tier prose (`11-49`) | the template's text, the roster without the column, the Tier C list as the exempt paragraph | `T-008` |
| § IDs & EARS (`65-146`) | the template's text, keeping this repo's three citation forms (`90-104`) and stating its finer granularity; the cross-tier paragraph (`129-130`) leaves for `testing-conventions.md` | `T-008` |
| § Change docs (`148-186`) | the template's text | `T-008` |
| § Lanes, lanes 1 and 2 (`188-272`) | the template's text, keeping the lane-1/2 dispatch envelope (`211-233`) and the review-round pointers to `/vion-git:pr` | `T-008` |
| § Lane 3 (`274-805`) | the plugin's `pass` skill, and § What a pass in this repo adds — every rule placed by the table below | `T-008` |
| § Routing (`806-818`) | kept | — |
| § Gates (`820-858`) | the template's three plugin rows, this repo's other rows, the `check.ps1` paragraph re-pointed | `T-008` |
| `testing-conventions.md:476-484` | "on a traced page"; the cross-tier clause enforced by `test-style-lint` | `T-009` |
| `CLAUDE.md` § Read before you write, § Lanes in this repo, § Skills in this repo | `D10` | `T-009` |
| `.claude/commands/check.md` | names the architecture checkout beside the shared-workflows one | `T-009` |
| `Vion.Dale.DevHost.Test/Scenarios/ScenarioDefinitionSitesShould.cs:366` | "Tier C" reworded to the exempt set | `T-009` |

The code comments pointing at `spec-process.md` § IDs & EARS (`Vion.Dale.Cli.Test/ProgramShould.cs:53`,
`Vion.Dale.Sdk.TestKit.Test/TestKitSurfaceShould.cs:165`,
`Vion.Dale.Sdk.Test/Core/PresentationAttributeShould.cs:14`) and the two that name `spec-trace`
(`Vion.Dale.DevHost.Xunit.Test/ScenarioFilesAttributeShould.cs:71`, `scripts/test-style-lint.ps1:236`)
stay true as written.

### The lane-3 recipe's retirement — where every rule goes (`T-008`)

`spec-process.md:274-805` is deleted. `T-008` writes a table here, as the SDD closeout did when the
pass skill retired (`archive/2026-09-07-sdd-closeout.md` § Skill retirement — where every rule
went): one row per rule, and its owner, which is exactly one of

1. **the plugin** — the `references/pass/*.md` file or `templates/check-*.md` that states it now;
2. **§ What a pass in this repo adds** — a rule this repository keeps: one that names its artifacts
   or tools, or a generic rule the plugin does not state, marked *until the plugin takes it* and
   proposed to the plugin, so no rule is lost for want of an owner;
3. **dropped**, with the reason — `D11` of the plugin's cut, the round-2 targeted read, is the
   standing example (plugin `README.md` § One step of lane 3 is deliberately absent).

The rules known to be this repository's, as a starting list and not the table:

- the self-check against the PublicApi manifest's entries for the area (`spec-process.md:421`);
- the gate list a REPORT pastes: `/cleanup`, the CI-shape run, Stryker.NET (`574-583`);
- the sweep discipline's pointer to `sweep-residue-lint.ps1` (`528-544`) — the rule itself is the
  plugin's (`references/pass/5-implement.md` § Sweep discipline);
- the review priorities beyond the template's, which name `docs/snapshots/publicapi-manifest.json`
  and the cross-tier clause (`765-795`).

A dozen other lane-3 rules spot-checked against the plugin were all stated there, so the generic
rules of owner 2 may be none.

### What this does not do

- Move a criterion, or touch a spec page beyond the corpus config beside them.
- Change architecture. What this work surfaced for the plugin — the README's *Verified on* row still
  saying no repository has run a pass, and any generic rule `T-008` keeps under owner 2 — is
  proposed to the plugin on its own.
- Replace `ARCHITECTURE_REPO_PAT` or `DOCS_REPO_PAT`; an App could, later.
- Change required checks (`D9`), or `.claude/settings.json`: the plugins are enabled per user, and
  the file carries no `enabledPlugins` today.

### Verification

- **The desk:** `check.ps1` after `T-006`, its `spec-lint` and `spec-trace` lines coming from the
  plugin, with the counts above.
- **CI, green and once red:** the `vion-sdd` job green on the pull request; then one commit that
  strips the `SHALL` from a criterion reddens it and is reverted before merge. Both run links go in
  the PR body, so the job is shown to gate, not just to run.
- **The cross-tier rule, red first:** `T-005`'s self-test case is run against the script before the
  rule exists and observed failing.
- **Not run here, routes to a human:** `T-001` — the App and its two secrets.

---

## Drift checkpoints

> One line per divergence discovered during implementation:
> `YYYY-MM-DD: <what changed and why>`. Never inline in a spec page. A checkpoint that fixes a
> CLASS bug states the sibling sweep (done / N/A / handed off).

- _(none yet — append as implementation diverges from Full design)_

---

## Spec delta (to distill)

> No criterion changes. This doc's own changes are process documents and scripts, each a task
> below, so the archive gate has nothing to compare.

---

## Tasks

> One-commit tasks, each tagged with the decision it lands; this change mints no id. Ephemeral —
> they live and die with this change doc. Plain list, no checkboxes — the PR's per-task commits are
> the completion record. **No task starts before this doc is ratified**: reviewer's question 1 is
> propose-and-wait, and `CLAUDE.md` § Lanes ratifies a feature-sized doc with an open question
> before code. After ratification `T-001` is a human's, `T-003`'s job goes green only once it is
> done, and every other task proceeds at the desk meanwhile.

- `T-001` (`D1`): **STOP — routes to a human.** The App, per reviewer's question 1's outcome:
  created or extended, installed on architecture with `contents: read`, its client id and private
  key added to dale-sdk as the two secrets the job names.
- `T-002` (`D2`): `docs/specs/_corpus.yml`, the template's header and the config above.
- `T-003` (`D1`): the `vion-sdd` job in `spec-gates.yml`, `actions/create-github-app-token` pinned
  by commit; the workflow's header comment names it.
- `T-004` (`D7`): `check.ps1` derives the step, runs both gates from `-ArchitectureRoot`, fails with
  no checkout and is partial in the three states § The desk names; `check.tests.ps1` proves each.
- `T-005` (`D6`): the cross-tier rule in `test-style-lint.ps1`, its self-test case red first.
- `T-006` (`D3`): the three scripts and their self-tests deleted, the two steps out of the
  `spec-gates` job; `run-script-tests.ps1` green with no exemption added.
- `T-007` (`D4`): `docs/changes/_template.md` from the plugin's `templates/change-doc.md`.
- `T-008` (`D4`, `D5`): `docs/spec-process.md` re-based, § What a pass in this repo adds, and the
  where-every-rule-goes table in this doc.
- `T-009` (`D5`, `D6`, `D10`): `testing-conventions.md`, `CLAUDE.md`, `.claude/commands/check.md`,
  the `ScenarioDefinitionSitesShould.cs` comment.
- `T-010`: the CI proof (§ Verification), gates pasted, every reviewer's question carrying its
  `OUTCOME`, and the archive.
