---
slug: dale-sdk-compatibility
status: archived
blocked-on: none           # for parked docs: what's blocking + ref
areas: PLUG, INTRO, API
author: jonasbertsch
created: 2026-11-11
updated: 2026-11-11
supersedes: none           # path of a superseded change doc, or none
---

# A library says which Dale SDK it needs, and the host can tell before it runs a block

> Change doc — one in-flight change. The `Spec delta` below is distilled into the current-truth
> pages under `docs/specs/` in the implementation PR, then this doc is archived
> (`spec-change.ps1 archive <slug>`). Process: `docs/spec-process.md`. Never put change narrative
> inline in a spec page — it lives here.

## At a glance

### Summary

A library built against a newer `Vion.Dale.Sdk` than the gateway's dale fails block by block with
`MissingMethodException` (Modbus.Rtu 0.15 on dale 0.22.4 / SDK 0.14.3). dale-sdk's slice of the
cross-repo spec `specs/in-flight/2026-11-10-dale-sdk-compatibility.md` (step 2 of 5): the parser
stamps a library's target and declared-minimum Dale SDK into the introspection document;
`Vion.Dale.Plugin` reads both in one public place and offers dale a one-line load check that warns
and proceeds; a CI gate keeps each release binary-additive over the previous one within its major,
which is what makes "minimum version" a sufficient answer.

### Spec implications

`plugin-loading.md` gains § The library's minimum Dale SDK (`AC-PLUG-009.*`): reading the target
version (the highest `Vion.Dale.Sdk` reference in the directory) and the declared minimum (assembly
metadata on the main assembly), `0.0.0` as unknown, and the comparison dale calls after constructing a
context. § The SDK version gate's `AC-PLUG-002.1` is `MODIFIED`: a `0.0.0` on either side is unknown
there too (question 3). `introspection.md` rewords `AC-INTRO-001.4` — the document-level map is no longer
always empty — and rewrites the prose under it (`introspection.md:46-47`), which today tells a
consumer the slot never carries a version signal; it gains `AC-INTRO-001.7` (target stamp),
`AC-INTRO-001.8` (declared-minimum stamp), `AC-INTRO-002.10` (the pack refuses a malformed or
wrong-major minimum) and `AC-INTRO-018.1` (the `MinimumDaleSdkVersion` MSBuild property reaches the
assembly). `_invariants.md` § The published surface gains `SYS-API-002` (every packable project is
package-validated against a baseline) and `SYS-API-003` (the CI gate itself, `GAP` like
`SYS-API-001`'s CI half). `docs/releasing.md` says where the baseline comes from and that a finding is
fixed in code or waits for a major.

### Decisions

Ratified by the brief (`brief-dale-sdk-compatibility-dale-sdk.md` § Constraints already decided) —
cited, not reopened: warn and never refuse; a missing version is silent, `0.0.0` is unknown; the
minimum is a fact pair (`MinimumDaleSdkVersion` else `TargetDaleSdkVersion`); `AssemblyMetadata`,
never an SDK attribute; no suppression file; the keys are `Vion.Contracts`' constants (11.4.0); the
comparison is not in the `PluginLoadContext` constructor; library level only. Also from the brief:
an unknown target is **omitted** from the document and a declared minimum is then validated for
format only.

This session's calls, each (b) decide-and-document:

- `D1` — **API shape in `Vion.Dale.Plugin`.** A public static `DaleSdkVersions` with
  `Host` (the loaded SDK's `MAJOR.MINOR.PATCH`, `null` when `0.0.0`), `ReadLibrary(pluginDirectory,
  mainAssemblyPath)` → a `LibraryDaleSdkVersions(Target, DeclaredMinimum)` record, and
  `Compare(library, host)` → an `UnmetDaleSdkMinimum?` (`null`: the minimum is met, a version is
  unknown, or the majors differ). `PluginLoadContext` gains `CheckLibraryDaleSdkMinimum(mainAssemblyPath)`,
  which reads, compares, logs the Warning and returns the unmet minimum — dale's change at
  `LibraryLoader.cs:391` is that one line after construction. The parser calls `ReadLibrary` only.
- `D2` — **The comparison is a call of its own and returns its verdict.** Constraint 7 (no comparison
  in the constructor) is held by this shape, not by a criterion: moved into the constructor, the check
  would change only a log line, which no test here may assert, so no mutation could redden such a
  criterion. It returns its verdict rather than only logging it: log text is not
  observable here (`testing-conventions.md` §15), so the criteria pin the returned value and the
  Warning is the instance method's side effect. `Compare` takes the host version as a parameter
  because every in-repo build hosts `0.0.0` — no end-to-end test could otherwise reach a known host.
- `D3` — **Validation runs in a job this repo owns.** A new `package-validation` job in
  `publish.yml`, beside `publish`, checks out with tags, derives the baseline and packs the solution
  fresh with `-p:PackageValidationBaselineVersion=<baseline>`. The shared `publish-nuget.yml` is not
  touched, so there is no shared-workflows Deviation. Skipped on a release tag: the tag commit's pull
  request already ran against the same baseline.
- `D4` — **Enabled per packable project, only under a baseline.** `Directory.Build.targets` sets
  `EnablePackageValidation=true` where `IsPackable` is `true` and `PackageValidationBaselineVersion` is
  non-empty. Measured: a solution-wide `-p:EnablePackageValidation=true` fails restore, because four
  test projects that never set `IsPackable` go looking for a `0.16.0` baseline of themselves (NU1101).
  The `IsPackable` condition re-derives the brief's `[assumed]` list exactly — 20 packages, the CLI
  tool excluded. Without a baseline nothing changes: the shared pack and a desk `dotnet pack` behave as
  today.
- `D5` — **The baseline is derived by a script**, `scripts/validate-packages.ps1`: the highest
  `v*` tag (by version) in `git tag --merged HEAD`, minus `git tag --points-at HEAD`. Same script at the
  desk and in CI; its derivation has a `scripts/*.tests.ps1` self-test, as `run-script-tests.ps1`
  demands.
- `D6` — **Known-path fixtures** are made in the tests, not as new projects. Plugin side: the existing
  `FixtureAssembly.EmitStandInSdk` / `Emit` (Roslyn) already emit assemblies referencing a non-zero
  stand-in SDK; the metadata attribute is one more emit option. Parser side: a test copies the probe's
  output into a temp directory and adds an emitted stand-in dll referencing `Vion.Dale.Sdk` `x.y.z`
  (raising the target) and/or emits the main assembly with `[assembly: AssemblyMetadata(...)]`.
- `D7` — **`Vion.Contracts` 11.0.2 → 11.4.0** in `Vion.Dale.Sdk.csproj`; dale-sdk uses only the new
  `const` keys, which compile into the caller, so the host needs no 11.4.0 at runtime for them.

### Reviewer's questions

1. **(c) Which types become `internal`?** Recommendation: **none — an empty list.** Measured:
   packing all 20 packages against the `0.16.0` baseline today gives **zero** findings, and a positive
   control (`Vion.Dale.Sdk.Http` against `0.14.1`) reproduces the spec's 20 × CP0002, 14 × CP0006, so
   the validation demonstrably runs. The natural candidates are the 49 `[InternalApi]` types; none
   qualifies cheaply: `IInterfaceFactory` is in a public signature
   (`DeclarativeInterfaceBinder.cs:12-15`); contract and handler types are public so the TestKits —
   separately versioned packages — can reach them (`sdk-surface-conventions.md:277`), and
   `InternalsVisibleTo` across independently versioned packages trades a compile-time break for a
   runtime one; dale reaches handler actors by reflection (`LogicSystemConfigurationInitializer.cs:664`,
   `IsAssignableFrom` over `HandlerActorTypes`) and by `nameof` lookups (`:542`, `:572-574`), which a
   name grep does not see. Cost of the recommendation: every `[InternalApi]` type stays public and is
   held additive until 1.0, the planned break, where internalising is free. Alternative: vet a subset
   per type (public-signature exposure, dale by name and by reflection, TestKit reach) in its own PR.
   **OUTCOME: accepted — none** (operator, 2026-11-11, amendment 1). Every `[InternalApi]` type stays
   public and is held additive until 1.0.
2. **(c) Sequencing.** Follows from 1. **(A, recommended) empty list:** arm the gate in this PR; one
   release, `0.17.0`, carries the load check, the stamp and the targets property; the gate is CI-only
   and ships nothing. **(B) a non-empty list:** this PR internalises and does not arm; release `0.17.0`;
   a second PR arms the gate and must merge only after the `v0.17.0` tag exists (before it, the derived
   baseline is still `0.16.0` and the internalisation fails it). Still one release for dale's step 5,
   but two PRs and a window in which `0.17.0` ships unguarded. **OUTCOME: accepted — A** (operator,
   2026-11-11, amendment 1): the gate arms in this PR; one release, `0.17.0`.
3. **(c) Does `0.0.0`-is-unknown also apply to the major gate (`AC-PLUG-002.1`)?** Pre-1.0 nothing
   observable changes either way — every major is 0. At 1.0: **not applied**, a desk-built host
   (`0.0.0`) rejects every released 1.x library and every 1.x host rejects a desk-built library;
   **applied**, both load and the new check stays silent for them. Recommendation: **apply**, a
   `MODIFIED` line adding "and neither version is `0.0.0`" — one rule for "unknown" across both checks,
   and the failure it removes would surface first at 1.0, when nobody is looking at this code. Cost: the
   end-to-end test of `AC-PLUG-002.1` today relies on the in-repo host being `0.0.0` and the fixture's
   being `1.0.0` (`PluginLoadContextShould.cs:96-105`); under the rule that pair is "unknown" and no
   rejection follows, so the test moves to a host-version seam (an internal constructor overload, as
   `EnsureSdkMajorCompatible` already takes both versions for `AC-PLUG-002.3`).
   **OUTCOME: accepted — apply** (operator, 2026-11-11, amendment 1). The `MODIFIED AC-PLUG-002.1` line
   stands; `AC-PLUG-002.7`'s test (`RejectPluginNamingOneFailingAssembly`) relies on the `0.0.0` host the
   same way and moves to the seam too (`T-003`).
4. **(b) The public names in `D1`** (`DaleSdkVersions`, `LibraryDaleSdkVersions`, `DaleSdkShortfall`,
   `CheckLibraryDaleSdkMinimum`) are mine; dale is the only caller beyond the parser. Rename at
   ratification if you prefer others — afterwards they are held by the gate this PR arms.
   **OUTCOME: changed** (operator, 2026-11-11, amendment 1): `DaleSdkShortfall` → `UnmetDaleSdkMinimum`;
   the other three stand.
5. **(b) The 1.0 baseline reset is not built here.** `D5` derives the previous tag whatever its major,
   so the PR that starts 1.0 must also teach the script to skip a baseline of an older major (or the
   1.0 tag fails). No major is planned in this round; building the reset now would be untested code.
   **OUTCOME: accepted — left out** (operator, 2026-11-11, amendment 1); a known limitation, named in
   the PR body for the PR that starts 1.0.

---

## Full design

### Version facts and the comparison

- **Host**: `typeof(LogicBlockBase).Assembly.GetName().Version`, as `EnforceSdkMajorCompatibility`
  reads it (`PluginLoadContext.cs:385-386`); truncated to `MAJOR.MINOR.PATCH`; `0.0.0` → `null`.
- **Target**: the maximum over `TryReadReferencedSdkVersion` (`PluginLoadContext.cs:292`) across the
  flat directory — the same enumeration the major gate makes (`:393`), with the maximum new;
  truncated; `0.0.0` → `null`; no dll referencing the SDK → `null`. The incident case is exactly why it
  is the maximum: a library built against 0.14.3 that ships `Vion.Dale.Sdk.Modbus.Rtu.dll` 0.15
  targets 0.15.
- **Declared minimum**: `AssemblyMetadataAttribute` with key
  `DaleSdkCompatibilityConventions.MinimumDaleSdkVersionAnnotationKey`, read from the main assembly's
  metadata with `PEReader`, as `HasDaleSharedAssemblyAttribute` reads its marker (`:329-372`), so an
  older host never meets a type it lacks. A value that is not `MAJOR.MINOR.PATCH` reads as not
  declared at load (the pack already refused it, `AC-INTRO-002.10`); `0.0.0` reads as not declared.
- **Compare**: effective minimum = declared, else target. Unmet when both it and the host are
  known, the majors are equal and minimum > host on `MAJOR.MINOR.PATCH`. A differing major is the major
  gate's (`AC-PLUG-002.1`), so the comparison says nothing about it.
- **Warning** (dale's log): package id, the minimum and whether it was declared or the target, the
  host version, "the load proceeds".

### The stamp

`Program.cs:201` builds `Annotations` from `DaleSdkVersions.ReadLibrary(directory, pluginDllPath)`:
`TargetDaleSdkVersion` when the target is known, `MinimumDaleSdkVersion` when declared, keyed by the
`Vion.Contracts` constants; the serializer's dictionary-key policy (`Program.cs:52`, `AC-INTRO-001.6`)
puts them on the wire as `targetDaleSdkVersion`, `minimumDaleSdkVersion`. Validation before any
document is written: the raw metadata value must parse as `MAJOR.MINOR.PATCH`, and when the target is
known the majors must match; otherwise the run fails naming both. Above the target is allowed.

### The MSBuild property

`Vion.Dale.Sdk.targets` (packed to `build/`, `Vion.Dale.Sdk.csproj:97`, so it reaches a project
referencing the package directly — the only kind the parser runs for):

```xml
<ItemGroup Condition="'$(MinimumDaleSdkVersion)' != ''">
    <AssemblyMetadata Include="MinimumDaleSdkVersion" Value="$(MinimumDaleSdkVersion)"/>
</ItemGroup>
```

Its test builds a small project that imports the targets file with the property set and reads the
compiled metadata. The project removes the analyzer item the targets file adds (a path that exists
only inside the packed package); the parser run is a pack target and a build does not reach it.

### The CI gate

`scripts/validate-packages.ps1 [-Baseline <v>]`: derives the baseline (`D5`) unless given; names the
solution's projects absent from the baseline tag's tree in `-p:DaleProjectsWithoutBaseline` (their
first release has nothing to compare with); deletes the validation's up-to-date markers under `obj/`
(the spec's spike saw an up-to-date package skip `RunPackageValidation`, and its inputs do not include
the baseline); then `dotnet pack Vion.Dale.Sdk.sln -c Release -p:Version=<b+1 patch>-validation
-p:PackageValidationBaselineVersion=<b> -o <temp dir>`. `publish.yml` gains `package-validation`
(needs `changes`; `dotnet == 'true'`; not on a tag; `fetch-depth: 0`; private feed set up as `style`
does), `ci` waits on it, and the scope step counts `validate-packages.ps1` as read by the build.

The pack version is above the baseline because a pack at the CI version `0.0.0-ci.N` fails on its own
(Drift checkpoints).

### Alternatives discarded

- Validation inside the shared `publish-nuget.yml` pack: needs a new input there (a shared-workflows
  change) and tags its checkout does not fetch; saves one build per run.
- `EnablePackageValidation` in each of the 20 csproj files: a new packable project would be outside
  the gate until someone remembers; `Directory.Build.targets` covers it by construction, and
  `SYS-API-002`'s test pins it.
- An SDK attribute for the minimum: refused by the brief (constraint 4).

---

## Drift checkpoints

> One line per divergence discovered during implementation:
> `YYYY-MM-DD: <what changed and why>`. Never inline in a spec page. A checkpoint that fixes a
> CLASS bug states the sibling sweep (done / N/A / handed off).

- 2026-11-11: brief's `[assumed]` package list re-derived — the 20 projects setting `IsPackable` to
  `true` in `Vion.Dale.Sdk.sln`, identical to the brief's; the CLI (`PackAsTool`) and the parser probes
  (`IsPackable` `false`) fall outside. Evidence: the trial below produced those 20 `.nupkg` plus the CLI.
- 2026-11-11: trial, `dotnet pack Vion.Dale.Sdk.sln -c Release -p:Version=0.17.0 -p:PvBaseline=0.16.0
  -p:CustomBeforeMicrosoftCommonTargets=<scratch targets setting EnablePackageValidation and
  PackageValidationBaselineVersion where IsPackable is true>` → exit 0, no `CP` diagnostic. Control,
  the same for `Vion.Dale.Sdk.Http` against `0.14.1` → exit 1, `20 error CP0002`, `14 error CP0006`.
- 2026-11-11: brief said `origin/main` differed from `v0.16.0` (`9e4c230a`) only in
  `scripts/set-version.ps1` and `examples.yml`; it is now at `da812778`, two more commits
  (#283, #284) touching examples only. The derived baseline is still `0.16.0`.
- 2026-11-11: the open item resolved — `dotnet pack Vion.Dale.Sdk.Http -p:Version=0.0.0-ci.1
  -p:PackageValidationBaselineVersion=0.16.0` → exit 1, `error CP0003` (assembly version `0.0.0.0`
  below the baseline's). The script packs at the baseline's next patch, `-validation`-suffixed.
- 2026-11-11: **a package's first release has no baseline** — not in the spec or this doc's design.
  `validate-packages.ps1 -Baseline 0.14.1` failed restore with 14 × `NU1102` for `Vion.Dale.Sdk.MBus`
  and its TestKit, first released in 0.16.0; the same would fail every pull request that adds a
  package. The script now names the projects absent from the baseline tag's tree and
  `Directory.Build.targets` leaves them out, so `SYS-API-002` gains that exception. Surfaced in the PR
  and the REPORT as a decision the brief did not name.
- 2026-11-11: the red proof after that fix — `-Baseline 0.14.1` → exit 1, 26 × `CP0002`, 22 × `CP0006`
  (34 of them `Vion.Dale.Sdk.Http`, the spec's 20 + 14); `validate-packages.ps1` with the derived
  `0.16.0` baseline → exit 0, no `CP` diagnostic, 21 packages, the new `Vion.Dale.Plugin` surface
  included.
- 2026-11-11: the script's first form exited 0 on a failed pack: `dotnet pack`'s output joined the
  function's return value and `exit` received an array. Found by that red proof, fixed with `Out-Host`.
  Sibling sweep of `scripts/*.ps1` for a native call writing into a returned exit code: done, none
  other — `verify-packed-assembly-versions.ps1` is the one other script exiting with a function's return
  value, and its gate path calls no native command (`grep -nE '^\s+& (dotnet|git|pwsh|node)\b[^|]*$'`).
- 2026-11-11: `D6` changed shape — the parser fixtures copy nothing. An emitted plugin with no logic
  block sits alone in its own directory (plus the stand-in-referencing dependency); the parser's load
  context resolves `Vion.Dale.Sdk` and its references from the parser's own process (`AC-PLUG-003.3`).
- 2026-11-11: the `0.0.0` sweep beyond the amendment's `AC-PLUG-002.1` and `AC-PLUG-002.7`: the tests of
  `AC-PLUG-002.2` and `AC-PLUG-008.1`'s version-check half also compared against the in-repo `0.0.0`
  host and would pass vacuously; both moved to the seam. `AC-PLUG-002.5`'s tests skip a file before
  any comparison and stay on the real host.
- 2026-11-11: the `PackAsTool` clause `D4` added to the `Directory.Build.targets` condition survived its
  mutation, even with `-p:IsPackable=true`: the .NET SDK sets `EnablePackageValidation` to `false` for a
  tool itself (`dotnet msbuild Vion.Dale.Cli/Vion.Dale.Cli.csproj -p:PackageValidationBaselineVersion=0.16.0
  -p:IsPackable=true -getProperty:EnablePackageValidation` → `false` with the clause removed). The clause
  is removed; `SYS-API-002`'s whole-solution test still pins that the tool is not validated.
- 2026-11-11: criteria reworded before publication to say what their tests prove: `AC-PLUG-009.1`
  (none when no assembly references the SDK), `AC-PLUG-009.5` (a met minimum), `AC-INTRO-018.1` (no
  metadata when the property is unset), `SYS-API-002` (the tool and a project the baseline lacks).

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

- ADDED AC-PLUG-009.1 -> docs/specs/plugin-loading.md : THE SYSTEM SHALL report a library's target Dale SDK version as the highest `Vion.Dale.Sdk` version referenced by any assembly directly in its plugin directory, as major, minor and patch, without loading any of them, and SHALL report none where no assembly there references `Vion.Dale.Sdk`.
- ADDED AC-PLUG-009.2 -> docs/specs/plugin-loading.md : THE SYSTEM SHALL report a library's declared minimum Dale SDK version from the `MinimumDaleSdkVersion` assembly metadata of its main assembly, without loading it, and SHALL report none where that value is absent or not a major.minor.patch version.
- ADDED AC-PLUG-009.3 -> docs/specs/plugin-loading.md : WHEN a host, target or declared `Vion.Dale.Sdk` version is `0.0.0` THE SYSTEM SHALL report it as unknown.
- ADDED AC-PLUG-009.4 -> docs/specs/plugin-loading.md : WHEN a library's minimum Dale SDK version — the declared minimum, else the target version — is above the host's `Vion.Dale.Sdk` version within the same major, comparing major, minor and patch only, THE SYSTEM SHALL report the minimum as unmet, naming the minimum, whether it was declared or is the target, and the host version, and SHALL let the load proceed.
- ADDED AC-PLUG-009.5 -> docs/specs/plugin-loading.md : WHEN the library's minimum is not above the host's `Vion.Dale.Sdk` version, either of them is unknown, or their majors differ, THE SYSTEM SHALL report no unmet minimum.
- MODIFIED AC-PLUG-002.1 -> docs/specs/plugin-loading.md : WHEN a plugin load context is created over a directory containing an assembly built against a `Vion.Dale.Sdk` whose major version differs from the SDK the host has loaded, and neither version is `0.0.0`, THE SYSTEM SHALL reject the plugin with a `PluginSdkVersionMismatchException` before loading any plugin assembly into the context.
- MODIFIED AC-INTRO-001.4 -> docs/specs/introspection.md : THE SYSTEM SHALL report at document level no annotation but the Dale SDK version annotations, and an empty annotation map when neither applies.
- ADDED AC-INTRO-001.7 -> docs/specs/introspection.md : WHEN the plugin's target Dale SDK version is known THE SYSTEM SHALL report it as the document-level `TargetDaleSdkVersion` annotation in major.minor.patch form, and SHALL omit that annotation when it is unknown.
- ADDED AC-INTRO-001.8 -> docs/specs/introspection.md : WHEN the plugin assembly declares a minimum Dale SDK version THE SYSTEM SHALL report it as the document-level `MinimumDaleSdkVersion` annotation, and SHALL omit that annotation when none is declared.
- ADDED AC-INTRO-002.10 -> docs/specs/introspection.md : IF the plugin assembly declares a minimum Dale SDK version that is not a major.minor.patch version, or whose major differs from a known target version's, THEN THE SYSTEM SHALL fail the run naming both values and SHALL write no document.
- ADDED AC-INTRO-018.1 -> docs/specs/introspection.md : WHERE a library project sets the `MinimumDaleSdkVersion` MSBuild property THE SYSTEM SHALL compile it into the library's assembly as `MinimumDaleSdkVersion` assembly metadata, and SHALL add no such metadata where the property is not set.
- ADDED SYS-API-002 -> docs/specs/_invariants.md : THE SYSTEM SHALL enable package validation, whenever a baseline version is given, for every packable project of the repository that is not a .NET tool and that the baseline release contains, and SHALL enable it for none when no baseline version is given.
- ADDED SYS-API-003 -> docs/specs/_invariants.md : THE SYSTEM SHALL fail a pull request whose packages are not binary-compatible with the previous release's, the previous release being the highest release tag reachable from the commit under test that does not point at it, with no suppression file. GAP: the gate runs only inside `.github/workflows/publish.yml`, which no in-process test can construct; the baseline derivation has a script self-test.

---

## Tasks

> One-commit tasks, each tagged with ≥1 AC id. Ephemeral — they live and die with this change doc.
> Plain list, no checkboxes — the PR's per-task commits are the completion record.

- `T-001` (`AC-PLUG-009.1`, `AC-PLUG-009.2`, `AC-PLUG-009.3`): bump `Vion.Contracts` to 11.4.0; `DaleSdkVersions.Host` and `ReadLibrary` in `Vion.Dale.Plugin`; fixture emit option for assembly metadata; tests.
- `T-002` (`AC-PLUG-009.4`, `AC-PLUG-009.5`): `Compare`, `UnmetDaleSdkMinimum`, `PluginLoadContext.CheckLibraryDaleSdkMinimum`; tests.
- `T-003` (`AC-PLUG-002.1`, `AC-PLUG-002.7`): the `0.0.0` rule in the major gate, and the host-version seam both tests move to.
- `T-004` (`AC-INTRO-001.4`, `AC-INTRO-001.7`, `AC-INTRO-001.8`, `AC-INTRO-002.10`): the parser stamp and its validation; known-path parser fixtures; tests.
- `T-005` (`AC-INTRO-018.1`): the `AssemblyMetadata` mapping in `Vion.Dale.Sdk.targets`; its test.
- `T-006` (`SYS-API-002`, `SYS-API-003`): `Directory.Build.targets` condition; `scripts/validate-packages.ps1` and its self-test; the `package-validation` job; `docs/releasing.md`.
- `T-007`: distill every delta line, rewrite `introspection.md:46-47`, archive.
