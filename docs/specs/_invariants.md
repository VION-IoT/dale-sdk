---
trace: enforced
---

# Cross-cutting invariants

`SYS-` rules every area page cites instead of restating: repository-wide guarantees no single area
owns, which a page would otherwise re-mint from its own side. The corpus roster, area codes and
tiers live in [`../spec-process.md`](../spec-process.md); pages arrive one per pass, each carrying
`trace: enforced` frontmatter from the day it lands.

The minting rule for the pages themselves — one criterion states one rule, the family's members as
its rows; no criterion's subject is a test suite — is [`../spec-process.md`](../spec-process.md)
§ IDs & EARS.

This file is traced. A page may cite a rule here by its full id in **prose** — never inside a
criterion sentence — because the trace gate reads any acceptance id on a traced page as a
declaration, and a rule with no test of its own has to carry its `GAP` marker to stay green.

## Releasing

- `SYS-REL-001` (Ubiquitous): THE SYSTEM SHALL name every packable project of the repository in the
  release roster the version script clears from the local package cache, so a release publishes the
  same set the roster names.

## The published surface

- `SYS-API-001` (Ubiquitous): THE SYSTEM SHALL carry every `[PublicApi]` type of every shipped package in the public-API manifest (`docs/snapshots/publicapi-manifest.json`), regenerated and auto-committed on a pull request and opened as an issue against the architecture repository on `main`, so that a change to the published surface is visible in the diff of one file. GAP: the regenerate-and-commit half runs only inside `.github/workflows/publish.yml`, which no in-process test can construct.
