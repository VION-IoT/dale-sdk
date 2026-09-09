# SDD pass data — the transcript harvest (`T-011`)

**Harvested:** 2026-09-09 · **Window:** 2026-09-01 13:55 → 2026-09-09 13:47 · **Method:** direct
transcript parse (JSONL), not sampling — every record in every session file under
`~/.claude/projects/C---gh-dale-sdk*` and `~/.claude/projects/C---gh-architecture*` was read.

This replaces the heuristic table in
[`../changes/2026-09-07-sdd-closeout.md`](../changes/2026-09-07-sdd-closeout.md) § *What the passes
measured, for the lanes*, which was "a string count over the transcripts, not a harvest." `T-018`
reads the table below.

## What was scanned, and what counts as a row

Both globs matched more project directories than the task line named — enumerated directly rather
than assumed:

`C---gh-architecture`, `C---gh-architecture-dispatch`, `C---gh-dale-sdk`,
`C---gh-dale-sdk--claude-worktrees-distracted-heisenberg-6ca362`,
`C---gh-dale-sdk--claude-worktrees-intelligent-bouman-3bfb96`, and the nine per-area fixup
directories (`C---gh-dale-sdk-{anlz,bind,cli,ctrl,http,io,life,modb,tkit}-fixup`) plus
`C---gh-dale-sdk-unify` — fourteen directories beyond the two named, fifteen scanned in total (two
of the fifteen, both `--claude-worktrees-` dirs, held no SDD-window sessions).

**548 session files** exist across all fifteen directories. Restricting to a first-message
timestamp inside 2026-09-01T00:00–2026-09-10T00:00 (the SDD window: pass 0 to the end of phase 1's
closeout day) leaves **298**. Of those, **45** are the SDD effort's own dispatched sessions — a
pass, a fixup, a coordinator round, a phase-1 closeout task worker, or the one infra session that
landed the `vion-dispatch` plugin adoption PR. The other 253 are not enumerated as separate rows,
for two distinct reasons, both stated here because a diff cannot show either:

- **~230 are subagent transcripts**, not sessions a human drove. Every `Task`-tool dispatch (a
  fresh-context `Explore`, `code-reviewer`, `security-review`, etc.) that a pass, fixup or worker
  session made gets its own top-level `.jsonl` in the same project directory, on the same git
  branch, with no `custom-title`/`agent-name` record. Counting these as independent "sessions"
  would double the row count against work already reflected in its parent's own message and
  tool-use counts, and the doc's own existing table already treats the dispatched session as the
  atomic unit. A session carrying a `custom-title` or `agent-name` record is a dispatched session;
  one carrying neither is a subagent call. (Three of these, `msg-test-*` / `22222222…` /
  `33333333…` / `launcher-test`, are synthetic fixtures with placeholder hex-repeated session ids —
  self-tests of the launcher and messaging plumbing, not subagent calls, but excluded for the same
  reason: not part of the SDD migration's work.)
- **~23 are unrelated concurrent work** by the same operator in the same repos during the same nine
  days — Jira items (`VION-1nn`), a weekly catchup, a timesheet reconstruction, dashboard and
  gateway specs, and the `vion-dispatch` plugin's own construction and self-tests
  (`session-orchestration: *`, `spike*`, `accept: *` in `C---gh-architecture` and
  `C---gh-architecture-dispatch`). These titles are unambiguous and none cite an `sdd-closeout`
  or `spec-pass` branch; one exception is called out below.

**One exception kept in:** `session-orchestration: dale-sdk` (`79663cbe`) is titled like the
plugin's own dev sessions above, but its branch is `docs/vion-dispatch-pointers` and it is the
session the handoff's Round 3 history names as landing PR #202 — "dale-sdk's dispatch mechanics
pointed at the `vion-dispatch` plugin." It is retro-relevant (it is why phase 2 dispatches
differently than phase 1) even though it is not a pass/fixup/coordinator/worker session, so it gets
its own `infra` role rather than being folded into a category it doesn't fit.

**This session (`T-011`, `fff8ed58…`) is excluded from its own table.** It is still open at harvest
time; any row for it would be wrong before the commit that carries it.

## The table

One row per dispatched SDD session, in start order. "Wall" is last-timestamp minus first-timestamp
on that session's own transcript — for the four dispatch-round coordinator sessions this includes
long idle stretches (the operator was away; `a8ddb783` and `57f313ff` both show ~4 real days because
the coordinator sat idle between a check and the operator's next word, not because either worked
that long). "Messages" excludes the injected compaction-summary turn itself and harness meta
records; "Tool uses" counts `tool_use` content blocks in assistant turns; "Compactions" counts
`isCompactSummary` records.

| Role | Pass / task | Session | Start (UTC) | End (UTC) | Wall | Messages | Tool uses | Compactions | Transcript |
|---|---|---|---|---|---:|---:|---:|---:|---:|
| coordinator | sdd passes 0–3 | `a7da4594` | 2026-09-01 13:55 | 2026-09-03 07:52 | 41.96 h | 1700 | 528 | 1 | 8.94 MB |
| pass | PLUG | `de9c32fa` | 2026-09-01 17:36 | 2026-09-02 05:39 | 12.05 h | 383 | 137 | 0 | 2.04 MB |
| pass | EMIT | `68ad382f` | 2026-09-02 06:44 | 2026-09-02 11:02 | 4.30 h | 983 | 373 | 0 | 4.65 MB |
| pass | GATE | `f04b0920` | 2026-09-02 12:34 | 2026-09-02 16:54 | 4.34 h | 1528 | 578 | 0 | 6.07 MB |
| fixup | GATE | `f06d93f4` | 2026-09-02 16:48 | 2026-09-02 18:08 | 1.33 h | 632 | 244 | 0 | 4.40 MB |
| coordinator *(aborted)* | sdd pass 4, 1st false start | `aff4c386` | 2026-09-02 18:10 | 2026-09-02 18:20 | 0.18 h | 2 | 0 | 0 | 0.05 MB |
| coordinator *(aborted)* | sdd pass 4, 2nd false start | `5ca5e6bc` | 2026-09-02 18:24 | 2026-09-02 18:24 | 0.00 h | 2 | 0 | 0 | 0.05 MB |
| coordinator | sdd pass 4 (INTRO + SCEN dispatch) | `a8ddb783` | 2026-09-02 18:25 | 2026-09-07 06:49 | 108.39 h | 1318 | 438 | 1 | 7.72 MB |
| pass | INTRO | `ef095862` | 2026-09-02 19:22 | 2026-09-03 08:24 | 13.04 h | 950 | 368 | 0 | 4.68 MB |
| fixup | INTRO | `85038481` | 2026-09-03 08:38 | 2026-09-03 09:05 | 0.45 h | 386 | 145 | 0 | 1.27 MB |
| pass | SCEN | `32a5c485` | 2026-09-03 11:39 | 2026-09-03 18:41 | 7.04 h | 1887 | 723 | 1 | 8.31 MB |
| fixup | SCEN | `87111b01` | 2026-09-03 19:17 | 2026-09-03 19:56 | 0.65 h | 466 | 175 | 0 | 2.11 MB |
| coordinator | sdd pass 6 (CTRL + LIFE dispatch) | `57f313ff` | 2026-09-03 20:13 | 2026-09-07 06:49 | 82.59 h | 967 | 318 | 1 | 6.22 MB |
| pass | CTRL | `9aa803a6` | 2026-09-03 20:56 | 2026-09-04 03:28 | 6.54 h | 1663 | 653 | 1 | 7.77 MB |
| fixup | CTRL | `b083fa09` | 2026-09-04 03:38 | 2026-09-04 04:28 | 0.84 h | 410 | 155 | 0 | 2.36 MB |
| coordinator | sdd pass 7 onward (BIND…HTTP dispatch) | `f0755cd2` | 2026-09-04 06:25 | 2026-09-07 06:49 | 72.40 h | 4079 | 1409 | 4 | 28.24 MB |
| pass | LIFE | `1b0c9ce0` | 2026-09-04 06:53 | 2026-09-04 09:05 | 2.20 h | 788 | 302 | 0 | 4.72 MB |
| fixup | LIFE | `a443324a` | 2026-09-04 09:20 | 2026-09-04 10:02 | 0.70 h | 521 | 199 | 0 | 2.69 MB |
| pass | BIND | `c413c702` | 2026-09-04 11:03 | 2026-09-04 12:52 | 1.81 h | 629 | 243 | 0 | 4.34 MB |
| fixup | BIND | `b1a83e8c` | 2026-09-04 13:06 | 2026-09-04 13:38 | 0.54 h | 400 | 155 | 0 | 2.17 MB |
| pass | ANLZ | `903175bc` | 2026-09-04 14:56 | 2026-09-04 17:03 | 2.12 h | 781 | 284 | 0 | 4.25 MB |
| fixup | ANLZ | `2e1823ef` | 2026-09-04 17:16 | 2026-09-04 18:09 | 0.89 h | 524 | 199 | 0 | 2.55 MB |
| pass | MODB | `a4b2e48e` | 2026-09-04 21:28 | 2026-09-05 00:07 | 2.65 h | 962 | 350 | 0 | 5.71 MB |
| fixup | MODB | `7d3c1e2a` | 2026-09-05 00:07 | 2026-09-05 00:59 | 0.87 h | 543 | 209 | 0 | 2.99 MB |
| pass | CLI | `c1f3a7d2` | 2026-09-05 06:26 | 2026-09-05 14:50 | 8.40 h | 1037 | 374 | 0 | 5.58 MB |
| fixup | CLI | `e4a9c6b1` | 2026-09-05 14:59 | 2026-09-05 16:09 | 1.17 h | 502 | 195 | 0 | 2.94 MB |
| pass | IO | `b7e2d4f9` | 2026-09-05 17:16 | 2026-09-05 19:01 | 1.74 h | 574 | 203 | 0 | 3.62 MB |
| fixup | IO | `ec9b468c` | 2026-09-05 19:01 | 2026-09-05 19:40 | 0.65 h | 381 | 148 | 0 | 2.25 MB |
| pass | TKIT | `6dd27af2` | 2026-09-05 20:30 | 2026-09-05 22:41 | 2.18 h | 900 | 330 | 0 | 5.12 MB |
| fixup | TKIT | `f90fec47` | 2026-09-05 22:41 | 2026-09-05 23:19 | 0.63 h | 548 | 206 | 0 | 2.46 MB |
| pass | HTTP | `66abc611` | 2026-09-06 06:05 | 2026-09-06 08:14 | 2.15 h | 675 | 252 | 0 | 4.10 MB |
| fixup | HTTP | `d93f7fa9` | 2026-09-06 08:32 | 2026-09-06 09:06 | 0.57 h | 439 | 168 | 0 | 2.69 MB |
| coordinator | handoff read-in | `271772ca` | 2026-09-07 06:20 | 2026-09-07 06:21 | 0.00 h | 2 | 0 | 0 | 0.18 MB |
| coordinator | sdd big picture (`T-001`; round 3, `D12`–`D15`) | `e7ce0715` | 2026-09-07 06:48 | 2026-09-09 13:25 | 54.63 h | 371 | 145 | 0 | 4.04 MB |
| worker | `T-002` | `31de7461` | 2026-09-07 08:27 | 2026-09-07 09:19 | 0.86 h | 219 | 78 | 0 | 1.71 MB |
| worker | `T-003` | `a3c2e21f` | 2026-09-07 09:10 | 2026-09-07 10:43 | 1.54 h | 414 | 159 | 0 | 2.21 MB |
| worker | `T-004` | `d708b29c` | 2026-09-07 10:44 | 2026-09-07 12:00 | 1.27 h | 366 | 130 | 0 | 1.97 MB |
| worker | `T-005` | `bfd4de70` | 2026-09-07 12:10 | 2026-09-07 13:25 | 1.24 h | 559 | 192 | 0 | 2.31 MB |
| worker | `T-006` | `e94b5fa5` | 2026-09-07 13:28 | 2026-09-07 14:27 | 1.00 h | 392 | 148 | 0 | 2.13 MB |
| worker | `T-007` | `42857ca6` | 2026-09-07 14:32 | 2026-09-08 05:39 | 15.12 h | 781 | 290 | 0 | 3.50 MB |
| worker | `T-008` | `daa76f13` | 2026-09-07 17:51 | 2026-09-08 05:39 | 11.81 h | 837 | 312 | 0 | 3.77 MB |
| worker | `T-009` | `eff71a4e` | 2026-09-07 20:24 | 2026-09-08 08:19 | 11.92 h | 743 | 257 | 0 | 3.06 MB |
| worker | `T-010` | `cebb02a0` | 2026-09-07 21:38 | 2026-09-08 08:19 | 10.69 h | 524 | 189 | 0 | 2.26 MB |
| infra | `vion-dispatch` adoption (#202) | `79663cbe` | 2026-09-09 11:37 | 2026-09-09 12:52 | 1.24 h | 156 | 55 | 0 | 0.83 MB |
| coordinator | sdd closeout coordinator 2 (`T-020` rescope) | `fb3343f7` | 2026-09-09 13:25 | 2026-09-09 13:47 | 0.37 h | 123 | 43 | 0 | 0.89 MB |

## What this corrects in the change doc's heuristic

The change doc's § *What the passes measured, for the lanes* table was a string count, and it named
itself replaceable. Against the rows above:

- **Fix-up sessions are twelve, not eleven.** INTRO and SCEN both got a fix-up round; the change
  doc's count missed one of the fourteen area passes having one. Only PLUG (the pilot) and EMIT ran
  with none.
- **Two pass sessions compacted, not one.** SCEN (`32a5c485`) and CTRL (`9aa803a6`) each show one
  `isCompactSummary` record; the change doc's "0 (one at 1)" undercounted by one pass.
- **The tool-use range for pass sessions (137–723) and the coordinator range (318–1409) match the
  change doc's heuristic exactly**; the transcript-size ranges are close (pass: 2.04–8.31 MB vs. the
  heuristic's 2.0–8.3 MB) once the two aborted false starts and the additional fix-up rows are read
  separately rather than folded into the same bucket.
- **The fix-up size range is wider than stated (1.27–4.40 MB, not 2.2–3.1 MB)** — GATE's fix-up ran
  long (4.4 MB, 244 tool uses, closer to a pass than a fix-up) and INTRO's was the shortest (1.27 MB).
- The change doc's prose separately claims "~27 h (INTRO, with a 10 h stall)" for wall time falling
  to "~3 h (TKIT, HTTP)." INTRO's own session ran 13.04 h end to end with no visible mid-session gap
  in its timestamps; TKIT and HTTP measure at 2.18 h and 2.15 h here, close to "~3 h" but not equal
  to it. This harvest does not resolve where the "~27 h" and "10 h stall" figures came from — they
  may describe dispatch-to-signoff latency from the coordinator's side (which this table does not
  reconstruct) rather than the pass session's own wall time. Flagged rather than silently
  corrected, since the mechanism is a hypothesis, not a verified fact.

## Assumptions this harvest carries

- The transcript store had not truncated the window: the oldest SDD-window session
  (`a7da4594`, 2026-09-01 13:55) is present and complete, and every merge commit from `#159` through
  `#204` has at least one dispatched session accounted for above. The window is not truncated.
- "Session" means a dispatched, human- or coordinator-initiated Claude Code session (one carrying a
  `custom-title` or `agent-name` record) — not every `Task`-tool subagent call nested inside one.
  This mirrors the change doc's own "session kind" framing (pass / fix-up / coordinator) and is
  necessary to keep the row count meaningful; the alternative (one row per `.jsonl` file) would add
  roughly 230 subagent rows that duplicate their parent's own counted activity.
- Messages, tool uses and compactions are counted directly from each session's own transcript. A
  session that later resumed (`e7ce0715`, `a8ddb783`, `57f313ff`, `f0755cd2` all show multi-day gaps)
  is one row, not split at the gap, because it is one `.jsonl` file and one `sessionId`.
