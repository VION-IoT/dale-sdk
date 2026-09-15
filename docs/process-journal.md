# Process journal

Where the process creaked, one line per event. Not a work log: what shipped is in git.

## Format

```
YYYY-MM-DD · <where> · <topic or —> · <what happened, one line> [ (second ask)] [ (self)] [ → codified: <path>]
```

- `where` is one of five: `review` a correction to produced work · `gate` tooling fought or false-passed · `brief` upstream was wrong · `decision` a settled point, with its reason · `manual` a human grumble.
- `topic` names what it is about — a component, a skill, a document slug, an issue key — or `—`.
- `(second ask)`: the same thing was asked for twice. `(self)`: the agent found it, not a human.
- `→ codified: <path>`: the file a rule or fix for this entry landed in.
- An entry says what was produced, what was wrong, and what was asked instead. No reasoning, no fix, no quote. At most 400 characters.
- A physical line that does not start with a date continues the entry above. Blank lines between entries are allowed.
- Written by the agent in the commit that carries the fix, for every `where` but `manual`. A fixed review finding counts, and its entry names the finding and the file that should have prevented it.
- Newest last, below the retro marker. Entries above the marker have been read by a retro.

## Entries

<!-- retro-2 marker -->

2026-09-15 · review · spec-gates.yml · The review found two comments still promising the packed-assembly self-test on every pull request, check.tests.ps1's cost stated as half a total it now sets, the workflow's place in the scope filter given the wrong reason, and the runner allowing any repository read the filter does not list. (self)

2026-09-15 · review · AnalyzerWiringShould · The batched rewrite judged every keep-out test on the whole ordinary build's output, so a probe leaking into Modbus.Rtu alone failed all ten; a mutation run showed it, and each project is now judged on the lines MSBuild attributes to it. (self)

2026-09-15 · gate · AnalyzerWiringShould · /check -Test failed the output guard after its fingerprints moved to bracket every set in ClassInitialize: dotnet test without --no-build was still building the same Debug outputs; the bracket is back around the guard's own builds. (self) → codified: Vion.Dale.Sdk.Generators.Test/AnalyzerWiringShould.cs

2026-09-15 · review · AnalyzerWiringShould · The review found the I/O row and the guard's diff never re-proven red after the rewrite, a shared exit code judged per row, doc claims about Built and the probe half that the code does not make, and two convention docs still naming dotnet build; mutations were run and the rest fixed. (self)

2026-09-15 · review · AnalyzerWiringShould · The review found the narrowed fingerprint bracket still racing a concurrent solution build, shrunk not removed; the guard now fails only on files carrying its own version stamp, which no other build writes. It also found the probe half unchecked. (self) → codified: Vion.Dale.Sdk.Generators.Test/AnalyzerWiringShould.cs

2026-09-15 · review · AnalyzerWiringShould · The review found the stamp scan able to throw on a file a concurrent build holds open, and its self-check passing on one stamped file where the guard needs all four graph assemblies stamped; both fixed, and the guard run twice beside a looping build. (self) → codified: Vion.Dale.Sdk.Generators.Test/AnalyzerWiringShould.cs

2026-09-15 · review · VION-132 · The regression test for the dynamic-assembly contract search was named BindContractWhileDynamicAssemblyIsMidEmission; test-style-lint refused the filler word Is, which testing-conventions.md section 12 already forbids, and the name became BindContractDuringDynamicAssemblyEmission. (self)

2026-09-15 · review · VION-132 · The review found the AC-BIND-008.3 fixture comment blaming the never-loaded base assembly while each image loaded from bytes into its own context, so the declaring assembly was unresolvable too and the base did nothing; comment-conventions.md asks a mechanism comment to be verified. (self)

2026-09-15 · review · VION-132 · The review found module and type names in both new ContractFactoryShould fixtures written as literals no assertion reads; testing-conventions.md section 14 asks for Guid strings there. (self)

2026-09-15 · review · retro-2 record · The branch review found the record claiming the extra path partitioned while seven of its entries sat in no cluster, and its counts carrying no command, one file tally unscoped; CLAUDE.md's count-with-its-command rule and review-checks.md P1 should have prevented it. (self)

2026-09-15 · review · review-checks.md P3 · The branch review found the new P3 clause on docs, skills and CLAUDE.md lines pointing at comment-conventions.md, which covers inline comments only; the harness skill's one-owner rule should have prevented it. (self)

2026-09-15 · review · CLAUDE.md · The branch review found the first-retro instruction left standing after this round read that window whole; the P3 clause on text a change makes false, added on the same branch, should have prevented it. (self)

2026-09-15 · review · VION-222 · The change doc's question 3 named a refused write and a failed start as the effects of the stepped timeout-continuation window; the operator's amendment found both decided by real-clock waits elsewhere and made closing the window conditional on a reachable red test, which there was not.

2026-09-15 · review · VION-222 · The drafted stepped criteria promised delivery before "the step that issued the request" returns; the amendment reworded them to before the clock next advances and before an advance in progress returns, because a step spans many hops and a request can be issued outside any advance.

2026-09-15 · gate · mutation runner · A scripted mutation restored its source file but not its build, and a later --no-build suite run ran the mutated binaries, reporting eight failures across two suites as regressions until a rebuild turned them green. (self) → codified: docs/testing-conventions.md

2026-09-15 · gate · test-style-lint · A new cited test was named NameHeldExchangeWhenQuiescenceBudgetIsSpent; the lint failed it for the filler word and three uncited premise tests carried the same shape. (self)

2026-09-15 · review · VION-222 · The branch review found AdvanceAsync and the barrier summary defining quiescence without open exchanges, an absolute "every settle" fallback comment, measurement counts pasted without their commands, and the teardown drain choice and the FluentModbus ordering proven by nothing committed; comment-conventions should have caught the first two. (self)

2026-09-15 · review · VION-222 · The hosted HTTP server opened its stepped exchange at accept, so an idle connection from outside the host could hold a settle for the 10 s read bound; amendment 2 moved the open point to the end of the request's full read.

2026-09-15 · gate · Bash tool · Two Python edit scripts passed through a quoted Bash heredoc failed with "unexpected EOF while looking for matching quote" and applied nothing; the same scripts written to a file ran. (self)

2026-09-15 · review · VION-222 · The re-review found the stop test asserting a signal its Arrange had awaited, the FluentModbus ordering pinned for register writes while the server also takes coil writes, the server exchange named without its endpoint, and a transport test summary naming one premise for three tests. (self)

2026-09-15 · review · VION-222 · The follow-up review found the write-ordering test labelled Act and Assert where the two cannot separate, and the server exchange's new endpoint in its name asserted by no test. (self)

2026-09-15 · review · VION-222 · The third review found the server exchange-name assertion sharing a counting test, the HTTP client exchange name pinned by no test, and a stored-value assert under a combined Act / Assert marker. (self)
