---
slug: http-failure-diagnostics
status: proposed           # proposed | in-flight | parked | archived
blocked-on: none           # for parked docs: what's blocking + ref
areas: HTTP
author: jonasbertsch
created: 2026-09-23
updated: 2026-09-23
supersedes: none           # path of a superseded change doc, or none
---

# An HTTP block can tell a status from a transport failure, and publish a summary of its requests

> Change doc — one in-flight change. The `Spec delta` below is distilled into the current-truth
> pages under `docs/specs/` in the implementation PR, then this doc is archived
> (`pwsh scripts/spec-change.ps1 archive <slug>`). Process: `docs/spec-process.md`. Never put
> change narrative inline in a spec page — it lives here.

## At a glance

### Summary

This change covers two backlog items. **VION-226:** a block targeting `netstandard2.1` gets a non-success
status as an `HttpRequestException` whose `StatusCode` it cannot compile against. A refused connection
arrives as the same class, so both known readers read the status by reflection, and one of them also
matches the message text. **VION-221:** no block can publish how its HTTP requests are going without
keeping the tally itself, and the one server example does keep it by hand. The proposal is that every
callback also receives an `HttpReceipt`: the outcome, the status when a response arrived, when the outcome
was observed, and the round trip. Both the client and the hosted server then accumulate what they see into
one flat summary struct that a block publishes as a single `[ServiceProperty]`, which is decision 0118
applied to HTTP. **Stopped for ratification: no code yet.**

### Spec implications

`http.md` gains three sections: the receipt (`AC-HTTP-019.*`), the client summary (`AC-HTTP-020.*`) and the
server summary (`AC-HTTP-021.*`). `AC-HTTP-005.2` changes (`MODIFIED`), because the outcome of a request
whose callback cannot be handed over is no longer "visible only in the log". Under Q3's recommendation,
`AC-HTTP-016.10` moves into the server summary (`REMOVED`). "What the package does not do" loses its "No
link or connection diagnostics" bullet. § The error model gains a paragraph saying that the receipt, and
not the exception, is where a status is told apart from a transport failure. Every class that `AC-HTTP-006.1`
lists stays the same class, and the package still wraps nothing. The page also gains a paragraph on
what publishing a summary costs (Q5). `testkit.md` mints nothing: the HTTP harness composes the real
client (`AC-TKIT-014.1`), so receipts and summaries reach a test with no kit change beyond recompiling
its sample block. `emission.md` mints nothing either. Its prose may gain the always-changing-struct
paragraph (Q5, option ii).

### Decisions

The session's own calls (class b), recorded with reasons. Everything the operator decides is under
Reviewer's questions.

- `D1` — **The status comes from the response and never from the exception.** The executor already
  holds `response.StatusCode` where it judges success (`HttpRequestExecutor.cs:371`). It records that
  value on the receipt, then throws exactly what it throws today. So `AC-HTTP-006.1`'s classes, the
  message the test kit pins (`FakeHttpHarnessShould.cs:33-51`) and the platform's own
  `HttpRequestException.StatusCode` on a .NET 5+ host all stay as they are. The two reflection readers
  therefore keep working until they migrate. An exception a *handler* throws carries no status on the
  receipt, even when that exception holds one, because only a response the SDK judged has a status.
- `D2` — **Timeouts are their own outcome, `Timeout`, for either bound.** A timeout is neither a status
  (no response was judged) nor a transport failure (nothing failed: the SDK gave up). It stays a
  `TimeoutException` (`AC-HTTP-008.1`, `AC-HTTP-008.2`). A failure raised after the bound elapsed keeps
  its own outcome, just as it keeps its own class today (the last clause of `AC-HTTP-006.1`).
- `D3` — **Six outcomes** (§ Full design › The outcome). `Success`, `StatusError`, `ContentError`,
  `Timeout`, `TransportError`, `Invalid`. Each is a place where a block acts differently, and Q2 asks
  whether `StatusError` splits by class.
- `D4` — **The round trip runs from handing the request to the client until the SDK has observed the
  outcome**, which is `ReceivedAt` minus the send instant. For the four typed members it includes
  reading and deserialising the body. For `SendRequest` it ends at the response headers, because the
  body belongs to the callback there (`AC-HTTP-004.2`). This is Modbus's "dispatch to response
  observed", and there is one rule for all eight members.
- `D5` — **The summary's round-trip figures are fed only by outcomes where the server answered**:
  `Success`, `StatusError` and `ContentError`. A timeout's duration is the bound, and a refused
  connect's is close to zero. Either would pull the mean away from what an author reads it as, which is
  how fast the server answers. This is the same exclusion Modbus makes for its locally decided outcomes
  (`AC-MODB-016.5`).
- `D6` — **Every outcome has a lifetime counter.** Modbus keeps eight counters for ten outcomes, and
  `AC-MODB-016.4` needed rewording because of it. With six outcomes, one counter each is a rule nobody
  has to memorise.
- `D7` — **The window, clock and snapshot rules are Modbus's, because the property they rest on
  holds here too.** The property is that a summary is published as one live value and nothing
  downstream keeps its history (`modbus.md` prose after `AC-MODB-016.9`). So the rules carry over:
  the same 15-to-16-minute window, the same empty-window reading, the lifetime maximum with its instant
  and only a strictly larger value moving it. The window rolls on the registered clock, which is
  virtual under the test kit and a stepped host (`AC-HTTP-008.3`). A read is a consistent snapshot,
  taken without blocking a request that is updating it.
- `D8` — **The receipt is recorded, and the summary updated, before the callback is handed to the
  actor, whether or not a callback was given.** Updating inside the callback would lag by the depth of
  the mailbox. That is 0118's `LastTransaction` staleness in a new form (0118, alternatives). A request
  with no error callback (`AC-HTTP-006.2`), or whose block has no actor yet (`AC-HTTP-005.2`), still
  counts.
- `D9` — **A call refused at the caller is not a request.** The four refusals in `AC-HTTP-007.*` throw
  before any exchange exists, so they produce no receipt and no count. `Invalid` covers what the
  *client* refuses once the request has been issued: a URL that is not absolute and that no base
  address resolves, a disposed client, and a `configureClient` that throws (`AC-HTTP-001.4`).
- `D10` — **Nothing is published by the SDK itself.** The summary is a pull property, the same as
  Modbus `Link`. Whether, how often and at what `MinInterval` it reaches the broker is the author's
  declaration (Q5).

### Reviewer's questions

Pre-classified per `docs/spec-process.md` § Lane 2. **(c)** items wait for the operator. **(b)** items
are decided above, with the decision named. **(a)** items are ratified and cited.

1. **(c) VION-226's shape.** How does a block tell a status from a transport failure?
   - **(i) A receipt in the callback signature** — `Action<T, HttpReceipt>` / `Action<Exception,
     HttpReceipt>` on all eight members, with no compatibility overloads. This is decision 0119
     applied literally ("the next SDK client that delivers results through callbacks carries its facts
     the same way"). Four call sites migrate across every tree on this machine. The same receipt feeds
     VION-221.
   - **(ii) A status-carrying exception**, `HttpStatusException : HttpRequestException` with its own
     status property, which is the reporter's suggestion. It is not breaking for a `catch`. But from
     `netstandard2.1` it cannot set the base `StatusCode` (probed, § Full design), so each reflection
     reader silently reads null and the example block misfiles every 404 as a transport failure. It
     also leaves the success side with nothing, and VION-221 needs a receipt-shaped fact regardless.
   - **(iii) Both.** This gives two carriers of one fact.
   - **Recommendation: (i).** The names are part of the question: `HttpReceipt` and `HttpOutcome`,
     which parallel `ModbusReceipt` and `ModbusOutcome`.
2. **(c) The client summary's shape.**
   - **(a) Scope.** One summary per client instance. The client is transient, so a block that talks to
     two services and wants them apart injects two clients (`AC-HTTP-001.1`). **Recommended.** The
     alternatives are one summary per origin, which is a keyed set that no flat struct can carry, and
     a named tracker passed on each call, which adds a parameter to eight members.
   - **(b) Verdict.** Decision 0118 names "a link verdict". HTTP has no link: one client may address
     many hosts, and a pooled handler hides the connections. The survey found an up/down verdict only in
     uptime monitors, which probe one URL. **Recommendation: no `State`.** Publish `LastResponseAt` and
     the last failure instead, and leave the cadence owner to derive the verdict, which is 0118's own
     rule against time decay. This departs from the letter of 0118, and if it is ratified the
     coordinator records it as a successor note. The alternative is a `State` of `Unknown / Responding
     / Unreachable` that moves only on outcomes that reached a server.
   - **(c) Status errors.** Either one `StatusErrorCount` together with the last failure's status code
     (**recommended**, the smallest surface), or counts split into 4xx and 5xx. The split is what every
     HTTP tool surveyed shows. It costs one field more, plus a home for the rare unfollowed 3xx.
   - **(d) Names.** `HttpClientSummary`, read as `ILogicBlockHttpClient.Summary`. The Modbus word is
     `Link`, and HTTP has none (see (b)).
3. **(c) The server summary.**
   - **(a) Include it.** **Recommended.** `HttpSimServer` keeps a request count, an unmatched count and a
     cumulative dropped count by hand today (`HttpSimServer.cs:126-159`, `:338-359`). It re-derives
     "unmatched" by re-running the server's route match against its own table, and its own comment
     admits the result is approximate when a route changes between the answer and the tick.
   - **(b) Fields.** The recommended set is: answered from a published response, answered 404/405,
     refused by the server itself (400, 411, 413, 431), refused at the connection limit (503), closed
     with no response written, dropped from the log over the server's lifetime, last request, and last
     refusal with its status. Service time is left out. On this server it is mostly the wait for `Sync`,
     which no author tunes. Add it only if the operator wants parity with the client's round trip.
   - **(c) Relation to `AC-HTTP-016.9` and `AC-HTTP-016.10`.** Keep the snapshot's dropped count, which
     counts since the last take and is the block's working interface to the log, and add a lifetime
     `DroppedCount` to the summary. **Move** `LastRequestAt` into the summary by deleting the server
     member, which is delete-don't-deprecate and keeps one place for one fact (**recommended**). The
     alternative is to keep both.
   - **(d) The per-request fact.** Optionally, give `HttpServerRequest` the status it was answered
     with. That is the server's analogue of a receipt, and it would make the example's per-route
     tables exact. **Recommendation: yes**, because it is the one fact the summary cannot give per
     route.
   - **(e) Name.** `HttpServerSummary`, read as `ILogicBlockHttpServer.Summary`.
4. **(b) The outcome taxonomy and where timeouts go.** Decided in `D2`, `D3` and `D9`. Listed so that
   the operator sees it before code: overriding it later changes a published enum.
5. **(c) Emission.**
   - **(i) Prose only (recommended).** `http.md` states the cost and what to set (§ Full design ›
     Emission), with no SDK default changed. There is no per-type default to change: `MinInterval` is
     only ever declared by the author.
   - **(ii) Also state the rule once in `emission.md`.** The rule is that a struct whose counters move
     on every transaction defeats the dedup floor. `modbus.md:21` already cites `emission.md` "for what
     publishing a diagnostics struct as a `[ServiceProperty]` costs", and `emission.md` says nothing of
     the kind (Drift checkpoints). **Recommended in addition to (i)**, because it is one paragraph and
     closes a citation that is dangling today.
   - **(iii) An analyzer.** `docs/sdk-surface-conventions.md` § 4 says that every author-facing rule
     ships with one, for example an info-level diagnostic on a summary-typed member that keeps the
     250 ms default. **Recommendation: file it, not in this PR.** It would cover `ModbusLinkSummary`
     too, which puts it outside this change's area (brief constraint 6).

---

## Full design

### 1. What happens today (verified this session)

| Fact | Evidence |
| --- | --- |
| Status is judged by `response.EnsureSuccessStatusCode()` on the headers. The failed response is then disposed and the platform's exception rethrown. | `Vion.Dale.Sdk.Http/HttpRequestExecutor.cs:369-382` |
| Every failure path funnels through one method before the error callback is handed over. | `HttpRequestExecutor.cs:387-409` (`HandleException`) |
| The three send paths are the only places a response exists. | `HttpRequestExecutor.cs:157-278` |
| The package targets `netstandard2.1`. | `Vion.Dale.Sdk.Http/Vion.Dale.Sdk.Http.csproj:4` |
| Every test project targets `net10.0`, where `HttpRequestException.StatusCode` compiles. | the brief's mask. Re-derived for the two HTTP test projects in the implementation, not here |
| The server already has `LastRequestAt` and a take-relative dropped count. | `Server/ILogicBlockHttpServer.cs` (`LastRequestAt`); `AC-HTTP-016.9`, `AC-HTTP-016.10` |
| The example block reads `StatusCode` by reflection, measures latency itself, and treats `TimeoutException` separately. | `examples/Vion.Examples.Http/Vion.Examples.Http/LogicBlocks/HttpDebugClient.cs:399-433` |
| The consumer reads `StatusCode` by reflection and falls back to finding `"404"` in the message. A 404 is a commissioning fact there: the gateway serves no file for a device it has never read. | `logic-block-libraries/…/EmuMCenter/Discovery/DiscoveringMeterRosterSource.cs:360-394` |
| The server example keeps request, unmatched and cumulative dropped counts by hand. | `HttpSimServer.cs:126-159`, `:338-359` |
| The first consumer's cadence for always-moving diagnostics structs is 30 s. The stated reason is that such a summary "defeats the value-equality dedup floor … a permanent broker emitter", and 30 s "keeps a whole fleet of them below ~1 msg/s". | `logic-block-libraries/…/Ecocoach.EnergyManagement.Common/Emission/EmissionDefaults.cs:80-88`; applied at `…/Shared/IModbusTcpDiagnostics.cs:39-44` |

**The `[assumed]` claim is now verified.** It was probed on Windows with the .NET 10 runtime, using a
`netstandard2.1` class library consumed by a `net10.0` console app. The probe is in the session
scratchpad and was not committed.

- The reference `netstandard.xml` for 2.1 declares three `HttpRequestException` constructors, `()`,
  `(string)` and `(string, Exception)`, and no `StatusCode`. Reading `e.StatusCode` from a
  `netstandard2.1` project fails to compile with `CS1061`.
- A subclass `StatusException : HttpRequestException` that sets its own `Status` through
  `base(message, null)` reads **`null`** from the base `StatusCode` at runtime, whether read directly
  or through `GetType().GetProperty("StatusCode")`.
- A subclass that *hides* `StatusCode` with its own property reads `NotFound` through
  `GetProperty("StatusCode")`, because reflection returns the most-derived property. It still reads
  `null` through the base type.
- `EnsureSuccessStatusCode()` on a 404 response, run on the .NET 10 runtime, throws an
  `HttpRequestException` whose `StatusCode` is `NotFound`. This is why the reflection readers work
  today, and why `D1` keeps that exception.

The probe ran on Windows only. None of these facts depends on the operating system, because they are
about type metadata and not sockets, so no Linux run is owed.

**Consumer sweep** (read-only, the `Explore` agent's report):

- Across `logic-block-libraries`, `examples/`, `libraries/` and `templates/`, `ILogicBlockHttpClient` has
  **four** call sites: three `GetJson` (`DiscoveringMeterRosterSource.cs:163`,
  `GeolocationService.cs:36`, `OpenMeteoService.cs:81`) and one `SendRequest` (`HttpDebugClient.cs:205`).
- `PostJson`, `PutJson`, `DeleteJson` and `Delete` have no callers.
- The agent's grep lines were pasted into the session and are re-derived in the implementation
  (Tasks `T-000`).
- `logic-block-libraries` also mocks the client with Moq (`EmuMCenterSourceGateShould.cs:243`, among
  others). Those `Setup` expressions recompile once the signatures change.
- No block keeps success or failure counters or an Online/Offline state for HTTP. `HttpDebugClient` is
  the only one that measures latency.

### 2. What HTTP tools surface (the survey constraint 2 asks for)

"On par with Modbus" means analogous, so the fields come from what HTTP tooling itself shows. The survey
is from documentation the session already knew, and none of it was re-fetched this session:

| Tool | Per-request facts | Aggregates |
| --- | --- | --- |
| curl `-w` | `http_code`, `exitcode`/`errormsg` (transport), `time_total`, `time_starttransfer`, phase times | — |
| .NET `System.Net.Http` metrics (8+) | `http.response.status_code`, `error.type` (a status code, or a transport-error name such as `connection_error` or `name_resolution_error`, or `timeout`) | `http.client.request.duration` histogram, `http.client.active_requests`, `http.client.request.time_in_queue`, open connections |
| OpenTelemetry HTTP semantic conventions | the same attributes: status code, and `error.type` that separates a status from a transport error | duration histogram by status code |
| k6 | status, `error_code` | `http_reqs`, `http_req_failed` rate, `http_req_duration` avg/min/med/max/p90/p95 |
| Prometheus blackbox exporter | `probe_http_status_code`, `probe_success` | `probe_duration_seconds` by phase |
| Uptime monitors (Uptime Kuma, UptimeRobot) | last status, response time | up/down per probed URL, average response time, "down since" |
| nginx `stub_status` / ASP.NET Core server metrics | status per request (access log) | requests, accepted, handled, active connections; `http.server.request.duration` by status; Kestrel rejected connections |

Four things carry over to this package:

- **A status and a transport failure are two different axes.** Every tool reports the status when a
  response arrived and a separate error kind when none did. That is exactly VION-226, and it is what
  `HttpOutcome` and `HttpReceipt.StatusCode` encode together.
- **Timeouts are named on their own** (`error.type = timeout`, curl exit 28). That is `D2`.
- **Duration is the headline aggregate.** Tools with a histogram show percentiles. This package keeps
  Modbus's mean, max and count over a window (`D7`), because a flat struct cannot carry a histogram and
  the cloud keeps no history of it.
- **An up/down verdict appears only where one URL is probed.** That is the evidence behind Q2(b)'s
  recommendation not to have one.

**What does not carry over** from Modbus: queued wait (`http.client.request.time_in_queue` is the pool's
queue, which the package cannot observe through `IHttpClientFactory`), the connection summary (the pooled
handler owns the connections, `AC-HTTP-002.1`), and backoff or expiry (the package retries nothing).
Adding any of these would be the "unnatural for HTTP" the analysis warns against.

### 3. The receipt (Q1 (i))

```csharp
[PublicApi]
public readonly record struct HttpReceipt(DateTime ReceivedAt,          // UTC, the instant the outcome was observed
                                          long ReceivedTimestamp,       // the same instant, monotonic scale
                                          TimeSpan RoundTrip,           // D4
                                          HttpOutcome Outcome,
                                          HttpStatusCode? StatusCode);  // the status of the response, when one arrived
```

The member shapes after the change are listed below. There are no overloads, and the optional-callback
defaults do not change:

| Member | Success callback | Error callback |
| --- | --- | --- |
| `GetJson`, `PostJson<TReq,TRes>`, `PutJson<TReq,TRes>`, `DeleteJson<TRes>` | `Action<TResponse, HttpReceipt>` | `Action<Exception, HttpReceipt>?` |
| `PostJson<TReq>`, `PutJson<TReq>`, `Delete` | `Action<HttpReceipt>?` | `Action<Exception, HttpReceipt>?` |
| `SendRequest` | `Action<HttpResponseMessage, HttpReceipt>?` | `Action<Exception, HttpReceipt>?` |

The consumer's `IsNotFound` becomes `receipt.StatusCode == HttpStatusCode.NotFound`. It needs no class
test, no reflection and no message match. A block that ignores the receipt discards it: `(v, _) => …`.

**Where it is built.** The receipt is built at the funnel that every outcome already passes through:
the success paths just before `TryInvokeCallback`, and `HandleException` for failures. The status is
captured where it is judged (`D1`). `Invalid` (`D9`) needs to know whether the client accepted the
request. The package adds no *primary* handler (`AC-HTTP-002.1`). The implementation chooses between
two mechanisms. The first is an outermost `DelegatingHandler` that marks the request as dispatched. The
second is catching `InvalidOperationException` and `ObjectDisposedException` only where they come from
the client's own preparation, together with `CreateClient`'s throw. The first is exact. The second
misfiles a handler that throws either class. The choice is recorded as a Drift checkpoint.

### 4. The outcome (`D3`)

| `HttpOutcome` | When | `StatusCode` | Severity |
| --- | --- | --- | --- |
| `Success` | a 2xx whose content, where the member reads it, was read | the 2xx | Success |
| `StatusError` | a response outside 2xx: 4xx, 5xx, or an unfollowed 1xx/3xx | that status | Warning |
| `ContentError` | a 2xx whose body is absent or malformed, or deserialises to null (`JsonException`, `ContentNullAfterDeserializationException`) | the 2xx | Error |
| `Timeout` | either bound elapsed (`TimeoutException`) | the status, if headers had arrived before a later body read timed out; otherwise none | Error |
| `TransportError` | anything the handler threw, or the body stream broke | none, or the status if the break came after the headers | Error |
| `Invalid` | the client refused the issued request before sending (`D9`) | none | Warning |

Severities follow `ModbusOutcome`. A status error means a server answered and the request or the server
was wrong, so it is Warning, the way `DeviceError` is. The enum carries `[EnumLabel]` and `[Severity]`
like `ModbusOutcome`, so the summary renders without a projection.

### 5. The client summary (Q2)

The recommended set is 16 fields, flat, and every field is legal on a service property. Types and titles
follow `ModbusLinkSummary`:

`LastResponseAt` (any status) · `LastFailureAt` · `LastFailureOutcome` · `LastFailureStatusCode` (`int?`) ·
`SuccessCount` · `StatusErrorCount` · `ContentErrorCount` · `TimeoutCount` · `TransportErrorCount` ·
`InvalidCount` · `RecentRoundTripCount` · `RecentMeanRoundTrip` · `RecentMaxRoundTrip` · `MaxRoundTrip` ·
`MaxRoundTripAt` · `InFlightCount` (issued and not yet completed, the analogue of
`http.client.active_requests`).

`LastFailureStatusCode` is an `int?` rather than `HttpStatusCode?`. That keeps a platform enum without
labels out of the introspection document, where it would render member names such as
`NotFound`/`ServiceUnavailable` in a dashboard enum control.

State interactions that the criteria must state:

- A callback that cannot be handed over (`AC-HTTP-005.2`) is still counted (`D8`).
- A request with no error callback is still counted (`D8`).
- A timeout raised after the bound has elapsed keeps its outcome (`D2`).
- `InFlightCount` falls when the outcome is recorded, before the callback is handed over.
- Disposing the client does not reset the summary. Requests issued after disposal are `Invalid`.

### 6. The server summary (Q3)

The recommended set is `LastRequestAt` (moved, Q3 (c)) · `AnsweredCount` (from a published response) ·
`UnmatchedCount` (404/405) · `RefusedCount` (400/411/413/431) · `OverloadedCount` (503 at the connection
limit) · `AbandonedCount` (closed with no response written in full: an incomplete request, the read
bound, a disconnect, or a stop while waiting, `AC-HTTP-015.9`) · `DroppedCount` (lifetime) ·
`LastRefusalAt` · `LastRefusalStatus` (`int?`) · `ActiveConnections`.

- The summary is readable without `Sync` and at any time.
- It survives disable and enable for the lifetime of the server instance.
- It is fed from the transport's threads and never calls the block, which is the server's model
  (§ The hosted server: responses and requests).
- It is updated when the outcome happens, not when the block takes the log. So `AnsweredCount +
  UnmatchedCount` matches `AC-HTTP-016.8`'s record-on-delivery.

### 7. Emission: what publishing a summary costs (Q5)

**The mechanism.** A summary changes on every completed request, because at least one counter moves. So
value-equality dedup never suppresses it (`AC-EMIT-004.*`), and there is no deadband for a struct. Its
publish rate is therefore the lower of two rates: how often the block assigns it, and one per
`MinInterval`. The throttle is leading-edge with a trailing release (`AC-EMIT-005.1`–`.4`). That means
the newest value is always published at most one interval late, and a longer interval loses nothing
except intermediate snapshots nobody keeps.

**What it costs on a busy client.** These figures follow from the throttle's semantics:

| Block assigns the summary | Requests | `MinInterval` | Publishes per summary |
| --- | --- | --- | --- |
| in every callback | 4/s or more | default `250ms` | 4/s, which is 14,400 an hour |
| in every callback | 1/s (a 1 Hz poll) | default `250ms` | 1/s, which is 3,600 an hour |
| in every callback | any | `30s` | at most 120 an hour |
| on a `[Timer(30)]` tick | any | `30s` | at most 120 an hour |

The payload is the whole struct every time. At ~16 fields it is several hundred bytes of JSON (inferred
from the field count, not measured).

**What an author sets, stated in `http.md` and on the types' remarks:**

- Declare `MinInterval` in seconds, not milliseconds. `"30s"` is the first consumer's own cadence for
  exactly this shape of property.
- Assign the summary from the block's own tick rather than from every callback.
- Never use `Immediate`.
- Use the receipt, not the summary, for a signal whose every edge matters. The receipt comes with the
  callback of the value it describes.
- Do not publish a summary as a `[ServiceMeasuringPoint]`. A 16-field time series of lifetime counters
  is what a meter observes (0118, operator-side OTel, deferred), not what a block publishes.

### 8. Migration (Affects others)

- **Compiler-guided, with no behaviour change at a site that ignores the receipt** (0119).
  - Add `, _` to each lambda.
  - A method group gains a parameter.
  - A Moq `Setup` over `It.IsAny<Action<T>>()` becomes `It.IsAny<Action<T, HttpReceipt>>()`.
- **`logic-block-libraries` first.**
  - One `GetJson` in `DiscoveringMeterRosterSource.cs`, where `IsNotFound` becomes one comparison and
    its reflection and message fallback are deleted.
  - The Moq setups in the EmuMCenter tests.
  - The simulator's server face does not change unless Q3 (c) moves `LastRequestAt`. It does not read
    that property today.
- **Examples** (the post-release bump, not this PR, brief constraint 5):
  - `HttpDebugClient`: delete `AnsweredStatus` and the hand-measured `LatencyMs`, and take them from the
    receipt. Publish `ILogicBlockHttpClient.Summary` as `[ServiceProperty(MinInterval = "30s")]`,
    assigned on a timer tick.
  - `HttpSimServer`: delete the hand counts `RequestCount`, `UnmatchedRequestCount` and the cumulative
    `DroppedRequestCount`, and publish `ILogicBlockHttpServer.Summary` at `"30s"`. With Q3 (d), take the
    per-route tallies from the recorded status and not from re-matching.
  - `GeolocationService` and `OpenMeteoService` only gain discards.

### 9. Proof plan, and the mask

Every test project targets `net10.0`, where the base `HttpRequestException.StatusCode` compiles. A test
that reads it passes with no SDK change at all. So:

- **Status is read only through `HttpReceipt.StatusCode`**, which the package declares, and the
  outcome only through `HttpOutcome`.
- **The discriminating fixture mirrors the consumer.** The transport case is the stub handler throwing
  a plain `HttpRequestException`, the class a refused connection arrives as (`HttpDebugClient.cs:425`).
  The status case is a 404 response. A fixture that throws some other class would separate the two
  vacuously.
- **A third row closes the reverse mask.** The handler throws `new HttpRequestException(message, null,
  HttpStatusCode.NotFound)`, which is legal on `net10.0`. The expected result is `TransportError` with
  no status. An implementation that read the status off the exception, rather than off the response it
  judged (`D1`), would report 404 and turn this row red.
- **Mutations to observe**:
  - capture no status, so the 404 row reddens;
  - read the exception's status, so the third row reddens;
  - feed the round trip from timeouts, so a `D5` row reddens;
  - update the summary inside the callback, so a row that asserts the summary before
    `FlushPendingActions` reddens.
- **The window and clock rows** drive the harness's virtual clock, as `AC-MODB-016.7`'s tests do.
  Nothing waits on the wall clock.

---

## Drift checkpoints

- 2026-09-23: The brief's `[assumed]` claim was verified (§ Full design › 1, the probe). It holds in
  full: a subclass cannot set the base `StatusCode` from `netstandard2.1`. The probe also found that a
  *hiding* subclass keeps name-based reflection working, a fact the brief did not have. It is recorded
  against Q1 (ii) and changes no recommendation.
- 2026-09-23: The brief's line references were re-read and hold: `HttpRequestExecutor.cs:371`,
  `FakeHttpHarnessShould.cs:33-51`, `HttpDebugClient.cs:425` and `:430-433`, and
  `DiscoveringMeterRosterSource.cs:362-364` (the `<summary>` of `Reject`) and `:380-394`.
- 2026-09-23: `modbus.md:21` cites `emission.md` "for what publishing a diagnostics struct as a
  `[ServiceProperty]` costs and when it emits". `emission.md` states nothing specific to structs. A grep
  of the page for `struct` finds only unrelated hits (`:43`, `:203`). This is the operator's emission
  concern, already unanswered for Modbus. It is carried as Q5 (ii).

---

## Spec delta (to distill)

> **Proposed text, pending ratification.** The ids and wording follow Q1 (i) and the recommended
> options, and are rewritten from the operator's answers before the status flips to `in-flight`.

- ADDED AC-HTTP-019.1 -> docs/specs/http.md : THE SYSTEM SHALL hand every success callback and every error callback of the eight request members a receipt of that request alongside its value, response or exception.
- ADDED AC-HTTP-019.2 -> docs/specs/http.md : THE SYSTEM SHALL report on the receipt a success for a 2xx response whose content was read, a status error for a response outside 2xx, a content error for a 2xx response whose body is absent, malformed or deserializes to null, a timeout where either bound elapsed, an invalid request where the client refused the issued request before sending it, and a transport error for any other failure.
- ADDED AC-HTTP-019.3 -> docs/specs/http.md : THE SYSTEM SHALL carry on the receipt the status of the response it judged whenever one arrived, and no status when none did, including when the exception a handler threw carries one.
- ADDED AC-HTTP-019.4 -> docs/specs/http.md : THE SYSTEM SHALL stamp the receipt, on the registered clock, with the instant the outcome was observed on the wall clock and on the monotonic timestamp scale, and with the time from handing the request to the client until that instant.
- ADDED AC-HTTP-020.1 -> docs/specs/http.md : THE SYSTEM SHALL accumulate every issued request of one client instance into a summary readable at any time without blocking a request that is updating it, before the request's callback is handed to the block and whether or not a callback was given.
- ADDED AC-HTTP-020.2 -> docs/specs/http.md : THE SYSTEM SHALL keep a lifetime counter for every outcome and never reset it.
- ADDED AC-HTTP-020.3 -> docs/specs/http.md : THE SYSTEM SHALL record every outcome but a success as the last failure, with its instant and its status where a response arrived, and SHALL record the instant of the last response of any status.
- ADDED AC-HTTP-020.4 -> docs/specs/http.md : THE SYSTEM SHALL feed every round-trip figure only from successes, status errors and content errors.
- ADDED AC-HTTP-020.5 -> docs/specs/http.md : THE SYSTEM SHALL report the mean, the maximum and the number of round trips over at least the last 15 minutes and less than the last 16 on the registered clock, or over the client's whole life where that is shorter, reporting the mean and maximum as empty and the count as zero while none falls within it.
- ADDED AC-HTTP-020.6 -> docs/specs/http.md : THE SYSTEM SHALL report the lifetime maximum round trip with the instant its request was observed, and SHALL move that instant only when a strictly larger value is recorded.
- ADDED AC-HTTP-020.7 -> docs/specs/http.md : THE SYSTEM SHALL report how many issued requests have not yet had their outcome recorded.
- ADDED AC-HTTP-021.1 -> docs/specs/http.md : THE SYSTEM SHALL accumulate what the hosted server answers and refuses into a summary readable at any time without a Sync callback, kept for the lifetime of the server instance across disabling and enabling.
- ADDED AC-HTTP-021.2 -> docs/specs/http.md : THE SYSTEM SHALL count separately the requests answered from a published response, the requests answered 404 or 405, the requests the server refused itself, the connections refused at the connection limit, and the connections closed without a response written in full.
- ADDED AC-HTTP-021.3 -> docs/specs/http.md : THE SYSTEM SHALL count every recorded request dropped from the log over the server's lifetime, independently of the count it reports since the block last took them.
- ADDED AC-HTTP-021.4 -> docs/specs/http.md : THE SYSTEM SHALL report the latest arrival instant among the requests it has recorded, none before it has recorded one, and the instant and status of the last refusal.
- ADDED AC-HTTP-021.5 -> docs/specs/http.md : THE SYSTEM SHALL record with every request it hands to the block the status it answered that request with.
- MODIFIED AC-HTTP-005.2 -> docs/specs/http.md : WHEN the block has not yet received its first message THE SYSTEM SHALL run neither callback and SHALL leave the outcome of the request visible only in the log and in the client's summary.
- REMOVED AC-HTTP-016.10 -> docs/specs/http.md : moved into the server summary as AC-HTTP-021.4 when Q3 (c) is ratified as "move"; the id is not reused.

---

## Tasks

> Written against the recommended options and rewritten from the ratification. One commit each.

- `T-000`: re-derive the consumer and call-site counts with pasted commands, and the test projects'
  target frameworks. Record them as a Drift checkpoint. Flip the status to `in-flight`.
- `T-001` (`AC-HTTP-019.1`, `AC-HTTP-019.2`, `AC-HTTP-019.3`, `AC-HTTP-019.4`): add `HttpReceipt` and
  `HttpOutcome`, give the eight members and `IHttpRequestExecutor` the receipt-bearing callbacks, build
  the receipt at the funnel, add the three-row discriminating fixture, and update the XML docs.
- `T-002` (`AC-HTTP-005.2`, `AC-HTTP-020.1`–`.7`): add the client accumulator and `HttpClientSummary`.
- `T-003` (`AC-HTTP-021.1`–`.5`): add the server accumulator and `HttpServerSummary`, move
  `LastRequestAt`, and add `HttpServerRequest.StatusCode`.
- `T-004`: migrate the test kit's own sample block and tests, and the `http.md` prose (§ The error
  model, § What the package does not do, the emission paragraph, and § Test discipline for the mask).
  Write the `emission.md` paragraph if Q5 (ii) is taken.
- `T-005`: distill, archive, run the pre-PR obligations, and the lane-2 review round
  (`/vion-git:pr docs/changes/2026-09-23-http-failure-diagnostics.md`).

---

## Relay notes for the PR body

> Filled as each consumer-visible change lands. Nothing has landed yet.

- _(none yet)_
