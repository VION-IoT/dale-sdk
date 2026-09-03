---
trace: enforced
---

# Scenario & topology files, stepping, contract pairing

What a committed `*.scenario.json` and `*.topology.json` may say, what running one guarantees under
the stepped and the real clock, and what a topology's `contractPairings` declaration means. Area code
`SCEN`. Process: [`../spec-process.md`](../spec-process.md).

The three subjects are one contract in practice: a scenario names a topology, a step's meaning depends
on the clock the topology's host was built with, and a pairing declared in the topology is what makes
a scenario's closed loop run.

Cited rather than restated: [`config-gating.md`](config-gating.md) for `[InstantiationParameter]`
decoding and config-time inclusion gates, [`introspection.md`](introspection.md) for the member and
contract vocabulary a name path resolves against, [`emission.md`](emission.md) for the
property-versus-measuring-point split a name path can land on, and `DALE046` (the analyzer registry's)
for the compile-time check on a `[ScenarioWire]` type. The DevHost control API, the SPA's internals,
the TestKit's virtual time and block lifecycle are other pages'.

## The three refusal layers

Which layer refuses is itself part of the contract, because it decides what an author has to have in
hand to get the verdict:

1. **Structural** — `ScenarioFile.Parse` / `DevTopologyFile.Parse`. Host-independent: no
   configuration, no block catalog, every problem reported at once.
2. **Resolution** — run time, against the wired host's configuration, collected for the whole file
   before any step executes.
3. **Execution** — a step's own failure detail.

A topology file splits its own second layer again: the catalog, interface compatibility and pairing
structure are host-independent (so the CLI and a unit test reach them), while the pairing wire-type
rule needs introspected blocks and runs at host load and at editor save.

## File identity and strictness

Both file formats are strict in the same way, and their identity rules are the same rules.

- `AC-SCEN-001.1` (Event-driven): WHEN a scenario file declares a `version` other than 1 THE SYSTEM
  SHALL reject it, naming the version it was given.
- `AC-SCEN-001.2` (Event-driven): WHEN a scenario or topology file carries a JSON property that maps
  to no declared field, or repeats a key, THE SYSTEM SHALL reject the file.
- `AC-SCEN-001.3` (Ubiquitous): THE SYSTEM SHALL require a scenario or topology `id` to be a URL-safe
  slug starting with an alphanumeric, and SHALL reject one containing `..` or equal to the reserved
  name `schema` compared case-insensitively.
- `AC-SCEN-001.4` (Ubiquitous): THE SYSTEM SHALL reject an absent, empty or whitespace-only value for
  every required string a scenario or topology file carries.
- `AC-SCEN-001.5` (Ubiquitous): THE SYSTEM SHALL report every structural problem in a file at once
  rather than the first.
- `AC-SCEN-001.6` (Event-driven): WHEN a scenario or topology is loaded from a path THE SYSTEM SHALL
  require its declared `id` to equal the file name without the suffix, compared ordinally.
- `AC-SCEN-001.7` (Ubiquitous): THE SYSTEM SHALL refuse a scenario or topology id that resolves to a
  path outside its own directory.
- `AC-SCEN-001.8` (Ubiquitous): THE SYSTEM SHALL treat an explicit `"value": null` as a value to write
  and an absent `value` as a format error.
- `AC-SCEN-001.9` (Event-driven): WHEN a scenario assembled in memory rather than parsed is run THE
  SYSTEM SHALL re-validate it structurally before executing any step.

`AC-SCEN-001.3` is one rule because the two ids are read by the same kinds of caller: both are file
names, both are route segments, and both reach a loader that joins them to a directory.
`AC-SCEN-001.7` is the confinement that makes that safe whatever the id says.

## The step vocabulary

Seven closed shapes, and the closedness is the point of a versioned file. The vocabulary has **four
definition sites** that must agree — the C# model and runner, the CLI's `dale scenario validate`, the
JSON schema, and the SPA's step forms — and missing one produces an asymmetric quiet failure: a schema
that autocompletes a step the runner rejects, or a validator that green-lights a file the runner
refuses.

- `AC-SCEN-002.1` (Ubiquitous): THE SYSTEM SHALL accept exactly the step kinds `set`,
  `serviceProviderSet`, `serviceProviderExpect`, `waitUntil`, `expect`, `advance` and `settle`, and
  SHALL declare that set identically at every site that defines it.
- `AC-SCEN-002.2` (Event-driven): WHEN a step is not exactly one of the closed shapes THE SYSTEM SHALL
  reject it.
- `AC-SCEN-002.3` (Ubiquitous): THE SYSTEM SHALL restrict `setup` entries to `set` and
  `serviceProviderSet`, and SHALL declare that subset identically at every site that defines it.
- `AC-SCEN-002.4` (Ubiquitous): THE SYSTEM SHALL accept an optional `label` and `spec` on every step
  kind.
- `AC-SCEN-002.5` (Ubiquitous): THE SYSTEM SHALL require a `value` on a `set` and on a
  `serviceProviderSet`.
- `AC-SCEN-002.6` (Event-driven): WHEN a step carries a field its kind does not take THE SYSTEM SHALL
  reject the step naming the field and the kind, at the runner, at the schema and at
  `dale scenario validate` alike.

`setup` stages order-independent, idempotent state; waits, assertions and time steps belong to the
timeline, which is why the subset exists at all.

## Durations and budgets

Three budgets — `advance.seconds`, `settle.maxSeconds`, `timeoutSeconds` — and one set of rules over
all three, because every one of them becomes a `TimeSpan` before it is spent.

- `AC-SCEN-003.1` (Ubiquitous): THE SYSTEM SHALL reject a non-positive duration.
- `AC-SCEN-003.2` (Ubiquitous): THE SYSTEM SHALL reject a duration that is not finite or is longer
  than a real clock can wait, and SHALL name the identical bound at the runner, at the schema and at
  `dale scenario validate`.
- `AC-SCEN-003.3` (Ubiquitous): THE SYSTEM SHALL require a duration to be a JSON number.
- `AC-SCEN-003.4` (Ubiquitous): THE SYSTEM SHALL default an omitted `waitUntil` timeout to 20 seconds
  and an omitted `settle` budget to 60 seconds.
- `AC-SCEN-003.5` (Ubiquitous): THE SYSTEM SHALL reject a present-but-empty `settle.until` and SHALL
  treat an omitted one as the scenario's whole `watch` list.
- `AC-SCEN-003.6` (Ubiquitous): THE SYSTEM SHALL accept a `settle` that declares no members.

There is no unit-suffix spelling: a duration is a number of seconds. `AC-SCEN-003.2` is why — the
bound has to be one number three sites can name, and a refusal at parse is worth more than a
framework argument exception thrown from inside a step. The bound is the real clock's, because that is
the smaller of the two: a stepped host would jump any span, but the same file has to run on both.

## Comparators

`waitUntil`, `expect` and `serviceProviderExpect` share one comparator vocabulary. Structs and arrays
are not comparable: a scenario that needs to compare one is a C# test.

- `AC-SCEN-004.1` (Ubiquitous): THE SYSTEM SHALL require exactly one of `above`, `below`, `equals`,
  `notEquals` or `oneOf` on every comparator block.
- `AC-SCEN-004.2` (Ubiquitous): THE SYSTEM SHALL require an `above` or `below` comparand to be
  numeric, and SHALL treat the comparator as unsatisfied when the compared value, or a resolved
  relational comparand, is not a number.
- `AC-SCEN-004.3` (Ubiquitous): THE SYSTEM SHALL reject a struct or array comparand for `equals` and
  `notEquals`, and SHALL accept `null`.
- `AC-SCEN-004.4` (Ubiquitous): THE SYSTEM SHALL require `oneOf` to be a non-empty array of scalars,
  and SHALL satisfy it when the compared value equals any element under the same exact semantics.
- `AC-SCEN-004.5` (Ubiquitous): THE SYSTEM SHALL accept a `tolerance` only alongside a numeric
  `equals`, SHALL reject a negative one, SHALL treat zero as exact equality, and SHALL apply the same
  rule at the schema.
- `AC-SCEN-004.6` (Ubiquitous): THE SYSTEM SHALL accept a `{ "path": … }` comparand only on an
  `expect`, and only as the whole comparand object.
- `AC-SCEN-004.7` (Ubiquitous): THE SYSTEM SHALL compare numbers across every numeric type, enums by
  case-sensitive member name, and a `TimeSpan` by its string form.

A relational comparand is a point-in-time read, which is why it belongs to `expect` and not to a wait:
a wait would re-read a moving target and could never say what it had compared.

## Name paths

`Block.Property` when the property is unambiguous within the block, `Block.Service.Property` always,
either optionally followed by a struct field path. Ambiguity is refused, never resolved last-wins.

- `AC-SCEN-005.1` (Ubiquitous): THE SYSTEM SHALL require a name path to carry at least two
  dot-separated segments, none of them empty or whitespace, and SHALL impose no upper bound on the
  number of segments at any site that declares the shape, the canonical schema included.
- `AC-SCEN-005.2` (Event-driven): WHEN a name path names a block or member the topology does not carry
  THE SYSTEM SHALL refuse it, naming what was not found and appending the exact spelling when one
  differs only in case.
- `AC-SCEN-005.3` (Ubiquitous): THE SYSTEM SHALL decide whether a path's second segment is a service
  or a member from the configuration, never from the number of segments.
- `AC-SCEN-005.4` (Event-driven): WHEN a name path is ambiguous THE SYSTEM SHALL refuse it and state
  every reading it could have.
- `AC-SCEN-005.5` (Event-driven): WHEN a member name is carried by exactly one service THE SYSTEM
  SHALL accept the two-segment form for it.
- `AC-SCEN-005.6` (Ubiquitous): THE SYSTEM SHALL resolve a service-qualified path to that service's
  member even when another service of the block carries the same member name.
- `AC-SCEN-005.7` (Ubiquitous): THE SYSTEM SHALL resolve both service properties and service
  measuring points, and SHALL record which of the two a path resolved to.
- `AC-SCEN-005.8` (Event-driven): WHEN a `set` targets a measuring point or a read-only property THE
  SYSTEM SHALL refuse it.

## Struct field paths

A path may descend a struct-typed member to one scalar field leaf. The authoring UI emits the schema's
camelCase wire keys and an author writes PascalCase, so both resolve.

- `AC-SCEN-006.1` (Ubiquitous): THE SYSTEM SHALL resolve a struct field path segment given in either
  PascalCase or the schema's camelCase key.
- `AC-SCEN-006.2` (Event-driven): WHEN a struct field path descends into a member that is not a
  struct, names a field the struct does not declare, or ends on a nested struct or an array, THE
  SYSTEM SHALL refuse it and say which of the three it is.
- `AC-SCEN-006.3` (Event-driven): WHEN a comparator targets a struct- or array-typed member with no
  field path THE SYSTEM SHALL refuse the step.
- `AC-SCEN-006.4` (Ubiquitous): THE SYSTEM SHALL judge a comparator against the resolved leaf's type,
  requiring a numeric leaf for `above` and `below` and refusing a `oneOf` element that does not fit
  the leaf.
- `AC-SCEN-006.5` (Ubiquitous): THE SYSTEM SHALL read a nullable-widened schema type as its non-null
  member when judging a leaf.
- `AC-SCEN-006.6` (Event-driven): WHEN a struct field path reads through a null intermediate, or
  reaches two members differing only in case, THE SYSTEM SHALL yield a null leaf rather than fail the
  run.

`AC-SCEN-006.3` exists because a `notEquals` against a whole struct used to report satisfied having
compared nothing.

## Service-provider contracts

`serviceProviderSet` drives the wire value a service provider delivers; `serviceProviderExpect`
asserts the value a block last wrote. Both address a **contract binding on a block**, never an
endpoint triple, because every contract already has an auto-created endpoint.

Direction is read per operation from what the contract's handler declares, never as one binary
classification.

- `AC-SCEN-007.1` (Ubiquitous): THE SYSTEM SHALL address a service-provider contract by the consuming
  block's name and the contract identifier, and SHALL refuse either operation on a contract with no
  endpoint mapping in the topology.
- `AC-SCEN-007.2` (Event-driven): WHEN a `serviceProviderSet` names a contract whose handler declares
  no inbound wire struct THE SYSTEM SHALL refuse the step and name the assertion step instead.
- `AC-SCEN-007.3` (Ubiquitous): THE SYSTEM SHALL decide drivability from the declared inbound wire
  struct rather than from the contract's consumer multiplicity.
- `AC-SCEN-007.4` (Event-driven): WHEN a `serviceProviderExpect` names a contract that is neither a
  single-writer output nor carries a declared outbound wire struct THE SYSTEM SHALL refuse the step.
- `AC-SCEN-007.5` (Ubiquitous): THE SYSTEM SHALL let one contract identifier be both driven and
  asserted in one scenario when its handler declares both wire directions.
- `AC-SCEN-007.6` (Ubiquitous): THE SYSTEM SHALL require a `field` selector for a contract whose
  outbound command carries more than one addressable leaf, SHALL refuse one on a contract that writes
  a single value, and SHALL match a given `field` against the available leaves case-insensitively.
- `AC-SCEN-007.7` (Ubiquitous): THE SYSTEM SHALL require a `field` to be a dotted path of non-empty
  segments.
- `AC-SCEN-007.8` (Event-driven): WHEN the host could not describe a contract's outbound leaves THE
  SYSTEM SHALL leave the `field` check to the read at run time rather than refuse the step.
- `AC-SCEN-007.9` (Ubiquitous): THE SYSTEM SHALL demand no `field` of a `serviceProviderSet`, whatever
  its contract's outbound declares.
- `AC-SCEN-007.10` (Event-driven): WHEN a `serviceProviderSet` drives an inbound that a materialised
  contract pairing also feeds THE SYSTEM SHALL record a warning on the run and SHALL run the step.
- `AC-SCEN-007.11` (Ubiquitous): THE SYSTEM SHALL read a handler's declared wire structs including
  those declared on a base handler.

`AC-SCEN-007.6` is why a whole multi-field command is not assertable: it has no scalar leaf, and
letting it through is how a `notEquals` passed having compared nothing. `AC-SCEN-007.10` is a warning
and not an error because seeding a closed loop from a scenario is a legitimate bench move — refusing
it would make a paired topology untestable from its own scenarios.

## The wire codec

A handler declares the wire struct its contract carries, and the DevHost builds the exact message a
consumer's handler already matches — the same payload the production handler forwards, sourced from a
scenario's JSON instead of a hardware frame.

- `AC-SCEN-008.1` (Event-driven): WHEN a service-provider handler declares no wire struct in either
  direction THE SYSTEM SHALL expose no scenario codec for it.
- `AC-SCEN-008.2` (Ubiquitous): THE SYSTEM SHALL build the exact closed contract message type the
  consuming block's handler matches.
- `AC-SCEN-008.3` (Ubiquitous): THE SYSTEM SHALL round-trip a single-parameter wire struct as its bare
  scalar in both directions, and SHALL report it as having no addressable field leaf.
- `AC-SCEN-008.4` (Ubiquitous): THE SYSTEM SHALL read a multi-parameter wire struct from a JSON object
  and its enum members by name.
- `AC-SCEN-008.5` (Ubiquitous): THE SYSTEM SHALL report a wire struct's addressable leaves in
  declaration order, dotting through a nested wire struct, excluding a collection-typed field, and
  treating every struct with its own converter as a scalar leaf.
- `AC-SCEN-008.6` (Event-driven): WHEN a declared wire struct exposes no constructor THE SYSTEM SHALL
  describe it without failing, because leaves are enumerated for every discovered handler when a
  configuration is built.

A `[ScenarioWire]` type that cannot be represented this way is a compile-time error — `DALE046`, the
analyzer registry's — so a consumer learns it from their own build rather than from a scenario run.

## Running a scenario

- `AC-SCEN-009.1` (Event-driven): WHEN the host is not on the topology a scenario declares THE SYSTEM
  SHALL refuse the run before executing anything, report the mismatch, and offer no override.
- `AC-SCEN-009.2` (Ubiquitous): THE SYSTEM SHALL resolve every setup entry, step, watch path and
  settle target before executing any step, and SHALL fail the run with all the resolution errors when
  any of them does not resolve.
- `AC-SCEN-009.3` (Ubiquitous): THE SYSTEM SHALL run setup entries in file order and then steps in
  file order, stopping at the first failure, marking the step that failed and every later step as
  skipped with the reason.
- `AC-SCEN-009.4` (Ubiquitous): THE SYSTEM SHALL record a run's failure on its report rather than
  raise it, and SHALL raise it instead — carrying the report — when a scenario is applied as the
  arrange phase of a test.
- `AC-SCEN-009.5` (Event-driven): WHEN a run is cancelled THE SYSTEM SHALL report it as cancelled and
  skip the remaining steps.
- `AC-SCEN-009.6` (Ubiquitous): THE SYSTEM SHALL publish the report at every step transition and SHALL
  produce a consistent deep copy of it on demand.
- `AC-SCEN-009.7` (Ubiquitous): THE SYSTEM SHALL describe every step by kind, target and argument
  before the run begins.
- `AC-SCEN-009.8` (Ubiquitous): THE SYSTEM SHALL report every judgment item as requiring a human and
  SHALL never fail a run for one.
- `AC-SCEN-009.9` (Ubiquitous): THE SYSTEM SHALL record a wall-clock duration for every step and a
  virtual duration only on a stepped host, so that two runs of one scenario on one host agree on every
  deterministic field.
- `AC-SCEN-009.10` (Event-driven): WHEN a `set` step's acknowledgement consumes its safety window THE SYSTEM SHALL record why in the step's detail, and SHALL fail the step only when a block exception was logged for that write. GAP: the window is a fixed five real seconds with no injection seam, so no test reaches it without a five-second wait per case; the seam is in `_findings.md`.
- `AC-SCEN-009.11` (Ubiquitous): THE SYSTEM SHALL report a `serviceProviderSet` as fire-and-forget.
- `AC-SCEN-009.12` (Event-driven): WHEN a `serviceProviderExpect` reads a contract the block never
  wrote, or a captured command with no scalar leaf at the addressed field, THE SYSTEM SHALL fail the
  step saying which of the two it is and showing what was captured.
- `AC-SCEN-009.13` (Event-driven): WHEN a comparator does not hold THE SYSTEM SHALL name the target,
  the expected bound and the actual value in the step's detail.
- `AC-SCEN-009.14` (Event-driven): WHEN an `expect` carries a relational comparand THE SYSTEM SHALL
  read the comparand's value at assert time and name its member in a failure.
- `AC-SCEN-009.15` (Ubiquitous): THE SYSTEM SHALL carry the scenario file's content hash on the run
  report when the file can be read, and no hash when it cannot.

There is no `force`: running against the wrong graph silently produced misleading green runs.
`AC-SCEN-009.8` has a consequence worth stating plainly — a consumer's test suite is green with
unmet judgments, by design, because a judgment is a human's verdict and not CI's.

## The watch trace

- `AC-SCEN-010.1` (Ubiquitous): THE SYSTEM SHALL sample every watched name path once after setup and
  once after each step, whether the step passed or failed.
- `AC-SCEN-010.2` (Ubiquitous): THE SYSTEM SHALL leave the watch trace empty when a scenario watches
  nothing.
- `AC-SCEN-010.3` (State-driven): WHILE the host is stepped THE SYSTEM SHALL produce a watch trace
  whose values and virtual timestamps are reproducible run to run.

The trace is observability, not an assertion target: it is what report-diffing and post-hoc
diagnosis read.

## Clock modes

One virtual clock, two drivers, and a scenario carries no stepped flag — the mode belongs to the host.
No step kind is refused for the active mode; each of the seven is either host-adaptive and says which
mode it ran in, or is mode-independent.

- `AC-SCEN-011.1` (Ubiquitous): THE SYSTEM SHALL treat a host as stepped exactly when its registered
  time provider exposes a public method to advance it, and SHALL refuse to step a real clock naming
  the resolved provider and the remedy.
- `AC-SCEN-011.2` (Event-driven): WHEN deterministic stepping is requested without an explicit start
  instant THE SYSTEM SHALL start the virtual clock at a fixed epoch.
- `AC-SCEN-011.3` (Ubiquitous): THE SYSTEM SHALL refuse no step kind for the host's clock mode.
- `AC-SCEN-011.4` (Ubiquitous): THE SYSTEM SHALL run an `advance` as an exact virtual jump on a
  stepped host and, otherwise, as a wait of the same span of real time — a span `AC-SCEN-003.2` has
  already bounded to what a real clock can wait — and SHALL say in the step's detail when it waited on
  the real clock.
- `AC-SCEN-011.5` (Event-driven): WHEN a `waitUntil` condition already holds THE SYSTEM SHALL satisfy
  the step immediately, in either clock mode.
- `AC-SCEN-011.6` (State-driven): WHILE the host is stepped THE SYSTEM SHALL advance a `waitUntil` one
  scheduled event at a time until the condition holds, the virtual budget is spent, or nothing further
  is scheduled, and SHALL report which.
- `AC-SCEN-011.7` (State-driven): WHILE the host is on the real clock THE SYSTEM SHALL subscribe for a
  `waitUntil` and re-evaluate the condition once more afterwards, and SHALL release the waiter as soon
  as the condition is met.
- `AC-SCEN-011.8` (Ubiquitous): THE SYSTEM SHALL converge a `settle` by advancing one scheduled event
  at a time on a stepped host and by requiring the targets steady across several consecutive polls on
  the real clock, and SHALL fail the step naming the still-changing target and its last transition.
- `AC-SCEN-011.9` (Ubiquitous): THE SYSTEM SHALL scope a `settle` to its declared targets when it
  names any and to the scenario's whole watch list otherwise, and SHALL converge immediately when the
  targeted set is empty.
- `AC-SCEN-011.10` (Ubiquitous): THE SYSTEM SHALL spend a `settle` budget in virtual seconds on a
  stepped host and in real seconds otherwise.
- `AC-SCEN-011.11` (Ubiquitous): THE SYSTEM SHALL converge a `settle` on its first hop when no
  targeted value changes across that hop, and SHALL report the hop count and the virtual time the hop
  spent in the step's detail.

`AC-SCEN-011.11` is a warning as much as a guarantee: a `settle` written to prove a cascade converges
on hop one when the stimulus scheduled nothing, having proved nothing at all. The hop count and the
virtual span in the detail are the only tell, which is why they are specified rather than incidental.

`AC-SCEN-011.3` states an absence, and it is load-bearing: the alternative history is a step kind that
was silently mis-timed in one mode. The kind that could not be made honest in both was removed rather
than documented. Adding a kind decides its behaviour in both modes
([`../devhost-conventions.md`](../devhost-conventions.md) § 3).

## Stepping guarantees

Determinism comes from a two-phase loop: nothing simulated happens except at a clock advance, and the
system is idle at every event boundary. Quiescence detection alone would not order concurrent
handlers, so message order is pinned as well.

- `AC-SCEN-012.1` (Ubiquitous): THE SYSTEM SHALL advance virtual time to each next scheduled event in
  due-time then registration order, delivering each due send itself and waiting for quiescence between
  events.
- `AC-SCEN-012.2` (Event-driven): WHEN no event falls at the end of an advance budget THE SYSTEM SHALL
  advance the remainder, so exactly the requested virtual time elapses.
- `AC-SCEN-012.3` (Ubiquitous): THE SYSTEM SHALL advance one virtual instant per event hop and drain
  every event due at that instant before the hop returns.
- `AC-SCEN-012.4` (Ubiquitous): THE SYSTEM SHALL never move the virtual clock backward, and SHALL
  refuse a negative advance budget.
- `AC-SCEN-012.5` (Ubiquitous): THE SYSTEM SHALL treat the actor system as quiescent exactly when
  every mailbox is empty and no handler is in flight.
- `AC-SCEN-012.6` (State-driven): WHILE the quiescence predicate does not hold THE SYSTEM SHALL keep
  waiting rather than treat the system as settled.
- `AC-SCEN-012.7` (Ubiquitous): THE SYSTEM SHALL settle start-up traffic once before the first event
  hop of an advance.
- `AC-SCEN-012.8` (State-driven): WHILE the host is stepped THE SYSTEM SHALL deliver a message cascade
  in the same order across fresh hosts, for fan-in and fan-out alike.
- `AC-SCEN-012.9` (Ubiquitous): THE SYSTEM SHALL report whether a stepped host's virtual clock has
  moved from the baseline captured when it was built.
- `AC-SCEN-012.10` (Ubiquitous): THE SYSTEM SHALL guarantee a scenario run a clean slate on the
  topology the scenario declares, one active run per host, and no override.

`AC-SCEN-012.5` is exact rather than a time window: mailbox depth alone reads zero between a dequeue
and the handler's entry, and the in-flight count closes that window, so a single satisfying
observation is true quiescence. `AC-SCEN-012.6` is the other half of that: never a heuristic
stand-down. A generous real-clock safety budget bounds the wait so a genuinely stuck cascade surfaces
as a thrown failure naming the predicate rather than as a hang — that budget is a backstop, not a
tolerance, and no scenario is meant to reach it. `AC-SCEN-012.10` is what makes a run reproducible; the round-trip a
caller performs to get there is the control API's contract
([`../devhost-conventions.md`](../devhost-conventions.md) § 8).

## The topology file

The dev profile of a logic configuration: block instances and explicit interface wiring, with contract
mappings optional.

- `AC-SCEN-013.1` (Ubiquitous): THE SYSTEM SHALL require at least one block instance, each with a type
  name and an instance name unique within the topology and free of `.`.
- `AC-SCEN-013.2` (Ubiquitous): THE SYSTEM SHALL require every interface mapping to name declared
  instances, SHALL apply the mappings as declared rather than rediscover them, and SHALL refuse the
  topology reporting every incompatible interface pair at once.
- `AC-SCEN-013.3` (Event-driven): WHEN a topology names a type that is not loadable or is not a logic
  block THE SYSTEM SHALL refuse the topology reporting every such instance at once.
- `AC-SCEN-013.4` (Ubiquitous): THE SYSTEM SHALL resolve an instance type from a single snapshot of
  the loaded assemblies, then from an assembly-qualified lookup, then from a probe of the application
  base directory.
- `AC-SCEN-013.5` (Ubiquitous): THE SYSTEM SHALL leave a contract the file does not map on its
  auto-created mock endpoint, and SHALL refuse a mapping that names an instance or a contract the
  topology does not carry.
- `AC-SCEN-013.6` (Ubiquitous): THE SYSTEM SHALL apply each declared instantiation-parameter value to
  its instance before the block is configured.
- `AC-SCEN-013.7` (Ubiquitous): THE SYSTEM SHALL omit an optional collection or map from a serialized
  topology when it holds nothing, so "none" has one spelling on the wire.
- `AC-SCEN-013.8` (Ubiquitous): THE SYSTEM SHALL accept in the topology schema every
  instantiation-parameter value the loader decodes.
- `AC-SCEN-013.9` (Event-driven): WHEN a topology is saved THE SYSTEM SHALL parse it, require its
  declared id to match, build it, apply the running host's own check when one is supplied, and write a
  self-contained file.
- `AC-SCEN-013.10` (Ubiquitous): THE SYSTEM SHALL project a wired host's configuration into the file
  shape, emitting the schema reference and the same field names a configuration export uses.
- `AC-SCEN-013.11` (Ubiquitous): THE SYSTEM SHALL generate a default topology of one instance per
  catalog type, named after its type, wiring only unambiguous interface pairs, and SHALL leave an
  existing file untouched.
- `AC-SCEN-013.12` (Ubiquitous): THE SYSTEM SHALL generate a default topology the loader rebuilds into
  an equivalent wiring.
- `AC-SCEN-013.13` (Ubiquitous): THE SYSTEM SHALL serve the generic scenario schema and the generic
  topology schema from the host.
- `AC-SCEN-013.14` (Ubiquitous): THE SYSTEM SHALL check a topology in the phases instance types,
  instantiation parameters, interface mappings, contract mappings, contract pairings — reporting every
  error of the first failing phase and none of a later one — and SHALL collect within every phase.

`AC-SCEN-013.14` is why `AC-SCEN-001.5`'s "at once" stops at the phase boundary: each phase's checks
need what the phase before it settled, so an author who fixes a type name is shown a new class of
error rather than a shorter list of the same one.

An instantiation parameter's own decoding rule and the gates it resolves are
[`config-gating.md`](config-gating.md)'s (`AC-GATE-001.*`, `AC-GATE-012.*`); `AC-SCEN-013.6` and
`AC-SCEN-013.8` state only what the *file* may carry and who applies it.

## Contract pairing

A topology can declare that two service-provider contract endpoints are one wire. The generic stand-in
then re-delivers each side's captured outbound as the other side's inbound. **Nothing in the host
decides anything**: every behaviour a bench needs lives in an ordinary simulator block bound to a
provider face ([`../simulator-authoring.md`](../simulator-authoring.md)).

The declaration is symmetric; which directions carry a value is derived.

- `AC-SCEN-014.1` (Ubiquitous): THE SYSTEM SHALL key a contract pairing on a block and a contract
  binding, never on an endpoint triple, and SHALL give a pairing declared in a topology file, through
  the configuration builder, or in the editor one meaning.
- `AC-SCEN-014.2` (Event-driven): WHEN a pairing endpoint names an instance the topology does not
  declare, or both endpoints coincide, THE SYSTEM SHALL refuse the file structurally.
- `AC-SCEN-014.3` (Event-driven): WHEN a pairing names a contract its block does not bind, or repeats
  a pair already declared, THE SYSTEM SHALL refuse the configuration and SHALL carry no partially
  resolved pairing.
- `AC-SCEN-014.4` (Ubiquitous): THE SYSTEM SHALL materialise a pairing direction exactly when one
  side's declared outbound wire type is identical to the other side's declared inbound.
- `AC-SCEN-014.5` (Event-driven): WHEN a declared pairing has no type-identical direction THE SYSTEM
  SHALL refuse the configuration naming both endpoints' declared wire types.
- `AC-SCEN-014.6` (Event-driven): WHEN a pairing endpoint's handler declares no wire struct, or is not
  loaded, THE SYSTEM SHALL refuse it wording the two cases apart.
- `AC-SCEN-014.7` (Ubiquitous): THE SYSTEM SHALL apply the wire-type rule both when a host loads a
  topology and when a draft is validated or saved.
- `AC-SCEN-014.8` (Ubiquitous): THE SYSTEM SHALL resolve pairings against the endpoints the file's own
  contract mappings settled.
- `AC-SCEN-014.9` (Event-driven): WHEN a block writes a command on a paired endpoint THE SYSTEM SHALL
  record it as that contract's last written value before forwarding it.
- `AC-SCEN-014.10` (Ubiquitous): THE SYSTEM SHALL consult the pairing table only when capturing a
  command and never when delivering one.
- `AC-SCEN-014.11` (Ubiquitous): THE SYSTEM SHALL forward a captured value to its peer unchanged, and
  SHALL send nothing further when the endpoint is unpaired.
- `AC-SCEN-014.12` (Ubiquitous): THE SYSTEM SHALL carry every pairing hop as an ordinary actor
  message, so that a closed paired loop runs stepped and deterministic.
- `AC-SCEN-014.13` (Event-driven): WHEN a contract whose handler declares no inbound is driven THE
  SYSTEM SHALL drop the delivery and record why, and SHALL otherwise deliver a driven value to every
  block mapped to that contract.
- `AC-SCEN-014.14` (Event-driven): WHEN a config-time gate excludes a contract THE SYSTEM SHALL refuse
  a pairing that names it.

`AC-SCEN-014.10` is the invariant a closed loop rests on: a forward that re-entered the delivery path
would let stand-ins originate messages, and the loop would converge on stand-in recursion instead of
on block cadence. `AC-SCEN-014.9` is the other one — a `serviceProviderExpect` still reads the command
a paired output wrote. The strict posture of `AC-SCEN-014.5` is deliberate: a *declared* pairing that
can carry nothing is an authoring mistake and has to be loud.

## The offline validator

`dale scenario validate` judges a scenario against an exported configuration, so CI and editors catch
renames without booting a host per file. The runner stays authoritative; the validator mirrors it.

- `AC-SCEN-015.1` (Ubiquitous): THE SYSTEM SHALL resolve name paths in the offline validator by the
  same rules the runner applies.
- `AC-SCEN-015.2` (Event-driven): WHEN a scenario declares a topology the exported configuration does
  not describe THE SYSTEM SHALL skip name-path resolution and still apply every structural check,
  reporting the file as skipped for that topology when the structural checks found nothing and
  reporting the errors otherwise.
- `AC-SCEN-015.3` (Ubiquitous): THE SYSTEM SHALL emit a per-project scenario schema that is the
  generic document with its name-path definition replaced by that topology's valid paths and nothing
  else changed.
- `AC-SCEN-015.4` (Ubiquitous): THE SYSTEM SHALL offer a two-segment name path in an enriched schema
  only when it is unambiguous.
- `AC-SCEN-015.5` (Ubiquitous): THE SYSTEM SHALL ship the generic scenario schema to the command-line
  tool as the canonical file itself rather than a copy of it.

`AC-SCEN-015.4` is the enricher's half of `AC-SCEN-005.5`: the schema must not autocomplete a path the
runner would refuse.

## A scenario as a consumer's test

- `AC-SCEN-016.1` (Ubiquitous): THE SYSTEM SHALL yield one test case per committed scenario, carrying
  its id and declared topology and named by its title.
- `AC-SCEN-016.2` (Ubiquitous): THE SYSTEM SHALL surface each committed scenario's declared trace ids
  as traits on its test case.
- `AC-SCEN-016.3` (Ubiquitous): THE SYSTEM SHALL omit a scenario that does not parse or declares no
  topology from the test cases, and SHALL restrict them to one topology when one is named.
- `AC-SCEN-016.4` (Ubiquitous): THE SYSTEM SHALL surface committed scenarios as test cases at
  discovery time.
- `AC-SCEN-016.5` (Ubiquitous): THE SYSTEM SHALL hand a consumer a fresh host per scenario, owned and
  disposed by the caller, from a fixture that holds none and whose only consumer-specific seam is the
  block catalog.
- `AC-SCEN-016.6` (Ubiquitous): THE SYSTEM SHALL resolve a consumer's scenarios and topologies
  directories from the working directory upward to the repository root.

One host per scenario is the in-process form of `AC-SCEN-012.10`: it is what keeps two scenarios from
interleaving on a shared network.
