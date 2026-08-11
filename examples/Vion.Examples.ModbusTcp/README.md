# Vion.Examples.ModbusTcp

An interactive Modbus TCP debug client, built as a LogicBlock. Point it at a device, read a range of
registers or coils, and reinterpret the same bytes under different field types and byte/word orders
until the numbers make sense — then write values back the same way.

It ships with a simulated server, so you can run the whole thing without any hardware.

## Getting Started

1. **Set the startup project:**
   - **Visual Studio:** Right-click `Vion.Examples.ModbusTcp.DevHost` in Solution Explorer → **"Set as Startup Project"**
   - **Rider:** Select `Vion.Examples.ModbusTcp.DevHost` from the run configuration dropdown (top-right toolbar)

2. **Run the DevHost:**
   - Press `F5` to run
   - The browser should open automatically at `http://localhost:5000`

The default topology starts two blocks: `SimServer` (listening on `127.0.0.1:15020`) and `DebugClient`
(already pointed at it). No configuration needed to see traffic.

## Two things worth knowing before you start

**Addresses are protocol (base-0) addresses** — the number that goes on the wire. Device documentation
written in the PLC/Modicon convention is one-based and encodes the area in the leading digit, so
holding register `40001` is address `0` here, and input register `30007` is address `6`. If everything
you read is off by one, this is why.

**Byte order and word order are separate settings.** Byte order is the order of the two bytes inside a
single register; Modbus standardises it as most-significant-first, and almost every device follows
that. Word order only matters for values spanning two or four registers, and it is the setting devices
actually disagree about. Both are chosen independently, and the dropdowns only appear for field types
where they apply.

## Try it against the simulator

The tour below uses the bundled `SimServer` and takes about a minute.

1. **Read a float.** Set *Read function* to `FC4 — Input registers`, *Start address* `6`, *Quantity*
   `2`, *Field type* `Float32`. Press **Read now**. The Interpreted column of the first row shows
   `3.1415927`.

2. **See a word-order mismatch.** Change *Start address* to `8` and read again. Input registers 8-9
   hold the very same π, stored with its two registers exchanged — so with the standard word order you
   get about `2.16e-29` instead. Now switch *Word order (32 bit)* to `LswToMsw` and read again: π is
   back. This is what a mismatch looks like in the field, and recognising it is most of the work.

3. **Write something and confirm it landed.** Set *Write function* to `FC16 — Write multiple
   registers`, *Write address* `100`, *Field type* `Float32`, *Value* `42.5`, then press **Write now**.
   Read holding registers at address `100`, quantity `2`, field type `Float32` — `42.5` comes back.

4. **Watch a value over time.** In the `Watch1` section set *Enabled*, *Function* `FC4 — Input
   registers`, *Address* `10`, *Field type* `Float32`. Its value is a sine wave with a 60-second period,
   charted as a measuring point.

## Simulated register map

All addresses are protocol (base-0). The simulator advances once per second; `tick` below is the number
of seconds since it started.

### Input registers (read-only, FC4)

| Address | Type / order | Content |
|---------|--------------|---------|
| 0 | UInt16 | Seconds counter |
| 1 | Int16 | Fixed `-1234` — a value the unsigned column shows as `64302` |
| 2-3 | UInt32, MswToLsw | `tick × 10` |
| 4-5 | Int32, MswToLsw | Fixed `-123456` |
| 6-7 | Float32, MswToLsw | π |
| 8-9 | Float32, **LswToMsw** | π again, words exchanged |
| 10-11 | Float32, MswToLsw | Sine wave, ±100.0, 60 s period |
| 12-15 | UInt64, ABCD | Uptime in milliseconds |
| 16-19 | Float64, ABCD | e |
| 20-27 | ASCII | `DALE-SIM-SERVER!` |
| 40-49 | UInt16 | Live echo of holding registers 0-9 |

### Holding registers (read/write, FC3/FC6/FC16)

| Address | Type / order | Content |
|---------|--------------|---------|
| 0-9 | UInt16 | Scratch space — whatever you write appears at input registers 40-49 on the next tick |
| 100-101 | Float32, MswToLsw | Seeded with π; overwrite it and read it back |

### Coils and discrete inputs (FC1/FC2/FC5/FC15)

| Area | Address | Content |
|------|---------|---------|
| Coils | 0-7 | The tick counter in binary — coil *n* flips every 2ⁿ seconds |
| Coils | 8-15 | Static alternating pattern |
| Discrete inputs | 0-15 | A single set bit, walking one position per second |

## Debugging a real device

Turn `SimServer` off (or remove it from the topology), then set *Server address* and *Port* to your
device. Most devices use port 502.

The Solar-Log™ Modbus TCP direct-marketing interface is a good worked example, because it documents
exactly the trap this tool exists for: the byte order follows the Modbus standard (most significant
byte first), but the **word order for 32-bit values is little-endian** — the low word sits in the first
register. Reading it with the standard word order produces plausible-looking nonsense rather than an
obvious error.

Connect with unit id `1` on port `502`, then:

| Register | Field type | Word order | Meaning |
|----------|-----------|------------|---------|
| 10901 | `UInt16` | — | Grid operator's limit, in percent (FC4) |
| 10902 | `Float32` | `LswToMsw` | Grid operator's limit, in kW (FC4) |
| 10904 | `UInt32` | `LswToMsw` | Current generation, in W (FC4) |
| 10910 | `Int32` | `LswToMsw` | Grid feed-in (+) or draw (−), in W (FC4) |
| 10401 | `UInt16` | — | Power limit setpoint, in percent (FC6) |

Read `10902` under both word orders to see the difference for yourself: with `MswToLsw` the value is
wildly wrong, with `LswToMsw` it is the kW figure the device meant.

## What is in the project

- `LogicBlocks/ModbusTcpDebugClient.cs` — the client. Reads go over the wire as raw reads and are
  decoded locally, so the hex, unsigned, signed and interpreted columns of one result always describe
  the same bytes. It opens two connections: one carries the poll loop and the watch slots, the other
  carries the reads and writes you trigger by hand, so a slow poll cannot delay a manual command.
- `LogicBlocks/WatchSlot.cs` — one pinned register. How many slots an instance has is decided at
  configuration time by the `WatchSlotCount` instantiation parameter (RFC 0016); slots above the count
  do not exist rather than sitting empty.
- `LogicBlocks/ModbusTcpSimServer.cs` — the simulated device described above.

## Limitations

FC1-FC4 reads and FC5/FC6/FC15/FC16 writes are supported. Address scanning, device discovery and a raw
frame log are not part of this iteration.
