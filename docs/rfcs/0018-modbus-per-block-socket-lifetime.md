# RFC 0018 — Per-block Modbus socket lifetime: scope-owned client/server + reuse-safe server bind (DF-46)

- **Status:** Draft — proposal, not yet accepted. 2026-07-15.
- **Author:** jonas.bertsch
- **Related:** RFC 0007 (Modbus-TCP server support — this extends its client/server *lifetime* story); RFC 0005 (observability — same `ActorSystem.CreateRootActorFromDi` spawn site). The Part A fix is **SDK-internal** (the block-instantiation path in `Vion.Dale.ProtoActor` + `Vion.Dale.DevHost`); the private `dale` runtime is involved only if its production initializer instantiates blocks at its own root-provider site rather than reusing the SDK spawn API (§4). Consumer: `logic-block-libraries` (the current `Stopping()` workaround). Origin: **DF-46** in [`logic-block-libraries/docs/dale-preview-feedback.md`](https://github.com/…/dale-preview-feedback.md).

> This is a design contract, not an implementation. **Part B** (server bind) is SDK-local and shippable now. **Part A** (per-block scope) is an **SDK-internal DI-lifetime change at the block-instantiation path**; it needs cross-repo confirmation on exactly one point — whether the production `dale` runtime reuses the SDK's `CreateRootActorFromDi` spawn API or has its own root-provider instantiation site (§4). Nothing Modbus-specific or TCP-central is configured by any host. No code has been written for either part.

## 1. Summary

A per-block Modbus **client** (`ILogicBlockModbusTcpClient`) is registered `AddTransient` and handed out by a **singleton** factory that resolves it from the **root** `IServiceProvider`. A transient `IDisposable` resolved from a container is disposed only when *that container* is — i.e. at process exit. So on a same-version config redeploy the old block is discarded but its `TcpClient` is retained by the root container and never disposed: **one half-open socket leaks onto the server per redeploy**. Separately, the sim-side **server** (`ModbusTcpServerProxy.Start`) binds with **no `SO_REUSEADDR` / `ExclusiveAddressUse=false` and no bind-retry**, so if a new instance `Start`s before the old one's socket is fully released the bind can hit `EADDRINUSE`.

Both are lifetime/ownership defects the *consumer* can only paper over (it does today — see §6). The deterministic owner of a per-block socket's lifetime should be the SDK/runtime, not the block.

This RFC proposes two independent changes, either of which reduces the symptom; together they remove the class:

- **Part A (SDK DI-lifetime, the clean fix):** resolve each **logic block from a per-block DI scope disposed on block teardown**, instead of from the root container. Its transient `IDisposable` dependencies (the Modbus client — and equally `ILogicBlockHttpClient`) then ride that scope and are reclaimed deterministically on redeploy, with no block self-disposal. This is nothing Modbus-specific — it is a generic fix at the block-instantiation site.
- **Part B (SDK-local, shippable now):** set `ExclusiveAddressUse=false` / `SO_REUSEADDR` on the server's listener via FluentModbus's public listener-injection hook, so an overlapping redeploy rebinds without relying on the consumer's per-tick retry.

## 2. Confirmation against current `main` (verified 2026-07-15)

- Client is `AddTransient`, factory is `AddSingleton` — [`ServiceCollectionExtensions.cs:28-29`](../../Vion.Dale.Sdk.Modbus.Tcp/ServiceCollectionExtensions.cs).
- The singleton factory resolves the transient from its injected provider, which for a singleton **is the root container** — [`LogicBlockModbusTcpClientFactory.cs:21-24`](../../Vion.Dale.Sdk.Modbus.Tcp/Client/LogicBlock/LogicBlockModbusTcpClientFactory.cs).
- The client is `IDisposable` — [`ILogicBlockModbusTcpClient.cs:64`](../../Vion.Dale.Sdk.Modbus.Tcp/Client/LogicBlock/ILogicBlockModbusTcpClient.cs). A transient `IDisposable` from the root scope is thus tracked for disposal by the root scope only (disposed at process exit) — the textbook "captive/leaked transient-disposable" trap.
- Server binds with no reuse option and no retry — [`ModbusTcpServerProxy.cs:105`](../../Vion.Dale.Sdk.Modbus.Tcp/Server/Implementation/ModbusTcpServerProxy.cs) (`_server.Start(new IPEndPoint(listenAddress, port))`).
- **Blocks are resolved from the root provider** — the disposal-tracking site. DevHost: `DevLogicSystemInitializer.CreateLogicBlockActors` → `_serviceProvider.GetService(blockType)` ([`:202`](../../Vion.Dale.DevHost/DevLogicSystemInitializer.cs), `_serviceProvider` = root). SDK spawn API: `ActorSystem.CreateRootActorFromDi` → `ActivatorUtilities.CreateInstance(_serviceProvider, blockType)` ([`:214`](../../Vion.Dale.ProtoActor/ActorSystem.cs)). Resolving a block from the root makes root own every `IDisposable` created while building it — including the block's transient Modbus client.

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

## 4. Part A — scope-owned per-block client (SDK DI-lifetime, the clean fix)

**Root cause, precisely.** A logic block is instantiated **from the root container**, so every `IDisposable` created while building it — including its transient `ILogicBlockModbusTcpClient` constructor dependency — is tracked by the *root* provider's disposable list and disposed only at process exit. Two instantiation sites, both resolving from root:
- DevHost — `DevLogicSystemInitializer.CreateLogicBlockActors` → `_serviceProvider.GetService(blockType)` ([`:202`](../../Vion.Dale.DevHost/DevLogicSystemInitializer.cs), `_serviceProvider` = root; it then wraps the pre-built instance with `CreateRootActorFor(() => logicBlock, …)`).
- SDK spawn API — `ActorSystem.CreateRootActorFromDi` → `ActivatorUtilities.CreateInstance(_serviceProvider, blockType)` ([`:214`/`:232`](../../Vion.Dale.ProtoActor/ActorSystem.cs)), `_serviceProvider` being the root the host passed into the `ActorSystem`.

**Nothing here is Modbus-aware.** The Modbus client reaches the graph only because the logic-block library's `IConfigureServices` called `AddDaleModbusTcpSdk()` (`AddTransient<ILogicBlockModbusTcpClient>`, [`ServiceCollectionExtensions.cs:29`](../../Vion.Dale.Sdk.Modbus.Tcp/ServiceCollectionExtensions.cs)); the host merely builds **one root container** from every library's registrations. So the fix is a **generic DI-lifetime change at the instantiation site**, not a TCP or runtime-configuration concern.

**Target design.** Resolve each block from a **per-block `IServiceScope`** and dispose that scope when the block's actor stops. The block's transient/scoped `IDisposable` dependencies are then tracked by *that* scope and reclaimed on teardown — the Modbus client's socket closes deterministically on redeploy, and the block never calls `Disconnect`/`Dispose` itself. The `Transient` registration need not change: resolving the *block* from a scope is enough for its constructor-injected client to ride along and be disposed with the scope.

**Where the change lands — all SDK:**
- `Vion.Dale.ProtoActor` — `CreateRootActorFromDi` creates the scope, resolves the block from `scope.ServiceProvider`, and associates the scope with the actor; `Actor<TReceiver>.ReceiveAsync` (which currently ignores every `SystemMessage`, [`Actor.cs:34`](../../Vion.Dale.ProtoActor/Actor.cs)) disposes it on `Stopped`.
- `Vion.Dale.DevHost` — `DevLogicSystemInitializer.CreateLogicBlockActors` resolves the block from a per-block scope instead of `_serviceProvider.GetService`, and disposes it on the host recycle/teardown path.

**The one cross-repo question — *not* "the runtime owns it".** The production `dale` runtime has an initializer that mirrors DevHost's ("Step 3: Create LogicBlock actors (same as production!)", [`DevLogicSystemInitializer.cs:109`](../../Vion.Dale.DevHost/DevLogicSystemInitializer.cs)). If it spawns blocks through the SDK's `CreateRootActorFromDi`, Part A is **pure-SDK** and the runtime gets it by bumping the package. If it instead has its own `GetService`/`ActivatorUtilities`-from-root site (like DevHost line 202), that site needs the **same generic per-block-scope change** — a small, mechanical DI edit, still nothing Modbus/TCP-specific. **Confirm which against the runtime's initializer before sizing Part A.** No `architecture` spec is required for the SDK change itself.

**Caveats to fold in:**
- **Factory-created clients still leak.** `LogicBlockModbusTcpClientFactory` is a singleton holding the *root* provider ([`:21-24`](../../Vion.Dale.Sdk.Modbus.Tcp/Client/LogicBlock/LogicBlockModbusTcpClientFactory.cs)), so a block that calls `factory.Create()` for extra clients gets root-scoped instances the per-block scope won't reclaim. Either make the factory resolve from the ambient block scope, or keep those the block's responsibility (documented). The common constructor-injection case is fully fixed.
- **Teardown ordering.** A just-issued fire-and-forget safe-baseline write must drain/settle *before* the scope disposes the client (whose `Dispose` closes the request queue — §6), so the fix doesn't inherit the current best-effort corner.

## 5. Recommendation / phasing

1. **Ship Part B now** in `dale-sdk` (SDK-local, low-risk, removes the `EADDRINUSE`-on-overlap class). One new internal provider + a one-line call-site change + a rebind integration test. Confirm the exact reuse option against a real redeploy first.
2. **Do Part A in the SDK** (removes the leak class and lets consumers retire the `Stopping()` workaround): per-block scope in `CreateRootActorFromDi` + the DevHost initializer, disposed on actor `Stopped` / recycle. First confirm whether the production runtime reuses `CreateRootActorFromDi` (→ package bump only) or has its own root-provider instantiation site (→ apply the same scope change there). No `architecture` spec required unless that second site exists.

Either alone reduces the symptom; (A) removes the leak, (B) removes the bind conflict.

## 6. Consumer impact

The consumer currently papers over the **client leak** by having every Modbus block self-dispose the root-container transient in `Stopping()` (`ModbusClientConfigurator.Shutdown()` = `Disconnect(this)` + `Dispose()`; REQ-MTCP-002 in `logic-block-libraries`), and papers over the **bind conflict** with a per-tick `EnsureTransport` retry in the sim. Both are safe today only because the SDK guards `Disconnect` (no-op when never enabled) and `Dispose` (idempotent) — but the block is disposing an object the root container *also* disposes at exit, and the teardown write is best-effort. **Part A lets the block stop self-disposing; Part B lets the sim stop relying on per-tick retry.** No consumer change is *required* by this RFC; it removes the need for those two workarounds.

## 7. Non-goals / risks

- **Not** a rewrite of the client request-queue or the RFC 0007 server surface — only *who owns the socket's lifetime* and *how the listener binds*.
- **Risk (Part A):** resolve the *block* from the per-block scope rather than flipping the client's registration to `Scoped` — a `Scoped` client left resolved from the root (any host that didn't adopt scopes) either throws under a validated root scope or silently re-leaks. Keeping the client `Transient` and scoping the block sidesteps that.
- **Risk (Part B):** `ExclusiveAddressUse=false` broadens bind semantics slightly; scope it to the server listener only, and pin the served behavior with the rebind regression test so a future FluentModbus bump can't silently change it (same discipline as RFC 0007's unit-id aliasing guard).

## 8. Open questions

1. Exact reuse knob (`ExclusiveAddressUse=false` vs `SO_REUSEADDR`) — decide against a real same-version-redeploy repro.
2. Does the production `dale` runtime spawn blocks via the SDK's `CreateRootActorFromDi`, or via its own root-provider instantiation site? Determines whether Part A is a pure package bump or also a small runtime edit.
3. Factory-created clients (`ILogicBlockModbusTcpClientFactory.Create`) — scope-bind the factory to the ambient block scope, or keep those the block's responsibility?
4. Does a newer FluentModbus expose an explicit `ExclusiveAddressUse` (making even the provider unnecessary)? Not required given the 5.3.2 hook; a quick follow-up if we bump for other reasons.
