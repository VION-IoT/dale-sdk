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

2026-09-15 · review · VION-220 · The review found the guard's reason moved into AnalyzerHelper.UnresolvedRoleNamesInAncestry still saying the caller found no [LogicInterface], true for DALE043's caller only; comment-conventions.md's comment-is-a-claim rule should have prevented it, and the reason now holds for both. (self)

2026-09-15 · review · retro-2 record · The branch review found the record claiming the extra path partitioned while seven of its entries sat in no cluster, and its counts carrying no command, one file tally unscoped; CLAUDE.md's count-with-its-command rule and review-checks.md P1 should have prevented it. (self)

2026-09-15 · review · review-checks.md P3 · The branch review found the new P3 clause on docs, skills and CLAUDE.md lines pointing at comment-conventions.md, which covers inline comments only; the harness skill's one-owner rule should have prevented it. (self)

2026-09-15 · review · CLAUDE.md · The branch review found the first-retro instruction left standing after this round read that window whole; the P3 clause on text a change makes false, added on the same branch, should have prevented it. (self)
