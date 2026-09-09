---
description: Run every gate a PR is gated on and report one pass/fail line per gate
---

Run the repo's gate suite and report the result:

1. Execute `pwsh -File scripts/check.ps1` from the repo root. It derives the gate list from
   `.github/workflows/spec-gates.yml` — the workflow is the authority on which gates run and in
   what order — and runs each with the arguments CI gives it, printing one line per gate with
   that gate's own summary line. Takes about 90 seconds, nearly all of it the script self-tests.
2. Add `-CiShape` when the change touches a gate script, a self-test, or anything whose
   behaviour could depend on the filesystem. It runs with the repository's dot-directories
   hidden (Linux hides them, Windows does not), scans `scripts/*.ps1` for path literals whose
   casing disagrees with the git index, and makes `-Build`/`-Test` carry the
   `-p:Version=0.0.0-ci.1` that CI passes as a global MSBuild property. These are the three
   shapes that were green at the desk and red on the runner during the SDD closeout. The
   hidden-directory shape shows up as a **smaller file tally**, not as a failure, so compare the
   counts against a plain run rather than reading the exit code alone.
3. Add `-Build` and/or `-Test` when the change touches C#. They are off by default so the cheap
   path stays cheap; without them both appear as `SKIP` lines, which is the honest report.
4. Read the states before pasting. `SKIP` means the step did not run, `PARTIAL` under a gate
   means it ran without one of its rules and names which — both are unproven, not passed.
5. Paste the whole output into the PR body's gate record and the REPORT's `Gates` section.
6. New files must be `git add`ed first: several gates take their file set from `git ls-files`,
   so an untracked file is invisible to them and the run says nothing about it.

What this does **not** cover: the ReSharper style gate — run `/cleanup` separately, before
`gh pr create` — and the packed-artifact gate, whose input is the publish job's `.nupkg` output
rather than the working tree.

What each gate fails on is [`docs/spec-process.md`](../../docs/spec-process.md) § Gates.
