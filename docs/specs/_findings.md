# Finding ledger

Defects an area pass found and deliberately did **not** fix: too big for the pass, or reaching past the
area that found them. One line each, newest area last. Triaged in bulk at the retro
([`../retro/`](../retro/)) — an entry that gets scheduled becomes a Jira item and is struck here with its
key; an entry that is fixed is deleted with the PR that fixes it.

Not for: a small area-local defect (the pass fixes it), a stated behavior that merely surprises (the spec
page states it), or a missing test (that is a `GAP` marker on the page).

## `GATE` — config-time structural gating (2026-09-02)

- **A development host reports itself started over a block that failed to initialize.** The
  configuration is sent to the actor and the send returns; the actor sends no acknowledgement, so an
  initialization throw surfaces only as one `[EXCEPTION CAUGHT]` log line while the host reports
  success and the operator sees a block whose properties all read null. The two ways a configuration
  is wrong before it reaches the actor — an unresolvable identifier and an undecodable value — are
  both refused at the topology loader now (`AC-GATE-012.8`); what remains is the general case, which
  is the development host's start-and-health surface. *(GATE pass rows 64/66, narrowed by amendment
  4b — `CTRL`.)*
