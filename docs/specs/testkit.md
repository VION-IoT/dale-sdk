---
trace: enforced
---

# The test kits: what the SDK ships so a block can be tested without a runtime

What the SDK guarantees a logic-block author who wants to test a block on their own machine, with no
runtime, no broker, no device and no development host. Five packable packages ship it — the core kit
`Vion.Dale.Sdk.TestKit`, the two I/O kits `Vion.Dale.Sdk.DigitalIo.TestKit` and
`Vion.Dale.Sdk.AnalogIo.TestKit`, and the two Modbus kits `Vion.Dale.Sdk.Modbus.Rtu.TestKit` and
`Vion.Dale.Sdk.Modbus.Tcp.TestKit`. Area code `TKIT`. Process:
[`../spec-process.md`](../spec-process.md).

The core kit is the substrate and the other four extend it. `LogicBlockTestContext<TLogicBlock>` is
an actor context that records what a block sends and hosts a controllable clock; the I/O kits add a
raise helper per face and a verification per direction; the Modbus kits add response simulation, a
fake client, two harnesses and a synchronous request queue. **A kit is a published package**, so
every helper, default, exception type and message shape below is a contract with readers outside this
repository.

The spine is the order an author meets the machinery: constructing a block, what `Build()` does to
it, the knobs, what the context records, the verification family, the raise helpers, the value
comparison, time, the Modbus kits, the surface, and the test discipline.

Cited rather than restated: [`block-lifecycle.md`](block-lifecycle.md) for the phases the builder
drives (`AC-LIFE-*`); [`emission.md`](emission.md) for the emission policy the kit can force on
(`AC-EMIT-001.3`, `AC-EMIT-001.4`); [`config-gating.md`](config-gating.md) for what an instantiation
parameter resolves against (`AC-GATE-012.1`); [`contracts.md`](contracts.md) for the binder's own
rules (`AC-BIND-*`); [`modbus.md`](modbus.md) for the client, the queue and the link policy the fakes
stand in for (`AC-MODB-*`); [`io.md`](io.md) for the four faces and the value contract (`AC-IO-*`).
Architecture decision
[`0081`](../../architecture/decisions/0081-inclusion-gates-resolve-at-bind.md) is the design authority
for where a kit's own discovery diverges from a host's, and is cited, never re-argued. Two more name
these kits as the shape they were decided around rather than as their subject:
[`0118`](../../architecture/decisions/0118-sdk-owns-protocol-link-diagnostics.md) (`:74`), whose link
diagnostics are driven through the synchronous queue below, and
[`0119`](../../architecture/decisions/0119-transaction-receipt-in-callback-signature.md) (`:63`,
`:66`), whose receipt is what the RTU kit's simulations stamp.

## Constructing a block

- `AC-TKIT-001.1` (Ubiquitous): THE SYSTEM SHALL construct a logic block for a test from a constructor taking one logger, supplying a fresh mock logger, and SHALL offer a second form returning that mock alongside the block.
- `AC-TKIT-001.2` (Event-driven): WHEN a requested block type has no constructor taking one logger THE SYSTEM SHALL refuse construction with a message naming the constructor the helper requires, and SHALL say instead that the type cannot be constructed when it is abstract.
- `AC-TKIT-001.3` (Event-driven): WHEN a block's own constructor throws THE SYSTEM SHALL propagate that exception rather than a reflection wrapper around it.

`AC-TKIT-001.1` is the entry point almost every consumer test starts from, and its constructor shape
is the whole of what it can do: a block whose constructor takes anything else — a clock, a client, a
second collaborator — is constructed by the test itself and handed to `CreateTestContext()`. That is
the supported path, not a workaround.

`AC-TKIT-001.2` and `AC-TKIT-001.3` are the three ways construction fails, told apart because they
need different answers: add the constructor, name the concrete type, or fix the constructor. The
first two keep `MissingMethodException`, which is what reflection already raised; the third replaces
a `TargetInvocationException` wrapper with the author's own exception, stack intact.

## What Build() does

- `AC-TKIT-002.1` (Ubiquitous): THE SYSTEM SHALL build a test context by driving the block through initialization, runtime-actor linking, persistent-state restoration, interface linking and start, in that order.
- `AC-TKIT-002.2` (Ubiquitous): THE SYSTEM SHALL initialize a block under a fixed logic-block identity and name, with the service and contract identifiers it discovers from the block's own class name and properties, and SHALL run every service registration declared in each discovered contract type's assembly.
- `AC-TKIT-002.3` (Ubiquitous): THE SYSTEM SHALL restore a declared persistent value under the key the block's own service-property bindings give it, falling back to a direct key when the property is not a service property, and SHALL store an enumeration value as its integer form.
- `AC-TKIT-002.4` (Ubiquitous): THE SYSTEM SHALL link each declared interface mapping to the block's own sender interface for that contract, addressing every mapped peer through a stand-in actor reference named after that peer. GAP: the kit's own test project cannot observe a mapping. A block binds its declared interfaces whether or not a mapping named them, so the bound-interface seam is over-determined by the declaration; the mapping is observable only as a message sent through the generated sender, and the generator that emits one is an analyzer on Vion.Dale.Sdk that does not travel through a project reference. The seven example suites drive the rule end to end against the published kit.
- `AC-TKIT-002.5` (Ubiquitous): THE SYSTEM SHALL start a built block and clear every message the start produced, unless the caller suppressed the start, in which case the messages the earlier phases produced remain recorded.
- `AC-TKIT-002.6` (Ubiquitous): THE SYSTEM SHALL expose the service provider the builder composed.

`AC-TKIT-002.1`'s five phases are the block lifecycle's, driven in the order a runtime drives them
(`AC-LIFE-*`). Two of them have no hook a block can observe; that `Ready` ran at all is what shows
initialization and the runtime-actor linking both happened.

`AC-TKIT-002.2` is where a kit and a host differ, and the difference is worth knowing before it
surprises someone. A host learns which contracts exist from the plugin it loaded; the builder learns
it from **the block under test** — the marked, writable properties the block itself declares. Two
consequences follow. A contract an inclusion gate would have excluded is mapped in a test and absent
in a host, because the discovery reads no gate (decision `0081`, and
[`_findings.md`](_findings.md) carries it). And a contract whose services live in an assembly the
block does not reach through a property is not registered, so its dependency does not resolve; the
supported answer is `WithServices`, which adds registrations the discovery would not find. Widening
the discovery would make a test's service graph depend on whatever else happened to be loaded, which
is why it stays as it is.

`AC-TKIT-002.5` is the asymmetry a test meets on its first `WithoutAutoStart`. The clear happens
inside the start, so suppressing the start keeps what the earlier phases produced — which is the
point: those messages are what an initialization test is about.

`AC-TKIT-002.6` is what makes a builder decision assertable at all. It is the composed container, so
a test can read back the clock the block will resolve or a service it added itself.

## The knobs

- `AC-TKIT-003.1` (Ubiquitous): THE SYSTEM SHALL return the builder from every knob, and SHALL leave the emission policy gated off unless a test asks for the block's declared throttling.
- `AC-TKIT-003.2` (Ubiquitous): THE SYSTEM SHALL apply a declared instantiation parameter to the block through the same encoded value channel a configuration payload uses.
- `AC-TKIT-003.3` (Ubiquitous): THE SYSTEM SHALL refuse a builder argument it cannot serve, naming what it could not resolve and the supported form.

`AC-TKIT-003.1`'s default is the one with the most weight on this page after `Times.Once()`: **the
emission policy is off unless a test asks for it**, so every assignment surfaces as a change and no
test is silently throttled by a min-interval or a deadband it did not think about. Asking for it is
`WithEmissionPolicy(EmissionPolicyMode.FromAttributes)`, which `emission.md` calls the only way to
exercise throttling deterministically (`AC-EMIT-001.3`, `AC-EMIT-001.4`).

`AC-TKIT-003.3` is one rule over six shapes — an ambiguous mapping target, a contract the block does
not implement, a mapping with no sender, a selector that is not a property access, an instantiation
parameter naming a property the block does not declare, and a null argument. Each names what it could
not resolve and the form that works, because the failure a caller cannot act on is the expensive one.

There is no builder knob for the clock's anchor. A test that needs a different one constructs its own
`FakeTimeProvider` and binds it with `WithTimeProvider`, which sets the anchor and the block's clock
together — the shape 33 call sites in the first consumer already use.

## What the context records

- `AC-TKIT-004.1` (Ubiquitous): THE SYSTEM SHALL record every message a block sends to an actor, to itself or in reply, in send order, taking the record and any action enqueue under one lock and answering every query from a materialised copy.
- `AC-TKIT-004.2` (Ubiquitous): THE SYSTEM SHALL answer an actor lookup with a stand-in reference of the requested name, rendering as that name inside `TestActorRef(…)`.
- `AC-TKIT-004.3` (Ubiquitous): THE SYSTEM SHALL clear the recorded messages when a test asks, and SHALL leave the actions a block has already queued armed for the next drive.

`AC-TKIT-004.1`'s lock is not incidental. A block driven by a real I/O client marshals its completion
callbacks from background threads, so the recording and the action queue take concurrent writes while
the test thread drives; taking both under one gate and answering every query from a materialised copy
is what makes the drive deterministic anyway.

`AC-TKIT-004.2` states the rendering because it is the only thing about the stand-in a test can read:
the reference type the lookup answers with implements a marker interface with no members, so the name
it was asked for reaches an assertion through the text form and nowhere else.

`AC-TKIT-004.3`'s second clause is the trap. The clear is scoped to the recording — a block's queued
actions survive it — and `Build()` makes the same call after an auto-start (`AC-TKIT-002.5`), so
*the builder cleared what start produced* is not *the context is reset*. What a start typically leaves
armed is the emission flush a held value schedules, so a test that turns the policy on
(`AC-TKIT-003.1`), clears, and then drives will see that flush fire.

## The verification family

- `AC-TKIT-005.1` (Ubiquitous): THE SYSTEM SHALL count the messages a verification matched against an expected number of occurrences, defaulting that expectation to exactly once.
- `AC-TKIT-005.2` (Ubiquitous): THE SYSTEM SHALL honour every occurrence-count form the mocking library expresses.
- `AC-TKIT-005.3` (Event-driven): WHEN a verification's occurrence count does not match THE SYSTEM SHALL fail with that verification's own message, the expected count and the actual count, rendering the counts in the invariant culture.
- `AC-TKIT-005.4` (Ubiquitous): THE SYSTEM SHALL filter each verification to its own stream and key, a service property or measuring point by member name, an interface send by message type and optionally its mapped peer, and a contract message by data type and optionally its contract identifier.
- `AC-TKIT-005.5` (Ubiquitous): THE SYSTEM SHALL run a caller's per-message assertion against every matching message rather than the first.
- `AC-TKIT-005.6` (Ubiquitous): THE SYSTEM SHALL treat a contract verification's message-kind argument as a label that names the verification in its failure message and filters nothing.
- `AC-TKIT-005.7` (Ubiquitous): THE SYSTEM SHALL return the recorded contract messages of a data type, and the recorded messages of any type, for assertions the verification helpers do not cover.
- `AC-TKIT-005.8` (Ubiquitous): THE SYSTEM SHALL raise one exception type for every assertion the kits fail, and SHALL offer a verification that a mocked logger recorded given text at a given level for both the bare and the typed logger mock.

`AC-TKIT-005.1`'s default is a contract: an omitted count means **exactly once**, not "at least
once". Every verification in every kit ends in the same check, including the Modbus TCP kit's, which
counts recorded operations rather than recorded messages.

`AC-TKIT-005.2` is satisfied by asking the mocking library's own predicate rather than reading the
constraint's rendered text. The forms are the library's, not the kit's, so a form the library adds
is honoured without a change here.

`AC-TKIT-005.4` is one rule with four keys, and one of them has a shape worth stating: the
service-property and measuring-point verifications read **separate streams**, because a member
carrying both annotations publishes to both and a verification of one must not count the other
([`emission.md`](emission.md)). Four members cover the two streams — a `Changed` and an `Emitted`
verification each — and the pair over one stream is the same check under two names: with the policy
off they are identical, and with it on the recorded stream is already the post-policy one. The pair
is kept because `sdk-surface-conventions.md` § 3 requires migrating every caller in the same change,
which a published kit's callers make unreachable.

`AC-TKIT-005.6` is the one argument that looks like a filter and is not. A contract verification
filters on the message's data type, on the contract identifier when given, and on the caller's own
predicate; the message-kind argument reaches the failure text and nothing else. It is documented as a
label so a reader stops expecting it to narrow anything.

## The raise helpers

- `AC-TKIT-006.1` (Ubiquitous): THE SYSTEM SHALL raise on an input face, an output face and an output provider face the event that face declares, carrying the given value, in both the digital and the analog kit, and SHALL offer no raise helper for an input provider face.
- `AC-TKIT-006.2` (Event-driven): WHEN a raise helper is given no face, or a face that is not the implementation the SDK ships, THE SYSTEM SHALL refuse it, naming the argument or the event it cannot raise.
- `AC-TKIT-006.3` (Ubiquitous): THE SYSTEM SHALL address a raised face message under the same logic-block identity the builder initializes a block with. GAP: no shipped reader reads a received contract message's identity — every face's HandleContractMessage dispatches on the message type and logs the face's own identity — so the rule guards the write path, where an empty identity is silently dropped, and no test can drive it until a helper raises through that path.

`AC-TKIT-006.1`'s absence is the faces' own shape rather than a gap: an input provider face carries a
`Drive` operation and no event ([`io.md`](io.md)), so there is nothing on it to raise. What a
simulator drove is asserted, not raised.

`AC-TKIT-006.2`'s second refusal is what makes the helpers safe to hand a mock: a raise reaches the
face's own message loop, which only the shipped implementation has, so a mocked face is refused at
the call site rather than silently doing nothing.

The identity rule above reaches further than the raise helpers. The Modbus RTU kit's four response
simulations (`AC-TKIT-009.1`) address their contract message the same way and carry the same
identity, for the same reason: the RTU contract's own message handler dispatches on message type and
logs the identity it already holds, so nothing downstream reads the one the kit supplies. The rule
guards the write path in both kits alike, and neither kit can test it, which is what the identity
criterion's own marker says.

## The value a verification compares

- `AC-TKIT-007.1` (Ubiquitous): THE SYSTEM SHALL assert that an output face was set, an output provider confirmed or an input provider drove a value, in both kits, treating an omitted face as any face of that kind and an omitted value as any value, and SHALL refuse a face that is not the implementation the SDK ships.
- `AC-TKIT-007.2` (Ubiquitous): THE SYSTEM SHALL compare a digital value for equality and an analog value within an inclusive tolerance defaulting to zero, and SHALL match an analog value that is bit-identical to the expected one at any tolerance, a non-number and both infinities included.
- `AC-TKIT-007.3` (Event-driven): WHEN an analog verification is given a tolerance that is not a finite number of at least zero THE SYSTEM SHALL refuse it, naming the tolerance.

`AC-TKIT-007.2` states one rule over two value types, and the analog half carries the whole of the
difference. A truth value has no near miss, so the digital comparison is equality and takes no
tolerance. A real number has near misses, so the analog comparison takes an inclusive tolerance
defaulting to zero — and compares **bit equality first**, because the value contract carries a
non-number and both infinities to the wire unaltered (`AC-IO-007.2`) and the difference comparison is
false for every one of them against itself at every tolerance. A signed zero matches an unsigned one
either way, which matches what the wire does with it.

`AC-TKIT-007.3` is the sibling rule. A tolerance is a width, so it is a number of at least zero: a
non-number or a negative one empties the band and rejects even an exact value, which no caller can
mean, and the failure a caller then reads speaks only of counts. An infinite tolerance is a legal
width that admits every finite value, so the refusal is written for "not a number, or below zero" and
not for "not finite".

## Time

- `AC-TKIT-008.1` (Ubiquitous): THE SYSTEM SHALL host a virtual clock anchored at the first instant of 2026 UTC, expose it as a time provider and as the current virtual instant, bind a caller-supplied clock to both the block and the context's deadlines when asked, and register the context's own clock for the block to resolve unless a later service registration replaces it for the block alone.
- `AC-TKIT-008.2` (Event-driven): WHEN the virtual clock is advanced THE SYSTEM SHALL dispatch every queued action whose deadline the advance reaches, in deadline order and in enqueue order among equal deadlines, setting the clock to each action's own deadline before running it, and SHALL dispatch an action queued during the advance whose deadline the advance still reaches while leaving one beyond it queued.
- `AC-TKIT-008.3` (Event-driven): WHEN a queued action's deadline already lies in the past THE SYSTEM SHALL run it at the current virtual time rather than moving the clock backwards.
- `AC-TKIT-008.4` (Ubiquitous): THE SYSTEM SHALL leave the clock at the instant an advance requested, whether the queue was empty, exhausted, or left by a dispatched action that threw, and SHALL leave every action the advance did not reach queued.
- `AC-TKIT-008.5` (Ubiquitous): THE SYSTEM SHALL run every action queued at the moment of a flush in one pass, ignoring their deadlines and the clock, deferring an action queued during the flush to the next one, and SHALL leave queued the actions a throwing action did not reach, ahead of everything queued during that flush or after it.
- `AC-TKIT-008.6` (Event-driven): WHEN an advance is asked to move the clock backwards, or either driver is entered from inside an action it dispatched, THE SYSTEM SHALL refuse.
- `AC-TKIT-008.7` (Ubiquitous): THE SYSTEM SHALL enqueue an action a block schedules with or without a delay so that either driver runs it, stamping a delayed action's deadline from the virtual clock at the moment it was scheduled.
- `AC-TKIT-008.8` (Ubiquitous): THE SYSTEM SHALL fire a block's timer callback and report its configured interval out of band of the virtual clock, selected by identifier or by a method-call expression, refusing an unregistered identifier from either query with the registered identifiers named.

**No kit waits on wall time.** There is no timeout anywhere in the five packages: a delay is virtual
time a driver consumes, and a test that wants to wait advances the clock.

`AC-TKIT-008.1`'s last clause is a trap worth stating plainly. `WithTimeProvider` binds one clock to
both the block and the context's deadlines. A clock registered through `WithServices` reaches the
block **only** — the context goes on driving its own — so the two diverge and every deadline
assertion silently misses. `WithServices` adds registrations; `WithTimeProvider` binds the clock.

`AC-TKIT-008.2` and `AC-TKIT-008.5` are the two drivers, and the difference between them is the whole
of what a test chooses. An advance moves the clock and dispatches what its deadlines have come due
for, in deadline order — it is the deadline-accurate driver, and it is what a cycle test should use.
A flush ignores every deadline and the clock, running whatever is queued in one pass; it is the older
knob, kept because a test that does not care about elapsed time is entitled not to. The first
consumer's own harness reached the same conclusion after driving cycles with a flush ran two cycles
per call.

`AC-TKIT-008.3`'s clamp has a reason: a background thread can stamp a deadline the drive has since
moved past, and the clock refuses to go backwards, so such an action runs at the current virtual time
exactly as a late message does in production.

`AC-TKIT-008.4` and `AC-TKIT-008.5`'s last clauses are one promise over two drivers — an exception
propagates, and what the driver did not reach is still there afterwards. The clock lands where the
caller asked whatever a dispatched action did, unless that action carried the clock past the target
itself, which a fake consuming virtual time does.

The flush half of that promise carries an ordering the advance half does not need. A flush detaches
what it is going to run before it runs any of it, so an action queued *by* one of those actions is
already in the queue when a later one throws; putting the unreached actions back at the head is what
keeps them ahead of it. Only a flush whose surviving actions raced with a newly queued one can tell
that apart from an append — which is what makes it worth stating rather than leaving to the
implementation.

`AC-TKIT-008.8` is deliberately out of band: advancing the clock never fires a timer, however far it
moves. A timer is fired explicitly, which keeps a periodic callback a thing a test decides rather
than a thing that happens to it.

## The Modbus RTU kit

- `AC-TKIT-009.1` (Ubiquitous): THE SYSTEM SHALL deliver a simulated Modbus RTU read or write outcome to the pending request's own callback through the contract message path the runtime uses, stamping the receipt's completion from the virtual clock.
- `AC-TKIT-009.2` (Ubiquitous): THE SYSTEM SHALL derive a simulated failure's outcome from its exception unless the caller names one, and SHALL carry no publish instant on an outcome a handler decides before publishing.
- `AC-TKIT-009.3` (Ubiquitous): THE SYSTEM SHALL answer the most recent recorded request matching the simulation's contract and, where given, its address, leaving that request answerable again.
- `AC-TKIT-009.4` (Event-driven): WHEN no recorded request matches a simulation, or the contract is not the implementation the SDK ships, THE SYSTEM SHALL refuse it, naming the request kind, the filter and what the block must do first.
- `AC-TKIT-009.5` (Ubiquitous): THE SYSTEM SHALL build Modbus response bytes most-significant byte first for each numeric width it offers, pack booleans least-significant bit first into one byte per eight, and return an empty array for an empty input.

`AC-TKIT-009.1` runs the block's own callback chain — the byte conversion, the receipt, the
dispatcher — so what a test exercises is the SDK's code against bytes the test chose, not a
stand-in for it.

`AC-TKIT-009.3`'s last clause is a shape rather than a defect: a simulation reads the recording and
does not consume it, so the same request can be answered again, and a test that answers twice gets
two receipts for one read. Clearing the recording is the test's own knob.

## The Modbus TCP kit

- `AC-TKIT-010.1` (Ubiquitous): THE SYSTEM SHALL hold a fake Modbus TCP client's register and coil contents as raw wire bytes per unit and address, answer a read of an address nothing populated with zeros, refuse register data that is not a whole number of registers, and refuse a single-register write of any length but one register.
- `AC-TKIT-010.2` (Ubiquitous): THE SYSTEM SHALL record every read, write and connection attempt a fake Modbus TCP client observes, in order, with that operation's own fields, before it decides that operation's outcome, copying a write's payload rather than aliasing the caller's buffer.
- `AC-TKIT-010.3` (Ubiquitous): THE SYSTEM SHALL record a multiple-coil write as one byte per coil, where a single-coil write records the wire's own on and off bytes and a coil read answers the wire's own bit packing.
- `AC-TKIT-010.4` (Ubiquitous): THE SYSTEM SHALL consume a queued fault on the next matching operation in the order the faults were queued, matching a read by its starting address, and SHALL queue connection failures separately from operation faults while leaving the connected state a failed attempt found.
- `AC-TKIT-010.5` (Ubiquitous): THE SYSTEM SHALL consume a configured delay of virtual time on every operation and connection attempt including one that then fails, requiring a virtual clock only when the delay is greater than zero, and SHALL refuse a delay below zero.

`AC-TKIT-010.1`'s two refusals are not one rule stated twice. The alignment refusal is about register
boundaries and admits any even length, so a four-byte payload passes it; the single-register arm is
about the function code, which writes one register and no more, and rejects those same four bytes.
A test that reaches only the first refusal leaves the stricter one unproven.

`AC-TKIT-010.3` is an asymmetry the recorded bytes make visible, so it is stated rather than
smoothed over. A single-coil write records the wire's own on and off bytes and a coil read answers
the wire's bit packing, but a multiple-coil write records one byte per coil — which is not what the
wire carries and is what makes an expected-bytes argument legible.

`AC-TKIT-010.5` is why a delay needs a clock: the delay is virtual time, and there is nothing to
consume it from otherwise. Zero is the default and needs none.

- `AC-TKIT-011.1` (Ubiquitous): THE SYSTEM SHALL wire a fake proxy and the synchronous queue into the real Modbus TCP client, and a fake server proxy into the real Modbus TCP server, registering its own overrides last and its own clock first so the SDK's conditional registration keeps it.
- `AC-TKIT-011.2` (Ubiquitous): THE SYSTEM SHALL hand a fake server out through a factory for a block that resolves one, and SHALL dispose the client or server it composed together with its container.
- `AC-TKIT-011.3` (Ubiquitous): THE SYSTEM SHALL measure a harness on the real system clock unless the caller supplies one, and SHALL refuse a null proxy on either harness and a null clock on the client harness, which is the one that takes a clock.
- `AC-TKIT-011.4` (Ubiquitous): THE SYSTEM SHALL run each request enqueued on the synchronous queue on the calling thread, routing its callbacks through the dispatcher the production queue uses.
- `AC-TKIT-011.5` (State-driven): WHILE the synchronous queue is held THE SYSTEM SHALL buffer enqueued requests, report how many it holds, and on a drain run those buffered at that moment in enqueue order, buffering again any enqueued during the drain.
- `AC-TKIT-011.6` (Ubiquitous): THE SYSTEM SHALL refuse a maximum queued age that is not greater than zero, and SHALL read the age at execution so a change reaches a request already buffered.
- `AC-TKIT-011.7` (Ubiquitous): THE SYSTEM SHALL discard the requests a drain never ran when the synchronous queue is disposed, refuse a request enqueued before the queue was initialized, and model neither a capacity nor an overflow policy.

`AC-TKIT-011.1` is what makes the Modbus kits worth their size: only the byte-level proxy and the
queue are fake, and everything between them and the block is the real client, the real converter and
the real request factory. `modbus.md` leans on the same wiring for two of its own criteria.

`AC-TKIT-011.3` is a default a test should know it has. A harness given no clock measures on the real
system clock, so a round-trip assertion against one is a wall-clock bound
([`../testing-conventions.md`](../testing-conventions.md) § 16); pass `ctx.TimeProvider` and it
becomes virtual time.

The two harnesses do not offer that clock the same way, which is why the criterion names them apart.
The client harness takes one as a constructor argument and refuses a null one there. The server
harness has no clock argument at all: its clock is the fake server proxy's own settable property,
which defaults to the system clock the same way (`AC-TKIT-012.3`) and is assigned rather than passed,
so there is no construction to refuse. Setting it to null is therefore accepted and would fail later
at the first stamped write — a refusal on a published setter is a change a consumer would see, so it
is stated here rather than added.

`AC-TKIT-011.5` is the documented seam for putting virtual time between an enqueue and its execution,
which is what a maximum queued age measures.

`AC-TKIT-011.7` names what this queue does **not** model, because a test that assumed otherwise would
pass for the wrong reason: it has no capacity and no overflow policy, so `modbus.md`'s overflow rules
cannot be exercised through it.

## The fake Modbus TCP server

- `AC-TKIT-012.1` (Ubiquitous): THE SYSTEM SHALL offer a master-side view of a fake Modbus TCP server whose members name the client surface and whose typed values are encoded independently of the converter under test, reachable only through the harness that composed it.
- `AC-TKIT-012.2` (Ubiquitous): THE SYSTEM SHALL hold a fake server's full address range regardless of the declared extents, validate every simulated client access against those extents with the illegal-data-address exception a master receives, and refuse a simulated access while the server is not listening.
- `AC-TKIT-012.3` (Ubiquitous): THE SYSTEM SHALL stamp a fake server's last client write from its own clock, which defaults to the real system clock, and SHALL let a test set the reported connection count and last-write instant directly.

`AC-TKIT-012.1`'s independent encoding is deliberate: the master view encodes with the platform's own
primitives rather than the converter under test, so a conversion bug cannot cancel itself out across
a round trip.

`AC-TKIT-012.2`'s two guards fire in argument-then-state order: an alignment complaint about the
bytes reaches the caller before the complaint that the server is not listening.

## The published surface

- `AC-TKIT-013.1` (Ubiquitous): THE SYSTEM SHALL ship the test kits as packable packages targeting the runtime tests run on, classify every public type each ships as published surface, and declare no assertion or test framework in any of them.
- `AC-TKIT-013.2` (Ubiquitous): THE SYSTEM SHALL judge each kit's own declarations with the Dale analyzers, so a public type in a kit's declared published namespace that carries neither surface mark draws a diagnostic in that kit's build.
- `AC-TKIT-013.3` (Ubiquitous): THE SYSTEM SHALL carry the mocking library and the controllable time provider in the published signature of the core kit.
- `AC-TKIT-013.4` (Ubiquitous): THE SYSTEM SHALL name every package a release publishes in the roster the version script clears from the local package cache.

`AC-TKIT-013.2` is what makes `AC-TKIT-013.1` enforceable rather than aspirational. Each kit declares
its own namespace as published surface, and until the analyzer reference landed beside that
declaration nothing read it — the API manifest's diff was the only gate on a quarter of the manifest,
and a manifest drift is auto-committed rather than failed. The diagnostic is a warning, so the proof
is the diagnostic each kit's probe build emits and not a failed build.

`AC-TKIT-013.3` is a cost a consumer inherits rather than chooses: taking a kit takes the mocking
library and the controllable time provider with it, because both are in the signature — an occurrence
count on every verification, a mock logger from the construction helper and the log assertions, and
the clock on the context. No kit pins a test framework, which is what lets the ten consumer-facing
suites in this repository be xunit while the sixteen inside it are MSTest.

The kits reach into the SDK for three things a runtime would otherwise hand over: an emission-policy
marker and a bound-services accessor through `InternalsVisibleTo`, and three private fields by
reflection — the service binder and the interface map on the builder, the timer callbacks on the
timer helpers. That is what standing in for a runtime costs, and it is stated here so it is not
rediscovered as a surprise. It is not licence for a *test* to do the same
([`../testing-conventions.md`](../testing-conventions.md) § 7).

**What the kits do not offer.** There is no test context for a service-provider handler. A handler
cannot be hosted by `LogicBlockTestContext<TLogicBlock>`, whose type parameter is a logic block, so
the SDK's own handler suites each hand-roll a recording actor context — three of them, in three
shapes. Building one is a new published type family and a refactor of a published generic;
[`_findings.md`](_findings.md) carries the design and its cost.

## Test discipline

- `AC-TKIT-014.1` (Ubiquitous): THE SYSTEM SHALL test each kit from its own test project, driving the kit through a fixture logic block and reaching no runtime, broker, device or development host, with the two I/O kits' suites proving the same rules over their own value type.
- `AC-TKIT-014.2` (Ubiquitous): THE SYSTEM SHALL leave a suite that proves another area's criteria cited to that area.

Each kit has its own MSTest project, mirroring the package it tests. The kit is the subject and a
fixture logic block is how it is driven — the ideal-echo recipe of
[`../simulator-authoring.md`](../simulator-authoring.md) — so a test exercises the helper rather than
the thing the helper stands in for.

The two I/O kits' suites are mirrors, as their packages are: one design over two value types, with
the value type as the row of each rule. What legitimately differs is the tolerance family, which
exists only on the analog side because a truth value has no near miss.

`AC-TKIT-014.2` is why a third of the tests in these projects cite another page. The core kit's
project is where emission, gating and lifecycle criteria are most cheaply driven — a block under a
controllable clock is exactly what they need — and the Modbus TCP kit's project is where the link
policy is. They stay where they are and keep citing the page that owns them; one suite there proves
introspection rules over the Modbus diagnostics structs and cites nothing yet, which is `MODB`'s and
`INTRO`'s call rather than this page's.
