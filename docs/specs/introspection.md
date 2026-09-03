---
trace: enforced
---

# The introspection document and identifier stability

What `dotnet pack` puts in a logic-block library's package, and which C# name keys each thing in it.
A library's assembly is introspected once at pack time; the JSON that falls out is the only
description of that library the cloud ever reads, and the identifiers in it are the names every
later configuration, wiring and translation is written against. Area code `INTRO`. Process:
[`../spec-process.md`](../spec-process.md).

The document is produced by `Vion.Dale.LogicBlockParser`, a tool the SDK's MSBuild targets run over
the *published* assembly. Its shape is `Vion.Contracts`' — this repository fills the fields, it does
not define them.

## The document

One document per plugin assembly: who the library is, which version, and one record per logic block.

- `AC-INTRO-001.1` (Event-driven): WHEN a plugin assembly is introspected THE SYSTEM SHALL emit one
  document carrying the package identity, the package version and one record per logic block.
- `AC-INTRO-001.2` (Event-driven): WHEN the pack supplies a package identity THE SYSTEM SHALL report
  it as the document's package identity, and SHALL report the plugin assembly's simple name where no
  identity is supplied or the supplied one is blank.
- `AC-INTRO-001.3` (Event-driven): WHEN the plugin assembly declares an informational version THE
  SYSTEM SHALL report it with any build-metadata suffix removed.
- `AC-INTRO-001.4` (Ubiquitous): THE SYSTEM SHALL report an empty annotation map at document level.
- `AC-INTRO-001.5` (Ubiquitous): THE SYSTEM SHALL introspect every non-abstract logic block of the
  plugin assembly, ordered by full type name.
- `AC-INTRO-001.6` (Ubiquitous): THE SYSTEM SHALL write the document with camelCase member names and
  camelCase annotation keys, and every enum value as its member name.

`AC-INTRO-001.2` is the rule the whole naming scheme hangs from. The package identity is the
namespace every identifier below is read under, so it has to be the id the package is *registered*
as — the one `dotnet pack` writes into the nuspec — and not the assembly's name, which a project may
set independently. MSBuild defaults the package id to the assembly name, so a library that sets
neither is unaffected. A blank supplied identity is treated as absent, because an unset MSBuild
property expands to the empty string.

`AC-INTRO-001.4` describes a slot this producer never fills. It is stable and empty; a consumer that
read its absence as a version signal would be wrong.

`AC-INTRO-001.6` is what makes the annotation keys below wire-visible in camelCase even though the
producer spells them in Pascal case: the serializer camel-cases dictionary keys at this boundary.

## When the introspection refuses

The document is built at pack time, so every refusal here is a build failure, and a build failure is
much cheaper than an artifact that is quietly incomplete.

- `AC-INTRO-002.1` (Event-driven): WHEN a non-abstract logic block of the plugin assembly is not
  registered in the plugin's service registration THE SYSTEM SHALL fail the run naming that type, and
  SHALL write no document.
- `AC-INTRO-002.3` (Event-driven): WHEN the plugin path or the output path is missing or empty THE
  SYSTEM SHALL fail the run and print its usage.
- `AC-INTRO-002.4` (Event-driven): WHEN the named plugin assembly does not exist THE SYSTEM SHALL
  fail the run, naming the path it looked at.
- `AC-INTRO-002.5` (Ubiquitous): THE SYSTEM SHALL accept its options in any position and
  case-insensitively, and SHALL treat neither an option nor an option's value as a positional
  argument.
- `AC-INTRO-002.6` (Event-driven): WHEN the development-only exclusion is requested THE SYSTEM SHALL
  leave out every logic block that binds a development-only contract and SHALL name each excluded
  block and its bindings on standard output.
- `AC-INTRO-002.7` (Ubiquitous): THE SYSTEM SHALL prefix every such notice with a stable marker.
- `AC-INTRO-002.9` (Ubiquitous): THE SYSTEM SHALL apply the development-only exclusion to the document
  alone, so an excluded block is still introspected and every refusal above still applies to it.
- `AC-INTRO-002.8` (Event-driven): WHEN introspecting a logic block throws THE SYSTEM SHALL report
  the originating exception rather than a reflection wrapper.

`AC-INTRO-002.1` is why a pack either produces a complete document or none: an artifact missing one
block uploads without complaint and is discovered by that block's absence in the dashboard. Register
every concrete block, or make the type abstract. `AC-INTRO-002.9` is the boundary beside it — the
development-only filter decides what the document *carries*, never what the run *checks*, so a block
the filter would drop must still be registered and still fails the pack if introspecting it throws.

`AC-INTRO-002.6` keeps bench surface off the wire. A simulator binds a provider face — the inverse of
a hardware contract — and no such block has a production deployment, so the artifact the cloud reads
leaves it out while the assembly is packed unchanged. The judgement is on the declaration alone: a
gate cannot argue a block back in. `AC-INTRO-002.7`'s marker is what lets `dale upload`, which
captures the pack output rather than inheriting it, repeat the notice to its own user.

`AC-INTRO-002.8` is not cosmetic. The block's configuration is reached by reflection, so without the
rethrow every refusal on this page would reach the author as "Exception has been thrown by the target
of an invocation."

## Determinism

The document is a build output that lands in version control and in diffs, so two runs over one
assembly must produce one file.

- `AC-INTRO-003.1` (Ubiquitous): THE SYSTEM SHALL emit a byte-identical document for repeated runs
  over one assembly.
- `AC-INTRO-003.2` (Ubiquitous): THE SYSTEM SHALL report the status-mapping, enum-label and
  struct-field maps in ordinal key order.
- `AC-INTRO-003.3` (Ubiquitous): THE SYSTEM SHALL report a service's properties and measuring points
  in base-to-derived declaration order.

`AC-INTRO-003.2` and `AC-INTRO-003.3` are what a byte-identical document rests on. The maps are built
as immutable dictionaries and .NET randomizes string hashing per process; reflection promises no member
order at all. Neither is stable without being made so. The sort belongs to the producer rather than to
an export boundary, so every reader of the document gets the canonical form.

## A block's identity and its annotations

- `AC-INTRO-004.1` (Ubiquitous): THE SYSTEM SHALL report a logic block's identity as its CLR full
  type name, so a nested block's identity carries the CLR nesting separator.
- `AC-INTRO-004.2` (Ubiquitous): THE SYSTEM SHALL report every other type name in the document in
  display form, with the nesting separator written the way source spells it.
- `AC-INTRO-004.3` (Event-driven): WHEN a logic block declares a name THE SYSTEM SHALL report it as
  the block's default-name annotation, and SHALL report its icon and its groups under their own.
- `AC-INTRO-004.4` (Unwanted): WHERE a logic block's declared name, icon or group set is empty THE
  SYSTEM SHALL omit that annotation.
- `AC-INTRO-004.5` (Event-driven): WHEN a logic block declares no annotations at all THE SYSTEM SHALL
  report an empty annotation map.
- `AC-INTRO-004.6` (Ubiquitous): THE SYSTEM SHALL carry a display string into the document verbatim,
  whatever characters it contains.

`AC-INTRO-004.1` and `AC-INTRO-004.2` are a deliberate split, not an inconsistency. A block's type
name is the string a host **loads the type by**, so it is the CLR's spelling or it resolves nothing;
every other type name in the document is descriptive, and is written the way a reader spells it in
source. Nested logic blocks were never a design intent, but they work, and the split is what makes
them work.

`AC-INTRO-004.6` is why nothing here trims, escapes or normalises an authored string. Display strings
are translation *sources*: the cloud pins staleness on their exact bytes, so rewriting one would
silently mark every existing translation of it out of date.

## Services and their identifiers

Every logic block has one root service and one further service per component property that carries a
service surface. Their identifiers are C# names, and there is no attribute that overrides them.

- `AC-INTRO-005.1` (Ubiquitous): THE SYSTEM SHALL identify a logic block's root service by the
  block's short class name.
- `AC-INTRO-005.2` (Ubiquitous): THE SYSTEM SHALL identify a component service by the name of the
  property holding it.
- `AC-INTRO-005.3` (Ubiquitous): THE SYSTEM SHALL distinguish service identifiers case-sensitively.
- `AC-INTRO-005.4` (Ubiquitous): THE SYSTEM SHALL report on each service the service-interface types
  its bound members came through, without repetition.
- `AC-INTRO-005.5` (Ubiquitous): THE SYSTEM SHALL identify a service property and a measuring point
  by its C# property name.

`AC-INTRO-005.1`, `AC-INTRO-005.2` and `AC-INTRO-005.5` are the identifiers with **no** decoupling
knob, and that is a decision rather than a gap: a service's and a member's C# names *are* their
identifiers, and no override attribute is planned. Renaming one is therefore a rename of the key the
cloud stores that member's translations under. The two binding attributes below are the exception.

## A member on both publication streams

A single C# property may carry both a service-property and a measuring-point declaration: live state
and a charted series off one value. The two are independent streams of one member.

- `AC-INTRO-006.1` (Ubiquitous): THE SYSTEM SHALL report a member declaring both as one identifier in
  each of the two member lists, carrying one title and one description.
- `AC-INTRO-006.2` (Ubiquitous): THE SYSTEM SHALL report the measuring-point kind on the measuring
  point's schema and not on the service property's.
- `AC-INTRO-006.3` (Ubiquitous): THE SYSTEM SHALL report a measuring point's kind as its wire token.
- `AC-INTRO-006.4` (Ubiquitous): THE SYSTEM SHALL report a member declaring both streams with the same
  presentation document on each.

`AC-INTRO-006.1` is what makes such a member **one translatable member**: one title key, one
description key, not two.

`AC-INTRO-006.2` is the same per-stream discipline `AC-EMIT-013.4` states for the emission policy,
one document over. The kind describes the series, so it belongs to the series' document; reported on
the property's as well, a client badges a writable service property with a policy that describes the
chart beside it. `AC-INTRO-006.4` is the other half of that split: the *presentation* is the member's,
not a stream's, so both documents carry it — one label, one group, one visibility predicate, whichever
surface renders the member.

## The schema document

Every member carries a `schema` — the introspection contract makes it mandatory, so a consumer never
null-checks it. What rides on it is the member's data shape plus the annotations a schema has a slot
for.

- `AC-INTRO-007.1` (Ubiquitous): THE SYSTEM SHALL report a schema for every service property and every
  measuring point.
- `AC-INTRO-007.2` (Ubiquitous): THE SYSTEM SHALL take a member's title, description, unit and string
  format from either of its emission declarations, preferring the service property's, and SHALL report
  them on the member's own schema — on an array member's root and not on its element schema.
- `AC-INTRO-007.3` (Ubiquitous): THE SYSTEM SHALL report a declared bound only where it is finite.
- `AC-INTRO-007.4` (Ubiquitous): THE SYSTEM SHALL report a member as read-only when it is a measuring
  point without a service property, when its implementing property has no public setter, when its
  declaration opts in, or when it is an instantiation parameter.
- `AC-INTRO-007.5` (Ubiquitous): THE SYSTEM SHALL report write-only from a member's service-property
  declaration alone.
- `AC-INTRO-007.6` (Ubiquitous): THE SYSTEM SHALL build a member's schema from a bool, a byte, a
  short, a ushort, an int, a uint, a long, a float, a double, a date-time, a duration, a globally
  unique identifier, a string, an enum, a flat readonly record struct, an immutable array of those, or
  a nullable of any value type or string, and SHALL refuse any other type naming it.
- `AC-INTRO-007.7` (Ubiquitous): THE SYSTEM SHALL report a declared minimum above a declared maximum
  unchanged.
- `AC-INTRO-007.8` (Ubiquitous): THE SYSTEM SHALL report an authored title, description, unit or
  string format that is empty as an empty value rather than omitting it.
- `AC-INTRO-007.9` (Ubiquitous): THE SYSTEM SHALL report the format a member's CLR type implies — a
  date-time for a `DateTime`, a duration for a `TimeSpan`, a unique identifier for a `Guid` — at every
  depth its schema reaches.

`AC-INTRO-007.3` reads as a formatting rule and is a durability rule. The two infinities are the
declaration's own defaults — one per bound — so "finite" is the same test as "declared", and it closes
the two values that are neither: the other infinity, and a value that is not a number. Both are
accepted by the compiler and judged by no diagnostic, and JSON can carry neither, so one such bound
used to abort the whole document with an error naming no member and no block.

`AC-INTRO-007.4`'s fourth clause is `AC-GATE-010.3`'s, restated here only because it lands in the same
field. `AC-INTRO-007.5` has no matching clause: a measuring point is published and never written, so a
write-only measuring point has no meaning.

`AC-INTRO-007.9` is where the schema stops being a transcription of what the author wrote: some formats
are the CLR type's own, so they hold at every depth a schema reaches — a `DateTime` field inside a struct
inside an array carries the same format token as one declared at the top. `AC-INTRO-008.9` is the same
idea for a nullable struct member: only the member's own type widens, so a client that has learned the
non-nullable shape has learned both.

`AC-INTRO-007.7` and `AC-INTRO-007.8` are the same boundary from two sides. The document reports what
the author declared; it is an editor that renders bounds and a renderer that renders strings, and
neither the producer nor the block enforces either. The one exception is `AC-INTRO-007.3`, where the
declared value cannot be carried at all.

The type set in `AC-INTRO-007.6` is also a compile-time rule — the analyzers refuse the same set — so
the refusal here is the pack-path backstop for an assembly built without them.

## Enums and structs

The two composite shapes are also the two that carry identity: their schema title is a CLR type name,
which is what a cloud keys that type's labels by.

- `AC-INTRO-008.1` (Ubiquitous): THE SYSTEM SHALL report an enum as its short type name and its
  member-name strings, never its ordinals.
- `AC-INTRO-008.2` (Ubiquitous): THE SYSTEM SHALL report a struct as its short type name and its
  positional-constructor parameters in declaration order, each keyed camelCase, requiring only the
  non-nullable ones and permitting no additional members.
- `AC-INTRO-008.3` (Event-driven): WHEN a struct declares more than one constructor THE SYSTEM SHALL
  enumerate its fields from the one with the most parameters.
- `AC-INTRO-008.4` (Event-driven): WHEN a struct used as a member's type has no positional constructor
  THE SYSTEM SHALL refuse the introspection naming that struct.
- `AC-INTRO-008.5` (Ubiquitous): THE SYSTEM SHALL report struct-field annotations, member labels and
  severities for the fields of a member's own struct type and no deeper.
- `AC-INTRO-008.6` (Ubiquitous): THE SYSTEM SHALL report a struct field's authored title, description,
  unit, string format, bounds and write-only flag on that field's own schema, and SHALL omit a field
  that declares none.
- `AC-INTRO-008.7` (Ubiquitous): THE SYSTEM SHALL read the declared nullability of every reference
  position of a member's type — the member itself and each element it nests — from the compiler-emitted
  annotation, falling back to the declaring constructor's and then the declaring type's.
- `AC-INTRO-008.8` (Ubiquitous): THE SYSTEM SHALL accept a struct-field declaration on a positional
  constructor parameter and nowhere else.
- `AC-INTRO-008.9` (Ubiquitous): THE SYSTEM SHALL report the same field schemas for a nullable struct
  member as for a non-nullable one of the same type, widening only the member's own type.

`AC-INTRO-008.1` keeps ordinals off the wire so reordering an enum's members is not a data change.

`AC-INTRO-008.2`'s requiring rule is what stops a legitimately absent value being rejected: a nullable
field encodes as null outbound and may be omitted inbound.

`AC-INTRO-008.5` is the flat-struct rule seen from the document. A struct whose own field is a struct
is refused at compile time, so the walk that stops one level down is what a conforming library never
reaches; the rule is stated because the pack path is what an assembly built without the analyzers hits.

`AC-INTRO-008.7` is the difference between a `string?` that publishes and one that throws: the outbound
codec refuses a null where the schema says none is allowed, and the whole property publish is dropped
with it. The annotation the compiler emits is one flag per position of a walk of the member's type, so
an array's element is read the same way its member is, at any nesting depth — a member and its elements
are one declaration, not a declaration with an untyped interior.

## The presentation document

`presentation` is opaque passthrough: nothing between the producer and the renderer parses it. That is
what lets a key ride it without a contracts-model change, and it is why the keys below are a contract
even though no model declares them.

- `AC-INTRO-009.1` (Event-driven): WHEN a member has no presentation to report THE SYSTEM SHALL report
  no presentation document rather than an empty one.
- `AC-INTRO-009.2` (Ubiquitous): THE SYSTEM SHALL report a member's display name, group, order,
  importance, UI hint, decimals, format and visibility predicate as declared.
- `AC-INTRO-009.3` (Ubiquitous): THE SYSTEM SHALL treat the integer sentinel as unset for order and
  for decimals, reporting neither.
- `AC-INTRO-009.4` (Unwanted): WHERE a member's importance is the default THE SYSTEM SHALL omit it.
- `AC-INTRO-009.5` (Event-driven): WHEN a member is declared a status indicator and declares no
  explicit UI hint THE SYSTEM SHALL report the status-indicator hint.
- `AC-INTRO-009.6` (Ubiquitous): THE SYSTEM SHALL accept a presentation declaration on a service
  property or a measuring point and nowhere else.

`AC-INTRO-009.1` matters to a consumer that null-checks before reading a key: an empty object and an
absent one are different answers.

`AC-INTRO-009.3` exists because attribute parameters cannot be nullable, so a sentinel is the only way
to say "not specified" — and an author who writes the sentinel gets the same answer as one who omits
the knob. `AC-INTRO-009.4` is the same economy for a value every member has.

`AC-INTRO-009.5` lets a dashboard detect a status tile from an explicit hint rather than infer it from
the presence of severities, which an enum can legitimately lack.

`AC-INTRO-009.6` and `AC-INTRO-008.8` read like trivia and are the same rule: a declaration the surface
accepts in a place nothing reads is a declaration that compiles, emits nothing and warns about nothing.
Presentation is read off a property; a struct field is read off a positional constructor parameter.

The visibility predicate on this document is the **soft** sibling of config-time gating
([`config-gating.md`](config-gating.md)): a hidden member still exists, still binds and still
publishes. The predicate rides both documents of a member declaring both streams, because one
presentation feeds both. Its parse and type discipline is a compile-time matter, not this document's:
the predicate is carried verbatim, and a predicate naming a member that does not exist reaches the
wire exactly as written.

## Labels and severities

Neither has a slot on a JSON schema, so both ride the presentation document.

- `AC-INTRO-010.1` (Event-driven): WHEN a member is declared a status indicator THE SYSTEM SHALL
  report the severity of each member of its enum type as its lower-cased wire token, and SHALL report
  none otherwise.
- `AC-INTRO-010.2` (Ubiquitous): THE SYSTEM SHALL read severities through a nullable enum and SHALL
  NOT read them through an array of enum.
- `AC-INTRO-010.3` (Ubiquitous): THE SYSTEM SHALL report each declared enum-member label without
  requiring any flag, reading through a nullable enum and through an array of enum, and SHALL omit an
  unlabelled member and every member whose type is not an enum.
- `AC-INTRO-010.4` (Ubiquitous): THE SYSTEM SHALL report a declared label whatever its value,
  including an empty one, one repeated on another member, and one on a combined flags member.

`AC-INTRO-010.1`'s gate is not decoration: the same flag routes the member to a status tile, and
severities without a tile have nothing to colour. A struct field has no tile, which is why the same
severities are ungated one level down (`AC-INTRO-011.2`).

`AC-INTRO-010.2` is a boundary an author is told about at compile time: a status indicator must be an
enum or a nullable enum, and the diagnostic for an array of enum says the mappings will be ignored.
The document agrees with the diagnostic.

`AC-INTRO-010.3`'s omission is the important half. An unlabelled enum member is still translatable —
its source string is the raw member name — so the label map is an override list, not a catalogue, and
adding a label later changes the source string rather than the key.

## Struct-field presentation

Three things authored on a struct field cannot travel inline: a title on a field whose own schema
title is a type identity, and the field's enum labels and severities.

- `AC-INTRO-011.1` (Ubiquitous): THE SYSTEM SHALL report a struct field's authored title beside the
  schema only where that field's own schema title carries type identity, leaving a scalar field's
  title inline.
- `AC-INTRO-011.2` (Ubiquitous): THE SYSTEM SHALL report a struct field's enum-member labels and
  severities without requiring the status-indicator flag.
- `AC-INTRO-011.3` (Event-driven): WHEN a member's struct has nothing to carry beside its schema THE
  SYSTEM SHALL report no struct-field presentation at all.
- `AC-INTRO-011.4` (Ubiquitous): THE SYSTEM SHALL leave a member's own presentation intact beside the
  struct-field presentation it carries.
- `AC-INTRO-011.5` (Ubiquitous): THE SYSTEM SHALL report an authored struct-field title that is empty
  rather than omitting it.

`AC-INTRO-011.1` is one rule at two levels: wherever a schema title is an identity, the authored title
goes to the presentation document instead of being silently dropped. Duplicating a scalar field's
title into both would leave two sources with no rule about which wins.

`AC-INTRO-011.3` is what keeps `AC-INTRO-009.1` true — an always-present struct-field node would stop
an otherwise-empty presentation from being absent.

`AC-INTRO-011.5` is `AC-INTRO-007.8` at the one place a title changes documents. An authored empty title
is still authored, and a length test here would make the re-routed title the single string in the
document that is not carried as written.

## Where a member's schema and its presentation come from

A member bound through a service interface has two declarations, and they own different halves.

- `AC-INTRO-012.1` (Event-driven): WHEN a member's schema title carries its own type identity THE
  SYSTEM SHALL route the authored title to the presentation display name instead.
- `AC-INTRO-012.2` (Ubiquitous): THE SYSTEM SHALL build an interface-bound member's schema from the
  interface's declaration and its presentation and runtime from the implementing property.
- `AC-INTRO-012.3` (Ubiquitous): THE SYSTEM SHALL take each presentation field from the implementing
  property where it sets one and from the interface otherwise.
- `AC-INTRO-012.4` (Ubiquitous): THE SYSTEM SHALL decide a member's writability from the implementing
  property.

`AC-INTRO-012.2` and `AC-INTRO-012.3` are what let a family of blocks share one interface: the
interface owns the data contract they have in common, the class owns what its own instance looks like,
and the merge is per field so a class overrides a label without restating a group.

`AC-INTRO-012.4` follows from what a set-value request actually writes. The interface declares intent;
the implementing property is the binding target, so its accessors decide.

## The runtime document

`runtime` is opaque passthrough like `presentation` — the codec and the mesh never read it.

- `AC-INTRO-013.1` (Ubiquitous): THE SYSTEM SHALL report a member as persistent when it opts in
  without excluding itself, and SHALL report no runtime document where there is nothing to report.

The document's other two runtime concerns are stated elsewhere and cited here rather than re-minted: a
member's emission policy is `AC-EMIT-013.1`–`AC-EMIT-013.6`, and an instantiation parameter's marker
and default are `AC-GATE-010.4` and `AC-GATE-010.5`.

## Endpoint identifiers

A contract binding and an interface binding each get an identifier. These are the two identifiers with
a decoupling knob, and the ones a topology names.

- `AC-INTRO-014.1` (Ubiquitous): THE SYSTEM SHALL identify an interface binding by its declared
  identifier, and where none is declared by the holding property's name joined to the interface's name
  for a property-based binding and by the bare interface name for a class-implemented one.
- `AC-INTRO-014.2` (Ubiquitous): THE SYSTEM SHALL identify a contract binding by its declared
  identifier, and by the holding property's name where none is declared.
- `AC-INTRO-014.3` (Event-driven): WHEN a binding declares an identifier that is empty or blank THE
  SYSTEM SHALL refuse the introspection naming the member.
- `AC-INTRO-014.4` (Event-driven): WHEN two bindings of one logic block and of the same kind resolve
  to one identifier THE SYSTEM SHALL refuse the introspection naming both declarations.
- `AC-INTRO-014.5` (Ubiquitous): THE SYSTEM SHALL mint contract-binding and interface-binding
  identifiers in separate namespaces, each distinguished case-sensitively.

The declared identifier is the decoupling knob: pin it and the C# member can be renamed without
minting a new key. `AC-INTRO-014.1`'s derived forms are what a topology names when it is not pinned,
which is why the property name is part of it — one property implementing two interfaces has to yield
two endpoints.

`AC-INTRO-014.3` and `AC-INTRO-014.4` are one posture in two shapes: an identifier addresses exactly
one endpoint, so a blank one addresses nothing and a repeated one addresses two things. A blank
identifier would reach the document as an endpoint named by the empty string — unwireable, invisible to
`dale list`, and a translation key with an empty part; a repeated one would leave the artifact carrying
one endpoint while the block binds two, with a relation derived for the first naming the second. Both
are refused where the binder mints them, so `dotnet pack` and a starting block report the same thing.

`AC-INTRO-014.5` is the boundary that keeps the refusal narrow. Contract bindings and interface
bindings are separate arrays in the document and separate namespaces in the cloud's key grammar, so one
name may address one endpoint of each kind. Case is not such a boundary: `Relay` and `relay` are two
endpoints, as they are two services (`AC-INTRO-005.3`).

## What an endpoint carries

- `AC-INTRO-015.1` (Ubiquitous): THE SYSTEM SHALL report an interface binding's logic-interface type
  and its matching counterpart type by display full name.
- `AC-INTRO-015.2` (Ubiquitous): THE SYSTEM SHALL report an interface binding's default name, tags and
  multiplicity, omitting each where it is unset or default, and SHALL report its contract's name.
- `AC-INTRO-015.3` (Event-driven): WHEN a logic interface's contract declares role default names THE
  SYSTEM SHALL report this side's and the counterpart's separately, omitting each where unset.
- `AC-INTRO-015.4` (Ubiquitous): THE SYSTEM SHALL resolve a contract's declared direction to an
  inbound or an outbound arrow for the side the endpoint is on, and to none where the contract declares
  none.
- `AC-INTRO-015.5` (Ubiquitous): THE SYSTEM SHALL report a contract binding's contract type and its
  contract-type token.
- `AC-INTRO-015.6` (Ubiquitous): THE SYSTEM SHALL report a contract binding's handler-actor name
  always, and its default name, tags, multiplicity, provider-side consumer limit and development-only
  flag only where each is set or non-default.

`AC-INTRO-015.1` is what decides which endpoints may be linked at all: the platform pairs a binding to
its counterpart by the two type names, so they travel together.

`AC-INTRO-015.4` is resolved in the producer rather than in each client so two renderers cannot
disagree about which way an edge points.

`AC-INTRO-015.6` has one member outside its own omission rule. The handler-actor name is not an authored
value that may be absent — it is how the runtime addresses the actor servicing the contract — so it is
reported for every binding.

The contract-type token in `AC-INTRO-015.5` is the one identifier on this page that is **not** a
translation key. Nothing translates it; the platform matches a binding to its handler through it, and
the development host shows it beside each endpoint when a pairing is authored. Renaming one is a
breaking change for every configuration that binds it, in a way renaming a translated identifier is
not — orphaned translations degrade gracefully, an orphaned contract type does not match.

`AC-INTRO-015.3`'s two role names are translated per binding block, so two blocks binding one contract
translate the same role name twice. The duplication is deliberate: there is no key aliasing anywhere in
this scheme.

## Service relations

A contract may declare that binding it means something about the two services at its ends. Each such
declaration becomes one half on each bound endpoint's owning service.

- `AC-INTRO-016.1` (Event-driven): WHEN a bound endpoint's contract declares a service relation THE
  SYSTEM SHALL report one relation half per declaration on the service owning that endpoint, carrying
  the endpoint's own identifier.
- `AC-INTRO-016.2` (Ubiquitous): THE SYSTEM SHALL report a half as outward where the endpoint's
  interface is the one the declaration names, and as inward otherwise.
- `AC-INTRO-016.3` (Event-driven): WHEN an endpoint's holding property carries no service surface THE
  SYSTEM SHALL report no relation half for it.
- `AC-INTRO-016.4` (Ubiquitous): THE SYSTEM SHALL report a relation half with an empty annotation map.
- `AC-INTRO-016.5` (Event-driven): WHEN a service relation names an interface that is neither side of
  its contract, or is declared on a class carrying no contract declaration, THE SYSTEM SHALL refuse the
  introspection.
- `AC-INTRO-016.6` (Ubiquitous): THE SYSTEM SHALL report the halves of a gated component in the
  definition view and none of them for an instance whose configuration excludes it.
- `AC-INTRO-016.7` (Ubiquitous): THE SYSTEM SHALL report on each relation half the logic-interface type
  of the endpoint it was derived from.
- `AC-INTRO-016.8` (Event-driven): WHEN a component property holds null THE SYSTEM SHALL report no
  relation half for its endpoints, having reported no service for it to hang on.

`AC-INTRO-016.1` carries a load-bearing detail: the half is registered by the same code path that just
minted the endpoint's identifier, so a relation's endpoint reference can never diverge from the
endpoint's actual wiring identifier. There is no second resolution rule.

`AC-INTRO-016.3` and `AC-INTRO-016.8` are the two cases where a declaration produces nothing, and they
are the same reason twice: a relation half hangs on a service, so no service means no half. A component
with no service surface has none by declaration, and the omission is reported at compile time; a
component property holding null has none because a service's members are enumerated off the object
(`AC-GATE-007.7`), and nothing reports that at all — a gated component must exist by the time the
configuration runs. The endpoint is described in both cases: its identity is type-level.

`AC-INTRO-016.7` is what makes a half readable without resolving its endpoint first — the type on the
half is the endpoint's own, put there by the code path that minted the endpoint's identifier.

`AC-INTRO-016.6`'s definition half is `AC-GATE-006.1`'s — the definition view describes the type, so
it binds the full set. The relation half following the endpoint is this page's.

## The pack-time hook

The SDK's MSBuild targets are what make all of the above happen without a library author doing
anything.

- `AC-INTRO-017.1` (Event-driven): WHEN a logic-block library is packed THE SYSTEM SHALL publish the project, run the introspection over the published assembly supplying the project's package id and excluding development-only blocks, and fail the pack if that run fails. GAP: a targets test is a pack-and-consume round trip, which nothing in this repository has a harness for; the parser half it drives is covered by the document and refusal criteria above.
- `AC-INTRO-017.2` (Ubiquitous): THE SYSTEM SHALL write the document beside the published output under the project's name and pack the whole published folder. GAP: as `AC-INTRO-017.1`.
- `AC-INTRO-017.3` (Ubiquitous): THE SYSTEM SHALL skip the introspection entirely for a project that opts out. GAP: as `AC-INTRO-017.1`.
- `AC-INTRO-017.4` (Ubiquitous): THE SYSTEM SHALL supply the source generator and analyzers to every
  consuming project.

Publishing first is not an optimisation. The introspection loads the plugin into its own assembly load
context and needs the plugin's whole dependency closure in one directory, which only a publish
produces. The opt-out is what lets a project that references the SDK without being a logic-block
library — a development host, a test host — still pack.

## Identifier stability, in one place

Everything a logic block declares carries two kinds of string: an **identifier** (a C# name, or a
declared `Identifier =` value) and a **display string** (a title, a description, a default name, an
enum label). The cloud lets integrators translate the display strings, and it keys those translations
by the identifiers.

The keys are *derived*, not declared. Nothing in a library names them, no attribute overrides them,
and this SDK has no translation feature to opt into. So:

> **Renaming an identifier mints a new key.** Translations authored against the old one are orphaned —
> they keep existing and stay visible, but nothing re-attaches them. Re-attaching is manual, per
> language.

Nothing breaks: a string with no translation falls back to the string compiled into the package. The
cost of a rename is re-translation work, in every language the library's owner authors.

| Display string | Keyed by | Orphaned by |
|---|---|---|
| A block's name | the block's identity (`AC-INTRO-004.1`) | renaming the class, **or moving it to another namespace** |
| A member's title and description | the block, its service identifier, and the member's identifier (`AC-INTRO-005.1`, `AC-INTRO-005.2`, `AC-INTRO-005.5`) | renaming the property, its service, or the class |
| A contract binding's default name | the binding's identifier (`AC-INTRO-014.2`) | renaming the property while leaving the identifier undeclared |
| An interface binding's name and role names | the binding's identifier (`AC-INTRO-014.1`, `AC-INTRO-015.3`) | renaming the property, the interface, or the class |
| An enum label | the enum's short type name and the member name (`AC-INTRO-008.1`) | renaming the enum type or a member — its namespace is not part of the key |
| A struct field's title and description | the struct's short type name and the camelCase field name (`AC-INTRO-008.2`) | renaming the struct type or a constructor parameter |
| A custom group label | the group-key string itself (`AC-INTRO-009.2`) | editing the key |
| *all of the above* | the library's package identity (`AC-INTRO-001.2`) | changing the package id |

Three consequences worth knowing before renaming anything:

- A member on both publication streams is **one** translatable member: one title key, one description
  key (`AC-INTRO-006.1`).
- Enum members are cataloged **exhaustively**. A member with no label is translatable too, with its raw
  member name as the source string, so adding a label later changes the source string and not the key
  (`AC-INTRO-010.3`).
- Well-known group keys are translated by the dashboard itself. Only custom keys resolve through a
  library's own translations.

Changing the package identity re-namespaces every key in the library, which makes it the single most
expensive rename available. The identity is the nuspec package id (`AC-INTRO-001.2`) — the id the
platform registers the library under — so it is that property, and not the assembly name, that a rename
has to leave alone. The exact key grammar belongs to the cloud platform and is not part of this SDK's
contract; the durable rule is the one above — the identifier goes in, the key comes out.

`dale list` prints the block identities and the service, member, contract and interface identifiers as
the introspection emits them, which is the cheapest way to see what a rename would cost. It lists every
block the assembly declares, development-only ones included: it runs the introspection without the
exclusion `AC-INTRO-002.6` describes, so its listing is the declared surface rather than the packed
artifact's.

## Who reads the document

Four consumers, and what each depends on:

- **The cloud** reads the whole document at upload: it catalogs the translatable strings, stores the
  presentation and runtime documents opaquely, and resolves a configuration against the identifiers.
  It is the reason every rule on this page about a *field's value* is a wire rule.
- **`dale list` and `dale build`** read it through a hand-maintained mirror of the contracts types,
  every member optional, so the CLI can read a document produced by an older or newer SDK than its own.
  They run the introspection themselves rather than reading a packed artifact, and without the
  development-only exclusion, so what they list is the declared surface.
- **The development host** reads a member's writability from its schema and an instantiation
  parameter's marker and default from its runtime document, and passes the presentation document
  through untouched.
- **A dashboard** renders a member from the presentation document alone — the display name, group,
  order, importance, UI hint, decimals, format, visibility predicate, status mappings, enum labels and
  struct-field presentation. That every one of those keys has a live reader is what makes an opaque
  node a contract rather than a bag.
