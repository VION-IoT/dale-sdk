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

<!-- retro-1 marker -->

2026-09-13 · brief · process-unification · The brief put the live window at 59 entries; the file held 54 below the retro-1 marker, by the old journal-lint and by a count of dated lines alike, so the archive carries 54.

2026-09-13 · brief · process-unification · The brief repointed every citation of the review command's § 7 at docs/review-checks.md, but § 7 was the pull request body's record of a review round, which the checks file does not hold; those citations now point at the PR template's Verification line.

2026-09-13 · gate · check.ps1 · /check derived its gate list from ./scripts/*.ps1 steps only, so a spec-gates.yml job using a shared-workflows action would have been invisible to it; shared actions are now derived too and a CI-only one prints SKIP. (self) → codified: scripts/check.ps1

2026-09-13 · review · CLAUDE.md · The review found the rotated window unnamed for the first retro, codify triggered from a third place in the trigger table, and a heading with no blank line above it; the plugin harness skill should have caught the second. (self)

2026-09-13 · review · spec-process · The review found the REPORT's Review section said to be the same text as the Reviewed at line, which holds only accepted findings; the lane-3 adversarial review named as the review skill when the appendix prompt runs; and releasing.md citing a numbered rule the block removed. (self)

2026-09-13 · review · check.ps1 · The review found the empty-derivation failure message still naming only ./scripts/*.ps1 steps after shared-workflows actions were derived too. (self)

2026-09-13 · review · VION-212 · The step-3 table's row 2 defaulted the server to port 80 and rows 34 and 62 cited Modbus precedents; the classification set 8080, refused the Modbus precedent for production capability, and called the virtual-clock default the kits' own rule. The precedent had to share the property the Why rested on. → codified: docs/spec-process.md

2026-09-13 · brief · VION-212 · The brief said HttpPackageSurfaceShould pins the exact dependency set and that a published kit gets no InternalsVisibleTo; the test asserts presence per row only, and the core kit has had the grant all along.

2026-09-13 · gate · mutation runner · A runner filtering with --filter Name~<method> ran nothing for DataRow tests carrying a DisplayName, and the empty summaries read as a hiccup; FullyQualifiedName~ was needed. (self) → codified: docs/testing-conventions.md

2026-09-13 · review · VION-212 · A hang-up test's red under its mutation was the test failing on its own on a half-closed TcpClient, and it was recorded as proven; the suite's five-run check caught it. A red run had to be read against a green run of the same test. (self) → codified: docs/testing-conventions.md

2026-09-13 · review · VION-212 · D6's Why said measuring the per-request timeout on the registered clock changed nothing observable; a deterministic DevHost registers a FakeTimeProvider, so a stepped host's real request times out on virtual time. The hosts registering the clock were not swept. (self)

2026-09-13 · review · VION-212 · The two checks found the hosted server recording a request as answered before its response was written, and a read bound armed at connect cutting short a request waiting for a Sync callback; both criteria had been tested only where the two readings agree. → codified: docs/testing-conventions.md

2026-09-13 · review · VION-212 · The checks found a request log bounded by count while each entry could hold the 1 MiB body cap, a header cap decided by two expressions, and row 33 classified intended but minted nowhere. Should have been caught by the self-check in docs/spec-process.md § Lane 3 step 3.

2026-09-13 · review · VION-212 · D2 bound the hosted server to all interfaces on AC-MODB-011.2's rule; the operator set loopback after the checks, because Modbus lacks transport security by nature and this server serves plaintext by choice through its own parser. → codified: docs/spec-process.md

2026-09-13 · brief · VION-212 · Amendment 1 said the kit's unbounded answer hangs under a thread-owned synchronization context and that Modbus refuses Sync after disposal; every exchange await declines the context, and Modbus closed only the disposed-but-enabled pair.

2026-09-13 · gate · Bash tool · C# edits passed through a Bash heredoc lost one backslash level, landing a literal tab and three real CRLFs in string literals, and Git Bash grep -c $'\r' reported zero for the file. (self)

2026-09-13 · gate · TcpHttpServerTransportShould · Windows loopback took a 128 MiB response in full while the client read none of it, so a stop mid-write could not be held for a test and the test was rebuilt on a request waiting at the gate. (self)

2026-09-14 · review · VION-212 · Both listener comments and a Windows-only probe claimed ExclusiveAddressUse=false maps to SO_REUSEADDR and refuses a second listener; two checks and Amendment 1 passed it, Linux CI failed AC-HTTP-015.5, and a Linux probe showed the option adds SO_REUSEPORT. → codified: docs/spec-process.md

2026-09-14 · review · VION-212 · The adversarial review's first judgment item questioned the ExclusiveAddressUse comment; the coordinator routed it to not carried, and that comment was the Linux defect Amendment 3 fixed.

2026-09-14 · brief · DF-46 · RFC 0018 said FluentModbus's default bind hits EADDRINUSE over a lingering socket and left the option to a redeploy repro nothing shows ran; probes show a plain bind rebinds on Linux and Windows, and its planned regression test was port sharing. (self)

2026-09-14 · gate · Linux mutation run · A mutation calling SetRawSocketOption did not compile under netstandard2.1, and the run's output filter dropped the build error, so the mutated project printed no test line at all. (self) → codified: docs/testing-conventions.md
