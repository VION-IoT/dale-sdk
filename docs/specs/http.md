---
trace: enforced
---

# HTTP: what a logic block gets when it talks to a server, or is one

What the SDK guarantees a logic-block author who needs to call an HTTP service — a weather API, a
device's own REST surface, a commissioning endpoint — or whose block must answer HTTP itself, as a
simulator standing in for such a device does. One packable package ships both,
`Vion.Dale.Sdk.Http`, added with one call: the client is reached through one interface, the hosted
server through a factory. Area code `HTTP`. Process: [`../spec-process.md`](../spec-process.md).

The package is a **thin adapter over `IHttpClientFactory`**, and almost everything below follows from
that. It composes no handler of its own, retries nothing, models no link, and wraps no exception
except one. What it does own is the hop: every callback runs on the calling block's own actor, so a
block needs no locking of its own state and no thread of its own. And it owns the facts of each
request, which exist nowhere else: a receipt handed to every callback, and a summary of every request a
client made. **It is a published package**, so
every default, exception class and ownership rule below is a contract with readers outside this
repository.

The **hosted server** is a separate half with a model of its own, stated after the client: the block
publishes responses and reads back what was asked, and the server answers on its own threads without
ever calling the block.

The spine is the order an author meets it: registration and its defaults, the eight members as one
family, `SendRequest` on its own, the actor hop, the error model, the receipt, the refusals, the two
timeout bounds, deserialization, lifetime and disposal, serialization, headers, the client's summary,
the hosted server and its summary, the surface, and the test discipline.

Cited rather than restated: [`block-lifecycle.md`](block-lifecycle.md) for the dispatcher every
callback arrives through — `AC-LIFE-006.1` (an action runs on the block's own actor),
`AC-LIFE-006.2` (a delay beyond a real clock is refused, naming the member), `AC-LIFE-006.3` (a
dispatcher call before the block's first message is refused), `AC-LIFE-006.4` (nothing armed can be
cancelled) and `AC-LIFE-014.7` (a self-send carries neither sender nor headers);
[`plugin-loading.md`](plugin-loading.md) for what an unmarked assembly means — `AC-PLUG-005.3`,
`AC-PLUG-005.7` and `AC-PLUG-005.9`; [`analyzers.md`](analyzers.md) for the rule that judges this
package's own declarations (`AC-ANLZ-012.1`, `AC-ANLZ-012.2`); [`testkit.md`](testkit.md) for the
surface-and-arming shape this package now follows (`AC-TKIT-013.1`, `AC-TKIT-013.2`) and for the HTTP
test kit that drives both halves (`AC-TKIT-014.*`, `AC-TKIT-015.*`); [`modbus.md`](modbus.md) for the
hosted Modbus server, whose binding rule the hosted HTTP server departs from (`AC-MODB-011.2`) and whose
factory ownership rule it shares (`AC-MODB-018.3`), and for the link summary whose window both HTTP
summaries share (`AC-MODB-016.7`); [`emission.md`](emission.md) for what publishing a summary costs and the 30 s default both
summaries declare (`AC-EMIT-002.8`);
[`../simulator-authoring.md`](../simulator-authoring.md) for what a socket means to a bench.

Not this area: the `dale` CLI's own hand-rolled HTTP client, which is [`cli.md`](cli.md)'s and does
not reference this package; and the development host's HTTP server, which is
[`devhost-control.md`](devhost-control.md)'s.

## Registration and its defaults

- `AC-HTTP-001.1` (Event-driven): WHEN `AddDaleHttpSdk` is called THE SYSTEM SHALL register the
  logic-block HTTP client, its request executor and its content serializer as transient services, and
  one named `HttpClient` the package resolves per request under a name no public member exposes.
- `AC-HTTP-001.2` (Ubiquitous): THE SYSTEM SHALL configure that client with a thirty-second timeout
  and a Vion `User-Agent` before running the caller's `configureClient`, so a caller's own timeout or
  `User-Agent` wins and a later registration does not undo it.
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
otherwise send the header twice, which a strict server rejects. Each default is therefore applied
only where the client still lacks it — no `User-Agent` at all, and the platform's own starting
timeout — which is also what keeps `AC-HTTP-001.2`'s promise across more than one registration: the
second registration's defaults do not run over a value the first registration's `configureClient`
chose, and the last `configureClient` still wins for whatever it sets itself.

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
those facts from the platform's statement into this package's.

## The eight members

- `AC-HTTP-003.1` (Ubiquitous): THE SYSTEM SHALL offer eight request members, each returning to its
  caller before the exchange completes, and SHALL carry the dispatcher, the timeout and the error
  callback its caller gave it into the request it makes, along with the URL and the headers for the
  seven members that take them.
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
  `HttpRequestMessage` it is given, taking no URL and no headers of its own, including a body on any
  method and the caller's own content type.
- `AC-HTTP-004.2` (Ubiquitous): THE SYSTEM SHALL hand a `SendRequest` success callback a response
  whose body may still be streaming, disposing neither that response nor the request the caller
  supplied, and SHALL dispose that response itself when the caller gave no callback to own it.

`SendRequest` is the escape hatch from everything the other seven decide: the method, the URI, the
headers, the content and its content type are all the caller's, which is how a block reaches an
endpoint that wants a form body, a charset parameter, or a method the family does not name. Nothing
is challenged on the way out either — a `GET` carrying a body is sent as written, because deciding
which methods may carry one would be a policy this package does not have and the server is the one
that answers the question.

`AC-HTTP-004.2` is the price of that hatch, and it is the one ownership rule in the package an author
must read rather than infer. The response is handed over as soon as its headers arrive, so the
callback may be reached while the body is still coming; the callback owns it and disposes it. The
request stays the caller's too. Every other member owns and disposes both, which is
`AC-HTTP-010.3`'s subject.

The success callback is optional, though, and the clause about the missing one is the other half of
the same rule: with no callback there is no owner, so the package disposes the response rather than
dropping it with its body stream still open. This is the one branch of `SendRequest` where it
disposes anything, and it is deliberately narrower than a `finally` around the whole exchange — that
would take the response away from the callback that was given one.

## The actor hop

- `AC-HTTP-005.1` (Ubiquitous): THE SYSTEM SHALL run every callback on the calling block's own actor
  rather than on the thread the exchange completed on.
- `AC-HTTP-005.2` (Event-driven): WHEN the block has not yet received its first message THE SYSTEM SHALL run neither callback and SHALL leave the outcome of the request visible only in the log and in the client's summary.
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
block has one, which `IActorDispatcher` does not expose. **Issue requests from `Ready()` or
`Starting()`, never from a constructor.**

`AC-HTTP-005.4`'s "not throw to the caller" is narrow and worth reading exactly: this package catches
only what handing the callback over throws. The callback's own body runs later, on the actor, where
an exception it throws is the actor's to handle and not this package's. "Not fail the request" is the
other half and the one with a visible consequence: a success callback that throws where the package
can see it does not turn the exchange into a failure, so the error callback is not reached with the
success callback's own exception.

## The refusals

- `AC-HTTP-007.1` (Event-driven): WHEN a caller passes no dispatcher THE SYSTEM SHALL refuse the call
  before sending anything, naming the parameter and the member.
- `AC-HTTP-007.2` (Event-driven): WHEN a caller passes a timeout the runtime's cancellation source
  will not accept THE SYSTEM SHALL refuse the call before sending anything, naming the parameter, the
  member and the bound.
- `AC-HTTP-007.3` (Event-driven): WHEN a caller passes `SendRequest` no request, or a request with no
  URI, THE SYSTEM SHALL refuse the call before sending anything, naming the parameter and the member.
- `AC-HTTP-007.4` (Event-driven): WHEN the configured serializer cannot serialize a request body THE
  SYSTEM SHALL throw the serializer's own exception at the caller before sending anything.

The first three are `AC-LIFE-006.2`'s shape, for `AC-LIFE-006.2`'s reason: each named an argument
whose only symptom was a request that went nowhere or went and reported nothing. They are raised
synchronously, at the caller, because a `void` member that faults a task nobody holds has no other
way to say anything.

`AC-HTTP-007.4` is the fourth thing a member can throw and the one the package does not word itself.
The four members that take a body serialize it on the calling thread, before the exchange is started,
so a body `AC-HTTP-011.1`'s options cannot write — a cycle, a property getter that throws, a
converter the consumer supplied — surfaces at the call site rather than in the error callback.
Nothing is sent and no callback runs. The exception is the serializer's own, which is why this
criterion names no class: a `catch` here is around the member, not around a request.

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
  threw; a per-request timeout is reported as a timeout only where that bound cancelled the exchange,
  a failure raised after it has elapsed keeping the class of what failed.
- `AC-HTTP-006.2` (Event-driven): WHEN a request fails and the caller gave no error callback THE
  SYSTEM SHALL schedule nothing onto the block's actor.

"The exception the handler threw" is the rule the rest of the table is the exception to: this package
wraps nothing. A DNS failure, a refused connection, a reset stream and a TLS fault reach the error
callback exactly as the transport raised them, and what class that is belongs to the handler the
platform composed, not to this package. The one class the package mints is the timeout below.

The timeout clause matters because the per-request bound does not cover the whole exchange: it ends
at the response headers, and the body read and the deserialization run outside it. A body that will
not parse, a stream that breaks, or a status judged once the bound has elapsed is therefore a failure
that happened *after* a cancellation source had fired without having cancelled anything — and it
reaches the block as what it is, not as a timeout.

A URL the client cannot resolve to an absolute URI — null, empty, whitespace, relative, or simply not
a URI — fails this way rather than being refused at the caller, because the failure is the transport's
own and arrives through the error callback like any other. A scheme the package does not know is not
special-cased either: it goes to the handler, which decides.

`AC-HTTP-006.2` is the documented half of "errors are always logged": with no error callback the
failure reaches the log and the client's summary and nothing else. It is not silence — but it is not
the block's business either, which is why a block that cares passes a callback.

The class does not tell a server's answer from a failure to reach one. A refused connection and a 404
both arrive as an `HttpRequestException`, and the status that tells them apart is a property the
`netstandard2.1` target a block compiles against does not declare. That is the receipt's job, below,
and not the exception's: the exception stays exactly what this table says.

## The receipt

- `AC-HTTP-019.1` (Ubiquitous): THE SYSTEM SHALL hand every success callback and every error callback of the eight request members a receipt of that request alongside its value, response or exception.
- `AC-HTTP-019.2` (Ubiquitous): THE SYSTEM SHALL report on the receipt a success for a 2xx response whose content was read, a client error for any other response below 500, a server error for a response of 500 or above, a content error for a 2xx response whose body is absent, malformed or deserializes to null, a timeout where either bound cancelled the exchange, an invalid request where the client or the request could not be built or the client refused the request before any handler saw it, and a transport error for any other failure.
- `AC-HTTP-019.3` (Ubiquitous): THE SYSTEM SHALL carry on the receipt the status of the response it judged whenever one arrived, and no status when none did, even where the exception a handler threw carries one.
- `AC-HTTP-019.4` (Ubiquitous): THE SYSTEM SHALL stamp the receipt, on the registered clock, with the instant the outcome was observed on the wall clock and on the monotonic timestamp scale, and with the time from handing the request to the client until that instant.

The receipt is how a block tells a server's answer from a failure to reach it. `StatusCode` is set
exactly when a response arrived, so a block asking whether a server said 404 asks
`receipt.StatusCode == HttpStatusCode.NotFound` — no class test, no reflection, no match on the
message. Ignoring the receipt is a discard, `(value, _) => …`; there is no overload without it.

`AC-HTTP-019.2`'s outcomes follow what the exchange reached, not the exception's class. A status of
500 or above is a server error and every other status outside 2xx a client error, a 3xx the platform
did not follow included: it says the request as sent did not get what it asked for, not that the
server is failing. A timeout is one of the two bounds below elapsing; a `TimeoutException` a handler
throws itself is a transport error. An invalid request is one the client refused before starting the
exchange — a URL no base address makes absolute, a disposed client, a request message it has sent
before — or one whose client could not be built, as when `configureClient` throws (`AC-HTTP-001.4`).
The client raises its own refusals before it hands the request to any handler, and that, not the
class, is what tells them from a handler throwing the same classes; the package composes no handler to
find out (`AC-HTTP-002.1`).

`AC-HTTP-019.3` is why the status is taken from the response the package judged and never from the
exception: a handler can construct an `HttpRequestException` carrying a status for a response that
never arrived, and a status on a receipt means a server said it. A 2xx whose body then broke keeps its
status beside its transport error.

`AC-HTTP-019.4`'s instant is taken before the callback is handed to the block, so it is the age of
the value it accompanies however long the block's mailbox holds the callback: `ReceivedAt` is what a
block publishes, `ReceivedTimestamp` what it ages a value against, because a wall clock can step when
a gateway's time is corrected. The round trip runs until the package observed the outcome — for the
four members that carry a response type, through reading and deserializing the body; for
`SendRequest`, to the headers, because the body is the callback's (`AC-HTTP-004.2`). A request the
client refused was never sent, and its round trip is zero.

## The two timeout bounds

- `AC-HTTP-008.1` (Event-driven): WHEN a per-request timeout elapses THE SYSTEM SHALL deliver a
  `TimeoutException` naming the timeout in seconds, rendered in the invariant culture; a timeout of
  zero SHALL fail the request at once, and the infinite timeout SHALL apply no per-request bound.
- `AC-HTTP-008.2` (Ubiquitous): THE SYSTEM SHALL bound every request by the client's own timeout as
  well, a per-request timeout never raising it, and SHALL deliver that bound's expiry as a
  `TimeoutException` naming the client's timeout in seconds, rendered in the invariant culture.

**Two bounds, one class, and the smaller bound wins.** A per-request timeout is applied *in addition
to* the client's, not in place of it: a value longer than the client's does not extend anything, and
the request then ends at the client's bound. Both expiries arrive as the same `TimeoutException`, so
a block that catches it catches every timeout it can have — including every request that set no
per-request timeout at all, where the client's is the only bound there is. What tells the two apart
is the number, which is always the bound that actually elapsed: a per-request value the request never
reached is never the one named.

Not every cancellation is one of the two, and the package does not treat it as one. A handler in the
composed pipeline can cancel on a token of its own, and that reaches the block as what it is
(`AC-HTTP-006.1`'s last row) rather than as a bound expiring. The two are told apart at their
sources: the per-request bound by the state of the source this package armed, the client's by the
`TimeoutException` the client puts inside the cancellation it raises for its own bound and for
nothing else.

- `AC-HTTP-008.3` (Ubiquitous): THE SYSTEM SHALL measure a per-request timeout on the clock registered
  in the container, registering the system clock where none is registered and keeping one registered
  before it.

`AC-HTTP-008.3` is what lets a host decide when a per-request bound elapses. On the system clock it
changes nothing a block can see; under a controllable clock — the HTTP test kit's — an advance of that
clock expires a held request's bound, and the expiry arrives exactly as `AC-HTTP-008.1` states. The
client's own bound is a timer inside the platform and is not measured on this clock.

The development host in deterministic stepping registers such a clock too, so there a real request's
per-request bound elapses when the stepper advances time, as every other timer in that mode does. A
request that nothing steps past its bound is still ended on wall time by the client's own bound.

That inner exception is the platform's, not this package's, and it is what tells the ceiling apart
from any other cancellation rather than leaving it guessed at: the client has set it since .NET 5.
The runtime this SDK's plugins load into is past that, and it is the host `AC-HTTP-008.2` is stated
for. The `netstandard2.1` target reaches further back, and on a host that predates .NET 5 the
client's bound arrives as the cancellation it raises with nothing to relabel it — the behaviour this
criterion replaced, never a message naming a bound that did not elapse.

The number in the message is rendered invariantly, so the string reads the same on every machine —
the gateways this runs on are German-locale, where a culture-rendered `0.05` reads `0,05` and no
support query finds it. Both bounds are named through one rendering, so the two messages cannot
drift apart.

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
`AC-HTTP-011.1` applies to every member. Or annotate the type, which is what `Vion.Examples.Energy`'s `OpenMeteoService` does.
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
- `AC-HTTP-011.2` (Ubiquitous): THE SYSTEM SHALL serialize a request body only for the members that
  carry one, send it as `application/json` with no charset, and dispose it with the request.
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

## The client's summary

- `AC-HTTP-020.1` (Ubiquitous): THE SYSTEM SHALL accumulate every request a client instance issues into that instance's own summary, readable at any time, before the request's callback is handed to the block and whether or not the request has a callback.
- `AC-HTTP-020.2` (Ubiquitous): THE SYSTEM SHALL count every request under its receipt's outcome, keeping one lifetime counter per outcome.
- `AC-HTTP-020.3` (Ubiquitous): THE SYSTEM SHALL record every outcome but a success as the last failure, with its instant and the status of its response where one arrived, and SHALL record the instant of the last response of any status.
- `AC-HTTP-020.4` (Ubiquitous): THE SYSTEM SHALL feed every round-trip figure only from successes, client errors, server errors and content errors.
- `AC-HTTP-020.5` (Ubiquitous): THE SYSTEM SHALL report the mean, the maximum and the number of round trips over at least the last 15 minutes and less than the last 16 on the registered clock, or over the client's whole life where that is shorter, reporting the mean and maximum as empty and the count as zero while none falls within it.
- `AC-HTTP-020.6` (Ubiquitous): THE SYSTEM SHALL report the lifetime maximum round trip with the instant its outcome was observed, and SHALL move that instant only when a strictly larger value is recorded.
- `AC-HTTP-020.7` (Ubiquitous): THE SYSTEM SHALL report how many issued requests have not yet had their outcome recorded.

Decision `0118`, for HTTP: the package keeps the tally of a client's requests, so no block hand-keeps
counters. `Summary` is one flat readonly record struct a block publishes as a single
`[ServiceProperty]`.

`AC-HTTP-020.1` scopes it to the client instance. The client is transient (`AC-HTTP-001.1`), so a
block that calls two services and wants their figures apart injects two clients. The summary is
recorded before the callback is handed over, so it does not lag by however many callbacks wait in the
block's mailbox, and a request whose callback reaches nobody (`AC-HTTP-005.2`) or that has none
(`AC-HTTP-006.2`) still counts. A call refused at the caller (the refusals below) never became a
request and does not.

There is no up/down verdict (decision `0200`): one client may call many servers, and only the block
knows how often it calls. `LastResponseAt` and the last failure are what a block builds one from, on
its own cadence — which is also why nothing here decays with time.

`AC-HTTP-020.4` keeps the round trip a measure of how fast a server answers: a timeout's duration is
its bound and a refused connection's is close to zero, and either would pull the mean away from that.
The window is the Modbus link summary's (`AC-MODB-016.7`) for that summary's reason — it is published
as one live value and nothing downstream keeps its history — and it runs on the registered clock, so
it rolls on virtual time under the HTTP test kit and a stepped host (`AC-HTTP-008.3`).

**What publishing a summary costs.** Every completed request moves a counter, so each value the block
assigns differs from the last: the dedup floor never holds one back, and a struct has no deadband
([`emission.md`](emission.md)). The summary is published as often as the block assigns it, up to once
per `MinInterval` — at 250 ms, four times a second for a block that assigns it in every callback of a
busy client, 14,400 publishes an hour. So the summary declares a 30 s default (`AC-EMIT-002.8`): a
member that assigns no interval of its own publishes it at most 120 times an hour, and the latest value
is still published within one interval. Do not set `Immediate`. A signal whose every edge matters is
the receipt's, not the summary's.

## The hosted server: configuration and lifecycle

A logic block can *be* an HTTP server. It is configured by properties and gated by `IsEnabled`, exactly
like the hosted Modbus server — configure while disabled, then enable.

- `AC-HTTP-015.1` (Event-driven): WHEN `AddDaleHttpSdk` is called THE SYSTEM SHALL register a server
  factory whose every `Create()` returns a new, disabled HTTP server that listens on a socket once
  enabled.
- `AC-HTTP-015.2` (Ubiquitous): THE SYSTEM SHALL listen on loopback and on port 8080 unless told
  otherwise.
- `AC-HTTP-015.3` (Event-driven): WHEN a listen address or a port is set while the server is enabled THE
  SYSTEM SHALL throw an `InvalidOperationException`.
- `AC-HTTP-015.4` (Unwanted): IF a listen address that is not an IP address, or a port outside 1 to
  65535, is set THEN THE SYSTEM SHALL throw a `FormatException` naming the value in the invariant
  culture.
- `AC-HTTP-015.5` (Event-driven): WHEN enabling the server cannot bind the listener THE SYSTEM SHALL
  propagate the failure to the caller and leave the server disabled and not listening, and a server
  already holding that port serving.
- `AC-HTTP-015.6` (Event-driven): WHEN the server is enabled THE SYSTEM SHALL start listening on the
  configured address and port, WHEN it is disabled THE SYSTEM SHALL stop, and WHEN either is repeated
  THE SYSTEM SHALL do nothing, keeping the published responses across both.
- `AC-HTTP-015.7` (Event-driven): WHEN the server is disposed THE SYSTEM SHALL stop listening, report
  itself disabled, stay silent on a second disposal, and refuse to be enabled again with an
  `ObjectDisposedException`, while still running `Sync` callbacks.
- `AC-HTTP-015.8` (Event-driven): WHEN the server is enabled on a port whose previous listener has stopped
  while that listener's closed connections still linger THE SYSTEM SHALL bind the port.
- `AC-HTTP-015.9` (Event-driven): WHEN the server is disabled or disposed while a complete request waits
  for its answer THE SYSTEM SHALL close that request's connection with no response and record nothing
  from it.
- `AC-HTTP-015.10` (Unwanted): IF accepting a connection fails with anything but a socket error THEN THE
  SYSTEM SHALL stop listening while the server stays enabled, and SHALL listen again once the server is
  disabled and enabled, and IF it fails with a socket error THEN THE SYSTEM SHALL go on accepting.
- `AC-HTTP-015.11` (Ubiquitous): THE SYSTEM SHALL register the server factory as a singleton and the
  server as a transient, and SHALL resolve a factory-created server from the container's root, so a
  block's scope ending leaves it serving, its block owns its disposal, and the container disposes it at
  its own disposal whether or not the block already has.

`AC-HTTP-015.2` departs from `AC-MODB-011.2`, which binds every interface, and the reason is the
protocol. Modbus has no transport security by nature, so a hosted Modbus server that serves the network
in clear is the protocol working as designed. This server serves plaintext **by choice**, where HTTP has
TLS and authentication as the norm, and every request it receives meets a request parser the package
wrote itself. Reaching it from off the machine is therefore something a block asks for — setting
`ListenAddress` to an interface, or to `0.0.0.0` for all of them — and not something a block gets by
omission. Nothing about what a production block can do changes; only what it does without saying. The
port is not the protocol's standard 80 because 80 is the port everything else on a host already wants.

`AC-HTTP-015.5` is where a simulator's degrade-on-failure lives: the server throws and stays disabled,
and whether a taken port takes the bench down is the block's decision, made in its own `catch`.

`AC-HTTP-015.8` matters more here than for most servers, because this one closes every connection first
(`AC-HTTP-017.5`), which leaves the lingering socket on the server's side of each exchange. It and
`AC-HTTP-015.5`'s last clause are one binding rule, the hosted Modbus server's too (`AC-MODB-011.3`,
`AC-MODB-014.4`): the listener is bound with no address-reuse option, because a plain bind already rebinds
over lingering connections on both operating systems and an option only adds port sharing — a second
server binds the held port and the kernel splits connections between the two. On Linux both of .NET's
spellings, `ExclusiveAddressUse = false` and `ReuseAddress`, add it; on Windows `ReuseAddress` does. Only on
Linux can the rebind's test fail, because only there is a bind with address reuse cleared refused over a
port's lingering connections; the held-port test fails on Linux under either spelling, and on Windows under
`ReuseAddress` alone.

`AC-HTTP-015.7`'s last clause is the hosted Modbus server's too: disposal ends the socket and not the
route table, so a block's late tick publishing into a disposed server changes a table nothing serves and
throws nothing. Enabling is what a disposed server refuses, and the refusal is the server's own rather
than its transport's, so the HTTP test kit's in-memory transport refuses it exactly as a gateway's socket
does.

`AC-HTTP-015.9` is `AC-HTTP-016.8`'s "once its response has been written in full" met by a stop. A stop
does not wait for a slow client to take a response: it closes every connection still open, so a request
that was waiting for a `Sync` callback was never answered and is not recorded. A response still being
written when the stop comes is cut short by the same close and goes unrecorded by the same rule; that
case is stated by `AC-HTTP-016.8` rather than here, because no test on this suite's desk can hold a
write open (§ Test discipline).

`AC-HTTP-015.10` is what keeps the listener from dying in silence. A socket error accepting one connection
is that connection's problem, and the next is accepted. Anything else is not understood, and retrying it
could spin, so the listener closes and `IsListening` says so while `IsEnabled` still reads true — the pair
a health property compares. Disabling and enabling starts a fresh listener.

`AC-HTTP-015.11` is `AC-MODB-018.3`'s rule for the HTTP server. The factory is a singleton, so the provider
it resolves from is the root, and a server it creates outlives the scope of the block that asked for it:
the block that created a server disposes it, typically in `Stopping`, and the container disposes it again
at process exit, which a second disposal tolerates (`AC-HTTP-015.7`). A server resolved directly into a
block's constructor instead is that block's scope's, as every transient is.

## The hosted server: responses and requests

- `AC-HTTP-016.1` (Ubiquitous): THE SYSTEM SHALL let a block set, replace and remove the response for a
  method and a path, and clear every response, inside a `Sync` callback run on the caller's thread in
  an action form and a value-returning form, and SHALL answer a request arriving while a callback runs
  from the responses that callback leaves.
- `AC-HTTP-016.2` (Ubiquitous): THE SYSTEM SHALL allow `Sync` while the server is disabled.
- `AC-HTTP-016.3` (Event-driven): WHEN `IsEnabled` is set or the server is disposed from inside a `Sync`
  callback, at any nesting depth, THE SYSTEM SHALL throw an `InvalidOperationException`.
- `AC-HTTP-016.4` (Event-driven): WHEN a snapshot is used after the callback it was given to has
  returned THE SYSTEM SHALL throw an `InvalidOperationException`.
- `AC-HTTP-016.5` (Event-driven): WHEN a request arrives for a method and a path with a response set
  THE SYSTEM SHALL answer with that response's status, content type and body.
- `AC-HTTP-016.6` (Event-driven): WHEN a request arrives for a path with no response under any method
  THE SYSTEM SHALL answer 404 with no body, and WHEN the path has responses only under other methods
  THE SYSTEM SHALL answer 405 with an `Allow` header naming them.
- `AC-HTTP-016.7` (Ubiquitous): THE SYSTEM SHALL match a request to a response by its method and by its
  path before any query string, both compared ordinally.
- `AC-HTTP-016.8` (Ubiquitous): THE SYSTEM SHALL record every request once its response has been
  written in full, with its method, path, query, headers, body and arrival instant from the registered
  clock, joining the values of a header sent more than once except a repeated identical
  `Content-Length`, which it keeps once, and SHALL hand each to the block once, in the order it recorded
  them, when the block takes them.
- `AC-HTTP-016.9` (State-driven): WHILE the recorded requests not yet taken are more than the server
  keeps or carry more body bytes than its budget THE SYSTEM SHALL drop the oldest until both hold, SHALL
  drop a request whose body alone is over the budget, and SHALL report how many it dropped since the
  block last took them.
- `AC-HTTP-016.11` (Unwanted): IF a response is set or removed with no method, a path that is empty,
  does not start with `/` or carries a query, or no response, or a response is built with a status
  outside 200 to 599, THEN THE SYSTEM SHALL throw an `ArgumentException` naming the argument.
- `AC-HTTP-016.12` (Unwanted): IF a response is built with a content type carrying a control character
  other than a tab, or a character outside ASCII, THEN THE SYSTEM SHALL throw an `ArgumentException`
  naming the argument.

The model is the hosted Modbus server's with register buffers replaced by responses. The block
publishes what the server serves, on its own cadence, and reads back what was asked the same way; the
server answers from its own threads and **never calls the block** — no event, no callback, no hop onto
the actor. A block that needs an answer computed from the request republishes the route; a block that
must react to a `POST` takes the received requests on its next tick. That is what lets a request
arrive before the block has started, while it is mid-tick or while it is stopping, and be answered
anyway: from whatever was last published, which before anything is a 404.

`AC-HTTP-016.1`'s second clause is why a republish goes inside one callback. A block replacing three
documents in one `Sync` is never seen half done; the same three edits in three callbacks can be.
`AC-HTTP-016.3` is the deadlock it prevents: stopping waits for the requests being answered, and a
request being answered waits for the callback that is holding them off.

`AC-HTTP-016.7`'s edges are the ones an author gets wrong: `/a?x=1` is answered by `/a`, while `/A` and
`/a/` are not, and neither is a `get` for a `GET`. The `Host` header plays no part.

`AC-HTTP-016.8` records on delivery rather than on arrival, so the log is a list of requests a client
was actually answered, and "arrival order" is not a promise it can keep: two connections served at once
finish in either order. For requests one client sends one after another the two orders are the same.

`AC-HTTP-016.9` is what keeps a server a block never drains from growing on a gateway, and it takes both
limits to do it: 256 requests, and four mebibytes of body between them. The count alone would let 256
bodies of the one-mebibyte body cap sit in a gateway's memory; the budget is four times the body cap, so
any body the socket accepts fits it on its own, and only a transport without that cap — none a block
meets — can deliver a body the server drops on arrival. A request's head is bounded by the header cap
and so by the count. The dropped count is the visible half: a block that must see every request reads it
before it takes them, in the same callback.

`AC-HTTP-016.11` refuses 1xx because a 1xx is an interim response: a client receiving one keeps waiting
for the final response, which a server that closes after one response never sends.

`AC-HTTP-016.12` exists because the content type is written into the response's header block as given.
A line break would end that header there and let whatever follows it add headers of its own — which,
for a block that derives the content type from anything a client sent, is response splitting on a
published constructor. A character outside ASCII has no single meaning in a header block at all.

## The hosted server: the wire

- `AC-HTTP-017.1` (Ubiquitous): THE SYSTEM SHALL read a request body of exactly its `Content-Length`,
  taking a request with neither a `Content-Length` nor a transfer encoding to have none, and send each
  response with its `Content-Length`, without a body where its status forbids one, and without its body
  in answer to `HEAD`.
- `AC-HTTP-017.2` (Unwanted): IF a request declares a transfer encoding THEN THE SYSTEM SHALL answer 411
  and record nothing.
- `AC-HTTP-017.3` (Unwanted): IF a request's head — its line and headers, before the blank line that
  ends them — is longer than the header cap THEN THE SYSTEM SHALL answer 431, and IF its declared body is
  longer than the body cap THEN THE SYSTEM SHALL answer 413, recording neither.
- `AC-HTTP-017.4` (Unwanted): IF a request line or a header is malformed, a `Content-Length` is anything
  but digits or is sent twice with different values, or the request names a version other than HTTP/1.0
  or HTTP/1.1, THEN THE SYSTEM SHALL answer 400 and record nothing.
- `AC-HTTP-017.5` (Ubiquitous): THE SYSTEM SHALL answer one request per connection and then close it,
  sending `Connection: close`.
- `AC-HTTP-017.6` (Event-driven): WHEN a client does not complete its request within the read bound of
  connecting THE SYSTEM SHALL close the connection and record nothing, WHEN a client has not closed within
  the read bound of its request being answered THE SYSTEM SHALL close the connection, and THE SYSTEM
  SHALL NOT count against the bound the time a complete request waits for a `Sync` callback.
- `AC-HTTP-017.7` (Event-driven): WHEN a client disconnects before its request is complete THE SYSTEM
  SHALL record nothing from it and go on serving other clients.
- `AC-HTTP-017.8` (Ubiquitous): THE SYSTEM SHALL send every response's status code on its status line,
  with an empty reason phrase for a status it names no phrase for.
- `AC-HTTP-017.9` (Ubiquitous): THE SYSTEM SHALL serve connections concurrently, answering a request on
  one connection while a request on another is still arriving.
- `AC-HTTP-017.10` (State-driven): WHILE the connection limit is reached THE SYSTEM SHALL answer a further
  connection 503 and close it without reading its request.

The wire is deliberately small, and each criterion here is a refusal where a larger server would accept
more. A chunked body, keep-alive and pipelining are all what a device's REST face does not need and a
small parser gets wrong. The limits are sixteen kibibytes of head, one mebibyte of body, sixty-four
connections served at once, and a read bound of ten seconds — a wall-clock bound, because this is the
one place the server meets a real network.

`AC-HTTP-017.3`'s head is decided on its length alone, in one place, so a head of exactly the cap is
served however the network splits it: judged on what had arrived so far, a read that happened to end at
the cap would have refused a head another split served. A declared length too long for any integer is a
body over the cap, not a malformed length.

`AC-HTTP-017.6`'s bound covers the client's two halves of the exchange and not the server's: ten seconds
from connecting to send the request, and ten again from the answer to take the response and close. The
wait for a `Sync` callback between them is the block's time — `AC-HTTP-016.1` lets a request wait for
one — so a callback holding its request up does not cost that request its response.

`AC-HTTP-017.8`'s empty phrase is valid HTTP, and a client reads the code alone; the phrases the server
does name are for the statuses a device face commonly sends and for its own refusals. `AC-HTTP-017.10`
answers before it reads, so a client that has already written its request may see the connection reset
rather than the 503. The limit is local pressure more than remote, since the server listens on loopback
unless told otherwise (`AC-HTTP-015.2`); it exists because every connection holds a header buffer from
the moment it is served.

The server speaks **plain HTTP and authenticates nobody**. That is a choice rather than the protocol's
nature, and it is stated on the published server type as well as here: anything that can reach the port
can read every response and send any request. That is why it listens on loopback until a block names an
interface, and a block serving something a network peer must not see keeps it there or binds a trusted
interface.

## The hosted server: its summary

- `AC-HTTP-021.1` (Ubiquitous): THE SYSTEM SHALL accumulate what the hosted server answers and refuses into a summary readable at any time without a Sync callback, kept for the lifetime of the server instance across disabling and enabling.
- `AC-HTTP-021.2` (Ubiquitous): THE SYSTEM SHALL count, once each response has been written in full, the requests answered from a published response apart from those answered 404 or 405 because nothing was published for them.
- `AC-HTTP-021.3` (Ubiquitous): THE SYSTEM SHALL count separately the requests it refused itself and the connections it refused at the connection limit, and SHALL record the instant and the status of the last refusal of either kind.
- `AC-HTTP-021.4` (Ubiquitous): THE SYSTEM SHALL count every connection it closes with neither a response written in full nor a refusal.
- `AC-HTTP-021.5` (Ubiquitous): THE SYSTEM SHALL count every recorded request dropped from the log over the server's lifetime, independently of the count it reports since the block last took them.
- `AC-HTTP-021.6` (Ubiquitous): THE SYSTEM SHALL report the latest arrival instant among the requests it has recorded, and none before it has recorded one.
- `AC-HTTP-021.7` (Ubiquitous): THE SYSTEM SHALL report how many connections it is serving when the summary is read, not counting a connection refused at the connection limit.
- `AC-HTTP-021.8` (Ubiquitous): THE SYSTEM SHALL record with every request it hands to the block the status it answered that request with.

The server's half of decision `0118`. Like the rest of the server, the summary is fed from the
server's own threads and never calls the block; a block reads it whenever it likes, outside `Sync`, and
publishes it at the cost the client's summary states, with the same 30 s default.

`AC-HTTP-021.2` counts on delivery, the moment `AC-HTTP-016.8` records, so the answered counts and the
log agree, and a request a stop cut short is abandoned rather than answered (`AC-HTTP-015.9`).
Unmatched means nothing was published: a route the block published with a 404 of its own is an
answer.

`AC-HTTP-021.3`'s two kinds are two causes. A refused request is a client sending what the server will
not read (`AC-HTTP-017.2` to `AC-HTTP-017.4`); an overload is more clients than the connection limit
(`AC-HTTP-017.10`). `AC-HTTP-021.4` counts the rest — a client gone before its request was complete,
the read bound, a stop — so every connection the server has finished with is in exactly one of the
answered, unmatched, refused, overloaded and abandoned counts.

`AC-HTTP-021.5` is `AC-HTTP-016.9`'s count over the server's life: the snapshot's own count runs since
the block last took the requests and is what a block reads to know it missed some; the summary's is
what a card shows. `AC-HTTP-021.8` is the per-request fact the summary cannot give per route: with it a
block tallies its routes from what the server answered, rather than by matching its own table again.

Over the HTTP test kit's in-memory transport every request is answered and delivered, so the refused,
overloaded and abandoned counts stay at zero there, and so does the connection count.

## On a stepped development host

- `AC-HTTP-018.1` (State-driven): WHILE the host is stepped THE SYSTEM SHALL deliver an HTTP request's callback to the block before the virtual clock next advances and before a stepped advance in progress returns.
- `AC-HTTP-018.2` (State-driven): WHILE the host is stepped THE SYSTEM SHALL record a request the hosted server answered for a client in the same host before the virtual clock next advances and before a stepped advance in progress returns.

Both are the scenario page's quiescence predicate (`AC-SCEN-012.5`) seen from this package: a request is
counted from the block's call until its callback is handed to the block, and a hosted server's request
from the moment the server has read it in full until it is recorded or its connection ends. A connection
that has not completed a request, and one refused before its request is read, is not counted, so an idle
connection holds no settle. `AC-HTTP-018.2` is worded for a client in the same host because that
client's own request is still counted when the server finishes reading it; a request from outside the
host read after a settle has already returned is not waited for, and neither is one whose client in the
host gave up on it before the server had read it in full.

While a request is counted the stepped clock cannot move, so a per-request timeout (`AC-HTTP-008.1`) —
measured on the registered clock — cannot elapse during it; the client's own timeout (`AC-HTTP-008.2`)
is real time and is what ends a request to a peer that never answers.

## The published surface

- `AC-HTTP-013.1` (Ubiquitous): THE SYSTEM SHALL classify every public type it ships as either
  published surface or internal plumbing.
- `AC-HTTP-013.2` (Ubiquitous): THE SYSTEM SHALL judge its own declarations with the Dale analyzers,
  so a public type in its declared published namespace carrying neither surface mark draws a
  diagnostic in its build.
- `AC-HTTP-013.3` (Ubiquitous): THE SYSTEM SHALL NOT declare itself a shared assembly, so every
  plugin loads its own copy and the client's types are that plugin's alone.
- `AC-HTTP-014.1` (Ubiquitous): THE SYSTEM SHALL target the SDK's cross-platform plugin framework and
  SHALL add logging, JSON, HTTP-factory and clock dependencies to any plugin that takes it.

The published set is what a block author is meant to name: the client interface, the registration
extension, the one exception worth catching by name, the receipt with its outcome, the two summaries,
and the hosted server's factory, server, snapshot, response and request. The concrete client and the two seams it
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
inherits is the logging abstractions, `System.Text.Json`, the HTTP factory and the clock abstraction
`AC-HTTP-008.3` measures on — the last already present in any plugin taking the core SDK. The hosted
server adds none: it is built on a TCP listener and the package's own HTTP/1.1 exchange, which is also
what keeps the package's target unchanged.

## What the package does not do

Stated because a consumer evaluating it will look for each, and finding nothing is a slower way to
learn it than reading it here.

- **No retries, no backoff, no circuit breaker.** A failure is one callback; a block that wants
  another attempt schedules it. `Vion.Examples.Energy`'s `OpenMeteoService` answers with a cache with a fifteen-minute life
  rather than a retry.
- **No link verdict and no connection diagnostics on the client.** The client's summary counts
  outcomes and times round trips but calls no server up or down: one client may call many, and the
  block owns the cadence a verdict needs (decision `0200`). The client's connections are the
  platform's pooled handler's, and nothing surfaces their state.
- **No TLS and no authentication on the hosted server.** It speaks plain HTTP/1.1 and serves anyone
  who can reach the port — loopback only, until a block names an interface — which its published type's
  own documentation says where an author meets it.

## Test discipline

The suite reaches the wire through two client seams, and every row that issues a request takes one of
them: a stub factory's client, where a fixture meets the bounds it sets itself rather than the
registration's, or the composed package — the real `AddDaleHttpSdk` and the real named client, only
the innermost handler replaced — where the timeout, the `User-Agent` and the handler chain are under
test rather than reconstructed. Holding a seam and issuing are separate things: a registration row
composes the package and reads the client's own configuration without sending anything, and a
refusal row is answered at the caller before a request exists. The rows on no seam at all stand where
their claim lives — the client's member-mapping family on a mocked executor, because mapping a member
to an executor call is all those members do; the surface family on the assembly; the serializer
family on content alone.

The executor's rows that do issue drive the real executor over a stub innermost handler and **await
the executor's own task** before asserting; callbacks are then drained from a dispatcher stub that
queues them the way a real actor does. Nothing waits on the wall clock. The discriminator for every timeout
claim is a handler that never answers and honours cancellation — never a delay raced against a
shorter bound ([`../testing-conventions.md`](../testing-conventions.md) § 16), and never a handler
that ignores the token, which reads the opposite of a real one on a zero bound.

The receipt's rows read a status only through the receipt. Every test here targets a runtime where
`HttpRequestException.StatusCode` compiles, so a test reading the exception's own property would pass
with no receipt at all. The transport row throws a plain `HttpRequestException` from the handler — the
class a refused connection arrives as — beside a 404, and a third row throws one carrying a 404 from
the handler, which only a status taken from the judged response reports as none. The invalid rows
throw the client's own refusal classes from a handler too, so only where an exception was raised can
tell them apart.

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

The hosted server's rules are proven over a transport with no socket, which records how the server
drove it and carries a request straight to the server's answer and delivers its response; its wire
criteria are proven over real loopback sockets against the real server, so "record nothing" is read from
the server's own log. The read bound's tests give their server a short bound of its own and make the
bound's expiry the observable — including as the synchronisation point, where a silent client closed by
the bound is the proof that the bound has elapsed for a request waiting beside it. The server reports
a connection's answer or refusal before it closes its side, and a connection the client left before
completing its request before closing it in turn, so for those the client seeing the close is the
synchronisation point; a connection the read bound or a stop ends is closed first and reported as it
unwinds; the connection count falls only after that, and its rows
wait for it under a bound only a hung server reaches. Two rows reach the
socket transport through a seam of its own: the connection limit, which a test sets low, and the accept
call, which a test makes fail, since nothing a client does can make a listener's accept throw.

A response cut short mid-write has no deterministic test on the desk this suite runs on: Windows loopback
accepted a 128-mebibyte response in full into its buffers while the client read none of it, so no write
stays in flight long enough to stop. `AC-HTTP-015.9`'s test stops a request waiting for its answer
instead, which the same stop, and the same record-on-delivery, decide.

Two behaviours are stated on this page and proven by no test in this suite, by rule rather than by
omission. The unhandled-exception continuation that logs a fault escaping a request's own error
handling has a log line as its only observable, and log text is not a contract
([`../testing-conventions.md`](../testing-conventions.md) § 15). And the API manifest is a snapshot CI
regenerates and commits rather than a gate ([`../releasing.md`](../releasing.md)); `AC-HTTP-013.1`
pins the marks, which are testable, and the manifest follows from them.
