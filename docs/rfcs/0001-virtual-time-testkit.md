# RFC 0001: Virtual time in `LogicBlockTestContext`

Status: **Implemented** — landed on branch `feat/testkit-timeprovider`, shipping in the same release as the bug fix. Author: jonas.bertsch. Date: 2026-05-21.

## Implementation notes (post-merge)

The implementation diverges from the original draft in two ways worth recording:

- **`TimeProvider` instead of a custom `IDateTimeProvider` shim.** The whole SDK migrated off the homegrown `IDateTimeProvider` and onto .NET's `System.TimeProvider` (via `Microsoft.Bcl.TimeProvider` on netstandard2.1). The TestKit hosts a `Microsoft.Extensions.Time.Testing.FakeTimeProvider` rather than reinventing one. This means open questions 1, 2, 3 inherit the well-defined answers from `FakeTimeProvider` (deadline-ordered dispatch with cascading, monotonic clock, no reentrancy magic). Open question 5 (Stopwatch / monotonic) is covered by `TimeProvider.GetTimestamp()` / `GetElapsedTime(long)`.
- **`WithTimeProvider(FakeTimeProvider)` builder hook.** Blocks that take `TimeProvider` in their ctor must be constructed with the same `FakeTimeProvider` the test context uses for scheduling, otherwise the block's `UtcNow` reads diverge from the deadlines `SendToSelfAfter` records. Tests own the `FakeTimeProvider`, pass it to the block, and bind it to the context via `.WithTimeProvider(clock)`. When the block doesn't depend on `TimeProvider`, callers can ignore this and use the test context's default-constructed clock.

The original API surface (`VirtualNow`, `AdvanceTime`) shipped as proposed. `FlushPendingActions` kept its clock-agnostic single-pass drain semantics from the prior bug fix.

## Motivation

The TestKit has two related gaps in how it models time:

1. **`SendToSelfAfter` ignores its `delay`.** Whether a tick is scheduled in 5 ms or 5 minutes, it lands on the same `_pendingActions` queue and fires on the next `FlushPendingActions()`. Tests cannot distinguish "due now" from "due later", so multi-tick scenarios collapse into a single flush. (The recently-fixed unbounded-drain bug was a symptom of the same gap.)
2. **No virtual clock for time *reads*.** Every consumer test that needs deterministic `DateTime` re-implements the same pattern: `Mock<IDateTimeProvider>` + a `_currentTime` field + a hand-rolled `AdvanceTime(TimeSpan)` helper. We see this duplicated across ≥5 logic-block test classes in `examples/Vion.Examples.Energy.Test/` and again in `Vion.Dale.Sdk.Modbus.Rtu.Test/`.

The two pains are linked: an action scheduled "5 s from now" is meaningless if the substrate has no notion of "now".

## Proposed API

Add three surfaces to `LogicBlockTestContext<T>`:

```csharp
DateTime VirtualNow { get; }
void AdvanceTime(TimeSpan delta);
IDateTimeProvider DateTimeProvider { get; }   // backed by VirtualNow
```

Plus a builder hook:

```csharp
LogicBlockTestContextBuilder<T> WithVirtualNow(DateTime anchor);   // default: 2026-01-01Z
```

Semantics:

- `SendToSelfAfter(message, delay)` records `(deadline = VirtualNow + delay, action)` instead of queueing immediately.
- `AdvanceTime(delta)` sets `VirtualNow += delta`, then dispatches every due action in deadline order. Actions re-scheduled to a future deadline stay pending. Exceptions propagate; remaining due actions stay queued.
- `DateTimeProvider` is auto-registered in the DI container the builder constructs, so blocks that take `IDateTimeProvider` in their ctor get the virtual clock for free — no explicit Moq setup.

## Backwards compatibility

- **Existing `Mock<IDateTimeProvider>` tests**: unchanged. The new provider is opt-in via builder/DI; explicit injection still wins.
- **`FlushPendingActions()`**: kept, redefined as the "ignore the clock, drain whatever is queued" knob. After today's snapshot fix it is already single-pass. Tests that don't care about deadlines continue to call it; tests that do call `AdvanceTime` instead. A docstring note steers new code to `AdvanceTime`.
- **Timer messages and periodic state save**: also flow through `SendToSelfAfter`. Bringing them under virtual time is a behaviour change — propose **opt-in** for the first release (`AdvanceTime` only fires `InvokeActionMessage` actions; `FireTimer(...)` keeps its current explicit semantics).

## Open questions

1. Should `AdvanceTime(60s)` fire an action scheduled at t=0 *and* the action that first action queues at +30s? (Lean: yes — dispatch in a deadline-ordered loop until no action's deadline ≤ new `VirtualNow`.)
2. Reentrancy: what if a fired action calls `AdvanceTime` from inside its callback? (Lean: throw `InvalidOperationException` — surprise > convenience.)
3. Should `FlushPendingActions` implicitly advance `VirtualNow` to the max deadline of the drained set, so subsequent `AdvanceTime` calls compose cleanly? Or stay clock-agnostic? (Lean: stay agnostic, keep two clearly separated mental models.)
4. Anchor default: `2026-01-01Z` matches the existing example tests' habit, but pinning a year encodes our era — `DateTime.UnixEpoch` is more neutral.
5. Should we also expose `IStopwatch` / monotonic-time abstraction, or defer until a real consumer needs it?

## Out of scope

- Real-time / wall-clock testing (`Task.Delay` etc.).
- `CancellationToken`-based cooperative cancellation in tests.
- Changing production `IActorContext.SendToSelfAfter` semantics.
