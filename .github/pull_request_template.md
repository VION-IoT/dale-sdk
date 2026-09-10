> External contributors: this repository is **source-available**. Pull requests from outside the `VION-IoT` organization are not accepted and will be auto-closed. See [CONTRIBUTING.md](../blob/main/CONTRIBUTING.md). For integration help, contact support@vion-iot.com.

<!--
The title is `<scope>: <what a reader sees>`. The squash merge keeps the title and this body and
nothing else, so what is not here is not recorded. The five sections below are the shape; drop a
heading only by saying why under it. `docs/spec-process.md` § Lanes says which lane you are in.
-->

## Summary

<!-- What a consumer of the SDK sees change, in their words, not the diff's. A behaviour, a
diagnostic, a CLI default, a message. Nothing consumer-visible is a fine answer — say so. For a
change doc's PR this is its "Relay notes for the PR body" section. -->

## Spec ids touched

<!-- Every `AC-…` this PR adds, rewords or removes, each with what changed. `None.` where the
change moves no criterion — a process document, a script, a test-only edit. A fix-sized change
carries its page edit here in the same commit set; that page edit is the distill. -->

## Gates

<!-- The pasted output of `pwsh -File scripts/check.ps1` (the `/check` command), verbatim, in a
fenced block. `SKIP` and `PARTIAL` are not passes; leave them visible. Add the `/cleanup` result,
and `-Build` / `-Test` when the change touches C#. -->

## Review round

<!-- The in-session `/vion-code-review branch` round from a fresh-context subagent: findings by
severity, each one fixed or accepted with its reason. One block per round, in the shape
`.claude/commands/vion-code-review.md` § 7 sets out; a second round is `branch:<the first round's
hash>` and gets its own block. No findings is a result — write it. The operator's dispositions ride
as one `review` line in `docs/process-journal.md`, in the commit that applies them (`CLAUDE.md`
working agreement 7). -->

## Verification beyond gates

<!-- What you ran that no gate runs, and what it showed: a mutation proven red first, a smoke skill,
a probe, a manual observation through the UI's own controls. A claim with nothing behind it belongs
here as an open question instead. -->
