---
slug: host-vocabulary
status: proposed           # proposed | in-flight | parked | archived
blocked-on: none           # for parked docs: what's blocking + ref
areas: HOST, BIND, LIFE
author: jonasbertsch
created: 2026-09-25
updated: 2026-09-25
supersedes: none           # path of a superseded change doc, or none
---

# The host vocabulary: a page for what a host runtime and the SDK exchange

> Change doc — one in-flight change. The `Spec delta` below is distilled into the current-truth
> pages under `docs/specs/` in the implementation PR, then this doc is archived
> (`pwsh scripts/spec-change.ps1 archive <slug>`). Process: `docs/spec-process.md`. Never put
> change narrative inline in a spec page — it lives here.

## At a glance

### Summary

The MQTT and lifecycle actor messages a host runtime and the SDK exchange are published as types
(`AC-BIND-016.1`, `.3`), but no page states what a host does with them: `contracts.md` says so below
`AC-BIND-016.3`. This change adds the `HOST` page, minted from the private runtime's consumer sweeps
(dale's SDD program, task `T-014`, its D10). A rule only a host can prove carries a `GAP` tail naming
the runtime's criterion. Lane 2, two-phase: STOP 1's answer is amendment 1
(`C:\_gh\dale\.claude\briefs\amend-sdd-host-host-1.md`), recorded under each question below. It adds
one public type: a timed-out acknowledgement wait now hands back what answered.

### Spec implications

- **New page** `docs/specs/host-vocabulary.md`, area `HOST`, `trace: enforced`: five umbrellas, eight
  leaves (§ Spec delta). Four are proven here; four are the host's alone and carry a `GAP` tail.
- **`contracts.md`**: the prose below `AC-BIND-016.3` stops saying the vocabulary is specified nowhere
  and points at `HOST`. No criterion changes.
- **`block-lifecycle.md`**: the remote-interface prose (`:66-68`) points at `HOST`, and
  `AC-LIFE-016.3` is `MODIFIED` to hand back what answered.
- **`docs/spec-process.md`** § The corpus: one roster row. § IDs & EARS gains one sentence on a
  qualified id.
- **Doc comments** on four vocabulary members, fixes with no criterion (e).

### Decisions

Decide-and-document points the brief left to this session. Each is open to the operator's override at
STOP 1 but is not asked.

- `D1` — **The file is `docs/specs/host-vocabulary.md`**, titled for the vocabulary rather than for
  one host, because two hosts speak it: the development host in this repository and the private
  runtime outside it. The code is `HOST` (program D2, ratified).
- `D2` — **The spine is the order a host meets the vocabulary**: bring the handlers up and have them
  register (`001`), receive (`002`), publish (`003`), drive the logic blocks (`004`), then the one
  helper a host's own registration uses (`005`). The ids follow the spine.
- `D3` — **Roster row: group *Wire contracts*, tier `A`**, beside `PLUG`. Like the plugin-loading ABI,
  the vocabulary is an interface between the SDK and a host rather than an author's surface or a
  runtime semantic of the block. Tier A because every leaf is EARS and traced, the host's leaves as
  `GAP`.
- `D4` — **Where `HOST` ends.** `BIND` keeps what a *handler* does with the vocabulary — the provider
  face, its registration, its publish helper, the topic and correlation reads, the installation
  topic's write-once rule (`AC-BIND-010.*`, `011.*`, `012.*`) — and that the types are published
  (`AC-BIND-016.*`). `LIFE` keeps what a *block* does with each lifecycle message, the pipeline and the
  waits (`AC-LIFE-001.*` to `016.*`). `HOST` states what the *host* side of each exchange is: what it
  sends, in what order, what it answers and what it may rely on. Every rule another page owns is cited
  there, never re-minted.
- `D5` — **Which rows become criteria**: one criterion per rule a host depends on (§ Full design,
  *The rows*). A message shape is stated once, by its type, never field by field. A log line mints
  nothing. `RegistrationSecret` is in: it is `[PublicApi]`, in the manifest, used by the runtime's
  registration and stated on no page (`grep -l RegistrationSecret docs/specs/*.md` is empty).
- `D6` — **A rule the development host proves is traced, not `GAP`**: `AC-HOST-004.1` is carried by a
  `Vion.Dale.DevHost.Test` test, and the runtime's criterion is named in the prose below it rather
  than in a tail.

### Reviewer's questions

Pre-classified by the brief: (a) to (e) are propose-and-wait, answered at STOP 1. Each has options
and a recommendation.

1. **(a) The anchor.** `spec-process.md` § The corpus defines an area as "one contract with its own
   anchor artifact" (`:20-21`). The vocabulary's types are not `[PublicApi]` (only
   `Vion.Dale.Sdk.Mqtt.RegistrationSecret` of this scope is in `docs/snapshots/publicapi-manifest.json`,
   `grep -n "Sdk.Mqtt\|Sdk.Messages" docs/snapshots/publicapi-manifest.json`), and `Vion.Dale.Sdk`
   declares no published namespace for them (`Vion.Dale.Sdk/PublicApiConfig.cs`).
   - *Option 1 — the declarations are the anchor*: `Vion.Dale.Sdk/Mqtt/ActorMessages.cs`,
     `Vion.Dale.Sdk/Messages/ActorMessages.cs` and `IMqttHandlerActor`, which `AC-BIND-016.1` and `.3`
     already pin as published and constructible. That is the anchor `BIND`, `LIFE` and `GATE` have
     today: their roster rows name no artifact beyond their types. The host-proven half is anchored in
     the runtime's suite, which is what D10's tail names.
   - *Option 2 — make the manifest the anchor*: mark the vocabulary `[PublicApi]` and declare the two
     namespaces, so `DALE014` arms over every public type in them and the manifest moves. A surface
     change (`sdk-surface-conventions.md` § 8): the types become a shipping doc surface, and the
     manifest is type-level, so it pins names and not shapes.
   - *Option 3 — no anchor*: `HOST` is not an area but a cited-contract page outside the roster, like
     `_invariants.md`, and carries no roster row.
   - **Recommendation: option 1.** It is true today, costs nothing, and matches the three tier-A areas
     whose anchor is their types. Option 2 is a surface change nobody needs for this page; if the
     ratchet should reach the vocabulary, that is decision 0145's classification, not `T-014`'s.
   - OUTCOME: accepted, option 1 — the declarations are the anchor; no `[PublicApi]` change (operator, amendment 1, answer 1).

2. **(b) A private repository's ids on a public page.** dale-sdk is source-available; its pages call
   the host "the private runtime" (`contracts.md` below `AC-BIND-016.3`; `block-lifecycle.md:66-68`).
   D10's tail puts `dale/AC-…` on such a page.
   - *Option 1 — the tail as D10 writes it*: `GAP: proven by the private runtime, dale/AC-PROP-001.4`.
     The id is an opaque label: it says which criterion to read and nothing of its content. The
     runtime's name is already public — the package is `Vion.Dale.*`, and `block-lifecycle.md`
     already names a runtime type and file (`ActorVitalsMeterHost`, `Dale/Diagnostics/ActorVitalsExport.cs`)
     and links the private `architecture` repository's decisions.
   - *Option 2 — no id on the page*: `GAP: proven by the private runtime`, with the id mapping kept in
     dale's pages, which cite `dale-sdk/AC-HOST-…` back. D10's "naming dale's criterion" is then met
     only from dale's side, and a reader of this page cannot find the proof.
   - *Option 3 — a prose table* of `HOST` leaf → runtime criterion below the section, with bare tails.
     Two places to keep in step.
   - **Recommendation: option 1.** The page already says the runtime exists and is private; naming
     which of its criteria proves a rule tells an external reader where the proof is without exposing
     it. It keeps D10's shape exactly, so the page needs no rewrite when dale-sdk re-points at the
     plugin's scripts.
   - OUTCOME: accepted, option 1 — `GAP: proven by the private runtime, dale/AC-…` (operator, amendment 1, answer 2).

3. **(c) The tooling.** Probed against this repository's scripts at `117cc963`, with a scratch corpus
   (`-RepoRoot` a temporary repository holding one traced page and one citing test):

   | Probe | Tail or line | `spec-trace` |
   | --- | --- | --- |
   | P1 | `GAP: proven by dale/AC-PROP-001.4` | **FAIL**: holes `AC-PROP-001.1` to `.3`, no `REMOVED` line |
   | P2 | the tail names all four leaves `001.1` to `001.4` | OK, and the four foreign ids count as this repo's GAP backlog (5 GAP ids for 1 page GAP) |
   | P3 | prose line (no `GAP`) citing `` `dale/AC-PROP-001.1` `` | **FAIL**: `AC-PROP-001.1` declared, no test reference |
   | P4 | `GAP: proven by the private runtime (dale PROP-001.4)` | OK, 1 GAP id |
   | P5 | `GAP: proven by dale/AC-HOST-001.3` (a foreign id sharing a local umbrella) | **FAIL**: local hole `AC-HOST-001.2` |
   | P6/P7 | `dale/AC-PROP-001`, `dale/AC-PROP-001.*` | OK, `AC-PROP-001` counted as a GAP id |

   `spec-lint` passed every probe: it reads ids only on declaration lines (`scripts/spec-lint.ps1:40`,
   `:43`). `spec-change.ps1 archive` sets a `GAP:` tail aside when it compares (`:96`), so it is
   unaffected. The cause is one regex: `scripts/spec-trace.ps1:39`'s `$idRx` starts at `\b`, and `/`
   is a boundary. The plugin reads the same token as a citation through a lookbehind
   (`architecture/plugins/vion-sdd/scripts/spec-common.psm1:26-32`, `BareIdRx`).
   - *Option 1 — port the plugin's bare-id rule into `spec-trace.ps1`*: `$idRx` gains the plugin's
     lookbehind, so `repo/AC-…` is neither declared nor GAP'd, with a self-test in
     `spec-trace.tests.ps1` for P1, P3 and P5, and one sentence in `spec-process.md` § IDs & EARS. One
     line of grammar, taken verbatim from the plugin, so re-pointing at the plugin later (D9's
     Follow-up) changes nothing on the page. It is not the re-point: `owns:`, `cites:` and the
     corpus file stay the Follow-up's.
   - *Option 2 — tails with no id-shaped token* (P4's shape): passes today, but departs from D2's
     citation form, and the page is rewritten at the re-point.
   - *Option 3 — accept the gates*: cite only whole families (`dale/AC-PROP-001.*`, P7). It passes,
     miscounts the GAP backlog, and fails the day a tail names one leaf, or the runtime mints a code
     this repository also uses (P5).
   - **Recommendation: option 1.** Every probe that failed is a real page this change would write
     (P1 is `AC-HOST-003.2`'s tail as D10 drafts it). The fix is the plugin's own rule, one line.
   - OUTCOME: accepted, option 1 — the bare-id rule ported into `spec-trace.ps1`, self-tests for P1, P3 and P5 and for an id after `.` or `-`, the totals before and after the port pasted in § Drift checkpoints; no `owns:`, `cites:` or corpus file (operator, amendment 1, answer 3).

4. **(d) Each routed Ledger row** (dale's program § Ledger, re-derived below in § Full design, *The
   routed rows*). For each: today's behaviour on the page, or an SDK change.
   - **`T-005` row 17 — a block whose configuration failed still starts, and the runtime registers no
     message observer.** Today: `block-lifecycle.md:160-163` states the start; `AC-LIFE-014.1` and
     `014.2` state that a handler's failure reaches a host only through a registered observer, which
     is how the development host names a failed block (`AC-CTRL-003.1`). *Option A*: the page states
     that in prose citing `AC-LIFE-014.1`, mints nothing, and the runtime registering an observer goes
     back to `T-013`. *Option B*: an SDK change so a block whose configuration failed refuses its start
     — a `LIFE` behaviour change that turns every host's start into a timeout for a block that is
     running on part of its configuration. **Recommendation: A.** The seam exists and one host
     already uses it.
   - **`T-005` row 20 and row 25 = `T-007` row 41 — a timed-out wait names no block and hands back
     nothing, so a start timeout cannot name the silent block and a snapshot timeout loses every
     block's snapshot.** Today: `AC-LIFE-016.3` fails the wait "naming how many had not", as a plain
     `TimeoutException` (`Vion.Dale.ProtoActor/ActorSystem.cs:167`, `:365`), and drops the answers it
     had collected (`:126`, `responses`). *Option A*: state today's, and both rows stay the runtime's
     problem. *Option B*: the wait's timeout throws a `TimeoutException` subclass carrying the answers
     received and the actors that did not answer; `AC-LIFE-016.3` is `MODIFIED`. Both hosts catch
     `TimeoutException` today (`Vion.Dale.DevHost/DevLogicSystemInitializer.cs:203`, `:208`, `:352`,
     `:402`; the runtime's `LogicSystemConfigurationInitializer.cs:274`, `:316`, `:345`, `:406`,
     `:442`, `:1007`), so the subclass breaks neither. It is one new public type and no member
     removed. **Recommendation: B, landed in this task**, because it is local to one method, backward
     compatible, and three Ledger rows wait on it. The runtime consumes it in `T-013` once a dale-sdk
     release carries it. If B lands here, the branch prefix `chore/` misnames a change that adds
     surface (`feat/`); the branch is not renamed unasked.
   - **`T-008` QoS 1 row — a QoS 1 message is acknowledged once it reaches the client actor's
     mailbox.** Today: `MqttMessageReceived` is one-way; no SDK handler answers it
     (`Vion.Dale.Sdk/Abstractions/ServiceProviderHandlerBase.cs:116-118`; no SDK subclass calls
     `RespondToSender`), and the vocabulary has no
     "handled" answer. *Option A*: the page states it — `AC-HOST-002.1`, proven here — so where
     at-least-once ends is written down: at the host's hand-off, never later. *Option B*: add an
     acknowledged receive form, so a host could acknowledge after a handler has handled the message —
     a new message pair every handler must answer, cross-repo, and a handler's actor would then hold
     the broker's acknowledgement for as long as it handles. **Recommendation: A**; B only if the
     runtime's `OPS` or `CFG` requests need it, which is `T-013`'s question to ask.
   - **`T-009` row 100 — a contract mapping's installation topics are read by nothing, and
     `ServiceProviderContractId` carries no installation.** Today: a provider's identity is read from
     the three segments after the host's own installation topic (`AC-BIND-012.1`, `.2`), and a
     provider face subscribes under that topic (`ServiceProviderHandlerBase.cs:107`, a group with no
     prefix). So a contract link is installation-local. *Option A*: the page states that in prose
     citing `AC-BIND-012.1`, mints nothing, and whether a mapping may cross installations goes back to
     `T-013` as a product question. *Option B*: add an installation to `ServiceProviderContractId` — a
     change to the link map's key, `AC-BIND-012.*`, every provider face's subscriptions and the
     broker's access rules. **Recommendation: A.** B answers a question nobody has asked yet; if the
     answer is yes, it is its own lane-2 change here.
   - **`AttemptNumber` — public and settable by a sender, read only by the runtime's retry.** A
     sender that sets it shortens or lengthens the host's retries (the runtime's
     `MqttClient.cs:757-758` counts from it). No sender sets it: `grep -rn AttemptNumber` over dale,
     dale-sdk and logic-block-libraries finds only the declaration, the runtime's client and its test.
     *Option A*: document it as the host's counter that a sender leaves at its default, and state it
     in `AC-HOST-003.1`. *Option B*: delete it (delete-don't-deprecate), so the runtime carries its
     count on its own retry message. A breaking change to a positional record that the runtime's next
     SDK bump has to absorb in the same PR. **Recommendation: A.** The member has a consumer now; only
     its owner is unusual, and the page and its doc comment say whose it is.
   - OUTCOME: accepted — the wait: option B, landed in this task (`MODIFIED AC-LIFE-016.3`); row 17, the QoS 1 row and row 100: option A each, their design questions back to the private runtime's `T-013`; `AttemptNumber`: option A (operator, amendment 1, answer 4).

5. **(e) The two documentation disagreements**, both fixes with no criterion (§ IDs & EARS):
   - `PublishMqttMessageRequest`'s summary says it ensures "the message is published"
     (`Vion.Dale.Sdk/Mqtt/ActorMessages.cs:114-120`), and `IMqttHandlerActor`'s remarks say the pair
     confirms "that publishing succeeded or failed" (`IMqttHandlerActor.cs:20-23`). The runtime answers
     success once a QoS 0 send completes (`dale/AC-PROP-001.4`). The fix: the answer says the message
     reached the host's connection, which is the broker's receipt only where the host publishes at QoS
     1 or above. `AC-HOST-003.2` states the rule.
   - `AttemptNumber` has no doc comment (`:97`). The fix is (d)'s option A.
   - Found on the way, same kind: `RegisterMqttHandlerResponse`'s remarks say it answers
     "`RegisterMqttHandlerResponse`" (`:23-24`), where they mean the request; and
     `IActorContext.RespondToSender` has no doc comment. The scope point the runtime's `PROP` REPORT
     raised — an answer reaches only the sender of the message being handled when it is sent
     (`Vion.Dale.ProtoActor/ActorContext.cs:99-106`) — is `AC-LIFE-014.6`'s, which `HOST` cites; the
     doc comment says it where a caller reads it. `HOST` needs no criterion of its own for it: no host
     rule depends on more than `AC-LIFE-014.6` states.
   - **Recommendation: fix all four doc comments in this change.**
   - OUTCOME: accepted — all four doc comments fixed (operator, amendment 1, answer 5).

---

## Full design

### What the page is for

The vocabulary has two sides. The SDK's side — what a block and a handler do with each message — is
`BIND`'s and `LIFE`'s and is already traced. The host's side is on no page: `grep -lE
'RegisterMqttHandler|PublishMqttMessage|IMqttHandlerActor' docs/specs/*.md` finds only
`contracts.md`, and there only in `AC-BIND-016.1`'s list of published types. A block or handler author
needs the host's side to know what their messages turn into: that a publish is retried by the host and
a publish request is not, that a success answer is not the broker's receipt, that a received message is
delivered once and nothing acknowledges its handling, that a start waits on an acknowledgement the
block must send.

Two hosts speak the vocabulary. The development host in this repository drives the lifecycle half and
none of the MQTT half (`grep -rln "RegisterMqttHandler\|PublishMqttMessage" Vion.Dale.DevHost*
--include=*.cs` is empty). The private runtime drives both. So the lifecycle rules can be proven here
and the MQTT rules only there — which is D10's split, and why most of the MQTT leaves are `GAP`.

### The rows

Where each input landed. Evidence is `file:line` at `117cc963` for this repository and `e6c4a3a` for
the runtime.

| Input | Rule a host depends on | Lands as | Evidence |
| --- | --- | --- | --- |
| handler registration, SDK side | the registration is sent to the client before the answer | `AC-HOST-001.1`, proven here | `Mqtt/MqttHandlerActorExtensions.cs:62-63`; ordered log in `Vion.Dale.Sdk.Test/TestHelpers/LifecycleHarness.cs:72` |
| handler registration, host side | one actor per handler type; every answer before the client starts | `AC-HOST-001.2`, `GAP` | runtime `plugin-loading.md` `AC-LOAD-006.4`; `startup.md` `AC-BOOT-005.1` |
| subscription and routing | a group with no prefix is under the installation topic; a topic containing the routing key reaches the handler; late or colliding registrations refused | `AC-HOST-001.3`, `GAP` | runtime `registration.md` `AC-REG-004.4`, `005.1` to `005.3`; `Mqtt/ActorMessages.cs:68-73` |
| receive is one-way (`T-008` QoS 1 row) | nothing answers a received message | `AC-HOST-002.1`, proven here | `Abstractions/ServiceProviderHandlerBase.cs:116-118` |
| `PublishMqttMessage` | published without an answer, retried by the host on its own count | `AC-HOST-003.1`, `GAP` | runtime `service-streams.md` `AC-PROP-001.1` to `001.3`; `MqttClient.cs:757-758` |
| `PublishMqttMessageRequest` | published once, answered; success is not the broker's receipt below QoS 1 | `AC-HOST-003.2`, `GAP` | runtime `AC-PROP-001.4` |
| `RegisterMessageToSendOnConnect` | sent on every connection, a non-recurring one once | prose citing `dale/AC-REG-004.3`, no criterion (amendment 1; § Drift checkpoints) | runtime `AC-REG-004.3`; `Mqtt/ActorMessages.cs:55-59` |
| lifecycle drive (`T-009` row 73) | configuration and link, link maps, restore, start, each start and restore acknowledged | `AC-HOST-004.1`, proven here by the development host | `Vion.Dale.DevHost/DevLogicSystemInitializer.cs`; runtime `logic-configuration.md` `AC-CFG-003.1`, `003.4` named in prose (D6) |
| failed configuration invisible (`T-005` row 17) | a host sees a handler failure only through an observer | prose, citing `AC-LIFE-014.1`, `014.2` | `block-lifecycle.md:160-163`, `:381-384` |
| wait hands back nothing on timeout (`T-005` rows 20, 25; `T-007` row 41) | per (d) | `MODIFIED AC-LIFE-016.3` ((d) B, accepted) | `Vion.Dale.ProtoActor/ActorSystem.cs:126`, `:167`, `:365` |
| installation topics unread (`T-009` row 100) | a contract link is installation-local | prose, citing `AC-BIND-012.1`, `.2` | `Utils/ServiceProviderContractId.cs:10`; `ServiceProviderHandlerBase.cs:39`, `:107` |
| `RegistrationSecret` | read trimmed; missing, empty or whitespace generates and persists one | `AC-HOST-005.1`, proven here (no test today) | `Mqtt/RegistrationSecret.cs:29-48` |
| installation topic write-once | — | cited, `AC-BIND-012.5` | |
| headers carry response topic and correlation | — | cited, `AC-BIND-012.6` | |
| `RespondToSender` scope | — | cited, `AC-LIFE-014.6`; doc comment (e) | `Vion.Dale.ProtoActor/ActorContext.cs:99-106` |
| remote-interface link and installation topics | sent by a host to its own proxy handler, handled by no SDK type | prose; `block-lifecycle.md:66-68` points here | `Messages/ActorMessages.cs:64-75`; runtime `AC-CFG-003.4` |
| host services a library resolves (runtime boot-pass row 30) | — | out of scope: `AC-LIFE-020.*` states the SDK's registrations, and which extra services a host adds is the host's | `ServiceCollectionExtensions.cs` |
| log lines, the client's actor name as a string | — | nothing: a log mints nothing; the name is `MqttConstants.MqttClientName`, stated by type in `AC-HOST-001.1` | |

### The routed rows

Re-derived from the program (`C:\_gh\dale\docs\changes\2026-09-18-sdd-migration.md` § Ledger, at
`e6c4a3a`), by task and finding: `T-005` row 17 (`:469-472`), row 20 (`:473-476`), row 25 with
`T-007` row 41 (`:477-481`, `:530-535`), `T-008`'s QoS 1 row (`:543-547`), `T-009` row 100
(`:567-572`). Dispositions: question 4.

### The wait change, if (d) B is accepted

`SendAndWaitForAcknowledgementAsync` keeps its signature and its success path. On a timeout it throws
`AcknowledgementTimeoutException<TAcknowledgementMessage> : TimeoutException` with the answers
received, keyed by the caller's references as on success, and the references that did not answer.
Its message is today's. The type lives beside `IActorSystem` in `Vion.Dale.Sdk.Abstractions`, so both
hosts reach it without referencing `Vion.Dale.ProtoActor`. The termination wait
(`ActorSystem.cs:365`) has no answers to hand back and is unchanged. Proof: a wait over two actors, one
of which never answers, throws the subclass with one answer and one silent reference; the mutation is
today's plain `TimeoutException`.

### The GAP tail

With (b) option 1 and (c) option 1 a host-proven leaf reads:

```
- `AC-HOST-003.2` (Event-driven): WHEN a handler sends `PublishMqttMessageRequest` THE SYSTEM SHALL … GAP: proven by the private runtime, dale/AC-PROP-001.4
```

A leaf proven by several runtime criteria names each, comma-separated. The prose may cite a runtime
criterion the same way, which (c) option 1 is what makes safe.

---

## Drift checkpoints

> One line per divergence discovered during implementation:
> `YYYY-MM-DD: <what changed and why>`. Never inline in a spec page. A checkpoint that fixes a
> CLASS bug states the sibling sweep (done / N/A / handed off).

- 2026-09-25: the brief placed `RespondToSender`'s doc gap under the program's input 2; it has no
  doc comment at all (`Vion.Dale.Sdk/Abstractions/IActorContext.cs:16`), so it is a missing comment,
  not a disagreeing one. Handled with (e).
- 2026-09-25: found beside (e): `RegisterMqttHandlerResponse`'s remarks name the response where they
  mean the request (`Vion.Dale.Sdk/Mqtt/ActorMessages.cs:23-24`).
- 2026-09-25: `AC-HOST-003.3` (`RegisterMessageToSendOnConnect` sent on connect) is dropped from the
  delta, by the coordinator (amendment 1, *do not mint `AC-HOST-003.3`*): the private runtime's program
  leaves open whether the message stays in the vocabulary, and minting it here would decide that. The
  page states it in prose citing `dale/AC-REG-004.3`. It was never published and was its umbrella's
  highest leaf, so it opens no hole and needs no `REMOVED` line.
- 2026-09-25: two evidence corrections from amendment 1's reader: the wait's collected answers are at
  `Vion.Dale.ProtoActor/ActorSystem.cs:126`, not `:125`; and question 4's list of the development host's
  `catch (TimeoutException)` sites missed `Vion.Dale.DevHost/DevLogicSystemInitializer.cs:235`, which the
  subclass leaves catching as before.

---

## Spec delta (to distill)

> The machine-readable change, one line per id. Grammar:
> `<OP> <ID> -> <target> : <payload>`
>
> - `OP` ∈ `ADDED` | `MODIFIED` | `REMOVED`
> - `target` is **repo-root-relative** (normally `docs/specs/<page>.md`)
> - `payload`: for `ADDED`/`MODIFIED` the EARS text; for `REMOVED` the reason
>
> On the implementation PR each line is applied into the named target; `spec-change.ps1 archive`
> refuses until every line is applied. The `ID` must be an exact token greppable in the target
> after distill (backticks stripped) — a real `AC-`/`SYS-` id, never an ad-hoc label.

As STOP 1 answered it (amendment 1): the tails as (b) and (c) option 1 write them, and
`AC-LIFE-016.3` for (d) B.

- ADDED AC-HOST-001.1 -> docs/specs/host-vocabulary.md : WHEN a handler registers through the SDK THE SYSTEM SHALL send its registration to the actor `MqttConstants.MqttClientName` names before it answers `RegisterMqttHandlerRequest`, so a host that waits for every answer holds every registration before it next writes to its client.
- ADDED AC-HOST-001.2 -> docs/specs/host-vocabulary.md : WHEN a host starts THE SYSTEM SHALL create one actor per `IMqttHandlerActor` type it finds, send each `RegisterMqttHandlerRequest`, and initialize its MQTT client only once every one has answered. GAP: proven by the private runtime, dale/AC-LOAD-006.4, dale/AC-BOOT-005.1
- ADDED AC-HOST-001.3 -> docs/specs/host-vocabulary.md : WHEN a host holds a handler's `RegisterMqttHandler` THE SYSTEM SHALL subscribe its topic groups, one that names no prefix under the installation topic, hand the handler every received message whose topic contains its routing key, and refuse a registration made after the client is initialized or colliding with an earlier handler's name or routing key. GAP: proven by the private runtime, dale/AC-REG-004.4, dale/AC-REG-005.1, dale/AC-REG-005.2, dale/AC-REG-005.3
- ADDED AC-HOST-002.1 -> docs/specs/host-vocabulary.md : THE SYSTEM SHALL hand an `MqttMessageReceived` to a service-provider handler's subclass without answering it, so the vocabulary offers a host no point after its hand-off to the handler at which to acknowledge the message.
- ADDED AC-HOST-003.1 -> docs/specs/host-vocabulary.md : WHEN a handler sends `PublishMqttMessage` THE SYSTEM SHALL publish it without an answer and retry a failed attempt on the host's own count, which a sender leaves at its default. GAP: proven by the private runtime, dale/AC-PROP-001.1, dale/AC-PROP-001.2, dale/AC-PROP-001.3
- ADDED AC-HOST-003.2 -> docs/specs/host-vocabulary.md : WHEN a handler sends `PublishMqttMessageRequest` THE SYSTEM SHALL publish it once without a retry and answer `PublishMqttMessageResponse` with success once the message reached the connection, which below QoS 1 is not the broker's receipt, and otherwise with failure and the reason. GAP: proven by the private runtime, dale/AC-PROP-001.4
- ADDED AC-HOST-004.1 -> docs/specs/host-vocabulary.md : WHEN a host brings a configuration up THE SYSTEM SHALL send each logic block its configuration and its runtime-actor link, then its link maps, then its restore, then its start, and SHALL wait on the restore's and the start's acknowledgements.
- ADDED AC-HOST-005.1 -> docs/specs/host-vocabulary.md : THE SYSTEM SHALL read a registration secret from its file, trimmed, and where the file is missing, empty or whitespace SHALL generate a new one, write it there creating its directory, and return it, so every later read of that file returns the same secret.
- MODIFIED AC-LIFE-016.3 -> docs/specs/block-lifecycle.md : WHEN a wait's timeout elapses before every actor has answered THE SYSTEM SHALL fail it naming how many had not, and SHALL hand the caller every answer it received and every actor that did not answer.

---

## Tasks

> One-commit tasks, each tagged with ≥1 AC id. Ephemeral — they live and die with this change doc.
> Plain list, no checkboxes — the PR's per-task commits are the completion record.

After STOP 1, adjusted to its answer:

- `T-001` (`AC-HOST-*`, tooling): `spec-trace.ps1` reads a qualified id as a citation, with its
  self-tests, and `spec-process.md` § IDs & EARS says so.
- `T-002` (`AC-HOST-001.1`, `002.1`, `005.1`): the three tests proven here, each with its mutation.
- `T-003` (`AC-HOST-004.1`): the development host's test cites the leaf.
- `T-004` (`AC-LIFE-016.3`): the wait's timeout hands back what answered, red first.
- `T-005` (no id): the four doc comments of (e).
- `T-006` (`AC-HOST-*`): the page, the roster row, and the pointers in `contracts.md` and
  `block-lifecycle.md`; distill; archive.

## Relay notes for the PR body

- _(none yet — written as each consumer-visible change lands)_
