# Masking a published value — practical guidance

A block that reads a device keeps each reading in a service member. When the device stops answering,
the member still holds the last reading, and it stays published as if it were current. The block
usually cannot simply assign `null`: its own logic, the contract it serves and persistence all read
the same member.

The SDK has no mask of its own for this; a getter does the job. **Declare the published member as a
getter over the real value and a flag:**

```csharp
private double? _activePowerKw;      // the real value: control code, served contracts read this
private bool _powerWindowLive;       // true once this read has succeeded since the last outage

[ServiceProperty(Title = "Active power", Unit = "kW")]
[ServiceMeasuringPoint(Title = "Active power", Unit = "kW")]
public double? ActivePowerKw
{
    get => _powerWindowLive ? _activePowerKw : null;
}
```

The member re-publishes when either input changes, so lowering the flag publishes `null` and raising
it publishes the value again, including a value equal to the one before the outage.

- **The backing field is the block's state; the member is only what is published.** Everything that
  must keep working through an outage reads the field. Nothing assigns the member, so there is no
  second copy to keep in step and no clear method to forget a member in.
- **One flag per group of members read by one request.** Raise it in that request's success
  callback, lower every flag when the block decides the device is not being read. A member whose
  request has not succeeded again stays `null` while the others return.
- **Assign the backing fields, then raise the flag.** The other order publishes the old value first.
- **When the device counts as unavailable is the block's decision.** The Modbus client gives it the
  link verdict, the last contact and a receipt per read; the staleness rule depends on the poll
  cadence, which only the block knows.
- **A getter reads whole fields and nothing else.** A property of a struct held in a field is not
  tracked, so a member derived from one needs a scalar backing field of its own; `DALE031` reports
  the shape. Compute a derived value where its backing field is assigned, not in the getter.
- **A member fed by two requests nests the flags**, for example
  `_voltageWindowLive ? _powerWindowLive ? _signedCurrent : _currentMagnitude : null`.
- **A persisted counter is persisted on the backing member.** Put `[Persistent]` on a private
  property holding the total and publish it through the getter; a getter-only member cannot carry
  `[Persistent]` (`DALE007`). The persistence key follows the member that carries the attribute, so
  moving it from a published member to a private one restarts that counter once.
- **A member whose type cannot hold `null` cannot be masked this way.** Make it nullable, which
  changes its schema, or leave it and say what it means during an outage in the block's status.

What a consumer sees:

- `null` is published as a value, on the property stream and the measuring-point stream alike. An
  absent value on one side is always a change, for the dedup floor and for a deadband
  ([`specs/emission.md`](specs/emission.md), `AC-EMIT-004.3`, `AC-EMIT-006.3`); `MinInterval` can
  hold it for up to one interval, and what is released is the member's latest value.
- The publish at stop and the re-publish after a broker reconnect read the getter too, so they
  publish the masked value and not the one behind it.
- A `null` measuring-point sample is the platform's "not measured": the series shows a gap
  (architecture decision
  [`0023`](../../architecture/decisions/0023-mp-truthfulness-via-clear-on-offline.md)).

**Test the publish, not the read-back.** A test that reads the member passes even when its getter
never re-publishes. Assert the published message or the `PropertyChanged` event for each masked
member, on the flag going down, on it coming up, and on a new value while it is up.
[`MaskedGetterShould`](../Vion.Dale.Sdk.Test/Core/MaskedGetterShould.cs) holds those three for a
scalar field, a whole-struct field and a two-flag member, so a weaver upgrade that stopped tracking
the shape fails the SDK's own tests first.
