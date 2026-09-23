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
keeping the tally itself, and the one server example does keep it by hand. Every callback now also
receives an `HttpReceipt`: the outcome, the status when a response arrived, when the outcome was
observed, and the round trip. Both the client and the hosted server accumulate what they see into one
flat summary struct that a block publishes as a single `[ServiceProperty]`, which is decision 0118
applied to HTTP. Ratified by amendment 1 (2026-09-23).

### Spec implications

- **`http.md`** gains three sections:
  - the receipt (`AC-HTTP-019.*`);
  - the client summary (`AC-HTTP-020.*`);
  - the server summary (`AC-HTTP-021.*`).
- **Changed criteria:**
  - `AC-HTTP-005.2` changes (`MODIFIED`), because the outcome of a request whose callback cannot be
    handed over is no longer "visible only in the log".
  - `AC-HTTP-016.10` moves into the server summary as `AC-HTTP-021.6` (`REMOVED`).
- **Prose on the same page:**
  - § What the package does not do loses its "No link or connection diagnostics" bullet.
  - § The error model gains the paragraph saying that the receipt, and not the exception, is where a
    status is told apart from a transport failure. Every class `AC-HTTP-006.1` lists stays the same
    class, and the package still wraps nothing and composes no handler (`http.md:14`).
  - The page gains the cost of publishing a summary.
- **`emission.md`** gains one prose paragraph on structs that change on every transaction, and mints
  nothing (Q5).
- **`testkit.md`** mints nothing. The HTTP harness composes the real client (`AC-TKIT-014.1`), so
  receipts and the client summary reach a test with no kit change beyond recompiling its sample block
  and forwarding the summary through the kit's tracking executor.

### Decisions

The session's own calls (class b), recorded with reasons. What the operator ratified is under
Reviewer's questions.

- `D1` — **The status comes from the response and never from the exception.**
  - The executor already holds `response.StatusCode` where it judges success
    (`HttpRequestExecutor.cs:371`). It records that value on the receipt and then throws exactly what it
    throws today.
  - So these all stay as they are: `AC-HTTP-006.1`'s classes, the message the test kit pins
    (`FakeHttpHarnessShould.cs:33-51`), and the platform's own `HttpRequestException.StatusCode` on a
    .NET 5+ host. The two reflection readers keep working until they migrate.
  - An exception a *handler* throws carries no status on the receipt, even when that exception holds
    one.
- `D2` — **Timeouts are their own outcome, `Timeout`, for either bound.**
  - A timeout is neither a status nor a transport failure.
  - It stays a `TimeoutException` (`AC-HTTP-008.1`, `AC-HTTP-008.2`).
  - A failure raised after the bound elapsed keeps its own outcome, just as it keeps its own class
    today.
- `D3` — **Seven outcomes:** `Success`, `ClientError`, `ServerError`, `ContentError`, `Timeout`,
  `TransportError`, `Invalid`. `HttpOutcome` itself splits the status, and not just the summary's
  counters (amendment 1, 2(c), second (b) item). A consumer then reads the class from one enum on the
  receipt, where it acts, and `D6`'s one-counter-per-outcome rule holds without an exception.
- `D4` — **The round trip runs from handing the request to the client until the SDK has observed the
  outcome**, which is `ReceivedAt` minus the send instant.
  - For the four typed members it includes reading and deserialising the body.
  - For `SendRequest` it ends at the response headers, because the body belongs to the callback
    (`AC-HTTP-004.2`).
- `D5` — **The summary's round-trip figures are fed only by outcomes where a server answered**:
  `Success`, `ClientError`, `ServerError`, `ContentError`.
  - A timeout's duration is the bound, and a refused connect's is close to zero. Either would pull the
    mean away from how fast the server answers.
  - This is the same exclusion `AC-MODB-016.5` makes.
- `D6` — **Every outcome has a lifetime counter.** Restated under amendment 1: seven outcomes give seven
  counters, with no outcome left uncounted and none counted twice.
- `D7` — **The window, clock and snapshot rules are Modbus's.** The property they rest on holds here
  too: a summary is published as one live value and nothing downstream keeps its history. So the rules
  carry over:
  - the same 15-to-16-minute window and empty-window reading;
  - a lifetime maximum with its instant, moved only by a strictly larger value;
  - the window rolls on the registered clock (`AC-HTTP-008.3`);
  - a read is a consistent snapshot, taken under one lock.
- `D8` — **The receipt is recorded, and the summary updated, before the callback is handed to the
  actor, whether or not a callback was given.**
  - Updating inside the callback would lag by the depth of the mailbox, which is 0118's
    `LastTransaction` staleness.
  - A request with no error callback (`AC-HTTP-006.2`), or whose block has no actor yet
    (`AC-HTTP-005.2`), still counts.
- `D9` — **A call refused at the caller is not a request.**
  - The four refusals in `AC-HTTP-007.*` throw before any exchange exists, so they produce no receipt
    and no count.
  - `Invalid` covers what fails after the call has returned and before any handler sees the request:
    building the client (a `configureClient` that throws, `AC-HTTP-001.4`, or a disposed container),
    building the request, and the client's own refusal (a URL no base address makes absolute, a
    disposed client, a message that was already sent).
- `D10` — **Nothing is published by the SDK itself.** Both summaries are pull properties, the same as
  Modbus `Link`. Whether, how often and at what `MinInterval` a summary reaches the broker is the
  author's declaration.
- `D11` — **An unfollowed 1xx or 3xx is a client error** (amendment 1, 2(c), first (b) item). The rule
  is that 500 and above is a server error and any other non-success status is a client error, so the
  two partition every status.
  - A 3xx reaches the block only when the platform did not follow it: its redirect limit ran out, the
    response carried no usable `Location`, or it is a `304` or a `300`. In each case the request as
    sent did not get what it asked for. None of them is evidence that the server is failing, which is
    what the 5xx count exists to show.
  - A status above 599, which the platform accepts, is a server error under the same rule.
- `D12` — **`Invalid` is detected exactly, without a handler** (amendment 1, 6). The mechanism is
  where the exception is thrown, not its class:
  - `HttpClient.SendAsync` raises its own refusals synchronously, before it returns a task, and
    everything a handler raises arrives on that task. Probed on .NET 10 (§ Full design › 1): a relative
    URL, a disposed client and a resent message throw synchronously; a handler throwing
    `InvalidOperationException` or `ObjectDisposedException` faults the task.
  - The executor therefore takes the task and awaits it in two steps. An exception from the call, or
    from building the client or the request, is `Invalid`. An exception from awaiting is judged as
    before.
  - This is exact on the runtime the plugins load into, so no misfiled case needs stating. The premise
    is pinned by tests whose rows cover both sides: the client's three refusals, and a handler throwing
    each of the two classes.
- `D13` — **The server's summary is fed by the transport and the server, never by the block.**
  - The transport reports each refusal, overload and abandoned connection to the server through the
    same internal handler interface it already answers through.
  - `ActiveConnections` is read from the transport at snapshot time.
  - The test kit's in-memory transport reports none of these, as its remarks already say of everything
    the socket decides on its own.

### Reviewer's questions

Ratified by amendment 1 (`C:\_gh\architecture\.claude\briefs\amend-VION-226-dale-sdk-1.md`,
2026-09-23). Every item is now (a): it is cited, and not reopened.

1. **(a) VION-226's shape → a receipt in the callback signature.** `HttpReceipt` and `HttpOutcome` are on
   every callback of the eight members, with no overloads (amendment 1, item 1). OUTCOME: accepted as
   proposed.
2. **(a) The client summary.** OUTCOME: changed in (c), accepted as proposed in (a), (b) and (d)
   (amendment 1, item 2).
   - (a) One summary per client instance.
   - (b) No `State`. `LastResponseAt` and the last failure stand instead. The cadence owner derives
     up/down, and 0118's verdict stays the rule for a binding that has a link. This is recorded as
     decision 0200, "An HTTP client's summary carries no link verdict", which lands with the
     coordinator's pull request.
   - (c) **Counts split into 4xx and 5xx**, changed from the doc's single count. The reason is the
     motivating consumer: a 404 there is a commissioning fact met in normal operation
     (`DiscoveringMeterRosterSource.cs:363`), so a single count lets it hide a failing server's 5xx.
     The first draft said the split "is what every HTTP tool surveyed shows". The survey table records
     status *per request*, not status-class counts, so the split rests on the consumer and not on the
     survey. The two sub-decisions are `D11` and `D3`.
   - (d) The name is `HttpClientSummary`, read as `ILogicBlockHttpClient.Summary`.
3. **(a) The server summary.** OUTCOME: accepted, with `ActiveConnections` carried by its own criterion
   (`AC-HTTP-021.7`) (amendment 1, item 3).
   - (a) Include it.
   - (b) The fields are those in § Full design › 6.
   - (c) Move `LastRequestAt` into the summary and delete the server member (`REMOVED
     AC-HTTP-016.10`, re-stated as `AC-HTTP-021.6`).
   - (d) `HttpServerRequest` gains the status it was answered with.
   - (e) The name is `HttpServerSummary`, read as `ILogicBlockHttpServer.Summary`.
4. **(a) The outcome taxonomy** stands as decided in `D2`, `D3` and `D9` (amendment 1, item 4), with
   `D3` now seven outcomes. OUTCOME: accepted.
5. **(a) Emission → prose in `http.md`, and one paragraph in `emission.md`.** The analyzer is not in this
   pull request and is not filed from this session (amendment 1, item 5). OUTCOME: accepted.
6. **(a) No handler.** Detecting `Invalid` adds no `DelegatingHandler` (amendment 1, item 6). OUTCOME:
   settled by `D12`, an exact mechanism, so `AC-HTTP-019.2` carries no limit clause.

---

## Full design

### 1. What happens today (verified this session)

| Fact | Evidence |
| --- | --- |
| Status is judged by `response.EnsureSuccessStatusCode()` on the headers. The failed response is then disposed and the platform's exception rethrown. | `Vion.Dale.Sdk.Http/HttpRequestExecutor.cs:369-382` |
| Every failure path funnels through one method before the error callback is handed over. | `HttpRequestExecutor.cs:387-409` (`HandleException`) |
| The package targets `netstandard2.1`. | `Vion.Dale.Sdk.Http/Vion.Dale.Sdk.Http.csproj:4` |
| The server already has `LastRequestAt` and a take-relative dropped count. | `Server/LogicBlockHttpServer.cs:98-129`, `:206-215`, `:306-314` |
| The server's refusals, overloads and abandoned connections are decided in the socket transport. | `Server/TcpHttpServerTransport.cs:254-257` (limit), `:306-377` (serve), `:379-453` (refusals) |
| The example block reads `StatusCode` by reflection, measures latency itself, and treats `TimeoutException` separately. | `examples/Vion.Examples.Http/Vion.Examples.Http/LogicBlocks/HttpDebugClient.cs:399-433` |
| The consumer reads `StatusCode` by reflection and falls back to finding `"404"` in the message. | `logic-block-libraries/…/EmuMCenter/Discovery/DiscoveringMeterRosterSource.cs:360-394` |
| The server example keeps request, unmatched and cumulative dropped counts by hand, and re-derives "unmatched" by re-matching its own table. | `HttpSimServer.cs:126-159`, `:338-359` |
| The first consumer's cadence for always-moving diagnostics structs is 30 s. Its stated reason is that such a summary "defeats the value-equality dedup floor … a permanent broker emitter", and 30 s "keeps a whole fleet of them below ~1 msg/s". | `logic-block-libraries/…/Ecocoach.EnergyManagement.Common/Emission/EmissionDefaults.cs:80-88`; applied at `…/Shared/IModbusTcpDiagnostics.cs:39-44` |

**Probe 1: the brief's `[assumed]` claim.** Run on Windows with the .NET 10 runtime, using a
`netstandard2.1` class library consumed by a `net10.0` console. The probe is in the session scratchpad and
was not committed.

- The 2.1 reference `netstandard.xml` declares three `HttpRequestException` constructors, `()`,
  `(string)` and `(string, Exception)`, and no `StatusCode`. Reading `e.StatusCode` from `netstandard2.1`
  fails with `CS1061`.
- A subclass that sets its own `Status` through `base(message, null)` reads `null` from the base
  `StatusCode`, whether read directly or by reflection.
- A subclass that hides `StatusCode` reads `NotFound` by `GetProperty("StatusCode")`, which returns the
  most-derived property, and still reads `null` through the base type.
- `EnsureSuccessStatusCode()` on a 404, run on .NET 10, throws with `StatusCode == NotFound`. This is why
  `D1` keeps that exception.

**Probe 2: `D12`'s premise.** Run on Windows with .NET 10, as a `net10.0` console calling
`HttpClient.SendAsync` and awaiting the task separately:

```text
relative url: SYNC InvalidOperationException
relative url with BaseAddress: TASK HttpRequestException
disposed client: SYNC ObjectDisposedException
first send: ok
resend: SYNC InvalidOperationException
handler throws IOE synchronously: TASK InvalidOperationException
handler throws ODE synchronously: TASK ObjectDisposedException
```

Neither probe depends on the operating system, because both are about managed type metadata and
`HttpClient`'s managed argument checks, not sockets. No Linux run is owed.

**Consumer sweep.** This was read-only and done by an `Explore` agent. The counts are re-derived with
commands in `T-000`.

- `ILogicBlockHttpClient` has four call sites across `logic-block-libraries`, `examples/`, `libraries/`
  and `templates/`:
  - three `GetJson`: `DiscoveringMeterRosterSource.cs:163`, `GeolocationService.cs:36`,
    `OpenMeteoService.cs:81`;
  - one `SendRequest`: `HttpDebugClient.cs:205`.
- `logic-block-libraries` also mocks the client with Moq, for example at
  `EmuMCenterSourceGateShould.cs:243`.
- No block keeps success or failure counters for HTTP.

### 2. What HTTP tools surface

"On par with Modbus" means analogous, so the fields come from what HTTP tooling itself shows. The survey
is from documentation the session already knew, and none of it was re-fetched:

| Tool | Per-request facts | Aggregates |
| --- | --- | --- |
| curl `-w` | `http_code`, `exitcode`/`errormsg` (transport), `time_total`, `time_starttransfer` | — |
| .NET `System.Net.Http` metrics (8+) | `http.response.status_code`, `error.type` (a status code, a transport-error name, or `timeout`) | `http.client.request.duration`, `http.client.active_requests`, `http.client.request.time_in_queue` |
| OpenTelemetry HTTP semantic conventions | status code; `error.type` separating a status from a transport error | duration histogram by status code |
| k6 | status, `error_code` | `http_reqs`, `http_req_failed`, `http_req_duration` avg/min/med/max/p90/p95 |
| Prometheus blackbox exporter | `probe_http_status_code`, `probe_success` | `probe_duration_seconds` by phase |
| Uptime monitors | last status, response time | up/down per probed URL, average response time |
| nginx `stub_status` / ASP.NET Core server metrics | status per request (access log) | requests, accepted, handled, active connections; request duration by status; rejected connections |

**What carries over:**

- A status and a transport failure are two axes. Every tool reports the status when a response arrived
  and a separate error kind when none did. That is VION-226, and it is `HttpOutcome` together with
  `HttpReceipt.StatusCode`.
- Timeouts are named on their own (`D2`).
- Duration is the headline aggregate: mean, max and count over a window, since no histogram fits a flat
  struct (`D7`).
- Active requests and active connections are live gauges (`InFlightCount`, `ActiveConnections`).
- An up/down verdict appears only where one URL is probed, which is the ground for decision 0200.

The tools record status per request, not status-class counts. The 4xx/5xx split in the summary rests on
the consumer (Reviewer's question 2).

**What does not carry over:**

- queued wait, since the pool's queue is invisible through `IHttpClientFactory`;
- a connection summary, since the pooled handler owns the connections (`AC-HTTP-002.1`);
- backoff and expiry, since the package retries nothing.

### 3. The receipt

```csharp
[PublicApi]
public readonly record struct HttpReceipt(DateTime ReceivedAt,          // UTC, the instant the outcome was observed
                                          long ReceivedTimestamp,       // the same instant, monotonic scale
                                          TimeSpan RoundTrip,           // D4
                                          HttpOutcome Outcome,
                                          HttpStatusCode? StatusCode);  // the status of the response, when one arrived
```

| Member | Success callback | Error callback |
| --- | --- | --- |
| `GetJson`, `PostJson<TReq,TRes>`, `PutJson<TReq,TRes>`, `DeleteJson<TRes>` | `Action<TResponse, HttpReceipt>` | `Action<Exception, HttpReceipt>?` |
| `PostJson<TReq>`, `PutJson<TReq>`, `Delete` | `Action<HttpReceipt>?` | `Action<Exception, HttpReceipt>?` |
| `SendRequest` | `Action<HttpResponseMessage, HttpReceipt>?` | `Action<Exception, HttpReceipt>?` |

The consumer's `IsNotFound` becomes `receipt.StatusCode == HttpStatusCode.NotFound`. The receipt is built
at the funnel every outcome already passes through. The status is captured where it is judged (`D1`), and
`Invalid` is decided by where the exception was thrown (`D12`).

### 4. The outcome (`D3`, `D11`)

| `HttpOutcome` | When | `StatusCode` | Severity |
| --- | --- | --- | --- |
| `Success` | a 2xx, its content read where the member reads it | the 2xx | Success |
| `ClientError` | a non-success response below 500: a 4xx, or an unfollowed 1xx/3xx | that status | Warning |
| `ServerError` | a response of 500 or above | that status | Error |
| `ContentError` | a 2xx whose body is absent, malformed or deserializes to null | the 2xx | Error |
| `Timeout` | either bound cancelled the exchange | none | Error |
| `TransportError` | anything else a handler or the body stream threw | the 2xx, if the body broke after the headers; otherwise none | Error |
| `Invalid` | the client or request could not be built, or the client refused it synchronously (`D12`) | none | Warning |

Neither bound covers the body read (`http.md` § The error model). So a timeout can only be raised while
the headers are awaited, and it never carries a status.

### 5. The client summary

This set is 17 fields: `LastResponseAt` (any status) · `LastFailureAt` · `LastFailureOutcome` ·
`LastFailureStatusCode` (`int?`) · `SuccessCount` · `ClientErrorCount` · `ServerErrorCount` ·
`ContentErrorCount` · `TimeoutCount` · `TransportErrorCount` · `InvalidCount` · `RecentRoundTripCount` ·
`RecentMeanRoundTrip` · `RecentMaxRoundTrip` · `MaxRoundTrip` · `MaxRoundTripAt` · `InFlightCount`.

The accumulator is owned by the executor, which is the choke point and is transient with its client, so
each client instance has its own. The client reads `Summary` from it. The test kit's tracking executor
forwards it.

### 6. The server summary

This set is 10 fields: `LastRequestAt` · `AnsweredCount` (from a published response) · `UnmatchedCount`
(404/405 because nothing was published) · `RefusedCount` (400/411/413/431) · `OverloadedCount` (503 at
the connection limit) · `AbandonedCount` (closed with neither a response written in full nor a refusal) ·
`DroppedCount` (lifetime) · `LastRefusalAt` · `LastRefusalStatus` (`int?`, both refusal kinds) ·
`ActiveConnections`.

- The two answered counts move when the response has been written in full, which is `AC-HTTP-016.8`'s
  moment.
- A route the block itself published with a 404 counts as answered, not unmatched.
- The summary is readable without `Sync` and survives disable and enable.

### 7. Emission: what publishing a summary costs

A summary changes on every completed request, so value-equality dedup never suppresses it and there is no
deadband for a struct. Its publish rate is the lower of two rates: how often the block assigns it, and
one per `MinInterval`. The throttle is leading-edge with a trailing release (`AC-EMIT-005.1`–`.4`), so a
longer interval loses nothing but intermediate snapshots.

| Block assigns the summary | Requests | `MinInterval` | Publishes per summary |
| --- | --- | --- | --- |
| in every callback | 4/s or more | default `250ms` | 4/s, which is 14,400 an hour |
| in every callback | 1/s | default `250ms` | 1/s, which is 3,600 an hour |
| in every callback, or on a `[Timer(30)]` tick | any | `30s` | at most 120 an hour |

What an author sets:

- a `MinInterval` in seconds, where `"30s"` is the first consumer's cadence for this shape;
- assign the summary from the block's own tick;
- never `Immediate`;
- use the receipt for a signal whose every edge matters;
- no `[ServiceMeasuringPoint]`.

### 8. Migration

Migration is compiler-guided, and a site that ignores the receipt behaves the same (0119):

- a lambda gains `, _`;
- a method group gains a parameter;
- a Moq `Setup` over `It.IsAny<Action<T>>()` becomes `It.IsAny<Action<T, HttpReceipt>>()`.

The server's `LastRequestAt` becomes `Summary.LastRequestAt`, a `DateTime?` in UTC where it was a
`DateTimeOffset?`. Per consumer:

- **`logic-block-libraries`**: its one `GetJson` (where `IsNotFound` becomes one comparison), and its
  Moq setups.
- **Examples** (post-release bump):
  - `HttpDebugClient`: take the status, round trip and outcome from the receipt, and publish
    `Summary` at `"30s"`.
  - `HttpSimServer`: publish `Summary` at `"30s"`, drop its three hand counts, read
    `Summary.LastRequestAt` (`:263`), and take each route's tally from `HttpServerRequest.StatusCode`.

### 9. Proof plan, and the mask

Every test project targets `net10.0`, where the base `HttpRequestException.StatusCode` compiles.

- Status is read only through `HttpReceipt.StatusCode` and `HttpOutcome`.
- **The transport row** is the stub handler throwing a plain `HttpRequestException`, the class a refused
  connection arrives as (`HttpDebugClient.cs:425`).
- **The status row** is a 404.
- **The reverse-mask row** is the handler throwing `new HttpRequestException(message, null,
  HttpStatusCode.NotFound)`. It must read `TransportError` with no status, which is `D1`.
- **`D12`'s rows**:
  - a relative URL, a disposed client and a resent `SendRequest` message read `Invalid`;
  - a handler throwing `InvalidOperationException` or `ObjectDisposedException` reads `TransportError`.
- **The window rows** drive the registered clock, as `AC-MODB-016.7`'s tests do.

---

## Drift checkpoints

- 2026-09-23: The brief's `[assumed]` claim was verified (§ Full design › 1, probe 1). It holds in full.
  The probe also found that a *hiding* subclass keeps name-based reflection working.
- 2026-09-23: The brief's line references were re-read and hold: `HttpRequestExecutor.cs:371`,
  `FakeHttpHarnessShould.cs:33-51`, `HttpDebugClient.cs:425` and `:430-433`, and
  `DiscoveringMeterRosterSource.cs:362-364` (the `<summary>` of `Reject`) and `:380-394`.
- 2026-09-23: `modbus.md:21` cites `emission.md` "for what publishing a diagnostics struct as a
  `[ServiceProperty]` costs and when it emits". `emission.md` stated nothing specific to structs. This
  is closed by the `emission.md` paragraph (Q5).
- 2026-09-23: The first draft of the doc said the 4xx/5xx split "is what every HTTP tool surveyed
  shows". Its own survey table records status per request. This is corrected in § Full design › 2 and
  Reviewer's question 2, per amendment 1.

---

## Spec delta (to distill)

- ADDED AC-HTTP-019.1 -> docs/specs/http.md : THE SYSTEM SHALL hand every success callback and every error callback of the eight request members a receipt of that request alongside its value, response or exception.
- ADDED AC-HTTP-019.2 -> docs/specs/http.md : THE SYSTEM SHALL report on the receipt a success for a 2xx response whose content was read, a client error for any other response below 500, a server error for a response of 500 or above, a content error for a 2xx response whose body is absent, malformed or deserializes to null, a timeout where either bound cancelled the exchange, an invalid request where the client or the request could not be built or the client refused the request before any handler saw it, and a transport error for any other failure.
- ADDED AC-HTTP-019.3 -> docs/specs/http.md : THE SYSTEM SHALL carry on the receipt the status of the response it judged whenever one arrived, and no status when none did, even where the exception a handler threw carries one.
- ADDED AC-HTTP-019.4 -> docs/specs/http.md : THE SYSTEM SHALL stamp the receipt, on the registered clock, with the instant the outcome was observed on the wall clock and on the monotonic timestamp scale, and with the time from handing the request to the client until that instant.
- ADDED AC-HTTP-020.1 -> docs/specs/http.md : THE SYSTEM SHALL accumulate every request a client instance issues into that instance's own summary, readable at any time, before the request's callback is handed to the block and whether or not the request has a callback.
- ADDED AC-HTTP-020.2 -> docs/specs/http.md : THE SYSTEM SHALL count every request under its receipt's outcome, keeping one lifetime counter per outcome.
- ADDED AC-HTTP-020.3 -> docs/specs/http.md : THE SYSTEM SHALL record every outcome but a success as the last failure, with its instant and the status of its response where one arrived, and SHALL record the instant of the last response of any status.
- ADDED AC-HTTP-020.4 -> docs/specs/http.md : THE SYSTEM SHALL feed every round-trip figure only from successes, client errors, server errors and content errors.
- ADDED AC-HTTP-020.5 -> docs/specs/http.md : THE SYSTEM SHALL report the mean, the maximum and the number of round trips over at least the last 15 minutes and less than the last 16 on the registered clock, or over the client's whole life where that is shorter, reporting the mean and maximum as empty and the count as zero while none falls within it.
- ADDED AC-HTTP-020.6 -> docs/specs/http.md : THE SYSTEM SHALL report the lifetime maximum round trip with the instant its outcome was observed, and SHALL move that instant only when a strictly larger value is recorded.
- ADDED AC-HTTP-020.7 -> docs/specs/http.md : THE SYSTEM SHALL report how many issued requests have not yet had their outcome recorded.
- ADDED AC-HTTP-021.1 -> docs/specs/http.md : THE SYSTEM SHALL accumulate what the hosted server answers and refuses into a summary readable at any time without a Sync callback, kept for the lifetime of the server instance across disabling and enabling.
- ADDED AC-HTTP-021.2 -> docs/specs/http.md : THE SYSTEM SHALL count, once each response has been written in full, the requests answered from a published response apart from those answered 404 or 405 because nothing was published for them.
- ADDED AC-HTTP-021.3 -> docs/specs/http.md : THE SYSTEM SHALL count separately the requests it refused itself and the connections it refused at the connection limit, and SHALL record the instant and the status of the last refusal of either kind.
- ADDED AC-HTTP-021.4 -> docs/specs/http.md : THE SYSTEM SHALL count every connection it closes with neither a response written in full nor a refusal.
- ADDED AC-HTTP-021.5 -> docs/specs/http.md : THE SYSTEM SHALL count every recorded request dropped from the log over the server's lifetime, independently of the count it reports since the block last took them.
- ADDED AC-HTTP-021.6 -> docs/specs/http.md : THE SYSTEM SHALL report the latest arrival instant among the requests it has recorded, and none before it has recorded one.
- ADDED AC-HTTP-021.7 -> docs/specs/http.md : THE SYSTEM SHALL report how many connections it is serving when the summary is read, not counting a connection refused at the connection limit.
- ADDED AC-HTTP-021.8 -> docs/specs/http.md : THE SYSTEM SHALL record with every request it hands to the block the status it answered that request with.
- MODIFIED AC-HTTP-005.2 -> docs/specs/http.md : WHEN the block has not yet received its first message THE SYSTEM SHALL run neither callback and SHALL leave the outcome of the request visible only in the log and in the client's summary.
- REMOVED AC-HTTP-016.10 -> docs/specs/http.md : moved into the server summary as AC-HTTP-021.6 with the server member it described; the id is not reused.

---

## Tasks

- `T-000`: re-derive the consumer and call-site counts and the test projects' target frameworks with
  pasted commands, and flip the status to `in-flight`.
- `T-001` (`AC-HTTP-019.1`–`.4`): add `HttpReceipt` and `HttpOutcome`, move the members and
  `IHttpRequestExecutor` to receipt-bearing callbacks, build the receipt at the funnel, and migrate the
  in-repo tests and the test kit.
- `T-002` (`AC-HTTP-005.2`, `AC-HTTP-020.1`–`.7`): add the client accumulator, `HttpClientSummary` and
  `ILogicBlockHttpClient.Summary`.
- `T-003` (`AC-HTTP-021.1`–`.8`): add the server accumulator, `HttpServerSummary` and
  `ILogicBlockHttpServer.Summary`, feed it from the transport, delete `LastRequestAt`, and add
  `HttpServerRequest.StatusCode`.
- `T-004`: write the `http.md` and `emission.md` prose, the XML docs, and the test kit's remarks.
- `T-005`: distill, archive, run the pre-PR obligations, and the lane-2 review round.

---

## Relay notes for the PR body

> Filled as each consumer-visible change lands.

- _(none yet)_
