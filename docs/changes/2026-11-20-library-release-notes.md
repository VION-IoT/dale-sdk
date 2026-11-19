---
slug: library-release-notes
status: proposed           # proposed | in-flight | parked | archived
blocked-on: none           # for parked docs: what's blocking + ref
areas: CLI
author: jonasbertsch
created: 2026-11-20
updated: 2026-11-20
supersedes: none           # path of a superseded change doc, or none
---

# Vion.Diagnostics uploads with its release notes

> Change doc — one in-flight change. The `Spec delta` below is distilled into the current-truth
> pages under `docs/specs/` in the implementation PR, then this doc is archived
> (`spec-change.ps1 archive library-release-notes`). Process: `docs/spec-process.md`. Never put
> change narrative inline in a spec page — it lives here.

## At a glance

### Summary

dale-sdk's slice 5 of the cross-repo spec `specs/in-flight/2026-11-18-library-release-notes.md`.
`libraries/Vion.Diagnostics` uploads with no notes today. It should upload with the author's notes,
and a version that only follows the SDK should upload with none. The open question is how the
workflow avoids sending the previous version's notes with the next version: `set-version.ps1` bumps
`<Version>` on every SDK release. **Recommended: one file per version, `RELEASE_NOTES-<Version>.md`
beside the csproj. The workflow passes `--release-notes-file` only when that file exists.**

### Spec implications

None. No spec page describes the library upload workflow or `set-version.ps1`. `grep -rn
"upload-libraries\|set-version" docs/specs/` finds nothing. The change uses `AC-CLI-011.11` (an
empty or whitespace-only file uploads without notes) and `AC-CLI-011.12` (a named file that does
not exist is refused) as they already stand. The area is `CLI` because this change consumes those
criteria. It adds none, so the Spec delta is empty. The process changes go in
`docs/releasing.md` and in the workflow's header comment.

### Decisions

- `D1` — Only `Vion.Diagnostics` gets notes. The `examples/` uploads stay without them — the brief
  and the spec both leave them out.
- `D2` — The mechanism is one of the three options in Full design. **Not decided here (c).**
- `D3` — A note for a version that has already been uploaded does not reach the cloud through this
  workflow. It is corrected with the dashboard's edit, which is how the spec corrects a note.
  Reason: `--skip-duplicate` skips the upload as a whole. Every option shares this.

### Reviewer's questions

1. **(c) Which mechanism prevents stale notes?** The options are A (`set-version.ps1` empties one
   notes file), B (a version marker inside the notes) and C (one file per version, used only if it
   exists). Recommendation: **C**. Costs and reasons are in Full design.
2. **(c) If C: where do the files go?** `RELEASE_NOTES-<Version>.md` beside the csproj, as the brief
   says, or `release-notes/<Version>.md` under it, so that a dozen versions do not crowd the project
   folder. Recommendation: beside the csproj, as briefed. Moving them later costs one rename and one
   path in the workflow.
3. **(b) Merge order.** This PR changes only `.github/workflows/upload-libraries.yml`, `docs/` and
   possibly `scripts/`. None of these match the workflow's trigger, `paths: libraries/**`
   (`upload-libraries.yml:23-24`). So merging this PR does not start an upload. The first run that
   passes the option is the v0.17.1 reference PR, the PR where the first notes file is written. By
   that point `Vion.Dale.Cli` 0.17.1 is on nuget.org, because the reference PR cannot restore
   without it (`docs/releasing.md` § Wait for the packages). The vion-test outage still applies to
   that PR, not to this one.

---

## Full design

### What exists

- `<Version>` in `libraries/Vion.Diagnostics/Vion.Diagnostics/Vion.Diagnostics.csproj:14` is
  `0.17.0`, and that version is already uploaded.
- `scripts/set-version.ps1:230-232,396-407` sets that `<Version>` to the SDK version on every
  reference bump.
- Any push to `main` touching `libraries/**` triggers the workflow (`upload-libraries.yml:20-24`).
  Both upload steps run in the csproj's folder (`:53`, `:84`) with `--skip-duplicate` (`:59`,
  `:90`). `upload-prod` runs only after `upload-test` succeeds (`:65`).
- `AC-CLI-011.8` treats only a conflict that names an existing version as a skip. The cloud decides
  which version is a duplicate. The notes play no part in that decision. This is inferred from the
  criterion's text and was not observed on a run.
- Two harmless effects follow, shared by every option:
  - A push that changes only notes for a version that is already uploaded starts the workflow,
    which then skips the upload as a duplicate (D3).
  - The same commit is uploaded to test and then to prod, so both environments get the same notes.

### Option A — `set-version.ps1` empties one `RELEASE_NOTES.md`

One file, kept in the repo. The bump script truncates it when it bumps `<Version>`. The workflow
always passes `--release-notes-file RELEASE_NOTES.md`. An empty file means no notes
(`AC-CLI-011.11`).

- **Cost:** the notes must be written in the reference PR itself, after the script runs. The bump
  commit is the one that uploads. Notes written in an earlier PR are wiped by the script. Notes
  written in a later PR arrive after the version is uploaded and are skipped (D3).
- **Cost:** a `<Version>` bumped by hand between releases does not empty the file, and that is
  allowed (`upload-libraries.yml:10-12`). In that case the stale notes return, which is the defect
  this change is meant to prevent.
- **Cost:** the file must always exist, even when empty. If it is missing, every upload is refused
  (`AC-CLI-011.12`).

### Option B — the notes name their version, and the workflow compares

`RELEASE_NOTES.md` begins with a version line. The workflow sends the file only when that line
matches `<Version>`.

- **Cost:** the workflow reads and interprets part of the notes, which breaks Constraint 1 (no tool
  parses notes).
- **Cost:** `AC-CLI-011.11` sends the text unchanged. Unless the workflow writes a stripped copy,
  the marker line reaches consumers. A stripped copy means the workflow is now transforming notes as
  well as reading them.

### Option C — one file per version, sent only when it exists (recommended)

`RELEASE_NOTES-<Version>.md` sits beside the csproj. The upload step reads the project's version,
then passes the option only for a file with that exact name:

```bash
version=$(dotnet msbuild -getProperty:Version)
notes="RELEASE_NOTES-$version.md"
args=()
if [ -f "$notes" ]; then args=(--release-notes-file "$notes"); echo "Release notes: $notes"
else echo "No $notes — uploading $version without release notes"; fi
dale upload ... --skip-duplicate "${args[@]}"
```

The step runs in the csproj's folder. That folder holds one project, so `msbuild` needs no project
argument. Locally, `dotnet msbuild libraries/Vion.Diagnostics/Vion.Diagnostics/Vion.Diagnostics.csproj
-getProperty:Version` printed `0.17.0`. Both upload steps get the same logic.

- **Why C:** the file name is the only thing the workflow checks. It never opens the file, so
  Constraint 1 holds. A version that only follows the SDK has no file, so it uploads with no notes
  and no placeholder (Constraint 2). Nothing has to remember to empty anything, so a hand bump
  between releases also works. Notes can be written in any PR that lands no later than the bump:
  a feature PR can add the next version's file in advance. Until `<Version>` reaches that version,
  the file just sits there unused.
- **Cost:** the files accumulate, one per version that has notes. They also serve as a readable
  history. Old ones are never sent again because their version never comes back.
- **Cost:** a misspelled version in the file name means that version silently uploads without notes.
  The step's log line names the file it looked for, which is where a reader would notice. The fix
  is the dashboard edit (D3).
- **Cost:** the workflow learns the version, which it does not need today. Getting it costs one
  `msbuild` evaluation in a job that has already run `setup-dotnet`.

### Docs that move with the code (any option)

- `docs/releasing.md`, after the paragraph on `set-version.ps1` covering `libraries/`: one paragraph
  telling the author where a library version's notes go and when they must land.
- `upload-libraries.yml` header comment: one sentence describing the notes rule.
- Option A only: `set-version.ps1` and its header comment.

### Not in scope

- No change to `dale` (brief).
- No notes for `examples/` (D1).
- No content for 0.17.1's notes. They are written in the v0.17.1 reference PR (brief § Scope 2).

---

## Drift checkpoints

> One line per divergence discovered during implementation:
> `YYYY-MM-DD: <what changed and why>`. Never inline in a spec page. A checkpoint that fixes a
> CLASS bug states the sibling sweep (done / N/A / handed off).

- _(none yet — append as implementation diverges from Full design)_

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
> after distill (backticks stripped) — a real `AC-`/`SYS-` id in an area this repo owns, never an
> ad-hoc label and never another repo's area. A criterion the delta declares unreachable carries
> its `GAP: <reason>` marker on the delta line.

_(none — the change consumes `AC-CLI-011.11` and `AC-CLI-011.12` unchanged; see Spec implications)_

---

## Tasks

> One-commit tasks, each tagged with ≥1 AC id. Ephemeral — they live and die with this change doc.
> Plain list, no checkboxes — the PR's per-task commits are the completion record.

Written for option C; they get rewritten if A or B is chosen.

- `T-001` (`AC-CLI-011.11`, `AC-CLI-011.12`): `upload-libraries.yml` — both upload steps read
  `<Version>` and pass `--release-notes-file RELEASE_NOTES-<Version>.md` only when that file exists.
  Also the header comment.
- `T-002` (`AC-CLI-011.11`): `docs/releasing.md` — where a library version's notes go and when
  they must land.
