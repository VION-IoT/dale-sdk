---
slug: http-response-headers
status: in-flight          # proposed | in-flight | parked | archived
blocked-on: none           # for parked docs: what's blocking + ref
areas: HTTP, TKIT          # area codes from docs/specs/_corpus.yml's `owns:`
author: jonasbertsch
created: 2026-11-10
updated: 2026-11-10
supersedes: none           # path of a superseded change doc, or none
---

# A block reads the headers of the answer it got, and sets the headers of the answer it gives

> Change doc — one in-flight change. The `Spec delta` below is distilled into the current-truth
> pages under `docs/specs/` in the implementation PR, then this doc is archived
> (`spec-change.ps1 archive <slug>`). Process: `docs/spec-process.md`. Never put change narrative
> inline in a spec page — it lives here.

## At a glance

### Summary

VION-464. A block sees nothing of a non-success answer but its status, so the Easee cloud block
(logic-block-libraries #198) cannot honour the `Retry-After` of a 429 or log the request id of a
refusal, and its simulator cannot send a `Retry-After`, because the hosted server takes no header from
a block. The receipt now carries the headers of the response it judged, 2xx or not, with
`Retry-After` parsed; a hosted-server response takes headers a block adds, guarded like the content
type, the framing headers staying the server's. Both test kits follow.

### Spec implications

`http.md` § The receipt gains three leaves: the headers (`AC-HTTP-019.5`), the parsed `Retry-After`
(`AC-HTTP-019.6`), and its absence for anything but the RFC's two forms (`AC-HTTP-019.7`).
§ The hosted server: responses and requests rewords `AC-HTTP-016.5` (status, content type, **headers**
and body) and gains `AC-HTTP-016.13` (adding a header) and `AC-HTTP-016.14` (the refusals). § Lifetime
and disposal's `AC-HTTP-010.2` is unchanged — the headers are copied before the dispose it states, and a
test pins that the content headers survive it. `testkit.md` rewords `AC-TKIT-014.3` (a scripted answer
carries headers), `AC-TKIT-014.4` (a header the response cannot carry is refused) and `AC-TKIT-015.2`
(the server view returns every header the response carries, a repeated name joined). The public-API
manifest is type-level and this shape adds no type, so it does not move.

### Decisions

Ratified by the operator on 2026-11-10 (VION-464, the `[/fix] analysis` comment) — cited, not reopened:
the headers ride `HttpReceipt`; `Retry-After` is parsed to RFC 9110's two forms only, the raw header
staying readable; no error body; hosted-server headers in scope, guarded, framing headers the server's;
breaking is acceptable. The shape below is this session's, each a lane-2 (b):

- `D1` — The receipt's headers are an `IReadOnlyDictionary<string, string>?` named `Headers`, by
  case-insensitive name, a header sent more than once carrying its values joined by `", "` — the shape
  `HttpServerRequest.Headers` already has (`AC-HTTP-016.8`), so a block reads a request it served and a
  response it got the same way. `null` exactly when `StatusCode` is.
- `D2` — Response and content headers in one collection. A block asking for `Content-Type` of a refusal
  should not need to know the platform splits them; the copy is taken before `AC-HTTP-010.2`'s dispose.
- `D3` — `Headers` is a sixth positional parameter of `HttpReceipt`, defaulting to `null`. Source
  compatible: every positional construction compiles unchanged (three in this repo's tests,
  `HttpClientSummaryAccumulatorShould.cs:39`, `:42`, `:181`, and the consumer's
  `CloudRequestGateShould.cs:69`); the break is binary, and a positional deconstruction into five
  variables, of which neither repo has one.
- `D4` — `RetryAfter` is a `TimeSpan?` computed from `Headers` and `ReceivedAt`, not stored: the
  consumer's gate takes a `TimeSpan?` (`CloudRequestGate.Completed(HttpReceipt, TimeSpan?)`), and a
  derived member cannot disagree with the header it reads, whoever built the receipt.
- `D5` — An HTTP-date resolves against the response's own `Date` when it carries a valid one, else
  against the receipt's `ReceivedAt` (the registered clock); a date already past reads as zero. Both
  dates are then the server's clock, so a gateway whose clock is off — the gateway-loki skill names
  clock drift as a field symptom — waits what the server meant.
- `D6` — delay-seconds is `1*DIGIT`; a value past `int.MaxValue` seconds reads as `int.MaxValue`
  seconds, as RFC 9111 §1.2.2 has a cache read an overflowing delta-seconds. RFC 9110 states no overflow
  rule for `Retry-After` itself.
- `D7` — A hosted-server response takes headers through `HttpServerResponse.WithHeader(name, value)`,
  returning a new response, the original unchanged. It reaches a response however it was built,
  `Json(ThrottledBody, TooManyRequests).WithHeader("Retry-After", "120")` included, and touches neither
  the constructor nor `Json`. `HttpServerResponse.Headers` becomes public, read-only, ordered pairs: what
  the transport writes, a repeated name on repeated lines.
- `D8` — The framing names a block cannot set are `Content-Type`, `Content-Length`, `Connection`
  (written by the transport, `TcpHttpServerTransport.cs:565-580`) and `Transfer-Encoding`, which the
  transport never writes but which would contradict the `Content-Length` it does. A name must be a token
  (the transport's own `IsToken`, `TcpHttpServerTransport.cs:545-548`); a value is guarded exactly as the
  content type is.
- `D9` — `FakeHttpHarness.Respond` takes a trailing optional
  `IEnumerable<KeyValuePair<string, string>>? headers`, so a dictionary initializer and repeated names
  both work; a header the response refuses goes onto the content, and one neither takes is refused
  before the request is taken. `FakeHttpServerResponse.Headers` keeps its type, now joining a repeated
  name rather than throwing, and is documented as every header but the content type.

### Reviewer's questions

1. (c) **The platform renders a header it understands before the package can read it.** On
   `netstandard2.1` the only way to read a response's headers is `HttpHeaders` enumeration, which parses
   every header the platform knows and returns a value it accepts in canonical form. Measured on .NET 10
   (a scratch program adding each value with `TryAddWithoutValidation`, as the socket handler and the
   test kit both do, then enumerating): `120`, `Fri, 31 Dec 1999 23:59:59 GMT`, `1.5`, `-5`,
   `99999999999`, `fri, 31 dec 1999 23:59:59 gmt` and `1999-12-31T23:59:59Z` read back as sent; the
   RFC's obsolete `Friday, 31-Dec-99 23:59:59 GMT` and `Fri Dec 31 23:59:59 1999` read back as
   IMF-fixdate, as they should; but `Fri, 31 Dec 1999 23:59:59` (no zone) and
   `Fri,  31 Dec 1999 23:59:59 GMT` (a doubled space) — neither an HTTP-date — **also** read back as
   `Fri, 31 Dec 1999 23:59:59 GMT`. So a strict parse over what the package can read parses those two
   shapes, and for them the "raw" header reads canonical. Constraint 2's examples (`1.5`, a negative,
   garbage) all stay absent with their raw value. Options: **(1)** accept and state it — the strict
   grammar applies to the platform's rendering, which differs from the wire only for a date the platform
   understood (recommended: the date it yields is the one the server meant, and nothing else moves);
   **(2)** read raw values off `HttpHeaders.ToString()`, which on .NET 10 renders unparsed values — an
   undocumented behaviour, and still canonical wherever a handler read the header first; **(3)** reach
   .NET 6's `NonValidated` view by reflection from the `netstandard2.1` build. OUTCOME: accepted, (1) — the
   operator, 2026-11-10 (amendment 1 to the brief). Stated in `http.md` beside `AC-HTTP-019.6` and `.7`;
   (2) and (3) not taken. The per-request cost of D1's copy was raised and judged negligible at the
   consumer's request rates; no measurement asked for.
2. (b) D5 — resolving an HTTP-date against the response's `Date` rather than the gateway's clock.
   OUTCOME: accepted as recorded — the coordinator, amendment 1.
3. (b) D3 — the optional sixth parameter rather than a required one: the consumer's test double keeps
   compiling, at the cost of a hand-built receipt carrying a status and no headers. OUTCOME: accepted as
   recorded — the coordinator, amendment 1.

---

## Full design

**Client.** `SendAsync` records `trace.Status` from the response before it judges it
(`HttpRequestExecutor.cs:405`); the header copy is taken on the same line, from `response.Headers` and
`response.Content?.Headers`, into a case-insensitive dictionary, values joined `", "`. That is before
`EnsureSuccessStatusCode` and its dispose (`:407-421`), so a refusal's content headers are copied while
the content lives — and the scratch program showed they also survive `HttpResponseMessage.Dispose`, so
the copy order is belt and braces rather than load-bearing. `Complete` builds the receipt from the trace
(`:482-490`) and passes the headers with the status, so every member and both callbacks receive them —
`AC-HTTP-019.1`'s rule, untouched. A response that never arrived leaves both `null`.

`RetryAfter` reads `Headers["Retry-After"]`: all digits → seconds (saturating, D6); otherwise
`DateTime.TryParseExact` with the IMF-fixdate pattern `ddd, dd MMM yyyy HH:mm:ss 'GMT'`, invariant
culture, no whitespace allowance — the platform has already rendered the two obsolete forms as
IMF-fixdate (question 1), so a parse of the obsolete forms here would be unreachable. Anything else,
including a header repeated (joined with a comma, so neither form), is `null`.

**Hosted server.** `WithHeader` validates and returns `new HttpServerResponse(status, contentType, body,
headers + (name, value))` through the existing private constructor. The transport already writes
`response.Headers` between `Content-Length` and `Connection: close`, so no transport change is needed;
the 405's `Allow` keeps going through the same list.

**Test kits.** `Respond` builds the `HttpResponseMessage`, adds each header with
`TryAddWithoutValidation` to the response, then to the content, and refuses one neither takes before it
takes the oldest exchange — so a refused answer leaves the request outstanding, as a refused `Fail` does.
`FakeHttpServerHarness.Send` groups the response's pairs by case-insensitive name and joins them.

**Alternatives discarded.** A `headers` parameter on the constructor and on `Json`: two signatures to
grow, and the consumer's existing `Json` call would have to be rewritten rather than extended. A
`Dictionary` on `HttpServerResponse`: loses order and repeated names, which the wire has. A dedicated
`[PublicApi]` header type: a new type for what a dictionary already says, and a manifest change.
`HttpResponseHeaders` on the receipt: mutable, holds no content headers, and on `netstandard2.1` a
block would meet the same parsing without the case-insensitive string view.

---

## Drift checkpoints

- 2026-11-10: the brief's `[assumed]` that RFC 9110 §10.2.3 defines exactly two forms holds — fetched:
  `Retry-After = HTTP-date / delay-seconds`, `delay-seconds = 1*DIGIT`; HTTP-date (§5.6.7) is
  IMF-fixdate or one of two obsolete forms a recipient MUST accept, and is case-sensitive. §10.2.3 states
  no overflow rule (D6).
- 2026-11-10: `RetryAfter`'s ordinal round trip, which refuses a date in the wrong case, is unreachable
  through the client: the platform renders every date it parses canonical, and leaves raw only one its
  own parse refused, which the exact parse refuses too. It is kept for a receipt a block builds itself,
  and `HttpReceiptShould` proves it there; a mutation dropping it reddens that test's wrong-case row
  and no executor test. Sibling sweep: N/A.
- 2026-11-10: the `TcpHttpServerTransportShould` loopback precedent paired the server with the
  platform's `HttpClient` only; the end-to-end test composes the package's own client with
  `AddDaleHttpSdk()` and calls the typed `GetJson`, through the production primary handler.

---

## Spec delta (to distill)

- ADDED AC-HTTP-019.5 -> docs/specs/http.md : THE SYSTEM SHALL carry on the receipt, whenever it carries a status, the headers of that response and of its content by case-insensitive name, the values of a header sent more than once joined by `", "`, and no headers when it carries no status.
- ADDED AC-HTTP-019.6 -> docs/specs/http.md : THE SYSTEM SHALL report on the receipt the delay a response's `Retry-After` asks for, reading delay-seconds as that many seconds up to 2147483647 and an HTTP-date as its distance from the response's own `Date`, or from the receipt's wall-clock instant where the response carries no valid `Date`, a date already past reading as zero.
- ADDED AC-HTTP-019.7 -> docs/specs/http.md : IF a response carries no `Retry-After`, or one that is neither delay-seconds nor an HTTP-date, THEN THE SYSTEM SHALL report no delay on the receipt, the header's value staying readable among its headers.
- MODIFIED AC-HTTP-016.5 -> docs/specs/http.md : WHEN a request arrives for a method and a path with a response set THE SYSTEM SHALL answer with that response's status, content type, headers and body.
- ADDED AC-HTTP-016.13 -> docs/specs/http.md : THE SYSTEM SHALL let a block add a header to a response, returning a new response carrying the original's status, content type, body and headers followed by the one added, the original unchanged, and SHALL write a response's headers in the order added, a name added twice on two lines.
- ADDED AC-HTTP-016.14 -> docs/specs/http.md : IF a header is added to a response with a name that is not a token or that is `Content-Type`, `Content-Length`, `Connection` or `Transfer-Encoding` in any case, or with a value carrying a control character other than a tab or a character outside ASCII, THEN THE SYSTEM SHALL throw an `ArgumentException` naming the argument.
- MODIFIED AC-TKIT-014.3 -> docs/specs/testkit.md : THE SYSTEM SHALL answer outstanding requests oldest first, delivering a scripted status, headers, content type and body as the response the SDK handles and a scripted exception unchanged, as the SDK delivers a transport failure.
- MODIFIED AC-TKIT-014.4 -> docs/specs/testkit.md : IF a test answers or fails a request while none is outstanding THEN THE SYSTEM SHALL throw an `InvalidOperationException`, IF it fails one with no exception THEN THE SYSTEM SHALL throw an `ArgumentNullException`, and IF it answers one with a `Content-Type` header, a header value carrying a line break, or a header neither the response nor its content can carry THEN THE SYSTEM SHALL throw an `ArgumentException`, leaving the request outstanding.
- MODIFIED AC-TKIT-015.2 -> docs/specs/testkit.md : THE SYSTEM SHALL offer a client-side view that sends a method, a path with its query, headers and a body to the server and returns the server's status, content type, body and every other header the response carries by case-insensitive name, a repeated one joined by `", "`, the server recording the request as it records one from a socket.

---

## Tasks

- `T-001` (`AC-HTTP-019.5`, `AC-HTTP-019.6`, `AC-HTTP-019.7`): the receipt's `Headers` and `RetryAfter`, the executor's copy, tests through the executor.
- `T-002` (`AC-HTTP-016.5`, `AC-HTTP-016.13`, `AC-HTTP-016.14`): `HttpServerResponse.WithHeader` and its guard, tests over the response and over loopback.
- `T-003` (`AC-TKIT-014.3`, `AC-TKIT-014.4`, `AC-TKIT-015.2`): both kits, tests through each — the consumer's typed-member 429 among them.
- `T-004` (`AC-HTTP-019.6`, `AC-HTTP-016.13`): the real hosted server answering the real client over loopback, a 429 with `Retry-After` arriving on the receipt.
- `T-005`: distill, the PublicApi snapshot, archive.
