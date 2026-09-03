# Writing a simulator block

A DevHost bench needs something on the far side of a service-provider contract. Until RFC 0020 there
was nothing: a scenario could *drive* a contract, but no block could answer one, so a closed loop —
an output whose confirmation comes back, an input a device model decides — could not exist off
production.

Now it can, and the whole mechanism is two ordinary things:

1. a **provider face** — the inverse contract surface, authored by whoever owns the contract;
2. a **simulator block** — an ordinary `LogicBlockBase` that binds it and decides what happens.

The host itself never decides anything; it re-delivers a value along a wire a topology declared. The
declaration side of that — the `contractPairings` array, what is refused when — is
[`devhost-conventions.md` §9](devhost-conventions.md#9-a-contract-pairing-is-a-declared-wire-and-the-host-never-transforms).
This page is the authoring side.

## 1. Do you need a provider face at all?

The SDK already ships four, beside their consumer faces in `Vion.Dale.Sdk.DigitalIo` /
`Vion.Dale.Sdk.AnalogIo`:

| Consumer face | Provider face | What the simulator gets |
|---|---|---|
| `IDigitalOutput` | `IDigitalOutputProvider` | `event SetReceived(bool)`, `Confirm(bool)` |
| `IDigitalInput` | `IDigitalInputProvider` | `Drive(bool)` |
| `IAnalogOutput` | `IAnalogOutputProvider` | `event SetReceived(double)`, `Confirm(double)` |
| `IAnalogInput` | `IAnalogInputProvider` | `Drive(double)` |

If your bench only needs digital / analog I/O, skip to §3 — you write a block, not a contract.

You author a provider face when you own a contract of your own (a PPC contract, a site protocol) and
want a behaving peer for it. Section 2 is that recipe.

## 2. Authoring a provider face for your own contract

A provider face is the same trio every contract already is — interface, contract class, handler —
and is bound, injected, discovered and TestKit-auto-mapped by the unchanged existing mechanisms.
Three rules make it work:

**Reuse the wire structs. Do not copy them.** A pairing materialises a direction only when one side's
declared `Outbound` is *the same CLR type* as the other side's declared `Inbound`. Reusing the
consumer face's structs makes the canonical pair exact by construction; a lookalike copy is an
authoring error the host refuses at load, naming both types.

**Declare `DevelopmentOnly = true`.** A provider face exists to stand in for a real provider; running
one against production MQTT would double-publish onto live topics. The flag reaches introspection as
an annotation, and the production runtime refuses to start a configuration that binds one.

**Say so in the XML docs.** The constraint has to be readable where the type is.

```csharp
[PublicApi]
[ServiceProviderContractType("PowerPlantControlPvProvider", DevelopmentOnly = true)]
public interface IPowerPlantControlPvProvider
{
    /// <summary>The paired consumer contract's measurement command arrives here.</summary>
    event EventHandler<PpcMeasurementSet>? MeasurementReceived;   // the consumer's OUTBOUND struct

    /// <summary>Answers the consumer's inbound — the demand a real controller would send.</summary>
    void SetDemand(PpcDemandPvReceived demand);                   // the consumer's INBOUND struct
}
```

The contract class dispatches `HandleContractMessage(ContractMessage<PpcMeasurementSet>)` to the
event and sends `ContractMessage<PpcDemandPvReceived>` from `SetDemand`; the handler carries
`[ScenarioWire(Inbound = typeof(PpcMeasurementSet), Outbound = typeof(PpcDemandPvReceived))]` — the
consumer handler's declaration with the two halves swapped. `DigitalOutputProvider` in
`Vion.Dale.Sdk.DigitalIo` is the shipped example to copy line for line.

The contract-type string (`"PowerPlantControlPvProvider"`) is a stable introspection identifier —
[`specs/introspection.md`](specs/introspection.md). Choose it once.

## 3. The ideal-I/O recipe — about twenty lines

The commonest bench need is not a device model at all: it is *ideal hardware*, so the block under
test sees the confirmation a real I/O module would send. That is a three-line `Ready()` body.

`Vion.Dale.DevHost.SmokeHost/LogicBlocks/IdealIoBlock.cs` is the reference block. Trimmed to the
load-bearing parts (it also surfaces what it last received, so a scenario can assert the command arrived):

```csharp
[LogicBlock(Name = "Ideal I/O", Icon = "device-line")]
public class IdealIoBlock : LogicBlockBase
{
    private bool? _lastDriven;

    [ServiceProviderContractBinding(DefaultName = "Output channel")]
    public IDigitalOutputProvider OutputChannel { get; private set; }

    [ServiceProviderContractBinding(DefaultName = "Input channel")]
    public IDigitalInputProvider InputChannel { get; private set; }

    /// <summary>The knob: what the input channel reports — the bench's hand on the wire.</summary>
    [ServiceProperty(Title = "Input closed")]
    [Presentation(Group = PropertyGroup.Configuration, Importance = Importance.Primary)]
    public bool InputClosed { get; set; }

    public IdealIoBlock(ILogger logger) : base(logger) { }

    [Timer(1)]
    public void OnTick()
    {
        if (_lastDriven == InputClosed) return;   // edge-only — see below
        _lastDriven = InputClosed;
        InputChannel.Drive(InputClosed);
    }

    protected override void Ready()
    {
        // The ideal I/O module, in full: whatever was commanded is what was applied.
        OutputChannel.SetReceived += (_, value) => OutputChannel.Confirm(value);
    }
}
```

Pair the two faces to a consumer block's output and input and the loop closes with no host magic:
the command reaches this block, its confirmation lights up the consumer's `OutputChanged`, and
`InputClosed` is the bench's hand on the input wire — pokeable in Explore, settable from a scenario
`set` step.

**Write inputs edge-only.** Drive from a timer (or from an event) and only when the value *changed*.
A simulator that re-drives an unchanged value every tick adds messages the quiescence barrier must
chase for nothing, and a paired loop is supposed to converge on block cadence.

**Every sad path is the same three lines, varied.** A delayed confirm, a wrong confirm, no confirm at
all — each is ordinary block code, unit-testable with the TestKit like any other block, and visible
in the topology as a block rather than hidden in the host.

## 4. When to write a behaving device model instead

The ideal module is deliberately behaviourless: in production the confirmation comes from the I/O
module, not from the device. Model the *device* separately when the bench needs the device to have an
opinion — `Vion.Dale.DevHost.SmokeHost/LogicBlocks/DeviceSimBlock.cs` is the fixture's example, and
the distinction is worth keeping in your own benches too.

Reach for a device model when you need:

- **non-take-up** — the device receives the command and ignores it (a knob on the sim), so the
  consumer's mismatch detection has something real to detect;
- **dynamics** — a contact that closes after a delay, a setpoint the plant ramps toward, a lock
  window;
- **a controller** — a peer that must *decide* an answer, not echo one. `PpcMeasurementSet` out is
  not `PpcDemandPvReceived` in; something has to choose the demand, and a mirror cannot.

Give the model knobs as service properties (`TakesUpCommands`, `ConfirmDelay`, a fault switch): they
are what makes the bench drivable interactively in Explore *and* deterministically from a scenario,
with no separate vocabulary for the two.

Two things a simulator block must not become:

- **A second copy of the logic under test.** If the sim mirrors the consumer's decision, the bench
  passes whatever the consumer does and asserts nothing. Model the *device*, from the device's side.
- **A socket.** A simulator that opens a TCP server is invisible to the quiescence barrier and
  wall-clock forever. Contract-hosted sims step; that is the whole point.

## 5. Simulating against a real gateway

Everything above is one of **two** ways to stand in for a provider, and they are different
mechanisms — not two settings of the same one. Pick by what you need to exercise.

| | **Logic-level — pairing, in the DevHost** | **Transport-level — a real service provider** |
|---|---|---|
| What the peer is | a simulator block binding a provider face | a program on MQTT, speaking the real topics |
| Where it runs | inside the DevHost, one actor hop | beside a real gateway, over the broker |
| Determinism | stepped and quiescence-fenced; per-PR CI | wall-clock; a manual or nightly bench |
| Fidelity | **the MQTT hop and the binary codec are bypassed** — the host forwards the value at the JSON layer the scenario codec already speaks | the encodings, topics, retention and correlation are the production ones |

So the two are complements, not alternatives: the pairing bench proves your **logic** converges,
deterministically and cheaply; a real service provider proves the **wire** — that your payloads
encode, your topics route, your provider identity resolves. A bug that lives only in the codec or
the topic layout is invisible to the first and caught by the second.

The second tier is not a deployed simulator block. A provider face is development surface by
declaration (`DevelopmentOnly = true`), and the boundary is hard on purpose: `dale pack` leaves a
block bound to one out of the introspection JSON that travels to the cloud — naming it in the pack
log — and the production runtime refuses to start such a configuration. Nothing relaxes that per
environment. If you need a stand-in on a real gateway, write the service provider, not a logic
block; it needs no development-only contract at all.

## 6. Checklist

- [ ] The provider face reuses the consumer face's wire structs — no copies.
- [ ] `DevelopmentOnly = true` on every provider face, and the XML docs say why.
- [ ] The simulator binds provider faces only; it never binds the consumer face it stands in for.
- [ ] Inputs are driven edge-only.
- [ ] Behaviour knobs are service properties, so Explore and scenarios drive the same thing.
- [ ] The topology declares the pairings; `validate` (or the host start) accepts them.
- [ ] A scenario runs the loop **stepped**, so it can join the per-PR gate.
