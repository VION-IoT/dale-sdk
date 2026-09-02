---
trace: enforced
---

# Emission policy

How a logic block's service properties and measuring points reach the outside world: which changes
are published, which are suppressed, which are held and released later, and what the introspection
document reports about all of it. Area code `EMIT`. Process:
[`../spec-process.md`](../spec-process.md).

A member's **emission policy** is the three knobs it declares on `[ServiceProperty]` or
`[ServiceMeasuringPoint]` — `MinInterval` (the minimum spacing between two published values,
`"250ms"` by default), `MinChange` (an optional deadband, absent by default) and `Immediate` (off by
default). The policy governs the **outbound** direction only: how a block republishes its own state.
A write *into* a writable member is always forwarded.

A single C# property may declare **both** attributes. It then publishes to two streams — its own
retained topic each — and this page treats them as two members throughout.

## When the policy applies

Throttling and a controllable clock do not mix: a test that advances time in jumps would silently
throttle its own assignments. So the policy is decided once, from the clock the block was given.

- `AC-EMIT-001.1` (State-driven): WHILE a logic block runs on a clock that is not controllable, THE
  SYSTEM SHALL apply each bound member's declared emission policy.
- `AC-EMIT-001.2` (State-driven): WHILE a logic block runs on a controllable clock and no
  emission-policy override is registered, THE SYSTEM SHALL publish every observed change ungated.
- `AC-EMIT-001.3` (Event-driven): WHEN an emission-policy override is registered in a logic block's
  service provider, THE SYSTEM SHALL apply each bound member's declared emission policy whatever the
  clock.
- `AC-EMIT-001.4` (Ubiquitous): THE SYSTEM SHALL treat a clock as controllable when, and only when, it
  exposes a public instance method named `Advance` taking a `TimeSpan` and returning nothing.

The override is what `Vion.Dale.Sdk.TestKit`'s `WithEmissionPolicy(EmissionPolicyMode.FromAttributes)`
registers, and it is the only way to exercise throttling deterministically.

`AC-EMIT-001.4` is structural rather than nominal so the SDK needs no reference to the test-only clock
package: any clock a test can wind forward is recognised by the method it offers, whoever wrote it.

## Which knobs govern a member

- `AC-EMIT-002.1` (Ubiquitous): THE SYSTEM SHALL gate a member's service-property stream and its
  measuring-point stream independently, each from the knobs declared on its own attribute.
- `AC-EMIT-002.2` (Unwanted): WHERE a member's attribute for one stream declares no emission knobs
  THE SYSTEM SHALL apply the knob defaults to that stream, and SHALL NOT apply knobs declared on the
  member's attribute for the other stream.
- `AC-EMIT-002.3` (Event-driven): WHEN the implementing property declares a stream's emission
  attribute THE SYSTEM SHALL read that stream's knobs from it, and otherwise SHALL read them from the
  `[ServiceInterface]` property the member is bound through.
- `AC-EMIT-002.4` (Event-driven): WHEN neither the implementation nor the interface declares a stream's emission attribute THE SYSTEM SHALL publish that stream's changes ungated. GAP: no in-repo binding omits one.
- `AC-EMIT-002.5` (Ubiquitous): THE SYSTEM SHALL search for a member's custom change threshold in the
  assembly that declares the property that stream's knobs were read from.

`AC-EMIT-002.2` is why a dual-annotated member needs its measuring point's interval written out when
that stream should be slower than the default: the property's interval beside it does not carry over.

`AC-EMIT-002.3` lets a family of blocks sharing a `[ServiceInterface]` declare its policy once, the
same way it already declares its schema once — and `AC-EMIT-002.5` follows it, so a deadband declared
on an interface resolves its threshold from the interface's own library rather than from the block's.

## A policy that cannot work fails at start

A deadband that resolves to nothing is indistinguishable, at runtime, from one that is working. Both
of the criteria below have a compile-time counterpart under *Authoring diagnostics*; these are what
happens when that gate was suppressed or bypassed.

- `AC-EMIT-003.1` (Event-driven): WHEN a member sets `MinChange` and no change threshold resolves for
  its value type THE SYSTEM SHALL fail block initialization, naming the member, the service, the value
  type, the searched assembly and the remedy.
- `AC-EMIT-003.2` (Ubiquitous): THE SYSTEM SHALL fail that initialization whether or not the emission
  policy is active for the block.
- `AC-EMIT-003.3` (Event-driven): WHEN a member's `MinInterval` is not a valid duration THE SYSTEM
  SHALL fail block initialization.
- `AC-EMIT-003.4` (Event-driven): WHEN a member's `MinChange` cannot be read by the deadband resolved
  for its value type THE SYSTEM SHALL fail block initialization.

`AC-EMIT-003.4` is why the token is read once, when the member's policy is built: a deadband that
throws on the first value that moves would fault the block mid-run, long after the declaration that
caused it. A deadband whose format is its own to define reads nothing here, which is the line
`DALE035` draws at compile time.

## The decision for one value

Every offered value is decided by the first rule below that applies.

### 1. It carries no news

- `AC-EMIT-004.1` (Event-driven): WHEN an offered value equals the value last emitted on that stream
  THE SYSTEM SHALL suppress it, whatever the member's knobs are.
- `AC-EMIT-004.2` (Ubiquitous): THE SYSTEM SHALL compare an `ImmutableArray<T>` value by content,
  recursing into nested arrays.
- `AC-EMIT-004.3` (Ubiquitous): THE SYSTEM SHALL treat an absent value on exactly one side of that
  comparison as a change.

The dedup floor runs ahead of every other rule, `Immediate` included. `AC-EMIT-004.2` exists because
`ImmutableArray<T>` — the only collection shape a service element may take — implements equality as
reference equality of its backing array, so a table rebuilt each control cycle would otherwise
republish forever, carrying nothing. A member typed `ImmutableArray<T>` therefore needs no `MinChange`
to stay quiet while unchanged; reach for one only when rows should also count as unchanged within a
per-field tolerance.

### 2. It is declared urgent

- `AC-EMIT-005.6` (Unwanted): WHERE a member sets `Immediate` THE SYSTEM SHALL emit every distinct
  value at once, applying neither the interval nor the deadband.

`Immediate` is the knob for a signal whose every edge matters, and for a `bool`, which has no
magnitude to deadband. It makes `MinInterval` and `MinChange` inert, which `DALE038` warns about.

### 3. It is inside the deadband

- `AC-EMIT-006.1` (Event-driven): WHEN a member sets `MinChange` and an offered value differs from the
  last emitted value by less than that threshold THE SYSTEM SHALL suppress it rather than hold it.
- `AC-EMIT-006.2` (Ubiquitous): THE SYSTEM SHALL measure a deadband against the last emitted value
  rather than the previously offered one, so a series of sub-threshold steps emits as soon as their
  accumulated difference reaches the threshold.
- `AC-EMIT-006.3` (Conditional): IF either side of a deadband comparison is absent or is not a number
  THEN THE SYSTEM SHALL treat the change as clearing the threshold.

`AC-EMIT-006.1` says *suppress*, not *hold*: a value inside the deadband must not resurface at the
next release, or the deadband would only delay the traffic it exists to remove. `AC-EMIT-006.2` is
the half authors mis-predict — a slow ramp does eventually publish, because the reference point stays
where the last published value was.

### 4. Its interval has run, or it waits

- `AC-EMIT-005.1` (Event-driven): WHEN a member has emitted no value on a stream THE SYSTEM SHALL emit
  the first offered value, applying neither the deadband nor the interval.
- `AC-EMIT-005.2` (Event-driven): WHEN a member's `MinInterval` has elapsed since its last emission on
  that stream THE SYSTEM SHALL emit the offered value at once.
- `AC-EMIT-005.3` (State-driven): WHILE a member's `MinInterval` has not elapsed THE SYSTEM SHALL hold
  the offered value until it expires, replacing any value already held.
- `AC-EMIT-005.4` (Event-driven): WHEN a held value's interval expires THE SYSTEM SHALL emit the held
  value and clear the hold.
- `AC-EMIT-005.5` (Unwanted): WHERE a member sets `MinInterval` to the disabling sentinel THE SYSTEM
  SHALL emit every value that clears the dedup floor and the deadband, holding nothing.
- `AC-EMIT-005.7` (Event-driven): WHEN a value is suppressed while an earlier value is held THE SYSTEM
  SHALL discard the held value, so what is released at the interval is the member's latest.

Throttling is leading-edge with a trailing release: the first change after a quiet period is never
delayed, and the newest value during a busy one is published when the interval runs out. The
disabling sentinel is **any duration that resolves to zero** — `"0"`, `"0ms"` and `"0s"` all configure
the same gate — and pairs with a `MinChange` to give a deadband-only policy, gated by magnitude alone,
which `DALE039` states back to the author.

`AC-EMIT-005.7` is what keeps a release honest: a suppressed value is still the member's newest, so a
held value that survived it would move the consumer away from where the member actually is.

## Deadbands

- `AC-EMIT-008.1` (Ubiquitous): THE SYSTEM SHALL provide a built-in deadband for `double`, `float`,
  `decimal`, `int`, `long` and `TimeSpan`, clearing the threshold when the magnitude of the change is
  at least the configured value.
- `AC-EMIT-008.2` (Ubiquitous): THE SYSTEM SHALL read a `TimeSpan` member's `MinChange` with the
  duration grammar.
- `AC-EMIT-008.3` (Ubiquitous): THE SYSTEM SHALL resolve a deadband on a nullable member using the
  threshold for its underlying type.

A `MinChange` is written in the format its deadband reads: an invariant-culture number for the
numeric built-ins, a duration for `TimeSpan`, and whatever a custom threshold defines for its own
type. `bool` has no magnitude and is never valid.

## The duration grammar

`MinInterval`, and a `TimeSpan` member's `MinChange`, are written in one grammar.

- `AC-EMIT-007.1` (Ubiquitous): THE SYSTEM SHALL read a duration knob as an invariant-culture number
  followed by an optional `us`, `ms`, `s`, `m` or `h` suffix, case-insensitive on the suffix, treating
  a bare number as milliseconds.
- `AC-EMIT-007.2` (Event-driven): WHEN a duration knob is empty, carries no numeric part, or carries
  an unknown unit THE SYSTEM SHALL reject it with a `FormatException` naming the token.
- `AC-EMIT-007.3` (Event-driven): WHEN a duration knob carries a negative value THE SYSTEM SHALL
  reject it.
- `AC-EMIT-007.4` (Event-driven): WHEN a duration knob names a value larger than a duration can
  represent THE SYSTEM SHALL reject it.

Both knobs the grammar serves are magnitudes — a spacing, a change size — so `AC-EMIT-007.3` closes
the reading in which a negative value would make the gate it configures unconditional instead of
restrictive. `AC-EMIT-007.4` keeps an unusable value reading as the malformed knob it is rather than
as an arithmetic fault raised from inside the parse.

## Finding a deadband for a custom type

A value type the SDK ships no built-in deadband for gets one by declaring a public
`IChangeThreshold<T>` implementation. There is no registration call: the implementation is found.

- `AC-EMIT-009.1` (Event-driven): WHEN no threshold exists for a member's value type THE SYSTEM SHALL
  search the assembly that declares the member for a non-abstract, non-generic type implementing
  `IChangeThreshold<T>` closed over that type with a parameterless constructor, and SHALL use the
  first one it finds.
- `AC-EMIT-009.2` (Event-driven): WHEN that assembly declares no such threshold THE SYSTEM SHALL
  search the other assemblies loaded in its load context that reference `Vion.Dale.Sdk`.
- `AC-EMIT-009.3` (Ubiquitous): THE SYSTEM SHALL retain a resolved deadband for the lifetime of the
  process, keyed by the member's value type, so every member of that type shares one deadband
  wherever it is declared.
- `AC-EMIT-009.4` (Event-driven): WHEN an assembly under search cannot be fully loaded THE SYSTEM SHALL search the types that did load. GAP: no in-repo fixture produces a partially loadable assembly.
- `AC-EMIT-014.1` (Ubiquitous): THE SYSTEM SHALL publish `IChangeThreshold<T>` as part of the SDK's documented public surface. GAP: pinned by the PublicApi manifest snapshot and by `DALE014`, not by a test.

`AC-EMIT-009.2` is what makes a shared foundation library work: the threshold ships in one assembly
and the `MinChange` that uses it is declared in another. It matches the visibility model `DALE034`
checks against, so a compile that passes implies a deadband that resolves — including inside a plugin
load context, because the search starts from the declaring assembly's context rather than the SDK's.

## Releasing held values

- `AC-EMIT-010.1` (Ubiquitous): THE SYSTEM SHALL track one release deadline per logic block: a hold
  due earlier replaces it, a hold due later is covered by it, and a wakeup that finds nothing due
  releases nothing.
- `AC-EMIT-010.2` (Event-driven): WHEN a flush falls due THE SYSTEM SHALL emit every member whose hold
  deadline has passed and SHALL re-arm for the earliest deadline still held.
- `AC-EMIT-010.3` (Ubiquitous): THE SYSTEM SHALL deliver a flush through the block's synchronized
  scheduling path, so a test driving virtual time observes it.
- `AC-EMIT-010.4` (Event-driven): WHEN a logic block has stopped THE SYSTEM SHALL publish nothing
  further for its members, whatever remained held.

One tracked deadline per block, not one per member: a block with fifty throttled members holds one
and re-arms as deadlines pass. Nothing is cancelled — an earlier hold arms an additional wakeup and
the one it overtook finds nothing due — so the guarantee is about what is *released*, not about how
many wakeups happen. `AC-EMIT-010.3` is what makes the trailing release assertable: a release a test
cannot observe under virtual time is a release that rots.

## The block's life

- `AC-EMIT-011.1` (State-driven): WHILE a logic block has not started THE SYSTEM SHALL emit no
  service-property or measuring-point change.
- `AC-EMIT-011.2` (Event-driven): WHEN a logic block starts THE SYSTEM SHALL emit the current value of
  every bound member and seed that member's gate.
- `AC-EMIT-011.4` (Event-driven): WHEN a logic block is told to republish its service state THE SYSTEM
  SHALL discard every gate's emitted state and publish each member's current value, even where it
  equals the value last published.
- `AC-EMIT-011.5` (Event-driven): WHEN a logic block stops THE SYSTEM SHALL publish each gated
  member's exact current value where it differs from that member's last emitted value, applying
  neither the interval nor the deadband.
- `AC-EMIT-011.6` (State-driven): WHILE the emission policy is not applied to a block THE SYSTEM SHALL
  publish nothing extra when it stops.
- `AC-EMIT-011.7` (Event-driven): WHEN a member's value cannot be read while a block stops THE SYSTEM
  SHALL still publish the remaining members' final values.
- `AC-EMIT-011.8` (Ubiquitous): THE SYSTEM SHALL publish a stopping block's final values before
  clearing its retained state.
- `AC-EMIT-011.9` (Event-driven): WHEN a service property is written from outside THE SYSTEM SHALL
  acknowledge the write with the value the block applied, whatever the member's emission policy.
- `AC-EMIT-011.10` (Event-driven): WHEN a write changes a member's value THE SYSTEM SHALL publish that
  change under the member's emission policy.

`AC-EMIT-011.9` and `AC-EMIT-011.10` are the two halves a caller has to keep apart: a write is never
refused, delayed or deadbanded, and its acknowledgement carries what the block actually applied — which
is not the written value where the member clamps or rounds it. The *state* the write produces is a
change like any other, and is gated like one, so the acknowledgement is the caller's answer and the
published state is everyone else's.

`AC-EMIT-011.2` is why a consumer subscribing at start receives state instead of waiting an interval
for it. `AC-EMIT-011.4` covers the reconnect: publishes made while the connection was down were lost,
but the gates advanced as though they had landed, so the dedup floor would suppress exactly the
re-assertion the broker needs. `AC-EMIT-011.5` is the other side of the deadband's bargain — accuracy
is traded for quiet *during* operation, so the last value a consumer keeps is exact.

## Authoring diagnostics

Six diagnostics validate a declared policy at compile time. The analyzer registry itself is the
`ANLZ` area's; what these six mean is here.

- `AC-EMIT-012.1` (Event-driven): WHEN a member sets `MinChange` and its value type has neither a
  built-in deadband nor an `IChangeThreshold<T>` implementation visible in the compilation THE SYSTEM
  SHALL report `DALE034` as an error naming the type and the built-in set.
- `AC-EMIT-012.2` (Event-driven): WHEN a member sets `MinChange` in a format its built-in deadband
  cannot read THE SYSTEM SHALL report `DALE035` as an error naming the expected format, and SHALL
  report nothing where the member's deadband is a custom one.
- `AC-EMIT-012.3` (Event-driven): WHEN a member sets `MinInterval` to a value the duration grammar
  cannot read THE SYSTEM SHALL report `DALE036` as an error.
- `AC-EMIT-012.4` (Event-driven): WHEN a member sets `MinInterval` to a positive duration below one
  millisecond THE SYSTEM SHALL report `DALE037` as a warning.
- `AC-EMIT-012.5` (Event-driven): WHEN a member sets `Immediate` together with a `MinChange` or with a
  `MinInterval` other than the default THE SYSTEM SHALL report `DALE038` as a warning naming the
  ignored knobs.
- `AC-EMIT-012.6` (Event-driven): WHEN a member sets `MinChange` while its `MinInterval` is the
  disabling sentinel THE SYSTEM SHALL report `DALE039` as information, and SHALL report nothing where
  `Immediate` is also set.
- `AC-EMIT-012.7` (Ubiquitous): THE SYSTEM SHALL apply every emission diagnostic to each emission
  attribute a member declares, and SHALL report on `MinInterval` only where the author wrote it.

`AC-EMIT-012.2` and `AC-EMIT-012.3` both reject a **negative** value, for the reason `AC-EMIT-006.2`
and `AC-EMIT-007.3` give: a negative threshold is cleared by every change and a negative interval is
already elapsed, so each would turn the knob it configures off while the member reported it on.
`AC-EMIT-012.2` declines to check a custom deadband's `MinChange` at all, because that format is the
deadband's own to define. `AC-EMIT-012.4` is a warning rather than an error because the release rides
the actor scheduler, which cannot honour a sub-millisecond interval — the value is not wrong, it is
not achievable. `AC-EMIT-012.5` compares the declared interval as a duration, the way `AC-EMIT-013.2`
does, so a knob is never called ignored on the strength of how it was spelled.

`AC-EMIT-012.7` is the rule the six share, and it is per **attribute**: a member declaring both
carries two policies, and each is validated on its own. Its second half keeps the diagnostics quiet
about an omitted `MinInterval`, which is indistinguishable from the default written out.

## What introspection reports

The introspection document carries each stream's effective policy as its `runtime.throttle` node,
which is what a dashboard renders as the member's throttle badge.

- `AC-EMIT-013.1` (Ubiquitous): THE SYSTEM SHALL report a member's effective emission policy for a
  stream in the introspection document as that stream's `runtime.throttle` carrying `minInterval`,
  `minChange` and `immediate`.
- `AC-EMIT-013.2` (Unwanted): WHERE a stream's emission policy is the default THE SYSTEM SHALL omit
  its `runtime.throttle` entirely, comparing the declared `MinInterval` as a duration rather than as a
  spelling.
- `AC-EMIT-013.3` (Unwanted): WHERE a stream's emission policy is reported THE SYSTEM SHALL carry its
  effective `minInterval` even when that is the default.
- `AC-EMIT-013.4` (Ubiquitous): THE SYSTEM SHALL report each stream's policy from the same attribute
  the gate reads it from, so a member declaring both attributes reports two independent policies.
- `AC-EMIT-013.5` (Ubiquitous): THE SYSTEM SHALL treat an empty `MinChange` as unset, in the reported
  policy as in the gate.
- `AC-EMIT-013.6` (Ubiquitous): THE SYSTEM SHALL omit `minChange` from a reported policy where the
  member declares no deadband, and `immediate` where the member does not set it.

Every member carries a policy, so a badge on all of them would say nothing — hence `AC-EMIT-013.2`.
`AC-EMIT-013.3` is its complement: once a policy *is* reported, its interval is carried whole, so a
consumer renders the badge without knowing the SDK's default. `AC-EMIT-013.4` and `AC-EMIT-013.5` are
the same rule as `AC-EMIT-002.1` and `AC-EMIT-006.1` seen from the wire — a badge that disagrees with
the gate is worse than no badge.
