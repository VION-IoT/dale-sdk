---
trace: enforced
---

# The host vocabulary: what a host runtime and the SDK exchange

What a host — the process that loads logic blocks and their handlers and connects them to the
outside — sends to them, answers them and may rely on, over the MQTT and lifecycle actor messages the
SDK publishes. Area code `HOST`. Process: [`../spec-process.md`](../spec-process.md).

Two hosts speak this vocabulary. The development host in this repository drives the lifecycle half
and has no MQTT client. The private runtime drives both halves. A rule only the private runtime can
prove carries a `GAP` tail naming the runtime's own criterion, as `dale/AC-…`; that id is the proof's
address, and this repository runs no test of it.

The spine is the order a host meets the vocabulary: bring its handlers up and have them register,
receive, publish, drive the logic blocks, and the one helper a host's own registration uses.

Cited rather than restated: [`contracts.md`](contracts.md) for what a handler does with each message —
the provider face, its registration and publish helpers, the topic and correlation reads, the
installation topic's write-once rule — and that the message types are published at all
(`AC-BIND-016.*`); [`block-lifecycle.md`](block-lifecycle.md) for what a logic block does with each
lifecycle message, the per-message pipeline and the waits; [`devhost-control.md`](devhost-control.md)
for what the development host adds around the sequence.

## Bringing the handlers up

- `AC-HOST-001.1` (Event-driven): WHEN a handler registers through the SDK THE SYSTEM SHALL send its
  registration to the actor `MqttConstants.MqttClientName` names before it answers
  `RegisterMqttHandlerRequest`, so a host that waits for every answer holds every registration before
  it next writes to its client.
- `AC-HOST-001.2` (Event-driven): WHEN a host starts THE SYSTEM SHALL create one actor per `IMqttHandlerActor` type it finds, send each `RegisterMqttHandlerRequest`, and initialize its MQTT client only once every one has answered. GAP: proven by the private runtime, dale/AC-LOAD-006.4, dale/AC-BOOT-005.1
- `AC-HOST-001.3` (Event-driven): WHEN a host holds a handler's `RegisterMqttHandler` THE SYSTEM SHALL subscribe its topic groups, one that names no prefix under the installation topic, hand the handler every received message whose topic contains its routing key, and refuse a registration made after the client is initialized or colliding with an earlier handler's name or routing key. GAP: proven by the private runtime, dale/AC-REG-004.4, dale/AC-REG-005.1, dale/AC-REG-005.2, dale/AC-REG-005.3

`AC-HOST-001.1` is what makes the host's wait meaningful. The answer says the handler is alive, not
that its registration was accepted (`AC-BIND-016.2`), and a registration the client refuses is refused
there, after the answer. What the order guarantees is only that nothing the host sends its client after
the last answer can overtake a registration.

A topic group's prefix is read by the host: none means the installation topic, an empty one means no
prefix, and any other value is used as written — `MqttTopicGroup` documents the three. So a handler
registered with no prefix, which is every provider face, hears only its own installation. A handler
marked as development surface is left out of the host's scan (`AC-BIND-015.3`).

## Receiving

- `AC-HOST-002.1` (Ubiquitous): THE SYSTEM SHALL hand an `MqttMessageReceived` to a service-provider
  handler's subclass without answering it, so the vocabulary offers a host no point after its hand-off
  to the handler at which to acknowledge the message.

A received message is one-way. Its response topic and correlation data ride the actor message's
headers (`AC-BIND-012.6`), so a handler answers the original requester by publishing. The consequence
for delivery: nothing tells a host that a handler has finished with a message, so it can acknowledge a
QoS 1 message (`dale/AC-REG-004.4`) no later than its hand-off, and a message lost between the hand-off
and its handling is not redelivered. Where at-least-once ends is that hand-off.

## Publishing

- `AC-HOST-003.1` (Event-driven): WHEN a handler sends `PublishMqttMessage` THE SYSTEM SHALL publish it without an answer and retry a failed attempt on the host's own count, which a sender leaves at its default. GAP: proven by the private runtime, dale/AC-PROP-001.1, dale/AC-PROP-001.2, dale/AC-PROP-001.3
- `AC-HOST-003.2` (Event-driven): WHEN a handler sends `PublishMqttMessageRequest` THE SYSTEM SHALL publish it once without a retry and answer `PublishMqttMessageResponse` with success once the message reached the connection, which below QoS 1 is not the broker's receipt, and otherwise with failure and the reason. GAP: proven by the private runtime, dale/AC-PROP-001.4

The count is `PublishMqttMessage.AttemptNumber`. It rides the message because the host's retry re-sends
the same message with the count raised, and a sender that sets it shortens or lengthens that retry. The two forms convert into
each other (`AC-BIND-016.1`), so a handler chooses per message whether it wants the retry or the answer.

`RegisterMessageToSendOnConnect` registers a message the host's client publishes on every connection,
or on the next one only where it is not recurring (`dale/AC-REG-004.3`). Whether it stays in the
vocabulary is open in the private runtime's program, so it carries no criterion here.

## Driving the logic blocks

- `AC-HOST-004.1` (Event-driven): WHEN a host brings a configuration up THE SYSTEM SHALL deliver each
  logic block its configuration, its runtime-actor link and its linked-interface map before its
  restore, and its restore before its start.

The order is what a block's hooks are written against: its links are complete and its persisted
values restored by the start hook (`AC-LIFE-012.2`), and the block holds a map that arrives before its
configuration (`AC-LIFE-003.5`). The private runtime restores before it starts, and terminates every
running block before it creates the next configuration's (`dale/AC-CFG-003.1`). The contract link map
goes to the handlers, which replace theirs whole (`AC-BIND-010.5`).

A host waits on the acknowledgements through the actor system's wait. A timed-out wait hands back the
answers that arrived and the actors that did not answer (`AC-LIFE-016.3`), which is what lets a host
name the block that did not start and keep the snapshots of the blocks that did answer.

A block's failures reach a host only through a registered message observer (`AC-LIFE-014.1`,
`AC-LIFE-014.2`). A block whose configuration failed still starts and acknowledges, so a host that
registers no observer reports such a block as started. The development host registers one
(`AC-CTRL-003.1`).

The remote-interface link and the remote installation topics are sent by a host to its own
remote-interface proxy handler, and no SDK type handles them. A contract link is local to the host's installation: the link map's key names the
provider, service and contract read after the host's own installation topic (`AC-BIND-012.1`,
`AC-BIND-012.2`), and it carries no installation of its own. An answer reaches only the sender of the
message being handled when it is sent (`AC-LIFE-014.6`).

## The registration secret

- `AC-HOST-005.1` (Ubiquitous): THE SYSTEM SHALL read a registration secret from its file, trimmed,
  and where the file is missing, empty or whitespace SHALL generate a new one, write it there creating
  its directory, and return it, so every later read of that file returns the same secret.

The secret identifies a host to the registration service across restarts, so a new one is made only
where the file holds none. The installation topic the registration assigns is taken once per process
(`AC-BIND-012.5`).
