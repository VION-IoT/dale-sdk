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

2026-09-14 · review · VION-212 · The change doc said the Modbus provider is kept, with only its name raised as a question; the review asked that keeping or deleting it be surfaced too, since a plain provider binds as the library default. CLAUDE.md's "A decision nobody named is surfaced, not taken" should have prevented it.

2026-09-14 · review · http.md · The page said Linux is where both binding clauses' tests can fail; the Windows probe shows ReuseAddress shares a held port there too, so only the rebind test is Linux-only. spec-process.md's probe-shape rule should have prevented it.

2026-09-14 · review · ActorSystem · The zero-timeout fix put the synchronous expiry in both waits for symmetry; removing it from the acknowledgement wait reddened no test, since acknowledgements are user messages the continuation always precedes, so that half was dropped. testing-conventions.md § 11 should have prevented it. (self)

2026-09-14 · gate · PowerShell tool · A string Replace keyed on "`r`n" inserted nothing into an LF file and reported no error, so a using directive was missing until the build failed. (self)

2026-09-14 · review · ActorSystem · The review found the forcing test missing its // Arrange marker, its comment reading as forcing the fixed code, the zero branch's reason parked on the timeout guard, the held actor never released, and block-lifecycle.md still saying both waits always arm a timeout. comment-conventions.md's claim rule should have prevented the last three. (self)

2026-09-14 · review · scenario-in-flight-reads · The start-publication test released its held handler when start completed, so the handler cached the value before the read and the test passed against the pre-fix host 3 of 3; the release had to follow the read. testing-conventions.md § 11 caught it through the red run it asks for. (self)

2026-09-14 · gate · dotnet test · Three test projects run in one background command were killed for low memory with no output written, so nothing said which had run; one project at a time completed. (self)

2026-09-14 · review · scenario-in-flight-reads · The review found a 250 ms fallback a slow runner could let decide the red run, a start-failure half called untestable that the branch's own hold reaches, a warn-only test that checked no warning, and a count citing a scratchpad script. testing-conventions.md § 16 and review-checks.md P1/P5 should have prevented them. (self)

2026-09-14 · review · process-journal · A 436-character entry was pushed and failed journal-lint on the pull request; the 400 limit is in the journal's header and a length count is one command, and /check prints journal-lint as SKIP, so nothing local ran it.

2026-09-14 · review · scenario-in-flight-reads · The correction review found the barrier-failure test on a 500 ms budget the start acknowledgement had to beat, unable to redden a removed backstop, and a before/after test comment. testing-conventions.md § 16 and comment-conventions.md should have prevented them. (self)

2026-09-14 · brief · http-examples · The brief listed the example's registrations as examples.yml, Vion.Dale.Sdk.sln and set-version.ps1; scripts/pack-examples.ps1 lists every packable example too and was not named. (self)

2026-09-14 · review · http-examples · Scenarios copied from modbus-healthy read an expect right after a waitUntil on a sibling property set by the same block action; on the real clock Outcome read InFlight after StatusCode 503 had arrived, and both runs failed until every such read became a waitUntil. modbus-healthy carries the same shape. (self)

2026-09-14 · review · Vion.Examples.ModbusTcp · ModbusLinkPolicyShould's comment says a failed step's detail is the assertion message, but Assert.Empty cuts it at fifty characters, before the detail. (self) → codified: examples/Vion.Examples.ModbusTcp/Vion.Examples.ModbusTcp.IntegrationTest/ModbusLinkPolicyShould.cs

2026-09-14 · gate · check.ps1 · /check -Build -Test was killed for low memory twice with only its header written while another session built on the machine; the plain run and -Build completed, and the solution test step did not run locally. (self)

2026-09-14 · review · http-examples · The review found HttpDebugClient kept an "already in flight" refusal in LastError after the request succeeded, and HttpSimServer's Requests counter left out dropped requests its own Dropped description calls answered; review-checks.md D2 and testing-conventions.md § 9 should have prevented them. (self)

2026-09-14 · review · http-examples · The review found the SDK's timeout refusal and the invalid-header-name refusal untested and http.md's "the in-repo example" ambiguous beside a second HTTP example; testing-conventions.md § 10 should have prevented the first. (self) → codified: docs/specs/http.md

2026-09-14 · review · process-journal · A codify stamp pushed an entry measured at 347 characters to 458, and journal-lint failed on the pull request again; /check printed journal-lint as SKIP, so nothing local ran it. (second ask) → codified: scripts/check.ps1

2026-09-14 · gate · check.ps1 · check.ps1 -CiShape's path-case scan fails on main at packed-msbuild-lint.ps1:79, whose lowercase file names are compared against ToLowerInvariant() on purpose, so every -CiShape run is red for a script that is right. (self) → codified: scripts/check.ps1

2026-09-14 · review · check.ps1 · The review found the journal-lint pin compared against the local v1, a major tag each release moves, a missing checkout still reporting check: OK, and the default checkout location untested; testing-conventions.md § 9 should have prevented the last. (self) → codified: scripts/check.ps1

2026-09-14 · brief · sdk-followups · The brief assumed a free-running host refuses advance, so nothing could close a serviceProviderExpect race; AC-SCEN-011.4 runs it as a real-time wait, and the real gap was that no step can target a service-provider output. (self)

2026-09-14 · brief · sdk-followups · The brief counted eight expects in modbus-healthy on members no wait names; six read fields of the Link and Connection structs an earlier waitUntil had already waited on, so two needed a wait. (self)

2026-09-14 · gate · dotnet test · check.ps1 -Build -Test and then a solution dotnet test with -m:1 were each killed for low memory mid-run; running the test projects one command at a time completed. (second ask) (self)

2026-09-14 · gate · cleanupcode · A doc comment line lengthened by one word was rewrapped with that word alone on the next line, mid-sentence; the paragraph had to be reflowed by hand before cleanup left it alone. (self)

2026-09-14 · review · sdk-followups · The review found AGENTS.md saying the validator cannot see a sibling read after a drive, which it warns on after a serviceProviderSet; step (4) omitting waitUntil; and the path-case marker's docs silent on a path sharing its line. review-checks.md D2 should have prevented the first. (self)

2026-09-14 · decision · publish.yml · A trial proposed a feature-branch push trigger so Linux CI runs before a PR opens; the operator dismissed it: vion-git:commit pushes per commit, so a run per commit for a failure seen once, and shared publish-nuget.yml:145-151 pushes packages on every non-pull_request event, so it would publish unreviewed branch builds.

2026-09-14 · review · spec-process · The review found the archive rule still owing a map row from any post-archive fix, lane 2 included, after the edit scoped the map to rounds with a classified table; the clause is now scoped too. (self)

2026-09-14 · brief · VION-215 · The brief named the branch feat/VION-215-devhost-next-free-port, which carries a ticket key the branch naming rule forbids; the operator chose feat/devhost-next-free-port.

2026-09-14 · gate · Python on Windows · A Python rewrite of WebHostService.cs read and wrote with the locale code page, so three em dashes it inserted landed as single 0x97 bytes; the diff showed replacement glyphs and the bytes were rewritten as UTF-8. (self)

2026-09-14 · review · VION-215 · The supervised readiness line named the pinned port rather than the port its generation bound, so mutation M5 (no pin) survived the rebind test; the line now names each generation's own bound port and the test requests that port. (self)

2026-09-14 · review · VION-215 · The recycle-failure test awaited the runner unbounded, so the mutations that let a generation walk hung the suite for minutes instead of failing it; the wait is now bounded. (self)

2026-09-14 · review · VION-215 · The readiness test took its unrelated runner port from FreePort(), which hands out consecutive ports, so it was the port the walk landed on and green code failed; the port is now taken below the preferred one. (self)

2026-09-14 · review · devhost-smoke · The rewritten skill boots started the host with -NoNewWindow, so the tool shell running a boot waited on the host's inherited console handles until the host died; the boots and smoke-modbus.ps1 now start it with -WindowStyle Hidden. (self) → codified: .claude/skills/devhost-smoke/SKILL.md

2026-09-14 · review · VION-215 · The review found every port the walk passed leaving an event broadcaster subscribed from a disposed attempt, and a non-bind start failure leaking its attempt; the broadcaster now subscribes only for the kept application and any failed attempt is disposed. (self)

2026-09-14 · review · VION-215 · The review found the walk tests accepting any port up to nineteen above, so a walk skipping a free port passed, and rows selecting the entry point and the holder kind; testing-conventions.md §11 and §13 state both rules. (self)

2026-09-14 · review · VION-215 · The review found the reworded AC-CTRL-006.2 adding a browser-address clause no test reaches, without a GAP, and the no-web-UI port fallback promised in prose with no criterion; spec-process.md states both. (self)

2026-09-14 · review · devhost-smoke · The review found the skill boots sharing one fixed output file, waiting without a deadline, and keeping pid and port only in shell state a later call loses, where smoke-modbus.ps1 already had per-run files and a bound; review-checks.md P3 names the shape. (self)

2026-09-14 · brief · hw-contracts-are-json · The brief required commit 1 to be the Vion.Contracts bump alone, green before any handler edit; 11.0.0 removes Vion.Contracts.FlatBuffers.Hw in the same major that adds Vion.Contracts.Hw, so the bump alone cannot compile and the two are one atomic change.

2026-09-14 · brief · hw-contracts-are-json · The brief listed six comment sites as describing the service-provider wire as FlatBuffers; two describe service property state, which decision 0038 made JSON, and two truly describe GetFlatBufferPayload and are correct.

2026-09-14 · decision · hw-contracts-are-json · JSON has no literal for a non-finite double, so AC-IO-007.2 could not be met on the new wire; rather than reword the criterion, the nine rows proving it were left red and the fix taken upstream, landing as Vion.Contracts 11.0.1. (self)

2026-09-14 · brief · hw-contracts-are-json · The amendment asked for a regenerated publicapi manifest showing three added members, gated by check.ps1; the manifest is type-level with only assemblies and types keys, this change declares no new [PublicApi] type, and check.ps1 has no manifest step. (self)

2026-09-14 · gate · publicapi-manifest · scripts/generate-api-reference.cjs needs node, absent on this machine, so the manifest cannot be regenerated locally at all; CI's snapshot bot on the pull request head is the only path. (self)

2026-09-15 · review · hw-contracts-are-json · The review found the publish records and helper defaulting the content type to JSON, which relabels dale's FlatBuffer Remote/Func publish that omits it and went against the spec's leave-the-default-alone; asked instead for no default at all. CLAUDE.md's decision-nobody-named STOP should have surfaced it.

2026-09-15 · review · hw-contracts-are-json · The review found the named-literal NumberHandling on JsonSerialization.DefaultOptions reaching the DevHost scenario codec and dale's Func payloads, well past hw/*; the operator took it out rather than record it, together with its upstream twin. spec-process.md's previously-inert-inputs sweep should have caught the reach.

2026-09-15 · decision · hw-contracts-are-json · A non-finite analog value does not cross the hw/* JSON wire: AC-IO-007.2 narrows to finite values and the analog output handler drops a non-finite command with a warning, reversing the 2026-09-14 named-literal outcome; operator, with the author, to keep every analog value on the wire a JSON number.

2026-09-15 · review · hw-contracts-are-json · The review found both harnesses' Number comment, the change doc and the PR body saying double.ToString renders the Unicode infinity sign; the harness formatted with the invariant culture, which renders ASCII Infinity. comment-conventions.md's verify-the-mechanism rule should have caught it.

2026-09-15 · review · hw-contracts-are-json · The review found ForwardNothingWhenPayloadWiderThanTopicCarries surviving deletion of the label check it cites, since the bool decode refuses the same document, and the identity tests reaching no field beside the value; the first is deleted and the second arranges a document naming another endpoint. testing-conventions.md § 11 should have caught both.

2026-09-15 · review · hw-contracts-are-json · The review found the AnalogIo harness keeping three helpers nothing called, string literals where the DigitalIo mirror uses nameof, and an unused FlatBuffers using in the Modbus RTU tests; asked for the helpers deleted and the mirror matched. io.md's mirror rule should have caught the second.

2026-09-15 · review · hw-contracts-are-json · The review found two DevHost comments still contrasting a scenario value with a FlatBuffer frame from the production handler; the drift sweep had grepped Vion.Dale.Sdk/ only. comment-conventions.md's falsified-comment rule should have widened it to the tree.

2026-09-15 · review · hw-contracts-are-json · The review found no test reading a published Modbus RTU request — its document, schema label or content type — and row 10 citing the inbound response arrangements as evidence for the outbound function code; asked for tests on the request wire. docs/review-checks.md P2 should have caught it.

2026-09-15 · review · hw-contracts-are-json · The review found the no-reachable-mutation reason for leaving the JsonTypeInfo overloads unspecified true only of hw/* records, whose naming matches the shared options; a snake-case test context makes the fallback observable on publish and read, so AC-BIND-011.5 and 012.8 are minted. docs/review-checks.md P4 should have caught it.

2026-09-15 · review · hw-contracts-are-json · The review found the typed PublishJson inheriting Publish's docs, which describe payload as serialized bytes and leave typeInfo undocumented, copied from the reflection overload beside it; both overloads now carry their own parameter docs. sdk-surface-conventions.md § 2's verify-against-the-code rule should have caught it.

2026-09-15 · review · hw-contracts-are-json · The review found io.md announcing exactly five things before four, narrating the cut with now, no longer and used-to, and arguing a catch that changes nothing observable, and contracts.md arguing why its default was not another; asked for current truth with no counts and no history. spec-process.md § The corpus should have caught it.

2026-09-15 · decision · hw-contracts-are-json · Vion.Contracts 11.0.2 takes the named literals back out of HwJsonContext and this repo pins it, so a quoted NaN or infinity on an analog state topic is refused as undecodable; the three HALs and the service-provider SDK released on 11.0.1 are left to the cross-repo spec to move.

2026-09-15 · review · hw-contracts-are-json · The review found the rewritten decode, encoding and content-type tests and the default test carrying no named mutation, and the obvious decode mutation reddening by an escaping exception rather than the assertion; asked for one test-to-mutation line each, read for the failing assertion. testing-conventions.md § 11 should have caught it.

2026-09-15 · gate · vion-dispatch:spawn · The spawn skill's steer triggers found no STOP and no commit pause, so the vion-contracts worker launched unsteered on the stored token, while spec-process.md says a dispatched worker is always steered; nothing in the launch read that rule. (self)

2026-09-15 · review · hw-contracts-are-json · The review found the change doc still in flight under docs/changes/ on the pull request that lands it; asked for it archived. CLAUDE.md's feature-sized lane, archived in the pull request that lands it, should have caught it.

2026-09-15 · gate · mutation runner · A mutation was restored with git checkout, which also discarded the session's uncommitted edit to the same file; the edit had to be re-applied before the round could go on. (self) → codified: docs/testing-conventions.md

2026-09-15 · review · hw-contracts-are-json · The correction-round review found the analog TestKit's tolerance doc and two ToleranceShould comments still saying the wire carries non-finite values unaltered and drops a signed zero, beside the testkit.md paragraph this round edited; the sweep grepped NaN and Infinity, not the prose words. docs/review-checks.md P3 should have caught it. (self)

2026-09-15 · review · hw-contracts-are-json · The correction-round review found AC-BIND-012.8's test building a single-segment payload, so the reader path for a segmented one could ignore the caller's metadata unnoticed; a segmented row was added. testing-conventions.md § 9's statement-by-statement walk should have caught it. (self)

2026-09-15 · review · hw-contracts-are-json · The correction-round review found the change doc's reason for deleting JsonSerializationShould wrong, and neither test deletion's reason in a commit, which the commit skill gives no body for; the reasons go in the pull request body. testing-conventions.md § 9 should have caught it. (self)

2026-09-15 · review · hw-contracts-are-json · The correction-round review found AC-IO-007.3 held by both the handler guard and the serializer's refusal, the guard-removal mutation reddening only by an escaping exception; the mutation list now names both guards. spec-process.md § Implement's over-determined rule should have caught it. (self)

2026-09-15 · decision · spec-process · The in-repo finding ledger is removed: a finding not fixed, stated on a page or marked GAP becomes a draft Jira item under VION-62, and lane 3's `park` classification is `file`. Nothing emptied the ledger between triages, and its one bulk triage deleted most of it. → codified: docs/spec-process.md

2026-09-15 · review · spec-process · The review found the draft item for an unfixed finding given no home outside lane 3, the review prompt asked to check drafts it is never handed, the item shape restated beside the skill that owns it, and `file` unticked in two prompts; the harness skill's walk-through rule should have caught the first two. (self)

2026-09-15 · decision · spec-process · A draft item for an engineering finding takes the dale-sdk-feedback skill's item shape whole, `file:line` chain included, rather than a spec-ids exception stated beside it; the shape has one owner. → codified: docs/spec-process.md

2026-09-15 · review · dale-sdk-feedback · The review found the item shape's Origin line naming only a consumer report once engineering findings take that shape whole, and a one-word reflow stub in spec-process; walking a draft through the template should have caught the first. (self)

2026-09-15 · review · verify-packages · The CI-speedup proposal said verify-packages sees nothing on pull requests, read off its version-check comment; the script also fails a lib-packed analyzer and missing required content on every run, so only its self-test moved. (self)

2026-09-15 · review · publish.yml · The changes job took a pull request's diff base from pull_request.base.sha, the base at the last pull request event; the merge commit's first parent was needed, since main can move without one. (self) → codified: .github/workflows/publish.yml

2026-09-15 · review · CLAUDE.md · The CI style scope was written into CLAUDE.md twice and again into the cleanup command; the harness pass cut it to the one sentence in CLAUDE.md. (self)

2026-09-15 · review · publish.yml · The review found drift-and-docs pushing its snapshot commit while style and verify-packages still ran; that push starts a run which cancels the first and passes ci with every job skipped, so a style failure never reached the head. The ci step was tested on one run's results only. (self) → codified: .github/workflows/publish.yml

2026-09-15 · review · publish.yml · The review found the changes job diffing with rename detection on, so moving a compiled file into docs/ or scripts/ listed only its new path and skipped the build. (self) → codified: .github/workflows/publish.yml

2026-09-15 · review · publish.yml · The review found the changes job skipping the build for a set-version.ps1 edit a solution test reads, and style on main for a project-file change; the scope was tested against past commits holding neither. (self) → codified: .github/workflows/publish.yml

2026-09-15 · review · publish.yml · The review found the new ci job left on the repository's default token scope where every other job in the workflow declares its own. (self) → codified: .github/workflows/publish.yml

2026-09-15 · review · CLAUDE.md · The review found spec-gates.yml, io.md's line citations and cleanup-code.ps1's CI example still describing the old publish.yml, and CLAUDE.md and a publish.yml comment claiming more than the code does; docs/comment-conventions.md's rule on comments a change falsifies should have caught it. (self)

2026-09-15 · review · publish.yml · The re-review found the snapshot fix gating drift-and-docs on always(), which keeps the job alive in a run a newer push cancelled, so it could still push a snapshot commit over that push and hide its gates; !cancelled() was needed. (self) → codified: .github/workflows/publish.yml

2026-09-15 · review · publish.yml · The re-review found the changes job still skipping the build for the API manifest, topology and .dale schema files and examples/ projects that solution tests read, after set-version.ps1 alone was added. (self) → codified: .github/workflows/publish.yml

2026-09-15 · review · publish.yml · The re-review found an examples project-file change skipping style on the pull request and on main, while the scope comment said main's full run catches a project setting cleanup reads. (self) → codified: .github/workflows/publish.yml

2026-09-15 · review · publish.yml · The re-review found a paths-ignore cause left in a publish.yml comment, cleanup-code.ps1's -Changed doc still saying main always runs full, and io.md crediting one step with three steps' work, after a commit meant to correct the stale claims. (self)

2026-09-15 · review · publish.yml · The third review found the build-input exceptions matching topology and .dale schema files only under docs/, while the tests that read them search the whole repository. (self) → codified: .github/workflows/publish.yml

2026-09-15 · review · CLAUDE.md · The third review found cleanup-code.ps1 pointing at a CLAUDE.md sentence that omitted a style-input change running full, and a publish.yml comment naming a snapshot drift cause no non-build change can produce. (self)

2026-09-15 · review · publish.yml · The fourth review found a .csproj under docs/ or .claude/ still skipping the build after topology and .dale files were added; each round had added one more exception to a list of skipped directories, so the rule became a list of inert file kinds and everything else builds. (self) → codified: .github/workflows/publish.yml

2026-09-15 · review · publish.yml · The fourth review found the snapshot comment and main's drift warning promising a heal on the next PR, which a PR that does not build never gives, and CLAUDE.md keeping its own list of style cases that missed a manual run. (self)

2026-09-15 · review · publish.yml · The fifth review found the inert-change comment naming more .github/ files than its regex skips and promising no test input needs a rule, and CLAUDE.md still saying every push publishes; a sweep found the same claim in releasing.md, the CLI's CLAUDE.md and the modbus-smoke skill. (self)

2026-09-15 · review · publish.yml · The sixth review found the read_by_build rule naming only Markdown and .ps1 while inert also skips settings.json, CODEOWNERS and release.yml, the .github/ list readable as root files, and three docs saying "a push that builds" with no pointer to what decides it. (self)

2026-09-15 · brief · VION-224 · The brief listed a shared endpoint, two blocks mapped to one renamed triple, as a guard green before and after the fix; on origin/main that drive is refused like any renamed triple, so the case is a second red proof, not a guard. (self)

2026-09-15 · review · VION-224 · The review found the HTTP renamed-endpoint test ignoring the advance response, so a refused advance would fail as the drive not reaching the block; WebControlEndpointsShould, the precedent it copied, asserts it. (self)

2026-09-15 · review · AnalyzerWiringShould · The batched rewrite judged every keep-out test on the whole ordinary build's output, so a probe leaking into Modbus.Rtu alone failed all ten; a mutation run showed it, and each project is now judged on the lines MSBuild attributes to it. (self)

2026-09-15 · gate · AnalyzerWiringShould · /check -Test failed the output guard after its fingerprints moved to bracket every set in ClassInitialize: dotnet test without --no-build was still building the same Debug outputs; the bracket is back around the guard's own builds. (self) → codified: Vion.Dale.Sdk.Generators.Test/AnalyzerWiringShould.cs

2026-09-15 · review · AnalyzerWiringShould · The review found the I/O row and the guard's diff never re-proven red after the rewrite, a shared exit code judged per row, doc claims about Built and the probe half that the code does not make, and two convention docs still naming dotnet build; mutations were run and the rest fixed. (self)

2026-09-15 · review · AnalyzerWiringShould · The review found the narrowed fingerprint bracket still racing a concurrent solution build, shrunk not removed; the guard now fails only on files carrying its own version stamp, which no other build writes. It also found the probe half unchecked. (self) → codified: Vion.Dale.Sdk.Generators.Test/AnalyzerWiringShould.cs
