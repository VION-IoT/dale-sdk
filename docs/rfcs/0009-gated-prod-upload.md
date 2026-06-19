# RFC 0009: Gated production upload for libraries and onboarding examples

Status: **Accepted** (open questions resolved 2026-06-19; deltas implemented on `feat/gated-prod-upload`.
Revision 2: `Vion.Examples.PingPong` added to the onboarding/production example subset).
Author: jonas.bertsch. Date: 2026-06-19.

One sentence: promote the two existing test-only `dale upload` workflows into a two-stage,
GitHub-Environment-gated pipeline — every `main` push still auto-publishes to the Vion Cloud **test**
integrator exactly as today, and the same run then parks a **production** upload behind a required-reviewer
approval, so production never moves without a human click and the repo's **Environments** view shows, per
commit, what reached test versus production.

## Motivation

Today two workflows publish on every `main` push, test-only, with no notion of promotion:

- [upload-libraries.yml](../../.github/workflows/upload-libraries.yml) — `libraries/**` → `dale upload
  --environment test` (matrix: `Vion.Diagnostics`), with a `dotnet test` gate.
- [examples.yml](../../.github/workflows/examples.yml) — `examples/**` → `dale upload --environment test`
  (matrix: the six `Vion.Examples.*`).

Both use the client-credentials flow (`--client-id`/`--client-secret`) with `--skip-duplicate` and,
notably, **no `--integrator-id`** — the CLI auto-resolves the single integrator the service account belongs
to (CLAUDE.md, "Integrator context"). A release is just a `<Version>` bump in the project csproj.

The gap: there is no automated path to **production**, and no surface that shows promotion state. We want
(1) automated production upload that is **gated** — one required reviewer must approve; (2) **separated
credentials** so the test and production integrators never share a secret; and (3) **visibility in the
GitHub UI** of "published to test, not yet to production" (and, once approved, that it reached production).

## Constraint that shaped the design (why this is dale-sdk-specific)

GitHub **Environments** — deployment records, environment-scoped secrets, and the required-reviewer
protection rule — are free on **public** repositories. `dale-sdk` is public, so the full design below is
available at no cost. On a **private repository on the Free plan** none of this is available ("you will not
be able to configure any environments"); required reviewers on a private repo require Enterprise even on
paid plans. The private sibling repo therefore cannot mirror this design and is **out of scope here** — it
falls back to `workflow_dispatch` with `_TEST`/`_PROD`-suffixed repo secrets. This RFC covers `dale-sdk`
only.

## Decision

Each workflow becomes a two-stage pipeline:

```
upload-test  (environment: test, runs automatically on every main push)
        │  needs
        ▼
upload-prod  (environment: production, waits for required-reviewer approval)
```

On every qualifying `main` push, `upload-test` goes green immediately and `upload-prod` parks in GitHub's
**"waiting for review"** state. The Environments panel then reads, e.g.:

```
test         ✓ deployed  commit a1b2c3   (just now)
production   ⏳ waiting for review  commit a1b2c3      ← in test, not yet in production
             last deployed: commit 9f8e7d
```

### Trigger & scope (unchanged trigger, no tags)

- Trigger stays **push to `main`, path-filtered** (`libraries/**` / `examples/**`) plus `workflow_dispatch`.
  No tags for either area, matching the current model.
- **Production scope:**
  - **Libraries** → all libraries (currently just `Vion.Diagnostics`; prod matrix = test matrix).
  - **Examples** → the **onboarding subset** `Vion.Examples.Energy`, `Vion.Examples.ToggleLight`, and
    `Vion.Examples.PingPong`. The other three examples stay test-only.

### Examples production is change-scoped (the chosen "Option A")

Because the production example set is a strict subset of the test set, a push touching a test-only example
(e.g. `Vion.Examples.ModbusRtu`) would otherwise still raise a production approval for the onboarding examples
that did not change — a pointless click and a noisy board. To avoid that, the examples `upload-prod` job is
driven by a **dynamic matrix** computed from what actually changed: a `detect-prod` job runs a path filter
over the production example directories and emits the list of changed ones. If none changed, the
production job is **skipped entirely** — no deployment record, no approval request.

(Libraries do not need this yet: while `Vion.Diagnostics` is the only library, prod scope equals test scope,
so any `libraries/**` push that warrants a test upload also warrants the prod gate.)

### Two GitHub Environments

| Environment | Protection rule | Auto-deploys? | Holds |
|---|---|---|---|
| `test` | none | yes | test-integrator service-account creds |
| `production` | **required reviewer: `vion-iot-developers` team**; deployments restricted to `main` | no — waits for approval | production-integrator service-account creds |

**Naming note — two different "environment" concepts that happen to coincide.** The GitHub *Environment*
(`environment: production`, which gives the approval gate + deployment record + scoped secrets) is a
*different system* from the dale CLI's `--environment production` (which selects the Vion Cloud production
API host). They are named the same on purpose, but a job's GitHub Environment and its `dale --environment`
flag are set independently in the YAML.

### Secrets — separated by environment, same names

Both jobs reference `secrets.DALE_CI_CLIENT_ID` / `secrets.DALE_CI_CLIENT_SECRET`. The values are defined as
**environment-scoped** secrets, so resolution depends on the job's environment: the `test` job reads the
`test` environment's pair, the `production` job reads the `production` environment's pair. An
environment-scoped secret takes precedence over a repo-level secret of the same name, and the `test` job
**cannot read** the production pair. Integrator stays auto-resolved (no `--integrator-id`) — proven by the
current test workflow — and holds **as long as each service account maps to exactly one integrator**; if the
production service account can see more than one, add `--integrator-id`/`DALE_INTEGRATOR_ID`.

## Proposed workflow deltas

### `upload-libraries.yml` — split `upload` into `upload-test` + `upload-prod`

`upload-test` is today's `upload` job with one added line, `environment: test`. The new gated job (prod
matrix = test matrix; no re-test — `upload-test` already validated this commit, so prod only re-packs and
uploads):

```yaml
  upload-prod:
    needs: upload-test
    runs-on: ubuntu-latest
    environment: production
    strategy:
      fail-fast: false
      matrix:
        library:
          - Vion.Diagnostics
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
      - name: Install Vion.Dale.Cli
        run: dotnet tool update -g Vion.Dale.Cli
      - name: Upload library
        working-directory: libraries/${{ matrix.library }}/${{ matrix.library }}
        run: |
          dale upload \
            --client-id "$DALE_CI_CLIENT_ID" \
            --client-secret "$DALE_CI_CLIENT_SECRET" \
            --environment production \
            --skip-duplicate
        env:
          DALE_CI_CLIENT_ID: ${{ secrets.DALE_CI_CLIENT_ID }}
          DALE_CI_CLIENT_SECRET: ${{ secrets.DALE_CI_CLIENT_SECRET }}
```

### `examples.yml` — `upload-test` (all six) → `detect-prod` → `upload-prod` (changed subset)

`upload-test` is today's `upload` job with `environment: test` added. Then:

```yaml
  detect-prod:
    needs: upload-test
    runs-on: ubuntu-latest
    outputs:
      examples: ${{ steps.resolve.outputs.examples }}
    steps:
      - uses: actions/checkout@v4
      - id: changes
        uses: dorny/paths-filter@v4
        with:
          filters: |
            Vion.Examples.Energy:
              - 'examples/Vion.Examples.Energy/**'
            Vion.Examples.ToggleLight:
              - 'examples/Vion.Examples.ToggleLight/**'
            Vion.Examples.PingPong:
              - 'examples/Vion.Examples.PingPong/**'
      - id: resolve
        # On a push: only the changed prod examples. On manual dispatch: all (deliberate re-push).
        run: |
          if [ "${{ github.event_name }}" = "workflow_dispatch" ]; then
            echo 'examples=["Vion.Examples.Energy","Vion.Examples.ToggleLight","Vion.Examples.PingPong"]' >> "$GITHUB_OUTPUT"
          else
            echo 'examples=${{ steps.changes.outputs.changes }}' >> "$GITHUB_OUTPUT"
          fi

  upload-prod:
    needs: detect-prod
    if: ${{ needs.detect-prod.outputs.examples != '[]' }}
    runs-on: ubuntu-latest
    environment: production
    strategy:
      fail-fast: false
      matrix:
        example: ${{ fromJson(needs.detect-prod.outputs.examples) }}
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
      - name: Install Vion.Dale.Cli
        run: dotnet tool update -g Vion.Dale.Cli
      - name: Upload example
        working-directory: examples/${{ matrix.example }}/${{ matrix.example }}
        run: |
          dale upload \
            --client-id "$DALE_CI_CLIENT_ID" \
            --client-secret "$DALE_CI_CLIENT_SECRET" \
            --environment production \
            --skip-duplicate
        env:
          DALE_CI_CLIENT_ID: ${{ secrets.DALE_CI_CLIENT_ID }}
          DALE_CI_CLIENT_SECRET: ${{ secrets.DALE_CI_CLIENT_SECRET }}
```

`dorny/paths-filter`'s `changes` output is a JSON array of the filter keys that matched — exactly the dynamic
matrix we want. The filter keys are the project directory names, so a matched key plugs straight into
`working-directory`. One required-reviewer approval releases the whole job, i.e. all matrix legs (both prod
examples when both changed). `permissions: contents: read` is sufficient — environment deployments do not
need `deployments: write`.

## Visibility — what the requirement buys, and its limit

With both stages on environments, the repo's **Environments** view (right rail on the code page + a
dedicated page) shows, per environment: the last-deployed commit, who, when, status, full history, and the
"waiting for review" state with a Review button. That is the "in test, not yet in production" board.

**Granularity caveat (stated honestly):** GitHub deployment records are **per environment, per commit** —
not per library/per version. The board says "commit X reached production," not "`Vion.Diagnostics` 1.2.3 is
live but 1.3.0 is only in test." The authoritative "what version is live where" remains the **Vion Cloud
per-integrator dashboard**. For an at-a-glance "is production behind test?" the commit-level board is enough;
if precise per-version promotion tracking is ever needed, that is the cloud's job, not GitHub's.

## Setup

**Done via `gh` (this branch's setup, applied 2026-06-19):**

- `test` environment created (no protection rules).
- `production` environment created with required reviewer **`vion-iot-developers`** (team id 14882742) and a
  deployment-branch policy limiting it to `main`. `prevent_self_review` left off so the pushing maintainer
  can approve.

**Remaining manual (secrets — maintainer):**

1. **Add environment secrets** `DALE_CI_CLIENT_ID` / `DALE_CI_CLIENT_SECRET` to **each** environment
   (Settings → Environments → *env* → Environment secrets) — `production` gets a **production-integrator**
   service account distinct from test's.
2. Optionally remove the now-superseded **repo-level** `DALE_CI_CLIENT_ID`/`SECRET` once the test pair lives
   in the `test` environment (env scope makes the repo-level pair redundant for these workflows).

Until the `production` environment secrets exist, an `upload-prod` job that is reached will fail at the
`dale upload` step (missing credentials) — so add the secrets before the first production promotion.

## Resolved decisions (2026-06-19)

1. **Required reviewer** on `production` is the **`vion-iot-developers`** team (GitHub environment reviewers
   accept a team; any team member can approve). Configured via `gh` (team id 14882742).
2. **No re-test in the prod job** — `upload-test` already validated the exact commit; the prod job re-packs
   and uploads only.
3. **`dorny/paths-filter@v4`** chosen for `detect-prod` (3.2k★, not archived, latest v4.0.1, Mar 2026) over a
   hand-rolled `git diff`. Pinned to the `@v4` major tag to match the repo's existing action-pinning style;
   swap to a full commit SHA if stricter supply-chain pinning is later desired.

## Alternatives considered

- **Tag/release-triggered production** — rejected: the user keeps the commit-driven model with no tags for
  libraries or examples.
- **Accept spurious production approvals for unchanged examples ("Option B")** — rejected in favor of the
  change-scoped dynamic matrix; an approval click that resolves to a `--skip-duplicate` no-op is noise.
- **One GitHub Environment per library (for per-version visibility)** — rejected: multiplies environment and
  reviewer config; the cloud dashboard is the version source of truth.
- **Hand-rolled `git diff --name-only` instead of `dorny/paths-filter`** — viable; paths-filter chosen for
  robust push-event base detection. Recorded as a swap-in if avoiding a third-party action is later preferred.
- **Environment parity for the private sibling repo** — not possible on Free/private (see Constraint);
  manual dispatch + suffixed secrets there.

## Rollout

1. Land the workflow changes (this RFC's deltas).
2. Add the environment secrets (Setup → Remaining manual).
3. First production upload is the first `main` push that touches `libraries/**` or one of the two prod
   examples — or a deliberate `workflow_dispatch` — and then a `vion-iot-developers` approval.
