---
trace: enforced
---

# The life of a logic block: the message sequence, the hooks, and the actor pipeline

What `LogicBlockBase` guarantees a block author and the runtime across the sequence a block's actor
receives — configure, link, restore, start, run, stop, snapshot, terminate — and what
`Vion.Dale.ProtoActor` guarantees per message underneath it. Area code `LIFE`. Process:
[`../spec-process.md`](../spec-process.md).

The spine of this page is that sequence, in the order the runtime sends it. The three hooks a block
overrides get a section of their own between start and stop, because they are the consumer's contract
in prose and a block author reads them as a set. The pipeline's per-message contract, the optional
observation seams, the runtime vitals and the two dependency-injection registrations follow.

Cited rather than restated: [`config-gating.md`](config-gating.md) for what a refused configuration,
an instantiation parameter and an inclusion gate do to the sequence; [`emission.md`](emission.md) for
what a value change does inside it; [`scenarios.md`](scenarios.md) for what deterministic stepping
does to a delayed send; [`devhost-control.md`](devhost-control.md) for the ordering a development host
imposes on the sequence and where a block's failure becomes readable;
[`introspection.md`](introspection.md) for the member vocabulary; [`plugin-loading.md`](plugin-loading.md)
for which assemblies a plugin's service registration is discovered from. The analyzer diagnostics that
guard the same rules at compile time — `DALE002`, `DALE005`, `DALE007`, `DALE012` — are the analyzer
area's and are named, never restated.

## The four outcomes

A message a block cannot handle cleanly has exactly four ends, and which one a caller gets is the
contract.

- **Refused** — the configuration message, and only it, answers a failure by throwing.
- **Skipped with a warning** — a mapping, a link or a persisted entry naming something this instance
  does not carry. The block stays whole; the message is partly applied.
- **Ignored** — a value change, a timer tick or a periodic save arriving while the block is not
  started.
- **Acknowledged anyway** — restore, start, stop and snapshot always answer, whatever state the
  instance is in, because the runtime waits on each answer and a silent block hangs its own
  reclamation.

There is no fifth. A message that is neither applied, nor answered, nor reported is a defect, and
every one this page's pass found was made into one of the four.

## The message vocabulary

- `AC-LIFE-001.1` (Ubiquitous): THE SYSTEM SHALL handle fourteen kinds of message on a logic block's
  actor — the runtime-actor link, the configuration, the linked-interface map, the restore, the start,
  the stop, the republish, the snapshot request, a contract message, an interface message, a set-value
  request, and the three the block sends itself — and SHALL ignore every other message without error
  and without acknowledgement.
- `AC-LIFE-001.2` (Ubiquitous): THE SYSTEM SHALL only send, and never receive, a block's
  service-binding announcement, its persistent-data snapshot notification, its four lifecycle
  acknowledgements, its set-value answer and its four value-changed and value-cleared publications.
- `AC-LIFE-001.3` (Ubiquitous): THE SYSTEM SHALL keep the three messages a block sends itself — a
  timer tick, a scheduled action and a periodic save — off the published vocabulary, so no host can
  inject one.
- `AC-LIFE-001.4` (Event-driven): WHEN a contract or interface message names an endpoint this instance
  did not bind THE SYSTEM SHALL drop it with a warning naming the endpoint and keep the block running.

`AC-LIFE-001.1` is a count because the arms are the surface: a host driving a block outside the
runtime knows exactly what it can send, and a version of the SDK that does not know a message answers
it with silence rather than with a fault. `AC-LIFE-001.4` is the same rule
[`config-gating.md`](config-gating.md)'s `AC-GATE-008.3` states for a routed message, and the two
directions are one rule.

Two message types the SDK publishes are handled nowhere in this repository — the remote-interface link
and the remote installation topics. They are the private runtime's remote-interface proxy handler's;
this page carries them, and specifies them nowhere.

## Configuring a block

- `AC-LIFE-002.1` (Ubiquitous): THE SYSTEM SHALL run the configuration phase once per instance, inside
  the configuration message, in the order instantiation-parameter values, the declarative binders,
  the emission gates, the contract mappings, persistence, the service announcement, the ready hook.
- `AC-LIFE-002.2` (Ubiquitous): THE SYSTEM SHALL take a block's identifier and name from its
  configuration message and make them readable by the block and by nothing outside it.
- `AC-LIFE-002.3` (Conditional): WHERE the configuration message's service provider carries a vitals
  collector or a clock THE SYSTEM SHALL use them, and SHALL otherwise measure nothing and read the
  real clock.
- `AC-LIFE-002.4` (Event-driven): WHEN any part of the configuration phase fails THE SYSTEM SHALL
  record the reason before letting the failure out, whether the phase ended inside the configuration
  message or on the runtime-actor link that completed it.

The phase order is the contract, not an implementation shape: parameter values are applied before the
binders because the inclusion gates resolve against them, the gates are built before the mappings
because a mapping addresses a bound member, and persistence is initialised last because discovery has
to see the configured member set rather than the declared one. What each binder does belongs to the
binding, emission, gating and introspection pages; that they run once, here, in this order, is this
page's.

A configuration is spent by running, whether it succeeded or failed, and a second one is refused —
`AC-GATE-004.1`. `AC-LIFE-002.4` is what lets that refusal name the original reason instead of
pointing an operator back at the configuration that just failed.

A bound contract the configuration carries no mapping for is warned about and left carrying no
traffic; that warning is its only tell, so it is stated here and carries no criterion of its own.

## Linking

- `AC-LIFE-003.1` (Ubiquitous): THE SYSTEM SHALL accept the configuration message and the
  runtime-actor link in either order, completing the configuration on whichever arrives second.
- `AC-LIFE-003.2` (Ubiquitous): THE SYSTEM SHALL announce a block's bound services and run its ready
  hook exactly once, however the two arrive.
- `AC-LIFE-003.3` (Ubiquitous): THE SYSTEM SHALL announce a block's bound service properties and
  measuring points to both handlers before any publication from that block.
- `AC-LIFE-003.4` (Event-driven): WHEN a bound service has no identifier in the configuration THE
  SYSTEM SHALL omit it from the announcement and drop every publication of it for the instance's
  life, warning each time.
- `AC-LIFE-003.5` (Event-driven): WHEN a linked-interface map arrives before the configuration THE
  SYSTEM SHALL hold it and apply it at the configuration's tail, after the ready hook.

`AC-LIFE-003.3` is why the tail can be deferred at all: the handlers dispatch a value's codec from
the announcement, so an announcement that arrives after a publication loses that publication
silently. `AC-LIFE-003.5` puts the map after the ready hook rather than before it because
`AC-LIFE-012.1` promises a block that its links are not yet readable there.

`AC-LIFE-003.4` is the one place the four outcomes leave a caller with less than a message: the
publications are dropped, and only a log says so. The finding ledger carries what a configuration that
omits a bound service should do instead — the answer belongs to the service allocation that produced
it, not to the block that received it.

## Restoring persisted values

- `AC-LIFE-004.1` (Ubiquitous): THE SYSTEM SHALL apply a restore before a block is started and SHALL
  acknowledge it whether or not the block was ever configured.
- `AC-LIFE-004.2` (Ubiquitous): THE SYSTEM SHALL apply each persisted entry independently, so an entry
  that cannot be applied leaves the others applied.
- `AC-LIFE-004.3` (Ubiquitous): THE SYSTEM SHALL convert a persisted value a storage layer returned in
  its serialised form into the member's own type, reading camel-cased names and string-named enum
  members.
- `AC-LIFE-004.4` (Event-driven): WHEN a persisted value cannot be converted into its member's type
  THE SYSTEM SHALL log the failure, leave the member at the value it holds, and complete the restore.

A gateway's persisted file outlives the release that wrote it, so `AC-LIFE-004.2` and `AC-LIFE-004.4`
are what keep one member's schema change from costing the gateway every other member's state — and
`AC-GATE-003.4` is the same tolerance for a member the instance no longer carries. The conversion of
`AC-LIFE-004.3` is the SDK's because the store cannot do it: it resolves a type from a name and most
compound type names do not resolve, so the value arrives as raw serialised data and the member's
declared type is the only thing that can read it.

## Starting a block

- `AC-LIFE-005.1` (Ubiquitous): THE SYSTEM SHALL start a block by running its start hook, marking it
  started, publishing every bound member's current value, arming the periodic save and acknowledging,
  in that order.
- `AC-LIFE-005.2` (Event-driven): WHEN a block's start hook throws THE SYSTEM SHALL leave the block not
  started and send no acknowledgement.
- `AC-LIFE-005.3` (Event-driven): WHEN a started block is started again THE SYSTEM SHALL acknowledge
  and change nothing.
- `AC-LIFE-005.4` (Event-driven): WHEN a block that was stopped is started again THE SYSTEM SHALL start
  it.

The order in `AC-LIFE-005.1` is what makes the hook contract true: the hook runs before the flag is
set, so a write from inside it is dropped like any other pre-start write, and the initial publish runs
after the flag, so it is what carries the block's starting state — `AC-EMIT-001.*` for how each of
those publications passes its gate. `AC-LIFE-005.3` exists because a second start used to arm a second
periodic-save chain that no stop retired.

A block whose start hook threw never acknowledges, so a host's start fails within its own real-time
budget and names the block through the failures it records — `AC-CTRL-002.4` and `AC-CTRL-003.*`. A
block whose *configuration* failed is a different case: it still starts, still publishes over whatever
bindings the failed configuration registered, and still acknowledges. The finding ledger carries that,
with the three shapes a refusal could take and the reader each one has.

## The dispatcher

- `AC-LIFE-006.1` (Ubiquitous): THE SYSTEM SHALL run an action handed to the dispatcher on the block's
  own actor, immediately or after a requested delay, so a block needs no locking of its own state.
- `AC-LIFE-006.2` (Event-driven): WHEN a delayed action is scheduled at a delay already past THE SYSTEM
  SHALL arm it as due now, and WHEN it is scheduled further out than a real clock can wait THE SYSTEM
  SHALL refuse it naming the member.
- `AC-LIFE-006.3` (Event-driven): WHEN either dispatcher member is called before the block has received
  its first message THE SYSTEM SHALL refuse the call, naming the hooks a block schedules from.
- `AC-LIFE-006.4` (Ubiquitous): THE SYSTEM SHALL run a delayed action that comes due after a stop, the
  dispatcher offering no cancellation.

`AC-LIFE-006.2` and `AC-LIFE-006.3` are guards over a silent loss rather than over a crash: both
failures used to leave the handler as an exception the middleware swallows, so a block simply stopped
rescheduling its own cycle and nothing said why. The bound on a delay is the one a scenario's
durations already carry (`AC-SCEN-003.2`), because it is the same real clock underneath.

`AC-LIFE-006.4` is the honest half of the stop hook's symmetry rule: an action already armed cannot be
cancelled, so a cycle that must not run after a stop ends by not re-scheduling itself.

## Timers

- `AC-LIFE-007.1` (Ubiquitous): THE SYSTEM SHALL call a `[Timer]` method at its declared interval for
  as long as its block's actor lives, under the identifier the attribute declares or, where it declares
  none, the method's name.
- `AC-LIFE-007.2` (Event-driven): WHEN a `[Timer]` cannot be scheduled as declared THE SYSTEM SHALL
  refuse the block's configuration naming the method — a signature that is not void and parameterless,
  an identifier that is empty or that another timer of the block already took, or an interval that is
  not a finite number of seconds of at least one clock tick and at most what a real clock can wait.
- `AC-LIFE-007.3` (Ubiquitous): THE SYSTEM SHALL bind a `[Timer]` declared anywhere in a block's base
  chain at any accessibility, counting a method and the method it overrides as one timer.
- `AC-LIFE-007.4` (Ubiquitous): THE SYSTEM SHALL arm a timer's first tick when the timer is registered,
  during the configuration, so the first tick falls one interval after the configuration.
- `AC-LIFE-007.5` (Event-driven): WHEN a timer tick arrives while the block is not started THE SYSTEM
  SHALL arm the next tick and invoke nothing, so a stopped block's timers keep their cadence and a
  restarted block resumes them.
- `AC-LIFE-007.6` (Ubiquitous): THE SYSTEM SHALL arm a timer's next tick before invoking its callback,
  so a callback that throws does not end the chain.
- `AC-LIFE-007.7` (Conditional): WHERE a vitals collector is registered THE SYSTEM SHALL report each
  timer callback's duration and the difference between the observed and the declared interval, and
  SHALL report them for a callback that threw as well as one that returned.

`AC-LIFE-007.2` is one criterion for four refusals because they are one rule — a timer the block
cannot run is refused where the author can be told which method to edit. Two of them are also guarded
at compile time by the analyzer's `DALE002` and `DALE012`, and the interval is guarded a third time by
the attribute's own constructor, which refuses zero and below: the criterion is over-determined and
deliberately so, because a block can ship past a warning-level diagnostic and a suppressed one leaves
only the binder.

`AC-LIFE-007.3` closes a hole rather than adding a feature: a base class of blocks that schedules its
own cycle from a private method bound no timer at all and warned about nothing.

`AC-LIFE-007.5` is why a stopped block still holds its cadence. Ending the chain at the stop would
leave a restarted block silent forever, because timers are armed at configuration and never again.

## Persistence

- `AC-LIFE-008.1` (Ubiquitous): THE SYSTEM SHALL persist every writable service property of a
  configured block unless its author excluded it, every property its author marked persistent, and
  every persistent property one level inside a class-typed property.
- `AC-LIFE-008.2` (Ubiquitous): THE SYSTEM SHALL discover those members anywhere in the block's base
  chain at any accessibility, counting a property hidden or overridden further down once.
- `AC-LIFE-008.3` (Event-driven): WHEN a property marked persistent has no setter THE SYSTEM SHALL
  persist nothing for it.
- `AC-LIFE-008.4` (Ubiquitous): THE SYSTEM SHALL key a persisted service member by its service and
  member identifiers, and a persisted property of the block by its own name, or by its parent's and
  its own, under a reserved prefix.
- `AC-LIFE-008.5` (Ubiquitous): THE SYSTEM SHALL replace a block's whole snapshot each time it takes
  one, capturing each member independently so one that cannot be read leaves the others captured.
- `AC-LIFE-008.6` (Ubiquitous): THE SYSTEM SHALL carry each persisted entry as its key, its member's
  declared type name and its value.
- `AC-LIFE-008.7` (Ubiquitous): THE SYSTEM SHALL take a snapshot and notify the persistence manager
  once a minute while a block is started, and SHALL skip the save and arm no further one otherwise.

`AC-LIFE-008.4` and `AC-LIFE-008.6` are a wire: every persisted file on every gateway is written and
read back through them, so a key shape or a field that moves strands the state it names.
`AC-LIFE-008.2` is the persistence twin of `AC-LIFE-007.3` and closed the same hole.

What a gate does to any of this is `config-gating.md`'s: an instantiation parameter is never persisted
(`AC-GATE-003.3`), an excluded component's members stop being captured (`AC-GATE-009.2`), and a
persisted key the instance no longer carries restores as nothing (`AC-GATE-003.4`). The analyzer's
`DALE007` is the compile-time door on `AC-LIFE-008.3`.

`AC-LIFE-008.7`'s second half is what retires the save chain a stop leaves armed — the chain arms its
successor only from a save that ran.

## Writing to a block, and republishing

- `AC-LIFE-009.1` (Ubiquitous): THE SYSTEM SHALL apply a set-value request whether or not the block is
  started, and SHALL answer the requester with the member's value after the write.
- `AC-LIFE-009.2` (Event-driven): WHEN a set-value request names a service the block does not carry THE
  SYSTEM SHALL warn and send no answer.
- `AC-LIFE-009.3` (Event-driven): WHEN a member's value changes while the block is not started THE
  SYSTEM SHALL publish nothing.

`AC-LIFE-009.1` is what the runtime replays an operator's writes through during a boot: the value
lands on the member and the initial publish at start is what carries it outward. The refusal of a
write to an instantiation parameter, and the answer that carries the value that did not move, are
`AC-GATE-003.1` and `AC-GATE-003.2`.

`AC-LIFE-009.2` is deliberately silent, and it is the one request in the sequence that goes unanswered.
There is no value to answer with — the member does not exist — and the runtime's property handler
publishes every answer it receives, so answering with nothing would tell the cloud the member had been
set to nothing. A host refuses such a request before sending it (`AC-CTRL-009.1`), and the runtime's
caller bounds its own wait.

## Stopping a block

- `AC-LIFE-010.1` (Ubiquitous): THE SYSTEM SHALL stop a block by taking its persistent-data snapshot,
  running its stop hook, publishing each gated member's exact current value, marking it stopped,
  clearing its retained publications and acknowledging, in that order.
- `AC-LIFE-010.2` (Ubiquitous): THE SYSTEM SHALL take the snapshot before the stop hook runs, so a
  write made from inside the hook does not survive a restart.
- `AC-LIFE-010.3` (Ubiquitous): THE SYSTEM SHALL keep a block in its started state for the whole of its
  stop hook, so its bindings, its links and its members behave normally inside it.
- `AC-LIFE-010.4` (Ubiquitous): THE SYSTEM SHALL run a block's stop hook and acknowledge the stop
  whether or not the block was ever configured or started.
- `AC-LIFE-010.5` (Event-driven): WHEN a stop hook throws THE SYSTEM SHALL finish the stop and
  acknowledge it, then report the failure, so a stop hook cannot refuse a shutdown and cannot fail
  invisibly.
- `AC-LIFE-010.6` (Event-driven): WHEN a block that has stopped is stopped again THE SYSTEM SHALL
  acknowledge and change nothing.

`AC-LIFE-010.4` keys on whether the block has *stopped*, not on whether it started: a block whose start
hook threw never started and still holds whatever its ready hook acquired, and the stop hook is the
only place it can release it. `AC-LIFE-010.6` is the other half — a hook that disposes a client must
not be asked to dispose it twice.

`AC-LIFE-010.5`'s report is what puts a failed stop on a host's health surface (`AC-CTRL-003.1`), where
a caller can poll it; the log line alone was readable nowhere. A caller driving the stop message
directly rather than through an actor sees the failure as an exception of its own.

A device write issued from a stop hook is best-effort and the SDK cannot make it anything else — the
hook returns before the write is delivered, and terminating the actor disposes the client that owns
the queue. What makes such a write land is the runtime's stop grace period, which is the runtime's
guarantee and not the SDK's.

## The snapshot, and termination

- `AC-LIFE-011.1` (Ubiquitous): THE SYSTEM SHALL answer a snapshot request with the block's identifier
  and a copy of the snapshot its stop took, rather than a freshly captured one.
- `AC-LIFE-011.2` (Event-driven): WHEN a block whose persistence was never initialised is asked for its
  snapshot THE SYSTEM SHALL answer with an empty one.
`AC-LIFE-011.1` returns the stop's capture and not a new one because the stop hook runs between them:
a fresh capture here would persist exactly the values `AC-LIFE-010.2` promises will not survive.
`AC-LIFE-011.2` is `AC-GATE-003.5`'s shape at the last message of the sequence. What happens after the
snapshot — the actor's termination and the disposal of the scope its dependencies came from — is
`AC-LIFE-015.2`.

## What the three hooks promise

The hooks are the consumer's contract in prose, and their documentation is where a block author reads
it. Each promise below is kept by the sequence above rather than by a guard of its own.

- `AC-LIFE-012.1` (Ubiquitous): THE SYSTEM SHALL call a block's ready hook once, after its declarative
  bindings are in place and before its persisted values are restored, its links are registered and its
  outbound path is open — so a member read there holds its declared default, a link enumerated there is
  empty, and a write made there is dropped and logged.
- `AC-LIFE-012.2` (Ubiquitous): THE SYSTEM SHALL call a block's start hook once, after the restore and
  the links, so a member read there holds its restored value on a host that restores, a link
  enumerated there is complete, and a scheduled cycle started there runs.
- `AC-LIFE-012.3` (Ubiquitous): THE SYSTEM SHALL call a block's stop hook once, as the only hook at
  which a block can release what it acquired, offering no guarantee that a device write issued there is
  delivered or that an emitted value is observed.

`AC-LIFE-012.1`'s three withholdings are the ones a block author gets wrong: each is silent, not an
error. `AC-LIFE-012.2`'s "on a host that restores" is exact — the production runtime restores before it
starts a block and the development host restores nothing, so a persisted value read in the start hook
is a default in the development lane and the operator's value in the field. The finding ledger carries
whether the development host should send an empty restore for parity.

`AC-LIFE-012.3` says *only* hook because nothing else disposes a block: the pipeline disposes the scope
a block's dependencies came from and never the block, so a block implementing a disposal interface has
a method nothing calls.

## Identity

- `AC-LIFE-013.1` (Ubiquitous): THE SYSTEM SHALL name a logic block's actor by a fixed prefix, the
  block's name and its identifier, and SHALL keep that construction off the published surface.
- `AC-LIFE-013.2` (Ubiquitous): THE SYSTEM SHALL classify an actor whose name carries that prefix as a
  logic block, reporting its class and the assembly it came from, and every other actor as part of the
  runtime, reporting its class alone.

The prefix is what a registry scan matches — it is how a host finds the blocks to stop
(`AC-CTRL-004.5`) and how the vitals tell a block apart from a publisher. Nothing splits the name back
into its parts, which is why the construction can be a concatenation.

## What the pipeline guarantees per message

- `AC-LIFE-014.1` (Conditional): WHERE a message observer is registered THE SYSTEM SHALL notify it
  before each message is dispatched and again after it is handled, carrying the handler's duration and
  the exception it threw where it threw one.
- `AC-LIFE-014.2` (Event-driven): WHEN a handler throws THE SYSTEM SHALL log the failure, drop the
  message and leave the actor running.
- `AC-LIFE-014.3` (Ubiquitous): THE SYSTEM SHALL isolate a faulty observer or activity monitor, so its
  exception affects neither message delivery nor another observer.
- `AC-LIFE-014.4` (Conditional): WHERE an activity monitor is registered THE SYSTEM SHALL enter it
  before a handler runs and leave it afterwards, on the path where the handler returned and on the path
  where it threw alike.
- `AC-LIFE-014.5` (Ubiquitous): THE SYSTEM SHALL carry the sending actor's own reference, and the
  headers of the message it is handling, on every message it sends.
- `AC-LIFE-014.6` (Event-driven): WHEN a handler answers a message that carries no sender THE SYSTEM
  SHALL refuse the answer, naming the member and the reason.

`AC-LIFE-014.2` is why one bad block cannot take a network down, and the cost is that a block which
failed to configure, to bind or to start leaves no trace on its own state. `AC-LIFE-014.1`'s second
notification is where that trace exists instead, and a host turns it into a readable per-block failure
list (`AC-CTRL-003.1`). `AC-LIFE-014.4` is the bracket deterministic stepping's quiescence rests on —
`AC-SCEN-012.*` states what the monitor does with it.

## Spawning an actor

- `AC-LIFE-015.1` (Ubiquitous): THE SYSTEM SHALL spawn an actor from a caller's factory, from a runtime
  type and from a compile-time type, wiring the same observation, vitals and dispatch onto each.
- `AC-LIFE-015.2` (Ubiquitous): THE SYSTEM SHALL resolve a dependency-injected receiver's dependencies
  from a scope of that actor's own and SHALL dispose that scope when the actor terminates, or at once
  where construction failed before the actor took ownership of it.
- `AC-LIFE-015.3` (Ubiquitous): THE SYSTEM SHALL construct a receiver for its actor rather than resolve
  it, so the lifetime its plugin registered changes nothing about how many instances exist.
- `AC-LIFE-015.4` (Ubiquitous): THE SYSTEM SHALL never dispose a receiver, whatever interfaces it
  implements.
- `AC-LIFE-015.5` (Event-driven): WHEN an actor is spawned under a name already taken THE SYSTEM SHALL
  refuse the spawn.
- `AC-LIFE-015.6` (Ubiquitous): THE SYSTEM SHALL deliver neither a null message nor the actor
  framework's own lifecycle messages to a receiver.

`AC-LIFE-015.2` is what keeps a per-block protocol client from being pinned on the root container until
the process exits — one stranded socket per same-version redeploy was the alternative. `AC-LIFE-015.3`
and `AC-LIFE-015.4` are the two halves a block author is most likely to assume the other way round:
the registration is a discovery entry rather than a lifetime, and the block is not what the scope owns.

## Waiting on a set of actors

- `AC-LIFE-016.1` (Ubiquitous): THE SYSTEM SHALL send one request to each actor of a set and complete
  when every one has acknowledged, returning each actor's acknowledgement against the reference the
  caller passed.
- `AC-LIFE-016.2` (Event-driven): WHEN a wait is given no actors THE SYSTEM SHALL complete at once.
- `AC-LIFE-016.3` (Event-driven): WHEN a wait's timeout elapses before every actor has answered THE
  SYSTEM SHALL fail it naming how many had not.
- `AC-LIFE-016.4` (Event-driven): WHEN a wait is given a negative timeout THE SYSTEM SHALL refuse it
  naming the parameter, before any message is sent or any actor is watched, and SHALL treat a timeout
  of nothing as an expiry that has already happened.
- `AC-LIFE-016.5` (Ubiquitous): THE SYSTEM SHALL count each actor's answer once and only for itself, so
  a repeated answer, or one from an actor that was not asked, cannot complete a wait on an actor that
  has not answered.
- `AC-LIFE-016.6` (Ubiquitous): THE SYSTEM SHALL complete a termination wait when every actor of the
  set has terminated.
- `AC-LIFE-016.7` (Ubiquitous): THE SYSTEM SHALL complete a termination wait for an actor that has
  already terminated and for a name nothing was ever spawned under.
Both waits arm their timeout through the registered clock and register it in the virtual schedule,
which is what makes them virtual on a stepped host — `AC-SCEN-012.*` states that seam's semantics and
this page adds nothing to it.

`AC-LIFE-016.4` and `AC-LIFE-016.5` are guards over the two ways a wait used to lie. A negative timeout
armed no clock at all and the caller's wait never returned — a hang, where a host's own real-time
backstops (`AC-CTRL-002.4`, `AC-CTRL-004.3`) exist to bound a *slow* answer rather than a missing one.
And a stray answer used to satisfy a silent actor's share, which is exactly the failure a lifecycle
wait exists to catch.

`AC-LIFE-016.7` is what lets a host discover the blocks to stop from the registry rather than from its
configuration (`AC-CTRL-004.5`) without risking a wait on a name that is already gone.

## The optional seams

- `AC-LIFE-017.1` (Ubiquitous): THE SYSTEM SHALL mint an actor reference from a name alone, whether or
  not an actor of that name exists, and SHALL warn on each message that reaches no actor.
- `AC-LIFE-017.2` (Ubiquitous): THE SYSTEM SHALL list the actors whose names match a caller's pattern.
- `AC-LIFE-017.3` (Conditional): WHERE a message observer, an activity monitor, a delayed-send gate, a
  virtual schedule, a clock or a vitals collector is registered THE SYSTEM SHALL use it, and SHALL
  otherwise behave as though the seam did not exist.
- `AC-LIFE-017.4` (Ubiquitous): THE SYSTEM SHALL combine every registered message observer into the one
  slot the pipeline notifies, using a lone observer as it is.

`AC-LIFE-017.1` is the shape a block's contract link takes: a handler that was never spawned is
addressed all the same, and every message to it becomes a dead letter with a warning and no error. The
finding ledger carries that a link-time registry lookup would be better and why it is not this page's
to make.

`AC-LIFE-017.3` is why the production runtime is unaffected by the development host's features: the
tap, the pause and the stepping are opt-in by registration, and a runtime that registers none of them
runs the same code paths it always did.

## Runtime vitals

- `AC-LIFE-018.1` (Ubiquitous): THE SYSTEM SHALL record every spawned actor's identity at its spawn and
  report that actor's vitals from then on, whether or not it has handled anything.
- `AC-LIFE-018.2` (Ubiquitous): THE SYSTEM SHALL count each handled message and each handler that threw
  against its actor, and accumulate the time its handlers spent.
- `AC-LIFE-018.3` (Ubiquitous): THE SYSTEM SHALL report an actor's mailbox depth as the messages posted
  to it less the messages taken off it, never below nothing.
- `AC-LIFE-018.4` (Ubiquitous): THE SYSTEM SHALL track the greatest handler duration, mailbox depth,
  timer callback duration and timer jitter over a recent window rather than over an actor's life, and
  SHALL report each as nothing once that window has passed with no further sample.
- `AC-LIFE-018.5` (Event-driven): WHEN the vitals core is given a window of nothing or less THE SYSTEM
  SHALL refuse it.
- `AC-LIFE-018.6` (Ubiquitous): THE SYSTEM SHALL report a timer's jitter as the size of the difference
  between its observed and its declared interval, whichever way it fell.
- `AC-LIFE-018.7` (Ubiquitous): THE SYSTEM SHALL report the instant of an actor's last handled message.
- `AC-LIFE-018.8` (Ubiquitous): THE SYSTEM SHALL keep counts and accumulated durations across windows.

The windowing of `AC-LIFE-018.4` is what makes a maximum actionable: a lifetime high-water mark on a
gateway that runs for months describes a minute nobody remembers. `AC-LIFE-018.5` refuses the window
that would report every one of them as nothing while the counts beside them kept rising.

`AC-LIFE-018.1` covers the runtime's own actors as well as logic blocks, because both are spawned
through the same seam — which is what lets an operator tell "a block is slow" from "the publisher is
saturated".

## The metrics the vitals are exported as

- `AC-LIFE-019.1` (Ubiquitous): THE SYSTEM SHALL publish the vitals as eight observable instruments on
  one named meter, each reading the core at the exporter's own tick rather than being pushed to.
- `AC-LIFE-019.2` (Ubiquitous): THE SYSTEM SHALL publish the handled and error counts as cumulative
  counters and the six remaining instruments as gauges, every duration in seconds.
- `AC-LIFE-019.3` (Ubiquitous): THE SYSTEM SHALL tag a logic block's measurements with its kind, its
  class, its instance name and its library, a runtime actor's with its kind and its role, and an actor
  whose identity was never recorded with its kind and its name.

The meter name and all eight instrument names are a wire: the runtime's export options name the meter,
and a dashboard template names each instrument. `AC-LIFE-019.3`'s full tag set is deliberate — the
runtime shapes cardinality by dropping tags at export, and a tag has to be emitted to be dropped.

Nothing in this repository constructs the meter; a host that adds the SDK gets the core and wires the
export itself. The finding ledger carries that.

## The two registrations

- `AC-LIFE-020.1` (Ubiquitous): THE SYSTEM SHALL register, for a host adding the SDK, a logger a block
  can take, the real clock unless the host registered one first, and one vitals core readable through
  its observer, collector and diagnostics faces.
- `AC-LIFE-020.2` (Ubiquitous): THE SYSTEM SHALL register, for a host adding the actor system, that
  system and the actor wrapper alone, taking every seam including the clock from whatever the host
  registered.
- `AC-LIFE-020.3` (Ubiquitous): THE SYSTEM SHALL have a host invoke every plugin's service registration before it constructs any logic block. GAP: both hosts in this repository belong to other areas — the development host's ordering is `AC-CTRL-001.3` and `AC-CTRL-002.1`, and the introspecting parser's is [`plugin-loading.md`](plugin-loading.md)'s — so this page states the rule a plugin author depends on without duplicating either host's proof.

`AC-LIFE-020.1`'s clock rule is the whole of stepped mode: a host registers a controllable clock first
and the SDK leaves it alone. `AC-LIFE-020.2` is why the two registrations are composed together — an
actor system without the SDK's registrations runs every timeout on wall time and cannot be stepped —
and why they are composed once, since a second registration of the vitals core would count every
message twice. Which assemblies a plugin's registration is discovered from is
[`plugin-loading.md`](plugin-loading.md)'s.
