2026-09-28 · gate · release-smoke · A smoke work directory under the session scratchpad failed `dale build` with MSB3106 and CS0012: restored assemblies' paths passed 260 characters on Windows. The smoke works under a short temp root instead. (self)

2026-09-28 · gate · release-smoke · An installed tool's `.nupkg.metadata` contentHash in the tool store is not the package file's SHA-512, unlike a restore's, so the first CLI provenance check failed a correct install; it compares the store's byte copy instead. (self)

2026-09-28 · brief · VION-210 · The brief read all seven AC-CLI-005 criteria as reached by the smoke; a green run reaches only the scaffolding half of .2 and .6, so only those two GAP tails name it. The refusal and restore-failure paths stay unexercised. (self)

2026-09-28 · decision · AC-CLI-005.8 · The pack-time template rewrite and its 0.0.0 guard are minted as a new leaf rather than folded into .7: they are one rule about what dale new scaffolds, while .7 is the run-time notice, and the smoke reaches the rewrite but never .7.
