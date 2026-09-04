---
trace: enforced
---

# Contracts and provider faces: declaring an endpoint, binding it, and exchanging messages

What the SDK guarantees an author who wants a logic block to talk to something else. There are two
kinds of counterpart and one model: an **inter-block interface**, whose counterpart is another logic
block in the same process, and a **service-provider contract**, whose counterpart is out of process
behind MQTT. Area code `BIND`. Process: [`../spec-process.md`](../spec-process.md).

The spine is the order an author meets the machinery: declare the contract, bind an endpoint, get
linked, exchange messages — first for the inter-block family, then for the service-provider one —
then the **provider face** on the far side of the second (implement, register, receive, forward,
publish), then the two identities and the metadata both families emit.

Cited rather than restated: [`introspection.md`](introspection.md) for what an endpoint's identity
is, what the document reports of it, and what a service relation carries;
[`config-gating.md`](config-gating.md) for what an inclusion gate does to a binding and what a
refused configuration leaves behind; [`block-lifecycle.md`](block-lifecycle.md) for when the binders
run and what the block does with a message routed to an endpoint it did not bind;
[`scenarios.md`](scenarios.md) for how a scenario drives a contract and how a topology maps one;
[`devhost-control.md`](devhost-control.md) for what the development host stands in with. The analyzer
diagnostics that guard the same rules at compile time — `DALE001`, `DALE009`, `DALE010`, `DALE011`,
`DALE019`, `DALE020`, `DALE025`, `DALE043`, `DALE045` — are the analyzer area's and are named, never
restated.

## The five ends of a declaration

A declaration that cannot be honoured has five ends, and which one an author gets is what this page
is for.

- **Refused at compile time** — the three shapes an analyzer covers never reach a running block.
- **Refused at bind time** — a missing setter, a blank or repeated identifier, an unresolvable
  implementation, a binding attribute no walk can read. The configuration message throws.
- **Skipped with a warning** — a mapping or a link naming an endpoint this instance did not bind, a
  send to an endpoint that is not linked.
- **Dropped in silence** — a contract with no mapping, a state update with no links, a forward for an
  unmapped contract. Each is documented design, and each is stated below.
- **Never noticed** — the fifth end, and the one this page exists to close: a declaration the compiler
  accepted, no diagnostic mentioned, and no binder read. Every such shape found is now a bind-time
  refusal.

## The two endpoint families

- `AC-BIND-001.1` (Ubiquitous): THE SYSTEM SHALL bind an inter-block interface endpoint for each
  logic interface a logic block's own class implements and for each one a public property's type
  implements, and a service-provider contract endpoint for each property whose type is declared a
  service-provider contract type.
- `AC-BIND-001.2` (Ubiquitous): THE SYSTEM SHALL decide which family a member belongs to from the
  marker on the bound type alone, so a binding attribute carries metadata and never causes a binding.
- `AC-BIND-001.3` (Event-driven): WHEN a binding attribute is declared where neither binder reads it —
  a contract binding whose property type is not a contract type, an interface binding naming an
  interface the annotated class or property does not implement, or an interface binding on a
  non-public property — THE SYSTEM SHALL refuse the configuration naming the member and what is
  wrong.

`AC-BIND-001.2` is the model [`../../architecture/concepts/logic-block-wiring.md`](../../architecture/concepts/logic-block-wiring.md)
describes, in the code that keeps it: the two binding attributes are a structurally matched pair and
the counterpart's marker is the only thing that tells the families apart. It is also why
`AC-BIND-001.3` had to exist — a declaration whose marker is missing or wrong is not a binding with
the wrong metadata, it is no binding at all, and until it was refused the block ran with a null
where its author expected an endpoint and a topology addressed an identifier nothing had minted.

An endpoint's identity — the declared identifier, the derived forms, the refusal of a blank or
repeated one, and the two namespaces — is [`introspection.md`](introspection.md)'s
`AC-INTRO-014.1`–`014.5`. Both binders mint through the one rule it states.

## Declaring a contract between two blocks

- `AC-BIND-002.1` (Ubiquitous): THE SYSTEM SHALL take an inter-block contract from a class naming two
  role interfaces, whose messages are the struct types nested in that class.
- `AC-BIND-002.2` (Ubiquitous): THE SYSTEM SHALL take from each message the role it travels from and
  the role it travels to, and from a request the struct its answer carries.
- `AC-BIND-002.3` (Event-driven): WHEN a contract's two role names do not both begin with `I` THE
  SYSTEM SHALL generate nothing for that contract.
- `AC-BIND-002.4` (Ubiquitous): THE SYSTEM SHALL give each role the ability to send every message
  declared from it and to handle every message declared to it, and a requesting role the ability to
  handle the answer.

`AC-BIND-002.1`'s "nested in that class" is a rule about where a message lives, and the reason is the
surface it produces: the generated faces are named for the contract class, so a message declared
beside it has no name to be generated under and contributes nothing. Nothing warns about one —
`DALE010` covers a role name that matches neither side, not a struct declared in the wrong place.

`AC-BIND-002.3` is the generator's half of `DALE009`. The diagnostic is an error, so a shipped
consumer never reaches this; it is stated because a hand-built compilation can.

## What a contract generates

- `AC-BIND-003.1` (Ubiquitous): THE SYSTEM SHALL generate one file per role of each contract.
- `AC-BIND-003.2` (Ubiquitous): THE SYSTEM SHALL generate for each role a handler interface marked as
  a logic interface naming the counterpart role's interface, the role's own sender interface and the
  contract class.
- `AC-BIND-003.3` (Ubiquitous): THE SYSTEM SHALL name a role's sender interface by the role's name
  with a `SenderInterface` suffix and its implementing class by that name without the leading `I`.
- `AC-BIND-003.4` (Ubiquitous): THE SYSTEM SHALL give the sender class a constructor taking the
  endpoint identifier, the implementation, an accessor for the owning block's identifier, the actor
  context and a logger, in that order.
- `AC-BIND-003.5` (Ubiquitous): THE SYSTEM SHALL generate, for a role that sends at least one message,
  an extension class named for the role's interface with an `Extensions` suffix, carrying a non-public
  static registration method, and SHALL generate none for a role that sends nothing.
- `AC-BIND-003.6` (Ubiquitous): THE SYSTEM SHALL give a sending role an extension that reports its
  linked counterparts, named for the counterpart role, and SHALL report none where the endpoint was
  never registered.
- `AC-BIND-003.7` (Ubiquitous): THE SYSTEM SHALL give a sending role one extension method per message
  it sends, forwarding to the registered endpoint and doing nothing where none is registered.

`AC-BIND-003.3`, `AC-BIND-003.4` and `AC-BIND-003.5` are one contract in three sentences, and it is
between two halves of the SDK rather than with a consumer: the interface factory finds the sender
class by the suffix, instantiates it by that constructor, and registers it by that method's name.
A consumer never writes any of the three and depends on all of them, because a rebuild against a
newer SDK regenerates one half and not the other.

`AC-BIND-003.5`'s second clause is what makes a receive-only endpoint's missing `GetLinked…` a
compile-time fact instead of a runtime surprise: there is no extension class to call it on, so the
code that would have failed silently does not compile.

## Binding an interface endpoint

- `AC-BIND-004.1` (Ubiquitous): THE SYSTEM SHALL bind one endpoint per logic interface the bound
  member offers, so a member whose type implements two logic interfaces yields two endpoints.
- `AC-BIND-004.2` (Ubiquitous): THE SYSTEM SHALL bind an interface endpoint from a public property
  whatever accessors it declares.
- `AC-BIND-004.3` (Ubiquitous): THE SYSTEM SHALL bind an interface endpoint from a public property
  only, so a non-public property carrying a logic interface offers no endpoint.
- `AC-BIND-004.4` (Event-driven): WHEN a property holds null THE SYSTEM SHALL bind no endpoint for it
  in the live view and SHALL describe its endpoints in the definition view, whether or not the binding
  is gated.
- `AC-BIND-004.5` (Ubiquitous): THE SYSTEM SHALL apply each interface binding's declared default name,
  tags and multiplicity to the endpoint it names.

`AC-BIND-004.2` and `AC-BIND-004.3` are the two halves an author has to hold together, and they are
not symmetric with the contract family on purpose. The binder never writes an interface-bearing
property — the component is the author's own instance — so a setter would be ceremony; but the
endpoints a block offers are its published wiring surface, so the walk stays public and a non-public
declaration is refused rather than quietly widened (`AC-BIND-001.3`).

`AC-BIND-004.4` is the general rule; [`config-gating.md`](config-gating.md)'s `AC-GATE-007.8` states
the gated case of it, where the null is the gate's own doing. The ungated case is the one an author
meets by accident, by constructing a component in the ready hook rather than at field initialisation.

Which of these bindings an inclusion gate can remove is [`config-gating.md`](config-gating.md)'s: a
class-implemented interface binds unconditionally (`AC-GATE-007.6`) because it has no member to carry
a gate, and a property-based one is gateable (`AC-GATE-007.2`).

## Finding an endpoint's implementation

- `AC-BIND-005.1` (Ubiquitous): THE SYSTEM SHALL build an interface endpoint from the one concrete
  sender implementation loaded for its sender interface, searching every loaded assembly.
- `AC-BIND-005.2` (Event-driven): WHEN no sender implementation, or more than one, is loaded for an
  endpoint THE SYSTEM SHALL refuse the configuration naming the sender interface and, where there are
  several, each of them.
- `AC-BIND-005.3` (Ubiquitous): THE SYSTEM SHALL register an endpoint against its implementation
  through the generated registration method, register nothing where the generated extension class is
  absent, and refuse the configuration where that class exists without the method.
- `AC-BIND-005.4` (Event-driven): WHEN building an endpoint fails THE SYSTEM SHALL name the endpoint
  identifier, its member and its logic block in the refusal, and SHALL carry the underlying reason
  rather than the reflection wrapper's.
- `AC-BIND-005.5` (Event-driven): WHEN an assembly cannot be fully enumerated during that search THE SYSTEM SHALL take the types that did load and continue. GAP: an assembly whose types fail to load cannot be produced inside the test process.

`AC-BIND-005.3`'s middle clause is not an oversight: a role that receives and never sends has no
generated extension class at all (`AC-BIND-003.5`), so an absent class is the ordinary shape of half
of every contract.

`AC-BIND-005.4` exists because both factory entries are reached reflectively. Without it every
refusal in this section arrived as "Exception has been thrown by the target of an invocation." —
which is then what a block records as its configuration failure and what
[`config-gating.md`](config-gating.md)'s `AC-GATE-004.1` repeats on every later refusal.

## Exchanging messages between blocks

- `AC-BIND-006.1` (Ubiquitous): THE SYSTEM SHALL address every inter-block message by the sending
  endpoint's identity and the receiving endpoint's identity.
- `AC-BIND-006.2` (Ubiquitous): THE SYSTEM SHALL send a command and a request to exactly the one
  linked endpoint the caller names, and a state update to every linked endpoint.
- `AC-BIND-006.3` (Ubiquitous): THE SYSTEM SHALL deliver a state update and an answer with the
  sender's identity, and a command and a request without it.
- `AC-BIND-006.4` (Event-driven): WHEN a send names an endpoint that is not linked THE SYSTEM SHALL
  drop the message and warn naming the endpoint, and WHEN a state update is sent from an endpoint with
  no links THE SYSTEM SHALL do nothing.
- `AC-BIND-006.5` (Ubiquitous): THE SYSTEM SHALL answer a request on the responding endpoint's own
  links, so an answer reaches its requester only where the two are linked in both directions.
- `AC-BIND-006.6` (Ubiquitous): THE SYSTEM SHALL replace an endpoint's links whole when a link map
  arrives.

`AC-BIND-006.3` is the difference between the three message attributes, and the only one an author
can observe: a command's receiver has no parameter to read a sender from, a state update's does.

`AC-BIND-006.5` is the one place the SDK's outbound depends on a link it did not make. A host that
links one direction only leaves every answer dropped by `AC-BIND-006.4`; the development host links
both (`scenarios.md`'s `AC-SCEN-013.2`).

`AC-BIND-006.6`'s replacement is deliberate and is the runtime's requirement rather than a
convenience: handler and block actors are recreated across a reconfiguration, so a merged map would
keep references to actors that have been stopped. It is not the same rule as
[`block-lifecycle.md`](block-lifecycle.md)'s `AC-LIFE-003.5`, which merges the maps a block receives
*before* its configuration, per interface — that merge is itself a replace at the endpoint.

## Binding a service-provider contract

- `AC-BIND-007.1` (Ubiquitous): THE SYSTEM SHALL construct one contract instance per bound property
  and assign it there, whatever the property's own accessibility.
- `AC-BIND-007.2` (Event-driven): WHEN a contract-typed property declares no setter THE SYSTEM SHALL
  refuse the configuration naming the property, resolving the property's accessors on the type that
  declares them.
- `AC-BIND-007.3` (Ubiquitous): THE SYSTEM SHALL construct a contract with its endpoint identifier and
  the block's actor context, taking every further constructor argument from the block's service
  provider.
- `AC-BIND-007.4` (Ubiquitous): THE SYSTEM SHALL apply the contract binding's declared default name,
  tags and multiplicity to the contract it binds.

`AC-BIND-007.2`'s second clause is the whole of it. Reflection does not inherit a non-public
accessor, so a property declared `{ get; private set; }` on a base class comes back from a derived
block's walk with no setter at all — and the refusal that followed recommended, verbatim, the
declaration the author had already written. `DALE001` sees the source and stays silent, so the
compile-time guard does not catch it either.

`AC-BIND-007.3` is why a contract package ships service registrations: anything beyond the two
arguments the binder supplies has to be resolvable, and [`plugin-loading.md`](plugin-loading.md)
states where a host discovers those registrations from.

## Finding a contract's implementation

- `AC-BIND-008.1` (Ubiquitous): THE SYSTEM SHALL bind a contract to the first concrete implementation
  of its interface the search reaches, whatever else implements it.
- `AC-BIND-008.2` (Event-driven): WHEN no implementation of a contract is loaded THE SYSTEM SHALL
  refuse the configuration naming the contract and the binding that wanted it.
- `AC-BIND-008.3` (Event-driven): WHEN an assembly that references the SDK cannot be enumerated THE SYSTEM SHALL refuse the configuration naming the assembly and the contract. GAP: an assembly whose types fail to load cannot be produced inside the test process.

The two discoveries of this page do not share a rule, and the divergence is real: this one filters
the assemblies by reference and refuses what it cannot enumerate, while `AC-BIND-005.1`'s scans
everything and degrades. Neither is stated as the other's mistake here.

The same search answers a *scan* — "which concrete implementations are loaded" — with a list, and an
empty list is an answer rather than a failure: the development host's handler discovery and this
repository's own exclusion suite both read it that way, and only the singular lookup behind
`AC-BIND-008.2` turns emptiness into a refusal.

`AC-BIND-008.1`'s "first" is an enumeration order, not a choice: where two differently named types
implement one contract, one of them is bound and the other is not, and which is not predictable from
the source. The search itself considers only assemblies that reference the SDK, and where one type
name occurs in several of them it takes the highest-versioned assembly's — the same identity question
[`plugin-loading.md`](plugin-loading.md) settles for a plugin's types.

## What a contract guarantees its block

- `AC-BIND-009.1` (Ubiquitous): THE SYSTEM SHALL make a contract's endpoint identifier readable on the
  contract for the block's whole life.
- `AC-BIND-009.2` (Ubiquitous): THE SYSTEM SHALL identify a contract by its owning block and its own
  identifier, and SHALL take the block half only from the runtime.
- `AC-BIND-009.3` (Event-driven): WHEN a contract identity is set whose contract half is not the
  contract's own identifier THE SYSTEM SHALL refuse it naming both.
- `AC-BIND-009.4` (Ubiquitous): THE SYSTEM SHALL require a contract implementation to name the handler
  actor it exchanges messages with, and SHALL address that name for the block's whole life.
- `AC-BIND-009.5` (Ubiquitous): THE SYSTEM SHALL drop every message a contract with no configured
  mapping sends.
- `AC-BIND-009.6` (Event-driven): WHEN a mapped contract sends before the runtime has linked its
  handler THE SYSTEM SHALL refuse the send naming the contract and the handler.

`AC-BIND-009.5` and `AC-BIND-009.6` are the two shapes of "there is nowhere to send this", and they
answer differently on purpose. An unmapped contract is a wire a topology deliberately left open, so
dropping is right and the warning is at configuration. An unlinked handler on a *mapped* contract is
a host that did something out of order, and dropping it would be indistinguishable from the first
while meaning the opposite.

`AC-BIND-009.4`'s handler name is resolved to an actor reference whether or not an actor of that name
exists — [`block-lifecycle.md`](block-lifecycle.md)'s `AC-LIFE-017.1`. A contract whose handler class
is absent from the host therefore binds, maps, sends, and reaches no one.

## The provider face

- `AC-BIND-010.1` (Ubiquitous): THE SYSTEM SHALL route the messages a service-provider handler
  receives to five ends — an MQTT registration request, a contract link map, a scheduled action, an
  MQTT message and a contract message — and SHALL ignore every other message without error.
- `AC-BIND-010.2` (Ubiquitous): THE SYSTEM SHALL require a service-provider handler to declare its
  MQTT registration, to receive MQTT messages and to receive contract messages, and SHALL keep the
  routing itself out of a subclass's reach.
- `AC-BIND-010.3` (Event-driven): WHEN a registration request arrives THE SYSTEM SHALL subscribe each
  declared action path under the three-level service-provider wildcard, register under the handler's
  own class name with its declared routing key, and answer the request.
- `AC-BIND-010.4` (Event-driven): WHEN a declared action path does not begin with a topic separator
  THE SYSTEM SHALL leave it out of the registration and report it, naming the handler and the path.
- `AC-BIND-010.5` (Event-driven): WHEN a contract link map arrives THE SYSTEM SHALL replace the
  handler's mappings whole and notify the subclass.
- `AC-BIND-010.6` (Event-driven): WHEN a handler reads its actor context before it has received a
  message THE SYSTEM SHALL refuse the read naming the handler.
- `AC-BIND-010.7` (Ubiquitous): THE SYSTEM SHALL forward a value to every logic-block contract mapped
  to a service-provider contract, addressing each by its own contract identity, and SHALL forward
  nothing for a service-provider contract it has no mapping for.
- `AC-BIND-010.8` (Ubiquitous): THE SYSTEM SHALL report every service-provider contract a logic-block
  contract is mapped to, and none where there is no mapping.
- `AC-BIND-010.9` (Ubiquitous): THE SYSTEM SHALL run an action a handler schedules on that handler's
  own actor after the requested delay.

`AC-BIND-010.3` is the whole of what a provider-face author implements and the reason the wildcard
has exactly three levels: they stand for the service provider, the service and the contract, which is
what `AC-BIND-012.1` reads back off a received topic.

`AC-BIND-010.4` reports rather than refuses because the acknowledgement of `AC-BIND-010.3` is
something a runtime waits for on a short timeout: a throw would trade a topic that matches nothing
for a host that does not start.

`AC-BIND-010.7`'s second clause is the ordinary case, not the error one — a handler subscribed by
wildcard receives frames for every endpoint of its kind on the installation, mapped or not.

## Publishing from a provider face

- `AC-BIND-011.1` (Ubiquitous): THE SYSTEM SHALL publish a message carrying a correlation identifier,
  the payload's schema name as a user property, and the caller's response topic and retain flag, and
  SHALL report the correlation identifier used.
- `AC-BIND-011.2` (Ubiquitous): THE SYSTEM SHALL mint a correlation identifier where the caller
  supplies none, reuse the one it supplies, and carry it as its sixteen raw bytes.
- `AC-BIND-011.3` (Ubiquitous): THE SYSTEM SHALL declare a published message's content type as
  FlatBuffer where the caller names none.
- `AC-BIND-011.4` (Ubiquitous): THE SYSTEM SHALL serialize a JSON publish with camel-cased names and
  string-named enum values, and declare its content type as JSON.

`AC-BIND-011.3` was the page's other unkept promise. The method documented the FlatBuffer default and
the message record carries it, but the value reached the record positionally, so an omitted content
type went out as none at all.

## Reading a topic

- `AC-BIND-012.1` (Ubiquitous): THE SYSTEM SHALL read a service-provider contract's identity from the
  three topic segments after the installation topic, the last of which may run to the end of the
  topic.
- `AC-BIND-012.2` (Event-driven): WHEN a topic is shorter than the installation topic, or carries
  fewer than three segments after it, THE SYSTEM SHALL refuse to read an identity from it, naming the
  topic.
- `AC-BIND-012.3` (Ubiquitous): THE SYSTEM SHALL read a correlation identifier from sixteen raw bytes
  or from thirty-six UTF-8 characters, and SHALL report an empty identifier for any other correlation
  data and for none.
- `AC-BIND-012.4` (Event-driven): WHEN a strict correlation read finds no data, or data it cannot
  parse, THE SYSTEM SHALL refuse it naming the topic.
- `AC-BIND-012.5` (Ubiquitous): THE SYSTEM SHALL take the installation topic once per process, ignore
  every later assignment and refuse a null one.
- `AC-BIND-012.6` (Ubiquitous): THE SYSTEM SHALL carry a received message's response topic and
  correlation data through actor message headers, encoding the correlation data as text, and SHALL
  carry neither where the message did not have both.
- `AC-BIND-012.7` (Event-driven): WHEN the installation topic is read before it has been assigned THE SYSTEM SHALL refuse the read. GAP: the configuration is process-wide and write-once, so no test in a shared test assembly can observe the unassigned state.

`AC-BIND-012.2` is the difference between a handler that logs a topic it cannot route and one whose
message arm dies with an exception nothing documents. The parse runs in the sealed dispatch, before a
subclass can catch anything, so its exception type is part of what the base promises.

`AC-BIND-012.3`'s tolerance is deliberate: a frame with unreadable correlation data loses its
identity, not the handler its life. `AC-BIND-012.4` is the same read for a handler that would rather
refuse.

## What a binding tells the outside

- `AC-BIND-013.1` (Ubiquitous): THE SYSTEM SHALL emit a binding's default name and tags into its
  introspection annotations only where each is non-empty.
- `AC-BIND-013.2` (Ubiquitous): THE SYSTEM SHALL emit a binding's multiplicity only where it is not
  the unconstrained default, as the shared wire token for that value.
- `AC-BIND-013.3` (Ubiquitous): THE SYSTEM SHALL emit a binding's inclusion predicate whenever one is
  declared, the empty predicate included.
- `AC-BIND-013.4` (Ubiquitous): THE SYSTEM SHALL emit a contract's handler-actor name under a fixed
  annotation key, and a development-only declaration under a second, omitting the second otherwise.

What the keys mean and how the document reports them is
[`introspection.md`](introspection.md)'s `AC-INTRO-015.2`, `015.5` and `015.6`; which values a
binding puts there is this page's. The key strings and the multiplicity tokens are the shared
contracts package's, so a producer and a consumer cannot drift.

## Multiplicity is declared here and enforced elsewhere

- `AC-BIND-014.1` (Ubiquitous): THE SYSTEM SHALL offer one multiplicity vocabulary of four values,
  declarable per binding on the consumer side and per contract type on the provider side, and SHALL
  enforce none of them.

The enforcement is the cloud's, at activation
([`../../architecture/decisions/0021-contract-link-multiplicity.md`](../../architecture/decisions/0021-contract-link-multiplicity.md)).
The default is the unconstrained value and is omitted from the annotations rather than emitted, which
is what keeps an unannotated block unconstrained for a reader that adopts the vocabulary later.

## Development-only surface

- `AC-BIND-015.1` (Ubiquitous): THE SYSTEM SHALL let a contract type declare itself development
  surface, and a service-provider handler declare the same of itself, with no link between the two
  declarations other than the author's.
- `AC-BIND-015.2` (Ubiquitous): THE SYSTEM SHALL let a development-only handler subscribe to no topic
  and claim an empty routing key.

A provider face is the inverse of a consumer contract, bound by a simulator block so a bench has a
behaving peer ([`../simulator-authoring.md`](../simulator-authoring.md)); it exists only where a
development host routes it
([`../../architecture/decisions/0139-service-provider-simulation-tiers.md`](../../architecture/decisions/0139-service-provider-simulation-tiers.md)).
Three mechanisms keep it off a production host and they act on different things:
[`introspection.md`](introspection.md)'s `AC-INTRO-002.6` and `002.9` keep a block that binds one out
of the packed document, the handler marker of `AC-BIND-015.1` is what a production host's type scan
filters on, and the production runtime refuses outright a configuration whose block binds a
development-only contract type — that refusal is the runtime's, carried here and specified nowhere in
this repository, and it is what makes the two-declaration convention load-bearing rather than
advisory. Nothing in the SDK connects them, so an author who declares one and forgets the
other gets no diagnostic; a suite in this repository is what holds the shipped pair in step.

`AC-BIND-015.2` is why the marker is load-bearing rather than cosmetic. A provider face has no
transport, so it declares an empty routing key — which a client that matches handlers by prefix or
substring cannot tell apart from a key that matches everything.

## Carried, not specified

- `AC-BIND-016.1` (Ubiquitous): THE SYSTEM SHALL publish the handler-actor protocol — the registration
  request and answer, the registration itself, its topic groups, the received message, the two publish
  forms and their answer, the connect-time registration and the service-provider marker — as message
  types a host outside the runtime can construct.
- `AC-BIND-016.2` (Ubiquitous): THE SYSTEM SHALL answer a registration request whether or not the
  registration it sent will be accepted, so the answer states that the handler is alive and not that
  it is subscribed.
- `AC-BIND-016.3` (Ubiquitous): THE SYSTEM SHALL publish the two message envelopes and the contract
  link map as types a host outside the runtime can construct.

What the MQTT client does with those messages is not this page's and is not specified anywhere in
this repository: the registration aborts its own remarks describe, the installation prefixing, the
retry the attempt counter drives and delivery itself are the private runtime's. `AC-BIND-016.2` is
the one consequence of that split an author has to know, because it is the difference between an
acknowledged registration and a subscribed handler — a handler whose routing key is empty is answered
here and skipped there.

The remote-interface link and the remote installation topics ride the same published vocabulary and
are handled nowhere in this repository — [`block-lifecycle.md`](block-lifecycle.md) carries them.
