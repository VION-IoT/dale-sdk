2026-10-26 · review · vion-sdd-adoption · The change doc's frontmatter author line named the session `sdk: sdd plugin migration`, whose colon makes YAML read a nested mapping; VS Code's preview refused the frontmatter while spec-lint, which reads it by regex, passed. The shape was copied from the archived sdd-closeout's author line.

2026-10-26 · gate · vion-dispatch:report · The partial REPORT's written: field read 2026-10-26T19:30:00Z, typed by the worker, while the hook filed it at 20261026T165515Z; the coordinator caught the mismatch from the file name. (self)
