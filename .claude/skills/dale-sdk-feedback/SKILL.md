---
name: dale-sdk-feedback
description: Use when Dale SDK feedback arrives from a consumer or customer — a bug report, feature ask, field log, pasted error/stack/logs, or an RFC-shaped proposal — and it needs to become a curated Jira item in the VION "Dale SDK Feedback" epic, or a recorded dismissal. Triggers: "new SDK feedback", "customer reported", "a consumer hit", "should we do this", "file this", "triage this report".
---

# Dale SDK feedback — curate Jira items from consumer feedback

The dale-sdk feedback backlog is the **"Dale SDK Feedback" epic (VION-62) in the Jira VION
project** (cloudId `ecocoach.atlassian.net`, parent initiative VION-14), written and curated by the
maintainer — consumers send feedback through their channels (mail, chat, docs); they do not file
items. Jira is org-internal: logs and device/site identifiers go in as-is; secrets and credentials
never do.

An intake produces exactly one of: a **created item**, a **recorded dismissal**, or a **parked
item** (named missing input). Feedback describes a need; the item records our verified
understanding of the **problem** — the fix is decided at implementation time.

**This skill authors and answers; it never drives.** Downstream — picking, scoping, briefing the
implementing session, closing worked items, metrics — is `/triage` + `/fix` in the architecture
repo, unchanged. The only Jira state this skill ever sets is create, and close-with-resolution on
born-closed dismissal records. It never transitions an open item, never posts `[/fix] analysis`
comments, never briefs, never implements.

## Flow

1. **Capture.** Keep the raw input verbatim in the session. Note source, date, reporter, SDK
   version reported.
2. **Ask the curator one question** — *what do you know that the report doesn't say?*
   (Hypothesis, prior sightings, customer context, constraints. "Nothing — go look" is fine.
   Skip if the invocation already supplied it.)
3. **Verify before drafting** — cheapest first, evidence as `file:line`. Verify thoroughly;
   **report the findings in chat**, not in the item (step 5 decides what the item keeps):
   - Already fixed? `git log --grep`, `git tag --contains <sha>` for the release. **Do not trust
     issue numbers in commit subjects** — the pre-Jira `DF-nn` numbers in 0.9.1-era commits are
     shifted by one; verify by content.
   - Already tracked? JQL over the whole backlog **including resolved**:
     `project = VION AND labels = dale-sdk AND text ~ "<terms>"` — a closed item's resolution
     (`Wird nicht gemacht` / `Duplikat`) is a recorded position, not noise.
   - By design / documented? RFCs, `docs/*-conventions.md`, stepping and observability boundaries.
   - Read the source path the report names. A report can be wrong in detail and right in
     substance — the item states what WE verified.
   - **Walk the reporter's case top-down, not only the cited mechanism bottom-up.** Start from
     the artifact the report names (contract, block, scenario) and trace the path it actually
     takes — the first gate it hits may not be the one reported (VION-71: the cited null-read
     was real and unreachable; the resolver's classification gate refused the motivating
     contract upstream of it). Run the case when a cheap harness exists; a report in the
     conditional mood ("would have to…") is one nobody has executed. The Origin line records
     the depth reached: `reproduced` (ran it) · `traced` (walked the path end-to-end) ·
     `not reproduced`.
4. **Decide**: create (**Bug** | **Story**) · dismiss (duplicate → link the key;
   already-shipped → name the release; by-design → cite the rationale; out-of-scope;
   needs-info → name exactly what's missing) · park. **Route by scope**: SDK-surface work → the
   SDK-feedback epic; a report that turns out runtime/cloud/dashboard work → author it under the
   ordinary platform epics (VION-16 Bugs / VION-15 Nice to Haves), keep the `dale-sdk` channel
   label; a platform guarantee/architecture ask → hand to `/decision`, no item here.
5. **Draft** from [templates.md](templates.md) — four sections, **≤250 words of body**. Count the
   words before showing the draft; over budget → cut, do not show. What the item keeps: the
   problem, the `file:line` chain, the origin, the observable done-when. What stays in chat: the
   verification narrative, alternatives weighed, mechanisms you would pick. Summary line per the
   title convention below. Fields: parent = VION-62 (or the routed epic), label `dale-sdk`, type per
   the decision.
6. **Gate.** Show the full draft (or dismissal) in chat and wait for an explicit go. **Announce
   every Jira write before making it** (which epic, type, summary, text). Never create, edit,
   transition, or close an item unprompted.
7. **Close the loop.** Hand the curator a short reply for the reporter's channel: what we did
   (VION-xx / dismissal reason), any workaround they can drop and when — consumers cite the key in
   their workaround comments so they can retire them. When a created item is later worked and
   closed by `/fix`, its closing comment is the relay text — consumers don't read Jira.

## Title convention

`<area>: <symptom or need>` — lowercase area from this closed list, then a plain clause of at
most ~80 characters, no em-dash sub-clauses (details belong in the body).

**The clause follows the type.** A **Bug** names the symptom — what goes wrong today. A **Story**
names the outcome to reach, phrased as a goal, because that is what the item is for; a Story
titled with its symptom reads as a defect on every board and in every JQL result, whatever the
type field says. `emission: whole-struct diagnostics default to a permanent 4 Hz emitter` is a
Bug title on a Story; `emission: make publishing a diagnostics struct whole cheap by default` is
the same item stated as the need. The body is unaffected — a Story's `## Need` describes today's
cost either way.

`sdk` · `emission` · `introspection` · `analyzers` · `modbus-tcp` · `modbus-rtu` · `testkit` ·
`devhost` · `scenario` · `topology` · `cli` · `xunit` · `docs` · `runtime` (items routed outside
the SDK epic)

Unknown area → ask the curator; do not invent one.

## Dismissals

A dismissal worth remembering (likely to be re-raised) becomes an item anyway — created and
immediately closed with the matching **resolution** (`Wird nicht gemacht`, or `Duplikat` with the
duplicate's key) and a closing comment carrying the reason and a **reopen condition**, so the
step-3 JQL finds the recorded position next time. Noise-level feedback gets a reply only.
**A closing transition MUST carry its `resolution` explicitly in the transition's fields** — the
API accepts a close without one and the item then vanishes from boards while staying "Unresolved"
in every filter. Re-read the item afterwards and confirm the resolution stuck.

## Without the Atlassian MCP tools

Say so, emit the finished draft, and name the skipped write so the curator can make it by hand.

## Common mistakes

| Mistake | Fix |
|---|---|
| Pasting the verification narrative into the item | Chat carries the narrative; the item carries problem + `file:line` + done-when, ≤250 words |
| Nominating the fix (in prose or via an acceptance criterion only one mechanism satisfies) | Done-when lists observable outcomes; hints, if any, are one line under `Directions` |
| Caveats in section titles ("non-binding — decided at implementation time") | Plain section names; how to read them is the picker's rule (`/fix` brief), not item text |
| Trusting a commit's DF/issue number | Verify by content — numbers have drifted |
| Verifying the reported mechanism, not the reported case | Walk the named artifact top-down through the same path; state who it bites only after that walk |
| Duplicate check on open items only | Include resolved; a `Wird nicht gemacht` IS the recorded position |
| Free-form or sentence-length summary | `<area>: <clause>` from the closed area list |
| Titling a Story with its symptom | A Bug names what breaks; a Story names the outcome to reach |
| Closing without an explicit `resolution` in the transition fields | Jira lets it through; the item goes half-dead — set it, then re-read to confirm |
| Creating the item in the same breath as drafting it | The gate is explicit confirmation, every time |
| A dismissal that lives only in chat | Closed item with resolution, or reply-on-record — never chat-only |
| Secrets/credentials in an item or attachment | Logs and device IDs yes; tokens, passwords, connection strings never |
