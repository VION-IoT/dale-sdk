# Vion.Examples.MBus

Two logic blocks that read wired M-Bus meters through the `IMBus` service-provider contract:

- **`Em5xxElectricityMeter`** reads a Carlo Gavazzi EM5XX's five telegrams every minute and publishes
  energy, power, current, voltage, frequency and run hours. Each value is named once, by meaning, with an
  `MBusSelector` — `MBusSelector.Of(MBusQuantity.Current, subunit: 1)` is the L1 current — and arrives in
  the unit the property publishes. A value the readout does not carry is published empty, never as zero.
- **`MBusDebugClient`** is a commissioning tool. Point it at a primary address and press *Read now*: it
  shows the meter's header, one row per record with a selector that finds it (ready to paste into a
  block), and every telegram as hex (ready to replay in a test).

## Running it

There is no DevHost: an M-Bus binding needs a service provider that owns the M-Bus master, so the
example runs on a gateway, and its tests replay a captured read through the SDK's frame checks and decoder
with `Vion.Dale.Sdk.MBus.TestKit`.

Until a release ships `Vion.Dale.Sdk.MBus`, the example compiles only against the working tree:

```bash
dotnet test Vion.Examples.MBus.Test -p:DaleLocalSource=true
```

## Three things worth knowing

**One read is in flight per binding.** A `Read` while the previous one is pending does nothing and is
counted in `Link.BusyCount`. One read returns the whole meter, so read once per interval.

**The operation timeout includes the provider's line queue.** A line of 40 meters takes minutes for one
pass, so the default is 300 s. The debug client polls no faster than that, and not at all by default: every
debug read takes line time from the production meters on the same line.

**Tell the read how many telegrams the meter sends.** A smaller count, or a provider that reads only the
first telegram, leaves the readout incomplete and the later telegrams' values empty.
