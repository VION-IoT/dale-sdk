# Vion.Examples.ModbusRtu

A Vion IoT Library template for creating LogicBlocks.

## Getting Started

1. **Set the startup project:**
   - **Visual Studio:** Right-click `Vion.Examples.ModbusRtu.DevHost` in Solution Explorer → **"Set as Startup Project"**
   - **Rider:** Select `Vion.Examples.ModbusRtu.DevHost` from the run configuration dropdown (top-right toolbar)

2. **Run the DevHost:**
   - Press `F5` to run
   - The browser should open automatically at `http://localhost:5000`

3. **Develop your LogicBlocks:**
   - Add your LogicBlock implementations in the `Vion.Examples.ModbusRtu` project
   - Register them in `DependencyInjection.cs`
   - Configure them in `Vion.Examples.ModbusRtu.DevHost/Program.cs`

## What the two blocks show

- `Em122ElectricityMeter` — a real device (Weidmüller EM122-RTU-2P): batch reads of contiguous float
  registers, individual reads at scattered addresses, holding-register and raw writes, and error
  handling. It publishes the SDK's `ModbusLinkSummary` as a single **Verbindung** service property —
  link state, last contact, per-outcome counters and wire latencies, all accumulated by the client from
  the receipts, so the block keeps no counters of its own beyond its poll-cycle tallies.
- `ModbusThroughputTest` — how batch size affects throughput. Its average latency comes from each
  receipt's `RoundTrip`, i.e. the time on the wire with the wait in the shared RTU queue excluded;
  dividing the test duration by the completion count, the only thing possible before receipts, measured
  the queue instead and under-reported the bus.

## Testing

**`Vion.Examples.ModbusRtu.Test` is this example's smoke.** There is no live tier: an RTU binding
resolves through a service-provider contract that needs a HAL service provider behind it, and the
DevHost does not stand in for one on a request/response contract, so there is nothing to drive
end to end without hardware. (Modbus TCP has no such gap — its example ships a simulated server, and
`pwsh scripts/smoke-modbus.ps1` drives it over a real socket.)

```bash
dotnet test examples/Vion.Examples.ModbusRtu/Vion.Examples.ModbusRtu.Test
```

One gotcha those tests encode: since SDK 0.10.4 RTU callbacks travel the dispatcher, so a response
simulated by the RTU TestKit is queued rather than applied inline. Every `SimulateReadResponse` /
`SimulateWriteResponse` / `Simulate*Error` needs a `ctx.FlushPendingActions()` before the assertion —
without it the code compiles and the assertion simply does not see the value. That and the rest of the
0.10.4 migration are in
[`docs/migrations/0.10.4-modbus-client-surface.md`](../../docs/migrations/0.10.4-modbus-client-surface.md).