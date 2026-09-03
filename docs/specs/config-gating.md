---
trace: enforced
---

# Instantiation parameters and config-time structural gating

How an operator's configuration decides which members a logic-block instance actually has. A block
declares a **static maximum** — every component, contract binding and interface endpoint it could
carry — and the configuration chooses among them by setting `[InstantiationParameter]` values that
`[IncludedWhen]` predicates read. Area code `GATE`. Process: [`../spec-process.md`](../spec-process.md).

The choice is made **once, before the block runs**. Nothing here reacts to a runtime value: an
excluded member does not exist for the life of the instance, and changing a parameter is a
re-activation that re-instantiates the block. The soft, runtime-reactive sibling is
`[Presentation(VisibleWhen = …)]`, which only hides a member that still exists and still publishes.

## Instantiation parameters

An `[InstantiationParameter]` is a modifier on a real `[ServiceProperty]`: the property is an
ordinary member of the block's root service, and the attribute marks its value as chosen at
configuration time rather than produced at runtime. Parameters are useful on their own as fixed
setup scalars; inclusion gates are their most important consumer.

- `AC-GATE-001.1` (Event-driven): WHEN a logic block is configured THE SYSTEM SHALL apply the
  configuration's `[InstantiationParameter]` values to their properties before `Configure` runs.
- `AC-GATE-001.2` (Event-driven): WHEN a configuration supplies no value for a declared
  `[InstantiationParameter]` THE SYSTEM SHALL leave the property at its C# initializer default.
- `AC-GATE-001.3` (Event-driven): WHEN a configuration supplies one identifier more than once THE
  SYSTEM SHALL apply the last of those values.
- `AC-GATE-001.4` (Ubiquitous): THE SYSTEM SHALL apply an `[InstantiationParameter]` value whatever
  the property's accessor shape, including an `init`-only one.
- `AC-GATE-001.5` (Ubiquitous): THE SYSTEM SHALL apply an `[InstantiationParameter]` declared on a
  base logic-block class to the derived instance.

`AC-GATE-001.1` is the ordering the whole design rests on: the binders resolve gates by reading these
properties, so a value applied after `Configure` would decide nothing. It also fixes what block code
can see — the value is set between construction and `Configure`, so a constructor sees the C#
initializer default and never the operator's choice.

`{ get; init; }` is the recommended shape, because the compiler then refuses an assignment the
analyzer could only warn about. It is not the mechanism: `AC-GATE-001.4` holds for a plain public
setter too. On `netstandard2.1` an `init` accessor needs an `IsExternalInit` polyfill, which the
project template supplies.

## Resolving and decoding a supplied value

The configuration channel carries identifiers and JSON scalars, not typed values. Resolution and
decoding are strict in both directions, because a parameter that silently took a wrong value would
resolve the instance's gates to a shape nobody chose.

- `AC-GATE-002.1` (Event-driven): WHEN a configuration names an identifier that is not an
  `[InstantiationParameter]` property of the block THE SYSTEM SHALL fail block initialization with an
  `InvalidOperationException` naming that identifier and the block type.
- `AC-GATE-002.2` (Ubiquitous): THE SYSTEM SHALL match a configuration's parameter identifier to the
  property name case-sensitively.
- `AC-GATE-002.3` (Event-driven): WHEN a supplied value cannot be decoded into its parameter's type
  THE SYSTEM SHALL fail block initialization with an `InvalidOperationException` naming that
  identifier, the block type and the decode error.
- `AC-GATE-002.4` (Ubiquitous): THE SYSTEM SHALL decode a parameter value from the shared JSON-scalar
  form, taking an enum from its member-name string and an integer kind from a JSON number, and SHALL
  refuse a value of any other JSON shape.
- `AC-GATE-002.5` (Event-driven): WHEN a supplied enum value names no member of the declared enum THE
  SYSTEM SHALL fail block initialization.
- `AC-GATE-002.6` (Event-driven): WHEN a supplied integer value lies outside the declared property
  type's range THE SYSTEM SHALL fail block initialization.
- `AC-GATE-002.7` (Event-driven): WHEN a supplied value is JSON null THE SYSTEM SHALL fail block
  initialization for a parameter whose declared type is not nullable, and SHALL set the property to
  null for one whose type is nullable.
- `AC-GATE-002.8` (Event-driven): WHEN more than one supplied parameter fails to resolve or to decode
  THE SYSTEM SHALL name every one of them in the failure.
- `AC-GATE-002.9` (Event-driven): WHEN exactly one supplied parameter fails to decode THE SYSTEM
  SHALL carry that decode failure as the initialization failure's inner exception.
- `AC-GATE-002.10` (Event-driven): WHEN any supplied parameter fails to resolve or to decode THE
  SYSTEM SHALL apply none of that configuration's values.

`AC-GATE-002.2` is an identifier-stability rule wearing a decoding hat: a parameter identifier is a
translation key the cloud stores ([`introspection.md`](introspection.md)), so two spellings must not
name one property.

`AC-GATE-002.4` is the encoding every producer of a configuration shares — the cloud payload, a
development-host topology file, a test. A decoder that tolerated a numeric string for an integer, or
an ordinal for an enum, is where those producers would start to disagree with each other.

`AC-GATE-002.8` and `AC-GATE-002.10` are what make a wrong configuration cheap to correct: every bad
parameter is named at once, and none of the good ones is applied, so the instance is left as it was
rather than half-moved.

## A parameter does not move at runtime

The declared type set is deliberately narrow — `bool`, an enum, an integer kind, `string`. No
floating-point type, because an analog value must not decide structure; no struct or array, because a
gate references a scalar. The value must also read back honestly: block code that branches on a
parameter has to see the value the gates evaluated.

- `AC-GATE-003.1` (Event-driven): WHEN a set-value request names an `[InstantiationParameter]`
  property THE SYSTEM SHALL refuse the write and leave the property unchanged.
- `AC-GATE-003.2` (Event-driven): WHEN the system refuses such a write THE SYSTEM SHALL answer the
  requester with the property's unchanged value.
- `AC-GATE-003.3` (Ubiquitous): THE SYSTEM SHALL exclude every `[InstantiationParameter]` from
  persistence discovery.
- `AC-GATE-003.4` (Event-driven): WHEN restoring persistent data that names a member this instance
  does not carry THE SYSTEM SHALL leave the block's state unchanged and complete the restore.
- `AC-GATE-003.5` (Event-driven): WHEN a block whose configuration failed is asked to restore
  persistent data, to stop, or for its persistent-data snapshot THE SYSTEM SHALL answer each request,
  restoring nothing and reporting an empty snapshot.
- `AC-GATE-004.1` (Event-driven): WHEN a block whose configuration already ran, whether it succeeded
  or failed, receives a second configuration THE SYSTEM SHALL refuse it with an
  `InvalidOperationException` that names the earlier failure where there was one, and SHALL leave the
  bound member set unchanged.

A parameter deliberately carries a public setter so the platform can apply the configured value by
reflection. `AC-GATE-003.1` and `AC-GATE-003.3` are the two guards that keep that setter from being
anything else: the runtime write is refused, and persistence — which discovers writable members, by
service binding and by opt-in alike — skips parameters at both doors, so the configuration channel
stays their only source of truth. An author who marks a parameter `[Persistent]` is told so at build
time rather than left with an attribute that does nothing.

`AC-GATE-004.1` is the same rule at the other end. Binding registrations only grow, so a second
configuration would add a member a widened gate now includes and keep one a narrowed gate no longer
does. There is no in-place reconfiguration: a changed parameter re-activates, and a re-activation
re-instantiates. A configuration that *failed* spends the instance too — it may have registered part
of a member set before it threw — so the retry is refused with the original reason rather than being
pointed back at the configuration that just failed.

`AC-GATE-003.5` is the same instance seen from teardown. A block that failed closed still has to be
reclaimed, and the runtime waits on an acknowledgement for each of restore, stop and snapshot, so all
three answer whether or not the configuration ever completed.

## Inclusion gates

`[IncludedWhen]` takes a predicate in the shared dialect. Its references resolve against the block's
own `[InstantiationParameter]` properties, as bare single-segment names.

- `AC-GATE-005.1` (Ubiquitous): THE SYSTEM SHALL treat a member carrying no `[IncludedWhen]` as part
  of every configured instance.
- `AC-GATE-005.2` (State-driven): WHEN a member carries `[IncludedWhen]` THE SYSTEM SHALL include it
  in the configured instance only while its predicate evaluates true against that instance's
  `[InstantiationParameter]` values.
- `AC-GATE-005.3` (Ubiquitous): THE SYSTEM SHALL build a predicate's evaluation context from the
  block's `[InstantiationParameter]` properties and no other member.
- `AC-GATE-005.4` (Ubiquitous): THE SYSTEM SHALL encode each parameter into that context in the
  shared JSON-scalar form, so an enum parameter compares against its member name and an integer
  parameter against a number.
- `AC-GATE-005.5` (Ubiquitous): THE SYSTEM SHALL build that context from each parameter's current
  value, so a configured value and an unsupplied parameter's C# default are both what the gates see.
- `AC-GATE-005.6` (Event-driven): WHEN a gate's predicate does not parse THE SYSTEM SHALL fail block
  initialization.
- `AC-GATE-005.7` (Event-driven): WHEN a gate's predicate references a name the parameter context
  does not carry THE SYSTEM SHALL fail block initialization.
- `AC-GATE-005.8` (Event-driven): WHEN a gate's predicate references a parameter whose value is null
  THE SYSTEM SHALL fail block initialization.
- `AC-GATE-005.9` (Ubiquitous): THE SYSTEM SHALL treat a declared `[IncludedWhen]` predicate as its
  member's gate whatever the predicate's text, so an empty one is a gate that cannot be resolved rather
  than an absent one.

Evaluation is strict and fail-closed. `AC-GATE-005.6` to `AC-GATE-005.8` are one posture in three
shapes: a block whose member set is undecidable must not bind an arbitrary half of itself, so it does
not start. A gate over a parameter whose value can be null needs a non-null default, or every
activation that leaves it unset fails.

`AC-GATE-005.3` is what keeps structure out of the runtime: a gate that could read an ordinary
service property would make members appear and vanish while a block runs, which is not what this is.

`AC-GATE-005.9` settles the one question the three recorders and the evaluator could answer
differently. A member's gate is `null` when the attribute is absent and its predicate otherwise;
"empty" is a predicate that does not parse, not the absence of one — so it is refused by
`AC-GATE-006.4` like any other unworkable gate, where reading it as absent would have shipped an
unconditional member the runtime then refuses to bind.

## The definition view and the live view

The same binders serve two callers. Introspection has no configuration — it describes a type, so it
must emit every member the type can carry along with the predicate that decides each. Configuration
has one, so it emits the members that instance actually has.

- `AC-GATE-006.1` (Event-driven): WHEN a block is introspected THE SYSTEM SHALL bind its full declared
  member set regardless of gates and record each gated member's predicate.
- `AC-GATE-006.2` (State-driven): WHILE binding that definition set THE SYSTEM SHALL evaluate no
  predicate.
- `AC-GATE-006.3` (Event-driven): WHEN a block is configured for a running instance THE SYSTEM SHALL
  evaluate every gate and bind only the included members.
- `AC-GATE-006.4` (Event-driven): WHEN a block is introspected and one of its gates carries a predicate
  that does not parse, or that names something the block does not declare as an
  `[InstantiationParameter]`, THE SYSTEM SHALL refuse the introspection naming the member and the
  predicate.
- `AC-GATE-006.5` (Ubiquitous): THE SYSTEM SHALL NOT evaluate a gate against the block's own parameter
  values while introspecting, so a parameter whose declared default is null does not make its gate a
  refusal.

`AC-GATE-006.2` follows from `AC-GATE-006.1` rather than qualifying it: the definition view runs a
default instance whose parameter values are nobody's configuration, so deciding there would answer a
question no operator asked.

`AC-GATE-006.4` is the line between deciding and checking. A gate whose predicate cannot parse, or
names something the block does not declare, has no configuration that could make it work — so it is
refused where a broken block is cheapest to catch: introspection is what `dotnet pack` runs, so the
artifact never ships. `AC-GATE-006.5` is the boundary on the other side: the referenced names are read
syntactically, off the parsed predicate, and nothing is evaluated. Evaluating would decide two ways at
once — an evaluator short-circuits, so `Count >= 2 && Missing >= 1` returns a verdict without ever
reaching the undeclared name, and a parameter whose declared default is null would turn a perfectly
good gate into a refusal. Type discipline inside a predicate remains the analyzer's, at build time.

## What an exclusion removes

The gateable members are the ones the definition view exposes as wireable or publishable: a
property-based interface binding, a contract binding, and a service-bearing component property.
Gating a component gates everything the binders derive from it.

- `AC-GATE-007.2` (Event-driven): WHEN a gated contract binding is excluded THE SYSTEM SHALL leave its
  property null and never construct the contract.
- `AC-GATE-007.3` (Event-driven): WHEN a gated service-bearing component is excluded THE SYSTEM SHALL
  bind neither its service properties nor its measuring points.
- `AC-GATE-007.4` (Ubiquitous): THE SYSTEM SHALL leave an excluded component's own instance untouched,
  so block code still reaches a non-null object.
- `AC-GATE-007.5` (Ubiquitous): THE SYSTEM SHALL bind the block's own root service unconditionally.
- `AC-GATE-007.6` (Ubiquitous): THE SYSTEM SHALL bind a class-implemented interface unconditionally.
- `AC-GATE-007.7` (Event-driven): WHEN a gated service-bearing component property holds null THE
  SYSTEM SHALL omit that member from the definition view, reporting neither the member nor its gate.
- `AC-GATE-007.8` (Event-driven): WHEN a gated property-based interface binding holds null THE SYSTEM
  SHALL still report that endpoint and its gate in the definition view.

`AC-GATE-007.2` and `AC-GATE-007.4` are the difference an author has to hold: a **contract** is what
the binder constructs, so an excluded one is `null` — declare a gated contract property nullable and
null-guard the code that drives it. A **component** is the author's own object, so an excluded one
stays reachable and merely inert, which is why a timer that samples every point still runs.

`AC-GATE-007.5` and `AC-GATE-007.6` are the two members with nowhere to put a gate. Whole-block
existence is the operator adding the instance or not; a class-implemented interface has no member to
carry the attribute, so an author who needs to gate one converts it to a property-based binding.

`AC-GATE-007.7` and `AC-GATE-007.8` split on what an instance is needed for. A service-bearing
component's members are enumerated off the object, so a null one contributes nothing to describe and
its gate is invisible to a catalog reader — a gated component must exist by the time `Configure` runs.
An interface endpoint's identity is the property name and the interface type, both known without an
instance, so a null one is still described and still carries its gate; nothing dispatches in the
definition view, so there is nothing behind it to need.

## A configuration that names an excluded member

A configuration can name an endpoint this instance removed — a payload written against a wider
configuration, a topology whose count was lowered, a peer that has not caught up. None of these is
fatal to the receiving block.

- `AC-GATE-008.1` (Event-driven): WHEN a configuration maps a contract this instance excluded THE
  SYSTEM SHALL skip that mapping and keep the block running.
- `AC-GATE-008.2` (Event-driven): WHEN a configuration links an interface this instance excluded THE
  SYSTEM SHALL skip that link and keep the block running.
- `AC-GATE-008.3` (Event-driven): WHEN a message is routed to a property-based interface binding this
  instance excluded THE SYSTEM SHALL deliver it nowhere and keep the block running, that binding never
  having been bound.

The wire to an excluded endpoint is dead, not broken: nothing arrives, and the block that would have
received it stays up. An editor is where such a mapping is meant to be caught, before it ships.

## Gates and persistence

Persistence follows the configured shape rather than the declared one, and it resolves the gates
itself rather than inferring the included set from what the binders registered — a component bound
only through its interface declares no service member, so binder keys would miss it.

- `AC-GATE-009.1` (Ubiquitous): THE SYSTEM SHALL capture the `[Persistent]` members of an included
  component and none of an excluded one, resolving the gates against the same parameter values the
  configuration used.
- `AC-GATE-009.2` (Event-driven): WHEN a later configuration excludes a component an earlier one
  included THE SYSTEM SHALL stop capturing that component's persistent state.

There is no dormancy. `AC-GATE-009.2` and `AC-GATE-003.4` are the two halves of that: an excluded
member's state stops being captured, and a file that still names it restores without it. A
configuration that removes a component loses that component's history; one that adds it back starts
from the member's declared default.

## What the wire carries

Introspection is the contract a cloud reads. The fields below are what a client needs to render a
parameter editor and resolve the definition view down to a live one: where a member's gate is recorded,
and how a parameter is marked and defaulted.

- `AC-GATE-010.1` (Ubiquitous): THE SYSTEM SHALL report a gated component service's predicate on that
  service, and report null for an ungated one.
- `AC-GATE-010.2` (Ubiquitous): THE SYSTEM SHALL report a gated contract binding's and a gated
  interface binding's predicate as an annotation on that binding.
- `AC-GATE-010.3` (Ubiquitous): THE SYSTEM SHALL report an `[InstantiationParameter]` property as
  read-only in its schema whatever its C# accessors are.
- `AC-GATE-010.4` (Ubiquitous): THE SYSTEM SHALL report an `[InstantiationParameter]` property with a
  runtime instantiation-parameter marker and the declaring block's default value, JSON-scalar-encoded.
- `AC-GATE-010.5` (Ubiquitous): THE SYSTEM SHALL report a null default for an
  `[InstantiationParameter]` whose declared default is null.
- `AC-GATE-010.6` (Ubiquitous): THE SYSTEM SHALL publish the `Minimum` and `Maximum` declared on an
  `[InstantiationParameter]`'s paired `[ServiceProperty]` in its schema, and SHALL apply a configured
  value outside them.

`AC-GATE-010.3` is what makes a parameter read-only everywhere it is rendered, whatever accessors the
declaration used — the flag is forced by the attribute, not inferred from the C# setter the platform
needs.

`AC-GATE-010.4` reports the default in the same encoding a gate's evaluation context uses, so a client
resolving gates for an instance that overrides nothing compares against exactly what the predicate
names.

`AC-GATE-010.6` states a boundary worth knowing: the declared bounds are what an editor renders, not
what the block enforces. A configured value outside them is applied.

## Authoring diagnostics

Two error-severity diagnostics police the declarations. `DALE043` judges an `[IncludedWhen]` — where
it may be applied, and whether its predicate parses and resolves. `DALE044` judges an
`[InstantiationParameter]`'s discipline, and the type errors of a predicate referencing one.

- `AC-GATE-011.1` (Event-driven): WHEN `[IncludedWhen]` is applied to a logic-block class THE SYSTEM
  SHALL report DALE043.
- `AC-GATE-011.2` (Event-driven): WHEN `[IncludedWhen]` is applied to a method THE SYSTEM SHALL report
  DALE043.
- `AC-GATE-011.3` (Event-driven): WHEN `[IncludedWhen]` is applied to a member that is neither a
  property-based interface binding, a contract binding, nor a service-bearing component THE SYSTEM
  SHALL report DALE043.
- `AC-GATE-011.4` (Event-driven): WHEN `[IncludedWhen]` or `[InstantiationParameter]` is re-declared on
  an `override` or `new` member THE SYSTEM SHALL report a diagnostic.
- `AC-GATE-011.5` (Event-driven): WHEN an `[IncludedWhen]` predicate does not parse, is qualified, or
  references a property that is not an `[InstantiationParameter]` of the same block THE SYSTEM SHALL
  report DALE043.
- `AC-GATE-011.6` (Event-driven): WHEN an `[IncludedWhen]` predicate compares a parameter against a
  literal of another type, or names an enum member unquoted, THE SYSTEM SHALL report DALE044.
- `AC-GATE-011.7` (Ubiquitous): THE SYSTEM SHALL report no diagnostic for an `[IncludedWhen]` predicate
  that parses and type-checks, whatever that predicate would evaluate to.
- `AC-GATE-011.8` (Event-driven): WHEN an `[InstantiationParameter]` is declared on a type that is not
  a logic block, without a paired `[ServiceProperty]`, combined with `WriteOnly`, combined with
  `[Persistent]`, on a type outside bool, enum, an integer kind and string, with a computed getter, or
  is assigned in the declaring block's own code outside its constructor or an object initializer, THE
  SYSTEM SHALL report DALE044.
- `AC-GATE-011.9` (Ubiquitous): THE SYSTEM SHALL report no diagnostic for an `[InstantiationParameter]`
  declared with a plain public setter.
- `AC-GATE-011.11` (Ubiquitous): THE SYSTEM SHALL report no diagnostic for an `[InstantiationParameter]`
  combined with `[Persistent(Exclude = true)]`.
- `AC-GATE-011.10` (Event-driven): WHEN `[IncludedWhen]` is applied to a type that is not a logic
  block, or to a member of one, THE SYSTEM SHALL report DALE043.

`AC-GATE-011.4` follows from both runtime readers keying by property **name**: two declarations of one
name is an ambiguity neither the parameter applier nor the gate context can resolve, so the gate is
declared once, at the declaration a hierarchy shares.

`AC-GATE-011.7` is the boundary of what compile time can say. The analyzer has no operator values, so
it checks that a predicate is well-formed and never what it would decide. `AC-GATE-011.10` closes the
matching hole: a gate is only read off a logic block's own members, so one declared anywhere else is
reported rather than left inert.

`AC-GATE-011.8`'s rules are one obligation in several shapes — the value block code reads must provably
be the value the gates evaluated. A computed getter, an in-code assignment, or a declaration on a
component all break that in different ways, and `[Persistent]` breaks it from the other end by letting a
restored value land after the gates resolved. `AC-GATE-011.11` is the exception that proves it is about
the opt-in: `[Persistent(Exclude = true)]` asks for exactly what a parameter already gets.

## The consumer surfaces

- `AC-GATE-012.1` (Ubiquitous): THE SYSTEM SHALL let a test supply an `[InstantiationParameter]` value
  that is applied through the same encode and decode path a configuration uses.
- `AC-GATE-012.2` (Event-driven): WHEN a topology names instantiation-parameter values for an instance
  THE SYSTEM SHALL apply them before that block's `Configure`.
- `AC-GATE-012.3` (Ubiquitous): THE SYSTEM SHALL resolve a development host's definition view down to
  the live view for an instance's chosen parameter values, dropping every excluded service, interface
  binding and contract binding.
- `AC-GATE-012.4` (Ubiquitous): THE SYSTEM SHALL overlay an instance's chosen parameter values on each
  parameter's reported default when resolving that live view.
- `AC-GATE-012.5` (Conditional): WHERE an `[IncludedWhen]` predicate cannot be resolved while resolving that live view THE SYSTEM SHALL leave the member visible. GAP: no committed fixture declares a gate the live view cannot resolve; CTRL's pass owns the host's fail-open surface.
- `AC-GATE-012.6` (Ubiquitous): THE SYSTEM SHALL project each catalog block's
  `[InstantiationParameter]` set with its editor schema and default, and each gated interface and
  contract binding's predicate.
- `AC-GATE-012.12` (Ubiquitous): THE SYSTEM SHALL accept a JSON null in a topology's
  instantiation-parameter values for a parameter whose declared type is nullable.
- `AC-GATE-012.11` (Ubiquitous): THE SYSTEM SHALL report on each catalog parameter whether its default
  was read from an instance, so a default of null that was read is distinguishable from one that was
  never read.
- `AC-GATE-012.10` (Ubiquitous): THE SYSTEM SHALL name each catalog interface binding the way the
  binder resolves it — the binding's explicit identifier where it declares one, otherwise the interface
  name for a class-level binding and the property name joined to the interface name for a
  property-based one.
- `AC-GATE-012.7` (Ubiquitous): THE SYSTEM SHALL report each service's gate predicate in the plugin
  listing read from a packed artifact.
- `AC-GATE-012.8` (Event-driven): WHEN a topology names an instantiation parameter that is not an
  `[InstantiationParameter]` property of the instance's block type, or supplies a value that will not
  decode into that parameter's type, THE SYSTEM SHALL refuse the topology, naming every such parameter.
- `AC-GATE-012.9` (Ubiquitous): THE SYSTEM SHALL carry each instance's chosen instantiation-parameter
  values in the development host's configuration output, omitting the field for an instance that chose
  none.

`AC-GATE-012.11` exists because null answers two different questions. A catalog entry is built by
reflection over the type, and the default can only be read from an instance — which the host cannot
always construct. Reporting "unknown" as null made a parameter that must be given a value look
identical to one that has no information, and an editor fails open on the second where it should warn
about the first.

`AC-GATE-012.10` is what makes `AC-GATE-012.6` usable: a topology is authored against the catalog and
then wired by the binder, so an endpoint listed under a name the binder does not answer to is worse
than one left out — the mapping looks wired and resolves to nothing.

`AC-GATE-012.1` is why a test sets a parameter through the TestKit's builder rather than by assigning
the property: the builder goes through the encode and decode path that ships, so a test exercises the
gates the way a configuration will.

The live view's fail-open is a deliberate exception to fail-closed, and the only one on this page. An
editor that hid every member whose gate it could not judge would remove wiring the operator still
needs; the running block is the strict gate, so the editor stays open and logs that it did.
`AC-GATE-012.8` is the counterweight — what an editor *can* check, the identifier and the value's
decodability, is refused where the operator is, rather than inside an actor after the host has
reported itself started. Both halves run the block's own rule, through one shared decoder, so loader
and block cannot drift.
