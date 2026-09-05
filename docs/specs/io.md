---
trace: enforced
---

# I/O: the four faces a block binds, the four a simulator binds, and what carries them

What the SDK guarantees a logic-block author who reads an input or drives an output. Two packages —
`Vion.Dale.Sdk.DigitalIo` and `Vion.Dale.Sdk.AnalogIo` — ship one design twice, differing only in
the value each carries: a truth value on a digital face, a real number on an analog one. Each
package holds four contract faces (the two a block binds and the two a simulator binds on the far
side), four handlers that carry them, and a registration class. Area code `IO`. Process:
[`../spec-process.md`](../spec-process.md).

The spine is the order an author meets the machinery: the four faces and what each carries, the four
round-trips between them, what a face does when the configuration mapped nothing to it, the handler
on the wire (registration, decode, publish), the value contract, the multiplicity and development-only
declarations, dependency injection and the published surface, the mirror, and the test discipline.

Cited rather than restated: [`contracts.md`](contracts.md) for the binding attributes, for what
`LogicBlockContractBase` does with a write on an unmapped or unlinked contract, and for the five arms
of `ServiceProviderHandlerBase` (`AC-BIND-009.*`, `014.*`, `015.*`, `016.*`);
[`plugin-loading.md`](plugin-loading.md) for what `[assembly: DaleSharedAssembly]` does to a type's
identity (`AC-PLUG-005.*`); [`introspection.md`](introspection.md) for the development-only exclusion
(`AC-INTRO-002.6`) and for what a binding's multiplicity, consumer limit and handler-actor name reach
the document as (`AC-INTRO-015.6`); [`scenarios.md`](scenarios.md) for how the development host pairs
a consumer face to a provider face (`AC-SCEN-014.*`); [`analyzers.md`](analyzers.md) for the standing
proof that these two packages' declarations are judged by the Dale analyzers (`AC-ANLZ-018.4`);
[`block-lifecycle.md`](block-lifecycle.md) for what an actor does with a handler that throws
(`AC-LIFE-014.2`). Architecture decisions
[`0014`](../../architecture/decisions/0014-third-party-contract-types-and-sp-deployment.md) (these
contract types are a VION packaging convenience, not a surface third parties must mirror),
[`0021`](../../architecture/decisions/0021-contract-link-multiplicity.md) (an output accepts one
consumer, an input any number) and
[`0139`](../../architecture/decisions/0139-service-provider-simulation-tiers.md) (a provider face is
the logic-level simulation tier, and carries both development-only declarations) are the design
authority for the declarations below and are cited, never re-argued.

`Vion.Contracts` owns the MQTT topic constants and the FlatBuffer payload schemas this area writes
and reads. They are carried, not specified here; a rename in that package is a wire change with its
own readers.

## The four faces

A face is what a logic block declares a property of and binds. Each carries exactly the members its
direction needs and nothing else — there is no read-back on an input, no confirmation to send from a
consumer, and no state to poll anywhere.

| Face | The block calls | The block observes |
|---|---|---|
| `IDigitalInput` / `IAnalogInput` | — | `InputChanged` |
| `IDigitalOutput` / `IAnalogOutput` | `Set(value)` | `OutputChanged` |
| `IDigitalInputProvider` / `IAnalogInputProvider` | `Drive(value)` | — |
| `IDigitalOutputProvider` / `IAnalogOutputProvider` | `Confirm(value)` | `SetReceived` |

The right-hand pair is the **provider** side: a simulator binds it to stand in for equipment that is
not there, and it is the inverse of the consumer face beside it.
[`../simulator-authoring.md`](../simulator-authoring.md) is the recipe; the shipped pair is the
worked example it points at.

- `AC-IO-001.1` (Ubiquitous): THE SYSTEM SHALL give each face exactly the members its direction
  needs — an input face one change event and no operation, an output face one command and one change
  event, an input provider face one operation and no event, and an output provider face one command
  event and one confirmation.
- `AC-IO-001.2` (Ubiquitous): THE SYSTEM SHALL carry a pair's two directions on one set of message
  types, a provider face reusing its consumer face's rather than declaring its own.
- `AC-IO-001.3` (Ubiquitous): THE SYSTEM SHALL name each face's contract type on the face itself, as
  the identifier a platform matches a binding to its handler through.

`AC-IO-001.2` is what makes a pair bridgeable at all: the development host re-delivers one side's
captured outbound as the other side's declared inbound and transforms nothing, so a copy of a message
type on either side would leave the two sides unpairable
([`scenarios.md`](scenarios.md), `AC-SCEN-014.5`).

`AC-IO-001.3`'s eight names — `DigitalInput`, `DigitalOutput`, `DigitalInputProvider`,
`DigitalOutputProvider` and the analog four — are stable identifiers, not labels.
[`../identifier-stability.md`](../identifier-stability.md) is the practical companion; renaming one
is a platform-visible break in the same class as renaming a service member.

## The four round-trips

Everything a block or a simulator observes on a face arrives as a message its handler declares. There
are four such trips, and they are the whole of the family's behaviour.

- `AC-IO-002.1` (Event-driven): WHEN a block sets an output THE SYSTEM SHALL raise the paired output
  provider's command event carrying that value.
- `AC-IO-002.2` (Event-driven): WHEN an output provider confirms a value THE SYSTEM SHALL raise the
  paired output's change event carrying that value.
- `AC-IO-002.3` (Event-driven): WHEN an input provider drives a value THE SYSTEM SHALL raise the
  paired input's change event carrying that value.
- `AC-IO-002.4` (Event-driven): WHEN a state message arrives for a mapped contract THE SYSTEM SHALL
  raise the bound face's change event carrying the value the message held.
- `AC-IO-002.5` (Ubiquitous): THE SYSTEM SHALL carry on a confirmation the value the far side reports
  it applied, which need not be the value commanded.
- `AC-IO-002.6` (Ubiquitous): THE SYSTEM SHALL leave an output's change event silent until the far
  side confirms, offering no timeout and no default.
- `AC-IO-002.7` (Ubiquitous): THE SYSTEM SHALL raise a face's event on every message it receives,
  including one carrying the value already reported.

`AC-IO-002.4` and `AC-IO-002.2` are the same event reached two ways. Off a bench the provider face
raises it; on a device the state topic does. A block cannot tell them apart, which is what makes a
paired topology a faithful rehearsal of production.

`AC-IO-002.5` and `AC-IO-002.6` are what a confirmation is *for*. A simulator that applies a command
and reports its inverse is modelling an echo fault; one that applies it and answers nothing is
modelling hardware that does not report. Both are legitimate, so neither the SDK nor the block may
assume a command took effect: a block that needs to know compares its own last command against the
confirmation, and a block that needs a deadline runs its own timer.

`AC-IO-002.7` is the difference between a face and a service property. A property's emission is
throttled and deadbanded ([`emission.md`](emission.md)); a face's event is not, so a value repeated on
the wire is raised again. A block that wants edges compares for itself — which is what a simulator
driving an input on a timer must do, or a paired loop never quiesces
([`../devhost-conventions.md`](../devhost-conventions.md)).

## What a face does when nothing is mapped to it

A block always holds every face it declares, whether or not the configuration mapped one to a service
provider. What differs is what a write does.

- `AC-IO-003.1` (Ubiquitous): THE SYSTEM SHALL address a face's messages to the handler actor named
  on the contract, by that handler's class name.
- `AC-IO-003.2` (Event-driven): WHEN a block writes on a face the configuration mapped nothing to THE
  SYSTEM SHALL drop the write, leaving the block nothing to observe.
- `AC-IO-003.3` (Ubiquitous): THE SYSTEM SHALL ignore a contract message a face does not handle,
  raising nothing.

`AC-IO-003.2` is the sharpest edge on this page for a block author, and it is silent by design: the
drop is `LogicBlockContractBase`'s (`contracts.md`, `AC-BIND-009.*`), the warning that a contract went
unmapped is issued once at start-up, and after that a `Set` on an unwired output is indistinguishable
from a `Set` on a wired one. **There is no way to ask.** A block whose own diagnostics depend on
knowing — a heat pump that must report an unwired contact rather than pretend — has no supported
answer today; the ledger carries what it would take
([`_findings.md`](_findings.md)). A face that *is* mapped but not yet linked is the other case and is
not silent: it refuses the write, naming the contract and when writing becomes legal (`AC-BIND-009.*`).

`AC-IO-003.3` holds on both sides of the wire: an input face given an output's message raises nothing,
and an input's handler given any contract message publishes nothing, because an input carries no
command in either direction.

## The handler on the wire

Each hardware face has a handler actor that carries it between the block and MQTT. The provider faces
have handlers too, but only as declarations — see *Development surface* below.

### Registration

- `AC-IO-004.1` (Ubiquitous): THE SYSTEM SHALL subscribe each hardware face's handler to that
  family's state topic under the service-provider wildcard prefix, and SHALL claim that family's
  topic prefix as its routing key.
- `AC-IO-004.2` (Event-driven): WHEN a registration request arrives THE SYSTEM SHALL derive and send
  its registration afresh and answer the request, however many have come before.
- `AC-IO-004.3` (Ubiquitous): THE SYSTEM SHALL have a provider face's handler subscribe to no topic
  and claim an empty routing key, while still answering a registration request.

An output's handler subscribes the **state** topic and publishes to the **set** topic; it never
subscribes what it publishes. `AC-IO-004.2` is the re-subscribe path: a handler holds no registration
state, so a runtime re-issuing the request after a broker reconnect gets a fresh registration and
nothing goes stale. `AC-IO-004.3` is why the development-only marking is load-bearing rather than
cosmetic — the empty routing key a provider handler would claim poisons a routing table matched by
prefix (`contracts.md`, `AC-BIND-015.2`).

### Decode

- `AC-IO-005.1` (Event-driven): WHEN a state message arrives THE SYSTEM SHALL deliver its value to
  every logic-block contract mapped to the service-provider contract the topic names.
- `AC-IO-005.2` (Event-driven): WHEN a state message's payload is not one the schema accepts THE
  SYSTEM SHALL drop the message, delivering nothing to any block.
- `AC-IO-005.3` (Ubiquitous): THE SYSTEM SHALL read a state message's value and no other field of its
  payload, taking the contract identity from the topic.
- `AC-IO-005.4` (Event-driven): WHEN a state message names a service-provider contract no logic-block
  contract is mapped to THE SYSTEM SHALL drop it, and SHALL replay nothing when a mapping later
  arrives.

`AC-IO-005.2` is what the schema's own check buys and what it does not. It refuses an empty payload,
a payload truncated anywhere but its last byte, and a payload of a *narrower* value type than the
topic carries. It does **not** refuse a payload of a wider value type — the two layouts agree, so an
analog payload read on a digital topic yields a value nothing sent — and it does not refuse trailing
bytes past a complete message. Distinguishing the remaining case needs the payload's schema label,
which every publisher on this wire already sets and this area's receiver cannot yet read;
[`_findings.md`](_findings.md) carries what that needs. Before the check existed, an empty payload
threw out of the handler (contained by `AC-LIFE-014.2`, so the message was dropped and the actor
survived) and a truncated one delivered a fabricated value to every mapped block — which is why the
guard is worth its line.

`AC-IO-005.3` is why the identity strings a state payload carries are the publisher's own bookkeeping:
the topic is the identity, and this area never reads them. `AC-IO-005.4` fixes a block's first value:
this area issues no read of its own, so a block sees the next state message after its contract is
linked, and one that arrives before is dropped. Both hardware abstraction layers publish state
retained, which is what makes that first value arrive at all.

### Publish

- `AC-IO-006.1` (Event-driven): WHEN a block sets an output THE SYSTEM SHALL publish the command to
  that output's set topic under the service-provider contract's identity, naming a response topic
  under the runtime's own identifier.
- `AC-IO-006.2` (Ubiquitous): THE SYSTEM SHALL publish each command under a correlation identifier of
  its own, labelled with its payload type's schema name and the FlatBuffer content type, and not
  retained.
- `AC-IO-006.3` (Ubiquitous): THE SYSTEM SHALL publish one command for each service-provider contract
  the block's output is mapped to.
- `AC-IO-006.4` (Event-driven): WHEN a block sets an output mapped to no service-provider contract
  THE SYSTEM SHALL drop the command, leaving the block nothing to observe.
- `AC-IO-006.5` (Ubiquitous): THE SYSTEM SHALL build a command's topic once per service-provider
  contract and keep it for the life of the handler, so the topic a contract is commanded on is the
  one the installation topic yielded when that contract was first commanded.

`AC-IO-006.2`'s schema label is not decoration: the far side dispatches on it. That this area sets one
on every command and reads none on any message is the asymmetry `AC-IO-005.2` is bounded by.

`AC-IO-006.1`'s response topic is where a service provider answers a command — including where it
answers that the command **failed**. Nothing in the runtime subscribes it. So a set that the far side
refused is invisible to the block, and the only evidence a command took effect is the retained state
that follows a successful one, arriving as `AC-IO-002.4`. Subscribing it is a new wire behaviour and
is carried in [`_findings.md`](_findings.md).

`AC-IO-006.5` states both halves of the cache deliberately. A handler is resolved per actor, so its
cache lives as long as that actor and holds an entry for every contract it has ever commanded — the
map being replaced does not release one. The growth is bounded by the configuration and is not the
interesting half; the interesting half is that the installation topic is captured at first publish, so
changing it afterwards leaves every cached topic addressed to the old one.

## The value

- `AC-IO-007.1` (Ubiquitous): THE SYSTEM SHALL carry one bare value on each message of a face — a
  truth value on a digital face, a real number on an analog one — with no unit, range, scale,
  deadband, timestamp or quality alongside it.
- `AC-IO-007.2` (Ubiquitous): THE SYSTEM SHALL carry any value its type can hold to the wire
  unaltered, a non-number and both infinities included, rejecting and clamping none of them.

`AC-IO-007.1` is a contract, not an omission. Units, ranges and presentation belong to the block's own
service properties ([`emission.md`](emission.md)); scaling and engineering conversion belong to the
block. Nothing here interprets a value, which is why the same two faces serve a contactor, a
0–10 V setpoint and a percentage without knowing which it is carrying.

`AC-IO-007.2` has one consequence worth stating once so nobody re-derives it: the serialiser omits a
field equal to the schema's default, and `-0.0` compares equal to `0.0`, so **a signed zero does not
survive the wire** and neither does the difference between a `false` command and an absent field.
Both read back as the default. Validating a non-finite value is not this area's — the reader is the
hardware abstraction layer, and analog I/O is served by the simulating layer alone today.

## Multiplicity and development surface

- `AC-IO-008.1` (Ubiquitous): THE SYSTEM SHALL declare an output face and every provider face to
  accept at most one consumer, and SHALL leave an input face unconstrained.
- `AC-IO-008.2` (Ubiquitous): THE SYSTEM SHALL declare every provider face development surface on the
  face and on its handler alike, and no hardware face on either.
- `AC-IO-008.3` (Ubiquitous): THE SYSTEM SHALL declare on every handler the message types its
  contract carries, a provider face's declaration being its consumer's with the directions exchanged.

`AC-IO-008.1` is decision `0021`'s rule, not an oversight: an output is single-writer because two
blocks commanding one contactor is a configuration error, and any number of blocks may read one input.
The SDK declares and never enforces it; enforcement is the cloud's at activation
(`contracts.md`, `AC-BIND-014.*`), and what reaches the introspection document is
`introspection.md`'s `AC-INTRO-015.6` — which omits the unconstrained default, so an input face's
absence of a consumer limit is the declaration.

`AC-IO-008.2` is two declarations with no compiler-enforced link between them (`AC-BIND-015.1`), so
this area's suites are what hold the eight in step. Each does different work: the contract type's
keeps a block that binds one out of the packed document (`AC-INTRO-002.6`) and is what the production
runtime refuses a configuration on; the handler's is what a production host's type scan filters on
(`AC-BIND-015.3`).

`AC-IO-008.3`'s declarations are read by the development host alone (`AC-SCEN-014.*`). This page
states that all eight are present and that the provider four mirror the consumer four; what the host
does with them is `scenarios.md`'s.

## Registration and the published surface

- `AC-IO-009.1` (Ubiquitous): THE SYSTEM SHALL register each package's hardware-face handlers with a
  host's service container and no others, one instance per actor.
- `AC-IO-009.2` (Ubiquitous): THE SYSTEM SHALL classify every public type either package ships as
  published or internal surface.

`AC-IO-009.1`'s omission is the point: a provider face's handler is registered by nothing and
constructed by nobody. A production host's scan drops it before it would resolve one
(`AC-BIND-015.3`), and the development host discovers it by its wire declaration and stands a generic
handler up under its class name instead (`AC-SCEN-014.*`). The three method bodies of each provider
handler therefore never run on any host — they exist so that the type carries its declarations and its
name. The per-actor lifetime is what `AC-IO-006.5`'s bound rests on.

Both packages carry `[assembly: DaleSharedAssembly]`, so every plugin binding them shares one instance
and one type identity (`plugin-loading.md`, `AC-PLUG-005.*`). That is not optional here: the message
types of `AC-IO-001.2` cross the plugin boundary on every round-trip.

`AC-IO-009.2` is author discipline with a partial gate behind it. Both packages carry the analyzer
reference, so `DALE014` judges a public type that carries neither mark — but only inside a namespace
the package declares as published surface, and each package declares its two face namespaces and not
its root. A public type in the root escapes the diagnostic, which is how the registration class went
unmarked; it is marked now, and the same shape elsewhere in the SDK is recorded in
[`_findings.md`](_findings.md).

## The mirror

- `AC-IO-010.1` (Ubiquitous): THE SYSTEM SHALL ship the digital and analog packages as one design, each file's counterpart differing only in the value type it carries and in what that value type implies. GAP: no test project references both packages, so the two are compared by each pass's own file-by-file diff rather than by a test.

The mirror is a contract on every change, not an observation about today: a fix applied to one package
is applied to the other in the same commit, or the change doc says why not. What the diff legitimately
still shows is the English article each package's noun takes, each package's `using` block sorted by
its own namespace names, the wire abbreviations in log templates, and the two serialisation buffer
sizes — a digital command's payload and an analog command's are genuinely different sizes, and each is
sized for its own.

## Test discipline

Each package has its own test suite, `Vion.Dale.Sdk.DigitalIo.Test` and `Vion.Dale.Sdk.AnalogIo.Test`,
mirror projects in the solution. A handler is driven through its own message loop —
`HandleMessageAsync` against a mocked actor context — because everything a handler does outwardly is
what it hands that context. FlatBuffer payloads are built in the test. **No test in this area reaches
a broker, the development host or a device**, and none asserts on a log call
([`../testing-conventions.md`](../testing-conventions.md) § 15): where the behaviour is a refusal, the
assertion is that nothing reached a block.

One criterion states one rule, with the digital and the analog member of a family as its rows — the
two packages are one design, so a criterion per package would double the page and halve its meaning.

Two suites outside this area prove criteria of it and are cited, never copied: the packages' TestKit
test projects, which own the helpers a consumer's tests call, and the development host's SmokeHost
scenarios, which drive the four round-trips over a live host at Tier 1 while the suites here own the
in-process half.
