---
description: Run ReSharper cleanupcode (the same cleanup the CI style gate enforces) and apply fixes before a PR
---

Run the repo's canonical code-cleanup and report the result:

1. Execute `pwsh scripts/cleanup-code.ps1 -Changed` from the repo root. `-Changed` scopes
   the cleanup to the `.cs` this branch touched (vs `origin/main` + the working tree) and
   **skips entirely in ~0.5s when no `.cs` changed** — the fast dev-loop path (most edits in
   a session are wwwroot / docs / config, not `.cs`). It restores the pinned JetBrains tool,
   builds, and runs the exact `dotnet jb cleanupcode` invocation the CI "verify code style"
   gate runs (profile `Custom: Full Cleanup (excl. optimize usings)`, the same one
   ReSharper/Rider apply on save), applying fixes in place. (Drop `-Changed` for a full-solution
   pass; CI always runs the full `-Verify` gate as the authoritative backstop, so the two
   can't diverge. Note: dale-sdk's cleanupcode is Metalama-heavy, so `-Changed` mainly wins
   on the skip path — a `.cs` run is only modestly faster than full.)
2. Summarize what changed from its `git diff --stat` output.
3. If it applied changes, remind the user to commit them before opening the PR (or do
   so if appropriate). If it reported "Already clean", say so.

This is the same cleanup CI enforces, so running it now keeps the style gate from
failing the PR. The script captures cleanupcode's (benign, noisy) output and only
surfaces it if cleanupcode itself fails — so a quiet run means success.
