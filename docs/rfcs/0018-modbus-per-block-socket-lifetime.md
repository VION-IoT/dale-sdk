# RFC 0018 — Per-block Modbus socket lifetime: scope-owned client/server + reuse-safe server bind (DF-46)

- **Status:** Draft — proposal, not yet accepted. 2026-07-15.
- **Author:** jonas.bertsch
- **Related:** RFC 0007 (Modbus-TCP server support — this extends its client/server *lifetime* story). Cross-repo: `dale` (the private runtime owns block instantiation/teardown, hence the per-block DI scope in Part A), `architecture` (a cross-repo spec + decision for Part A), `logic-block-libraries` (consumer of the current `Stopping()` workaround). Origin: **DF-46** in [`logic-block-libraries/docs/dale-preview-feedback.md`](https://github.com/…/dale-preview-feedback.md).

> This is a design contract, not an implementation. **Part B** (server bind) is SDK-local and shippable now; **Part A** (per-block scope) has its load-bearing seam in the private `dale` runtime and is written here to the seam, to be concretized by an `architecture` spec against current `main` of each repo. No code has been written for either part.

## 1. Summary

A per-block Modbus **client** (`ILogicBlockModbusTcpClient`) is registered `AddTransient` and handed out by a **singleton** factory that resolves it from the **root** `IServiceProvider`. A transient `IDisposable` resolved from a container is disposed only when *that container* is — i.e. at process exit. So on a same-version config redeploy the old block is discarded but its `TcpClient` is retained by the root container and never disposed: **one half-open socket leaks onto the server per redeploy**. Separately, the sim-side **server** (`ModbusTcpServerProxy.Start`) binds with **no `SO_REUSEADDR` / `ExclusiveAddressUse=false` and no bind-retry**, so if a new instance `Start`s before the old one's socket is fully released the bind can hit `EADDRINUSE`.

Both are lifetime/ownership defects the *consumer* can only paper over (it does today — see §6). The deterministic owner of a per-block socket's lifetime should be the SDK/runtime, not the block.

This RFC proposes two independent changes, either of which reduces the symptom; together they remove the class:

- **Part A (cross-repo, the clean fix):** resolve the per-block client (and, for sim blocks, the server) from a **per-block DI scope disposed on block teardown**, so a redeploy deterministically reclaims sockets and the block never self-disposes a root transient.
- **Part B (SDK-local, shippable now):** set `ExclusiveAddressUse=false` / `SO_REUSEADDR` on the server's listener via FluentModbus's public listener-injection hook, so an overlapping redeploy rebinds without relying on the consumer's per-tick retry.

## 2. Confirmation against current `main` (verified 2026-07-15)

- Client is `AddTransient`, factory is `AddSingleton` — [`ServiceCollectionExtensions.cs:28-29`](../../Vion.Dale.Sdk.Modbus.Tcp/ServiceCollectionExtensions.cs).
- The singleton factory resolves the transient from its injected provider, which for a singleton **is the root container** — [`LogicBlockModbusTcpClientFactory.cs:21-24`](../../Vion.Dale.Sdk.Modbus.Tcp/Client/LogicBlock/LogicBlockModbusTcpClientFactory.cs).
- The client is `IDisposable` — [`ILogicBlockModbusTcpClient.cs:64`](../../Vion.Dale.Sdk.Modbus.Tcp/Client/LogicBlock/ILogicBlockModbusTcpClient.cs). A transient `IDisposable` from the root scope is thus tracked for disposal by the root scope only (disposed at process exit) — the textbook "captive/leaked transient-disposable" trap.
- Server binds with no reuse option and no retry — [`ModbusTcpServerProxy.cs:105`](../../Vion.Dale.Sdk.Modbus.Tcp/Server/Implementation/ModbusTcpServerProxy.cs) (`_server.Start(new IPEndPoint(listenAddress, port))`).

## 3. Part B — reuse-safe server bind (SDK-local, ship now)

**Feasibility is settled** (FluentModbus **5.3.2**, the pinned version — [`Vion.Dale.Sdk.Modbus.Tcp.csproj:27`](../../Vion.Dale.Sdk.Modbus.Tcp/Vion.Dale.Sdk.Modbus.Tcp.csproj); no central pin). `ModbusTcpServer` exposes **no** socket-option property, but it has a public, first-class listener-injection overload:

```
public void Start(FluentModbus.ITcpClientProvider tcpClientProvider, bool leaveOpen)
```

`ITcpClientProvider` is public (`Task<TcpClient> AcceptTcpClientAsync()` + `IDisposable`). The built-in `DefaultTcpClientProvider` is `internal` and sets **no** socket options (which is exactly why the default bind hits `EADDRINUSE`), but its shape is trivial to replicate while owning the socket options. Because `ITcpClientProvider : IDisposable` and we pass `leaveOpen: false`, the server disposes the provider on `Stop()`/`Dispose()` — **the same lifecycle path as today**, a true drop-in. **No fork, no FluentModbus upgrade.**

```csharp
// Vion.Dale.Sdk.Modbus.Tcp — mirrors FluentModbus's internal DefaultTcpClientProvider, but owns the socket options.
internal sealed class ReuseAddressTcpClientProvider : ITcpClientProvider
{
    private readonly TcpListener _listener;

    public ReuseAddressTcpClientProvider(IPEndPoint endpoint)
    {
        _listener = new TcpListener(endpoint);
        _listener.ExclusiveAddressUse = false; // set BEFORE Start()/Bind()
        _listener.Start();
    }

    public Task<TcpClient> AcceptTcpClientAsync() => _listener.AcceptTcpClientAsync();

    public void Dispose() => _listener.Stop();
}
```

Call-site change at `ModbusTcpServerProxy.Start` ([`:105`](../../Vion.Dale.Sdk.Modbus.Tcp/Server/Implementation/ModbusTcpServerProxy.cs)):

```csharp
_server.Start(new ReuseAddressTcpClientProvider(new IPEndPoint(listenAddress, port)), leaveOpen: false);
```

Notes / open sub-questions:
- **`ExclusiveAddressUse=false` vs raw `SO_REUSEADDR`.** On Windows `SO_REUSEADDR` has *permissive* semantics (two sockets may genuinely bind the same endpoint); `ExclusiveAddressUse=false` is the conventional .NET knob for "don't hold the port exclusively / allow rebind over a lingering `TIME_WAIT`". Both are applied on our own socket via the public API. **Which one the observed `EADDRINUSE` actually needs should be confirmed against a real same-version redeploy** before we settle on the exact option.
- **Retry-loop fallback** (simpler, less deterministic): wrap only the `_server.Start(...)` call in a catch for `SocketException{ SocketError: AddressAlreadyInUse }` and retry a handful of times with a short backoff, keeping `IsListening = true` / `LogStarted(...)` inside the success path. This waits out the OS release window rather than *preventing* the conflict; worst-case start latency grows. Prefer the provider hook; keep this documented as the fallback.
- **TestKit twin.** `FakeModbusTcpServerProxy` binds nothing, so the TestKit is unaffected; the change is real-socket only. The existing real-socket integration tests (`ModbusTcpServerIntegrationShould`) should gain a **rebind-before-release** regression (start → accept a client → start a *second* proxy on the same port before the first releases → the second must bind).

## 4. Part A — scope-owned per-block client/server (cross-repo, the clean fix)

**Target design:** register the per-block Modbus client (and server) so their lifetime is a **per-block DI scope** created when a block is instantiated and disposed when the block is torn down. Disposing the scope disposes the client/server deterministically — reclaiming the socket on redeploy — and the block never calls `Disconnect`/`Dispose` itself.

**Why it isn't SDK-only.** This SDK *registers* the service and *provides* the factory, but **block instantiation and teardown are owned by the private `dale` runtime** (via `Vion.Dale.ProtoActor` actors); the DevHost is a second host with the same responsibility. Whoever owns the block's lifetime must (a) create an `IServiceScope` per block, (b) resolve the block's dependencies (including the Modbus client/server) from it, and (c) dispose it on teardown. The SDK cannot unilaterally close the leak — it can only offer the mechanism. Hence Part A is a **≥2-repo change** and, per this repo's cross-repo rule, wants an `architecture/specs/*` entry + a decision, generated against current `main` of `dale` + `dale-sdk`.

**Seams to fill in the spec (not decided here):**
- Whether the client/server registration becomes `Scoped` (and the hosts adopt per-block scopes), or the factory itself creates+owns a child scope tied to the returned instance's disposal. `Scoped` is cleaner but pushes the per-block-scope requirement onto every host; a scope-owning factory is more self-contained but still needs *someone* to dispose the returned instance.
- How a block's teardown signal reaches scope disposal in the actor model (Proto.Actor `Stopping`/`Stopped`), and the DevHost equivalent (`DevLogicSystemInitializer` recycle path).
- Interaction with the current fire-and-forget safe-baseline write: today `Dispose()` closes the request queue, so a just-issued teardown write is best-effort (§6). A scope-owned teardown must define the ordering ("drain/settle the safe-baseline write, *then* dispose the scope") so the fix doesn't inherit the same best-effort corner.

## 5. Recommendation / phasing

1. **Ship Part B now** in `dale-sdk` (SDK-local, low-risk, removes the `EADDRINUSE`-on-overlap class). One new internal provider + a one-line call-site change + a rebind integration test. Confirm the exact reuse option against a real redeploy first.
2. **Pursue Part A via an `architecture` spec** (removes the leak class and lets consumers retire the `Stopping()` workaround). Gate it behind the runtime's block-lifetime/scope model; do not land an SDK-only half that changes lifetimes without the host cooperating.

Either alone reduces the symptom; (A) removes the leak, (B) removes the bind conflict.

## 6. Consumer impact

The consumer currently papers over the **client leak** by having every Modbus block self-dispose the root-container transient in `Stopping()` (`ModbusClientConfigurator.Shutdown()` = `Disconnect(this)` + `Dispose()`; REQ-MTCP-002 in `logic-block-libraries`), and papers over the **bind conflict** with a per-tick `EnsureTransport` retry in the sim. Both are safe today only because the SDK guards `Disconnect` (no-op when never enabled) and `Dispose` (idempotent) — but the block is disposing an object the root container *also* disposes at exit, and the teardown write is best-effort. **Part A lets the block stop self-disposing; Part B lets the sim stop relying on per-tick retry.** No consumer change is *required* by this RFC; it removes the need for those two workarounds.

## 7. Non-goals / risks

- **Not** a rewrite of the client request-queue or the RFC 0007 server surface — only *who owns the socket's lifetime* and *how the listener binds*.
- **Risk (Part A):** changing a registration lifetime (Transient→Scoped) without every host adopting per-block scopes would resolve a scoped service from the root and either throw (validated scopes) or re-introduce the leak — hence the cross-repo gating.
- **Risk (Part B):** `ExclusiveAddressUse=false` broadens bind semantics slightly; scope it to the server listener only, and pin the served behavior with the rebind regression test so a future FluentModbus bump can't silently change it (same discipline as RFC 0007's unit-id aliasing guard).

## 8. Open questions

1. Exact reuse knob (`ExclusiveAddressUse=false` vs `SO_REUSEADDR`) — decide against a real same-version-redeploy repro.
2. Part A registration shape (`Scoped` + host-owned scope vs scope-owning factory) — for the `architecture` spec.
3. Does a newer FluentModbus expose an explicit `ExclusiveAddressUse` (making even the provider unnecessary)? Not required given the 5.3.2 hook; a quick follow-up if we bump for other reasons.
