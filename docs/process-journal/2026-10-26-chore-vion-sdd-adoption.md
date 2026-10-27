2026-10-26 · review · vion-sdd-adoption · The change doc's frontmatter author line named the session `sdk: sdd plugin migration`, whose colon makes YAML read a nested mapping; VS Code's preview refused the frontmatter while spec-lint, which reads it by regex, passed. The shape was copied from the archived sdd-closeout's author line.

2026-10-26 · gate · vion-dispatch:report · The partial REPORT's written: field read 2026-10-26T19:30:00Z, typed by the worker, while the hook filed it at 20261026T165515Z; the coordinator caught the mismatch from the file name. (self)

2026-10-26 · gate · vion-sdd job · In both CI runs the plugin checkout's first fetch with the freshly minted App token answered "Repository not found"; actions/checkout's built-in retry fetched it 16 seconds later and the job went green. (self)

2026-10-27 · gate · vion-dispatch:ingest · session-report.ps1 latest returned the worker's earlier partial REPORT instead of its done one: it orders by the typed written: field, and the partial's mistyped 19:30Z sorted after the done report's 18:41Z; the newer file was found by listing the reports folder. (self)
