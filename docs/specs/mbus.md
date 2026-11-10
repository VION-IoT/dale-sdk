---
trace: enforced
---

# M-Bus: what a logic block gets when it reads a wired meter

What the SDK guarantees a logic-block author who reads wired M-Bus meters (EN 13757-2/-3) through the
service provider that owns the M-Bus master. One packable package ships it, `Vion.Dale.Sdk.MBus`, beside
its test kit `Vion.Dale.Sdk.MBus.TestKit`. Area code `MBUS`. Process: [`../spec-process.md`](../spec-process.md).

The provider returns the telegrams exactly as the meter sent them; the contract checks and decodes them on
the block's side, and a block picks values out of the readout by meaning or by code. The block-facing
surface copies `IModbusClient`'s: the settings, the `(result, receipt)` callbacks, the refusal rule, the
link summary and one shared handler per runtime.

Cited rather than restated: [`contracts.md`](contracts.md) for binding, the handler base, link maps and
publishing (`AC-BIND-*`); [`block-lifecycle.md`](block-lifecycle.md) for the dispatcher every callback
arrives through and for the containment of an exception a callback throws (`AC-LIFE-014.2`);
[`plugin-loading.md`](plugin-loading.md) for what a shared assembly is; [`emission.md`](emission.md) for
the link summary's default interval (`AC-EMIT-002.8`); [`testkit.md`](testkit.md) for the kit
(`AC-TKIT-016.*`).

## The read and its callbacks

- `AC-MBUS-001.1` (Ubiquitous): THE SYSTEM SHALL offer an M-Bus read on a service-provider contract type
  whose token is `MBus`, SHALL return from it immediately and SHALL deliver the result later through the
  `IActorDispatcher` the call was given.
- `AC-MBUS-001.2` (Ubiquitous): THE SYSTEM SHALL hand an `MBusReceipt` to both the success and the error
  callback of every read, carrying the wall-clock instant the response or failure was observed, the same
  instant on the monotonic scale, the round trip, the queued wait and the outcome.
- `AC-MBUS-001.3` (Event-driven): WHEN a primary address below 0 or above 250, or a telegram count below
  1, is passed THE SYSTEM SHALL complete the read through its error callback with an `Invalid` receipt,
  without publishing it.
- `AC-MBUS-001.4` (Ubiquitous): THE SYSTEM SHALL complete every failed read through the error callback
  with an `MBusException` whose outcome equals the receipt's.
- `AC-MBUS-001.5` (Ubiquitous): THE SYSTEM SHALL render every number and duration in an M-Bus exception
  message and in a description of a telegram or readout in the invariant culture.

A timeout outside its limits is the one argument refused inline (`AC-MBUS-004.2`); an address or count
out of range reaches the error callback, so a caller handles a refusal in one place. Every failure is
logged once per read: at error level, but at debug level for a read that expired or was dropped, which a
congested runtime produces in bursts.

## Enablement

- `AC-MBUS-002.1` (Ubiquitous): THE SYSTEM SHALL default an M-Bus contract to disabled.
- `AC-MBUS-002.2` (State-driven): WHILE an M-Bus contract is disabled THE SYSTEM SHALL execute no read
  and SHALL invoke neither callback.
- `AC-MBUS-002.3` (Ubiquitous): THE SYSTEM SHALL complete a read it has already accepted when the
  contract is disabled afterwards.

## One read in flight

- `AC-MBUS-003.1` (Event-driven): WHEN a read is requested while the same contract's previous read has
  not completed THE SYSTEM SHALL publish nothing, SHALL invoke neither of the new read's callbacks,
  whatever its arguments, and SHALL count it in the link summary as busy.
- `AC-MBUS-003.2` (Event-driven): WHEN the read holding a contract's slot succeeds, fails, times out or
  expires THE SYSTEM SHALL accept the same contract's next read, and SHALL let no read refused for its
  arguments take or free the slot.

The busy check comes before anything else, so a busy read does nothing at all even when its arguments are
invalid. The rule bounds the provider's queue at one read per binding mapped to a line, and a block issuing
one read per interval never meets it: one read returns the whole meter.

## Timeouts and expiry

- `AC-MBUS-004.1` (Ubiquitous): THE SYSTEM SHALL default the operation timeout to 300 seconds and the
  maximum queued age to 30 seconds, and SHALL accept `null` as no queued-age limit.
- `AC-MBUS-004.2` (Event-driven): WHEN an operation timeout or a maximum queued age, set on a contract
  or passed with a single read, is zero, negative, or longer than `int.MaxValue` milliseconds THE SYSTEM
  SHALL throw `ArgumentOutOfRangeException`.
- `AC-MBUS-004.3` (Ubiquitous): THE SYSTEM SHALL start the operation timeout when the handler publishes
  the read, and SHALL measure the maximum queued age over the hop from the block to the handler.
- `AC-MBUS-004.4` (Event-driven): WHEN a read has waited strictly longer than the maximum queued age
  when the handler takes it THE SYSTEM SHALL complete it through its error callback with an `Expired`
  receipt, without publishing it.
- `AC-MBUS-004.5` (Event-driven): WHEN no response to a published read has arrived when its operation
  timeout has passed THE SYSTEM SHALL complete it through its error callback with a `Timeout` receipt
  within about a second.
- `AC-MBUS-004.6` (Ubiquitous): THE SYSTEM SHALL publish each read with a deadline of the moment the
  handler publishes it plus the read's operation timeout.
- `AC-MBUS-004.7` (Event-driven): WHEN no answer to a read has reached its contract by its operation
  timeout, plus its maximum queued age where it has one, plus ten seconds THE SYSTEM SHALL complete the
  read through its error callback with a `Timeout` receipt, SHALL accept the contract's next read, and
  SHALL ignore an answer to it that arrives later.

Because the timeout starts at the publish, it covers the read's wait in the provider's line queue behind
other meters, which is why its default is minutes rather than seconds.

## The wire and the shared handler

- `AC-MBUS-005.1` (Ubiquitous): THE SYSTEM SHALL publish a read as a `GetMBusPayload` JSON document
  carrying the primary address and the telegram count, labelled with the record's name as its schema and
  with the JSON content type.
- `AC-MBUS-005.2` (Event-driven): WHEN a response with the code `Ok` arrives and every telegram it
  carries passes the frame checks THE SYSTEM SHALL decode the telegrams in order and SHALL deliver the
  readout to the success callback.
- `AC-MBUS-005.3` (Event-driven): WHEN a response with the code `NoResponse`, `Garbled`,
  `LineUnavailable` or `NotSupported` arrives THE SYSTEM SHALL complete the read through its error
  callback with the outcome `NoResponse`, `Garbled`, `LineUnavailable` or `Invalid` respectively, and
  SHALL carry the provider's code on the exception.
- `AC-MBUS-005.4` (Event-driven): WHEN a read arrives for a contract with no service-provider mapping
  THE SYSTEM SHALL complete it with an `Invalid` receipt.
- `AC-MBUS-005.5` (Event-driven): WHEN a response arrives whose correlation identifier matches no
  pending read THE SYSTEM SHALL ignore it.
- `AC-MBUS-005.6` (Event-driven): WHEN the shared pending-request limit is reached THE SYSTEM SHALL
  complete the arriving read with a `Dropped` receipt, without publishing it.
- `AC-MBUS-005.7` (Ubiquitous): THE SYSTEM SHALL route every M-Bus binding in a runtime through one
  shared handler, and SHALL mark the M-Bus assembly a shared assembly.
- `AC-MBUS-005.8` (Event-driven): WHEN a response carries a response code the system does not know THE
  SYSTEM SHALL complete the read through its error callback with an `Invalid` outcome and SHALL leave
  the link state unchanged.

The read is published on `{contract}/hw/mbus/get` as `{ "primaryAddress": 1, "maxTelegrams": 5 }`, with
an MQTT 5 response topic (`…/hw/mbus/get/dale/response`), correlation data and message expiry. The answer
is `{ "responseCode": "Ok", "errorMessage": null, "telegrams": ["<base64>", …] }`: every RSP_UD long frame
from 68h to 16h inclusive, one base64 string each, and telegrams only with `Ok`. JSON names are camelCase
and the response code travels by name.

What a provider owes, which this SDK neither sees nor checks:

1. One request at a time per line, first in, first out; a full queue answers `LineUnavailable` with an
   `errorMessage` that says so.
2. The link layer: SND_NKE, then REQ_UD2 with the frame-count bit toggled, until a telegram arrives
   without the more-records marker (DIF 1Fh) or `maxTelegrams` telegrams are in hand.
3. Every frame verbatim, decoding nothing; a provider that reads fewer telegrams than asked answers `Ok`
   with the ones it read.
4. A request whose MQTT message expiry has passed before its turn is dropped unanswered.
5. Every other request answered exactly once, including one on an action or with a member the provider
   does not implement, which it answers `NotSupported`.
6. Line settings kept as the provider's own service properties.

## Frame checks and decoding

- `AC-MBUS-006.1` (Event-driven): WHEN an `Ok` response carries no telegram or more telegrams than
  asked, or a telegram with a wrong start, length, checksum or stop byte, a CI other than 72h, an
  address other than the one asked, an identification other than the first telegram's, or records that
  cannot be decoded THE SYSTEM SHALL complete the read through its error callback with a `ProtocolError`
  receipt and the telegrams as received.
- `AC-MBUS-006.2` (Ubiquitous): THE SYSTEM SHALL decode a telegram into its fixed header and its data
  records, and SHALL keep each record's data information block, value information block and data bytes
  as received.
- `AC-MBUS-006.3` (Ubiquitous): THE SYSTEM SHALL scale a record's value by its value information's
  exponent and by its multiplicative and additive correction extensions into the unit its quantity is
  reported in.
- `AC-MBUS-006.4` (Event-driven): WHEN a record's value information is not in the decoder's table THE
  SYSTEM SHALL keep the record with no quantity and its bytes, and SHALL decode the rest of the
  telegram.
- `AC-MBUS-006.5` (Event-driven): WHEN a record carries a record error code THE SYSTEM SHALL keep the
  record with no value.
- `AC-MBUS-006.6` (Ubiquitous): THE SYSTEM SHALL report a readout complete only when its last telegram
  does not announce further records.
- `AC-MBUS-006.7` (Ubiquitous): THE SYSTEM SHALL take a readout's header from its first telegram.
- `AC-MBUS-006.8` (Ubiquitous): THE SYSTEM SHALL report a meter error from the status byte when its
  application status reads 10b or 11b or its permanent or temporary error bit is set, and SHALL report
  none for the manufacturer-specific bits alone.

The decoder's table is EN 13757-3's primary VIF table, the FDh entries for voltage, current and
dimensionless values, and the FBh codes 02h (reactive energy), 17h (reactive power), 2Eh (frequency) and
37h (apparent power). A primary VIF whose quantity `MBusQuantity` does not name, and every other code,
decodes with no quantity and its raw number. Of the extensions after the true VIF, the multiplicative
(E111 0nnn, E111 1101) and additive (E111 10nn) corrections are folded into the scale, a record error code
(E00x xxxx) leaves the record without a value, and every other extension changes the record's meaning.
Only CI 72h is decoded; another CI is a protocol error. Date and time types F, G and I become a timestamp;
type J, a time of day, has none.

## Selecting records

- `AC-MBUS-007.1` (Ubiquitous): THE SYSTEM SHALL select a record by quantity, storage number, tariff,
  subunit and function, defaulting to the instantaneous value of storage 0, tariff 0 and subunit 0 and
  passing over records with a meaning-changing extension, or by its exact information-block bytes, and
  SHALL return the first match in telegram order.
- `AC-MBUS-007.2` (Event-driven): WHEN no record matches a selector THE SYSTEM SHALL return no value.
- `AC-MBUS-007.3` (Ubiquitous): THE SYSTEM SHALL convert a selected value into a unit the caller names
  among its quantity's units, and SHALL throw an `ArgumentException` naming the selector, the record and
  the unit when the record cannot be expressed in it.
- `AC-MBUS-007.4` (Event-driven): WHEN a selector matches nothing on a complete readout THE SYSTEM SHALL log it once per selector and contract. GAP: a log call, which `../testing-conventions.md` § 15 forbids asserting on.
- `AC-MBUS-007.5` (Ubiquitous): THE SYSTEM SHALL give, for any record of a readout that a selector can
  reach, a selector that finds that record in it, SHALL give none for a record whose data and value
  information blocks repeat an earlier record's, whatever its data, and SHALL describe a telegram or
  readout one line per record, each with its selector or the record that shadows it.

A selector by meaning names its quantity, so a unit outside that quantity's family is refused whether or
not the readout carries the record. A miss on an incomplete readout is expected and not logged. A selector
prints as the C# that constructs it, which is what a description hands an author to paste.

## The link summary

- `AC-MBUS-008.1` (Ubiquitous): THE SYSTEM SHALL accumulate every completed read of one contract into a
  link summary readable at any time without blocking the read updating it.
- `AC-MBUS-008.2` (Ubiquitous): THE SYSTEM SHALL set the link state to `Online` on a success, to
  `Faulted` on no response, a garbled answer, an unavailable line, a timeout or a protocol error, and
  SHALL leave it unchanged on every other outcome.
- `AC-MBUS-008.3` (Ubiquitous): THE SYSTEM SHALL keep a lifetime counter for every outcome and for busy
  reads, SHALL feed round-trip figures only from reads that were published and answered, and SHALL feed
  queued-wait figures only from reads that reached the handler.
- `AC-MBUS-008.4` (Ubiquitous): THE SYSTEM SHALL report, for round trip and for queued wait separately,
  the mean, the maximum and the number of reads over at least the last 15 minutes and less than the last
  16 on the contract's clock, or over its whole life where that is shorter.

A round trip comes from reads the provider answered: success, no response, garbled, line unavailable and
protocol error. A timeout was published but not answered, and an invalid read may never have been
published, so neither feeds it. Queued wait comes from every outcome but invalid.

## The published surface

- `AC-MBUS-009.1` (Ubiquitous): THE SYSTEM SHALL classify every public type the M-Bus packages ship as
  published surface or internal plumbing, and SHALL judge their declarations with the Dale analyzers.

Published: the contract interface `IMBus`; the readout, telegram, header, record and status types; the
selector, quantity, unit and function types; the receipt, outcome, link state and link summary; and the one
exception every failure arrives as. Plumbing: the contract implementation, the handler and the actor
messages, which are public only so the runtime and the kit can reach them. The wire records are internal.
