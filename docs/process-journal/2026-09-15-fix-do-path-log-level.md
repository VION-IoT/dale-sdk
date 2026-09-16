2026-09-15 · brief · do-path-log-level · The amendment asked for a test pinning the raised log level, hedged on the repo testing log levels; testing-conventions.md § 15 forbids asserting on log calls in SDK tests, so the level and the once-per-configuration rate limit ship unproven and the question is open. (self)

2026-09-15 · gate · AnalyzerWiringShould · Six RunDaleAnalyzersOverTestKits rows failed saying the Dale analyzers did not run over each kit, which reads as a wiring regression; the probe build's CS8034 showed the analyzer assembly itself blocked by the host's Application Control policy. (self) → codified: docs/testing-conventions.md

2026-09-15 · review · do-path-log-level · The review found the raised warning reachable before the handler's first link map, where the runtime links block actors before contracts, so a block writing from Ready() on a correctly mapped gateway would draw it; io.md's own standard says nothing routine reaches a warning arm. (self) → codified: docs/specs/io.md

2026-09-15 · review · do-path-log-level · The review found the clear's comment giving two cases it does not enable, a fixed mapping never reaching the arm and a different contract never in the set; comment-conventions' comment-is-a-claim rule should have prevented it. (self)

2026-09-15 · brief · do-path-log-level · The amendment read as a bench finding of a dropped write; the coordinator confirmed the DO path was working and the symptom never reproduced, the premise being a code reading, so the change makes a possible drop visible rather than explaining a seen one. (self)

2026-09-15 · decision · do-path-log-level · The raised warning and its once-per-link-map limit ship pinned by no test, testing-conventions.md § 15 forbidding log assertions in SDK tests and no exception being carved, because pinning a trace's level would make it the contract io.md says it is not.
