# Vion.Examples.Http

An interactive HTTP debug client, built as a LogicBlock. Set the method, URL, headers and body, send one
request, and read back the status, the latency, the response headers and a preview of the body — from the
dashboard, on the gateway the device is actually connected to.

It ships with a simulated HTTP server whose routes are editable from service properties, so you can run
the whole thing without a device, and turn a healthy route into a faulty one while the client is talking
to it.

The two blocks are the two halves of `Vion.Dale.Sdk.Http`: `ILogicBlockHttpClient.SendRequest` on the
client side, `ILogicBlockHttpServerFactory` on the server side.

## Getting Started

1. **Set the startup project:**
   - **Visual Studio:** Right-click `Vion.Examples.Http.DevHost` in Solution Explorer → **"Set as Startup Project"**
   - **Rider:** Select `Vion.Examples.Http.DevHost` from the run configuration dropdown (top-right toolbar)

2. **Run the DevHost:**
   - Press `F5` to run
   - The browser should open automatically at `http://localhost:5000`

The default topology starts two blocks: `SimServer` (binding `127.0.0.1:18080`, with three routes) and
`DebugClient` (already pointed at `http://127.0.0.1:18080/api/status`). Press **Send now** in
`DebugClient`'s Request group and the answer appears in its Response group.

## Security: what the simulator exposes, and where

**The simulated server speaks plain HTTP and authenticates nobody.** There is no TLS and no login:
anything that can reach its port can read every route and send any request. That is the SDK server's own
nature, not a shortcut this example takes.

What that means depends on the address it binds — `SimServer`'s *Listen address*:

| Listen address | Who can reach it |
|---|---|
| `127.0.0.1` (the default) | Only programs on the same machine. On a gateway, only the gateway itself. |
| One interface's address, e.g. `192.168.10.5` | Anything on that interface's network. |
| `0.0.0.0` | Anything on **every** network the machine is attached to. On a gateway that is typically the plant network *and* whatever uplink it has — in clear, with no authentication. |

Keep it on `127.0.0.1` unless a device on the network genuinely has to call it, and then bind the one
interface that device is on rather than `0.0.0.0`. Never serve anything a network peer must not see.

**The client's headers are configuration.** *Headers* is a service property: what you type is stored with
the block and visible to anyone who can see the block's configuration. Do not paste a production API key
or bearer token into it.

## Try it against the simulator

The tour takes about a minute. Route edits reach the simulator on its next tick, so give each one a
second before sending.

1. **A plain GET.** Press **Send now**. *Outcome* reads `Succeeded`, *Status code* `200`, *Content type*
   `application/json`, and *Body preview* shows `{"device":"dale-http-sim","state":"ok"}`. In `SimServer`,
   `Route1 → Hits` is `1` and *Recent requests* lists the GET.

2. **A POST with a body.** Set *Method* `POST`, *URL* `http://127.0.0.1:18080/api/setpoint?unit=kW`,
   *Headers* `Content-Type: application/json` and *Body* `{"value":42}`. Press **Send now**: the client
   shows `202` and `{"accepted":true}`, and `SimServer`'s *Last request line*, *Last request headers* and
   *Last request body* show exactly what was sent, query included.

3. **Break a route.** In `SimServer`'s `Route1`, set *Status code* to `503`. Send the GET again: *Outcome*
   reads `HTTP error` and *Status code* `503`. The response body is not shown — see below.

4. **Type something the server refuses.** Set `Route1 → Status code` to `99`. `Route1 → Status` explains
   why the route is not served, `SimServer` keeps running, and the client's GET now meets a `404`.
   `SimServer`'s *Unmatched requests* counts it.

5. **Point at nothing.** Set *URL* to `http://127.0.0.1:18081/api/status`, where nothing listens. *Outcome*
   reads `Failed`, *Status code* is empty, and *Last error* carries the platform's reason, including the
   cause from the inner exception — a refused connection here.

6. **Type something the client refuses.** Set *URL* to `/api/status`. *Outcome* reads `Invalid request` and
   *Last error* says why. Nothing was sent: an invalid request never reaches the network.

Both tours are also committed as replayable scenarios: **http-roundtrip** (steps 1-2) and **http-failures**
(steps 3-6, plus the simulator going away and coming back) in the DevHost's Player.

## How the client reads an outcome

*Outcome* is the headline, and each value points somewhere different:

| Outcome | Meaning | Where to look |
|---|---|---|
| `Succeeded` | A 2xx arrived and its body preview was read. | The Response group. |
| `HTTP error` | The server answered, with a status outside 2xx. | *Status code*. |
| `Timed out` | No response headers within *Timeout*. | *Last error* names the bound that elapsed. |
| `Failed` | No answer at all — refused, unknown host, reset, TLS fault — or the body stalled. | *Last error*. |
| `Invalid request` | What was typed could not be sent. Nothing left the block. | *Last error*. |
| `In flight` | Waiting. **Send now** is refused until the answer or the timeout arrives. | — |

**A 4xx or 5xx shows its status code and nothing else.** `Vion.Dale.Sdk.Http` judges the status before it
hands a response over, and reports anything outside 2xx to the error callback with the response already
disposed. The block therefore cannot show the headers or the body a server sends with an error — the
problem-details JSON, the HTML error page. For those, use `curl` on the gateway.

**Latency** runs from sending to the response headers, or to the failure, measured on the block's own clock
when the answer reaches the block. A busy block adds its own mailbox wait to it; on an idle debug block
that is negligible.

## Debugging a real device

Turn `SimServer` off (or remove it from the topology), then point *URL* at the device.

- **HTTPS works**, through the platform's own handler and certificate validation. A device with a
  self-signed certificate fails with a TLS error under `Failed`; this block has no switch to trust it.
- **Redirects are followed** by the platform before the block sees anything, so a `301` never appears —
  the final response does.
- **Cookies are shared.** Every HTTP request any block in the same library makes goes through one pooled
  handler with one cookie container, so a login cookie one block receives is sent by the others.
- **`Content-Type` and the other body headers need a body.** Typed without one, the request is refused as
  invalid rather than sent without the header.
- **The body preview holds 8 KiB**, decoded as UTF-8; *Body truncated* says when there was more. A binary
  body shows as replacement characters.
- **Timeout** bounds the wait for the headers and, separately, for the preview. The SDK's own client gives up
  after 30 seconds whatever *Timeout* says.

## The simulated routes

`SimServer` has three route slots by default (the `RouteSlotCount` instantiation parameter allows up to
eight). Each slot is its own service — `Route1`, `Route2`, … — with *Enabled*, *Method*, *Path*, *Status
code*, *Content type* and *Body*, plus *Hits*, *Last hit* and a *Status* line saying what it serves or why it
serves nothing.

| Slot | Method | Path | Status | Content type | Body |
|---|---|---|---|---|---|
| `Route1` | GET | `/api/status` | 200 | `application/json` | `{"device":"dale-http-sim","state":"ok"}` |
| `Route2` | POST | `/api/setpoint` | 202 | `application/json` | `{"accepted":true}` |
| `Route3` | GET | `/api/fault` | 503 | `text/plain` | `maintenance` |

Matching is the SDK server's: the method and the path before any query, compared exactly — `/api/status`
does not answer `/API/status` or `/api/status/`. A path with no route answers `404`; a path routed only
under other methods answers `405`. When two enabled slots name the same method and path, the first one
serves and the later one's *Status* says it is shadowed.

The server answers from its own threads and never calls the block, so the block republishes every route
and collects what was asked once per second. Edits take effect on that tick, and *Hits*, *Recent requests*
and the *Last request* properties catch up on it too.

## What is in the project

- `LogicBlocks/HttpDebugClient.cs` — the client. One request at a time through
  `ILogicBlockHttpClient.SendRequest`, which leaves the request, the method, the headers and the body
  entirely to the caller. The response arrives as soon as its headers do, so the body preview is read off
  the block's actor and handed back when it is complete.
- `LogicBlocks/HttpSimServer.cs` — the simulated server, created from `ILogicBlockHttpServerFactory` and
  disposed by the block in `Stopping`, since a factory-created server outlives the block's scope.
- `LogicBlocks/RouteSlot.cs` — one editable route; how many exist is decided at configuration time.
- `Vion.Examples.Http.Test` — unit tests on `Vion.Dale.Sdk.Http.TestKit`: `FakeHttpHarness` scripts the
  client's answers on a virtual clock, so latency and timeouts are exact; `FakeHttpServerHarness` hosts the
  real server on an in-memory transport.
- `Vion.Examples.Http.IntegrationTest` and `scenarios/` — the two scenarios above, run headlessly with the
  client and the server talking over a real loopback socket. They run on the **real** clock: the server
  holds a socket the host cannot step.

## Limitations

One-shot requests only: no polling, no repeated watches, no failure counters. A non-2xx response's headers
and body are not shown (see above). The simulator serves static routes — a response cannot depend on the
request — and sets no response headers beyond the content type.
