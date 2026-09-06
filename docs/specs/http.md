---
trace: enforced
---

# HTTP: what a logic block gets when it talks to a server

What the SDK guarantees a logic-block author who needs to call an HTTP service — a weather API, a
device's own REST surface, a commissioning endpoint. One packable package ships it,
`Vion.Dale.Sdk.Http`, added with one call and reached through one interface. Area code `HTTP`.
Process: [`../spec-process.md`](../spec-process.md).

The package is a **thin adapter over `IHttpClientFactory`**, and almost everything below follows from
that. It composes no handler of its own, retries nothing, models no link, and wraps no exception
except one. What it does own is the hop: every callback runs on the calling block's own actor, so a
block needs no locking of its own state and no thread of its own. **It is a published package**, so
every default, exception class and ownership rule below is a contract with readers outside this
repository.

The spine is the order an author meets it: registration and its defaults, the eight members as one
family, `SendRequest` on its own, the actor hop, the error model, the refusals, the two timeout
bounds, deserialization, lifetime and disposal, serialization, headers, the surface, and the test
discipline.

Cited rather than restated: [`block-lifecycle.md`](block-lifecycle.md) for the dispatcher every
callback arrives through — `AC-LIFE-006.1` (an action runs on the block's own actor),
`AC-LIFE-006.2` (a delay beyond a real clock is refused, naming the member), `AC-LIFE-006.3` (a
dispatcher call before the block's first message is refused), `AC-LIFE-006.4` (nothing armed can be
cancelled) and `AC-LIFE-014.7` (a self-send carries neither sender nor headers);
[`plugin-loading.md`](plugin-loading.md) for what an unmarked assembly means — `AC-PLUG-005.3`,
`AC-PLUG-005.7` and `AC-PLUG-005.9`; [`analyzers.md`](analyzers.md) for the rule that judges this
package's own declarations (`AC-ANLZ-012.1`, `AC-ANLZ-012.2`); [`testkit.md`](testkit.md) for the
surface-and-arming shape this package now follows (`AC-TKIT-013.1`, `AC-TKIT-013.2`).

Not this area: the `dale` CLI's own hand-rolled HTTP client, which is [`cli.md`](cli.md)'s and does
not reference this package; and the development host's HTTP server, which is
[`devhost-control.md`](devhost-control.md)'s.

## Registration and its defaults

- `AC-HTTP-001.1` (Event-driven): WHEN `AddDaleHttpSdk` is called THE SYSTEM SHALL register the
  logic-block HTTP client, its request executor and its content serializer as transient services, and
  one named `HttpClient` the package resolves per request under a name no public member exposes.
- `AC-HTTP-001.2` (Ubiquitous): THE SYSTEM SHALL configure that client with a thirty-second timeout
  and a Vion `User-Agent` before running the caller's `configureClient`, so a caller's own timeout or
  `User-Agent` wins.
- `AC-HTTP-001.3` (Event-driven): WHEN `AddDaleHttpSdk` is called more than once THE SYSTEM SHALL
  still send exactly one Vion `User-Agent` on the wire.
- `AC-HTTP-001.4` (Event-driven): WHEN the caller's `configureClient` throws THE SYSTEM SHALL deliver
  that exception to the error callback of every request.

The name is the package's own and no member exposes it, so `configureClient` is the supported way to
change anything about the client — a caller that spells the name itself is relying on an
implementation detail. The transient lifetime means a block may hold the injected client for its
whole life without sharing state with any other block; what *is* shared is the pooled handler
underneath, which is `AC-HTTP-002.1`'s subject.

`AC-HTTP-001.3` exists because `AddHttpClient` keeps one configuration action per call and runs them
all against the same client. A plugin composed from two libraries that each register the SDK would
otherwise send the header twice, which a strict server rejects.

`AC-HTTP-001.4` is the one registration mistake with no registration-time symptom: `configureClient`
runs when the client is first created, inside the request, so a broken one is met once per request
forever and nothing in what the block receives names registration as the cause.

## The transport policy

- `AC-HTTP-002.1` (Ubiquitous): THE SYSTEM SHALL add no primary message handler of its own, so
  redirect following, decompression and the connection pool are the platform's defaults rather than
  this package's contract.

This is the criterion the package can keep. What the platform's defaults currently are — redirects
followed up to a limit, responses not decompressed, one pooled handler per container with one cookie
container in it — is `Microsoft.Extensions.Http`'s to state and to change, and a test asserting them
here would pin the platform rather than this package.

Two consequences are worth an author's attention even so. A 3xx is followed before the block sees
anything, so a block cannot observe a redirect through this package. And the cookie container is
shared by every request every block in **one plugin's container** makes — each plugin composes its
own container, and `AC-PLUG-005.3` gives each its own copy of this assembly, so two plugins cannot
collide. Turning either off would mean owning the primary handler, which would move all three of
those facts from the platform's statement into this package's; the finding ledger carries that ask.

## The eight members

- `AC-HTTP-003.1` (Ubiquitous): THE SYSTEM SHALL offer eight request members, each returning to its
  caller before the exchange completes and each delivering its callbacks through the dispatcher it was
  given.
- `AC-HTTP-003.2` (Ubiquitous): THE SYSTEM SHALL send the HTTP method each member is named for, and
  SHALL deserialize the response body only for the members that carry a response type.
- `AC-HTTP-003.3` (Event-driven): WHEN a member is given no success callback THE SYSTEM SHALL
  schedule nothing onto the block's actor.

The family is `GetJson`, `PostJson` in two shapes, `PutJson` in two shapes, `DeleteJson`, `Delete`
and `SendRequest`. Four carry a response type and take a required success callback; four do not and
take an optional one. Every member is `void`: the request is in flight when the member returns, and
the only way a block learns what happened is a callback.

`AC-HTTP-003.3` is a chosen silence, not another silent loss. A member given no callback has been
told the block does not want the answer, and discarding it is what the author asked for — which is
why it is not among the refusals below. The distinction is that the refusals name arguments no
caller could have meant.

None of the eight takes a `CancellationToken`: a request, once issued, runs to its own end. That is
the same shape `AC-LIFE-006.4` states for the dispatcher, and the reason a block that must stop
calling stops issuing rather than cancelling.

## `SendRequest`

- `AC-HTTP-004.1` (Event-driven): WHEN a caller uses `SendRequest` THE SYSTEM SHALL send the
  `HttpRequestMessage` it is given, taking no URL and no headers of its own.
- `AC-HTTP-004.2` (Ubiquitous): THE SYSTEM SHALL hand a `SendRequest` success callback a response
  whose body may still be streaming, disposing neither that response nor the request the caller
  supplied.

`SendRequest` is the escape hatch from everything the other seven decide: the method, the URI, the
headers, the content and its content type are all the caller's, which is how a block reaches an
endpoint that wants a form body, a charset parameter, or a method the family does not name.

`AC-HTTP-004.2` is the price of that hatch, and it is the one ownership rule in the package an author
must read rather than infer. The response is handed over as soon as its headers arrive, so the
callback may be reached while the body is still coming; the callback owns it and disposes it. The
request stays the caller's too. Every other member owns and disposes both, which is
`AC-HTTP-010.3`'s subject.

## The actor hop

- `AC-HTTP-005.1` (Ubiquitous): THE SYSTEM SHALL run every callback on the calling block's own actor
  rather than on the thread the exchange completed on.
- `AC-HTTP-005.2` (Event-driven): WHEN the block has not yet received its first message THE SYSTEM
  SHALL run neither callback and SHALL leave the outcome of the request visible only in the log.
- `AC-HTTP-005.3` (Ubiquitous): THE SYSTEM SHALL deliver callbacks in the order their exchanges
  complete, imposing no ordering of its own, and SHALL accept a request issued from within a callback.
- `AC-HTTP-005.4` (Event-driven): WHEN a callback throws THE SYSTEM SHALL not fail the request and
  SHALL not throw to the caller.

`AC-HTTP-005.1` is the whole reason this package exists rather than an `HttpClient` a block holds
itself: the callback is a self-send, so it runs on the block's own actor and the block needs no
locking of its own state (`AC-LIFE-006.1`). It also carries neither sender nor headers
(`AC-LIFE-014.7`), so a handler reached from a callback cannot answer the message that started the
work.

`AC-HTTP-005.2` is the package's sharpest edge and it is stated rather than fixed. A block that
issues a request from its constructor may get its answer before it has an actor; the dispatcher
refuses the self-send, naming the cause and saying where to schedule from instead, and this package
catches that refusal and logs it. The block waits forever. Neither cure is this package's to write —
re-queuing needs an actor it does not have, and refusing at issue time needs to know whether the
block has one, which `IActorDispatcher` does not expose. The finding ledger carries the ask, owned by
`LIFE`. **Issue requests from `Ready()` or `Starting()`, never from a constructor.**

`AC-HTTP-005.4`'s "not throw to the caller" is narrow and worth reading exactly: this package catches
only what handing the callback over throws. The callback's own body runs later, on the actor, where
an exception it throws is the actor's to handle and not this package's.

## The refusals

- `AC-HTTP-007.1` (Event-driven): WHEN a caller passes no dispatcher THE SYSTEM SHALL refuse the call
  before sending anything, naming the parameter and the member.
- `AC-HTTP-007.2` (Event-driven): WHEN a caller passes a timeout the runtime's cancellation source
  will not accept THE SYSTEM SHALL refuse the call before sending anything, naming the parameter, the
  member and the bound.
- `AC-HTTP-007.3` (Event-driven): WHEN a caller passes `SendRequest` no request, or a request with no
  URI, THE SYSTEM SHALL refuse the call before sending anything, naming the parameter and the member.

All three are `AC-LIFE-006.2`'s shape, for `AC-LIFE-006.2`'s reason: each named an argument whose
only symptom was a request that went nowhere or went and reported nothing. They are raised
synchronously, at the caller, because a `void` member that faults a task nobody holds has no other
way to say anything.

The accepted band for a timeout is the runtime's own: the infinite timespan for no bound, or from
zero up to what the cancellation source will take. That upper bound has the same origin as the delay
bound `AC-LIFE-006.2` enforces — the same milliseconds — but it is not the same number, being 294 ms
larger; neither is derived from the other, and the dispatcher's is not reachable from here.

## The error model

- `AC-HTTP-006.1` (Ubiquitous): THE SYSTEM SHALL deliver one exception class per failure to the error
  callback: a non-success status as `HttpRequestException`, a URL that is not an absolute URI as
  `InvalidOperationException`, a response body that is absent or malformed as `JsonException`, a body
  that deserializes to null as `ContentNullAfterDeserializationException` naming the full type name, a
  disposed client as `ObjectDisposedException`, and any transport failure as the exception the handler
  threw.
- `AC-HTTP-006.2` (Event-driven): WHEN a request fails and the caller gave no error callback THE
  SYSTEM SHALL schedule nothing onto the block's actor.

"The exception the handler threw" is the rule the rest of the table is the exception to: this package
wraps nothing. A DNS failure, a refused connection, a reset stream and a TLS fault reach the error
callback exactly as the transport raised them, and what class that is belongs to the handler the
platform composed, not to this package. The one class the package mints is the timeout below.

A URL the client cannot resolve to an absolute URI — null, empty, whitespace, relative, or simply not
a URI — fails this way rather than being refused at the caller, because the failure is the transport's
own and arrives through the error callback like any other. A scheme the package does not know is not
special-cased either: it goes to the handler, which decides.

`AC-HTTP-006.2` is the documented half of "errors are always logged": with no error callback the
failure reaches the log and nothing else. It is not silence — but it is not the block's business
either, which is why a block that cares passes a callback.

## The two timeout bounds

- `AC-HTTP-008.1` (Event-driven): WHEN a per-request timeout elapses THE SYSTEM SHALL deliver a
  `TimeoutException` naming the timeout in seconds, rendered in the invariant culture; a timeout of
  zero SHALL fail the request at once, and the infinite timeout SHALL apply no per-request bound.
- `AC-HTTP-008.2` (Ubiquitous): THE SYSTEM SHALL bound every request by the client's own timeout as
  well, a per-request timeout never raising it, and SHALL deliver that bound's expiry as a
  `TaskCanceledException`.

**Two bounds, two classes, and the smaller bound wins.** A per-request timeout is applied *in
addition to* the client's, not in place of it: a value longer than the client's does not extend
anything, and the request then ends at the client's bound and arrives as the platform's cancellation
rather than as this package's `TimeoutException`. A block that catches only `TimeoutException` will
miss every expiry of the ceiling, and the finding ledger carries the ask to normalise the two.

The number in the message is rendered invariantly, so the string reads the same on every machine —
the gateways this runs on are German-locale, where a culture-rendered `0.05` reads `0,05` and no
support query finds it.

A timeout of zero is not "no timeout": the cancellation source it builds is already expired, so the
request fails immediately against any handler that honours cancellation. The value that means no
per-request bound is the infinite timespan.

## Deserialization

- `AC-HTTP-009.1` (Event-driven): WHEN a response body's property names do not match the target
  type's THE SYSTEM SHALL deliver the value with those properties at their defaults and report no
  error.
- `AC-HTTP-009.2` (Event-driven): WHEN a response body carries properties the target type does not
  declare, or omits ones it does, THE SYSTEM SHALL deliver the value with the omitted properties at
  their defaults and report no error.

These two are the most consequential thing this package does quietly, and they are stated here rather
than in the error table because **they are not errors**: the success callback fires, with an object
full of defaults. A server answering camelCase into a type declared the way C# declares things
produces a fully populated response and a completely empty value, and nothing anywhere says so.

There are two cures, both the author's. Configure
`services.Configure<JsonSerializerOptions>(…)` — case-insensitive matching, or a naming policy — which
`AC-HTTP-011.1` applies to every member. Or annotate the type, which is what the in-repo example does.
A block whose values are suspiciously zero should suspect this first.

## Lifetime and disposal

- `AC-HTTP-010.1` (Ubiquitous): THE SYSTEM SHALL judge a response's status on its headers, before its
  body has been read.
- `AC-HTTP-010.2` (Event-driven): WHEN a request fails THE SYSTEM SHALL dispose the response before
  delivering the failure.
- `AC-HTTP-010.3` (Ubiquitous): THE SYSTEM SHALL dispose the response it created for a member that
  carries a response type, the deserialized value having been read from it first.

`AC-HTTP-010.1` is the fact the other two turn on, and the one that explains `AC-HTTP-004.2`: the
response is available as soon as its headers arrive, so a failure is reported before the body exists
and a `SendRequest` callback can be reached before it does either.

`AC-HTTP-010.2` follows from that: no callback ever receives a failed response, so nothing downstream
could dispose one, and a failed response still holds a body stream nobody will drain. A block polling
a failing endpoint on a timer used to leak one per tick.

`AC-HTTP-010.3` is why the seven body-carrying members need no ownership rule: the value is read out
of the response before the hop, so the callback receives the value and never the response.

## Serialization and headers

- `AC-HTTP-011.1` (Ubiquitous): THE SYSTEM SHALL serialize and deserialize with the
  `JsonSerializerOptions` the consumer configured, and with the platform's defaults when none is
  configured.
- `AC-HTTP-011.2` (Ubiquitous): THE SYSTEM SHALL send a body it serialized as `application/json` with
  no charset, and SHALL dispose that content with the request.
- `AC-HTTP-011.3` (Event-driven): WHEN a caller passes a null request body THE SYSTEM SHALL serialize
  it as the JSON literal null and send it.
- `AC-HTTP-012.1` (Ubiquitous): THE SYSTEM SHALL add the caller's headers to the request without
  validating them.
- `AC-HTTP-012.2` (Event-driven): WHEN a header cannot be added to the request THE SYSTEM SHALL drop
  it and send the request.

`AC-HTTP-011.2`'s "no charset" is a real constraint: a server that requires `charset=utf-8` will
reject every body this package serializes, and the only way past it is `SendRequest` with content the
caller built.

`AC-HTTP-011.3` is where a compile-time constraint stops being a guarantee. The members constrain the
body to be non-null, but that is checked by the compiler and not at runtime, so a call site with
nullable reference types disabled reaches the wire with the literal `null` as its body.

`AC-HTTP-012.2` is the same shape as `AC-HTTP-009.1`: something is quietly not done and the request
succeeds anyway. A header a request-header collection refuses — a *content* header such as
`Content-Type` or `Content-Length`, or a malformed name — is dropped, and nothing the block receives
mentions it. `SendRequest` is the way to set a content header, since there it belongs to content the
caller owns.

## The published surface

- `AC-HTTP-013.1` (Ubiquitous): THE SYSTEM SHALL classify every public type it ships as either
  published surface or internal plumbing.
- `AC-HTTP-013.2` (Ubiquitous): THE SYSTEM SHALL judge its own declarations with the Dale analyzers,
  so a public type in its declared published namespace carrying neither surface mark draws a
  diagnostic in its build.
- `AC-HTTP-013.3` (Ubiquitous): THE SYSTEM SHALL NOT declare itself a shared assembly, so every
  plugin loads its own copy and the client's types are that plugin's alone.
- `AC-HTTP-014.1` (Ubiquitous): THE SYSTEM SHALL target the SDK's cross-platform plugin framework and
  SHALL add logging, JSON and HTTP-factory dependencies to any plugin that takes it.

The published set is what a block author is meant to name: the client interface, the registration
extension, and the one exception worth catching by name. The concrete client and the two seams it
composes itself through are plumbing — public only because the container's activator needs a public
constructor, which is the classification rule's own case.

`AC-HTTP-013.2` is what makes `AC-HTTP-013.1` enforceable rather than aspirational, and it is the
lesson of this area: the package declared a published namespace with **no reader at all** through
every release, so nothing ever asked it for a mark and three of its five public types had none. The
proof is a deliberately unmarked type linked into the package's own build under a property, where the
diagnostic must appear — `AC-TKIT-013.2`'s shape.

`AC-HTTP-013.3` is a decision, not an omission. The shared-assembly marker's own rule scopes it to
contract handler actors and cross-plugin message types, and this package declares neither: it is
reached through DI inside one plugin and hands nothing across a boundary. Marking it would engage
`AC-PLUG-005.9`, letting whichever plugin bound it first fix the logging, JSON and HTTP-factory
versions every other plugin's client resolves against for the process's life (`AC-PLUG-005.7`). The
cost of leaving it is that two plugins cannot pass an `ILogicBlockHttpClient` between them, which
nothing does.

`AC-HTTP-014.1` is the rest of the adoption bill. The package is one call to add; what a plugin also
inherits is the logging abstractions, `System.Text.Json` and the HTTP factory.

## What the package does not do

Stated because a consumer evaluating it will look for each, and finding nothing is a slower way to
learn it than reading it here.

- **No retries, no backoff, no circuit breaker.** A failure is one callback; a block that wants
  another attempt schedules it. The in-repo example's answer is a cache with a fifteen-minute life
  rather than a retry.
- **No link or connection diagnostics.** There is no HTTP analogue of the Modbus link and socket
  summaries: the transport is the platform's pooled handler and nothing surfaces its state.
- **No test kit.** `ILogicBlockHttpClient` is an interface and mocks cleanly, but there is no fake
  harness with the byte-level fidelity the Modbus kits give.

The finding ledger carries the first consumer's asks for the second and third, with the note that
raised them.

## Test discipline

The suite drives the real executor over a stub innermost handler, behind the real named client, and
**awaits the executor's own task** before asserting; callbacks are then drained from a dispatcher
stub that queues them the way a real actor does. Nothing waits on the wall clock. The discriminator
for every timeout claim is a handler that never answers and honours cancellation — never a delay
raced against a shorter bound ([`../testing-conventions.md`](../testing-conventions.md) § 16), and
never a handler that ignores the token, which reads the opposite of a real one on a zero bound.

Three limits of that seam are contract rather than accident, because the stub replaces the very
handler the platform composes:

- **A 3xx is not followed.** A redirect assertion written against the stub would prove the opposite of
  production, which is why `AC-HTTP-002.1` states what the package owns instead of what the platform
  currently does.
- **A `Content-Length` that disagrees with its body is accepted**, where a real socket handler fails
  the read. The harness is more forgiving than production here.
- **A scheme the platform would refuse reaches the stub and succeeds.** What `AC-HTTP-006.1` states
  about a URL is what the package does with the handler's answer, not a claim about which schemes
  connect.

Two behaviours are stated on this page and proven by no test in this suite, by rule rather than by
omission. The unhandled-exception continuation that logs a fault escaping a request's own error
handling has a log line as its only observable, and log text is not a contract
([`../testing-conventions.md`](../testing-conventions.md) § 15). And the API manifest is a snapshot CI
regenerates and commits rather than a gate ([`../releasing.md`](../releasing.md)); `AC-HTTP-013.1`
pins the marks, which are testable, and the manifest follows from them.
