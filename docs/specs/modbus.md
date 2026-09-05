---
trace: enforced
---

# Modbus: the client surface, the link policy, the hosted server and the two transports

What the SDK guarantees a logic-block author who talks Modbus. One surface — `IModbusClient` — is
shared by both transports, so a device sold as TCP *and* as RTU is one block; everything below it
differs because a socket and a shared serial bus differ. The area also hosts the other role: a logic
block that *is* a Modbus server, which masters elsewhere on the network read and write. Area code
`MODB`. Process: [`../spec-process.md`](../spec-process.md).

The spine is the order an author meets the machinery: the shared client surface and its receipt, the
Modbus TCP link policy, the request queue, register-word conversion, the hosted server, the Modbus
RTU contract, the diagnostics the SDK owns, registration and the socket's lifetime, and the test
discipline the area's own suites follow.

Cited rather than restated: [`contracts.md`](contracts.md) for how a block binds `IModbusRtu` and
what `ServiceProviderHandlerBase` does around the RTU handler;
[`plugin-loading.md`](plugin-loading.md) for what `[assembly: DaleSharedAssembly]` does to a type's
identity; [`emission.md`](emission.md) for what publishing a diagnostics struct as a
`[ServiceProperty]` costs and when it emits; [`introspection.md`](introspection.md) for what a
`[StructField]` annotation reaches the wire as; [`block-lifecycle.md`](block-lifecycle.md) for the
per-block DI scope that reclaims a client and for what a stop hook can and cannot finish;
[`scenarios.md`](scenarios.md) and [`devhost-control.md`](devhost-control.md) for how the development
host drives and stands in for a Modbus binding. Architecture decisions
[`0118`](../../architecture/decisions/0118-sdk-owns-protocol-link-diagnostics.md) (the SDK owns
protocol link diagnostics) and
[`0119`](../../architecture/decisions/0119-transaction-receipt-in-callback-signature.md) (the receipt
rides in the callback signature) are the design authority for the diagnostics and the callback shape
and are cited, never re-argued.

**No `DALE` diagnostic judges any declaration in these three projects.** None of
`Vion.Dale.Sdk.Modbus.Core`, `.Tcp` or `.Rtu` carries the analyzer `ProjectReference` that
`Vion.Dale.Sdk.DigitalIo` and `.AnalogIo` do, so `AC-ANLZ-018.*` puts every type, mark and XML doc
here outside the pack. The `[PublicApi]` marks and the documentation below are author discipline with
no gate behind them; only the PublicApi manifest snapshot notices when the marked set changes.

## The shared client surface

Both transports implement `IModbusClient`, whose whole surface is four properties and thirty-six
operations: two bit reads, twenty register reads (ten value shapes on each of the two read-only and
read-write areas), two bit writes, two single-register writes and ten multiple-register writes. The
value shapes are `Raw`, `short`, `ushort`, `int`, `uint`, `float`, `long`, `ulong`, `double` and
`string`.

- `AC-MODB-001.1` (Ubiquitous): THE SYSTEM SHALL offer the same read and write operations, with the same signatures, on the Modbus TCP client and on the Modbus RTU contract. GAP: no test project references both transports, and the sameness is structural — both interfaces inherit the operations rather than re-declaring them.
- `AC-MODB-001.2` (Ubiquitous): THE SYSTEM SHALL return from every read and write immediately and
  deliver the result later, through the `IActorDispatcher` the call was given.
- `AC-MODB-001.3` (Ubiquitous): THE SYSTEM SHALL hand a `ModbusReceipt` to both the success and the
  error callback of every device operation, carrying the wall-clock instant the response was
  observed, the same instant on the monotonic scale, the round trip, the queued wait and the outcome.
- `AC-MODB-001.4` (Ubiquitous): THE SYSTEM SHALL stamp a receipt's instants where the response or
  failure was observed, before the callback is handed to the block, so a block that is slow to drain
  its mailbox does not move the timestamps of the values it reads.
- `AC-MODB-001.5` (Ubiquitous): THE SYSTEM SHALL require a success callback on a read and accept a
  write without one.
- `AC-MODB-001.6` (Ubiquitous): THE SYSTEM SHALL log every failure whether or not an error callback was supplied. GAP: a log call, which `../testing-conventions.md` § 15 forbids asserting on.
- `AC-MODB-001.7` (Ubiquitous): THE SYSTEM SHALL count a register read's and write's span in
  registers under the parameter named `quantity`, and a 32- or 64-bit read's span in values under the
  parameter named `count`.
- `AC-MODB-001.8` (Event-driven): WHEN handing a completed Modbus TCP transaction to the block's
  dispatcher throws THE SYSTEM SHALL contain the failure, so one block's mailbox cannot stop the
  client's queue.
- `AC-MODB-001.9` (Ubiquitous): THE SYSTEM SHALL render every number and duration in a Modbus
  exception message in the invariant culture.
- `AC-MODB-001.10` (Ubiquitous): THE SYSTEM SHALL make each accepted operation a request carrying the
  operation's name, an identity of its own, and the caller's arguments unchanged — unit identifier,
  address, span, payload and conversion options — and SHALL deliver the device's answer converted to
  the value shape the operation names.

`AC-MODB-001.9` is [`../sdk-surface-conventions.md`](../sdk-surface-conventions.md) § 4's rule at
this surface, and the reason it is a criterion rather than a convention here is that these messages
are shipped text: a consumer publishes `exception.Message` onto a service property, and the machine
that renders it is a gateway commissioned in whatever locale its image carried, not the engineer
reading it.

`AC-MODB-001.7` is the one place the surface speaks two vocabularies for the same argument position,
and it is deliberate: a `count` of 4 doubles is 16 registers, and the conversion between them is the
data converter's. Renaming either would make the confusion quieter rather than louder. No call site
in the first consumer passes them by name.

`AC-MODB-001.3`'s receipt is decision `0119`: there are no compatibility overloads, and a caller that
does not want it discards it (`(values, _) => Power = values[0]`).

## Enablement

- `AC-MODB-002.1` (Ubiquitous): THE SYSTEM SHALL default a Modbus client, and a hosted Modbus server,
  to disabled.
- `AC-MODB-002.2` (Event-driven): WHILE a client is disabled THE SYSTEM SHALL execute no read, write
  or disconnect, and SHALL invoke neither callback.
- `AC-MODB-002.3` (Ubiquitous): THE SYSTEM SHALL run a request it has already accepted when the
  client is disabled afterwards.
- `AC-MODB-002.4` (Event-driven): WHEN a Modbus TCP client is enabled for the first time THE SYSTEM
  SHALL create its request queue, and SHALL create it once.

`AC-MODB-002.2` is what lets a block be configured field by field without half-set configuration
failing, and it is why a block must never wait on a callback to learn that a disabled client did
nothing — including from `Disconnect`, the one member whose own documentation used to omit the gate.

## Timeouts and staleness

- `AC-MODB-003.1` (Ubiquitous): THE SYSTEM SHALL measure an operation timeout over the wire exchange
  only — from dispatch to the complete response — and not over the wait before dispatch, parameter
  validation or data conversion.
- `AC-MODB-003.2` (Ubiquitous): THE SYSTEM SHALL default the operation timeout to 1 second on Modbus
  TCP and to 5 seconds on Modbus RTU, and the connect timeout to 3 seconds.
- `AC-MODB-003.3` (Event-driven): WHEN an operation passes no timeout of its own THE SYSTEM SHALL use
  the client's default, fixed at the moment the operation was accepted.
- `AC-MODB-003.4` (Event-driven): WHEN a connect timeout, an operation timeout or a maximum queued age
  — set on a client or passed with a single operation — is zero, negative, or longer than
  `ModbusTimeoutLimits.MaxTimeout` THE SYSTEM SHALL throw `ArgumentOutOfRangeException`.
- `AC-MODB-003.5` (Ubiquitous): THE SYSTEM SHALL default the maximum queued age to 30 seconds on both
  transports and SHALL accept `null` as no limit.
- `AC-MODB-003.6` (Event-driven): WHEN a request has waited strictly longer than the maximum queued
  age at the moment its turn comes THE SYSTEM SHALL complete it through its error callback with a
  `RequestExpiredException` and an `Expired` receipt, without contacting the device.
- `AC-MODB-003.7` (Ubiquitous): THE SYSTEM SHALL read the maximum queued age at dispatch, so a value
  set now applies to requests already waiting.

`AC-MODB-003.4` is the boundary the four knobs and the per-call timeout share. Before it, two of them
took a zero: a zero connect timeout failed every connect and armed a backoff against a device that was
answering, and a zero operation timeout faulted the link and closed the socket without touching the
wire. The consumer offers both as free-form commissioning fields, which is how a zero arrives. The
upper bound is the framework timer's, not the protocol's: a duration past it is refused by the
cancellation source each operation arms, and on Modbus RTU it overflows the instant the shared handler
computes the request's expiry — so an unchecked value came back as a transport fault that closed the
socket, or as no callback at all. `ModbusTimeoutLimits.MaxTimeout` publishes it, a value exactly at it
is accepted, and it is the smallest ceiling any runtime this SDK's plugins load into carries, so the
refusal is the same wherever the block runs.

## What a call is refused for

Every refusal below is decided before the request reaches the device, reported through the error
callback, and recorded with an `Invalid` outcome — so a caller never has to handle the same error
both inline and later from its callback.

- `AC-MODB-004.1` (Event-driven): WHEN a unit identifier below 0 or above 255 is passed THE SYSTEM
  SHALL refuse the operation with an `InvalidUnitIdentifierException`.
- `AC-MODB-004.2` (Ubiquitous): THE SYSTEM SHALL accept the unit identifiers 0 and 255, which address
  a gateway and a direct TCP device.
- `AC-MODB-004.3` (Event-driven): WHEN an operation would carry no addresses at all — a count of zero,
  or a quantity of zero — or a count would require more than 65535 registers, THE SYSTEM SHALL refuse
  it with an `InvalidCountException`.
- `AC-MODB-004.4` (Event-driven): WHEN a read or write would carry more than the Modbus protocol's
  data unit allows — 125 registers to read, 123 registers to write, 2000 bits to read, 1968 bits to
  write — THE SYSTEM SHALL refuse it with an `InvalidCountException` before it reaches the wire.
- `AC-MODB-004.5` (Event-driven): WHEN a byte order, word order or text encoding outside the declared
  set is passed THE SYSTEM SHALL refuse the operation with the exception named for that conversion.

`AC-MODB-004.3`'s two ends were one for a while: a `count` of zero was refused where it is converted
to a quantity, while a `quantity` of zero — the shape the bit, raw, 16-bit and string families take
straight from the caller — was dispatched, and came back a code-less frame fault that closed the
socket under `AC-MODB-008.1`. Both ends are decided in the one validation both transports run.

`AC-MODB-004.4`'s four numbers are the protocol's, not a device's: no standard function code has a
field wide enough to answer more, so the SDK used to send a request the device could only refuse,
drop or truncate — and the block saw a `DeviceError` or a `Timeout` with nothing pointing at the
request. They are published as `ModbusProtocolLimits` so a block that splits a large span can name
them rather than repeat them. The largest single call in the first consumer reads 40 registers.

## Turning register words into values

- `AC-MODB-005.1` (Ubiquitous): THE SYSTEM SHALL swap register bytes only when the requested byte
  order differs from the host's endianness, and the two plain 32- and 64-bit word orders only when the
  requested word order differs from it.
- `AC-MODB-005.2` (Ubiquitous): THE SYSTEM SHALL always rearrange for the two mid-endian 64-bit word
  orders, in the direction the host requires.
- `AC-MODB-005.3` (Ubiquitous): THE SYSTEM SHALL decide whether a byte order, a word order or a text
  encoding is supported before it reads the buffer, so a buffer that is empty or not a whole number of
  values is refused or accepted alike across the three swaps.
- `AC-MODB-005.4` (Event-driven): WHEN a device's response carries a byte count that is not a multiple
  of the requested value's size THE SYSTEM SHALL refuse the operation with a
  `ModbusResponseAlignmentException` and a `ProtocolError` receipt.
- `AC-MODB-005.5` (Event-driven): WHEN a device returns fewer bits than were requested THE SYSTEM
  SHALL refuse the operation with an `InvalidBitQuantityException`.
- `AC-MODB-005.6` (Ubiquitous): THE SYSTEM SHALL unpack bits from the least significant bit of each
  byte and ignore the padding bits of the last one.
- `AC-MODB-005.7` (Ubiquitous): THE SYSTEM SHALL encode and decode text as ASCII, UTF-8, UTF-16
  little-endian or UTF-16 big-endian, reading and writing the bytes in their natural sequential order
  with no byte or word swap, and SHALL append one zero byte when an encoded string is an odd number of
  bytes.

- `AC-MODB-005.8` (Ubiquitous): THE SYSTEM SHALL move values between a byte buffer and a value array
  by reinterpreting the bytes in the host's own layout, dropping a tail shorter than one value, and
  SHALL represent a `bool` as the byte 1 or 0.
- `AC-MODB-005.9` (Ubiquitous): THE SYSTEM SHALL default a byte order to most-significant-byte-first,
  a 32-bit word order to most-significant-word-first, a 64-bit word order to `ABCD` and a text
  encoding to ASCII, on every operation that takes them.

`AC-MODB-005.9` is the Modbus standard's own order, so a block written for a standards-compliant
device names none of them; a device that differs is the exception, and says so at the call site.

`AC-MODB-005.4` is what turns a short frame into a diagnosis: the cast that follows it reinterprets
bytes and drops a ragged tail in silence. `AC-MODB-005.7`'s natural order is the client's and the
server's alike — a device that stores text in a non-standard layout goes through the raw family,
because the swap that would serve it is a host-memory transform that inverts on a little-endian host.

## The Modbus TCP link policy

A Modbus TCP client owns one socket and one request queue. It reports two states, and they answer
different questions: `Connection.State` is about the transport, `Link.State` about the device.

- `AC-MODB-006.1` (Ubiquitous): THE SYSTEM SHALL establish the connection lazily, inside the first
  operation that needs one, and keep it for later operations.
- `AC-MODB-006.2` (Ubiquitous): THE SYSTEM SHALL default the port to 502.
- `AC-MODB-006.3` (Event-driven): WHEN a port outside 1–65535 is set on a Modbus TCP client or on a
  hosted Modbus TCP server THE SYSTEM SHALL throw a `FormatException`.
- `AC-MODB-006.4` (Event-driven): WHEN an address that is null, empty, whitespace or not a literal IP
  address is set THE SYSTEM SHALL throw a `FormatException`.
- `AC-MODB-006.5` (Event-driven): WHEN an operation runs before an address has been set THE SYSTEM
  SHALL refuse it with an `IpAddressNotSetException` and an `Invalid` receipt, without recording a
  connect attempt.
- `AC-MODB-006.6` (Event-driven): WHEN an address or a port is set to the value already in force THE
  SYSTEM SHALL do nothing — no reconnect, and no change to an armed backoff.
- `AC-MODB-006.7` (Event-driven): WHEN an address or a port changes THE SYSTEM SHALL reconnect on the
  next operation and clear any armed connect backoff.

`AC-MODB-006.3` closes the sentinel both roles used to accept: port 0 asks the operating system for
an ephemeral port, which is a guaranteed failure on the client and an endpoint nobody can be pointed
at on the server, and it is what an unset configuration field binds to. `AC-MODB-006.6` is why a
consumer that re-applies its whole configuration on every edit does not drop its socket for an
unrelated one — the committed `modbus-link-policy` scenario re-applies the entire connection struct
and asserts the connect count does not move.

## The connect backoff

- `AC-MODB-007.1` (Ubiquitous): THE SYSTEM SHALL arm no connect backoff before the second consecutive
  failed connect.
- `AC-MODB-007.2` (Event-driven): WHEN the second consecutive connect fails THE SYSTEM SHALL wait the
  configured backoff, doubling it on each further consecutive failure up to the configured maximum,
  and SHALL never overflow however long the run of failures.
- `AC-MODB-007.3` (Event-driven): WHEN a device operation is issued while a connect backoff is still
  running THE SYSTEM SHALL fail it immediately with a `LinkBackoffException` and a `BackedOff` receipt
  naming the endpoint, the consecutive-failure count and the next attempt.
- `AC-MODB-007.4` (Event-driven): WHEN an armed backoff's instant has passed THE SYSTEM SHALL let the
  next operation attempt a connection, and a successful connect SHALL clear the backoff.
- `AC-MODB-007.5` (Ubiquitous): THE SYSTEM SHALL offer no backoff value that turns the wait off, and
  SHALL produce a constant wait when the initial and maximum values are equal.
- `AC-MODB-007.6` (Event-driven): WHEN a client is re-enabled THE SYSTEM SHALL clear any armed connect
  backoff.

`AC-MODB-007.3` is what drains a queue during an outage instead of filling it: without it every
queued request pays its own connection attempt against a device that is not there.
`AC-MODB-007.4`'s "next operation" is exactly one attempt because the queue has one consumer.

## Wire faults and the socket

- `AC-MODB-008.1` (Event-driven): WHEN an operation on an established connection ends in a timeout, a
  transport error or a protocol error THE SYSTEM SHALL close the socket, so the next operation
  reconnects.
- `AC-MODB-008.2` (Event-driven): WHEN an operation fails for any other reason THE SYSTEM SHALL keep
  the socket open.
- `AC-MODB-008.3` (Ubiquitous): THE SYSTEM SHALL report a device that answers with a Modbus exception
  code as a `DeviceError` and keep the socket, and a frame or protocol fault carrying no code as a
  `ProtocolError` that closes it.
- `AC-MODB-008.4` (Ubiquitous): THE SYSTEM SHALL never retry an operation automatically.

`AC-MODB-008.1` is what reaches a peer that dropped and came back without operator action, and what
keeps a stray response from being read as the next transaction's answer. `AC-MODB-008.4` is
deliberate: a read is re-polled by construction, and repeating a write after a fault would write a
pulse twice.

## The request queue

- `AC-MODB-009.1` (Ubiquitous): THE SYSTEM SHALL execute one Modbus TCP client's operations strictly
  one at a time, in the order they were issued.
- `AC-MODB-009.2` (Event-driven): WHEN a queue capacity or overflow policy is set to a different value
  after the queue has been created THE SYSTEM SHALL throw an `InvalidOperationException`, and WHEN it
  is set to the value already in force THE SYSTEM SHALL do nothing.
- `AC-MODB-009.3` (Ubiquitous): THE SYSTEM SHALL default the queue capacity to 256 and the overflow
  policy to dropping the oldest request.
- `AC-MODB-009.4` (Event-driven): WHEN the queue is full THE SYSTEM SHALL apply the configured policy —
  drop the oldest, drop the newest already waiting, or reject the arriving request — and complete the
  evicted one through its error callback with a `RequestDroppedException` and a `Dropped` receipt.
- `AC-MODB-009.5` (Ubiquitous): THE SYSTEM SHALL exclude the request currently executing from the
  reported queue depth.
- `AC-MODB-009.6` (Ubiquitous): THE SYSTEM SHALL exempt a control operation from the queued-age check,
  from the link summary and from the receipt, and SHALL let it run during a connect backoff without
  ending the backoff.
- `AC-MODB-009.7` (Ubiquitous): THE SYSTEM SHALL complete each request exactly once, whatever reaches
  it first.
- `AC-MODB-009.8` (Event-driven): WHEN a queue capacity below one request, or an overflow policy
  outside the declared set, is set on a Modbus TCP client THE SYSTEM SHALL throw an
  `ArgumentOutOfRangeException` naming the value.
- `AC-MODB-009.9` (Event-driven): WHEN an operation is enqueued on a request queue that was never
  created THE SYSTEM SHALL throw an `InvalidOperationException` synchronously, without invoking either
  callback.

`AC-MODB-009.8` is the pair of knobs the queue is built from, refused where they are set rather than
by the enable that builds it: both used to be reported by that enable, which sent a commissioner to
look at the wrong line — a capacity below one from the channel, an undeclared policy from the switch
over it, and the second left a client reading enabled with no queue behind it. `AC-MODB-009.9` is what
is left of that shape and is unreachable through a client, whose enablement gate returns first; it is
reachable through the request queue the TestKit substitutes.

`AC-MODB-009.2` used to be a silent no-op, so a commissioner who raised a congested client's capacity
read the number back and kept losing requests at the old one. Re-setting the value in force stays a
no-op because that is the rule the address and port setters follow and a consumer re-applies whole
configurations. `AC-MODB-009.1` is not a policy but a property of the transport: the underlying
library cannot run concurrent operations on one connection, which is what the client factory exists
to work around.

## Disposal

- `AC-MODB-010.1` (Event-driven): WHEN a Modbus TCP client is disposed THE SYSTEM SHALL complete every
  request still waiting in its queue with a `RequestDroppedException` whose reason is that the client
  was disposed, and a `Cancelled` receipt.
- `AC-MODB-010.2` (Event-driven): WHEN a request is enqueued after the client is disposed THE SYSTEM
  SHALL complete it the same way.
- `AC-MODB-010.3` (Ubiquitous): THE SYSTEM SHALL return from disposal without waiting for the request
  in flight.
- `AC-MODB-010.4` (Ubiquitous): THE SYSTEM SHALL log a request not executed because it was backed off, expired, dropped or cancelled below the level it logs a failure at. GAP: a log level, which `../testing-conventions.md` § 15 forbids asserting on.
- `AC-MODB-010.5` (Event-driven): WHEN a Modbus TCP client is disposed THE SYSTEM SHALL release its
  request queue and its socket exactly once, report itself disabled, and stay silent on a second
  disposal.
- `AC-MODB-010.6` (Event-driven): WHEN the underlying client library throws while the socket is
  released THE SYSTEM SHALL swallow and log it, never throwing out of disposal.

`AC-MODB-010.1` is what a block's teardown write now gets. Before it, a request still queued when the
scope disposed the client was abandoned in silence — no callback, no receipt, no link record — while
the surface documented it as cancelled; the consumer's device blocks carry comments naming that
behaviour. The drain runs on the queue's own consumer rather than on the disposing thread, because
the channel has one reader and because a block's teardown must not wait on a socket.
`AC-MODB-010.5`'s "report itself disabled" is the client's half of `AC-MODB-011.4`: a disposed client
that still read enabled let `AC-MODB-002.2`'s gate pass after teardown, so a call arriving afterwards
took `AC-MODB-010.2`'s drop-with-receipt path instead of doing nothing, and the two criteria agreed
only by accident of an unstated flag.

`AC-MODB-010.3` is the other half: **whether a fire-and-forget write issued from a stop hook actually
reaches the device is not decided here** — the runtime's bounded grace period between the stop and
the actor's termination is what gives it a chance, and [`block-lifecycle.md`](block-lifecycle.md)'s
`AC-LIFE-012.*` owns it. A queued write is cancelled with a receipt; an in-flight one may still
complete inside that period.

## The hosted Modbus TCP server: configuration

A logic block can *be* a Modbus server. It is configured by properties and gated by `IsEnabled`,
exactly like the client — configure while disabled, then enable.

- `AC-MODB-011.1` (Event-driven): WHEN a listen address, a port or an area extent is set while the
  server is enabled THE SYSTEM SHALL throw an `InvalidOperationException`.
- `AC-MODB-011.2` (Ubiquitous): THE SYSTEM SHALL listen on all interfaces and on port 502 unless told
  otherwise.
- `AC-MODB-011.3` (Event-driven): WHEN enabling the server cannot bind the listener THE SYSTEM SHALL
  propagate the failure to the caller and leave the server disabled.
- `AC-MODB-011.4` (Event-driven): WHEN the server is disposed THE SYSTEM SHALL stop the listener,
  release the server, report itself disabled, and stay silent on a second disposal.
- `AC-MODB-011.5` (Ubiquitous): THE SYSTEM SHALL swallow and log a teardown race raised by the underlying server library on stop or disposal, never throwing it. GAP: the race is the third-party server's, reachable only by making it throw from inside its own teardown.
- `AC-MODB-011.6` (Event-driven): WHEN a hosted server is enabled THE SYSTEM SHALL start the listener
  on the configured address, port and area extents, WHEN it is disabled THE SYSTEM SHALL stop it, and
  WHEN either is repeated THE SYSTEM SHALL do nothing.

`AC-MODB-011.2` is a role decision: a logic-block-hosted server exists to be reached by masters
elsewhere on the network, so a simulator that must not be reachable off the machine sets loopback
explicitly. `AC-MODB-011.4` closes a state pair the surface did not define — a disposed server that
reported itself enabled, whose next disable ran a stop on a disposed proxy.

## The server's extents

An extent is a count of addresses from zero, not a size from an offset: a ten-register map at
address 0x8000 declares 0x800A. Extents drive both the wire-side answer and the block-side bounds
check, and they size no buffer — the underlying buffers always cover the full address range.

- `AC-MODB-012.1` (Ubiquitous): THE SYSTEM SHALL serve all four register areas, each bounded by its
  own declared extent, and SHALL treat an extent of zero as an area that is not served.
- `AC-MODB-012.2` (Event-driven): WHEN a client requests a range outside the declared extent of its
  area THE SYSTEM SHALL answer with an `IllegalDataAddress` Modbus exception.
- `AC-MODB-012.3` (Event-driven): WHEN a block accesses a snapshot outside the declared extent of an
  area THE SYSTEM SHALL throw an `InvalidServerAddressException` naming the area, the address, the
  quantity and the extent.
- `AC-MODB-012.4` (Event-driven): WHEN a block reads or writes an empty range through a snapshot
  register accessor THE SYSTEM SHALL throw an `ArgumentException` naming the payload.
- `AC-MODB-012.5` (Ubiquitous): THE SYSTEM SHALL decide coverage without overflowing, so a range that
  starts near the top of the address space is refused rather than wrapping.
- `AC-MODB-012.6` (Event-driven): WHEN a client sends a single-value write reported with a quantity of
  zero THE SYSTEM SHALL validate it as a range of one address.
- `AC-MODB-012.7` (Event-driven): WHEN a block writes an odd number of raw register bytes THE SYSTEM
  SHALL throw an `ArgumentException`.

`AC-MODB-012.4` separates two faults the same exception used to name: an empty payload is something
the caller built, and calling it an address fault sent an author to check extents that were fine.
`AC-MODB-012.5`'s widening is the whole mechanism — without it the sum wraps and a request past the
end reads as covered.

## The server snapshot

All register access happens inside a `Sync` callback, which runs synchronously on the caller's thread
while the server lock is held. Client requests are served from the same buffers on background
threads, and the lock is what makes one callback atomic against them.

- `AC-MODB-013.1` (Ubiquitous): THE SYSTEM SHALL execute a `Sync` callback on the caller's thread
  while holding the server lock, and SHALL offer the callback in an action form and a value-returning
  form.
- `AC-MODB-013.2` (Ubiquitous): THE SYSTEM SHALL allow `Sync` while the server is disabled and SHALL
  retain buffer contents across disable and enable cycles.
- `AC-MODB-013.3` (Event-driven): WHEN `IsEnabled` is set or the server is disposed from inside a
  `Sync` callback THE SYSTEM SHALL throw an `InvalidOperationException`.
- `AC-MODB-013.4` (Ubiquitous): THE SYSTEM SHALL keep `AC-MODB-013.3` in force for the whole of an
  outer callback, however many `Sync` calls are nested inside it.
- `AC-MODB-013.5` (Event-driven): WHEN a snapshot or one of its accessors is used after the callback
  it was given to has returned THE SYSTEM SHALL throw an `InvalidOperationException`.
- `AC-MODB-013.6` (Ubiquitous): THE SYSTEM SHALL deliver no event or callback to the logic block from a background thread; a block chooses its own cadence. GAP: an absence — the surface has no member to subscribe to, which is grep-enumerable and has no mutation.

`AC-MODB-013.3` prevents a permanent deadlock, not an inconvenience: stopping the listener joins
request-handler tasks that may themselves be waiting for the lock the callback holds, reproduced live
during the server's design review. `AC-MODB-013.4` is why the guard counts depth rather than setting
a flag — the server lock is re-entrant, so a nested callback returning used to disarm the guard for
the rest of the outer one. `AC-MODB-013.5` makes enforceable what the surface only warned about: an
accessor kept past its callback reaches the live buffers with no lock held.

## The server's endpoint and listener

- `AC-MODB-014.1` (Ubiquitous): THE SYSTEM SHALL accept requests for any unit identifier and echo the
  request's identifier in the response.
- `AC-MODB-014.2` (Event-driven): WHEN the underlying server library's per-unit buffer maps are not the shape the identifier aliasing expects THE SYSTEM SHALL refuse to construct the server, naming what it could not find. GAP: reachable only by changing the pinned library version; the real-socket suite covers the served behaviour it protects.
- `AC-MODB-014.3` (Ubiquitous): THE SYSTEM SHALL leave a function code it maps to no area for the
  underlying server to answer.
- `AC-MODB-014.4` (Ubiquitous): THE SYSTEM SHALL bind the listener with the address-reuse option, so a
  same-version redeploy can rebind a port whose previous socket is still lingering.
- `AC-MODB-014.5` (Ubiquitous): THE SYSTEM SHALL timestamp the most recent client write to any area,
  including a write that does not change the stored value.

`AC-MODB-014.1` is the endpoint behaviour the Modbus TCP specification intends for directly connected
servers, which every fielded master in the porting pipeline assumes; a strict per-unit filter would
protect nothing in a one-map-per-port topology and would break ported masters. The refusal above
fails loudly rather than degrading because a warn-and-degrade fallback to a unit-0-only server would look green in the
development host and the TestKit while every fielded master broke. `AC-MODB-014.5` counts an
unchanged re-write because a master cyclically re-asserting a setpoint must still count as alive.

## Modbus RTU

Modbus RTU reaches its devices through a runtime-wide handler shared by every RTU binding, over MQTT.
Declaring the binding is [`contracts.md`](contracts.md)'s — `IModbusRtu` is a service-provider
contract type and `AC-BIND-*` states what binds it and what the handler base does around it.

- `AC-MODB-015.1` (Ubiquitous): THE SYSTEM SHALL route every Modbus RTU binding in a runtime through
  one shared handler, so requests from all of them share one order, one pending limit and one expiry
  sweep.
- `AC-MODB-015.2` (Ubiquitous): THE SYSTEM SHALL report a Modbus RTU client's queue depth as zero. GAP: a constant on a surface with no queue behind it; the RTU TestKit's harness reads the summary and would surface a change.
- `AC-MODB-015.3` (Ubiquitous): THE SYSTEM SHALL start the operation timeout when the handler
  publishes the request, and SHALL check the maximum queued age against the hop from the block to the
  handler.
- `AC-MODB-015.4` (Ubiquitous): THE SYSTEM SHALL sweep for expired requests about once a second, so an
  operation can complete up to a second after its timeout.
- `AC-MODB-015.5` (Event-driven): WHEN the shared pending-request limit of 1000 is reached THE SYSTEM
  SHALL complete the arriving request with a `PendingRequestsLimitReachedException` and a `Dropped`
  receipt, without publishing it.
- `AC-MODB-015.6` (Event-driven): WHEN a request arrives for a contract with no service-provider
  mapping THE SYSTEM SHALL complete it with a `ServiceProviderContractMappingNotFoundException` and an
  `Invalid` receipt.
- `AC-MODB-015.7` (Event-driven): WHEN a response arrives whose correlation identifier matches no
  pending request THE SYSTEM SHALL ignore it.
- `AC-MODB-015.8` (Event-driven): WHEN turning a device's answer into the requested type fails THE
  SYSTEM SHALL correct the outcome before it reaches the link summary — to `Invalid` for a conversion
  the caller asked for and cannot be done, and to `ProtocolError` otherwise.
- `AC-MODB-015.9` (Event-driven): WHEN a logic block contract is linked a second time to a different
  service provider THE SYSTEM SHALL keep the first mapping.

`AC-MODB-015.1` is why an outcome of `Dropped` on one binding may have been caused by another block's
traffic; the zero queue depth is the honest consequence of the same sharing, because there is no
depth this client can claim as its own. `AC-MODB-015.8` is what stops a bad word order from reading as a healthy
link and a successful transaction, because the handler had already stamped the answer a success.

A block's own callback runs bare inside the block's actor on **both** transports — the dispatcher
posts rather than invokes — so a throwing callback is contained by the same middleware either way
(`AC-LIFE-014.2`). The transports differ only in what a throw from the dispatcher call itself logs.

## The link diagnostics the SDK owns

Decision `0118`: the SDK accumulates a Modbus client's link diagnostics so no consumer hand-keeps
counters. `Link` is one flat readonly record struct a block publishes as a single `[ServiceProperty]`;
what that costs and when it emits is [`emission.md`](emission.md)'s.

- `AC-MODB-016.1` (Ubiquitous): THE SYSTEM SHALL accumulate every completed transaction of one client
  into a link summary readable at any time without blocking the transaction updating it.
- `AC-MODB-016.2` (Ubiquitous): THE SYSTEM SHALL set the link state to `Online` on a success or a
  device error, to `Faulted` on a timeout, transport error or protocol error, and SHALL leave it
  unchanged on every locally decided outcome.
- `AC-MODB-016.3` (Ubiquitous): THE SYSTEM SHALL leave the link state unchanged by the passage of time. GAP: an absence — nothing in the accumulator reads a clock, and a test that advanced one would pin the absence of code rather than a behaviour.
- `AC-MODB-016.4` (Ubiquitous): THE SYSTEM SHALL record every non-successful outcome as the last
  failure, and SHALL keep a lifetime counter for eight of the ten outcomes — the five that reached the
  wire, and backed off, expired and dropped.
- `AC-MODB-016.5` (Ubiquitous): THE SYSTEM SHALL update the round-trip extremes only from transactions
  that reached the wire, and the queued-wait fields only from requests that were queued.
- `AC-MODB-016.6` (Ubiquitous): THE SYSTEM SHALL count for the lifetime of the client instance and never reset. GAP: an absence — the summary offers no reset, which is grep-enumerable from its surface.

`AC-MODB-016.2` is the split the whole outcome enum exists for: a full queue or a bad unit id is not
evidence about the device, so a congested client stays distinguishable from a broken one — which is
what the first consumer reads the state for. The state's indifference to time is a deliberate
omission: only the caller knows its own poll cadence, so a freshness rule is built from the last contact or from the
receipt's monotonic stamp. `AC-MODB-016.4` is the counter set as it is, after two sentences that
claimed the other two were counted; `AC-MODB-016.5`'s queued-wait half is why a request refused
before it was ever queued no longer clears the gauge a block reads to see congestion.

## The connection diagnostics, on Modbus TCP only

- `AC-MODB-017.1` (Ubiquitous): THE SYSTEM SHALL report a Modbus TCP client's socket separately from
  its link, as disconnected, connected or backing off, with the connect attempts, failures,
  consecutive-failure run, last handshake duration, current backoff and next attempt.
- `AC-MODB-017.2` (Ubiquitous): THE SYSTEM SHALL reset the consecutive-failure run on a successful
  connect and on a configuration change, and never reset the two totals.
- `AC-MODB-017.3` (Event-driven): WHEN an armed backoff's instant has passed THE SYSTEM SHALL report
  the state as disconnected while keeping the current backoff and next attempt filled.

`AC-MODB-017.1` is separate from the link because a socket can be up while every request times out,
and a device can be reachable across a socket re-established several times. The handshake is part of
the round trip of the operation that established it, which is what the last handshake duration is for.

## Registration, and who owns the socket's lifetime

- `AC-MODB-018.1` (Ubiquitous): THE SYSTEM SHALL register the Modbus core services and the Modbus TCP
  client, server, wrappers, proxies, request factory and queue as transients, and the two factories as
  singletons.
- `AC-MODB-018.2` (Ubiquitous): THE SYSTEM SHALL register a system clock only when the host has not already registered one. GAP: a registration-order fact, grep-enumerable from the extension; the TestKit harness depends on it and would fail loudly if it changed.
- `AC-MODB-018.3` (Ubiquitous): THE SYSTEM SHALL resolve a factory-created Modbus client or server from the factory's own provider, which is the root, so the block's scope does not reclaim it and the block that created it owns its disposal. GAP: a container-lifetime fact, grep-enumerable from the registrations; the ledger carries the open question.
- `AC-MODB-018.4` (Ubiquitous): THE SYSTEM SHALL mark as a shared assembly each Modbus assembly whose
  types cross the plugin boundary inside a contract message, and leave the others unmarked.

`AC-MODB-018.1`'s transient client is per block because one client owns one socket and one queue.
A constructor-injected client is reclaimed when the block's actor stops — that is the per-block scope
[`block-lifecycle.md`](block-lifecycle.md) owns, and it is why a block never disposes an injected
client itself. The factory is the documented exception: the SDK's own example and twelve consumer
sites create servers and clients through a factory and dispose them themselves, and whether the
factory should instead resolve from the ambient block scope is an open DI question in
[`_findings.md`](_findings.md).

`Vion.Dale.Sdk.Modbus.Core` and `Vion.Dale.Sdk.Modbus.Rtu` are marked shared assemblies because the
RTU actor messages carry their types across the plugin boundary; `Vion.Dale.Sdk.Modbus.Tcp` is
deliberately unmarked. What the marker does is [`plugin-loading.md`](plugin-loading.md)'s. The RTU
assembly registers no handler of its own, and does not need to: the runtime constructs a handler
actor by activation rather than resolution.

## Test discipline

- `AC-MODB-019.1` (Ubiquitous): THE SYSTEM SHALL keep the hosted server's real-socket integration
  tests passing unchanged as the acceptance bar for the server surface.

The link policy is provable two ways and both are used. The TestKit's fake proxy substitutes the
**proxy**, so the wrapper's real policy runs above it and a virtual clock elapses a backoff in
milliseconds — that is the fast lane, and the TestKit's own suite drives the whole state machine
through it. What only a real socket can settle is what the socket does: which errno a refused or
unroutable address produces, a half-open connection, the reuse-address bind, and a round trip that is
not zero. Two committed scenarios cover that lane on a real client/server pair, and the
`modbus-smoke` skill runs them; neither asserts the maximum queued age or the expired outcome,
because the simulated server answers too fast to build a queue that ages.

Three suites in this area bind real sockets. They take ephemeral ports, so they collide with each
other nowhere, but they must not run beside the smoke skill's host, which takes fixed ones.
