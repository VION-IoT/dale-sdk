# Item + dismissal templates (dale-sdk-feedback)

**Budget: ≤250 words of body, four sections, in this order.** The sections are what must be
*present*; a section with nothing verified to say is deleted, not padded. Count words before
showing the draft. Everything the intake verified beyond this stays in chat — the implementer
re-derives detail from the `file:line` chain.

**The item is a problem record, not a design.** Hints are welcome as one line each under
`Directions`; never a recipe, never a nominated winner. `Done when` lists observable outcomes only —
never a named mechanism, never a criterion only one candidate fix could satisfy. The section names
carry no caveats — that `Directions` is non-binding and `Done when` is the contract is a reading
rule for whoever picks the item up (the `/fix` brief states it), not text the item repeats.

## Bug

Summary: `<area>: <symptom>` · Type: **Bug** · Parent: VION-62 · Label: `dale-sdk`

    ## Problem
    <what breaks, where, and who it bites — ≤4 sentences; one scrubbed log line if it carries the signal>

    ## Evidence
    - <dale-sdk file:line — one clause on why it matters>          (≤6 bullets, verified by us)

    ## Origin
    Consumer report (<channel/customer, or legacy DF-nn>, <version reported>) · verified on <tag/sha>, <date>.
    <optional, one line: what the reporter proposed / a hint worth knowing>

    ## Done when
    - [ ] <observable outcome>
    - [ ] <regression coverage>
    - [ ] <surface obligation only if public API moves: XML docs, PublicApi snapshot, analyzer>

## Feature / enhancement

Summary: `<area>: <need>` · Type: **Story** · Parent: VION-62 · Label: `dale-sdk`

    ## Need
    <what cannot be done or costs time today, and why it matters — ≤4 sentences, solution-independent>

    ## Origin
    Consumer report (<channel/customer, or legacy DF-nn>, <version reported>) · verified on <tag/sha>, <date>.

    ## Directions
    - <the reporter's proposal, one line>
    - <our hint / constraint / a fact that narrows the space, one line each>   (≤4 bullets total)

    ## Done when
    - [ ] <observable outcome>
    - [ ] <surface obligation only if public API moves; RFC first if the vocabulary/lifecycle changes>

## Dismissal (recorded as a closed item with resolution, or reply-only)

    <report>: DECLINED / ALREADY SHIPPED / DUPLICATE / NEEDS INFO — <date>
    Reason: <the honest why, citing the rule/RFC/release — or the duplicate's VION-xx>
    Reopen when: <the observable condition, or "n/a">
    Resolution on close: Wird nicht gemacht | Duplikat
