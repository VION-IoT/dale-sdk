---
trace: enforced
---

# Cross-cutting invariants

`SYS-` rules every area page cites instead of restating: repository-wide guarantees no single area
owns, which a page would otherwise re-mint from its own side. The corpus roster and area codes
live in [`../spec-process.md`](../spec-process.md); pages arrive one per pass, each carrying
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
- `SYS-API-002` (Ubiquitous): THE SYSTEM SHALL enable package validation, whenever a baseline version is given, for every packable project of the repository that is not a .NET tool and that the baseline release contains, and SHALL enable it for none when no baseline version is given.
- `SYS-API-003` (Ubiquitous): THE SYSTEM SHALL fail a pull request whose packages are not binary-compatible with the previous release's, the previous release being the highest release tag reachable from the commit under test that does not point at it, with no suppression file. GAP: the gate runs only inside `.github/workflows/publish.yml`, which no in-process test can construct; the baseline derivation has a script self-test.

The package-validation rules keep every release binary-additive over the one before it within its
major, which is what makes a library's minimum Dale SDK ([`plugin-loading.md`](plugin-loading.md)) a
sufficient answer. A project the baseline release lacks ships its first release, with nothing to be
compatible with. The procedure, and what the gate does not cover, is
[`../releasing.md`](../releasing.md).
