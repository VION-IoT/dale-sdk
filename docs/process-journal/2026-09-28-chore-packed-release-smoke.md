2026-09-28 · gate · release-smoke · A smoke work directory under the session scratchpad failed `dale build` with MSB3106 and CS0012: restored assemblies' paths passed 260 characters on Windows. The smoke works under a short temp root instead. (self)

2026-09-28 · gate · release-smoke · An installed tool's `.nupkg.metadata` contentHash in the tool store is not the package file's SHA-512, unlike a restore's, so the first CLI provenance check failed a correct install; it compares the store's byte copy instead. (self)
