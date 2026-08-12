# Retro-0 — mining two months of review corrections

**Date:** 2026-08-12 · **Window:** 2026-06-12 → 2026-08-12 · **Method:** transcript mining

The round that seeded this repo's improvement loop. It exists because dale-sdk had none of the
substrate its sibling repos had grown — no repo-local review, no friction journal, no convention
docs — while shipping 31 releases and 70 merged PRs in the window. The taxonomy below is what
[`/vion-code-review`](../../.claude/commands/vion-code-review.md) § 5 runs as checks.

## What was mined

The local Claude Code transcript store for this repo — `~/.claude/projects/C---gh-dale-sdk/` plus the
two linked-worktree stores — **26 session files**, spanning 2026-06-12 to 2026-08-12. Human turns were
extracted programmatically, subagent sidechains and harness-injected payloads (skill bodies, task
notifications, command expansions, hook output, system reminders) filtered out, and the **288
follow-up turns** — everything the user said after the opening brief — read as the correction corpus.
The 26 opening briefs were read separately, for intent and for what briefs to this repo assume.

Cross-checked against durable sources that do not age out: `gh pr list` (70 merged PRs), `git tag`
(31 releases), the RFC set in `docs/rfcs/`, and the reports the user pasted back into sessions.

**This window will not be re-openable, and it was already partial.** The oldest surviving session
starts 2026-06-12 — the CLI's `.slnx` solution-discovery fix — so everything before it, including the
whole early SDK surface, was gone before this round began. Everything from here is journalled as it happens
([`../process-journal.md`](../process-journal.md)) or it is lost. That is the whole argument for the
journal, and it is why a second mining round is not a fallback plan.

## The taxonomy

Ten shapes, ordered by how often and how expensively they recurred. Quotes are verbatim, including
the typing; each was verified mechanically against the extracted corpus rather than by recall.

**D1 — Surface minimalism.** The loudest, and it always ended in deletion rather than defence. Unlike
the sibling repos' YAGNI, here it lands on the *published surface*: what an SDK adds is permanent for
consumers.
> *"on the generator idea. i think this is overkill for one line of boilerplate, let's avoid it until
> the cont/benefit gets better"* (06-22)
> *"ok, we skip it, can be done in user land via c#, no magic from SDK"* (06-18, on parameterised scenarios)
> *"skip the Order = X attributes, declaration order is enough"* (06-29)
> *"if the sugar is not that much of a win i'd prefer removing it and use even for di/do/ai/ao
> (modbusrtu!) use it the same way as a non-platform authored service provider would"* (06-22)

That last one is the shape at its sharpest: rather than keep four convenient hard-coded HAL scenario
steps beside the new generic mechanism, the user chose to delete them and make the platform's own
service providers use the third-party path. RFC 0010 shipped that way.

**D2 — XML docs terse and reader-facing.** This repo's docs *are* the product; the correction is
always to cut.
> *"keep the ServiceRelationAttribute xmldoc lighter, no rfc mentioning, no analyzer number, no
> customer specific examples. just explain what it is for and how to use it correctly"* (08-04)
> *"technically correct but i find the remarks too verbose. focus on what is important. that
> translation is possible and what is used as keys (and therefore breaks translations when changed)"* (08-06)

The post-correction `ServiceRelationAttribute` is now the reference shape. The paired hazard —
docstrings that are simply wrong — is live in the tree: `ScenarioStep`'s summary in
`Vion.Dale.DevHost/Scenarios/ScenarioFile.cs` still advertises `digitalInput` / `analogInput`, which
RFC 0010 deleted. It was found by this round while verifying, not by any test.

**D3 — Delete, don't deprecate; breaking is cheap while it still is.**
> *"breaking is no problem, the devhost is the only user. can the thing thin wrappers not live in the
> ui code instead of the api?"* (06-22)
> *"IDevhostControl still has SetAnalogInputAsync, GetAnalogOutput and the like methods, are they
> still used? from the hardcoded ui elements? or can they be removed"* (06-22)
> *"let's do it right as long as we can, so i lean (b)"* (07-15)
> *"it would still be possible, i control all users"* (07-13, on making a nullable field required)
> *"no rollout gate, this is pre-public, i'll update sdk and other vion-contracts users right
> afterwards"* (06-15)

**D4 — An author-facing rule ships with an analyzer.** 45 diagnostics (`DALE001`–`DALE045`) exist
because this is the standing answer to "how will an author know?".
> *"The analyzer should be there to surface bad usage like trying to use it for date formats or
> fprint ing numbers."* (06-15)
> *"I can live with nullable strings, but it must be clear to users what formats are valid for which
> type, and the SDK analyzer validating it, right?"* (06-23)
> *"is there a test for the analyzer role that checks that it fires for a [InstantiationParameter]
> that is not a service property,"* (07-13)

The failure mode is coverage, not absence: PR #78 added `Guid` as a supported service-element type,
and PR #79 — the very next one — landed as `fix(analyzer): exempt Guid from DALE016 so Guid service
properties compile`. The supported-type gate is spread across `DALE003`, `DALE016` and `DALE008`, and each
one's tests passed in isolation while the combination rejected the type the SDK now claimed to
support.

**D5 — The analyzer is proven to fire in a real build.** The most expensive single defect in the
window, and it was caught by an implementing session's own scepticism rather than by any test. A
symbol-based check compiled and passed all its unit tests while being *"completely dead in real
builds"*: every logic-block project references Metalama, which replaces the compiler task, and the
`LogicClassGenerator`-emitted contract interfaces are simply not in the compilation analyzers see —
`ChargingPoint : IPing, IToggleable, IChargingStationService` yields `AllInterfaces` with `IPing`
gone entirely. The fix resolves contract interfaces by symbol **and** by name, pinned by a test whose
contract interface is genuinely unresolved. The session's own note: *"Anyone writing a future Dale
analyzer that keys off contract interfaces will hit this."*

**D6 — Validated against a real consumer shape.** The distinguishing check of this repo. The user
does not ask whether the tests pass; he asks whether it works for the shape a consumer actually has.
> *"when IChangeThreshold implementation is in a different assembly (say common.xy) than the logic
> block, is it safe? at compile time and runtime?"* → then, on discovering it was not:
> *"add that to this PR, this is a valid use case that was not working"* (06-24)
> *"one thing to verify before approving: does it work for the multi-service example like this:"* …
> *"are there limits/conventions to this approach that e.g. if chargepoint was not a a service (no
> service properties on it, only the contract implementation)?"* (08-04)
> *"write it, and place yourself in the shoes of the implementer (who aothors ppc service provider or
> something simple like digital input., is it usable? is there enough guidance, documentation,
> examples"* (06-22)

The RFC 0019 request came from a consumer who could not express their topology at all — six relation
halves on one class, and component services whose identifiers the binder could never mint. The
in-repo examples had only the simple shape, which is exactly why it shipped unusable.

**D7 — Introspection verified from the packed artifact.**
> *"check the introspection result (e.g. by looking at the json of the packed energy examples nugpk,
> check if all relation halfs are there, escpecially  check the multi charging station"* (08-04)
> *"can you verify it it there? it is created during pack"* (06-19)

Two mechanisms make the working tree untrustworthy here: the parser runs `BeforeTargets=
"IncludePublishedFilesInPack"` — so `dotnet publish` silently leaves a stale `tools/publish/*.json`,
which *"briefly looked like the migration hadn't worked"* — and that file is gitignored build output,
so there is no checked-in copy to compare against. A separate 06-19 round was spent on a production
upload failing because the packed JSON's filename could not survive a `-preview.1` suffix.

**D8 — Names are explicit and platform-aligned.**
> *"on naming: use serviceProvider, not provider where possible, be explicit"* (06-22)
> *"is the ServiceProviderContractWire attribute only relevant for devhost ,not dale? if so, it should
> be clearer from the attribute name"* (06-22)
> *"maybe it's time to rename done things to the way the dashboard and cloud api name things, logic
> block definitions, vs logic block instances, topology vs logic configuration etc?"* (06-25)

One whole review round on 06-22 consisted of a single line: `* "block" -> logicBlock`.

Since 2026-08-06 this carries a second cost: introspection identifiers are the cloud's translation
keys, so a rename orphans authored translations ([`../identifier-stability.md`](../identifier-stability.md)).

**D9 — Silence is a defect.**
> *"contractMappings in the saved json should not be [], it should contain the mappings"* (06-26)
> *"that's not great, a real trap. still early so we could change it."* (06-24, on `wait` behaving
> differently under stepping than an author would assume — it was removed, not documented)
> *"running the load-management scenario with the real clock (not stepped) leads to an error in the
> advance step. is it not supported? then in should not be possible to run."* (06-24)
> *"the dangling edge leads to errors in devhost:"* … *"is it ok?"* (07-13)

And from the consumer letter the user forwarded on 08-04: three silent failures in the relation path,
of which *"A typo'd `relationType` is currently invisible end to end."* The brief for RFC 0010 had
already named the general form: add *"a positive round-trip test"* … *"not a "no-throw" test
(silent-drop trap at LogicBlockContractBase.cs:111-116)"*.

**D10 — Claims verified, mechanism explained.** The user does not accept a fix without its
explanation, and asks specifically how it slipped.
> *"explain the bug, and why it could slip through, was the rfc silent about it?"* (06-24)
> *"what about the fix. the new code in LogicBlockBase. is it just valdation to fail fast? can it
> false-positive?"* (06-24)
> *"why that change? please explain"* (06-23)
> *"i see reflection used in unit tests to get _serviceBinder etc, is there no better way currently to
> validate the it?"* (08-04)
> *"the emission policy feature was rocky, several follow-up bugs. was the spec phase not sufficient?
> or the design/plan? how to do it better next time?"* (06-25)

## Two process shapes that are not code review

**The DevHost is demonstrated, never asserted.** In nine separate sessions the user asked to see it
running before the PR: *"i want to test it myself with devhost ui before the pr is opened"* ·
*"continue into implementation, let me review before pr with devhost"* · *"better run it gains the
energy examples (with project reference), there is more realistic logic blocks"* · *"the watch
property select is empty, the setup (set) property select looks good. do the smoke test with ui"*.
The `devhost-smoke` skill and the SmokeHost fixture exist because of this; the round found they were
already the strongest piece of substrate in the repo, and left them alone. What was missing was the
*written* obligation, now [`../devhost-conventions.md`](../devhost-conventions.md) § 1.

**Style drift was the most repeated mechanical creak.** Four sightings in the window — *"code style
drift. run code cleanup"* (07-13), *"ci failing with code Style drift"* (07-13), *"code style drift in
the DF-47 PR"* with the CI error pasted naming one file (07-15), and *"CI fails in the vion-contracts
pr (code style)"* (06-23, the adjacent repo but the same shared gate). Two briefs also carried standing
instructions about it, one of them naming the trap directly: run the full cleanup, *"not only
`-Changed`: newly added .cs files slip past `-Changed`"*. See D3 below.

## Decisions

**D1 — Conventions move to activity-triggered docs.** `CLAUDE.md` was a single 150-line file mixing
always-loaded contract with authoring guidance, which is exactly what a review rubric has to
disentangle. Three docs, each triggered by an activity: `sdk-surface-`, `testing-`,
`devhost-conventions.md`. The two nested `CLAUDE.md` files (`Vion.Dale.Cli`,
`Vion.Dale.DevHost.Web`) were already doing this job well and were left untouched — the new
`devhost-conventions.md` links to the SPA one rather than restating it.

**D2 — `/vion-code-review` lands here, carrying D1–D10.** The reason it belongs here is a broken
contract: [`/fix`](../../../architecture/.claude/commands/fix.md) conditions its brief's
definition-of-done on the repo *having* such a command — "where the repo defines a pre-PR review
(PROCESS.md, CLAUDE.md, a vion-code-review command), the brief requires running it before the REPORT,
dispatched as a fresh-context read-only subagent". dale-sdk defined none. Its `CLAUDE.md` named a
*cleanup* step, which is a formatter, not a review. **Every brief sent to this repo has been silently
skipping that step**, while the equivalent brief to mesh, dashboard, cloud-api or dale did not. The
command is ported from cloud-api's, with the scope-resolution and PR-argument handling unchanged and
the taxonomy replaced wholesale.

**D3 — The `cleanup-code.ps1` blind spot is fixed rather than documented.** Two bugs, one cause:
`git diff` sees tracked files only. `-Changed` built its file list from three `git diff` forms, so a
brand-new `.cs` was never cleaned; and the drift check used the same source, so cleanup of an
untracked file reported "Already clean". Untracked, non-ignored files are now folded into `-Changed`'s
scope and hashed either side of the run. **Enforcement ladder: this belonged at the bottom rung.** A
known trap that keeps costing rounds is a tooling bug, not a documentation gap — the same fix landed
in cloud-api at its own retro-0, and the shape is deliberately identical.

**D4 — Two false beliefs about the PublicApi snapshot were retired.** Briefs to this repo have
asserted that `IDevHostControl` is `[PublicApi]` and that DevHost changes force a manifest
regeneration. Neither is true: the type carries no such attribute, and `Vion.Dale.DevHost` declares no
`[assembly: PublicApiNamespace]`, so nothing in it can reach the manifest. A machine-local memory
claimed the reverse of the second fact too — that CI `--exclude`s the TestKits — when CI excludes
`*.Test` and all five TestKits are in the manifest by design. Both are now stated correctly in
[`../sdk-surface-conventions.md`](../sdk-surface-conventions.md) § 8.

**D5 — Machine-local memories are retired into the repos.** Twenty-one memory files held dale-sdk
knowledge that no colleague, CI runner or second workstation could see. See the round's memory audit
below.

**D6 — No `/retro` command yet.** The architecture repo, dashboard, cloud-api and dale all ran manual
rounds before freezing one, on the grounds that a command records which parts of a calibration are
invariant, and one round cannot tell. Copying an unproven command into a fifth repo is the D1 finding
in process clothing. Revisit after retro-1.

## Memory audit

The round found **21** entries in `~/.claude/projects/C---gh-dale-sdk/memory/`. Disposition:

- **False, deleted** — `publicapi-snapshot-scope` claimed CI excludes the TestKits from the snapshot;
  all five are in it. Believing it would have made a session "fix" a non-existent drift.
- **Fixed in code, deleted** — `cleanup-changed-misses-untracked` (D3 above) and
  `metalama-observable-field-keyword-bug` (fixed upstream in Metalama 2026.1.18; DALE029 retired in
  PR #54, and regression-guarded by a test in this repo).
- **Promoted into convention docs** — the analyzer/Metalama gotcha, the supported-type analyzer pair,
  the closed service-element type system, the pack-not-publish trap, the snapshot bot, the scenario
  step vocabulary sites, the DevHost apply/recycle contract, the identifier/translation-key rules, the
  `DaleLocalSource` loop, and the design-docs-in-`docs/rfcs/` rule.
- **Cross-repo, moved to `architecture/`** — the struct-field-order/jsonb finding (a cloud-api defect
  observed from here) and the worktree-vs-main-repo edit-target trap (affects every repo briefed with
  absolute paths).
- **Kept as status, not convention** — the RFC status entries (0004, 0008, 0010, 0012, 0014, 0016/17,
  0019) record what shipped when. That is git's and the RFCs' job; they are superseded by the RFCs
  themselves carrying implemented-by notes.

## Deliberately not done

- **A CI review agent.** dashboard retired both of its automated PR reviewers at its own retro-0: a
  hand-inlined rule digest drifts from the doc graph by construction. Review belongs before the PR,
  in a session where the docs are live.
- **Fixing the `ScenarioStep` doc drift and the uneven `DaleLocalSource` coverage in this PR.** Both
  are named as non-conforming; both are code changes that do not belong in a substrate PR. The first
  is filed as its own task.
- **A `PROCESS.md`.** cloud-api folds the working agreement into `CLAUDE.md` and this repo follows
  that shape; a fourth top-level process file would compete with `CLAUDE.md` for the same job.
- **Enforcing the packed-artifact check mechanically.** There is no cheap gate for "this was verified
  from the nupkg"; it stays a review check plus a PR-description obligation.

## Next round

Trigger: ~6 weeks, or when `review` lines pile up in [`../process-journal.md`](../process-journal.md).
Read everything below the marker in one sitting, plus the PR history since this note, and promote each
recurrence down the ladder — **CI gate or analyzer > `/vion-code-review` check > prose**. In this repo
the ladder has a rung the others lack: a recurring authoring mistake can become a **DALE diagnostic**,
which is cheaper than any review check because it fires in the consumer's build too. Mechanical fixes
land in the retro PR; judgment calls get a recommendation and the user decides. Move the marker, append
a row to [`../process-metrics.md`](../process-metrics.md), and record the round as a dated note here.

The open question this round could not answer: **which of D1–D10 actually fire.** They are mined from
what the user said, not from what the review command catches. Retro-1 has the first real evidence of
that, from journal lines naming D-numbers.
