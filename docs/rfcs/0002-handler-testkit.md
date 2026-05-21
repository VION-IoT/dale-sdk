# RFC 0002: TestKit for `ServiceProviderHandlerBase`

Status: **Draft** — design-only, not implemented. Author: jonas.bertsch. Date: 2026-05-21.

## Motivation

`ServiceProviderHandlerBase` ([Vion.Dale.Sdk/Abstractions/ServiceProviderHandlerBase.cs](../../Vion.Dale.Sdk/Abstractions/ServiceProviderHandlerBase.cs)) is the contract for every SP-contract bridge actor we ship: `AnalogInputHandler` / `AnalogOutputHandler` (`Vion.Dale.Sdk.AnalogIo.*`), `DigitalInputHandler` / `DigitalOutputHandler` (`Vion.Dale.Sdk.DigitalIo.*`), `ModbusRtuHandler` (`Vion.Dale.Sdk.Modbus.Rtu`), and downstream custom handlers like the ones in `Ecocoach.Dale.Contracts.Ppc`. Today none of them can be tested end-to-end. Four concrete gaps:

1. **`ServiceProviderMqttMessage` has an internal ctor** ([ServiceProviderMqttMessage.cs:21](../../Vion.Dale.Sdk/Abstractions/ServiceProviderMqttMessage.cs#L21)). Tests cannot synthesise one to drive `HandleMqttMessage(message)`.
2. **`PublishJson<T>`, `Publish`, `ForwardToLogicBlocks<T>` are `protected`** with no recordable seam — there is no equivalent of `LogicBlockTestContext.VerifyContractMessageSent`.
3. **`ContractLogicBlockActorReferences` is `protected ... { private set; }`**, populated only via a `LinkLogicBlockContractActors` message. `FindMappedServiceProviderContracts` reads from it; tests can't populate it without crafting and dispatching that message manually.
4. **No SDK-shipped handler tests** beyond `ModbusRtuHandlerShould`, which reaches for `Mock<IActorContext>` + manual setup callbacks to capture `SendTo` invocations — the workaround consumers are forced to replicate.

The net effect: JSON/FlatBuffer/raw deserialisation, contract→sp mapping, and outbound publish paths are uncovered, or the handler logic gets split into static helpers to make it testable (loses dispatch coverage).

## Proposed API

A new `Vion.Dale.Sdk.TestKit.ServiceProviderHandlerTestContext<THandler>` paired with a `ServiceProviderHandlerTestContextBuilder<THandler>`, modelled on `LogicBlockTestContext` / `LogicBlockTestContextBuilder`:

```csharp
var ctx = handler.CreateTestContext()
                 .WithLogicBlockMapping(spContractId, lbContractIdA)
                 .WithLogicBlockMapping(spContractId, lbContractIdB)
                 .Build();

// Inbound
ctx.DeliverMqttMessage(topic, jsonPayload, correlationId: someGuid);
ctx.DeliverMqttMessage(topic, flatBuffer, correlationId);
ctx.DeliverMqttMessage(topic, rawBytes,   correlationId);

// Outbound assertions
ctx.VerifyPublishedJson<MyDto>(topic: expectedTopic, dto => dto.Foo == 42);
ctx.VerifyPublishedFlatBuffer<MyFb>(predicate);
ctx.VerifyPublishedRaw(topic, bytesPredicate);
ctx.VerifyForwarded<MyChanged>(logicBlockContractId, changed => changed.Bar == "x");

// Optional direct contract-message dispatch (outbound path)
ctx.DeliverContractMessage(new ContractMessage<MyCommand>(lbId, command));
```

The builder dispatches `RegisterMqttHandlerRequest` (so `GetMqttRegistration()` runs), then dispatches a synthesised `LinkLogicBlockContractActors` populated from the `WithLogicBlockMapping` calls.

## Test-only ctor for `ServiceProviderMqttMessage`

Two viable strategies:

- **`InternalsVisibleTo("Vion.Dale.Sdk.TestKit")` + TestKit factory.** SDK keeps the `internal` ctor; TestKit hosts a `MqttMessageFactory` (or just calls the ctor in `DeliverMqttMessage`). Downstream consumers go through `ctx.DeliverMqttMessage(...)` — they don't need to construct the message themselves. Keeps the public surface minimal.
- **Public `[PublicApi]` test-only ctor.** Cleaner for any consumer building their own test scaffolding, but bakes a "for-testing-only" entrypoint into the prod-shipped contract — easy to misuse outside tests, awkward to evolve.

**Lean: option 1.** First-party SDK+TestKit are co-versioned, so the `InternalsVisibleTo` coupling is safe; the public API surface stays clean. We already use this pattern (`Vion.Dale.Sdk` exposes internals to `Vion.Dale.Sdk.TestKit` and `Vion.Dale.Sdk.Test`).

## Backwards compatibility

- `ServiceProviderHandlerBase`, `ServiceProviderMqttMessage`, and the `protected` helpers stay unchanged in shape — no breaking changes for handler authors.
- Adding `InternalsVisibleTo` is additive.
- The new `ServiceProviderHandlerTestContext` is purely additive in the TestKit assembly.
- The existing `ModbusRtuHandlerShould.cs` keeps working against `Mock<IActorContext>`; the TestKit is the recommended path for new tests but not a forced migration.

## Interaction with FlushPendingActions / virtual time (RFC 0001)

`ServiceProviderHandlerBase.InvokeSynchronizedAfter` ([ServiceProviderHandlerBase.cs:140](../../Vion.Dale.Sdk/Abstractions/ServiceProviderHandlerBase.cs#L140)) flows through `ActorContext.SendToSelfAfter` — exactly the actor scheduling primitive that the LogicBlock TestKit already virtualises via `FakeTimeProvider`. The handler TestKit's `IActorContext` impl should reuse the same recording + deadline machinery, which means:

- `ctx.AdvanceTime(TimeSpan)` and `ctx.FlushPendingActions()` work identically here.
- `ctx.TimeProvider` and `ctx.VirtualNow` are exposed for the same reasons.
- Handlers that depend on `TimeProvider` (post-0.5.3 migration, e.g. `ModbusRtuHandler`) get the shared clock via the same `WithTimeProvider(FakeTimeProvider)` builder hook.

This argues for a small refactor: extract an `ActorTestContextBase` shared by `LogicBlockTestContext` and `ServiceProviderHandlerTestContext` (IActorContext impl, `_sentMessages`, `_pendingActions`, virtual clock, `AdvanceTime`/`FlushPendingActions`/`TimeProvider`). Without the extraction we duplicate ~150 lines and risk drift.

## Open questions

1. **`AddMapping` vs `WithLogicBlockMapping`** — which name composes better with future "with-N-things" builder methods?
2. **Verify method shape for raw bytes** — `byte[]` predicate or `ReadOnlySpan<byte>`? Span is allocation-free but predicate signatures with Span captures are awkward.
3. **Should `DeliverMqttMessage` accept an already-built `ServiceProviderMqttMessage`?** Useful for replay scenarios but defeats the InternalsVisibleTo seal. Recommend deferring until a real consumer needs it.
4. **Multi-handler scenarios.** If a system has two handler actors that talk to each other, do we need cross-handler test contexts? Out of scope for v1.

## Out of scope

- Testing the MQTT broker side of the bridge (registration, ACL).
- Cross-actor end-to-end tests spanning multiple handlers.
- Test scaffolding for `LogicBlockBase` ↔ handler integration — that's the LogicBlock TestKit's responsibility.
