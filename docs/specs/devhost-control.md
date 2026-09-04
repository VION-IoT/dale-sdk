---
trace: enforced
---

# The development host: control surface and lifecycle

What a running `Vion.Dale.DevHost` guarantees a caller — in process (`IDevHost`, `IDevHostControl`),
over HTTP and SignalR, and on standard output to the process that spawned it — and how a host is
built, booted, recycled into a new generation, switched between clock modes and torn down. Area code
`CTRL`. Process: [`../spec-process.md`](../spec-process.md).

The three faces are one contract: the HTTP routes are a projection of the in-process surface, the
push wire is a projection of the same observation stream, and the stdout receipts are what a process
that cannot call either has instead.

Cited rather than restated: [`scenarios.md`](scenarios.md) for scenario and topology files, stepping
semantics and contract pairing; [`config-gating.md`](config-gating.md) for instantiation parameters,
inclusion gates and the catalog projection; [`introspection.md`](introspection.md) for the member and
contract vocabulary an exported configuration carries; [`emission.md`](emission.md) for what the SDK
publishes — this page states what the host does with a publication. Block lifecycle inside the actor,
the CLI's option surface, the single-page application's internals and the smoke fixture are other
pages' or deliberately unspecified.

## Refusal shapes

Which shape a caller gets is itself the contract, because it decides what they can do about it.

- A **build-time** refusal throws before any actor exists.
- A **start-time** refusal throws `InvalidOperationException` or `TimeoutException`; nothing was
  bound, so disposing the host is sufficient teardown.
- A **per-call** refusal is a typed exception in process and a `400` or `409` carrying a stable
  `reason` token over HTTP.
- A **recorded failure** is a block the host could not bring up: the host still runs, and the failure
  is readable rather than only logged.

There is no fifth shape. A call that changes nothing and reports success is a defect, not a design —
every route that could answer that way was made to refuse instead.

## Building a host

- `AC-CTRL-001.1` (Event-driven): WHEN a development host is built without a configuration THE SYSTEM
  SHALL refuse to build it, naming the builder that produces one.
- `AC-CTRL-001.2` (Conditional): WHERE the caller registered no logger factory THE SYSTEM SHALL supply
  one, so a host builds and logs without being told how.
- `AC-CTRL-001.3` (Ubiquitous): THE SYSTEM SHALL enumerate as the block catalog every distinct logic
  block type the added plugin assemblies register, excluding every other registration, counting an
  assembly once however many of its registration types are named, and requiring no build.
- `AC-CTRL-001.4` (Ubiquitous): THE SYSTEM SHALL leave the builder unchanged by a catalog
  enumeration, so the same instance can be enumerated and then built.
- `AC-CTRL-001.5` (Ubiquitous): THE SYSTEM SHALL let a clock registered before the build win over the
  real clock the SDK registers.
- `AC-CTRL-001.6` (Ubiquitous): THE SYSTEM SHALL make the control surface resolvable as soon as the
  host is built, and SHALL introspect on demand when the configuration is read before the host starts.

The tap, pause and stepping are opt-in *by registration*, and only a development host registers
them — which is why the production runtime is unaffected by any of this. Each is stated by the
behaviour it enables (`AC-CTRL-008.9`, `AC-CTRL-011.1`, `AC-CTRL-012.1`) rather than by its
registration, because the registration has no other observable. `AC-CTRL-001.6` is what the
boot-dump-exit export path rests on — it never starts the host it exports.

## Starting a host

- `AC-CTRL-002.1` (Ubiquitous): THE SYSTEM SHALL introspect the wired network and construct the
  control surface before starting any hosted service.
- `AC-CTRL-002.2` (Event-driven): WHEN a topology names a block type with no dependency-injection
  registration THE SYSTEM SHALL refuse to start, listing every such block at once with the
  registration line to add.
- `AC-CTRL-002.3` (Event-driven): WHEN the logic system cannot be initialized THE SYSTEM SHALL fail
  the start carrying the initializer's message and its cause.
- `AC-CTRL-002.4` (Event-driven): WHEN not every block acknowledges start THE SYSTEM SHALL fail the
  start within a real-time budget no clock mode can stall.
- `AC-CTRL-002.5` (Event-driven): WHEN a host that is already started is started again THE SYSTEM
  SHALL refuse the second start.
- `AC-CTRL-002.6` (Event-driven): WHEN the configured port is already bound THE SYSTEM SHALL fail the
  start with a message naming the port.
- `AC-CTRL-002.7` (Ubiquitous): THE SYSTEM SHALL run a started host until its cancellation token fires,
  then stop it and return without throwing.

`AC-CTRL-002.4` is a real-time budget because the acknowledgement wait itself is virtual: on a stepped
host nothing advances the clock during a boot, so a block that never answers would leave a due-time
that never arrives. The block that does not answer is one whose start hook threw — which
`AC-CTRL-003.*` is how a caller finds out about.

## Health after a start

The actor middleware catches a handler's exception and carries on, so one bad block cannot take the
network down. The cost is that a block which failed to configure, to bind or to start leaves no trace
on its own state: its members read their defaults and the host reports itself started. These criteria
are where that failure is readable.

- `AC-CTRL-003.1` (Ubiquitous): THE SYSTEM SHALL record every handler exception a logic block's actor
  threw and the middleware caught, naming the block, the message it was handling and the error.
- `AC-CTRL-003.2` (Ubiquitous): THE SYSTEM SHALL report those failures oldest first, filtered to one
  block on request, and none at all for a block that came up.
- `AC-CTRL-003.3` (Ubiquitous): THE SYSTEM SHALL report the recorded failures on the control-status
  route as well as in process.
- `AC-CTRL-003.4` (Ubiquitous): THE SYSTEM SHALL bound the recorded failures, dropping the oldest.

`AC-CTRL-003.1` is deliberately every handler exception and not a list of message types: the three
ways a block fails to come up — a throw from its configuration, from its binding and from its start
hook — arrive on three different messages, and a fourth would arrive on a fourth. Read the list
accordingly: a non-empty one on a long-lived host means some handler has thrown since this generation
started, not that a block failed to come up. A reader that wants only the boot reads it right after the
start returns.

## Stopping and disposing

- `AC-CTRL-004.1` (Ubiquitous): THE SYSTEM SHALL run the domain stop over the logic blocks before
  stopping any hosted service, so the final values a block publishes from its stop hook reach the
  observation stream while the server is still up.
- `AC-CTRL-004.2` (Ubiquitous): THE SYSTEM SHALL run the domain stop as stop acknowledgement, then
  persistent-data snapshot, then a quiescence drain, then actor termination, in that order.
- `AC-CTRL-004.3` (Ubiquitous): THE SYSTEM SHALL bound the whole stop sequence by one real-time
  budget no clock mode can stall.
- `AC-CTRL-004.4` (Ubiquitous): THE SYSTEM SHALL downgrade every teardown failure to a warning,
  continue to the next step, and observe an abandoned step's later fault.
- `AC-CTRL-004.5` (Ubiquitous): THE SYSTEM SHALL discover the blocks to stop from the actor registry
  rather than from the configuration, so a host that never started stops without waiting on anything.
- `AC-CTRL-004.6` (Ubiquitous): THE SYSTEM SHALL invoke each block's stop hook on disposal as well as
  on an explicit stop, and SHALL do nothing on a second disposal.
- `AC-CTRL-004.7` (Ubiquitous): THE SYSTEM SHALL shut the actor system down and dispose the service
  provider it owns when the host is disposed.
- `AC-CTRL-004.8` (Ubiquitous): THE SYSTEM SHALL cancel an active scenario run on demand, and SHALL do
  so as the web host stops.

`AC-CTRL-004.2` exists for message-sequence parity with the runtime, which is the fidelity a
development host is for. Termination keeps a floor of its own even when `AC-CTRL-004.3`'s budget is
already spent: it is the step that actually releases the actors and their scopes, so a budget consumed
by a block that would not acknowledge must not skip it. `AC-CTRL-004.1`'s ordering is the inverse of the obvious one and is
load-bearing: stopping the server first loses exactly the values a reader most wants to see.

## The supervised recycle protocol

A supervisor rebuilds the host in place — dispose, rebuild, restart on the same port — which is the
kill-and-restart loop without the kill. A reset, a topology switch and a clock-mode switch are one
signal with a parked request beside it.

- `AC-CTRL-005.1` (Ubiquitous): THE SYSTEM SHALL build a fresh host, service provider, actor system
  and service-id set per generation.
- `AC-CTRL-005.2` (Ubiquitous): THE SYSTEM SHALL attach the supervisor's reset handler once per
  generation, SHALL refuse a second attach to the same generation, and SHALL detach on disposal only
  the handler the disposed token was issued for.
- `AC-CTRL-005.3` (Ubiquitous): THE SYSTEM SHALL keep the current topology selection across a plain
  reset and rebuild from the requested one across a switch.
- `AC-CTRL-005.4` (Ubiquitous): THE SYSTEM SHALL let the previous generation release the port before
  the next binds it, and SHALL announce each recycle on standard output naming the generation.
- `AC-CTRL-005.5` (Event-driven): WHEN a topology a switch selected cannot be built or cannot start
  THE SYSTEM SHALL report it on standard output and recycle back onto the topology that was running.
- `AC-CTRL-005.6` (Event-driven): WHEN a generation that nothing can be recycled back onto fails THE
  SYSTEM SHALL print a machine-readable failure receipt before the process ends.
- `AC-CTRL-005.7` (Event-driven): WHEN cancellation is requested during a generation THE SYSTEM SHALL
  stop that host and return.
- `AC-CTRL-005.8` (Ubiquitous): THE SYSTEM SHALL park a requested topology or clock mode for the
  supervisor to read when the reset fires, keep it readable for the rest of that generation, and accept
  any topology identifier in process — the route a client calls is where an unknown one is refused.
- `AC-CTRL-005.9` (Ubiquitous): THE SYSTEM SHALL report a host as resettable exactly while a
  supervisor's handler is attached, and SHALL refuse a reset, a topology switch and a clock-mode
  switch when none is.

`AC-CTRL-005.2`'s three clauses are one rule about ownership. Replacing an attached handler made the
host answer a recycle request with success while nothing recycled; a token that cleared whichever
handler was current let a stale subscription silently unsupervise a host somebody else owned.
`AC-CTRL-005.5` is why building the next generation is inside the guard and not before it: a topology
file that was deleted, one that no longer builds and one naming an unregistered block are three
spellings of the same operator mistake, and none of them may take away the interface the operator
needs to pick another topology. `AC-CTRL-005.6` is the readiness line's counterpart — without it a
spawning agent waits out its own timeout to learn the host is never coming.

## The process contract

What the host promises the process that spawned it. `dale dev`'s option surface is the command-line
tool's; the variables, the receipts and the handshake below are the host's, and a consumer's
`Program.cs` gets them by calling the runner.

- `AC-CTRL-006.1` (Ubiquitous): THE SYSTEM SHALL read every `DALE_DEVHOST_*` switch as enabled
  exactly when its value is the single character `1`, treating every other value, the empty string
  and an unset variable as disabled.
- `AC-CTRL-006.2` (Ubiquitous): THE SYSTEM SHALL open a browser when not headless and print a
  machine-readable readiness line when it is, in one shape whichever entry point is serving.
- `AC-CTRL-006.3` (Ubiquitous): THE SYSTEM SHALL open the browser once per process, and SHALL print the failure and the address and keep serving when it cannot. GAP: every automated boot is headless, so nothing in the repository opens a browser; the failure branch has no observable a test can reach without one.
- `AC-CTRL-006.4` (Event-driven): WHEN an export path is set THE SYSTEM SHALL boot, write that export,
  print a machine-readable receipt naming the file, and exit without serving — writing both exports
  when both are set.
- `AC-CTRL-006.5` (Event-driven): WHEN an export path does not name a file the process can write THE
  SYSTEM SHALL refuse it before booting, naming the variable.
- `AC-CTRL-006.6` (State-driven): WHILE an export is in progress THE SYSTEM SHALL print no readiness
  line and open no browser.
- `AC-CTRL-006.7` (Ubiquitous): THE SYSTEM SHALL stop an exporting host exactly once.
- `AC-CTRL-006.8` (Ubiquitous): THE SYSTEM SHALL print the web address and one deep link per
  discovered scenario before the readiness line, marking a scenario that could not be parsed rather
  than omitting it.
- `AC-CTRL-006.9` (Ubiquitous): THE SYSTEM SHALL emit every stdout receipt as one line of valid JSON,
  so a path with backslashes survives it.

`AC-CTRL-006.1` is one spelling across every switch because a skill, a continuous-integration job and
the command-line tool have to agree without consulting each other: `true`, `TRUE` and `yes` are all
off. `AC-CTRL-006.2`'s "one shape" is the half that used to differ — a parser written against one
entry point read a field the other did not print. Completion of an export is observed by the target
file appearing, not by its receipt: a `Program.cs` that predates the runner ignores the variables
entirely and would otherwise be waited on forever.

## Folder-driven boot

- `AC-CTRL-007.1` (Ubiquitous): THE SYSTEM SHALL resolve the topology directory once at boot by the
  same rule the topology store uses.
- `AC-CTRL-007.2` (Ubiquitous): THE SYSTEM SHALL boot the topology named `default` when one exists
  and otherwise the first identifier in case-insensitive ordinal order.
- `AC-CTRL-007.3` (Event-driven): WHEN no topology file exists THE SYSTEM SHALL generate one,
  announce its path, and boot it — leaving an existing file untouched.
- `AC-CTRL-007.4` (Ubiquitous): THE SYSTEM SHALL own topology loading in the folder-driven entry
  point, so a consumer's configuration callback supplies only registrations.
- `AC-CTRL-007.5` (Ubiquitous): THE SYSTEM SHALL use an explicitly configured data directory
  verbatim, resolved to an absolute path.
- `AC-CTRL-007.6` (Ubiquitous): THE SYSTEM SHALL otherwise prefer a data directory in the starting
  directory and then search its nearest ancestors, bounded by the repository marker and by a fixed
  depth.
- `AC-CTRL-007.7` (Event-driven): WHEN no data directory is found THE SYSTEM SHALL name the one in
  the starting directory, whether or not it exists.

`AC-CTRL-007.6`'s bound is the repository marker and deliberately not a solution file: nested
per-project solutions below the data directory are a real layout. The ancestor search exists because
an integrated development environment launches with the working directory set to the build output,
where the plain convention finds nothing.

## Reading the network

- `AC-CTRL-008.1` (Ubiquitous): THE SYSTEM SHALL list the wired blocks as their topology identifier,
  instance name, block type name and service identifiers.
- `AC-CTRL-008.2` (Ubiquitous): THE SYSTEM SHALL introspect exactly once however many callers ask,
  safely under concurrent requests.
- `AC-CTRL-008.3` (Ubiquitous): THE SYSTEM SHALL address a block by its instance name or its topology
  identifier wherever it addresses a block at all.
- `AC-CTRL-008.4` (Ubiquitous): THE SYSTEM SHALL read a member's last published value by its bare
  name, by a dotted service-and-member path, or by service identifier and member name.
- `AC-CTRL-008.5` (Event-driven): WHEN a member name is carried by both a block's own service and a
  nested component THE SYSTEM SHALL resolve the bare name to the block's own service, leaving the
  qualified forms to reach the other.
- `AC-CTRL-008.6` (Ubiquitous): THE SYSTEM SHALL report no value in process for an unknown block,
  service or member, and every known member of a block keyed by its bare name.
- `AC-CTRL-008.7` (Ubiquitous): THE SYSTEM SHALL take every value it reports from the published
  emission stream rather than from the block.
- `AC-CTRL-008.8` (Ubiquitous): THE SYSTEM SHALL report the registered clock's current instant, which
  is the real clock on a host that is not stepped.
- `AC-CTRL-008.9` (Ubiquitous): THE SYSTEM SHALL report the messages the tap captured, unfiltered or
  filtered to one block, and SHALL bound them, dropping the oldest.
- `AC-CTRL-008.10` (Ubiquitous): THE SYSTEM SHALL report the most recent captured log lines oldest
  first with their level, category, timestamp, message and exception text, nothing for a non-positive
  count, and no more than its scrollback holds.
- `AC-CTRL-008.11` (Ubiquitous): THE SYSTEM SHALL capture log lines at every level except none,
  alongside the console rather than in place of it, and SHALL let no subscriber's failure break
  logging or another subscriber.
- `AC-CTRL-008.12` (Ubiquitous): THE SYSTEM SHALL keep a service-provider output's captured value for
  one host generation.

`AC-CTRL-008.5` is not a tie-break, it is the rule: standard telemetry names are shared between a
block's own surface and its nested components, and resolving the bare name last-service-wins made it
unusable. `AC-CTRL-008.7` has a consequence worth stating — a value emission policy throttled away is
not readable here either; what the host caches is what was published ([`emission.md`](emission.md)).

## Writing and driving

- `AC-CTRL-009.1` (Event-driven): WHEN a write names an unknown service, an unknown member or a
  read-only member THE SYSTEM SHALL refuse it before sending anything, carrying a stable reason token
  and the offending member name.
- `AC-CTRL-009.2` (Ubiquitous): THE SYSTEM SHALL complete a write when the block acknowledges it, so
  a read immediately afterwards reflects the new value, and SHALL acknowledge a write that changed
  nothing.
- `AC-CTRL-009.3` (Ubiquitous): THE SYSTEM SHALL decode a JSON write value against the member's
  schema, pass a runtime value through unchanged, and accept a duration in either its ISO-8601 or its
  .NET spelling.
- `AC-CTRL-009.4` (Event-driven): WHEN a drive names a handler no stand-in was created under, or an
  endpoint the wired network does not carry, THE SYSTEM SHALL refuse it before sending anything,
  carrying a stable reason token and the addressed endpoint.
- `AC-CTRL-009.5` (Ubiquitous): THE SYSTEM SHALL complete a drive before the block has seen the value.
- `AC-CTRL-009.6` (Ubiquitous): THE SYSTEM SHALL ask every stand-in the host created, and the property
  and measuring-point handlers, to re-publish both the last driven and the last written value of every
  contract they serve.
- `AC-CTRL-009.7` (Event-driven): WHEN a block does not acknowledge a write within the acknowledgement
  window THE SYSTEM SHALL refuse the write naming the window, and SHALL answer the route with a client
  error carrying that reason.

`AC-CTRL-009.1` and `AC-CTRL-009.4` are the same rule on the two write paths, and both exist because
the alternative was observed: a rejection swallowed inside the actor, an acknowledgement that never
came, and a caller told the poke took. `AC-CTRL-009.7` closes the third door into the same room: the
two refusals above are what the control surface can see before it sends, and silence is what is left
over. A run's hollow-acknowledgement detection reads that silence for itself and says more about it
(`AC-SCEN-009.10`), so it consumes the refusal rather than reporting it. `AC-CTRL-009.5` is what makes
a drive deterministic under stepping — the barrier is the synchronisation, not an acknowledgement.

## The observation stream

- `AC-CTRL-010.1` (Ubiquitous): THE SYSTEM SHALL surface a service-property change, a write
  acknowledgement, a measuring-point change and a service-provider contract change on one subscribable
  stream, each naming the originating block and falling back to the raw service identifier when it
  cannot be resolved.
- `AC-CTRL-010.2` (Ubiquitous): THE SYSTEM SHALL detach a subscriber when its token is disposed,
  tolerate a second disposal, and let no subscriber's or selector's failure break the fan-out.
- `AC-CTRL-010.3` (Ubiquitous): THE SYSTEM SHALL observe for a wait only events raised after the wait
  was registered, and SHALL resolve it with no value on timeout and on cancellation.
- `AC-CTRL-010.4` (Ubiquitous): THE SYSTEM SHALL treat a zero wait timeout as observing nothing and
  SHALL refuse a negative one that is not the infinite sentinel.
- `AC-CTRL-010.5` (Ubiquitous): THE SYSTEM SHALL refuse a null sink or selector.
- `AC-CTRL-010.6` (Ubiquitous): THE SYSTEM SHALL surface the write acknowledgement in process only,
  and SHALL update its value cache from it as well as from a change.

`AC-CTRL-010.3`'s first clause is why a caller registers a wait *before* the stimulus: a wait that saw
earlier events could never say what it had waited for. `AC-CTRL-010.6` keeps the acknowledgement off
the general observation contract because it is a correlation signal for one write, not a description
of the network.

## Run control

- `AC-CTRL-011.1` (State-driven): WHILE the host is paused THE SYSTEM SHALL hold every new delayed
  self-send, still deliver a fire already scheduled, and keep processing messages.
- `AC-CTRL-011.2` (Ubiquitous): THE SYSTEM SHALL replay every held schedule in the order it was held
  with its original delay, and SHALL treat a pause while paused and a resume while running as having
  no effect.

Pause holds delayed sends and nothing else — the world stands still and stays pokeable — so each
timer may tick at most once more after it, and a block computing from the current time observes the
gap. That is why it is meaningful on a real-clock host, where stepping is not.

## Stepping entry points

Stepping semantics are [`scenarios.md`](scenarios.md)'s (`AC-SCEN-012.*`). What this page owns is the
host's side: when the engine is built, from what, and with which budget.

- `AC-CTRL-012.1` (Ubiquitous): THE SYSTEM SHALL build the stepping engine on first use and reuse it
  for the rest of the generation, so a host on the real clock is refused only when stepping is
  actually requested.

The barrier that engine waits on reads the host's own mailbox statistics and its actor-activity
monitor; the predicate over them is `AC-SCEN-012.5`'s.

## Safety budgets

Four real-time backstops bound waits that no clock mode can complete. They are budgets, not
tolerances: the normal path completes in milliseconds and never approaches one, and they exist so a
stuck host surfaces as a named failure instead of a hang.

- `AC-CTRL-013.1` (Ubiquitous): THE SYSTEM SHALL give a caller one place to set the write
  acknowledgement window, the start acknowledgement backstop, the stop-sequence backstop and the
  quiescence ceiling, and SHALL bound each of those four waits by the value set there.
- `AC-CTRL-013.2` (Ubiquitous): THE SYSTEM SHALL refuse a budget that is not a positive span.
- `AC-CTRL-013.3` (Ubiquitous): THE SYSTEM SHALL take a scenario run's hollow-acknowledgement
  detection from the host's refusal of an unacknowledged write rather than from a measurement of the
  runner's own.

`AC-CTRL-013.3` is what made `AC-SCEN-009.10` provable. The run and the host each held a number, and
a host built with any other window either fired the detection on every write or on none; no test
could reach the failure without waiting out five real seconds per case. There is one number now, and
the runner reads the refusal it produces instead of racing it with a stopwatch.

## The HTTP host

- `AC-CTRL-014.1` (Ubiquitous): THE SYSTEM SHALL bind the configured port on loopback only.
- `AC-CTRL-014.2` (Ubiquitous): THE SYSTEM SHALL serve reads to any caller and SHALL refuse a
  state-changing request whose host header is not loopback, or whose declared origin is not, accepting
  one that declares no origin.
- `AC-CTRL-014.3` (Event-driven): WHEN a request names a route the host does not serve THE SYSTEM
  SHALL answer not-found rather than the single-page application.
- `AC-CTRL-014.4` (Ubiquitous): THE SYSTEM SHALL serve the single-page application from resources
  embedded in the assembly, with revalidation forced on every file.
- `AC-CTRL-014.5` (Ubiquitous): THE SYSTEM SHALL emit a duration as an ISO-8601 duration and an enum
  as its member name on both the request-response and the push wire.

`AC-CTRL-014.2` is the local-tool posture: the server binds loopback, but a hostile page in the
developer's own browser can still fire cross-origin requests at it, and cross-origin resource sharing
does not prevent a send. Reads stay open because a notebook, a shell client and a dashboard tab are
all legitimate readers. `AC-CTRL-014.4` matters because the no-build discipline rules out
content-hashed file names, so every asset lives at a stable address and a package upgrade would
otherwise serve the old interface until a hard reload.

## The route table

- `AC-CTRL-015.1` (Ubiquitous): THE SYSTEM SHALL serve the wired network's full introspection, its
  lightweight block list, every last-known member value of one block, one member's value, the recent
  log lines, the run-control state, the captured messages and the block definitions, each on its own
  route.
- `AC-CTRL-015.2` (Event-driven): WHEN a state read names a block or a member the host does not carry
  THE SYSTEM SHALL answer not-found, while the in-process read reports no value.
- `AC-CTRL-015.3` (Ubiquitous): THE SYSTEM SHALL report the run-control state as paused, resettable,
  stepped, the virtual clock, whether a run is active, and the block failures the host recorded.
- `AC-CTRL-015.4` (Ubiquitous): THE SYSTEM SHALL advance the virtual clock to the next scheduled event
  on one route and by a given number of seconds on another, answering with the clock's new instant.
- `AC-CTRL-015.5` (Event-driven): WHEN a manual advance is not a positive finite number of seconds a
  real clock can wait THE SYSTEM SHALL refuse it naming the bound.
- `AC-CTRL-015.6` (Ubiquitous): THE SYSTEM SHALL pause and resume time-driven activity on their own
  routes in either clock mode, answering with the resulting paused state.
- `AC-CTRL-015.7` (Ubiquitous): THE SYSTEM SHALL accept a recycle and a clock-mode switch on their own
  routes, answering that a recycle is under way.

`AC-CTRL-015.2` is deliberately asymmetric: the in-process read is a poll whose caller has the
configuration in hand, while a route's caller has only a status code — and an unknown block answering
`200 {}` is indistinguishable from a block that has published nothing. `AC-CTRL-015.5` reuses the
bound a scenario's durations carry (`AC-SCEN-003.2`), because a manual advance is the same clock.

## Refusals and their tokens

A refusal a client is expected to act on carries a machine-readable token beside its prose, so a tool
never has to match a message. The scenario and topology stores' own refusals do not carry one yet
([`_findings.md`](_findings.md)).

- `AC-CTRL-016.1` (Ubiquitous): THE SYSTEM SHALL carry a stable reason token on every conflict it
  answers with, and on every write, drive and manual advance the control surface itself refuses.
- `AC-CTRL-016.2` (Ubiquitous): THE SYSTEM SHALL name a refused write's reason as unknown service,
  unknown member, read-only or unacknowledged, and carry the offending member.
- `AC-CTRL-016.3` (Ubiquitous): THE SYSTEM SHALL name a refused drive's reason as unknown handler or
  unknown contract, and carry the addressed endpoint.
- `AC-CTRL-016.4` (Ubiquitous): THE SYSTEM SHALL refuse manual stepping when the host is not stepped
  and when a scenario run is driving the clock, naming which.
- `AC-CTRL-016.5` (Ubiquitous): THE SYSTEM SHALL refuse a reset and a clock-mode switch on an
  unsupervised host, and a topology switch on any host whose supervisor does not rebuild from the
  requested topology, naming that as the reason.

`AC-CTRL-016.4`'s two refusals exist because one virtual clock has two drivers and letting both run
would race them on the shared schedule. The token is what lets a client disable the right control for
the right reason rather than showing one message for both.

## Scenario and topology routes

The files, their validation and what a run means are [`scenarios.md`](scenarios.md)'s. What this page
owns is the routes that serve them, the status each refusal answers with, and the recycle round trip a
caller performs.

- `AC-CTRL-017.1` (Ubiquitous): THE SYSTEM SHALL serve the discovered scenarios and topologies with
  the directory they came from and whether saving is disabled, and the topologies with the running
  topology and whether switching is possible.
- `AC-CTRL-017.2` (Ubiquitous): THE SYSTEM SHALL serve a scenario or topology file byte for byte, and
  each generic schema from the resources embedded in the host, answering not-found for an identifier
  the store does not carry.
- `AC-CTRL-017.3` (Ubiquitous): THE SYSTEM SHALL answer a save with the file written, a forbidden
  status when saving is disabled, and the collected errors when the content is refused.
- `AC-CTRL-017.4` (Ubiquitous): THE SYSTEM SHALL serve the latest run report for a scenario, answering
  not-found when that scenario has not run in this host generation.
- `AC-CTRL-017.5` (Ubiquitous): THE SYSTEM SHALL keep the latest report per scenario for one host
  generation, publish a run's pending report before answering the request that started it, and give
  every run a fresh identity.
- `AC-CTRL-017.6` (Ubiquitous): THE SYSTEM SHALL serve a consistent snapshot of a run report rather
  than the instance the runner is mutating, and SHALL report a run as active exactly while its task
  has not completed.
- `AC-CTRL-017.7` (Event-driven): WHEN a scenario is applied to a host on another topology, or to a
  stepped generation whose clock has advanced or in which any scenario has already run, THE SYSTEM
  SHALL recycle onto the scenario's topology and answer that a recycle is under way.
- `AC-CTRL-017.8` (Event-driven): WHEN a scenario is applied to a host on the wrong topology that
  nothing can rebuild onto it THE SYSTEM SHALL refuse the run naming both topologies, and SHALL run in
  place when only the clock is dirty.
- `AC-CTRL-017.9` (Ubiquitous): THE SYSTEM SHALL refuse a second run while one is active, naming the
  active run and its scenario, and SHALL cancel the active run first when the caller asks for a
  restart.

`AC-CTRL-017.7` is the host's half of `AC-SCEN-012.10`'s clean slate, and "any scenario" is the
load-bearing word: a generation another scenario has already driven still holds everything that run
wrote. The caller's obligation is the round trip — re-apply until the host answers with a run
identity — and every client of this API performs it rather than assuming the first call took effect.

## The push wire

- `AC-CTRL-018.1` (Ubiquitous): THE SYSTEM SHALL push a service-property change, a measuring-point
  change and a service-provider contract change to every connected client, each under its own stable
  event name and keyed by the identifiers the configuration carries.
- `AC-CTRL-018.2` (Event-driven): WHEN a client connects THE SYSTEM SHALL ask every stand-in and mock
  handler to re-publish, so the new client sees current state without polling.
- `AC-CTRL-018.3` (Ubiquitous): THE SYSTEM SHALL log and swallow a failed broadcast rather than let it reach the block that published. GAP: nothing in the repository can make a live hub's broadcast fail, and the swallow is what keeps a publishing block's thread from carrying a transport error.

## The exported configuration

One shape serves the configuration route and both export modes. What each field *means* is
[`introspection.md`](introspection.md)'s and [`config-gating.md`](config-gating.md)'s; the envelope is
this page's.

- `AC-CTRL-019.1` (Ubiquitous): THE SYSTEM SHALL export the wired network as the topology's name, its
  blocks, its service providers, its interface mappings and its contract pairings.
- `AC-CTRL-019.2` (Ubiquitous): THE SYSTEM SHALL export the live view of each block rather than the
  definition view, resolved for the instantiation-parameter values that instance was configured with.
- `AC-CTRL-019.3` (Ubiquitous): THE SYSTEM SHALL derive a service identifier from the block's topology
  identifier and the service's identifier, unique across every block and service provider of one host,
  so two runs of one wired configuration export identically.
- `AC-CTRL-019.4` (Ubiquitous): THE SYSTEM SHALL write an export as indented camel-case JSON, the same
  shape the configuration route serves.

`AC-CTRL-019.3` replaced freshly minted identifiers, which made every export differ from the last. The
identifiers are process-local by design — a topology file carries names, not them — so a client that
persists anything keys on name paths instead.

## The block catalog

- `AC-CTRL-020.1` (Ubiquitous): THE SYSTEM SHALL compute the block catalog when it is first resolved
  rather than when the web interface is added, so blocks registered afterwards are included.
- `AC-CTRL-020.2` (Ubiquitous): THE SYSTEM SHALL instantiate each catalog block once to read the
  defaults its initializers set, dispose it, and list a block whose construction needs more than a
  logger without those defaults.
- `AC-CTRL-020.3` (Ubiquitous): THE SYSTEM SHALL publish a parameter's declared bound in its editor
  schema only where that bound can be carried there.
- `AC-CTRL-020.4` (Conditional): WHERE the caller does not say THE SYSTEM SHALL boot the web interface
  stepped exactly when the process environment asks for it.

`AC-CTRL-020.3` is `AC-INTRO-007.3`'s rule at a second site, and its second half is what that one does
not need: the editor schema's integer cannot carry every finite double, so a bound outside its range
is omitted rather than saturated into a limit the author never wrote (`AC-GATE-010.6`).
