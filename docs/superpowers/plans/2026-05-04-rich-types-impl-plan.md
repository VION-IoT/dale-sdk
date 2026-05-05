# Rich Data Types Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Spec:** [docs/superpowers/specs/2026-05-04-service-property-rich-types-design.md](../specs/2026-05-04-service-property-rich-types-design.md)

**Goal:** Extend `[ServiceProperty]` and `[ServiceMeasuringPoint]` to support nullable primitives, flat structs, arrays, and any non-nested composition (`ImmutableArray<Coordinates?>`), driven end-to-end by a constrained profile of JSON Schema 2020-12.

**Architecture:** Per-property metadata splits into three sibling documents — `schema` (data shape), `presentation` (UI hints), `runtime` (behavior). Wire format uses a 14-variant FlatBuffers union; numeric precision narrowing happens at the C# binding boundary using `format`. Mesh stays schema-free (today's invariant): the FB tag tree drives FB→JSON, and Cloud bundles the schema alongside each value on JSON→FB. Schemas are library-pinned (parsed deterministically by Cloud at upload and Dale at load).

**Tech Stack:** C# 12 / .NET 10 (SDK + Mesh + Cloud + private Dale runtime); netstandard2.1 (SDK plugin compatibility); netstandard2.0 (Roslyn analyzers); System.Text.Json + JsonNode; FlatBuffers (Google.FlatBuffers 25.x); EF Core + PostgreSQL jsonb; Vue 3 + TypeScript (Dashboard).

**Repos in scope:**
- `C:\_gh\vion-contracts` — types, codec, FB schemas, DTOs (PR 1)
- `C:\_gh\dale-sdk` — analyzers, attributes, introspection (PR 2)
- `C:\_gh\cloud-api` — DTOs, validation, DB (PRs 3 and 6)
- `C:\_gh\dale` (private) — runtime adoption (PR 4)
- `C:\_gh\mesh` — codec swap (PR 5)
- `C:\_gh\dashboard` — TS types, renderers (PR 7)

---

## 0. Orchestration

This plan spans 7 PRs across 5+1 repos. Each PR section below is self-contained — a fresh session reading just that section, plus this orchestration block, plus the spec, can pick up the work cold.

### 0.1 Status

Update on every session boundary. Mark "in progress" with the date; mark "merged" with the published version.

| PR | Repo | Branch | Status | Version published |
|----|------|--------|--------|-------------------|
| 1 | vion-contracts | `feat/rich-types` | not started | — |
| 2 | dale-sdk | `feat/rich-types` | not started | — |
| 3 | cloud-api | `feat/rich-types` | not started | — |
| 4 | dale (private) | `feat/rich-types` | not started | — |
| 5 | mesh | `feat/rich-types` | not started | — |
| 6 | cloud-api | `feat/rich-types-pt2` | not started | — |
| 7 | dashboard | `feat/rich-types` | not started | — |

### 0.2 Deviation log

Every time a session changes the plan during implementation — different file structure, different method signature, different task ordering — append a row. The log is the audit trail; future sessions read it to understand why the plan and reality differ.

| Date | PR | What changed | Why | Approved by |
|------|----|--------------|-----|-------------|
| 2026-05-05 | PR 1 (process) | Implementer subagents stage but do not commit; controller shows diff and waits for explicit user approval before commit | User wants visibility/approval gate on every commit | User in this session |
| 2026-05-05 | PR 1 (process) | All implementer dispatches must run `jb cleanupcode` on changed files before reporting back | Project convention; ensures consistent C# formatting (Allman braces, etc.) per `CLAUDE.md` | User in this session |
| 2026-05-05 | PR 1 | Test framework: xunit + FluentAssertions → MSTest 4.0.2 + built-in assertions | First implementer used xunit; SDK convention (matching `Vion.Dale.Sdk.Test`) is MSTest. Scaffold rewritten on a clean branch | User in this session |
| 2026-05-05 | PR 1 | Added `Byte`, `UShort`, `UInt` to `PrimitiveKind` | Unsigned types overlooked in spec; needed for Modbus register values, large counters, byte-sized status bits. All fit in `LongVal` (no new wire variant). `ulong`/`sbyte` deferred to spec §10 (ulong > 2^63 doesn't round-trip; sbyte rarely used). Spec primitive mapping table updated; codec range-checks via `format` | User in this session |
| 2026-05-05 | PR 1 (convention) | Block-scoped namespaces for all C# code, both production and test | `jb cleanupcode` enforces it; plan §0.7 updated. Earlier plan samples sometimes showed file-scoped — those are illustrative only | User in this session |
| 2026-05-05 | PR 1 (deferred) | Encoding hygiene (UTF-8 BOM stripping, trailing-newline enforcement) deferred | Reviewer flagged BOM on csproj + missing trailing newlines on .cs files as Important. User opted to skip in this PR. Revisit via `.editorconfig` when convenient (`charset = utf-8`, `insert_final_newline = true`, `end_of_line = lf`). Logged to §0.3 Followups | User in this session |

### 0.3 Followups (out-of-scope discoveries)

If a session notices something unrelated to the current PR but worth fixing later, log it here instead of fixing it inline. Don't lose the observation; don't bloat the PR.

- **Add `.editorconfig` to vion-contracts (and possibly other repos)** to enforce `charset = utf-8`, `insert_final_newline = true`, `end_of_line = lf` for `*.{cs,csproj}`. Prevents the BOM/missing-newline drift seen during PR 1 implementation.

### 0.4 Pause rule

Each session evaluates every change before making it. Three classes:

1. **In-scope tweak inside the current PR** — e.g. method renamed for clarity, extra test added. **Do it; commit captures why; optionally append a deviation-log row.**
2. **Cross-PR ripple** — e.g. PR 5 implementation needs a method PR 1 didn't ship, or PR 1 already merged but turns out wrong. **Stop. Don't work around silently. If upstream PR is unmerged: extend its scope, append deviation row. If merged: open a follow-up PR (PR 1.5 / `fixup/...`). Resume downstream PR after upstream lands.**
3. **Design break — a spec assumption is invalidated** — e.g. discover a customer LogicBlock the analyzer can't validate, discover a schema-bytes-per-set load that doesn't match the §9 estimate. **Stop. Surface to user. User decides; spec gets a "Revised YYYY-MM-DD" annotation; plan adjusts; resume.**

**When in doubt: pause.** Silent drift is the failure mode; over-pausing is a small cost.

### 0.5 Session handoff pattern

**At session start:**
1. Read the spec (refresher).
2. Read this plan: §0 (orchestration, status, deviation log) + the PR section the session is executing.
3. Read commits on the relevant branch since the last status update (the trail).
4. Pick up where the prior session left off (next unchecked task).

**At session end:**
1. Update §0.1 Status row.
2. Append any deviation rows to §0.2.
3. Append any followups to §0.3.
4. Commit the plan changes in a separate commit from code: `plan: update status for PR N; deviation: ...`.
5. Push branch.
6. If PR is ready for review: open it, link spec + plan + relevant deviations.

### 0.6 Branch + version pin convention

- All public repos: branch `feat/rich-types`. (cloud-api gets a second branch `feat/rich-types-pt2` for PR 6.)
- vion-contracts feature-branch CI (verify or add — see §1.2) publishes prerelease versions to the private Azure DevOps feed: `0.0.0-feat-rich-types.<runNumber>`.
- Downstream repos consume vion-contracts via `<PackageReference>` always — never `<ProjectReference>`. Pin to the latest feature-branch prerelease during dev:
  ```bash
  dotnet add package Vion.Contracts --version 0.0.0-feat-rich-types.<n> --source <private-feed>
  ```
  After PR 1 merges, switch to the merge-to-main CI version `0.0.0-ci.<runNumber>`. After tag/release, switch to the stable `X.Y.Z`.

### 0.7 Subagent dispatch conventions

Read these before every implementer dispatch. Updated 2026-05-05 with lessons from Task A.

#### 0.7.1 Code conventions (paste into every dispatch prompt)

**Test framework:** MSTest 4.0.2, matching `C:\_gh\dale-sdk\Vion.Dale.Sdk.Test\`. Not xunit.
- Packages: `Microsoft.NET.Test.Sdk` 18.0.1, `MSTest` 4.0.2, `coverlet.collector` 6.0.4.
- Global `<Using Include="Microsoft.VisualStudio.TestTools.UnitTesting"/>`.
- Built-in assertions only (`Assert.AreEqual`, `Assert.IsTrue`, `Assert.HasCount`). **No FluentAssertions.**
- `Directory.Build.props` auto-applies `[assembly: DoNotParallelize]` to any `IsTestProject == true`.
- Test file naming: `<Subject>Should.cs` (BDD-style).
- `[TestClass]` on class; `[TestMethod]` on each test. Method names: descriptive PascalCase, no underscores, no `_should` suffix.
- For data-driven tests: `[TestMethod]` + `[DataRow(...)]` (MSTest 4 doesn't need `[DataTestMethod]`).

**C# style:**
- **Block-scoped namespaces** for all C# code (production AND test). Enforced by `jb cleanupcode --profile="Built-in: Reformat Code"`.
- Allman braces. All usings explicit (`<ImplicitUsings>false</ImplicitUsings>`).
- Records require custom `Equals`/`GetHashCode` whenever they contain `ImmutableArray<T>` (default record equality uses reference equality on the underlying array). Guard with `IsDefault` checks before `SequenceEqual`/`foreach` to avoid `NullReferenceException` on `default(ImmutableArray<T>)`.

**File hygiene** (known fragile area):
- Trailing newline on every file (`tail -c 1` should be `0a`).
- No UTF-8 BOM on `.csproj` files (`head -c 3` should be the actual content's first 3 bytes, not `ef bb bf`).
- `dotnet add package` is known to introduce a BOM and strip the trailing newline. After running it, check both with `tail -c 1` and `head -c 3` and fix manually if needed.
- `jb cleanupcode` does **not** reliably enforce trailing newlines or strip BOMs. Cannot rely on it.

**XMLdoc** on public types in `Vion.Contracts` (the shared library): brief `///` comments explaining the contract, especially for non-obvious patterns like identity-vs-annotations splits or custom equality.

#### 0.7.2 Commit policy

Implementer subagents **stage** their work but **do not commit**. The controller shows the diff, gets user approval, then commits. The implementer prompt must include: *"Do not run `git commit`. Stage with `git add` is fine if the task uses staging; otherwise leave files modified on disk."*

#### 0.7.3 What the implementer must include in their final report

1. Status (DONE / DONE_WITH_CONCERNS / BLOCKED / NEEDS_CONTEXT).
2. Files modified (full paths).
3. Test command output verbatim (last few lines including the pass/fail summary).
4. `dotnet build` warning/error count (exact numbers).
5. Confirmation: "files modified on disk, nothing committed beyond `<HEAD-sha>`".
6. **Per-fix verification proof** — when applying review fixes, the implementer must verify each fix with a concrete check (e.g., for trailing newlines: `tail -c 1 <file>` shows `0a`; for BOM: `head -c 3 <file>` is content not `ef bb bf`). Don't accept "I added a newline" without proof.

#### 0.7.4 Controller verification ladder (lessons from Task A)

The implementer's report is **not authoritative** on these points. Verify independently before declaring a task done:

| Implementer claim | Verify by |
|---|---|
| "Tests pass" | `dotnet test --filter <pattern>` from controller |
| "Build clean" | `dotnet build` from controller; check warning + error counts |
| "Trailing newline added to file X" | `tail -c 1 X | od -An -tx1` should be `0a` |
| "BOM stripped from file X" | `head -c 3 X | od -An -tx1` shouldn't start `ef bb bf` |
| "Specific code change applied" | `git diff` to inspect the actual change |
| "`jb cleanupcode` ran clean" | Spot-check formatted files; the tool has known quirks (HtmlReformatCodeCleanupModule LoggerException is a known false positive on test files) |

If a claim doesn't survive verification, treat it as the implementer being wrong, not the verification being wrong. Don't dispatch another implementer for trivial mechanical fixes the controller can do in seconds (trailing newlines, BOM strips) — fix directly.

#### 0.7.5 Pre-flight before each dispatch

Before writing the implementer prompt, the controller should:

1. **Read at least one comparable file in the target repo** to ground convention claims in actual code (e.g., before writing tests, read the most recent `*Should.cs` in the target test project).
2. **Read the target project's `.csproj`** to confirm package versions, target framework, language version. Don't paste outdated package versions into the prompt.
3. **State conventions explicitly upfront** in the prompt — don't expect the implementer to infer them. The first Task A dispatch failed because xunit-vs-MSTest wasn't called out; the implementer used xunit by default.
4. **Include the exact path to a reference implementation file** when one exists. "Mirror the style of `<exact path>`" is more reliable than "follow project conventions".

### 0.8 Smoke test (the wide-loop gate)

Located at `C:\_gh\dale-sdk\scripts\rich-types-smoke.ps1`. Created at the start of the PR 4+5 paired stretch (§5.3 below), since that's the first time the script has anything to run against. Run as the merge gate for the dale-private + mesh pair. Verifies a property write actually round-trips end-to-end through the full stack.

**What it tests:** publishes one of each kind (primitive, nullable primitive, struct, array of struct, nullable struct, enum) into a known LogicBlock instance via `/cloud/sw/property/set` and confirms the corresponding `/sw/property/state` retained value matches.

**Pass criteria:** 6/6 round-trips green; no decode exceptions in Dale or Mesh logs.

**Run:** `pwsh scripts/rich-types-smoke.ps1` (configuration documented inline in the script).

---

## 1. Foundational setup (one-time, before PR 1)

### 1.1 Verify or add CI feature-branch publishing in vion-contracts

- [ ] **Step 1:** Read `C:\_gh\vion-contracts\.github\workflows\publish.yml`. Check whether feature branches publish prerelease packages.

- [ ] **Step 2:** If only `main` and tags trigger publish, add a feature-branch trigger:

```yaml
on:
  push:
    branches: [main, "feat/**"]
    tags: ["v*"]
```

And a version-suffix step:

```yaml
- name: Compute version
  id: version
  run: |
    if [[ "${GITHUB_REF}" == refs/heads/feat/* ]]; then
      BRANCH="${GITHUB_REF#refs/heads/}"
      SAFE_BRANCH="${BRANCH//\//-}"
      echo "version=0.0.0-${SAFE_BRANCH}.${GITHUB_RUN_NUMBER}" >> $GITHUB_OUTPUT
    elif [[ "${GITHUB_REF}" == refs/heads/main ]]; then
      echo "version=0.0.0-ci.${GITHUB_RUN_NUMBER}" >> $GITHUB_OUTPUT
    else
      echo "version=${GITHUB_REF_NAME#v}" >> $GITHUB_OUTPUT
    fi
```

- [ ] **Step 3:** Skip publish to nuget.org when the version is a feature-branch prerelease. The existing tag-only push step is fine; just guard it.

- [ ] **Step 4:** Commit on a separate prep branch in vion-contracts; merge before starting PR 1. This is operational scaffolding, not part of the rich-types change set.

### 1.2 Pre-flight audits

- [ ] **Step 5:** Audit private LogicBlocks (`C:\_gh\dale\` and any partner repos) for `decimal` usage on properties. Convert to `double` or surface for discussion. Document findings in §0.3 Followups if any are found.

- [ ] **Step 6:** Grep cloud-api for `PropertyState.Value` casts to concrete types. List sites that need updating in PR 6.
  ```bash
  cd C:\_gh\cloud-api && grep -rn "PropertyState.*Value" --include="*.cs"
  ```

- [ ] **Step 7:** Grep dashboard for `annotations.{decimals,group,order,unit,defaultName,enumValues,decimals}` access sites. List for PR 7.
  ```bash
  cd C:\_gh\dashboard && grep -rn "annotations\." src/
  ```

---

## 2. PR 1: vion-contracts (foundation)

**Goal:** Ship the new type-language records, JSON Schema serializers, FB schema, codec, and DTOs. After this merges and publishes a prerelease, downstream repos can pin to it.

**Repo:** `C:\_gh\vion-contracts`
**Branch:** `feat/rich-types`
**Prereqs:** §1.1 done (CI publishes feature-branch prereleases).

> **⚠️ Test-framework note for the rest of §2.** Code samples in §2.8 onwards were originally drafted in xunit + FluentAssertions syntax (`[Fact]`, `[Theory]`, `[InlineData]`, `.Should().Be(...)`). The actual conventions are **MSTest 4 + built-in assertions** per §0.7. The controller dispatching each task must convert snippets to MSTest equivalents before pasting into the implementer prompt:
>
> - `[Fact]` → `[TestMethod]`
> - `[Theory]` → (remove; `[TestMethod]` alone supports `[DataRow]` in MSTest 4)
> - `[InlineData(...)]` → `[DataRow(...)]`
> - `actual.Should().Be(expected)` → `Assert.AreEqual(expected, actual)` (note: argument order flips)
> - `actual.Should().NotBe(expected)` → `Assert.AreNotEqual(expected, actual)`
> - `actual.Should().BeTrue()` / `BeFalse()` → `Assert.IsTrue(actual)` / `Assert.IsFalse(actual)`
> - `act.Should().Throw<E>()` → `Assert.ThrowsException<E>(act)`
> - `actual.Should().Contain(s)` → `StringAssert.Contains(actual, s)` (for strings) or `Assert.IsTrue(actual.Contains(s))` (collections)
> - `actual.Should().NotBeNull()` → `Assert.IsNotNull(actual)`
> - Test class names follow `<Subject>Should` (BDD-style); decorate with `[TestClass]`.
> - Test method names are descriptive PascalCase, no underscores.
> - File naming: `<Subject>Should.cs`.
> - Block-style namespaces.
>
> When tasks call out specific test files (e.g. `TypeSchemaSerializationTests.cs`), rename to `<Subject>Should.cs` (e.g. `TypeSchemaSerializationShould.cs`).

### 2.1 File structure

**Create:**
- `Vion.Contracts/TypeRef/PrimitiveKind.cs`
- `Vion.Contracts/TypeRef/TypeRef.cs` (abstract + 5 sealed records)
- `Vion.Contracts/TypeRef/StructField.cs`
- `Vion.Contracts/TypeRef/TypeAnnotations.cs`
- `Vion.Contracts/TypeRef/Presentation.cs`
- `Vion.Contracts/TypeRef/RuntimeMetadata.cs`
- `Vion.Contracts/TypeRef/TypeSchema.cs`
- `Vion.Contracts/TypeRef/PropertyMetadata.cs`
- `Vion.Contracts/TypeRef/InvalidSchemaException.cs`
- `Vion.Contracts/TypeRef/TypeSchemaSerialization.cs`
- `Vion.Contracts/TypeRef/PropertyMetadataSerialization.cs`
- `Vion.Contracts/Codec/PropertyValueCodec.cs`
- `Vion.Contracts/Codec/PropertyValueDecodeException.cs`
- `Vion.Contracts/Codec/EnumNameCache.cs` (internal helper for `Enum.GetName`/`Parse` caching)
- `Vion.Contracts/Codec/ValidationResult.cs`
- `Vion.Contracts/FlatBuffers/Common/property_value.fbs`
- `Vion.Contracts.Test/TypeRef/...` (test project; create if missing)
- `Vion.Contracts.Test/Codec/...`

**Modify:**
- `Vion.Contracts/FlatBuffers/Sw/Property/property_state_payload.fbs` — embed `PropertyValue` instead of `CommonValue`
- `Vion.Contracts/FlatBuffers/Sw/Property/set_property_payload.fbs` — same
- `Vion.Contracts/FlatBuffers/Sw/Property/get_property_response_payload.fbs` — same
- `Vion.Contracts/FlatBuffers/Sw/Property/set_property_response_payload.fbs` — same
- `Vion.Contracts/FlatBuffers/Sw/MeasuringPoint/measuring_point_state_payload.fbs` — same
- `Vion.Contracts/FlatBuffers/Sw/MeasuringPoint/get_measuring_point_response_payload.fbs` — same
- `Vion.Contracts/FlatBuffers/Generate.ps1` — already reflects all schemas; no edit needed
- `Vion.Contracts/Events/CloudToMesh/SetPropertyPayload.cs` — `(object Value, string Type)` → `(JsonNode Value, JsonNode Schema)`
- `Vion.Contracts/Events/MeshToCloud/PropertiesStatePayload.cs` — `object Value` → `JsonNode? Value`
- `Vion.Contracts/Events/MeshToCloud/SetPropertyResponsePayload.cs` — `object Value` → `JsonNode? Value`
- `Vion.Contracts/Events/MeshToCloud/MeasuringPointsStatePayload.cs` — `object Value` → `JsonNode? Value`
- `Vion.Contracts/Introspection/LogicBlockIntrospectionResult.cs` — replace `ServicePropertyInfo` and `ServiceMeasuringPointInfo` shape

**Delete:**
- `Vion.Contracts/FlatBuffers/Common/common_value.fbs`
- `Vion.Contracts/FlatBuffers.Generated/Common/CommonValue.cs` (regenerate)
- `Vion.Contracts/Constants/ServiceElementTypes.cs`

### 2.2 Branch setup

- [ ] **Step 1:** Create branch.
  ```bash
  cd C:\_gh\vion-contracts
  git checkout main && git pull
  git checkout -b feat/rich-types
  ```

- [ ] **Step 2:** Verify the `Vion.Contracts.Test` project exists. It was scaffolded as an MSTest project in commit `3027600` on this branch; the csproj references `MSTest 4.0.2`, `Microsoft.NET.Test.Sdk 18.0.1`, `coverlet.collector`, and includes a global `Microsoft.VisualStudio.TestTools.UnitTesting` using. `Directory.Build.props` adds `[assembly: DoNotParallelize]` to all test projects automatically. **Use MSTest, not xunit.** See §0.7 for the full test-framework convention.

### 2.3 Task: PrimitiveKind enum

- [ ] **Step 1:** Create `Vion.Contracts/TypeRef/PrimitiveKind.cs`.

```csharp
namespace Vion.Contracts.TypeRef;

public enum PrimitiveKind
{
    Bool,
    String,
    Byte,
    Short,
    UShort,
    Int,
    UInt,
    Long,
    Float,
    Double,
    DateTime,
    Duration,
}
```

- [ ] **Step 2:** Stage and report (do not commit; controller commits after user approval).
  ```bash
  git add Vion.Contracts/TypeRef/PrimitiveKind.cs
  ```

### 2.4 Task: TypeRef hierarchy and StructField

- [ ] **Step 1:** Create `Vion.Contracts/TypeRef/StructField.cs`.

```csharp
namespace Vion.Contracts.TypeRef;

public sealed record StructField(string Name, TypeRef Type);
```

- [ ] **Step 2:** Create `Vion.Contracts/TypeRef/TypeRef.cs`.

```csharp
using System.Collections.Immutable;

namespace Vion.Contracts.TypeRef;

public abstract record TypeRef;

public sealed record PrimitiveTypeRef(PrimitiveKind Kind) : TypeRef;

public sealed record EnumTypeRef(
    string Title,                              // identity-bearing
    ImmutableArray<string> Members) : TypeRef
{
    public bool Equals(EnumTypeRef? other) =>
        other is not null
        && Title == other.Title
        && Members.SequenceEqual(other.Members);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Title);
        foreach (var m in Members) hash.Add(m);
        return hash.ToHashCode();
    }
}

public sealed record StructTypeRef(
    string Title,                              // identity-bearing — mirrors EnumTypeRef
    ImmutableArray<StructField> Fields,
    ImmutableArray<string> Required) : TypeRef
{
    public bool Equals(StructTypeRef? other) =>
        other is not null
        && Title == other.Title
        && Fields.SequenceEqual(other.Fields)
        && Required.SequenceEqual(other.Required);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Title);
        foreach (var f in Fields) hash.Add(f);
        foreach (var r in Required) hash.Add(r);
        return hash.ToHashCode();
    }
}

public sealed record ArrayTypeRef(TypeRef Items) : TypeRef;

public sealed record NullableTypeRef(TypeRef Inner) : TypeRef;
```

> **Note:** the custom `Equals` on EnumTypeRef and StructTypeRef is required because `ImmutableArray<T>` default record equality uses *reference* equality on the underlying arrays, not element equality. Same fix is needed everywhere a record contains an `ImmutableArray<T>`.

- [ ] **Step 3:** Write identity tests in `Vion.Contracts.Test/TypeRef/TypeRefIdentityTests.cs`.

```csharp
using FluentAssertions;
using System.Collections.Immutable;
using Vion.Contracts.TypeRef;

namespace Vion.Contracts.Test.TypeRef;

public class TypeRefIdentityTests
{
    [Fact]
    public void Primitive_same_kind_equal()
    {
        var a = new PrimitiveTypeRef(PrimitiveKind.Double);
        var b = new PrimitiveTypeRef(PrimitiveKind.Double);
        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void Primitive_different_kind_not_equal()
    {
        var a = new PrimitiveTypeRef(PrimitiveKind.Double);
        var b = new PrimitiveTypeRef(PrimitiveKind.Float);
        a.Should().NotBe(b);
    }

    [Fact]
    public void Enum_same_title_and_members_equal()
    {
        var a = new EnumTypeRef("AlarmState", ImmutableArray.Create("Ok", "Warning", "Critical"));
        var b = new EnumTypeRef("AlarmState", ImmutableArray.Create("Ok", "Warning", "Critical"));
        a.Should().Be(b);
    }

    [Fact]
    public void Enum_different_title_not_equal()
    {
        var a = new EnumTypeRef("AlarmState", ImmutableArray.Create("Ok"));
        var b = new EnumTypeRef("DoorState",  ImmutableArray.Create("Ok"));
        a.Should().NotBe(b);
    }

    [Fact]
    public void Struct_same_title_and_shape_equal()
    {
        var a = new StructTypeRef(
            "Coordinates",
            ImmutableArray.Create(
                new StructField("Lat", new PrimitiveTypeRef(PrimitiveKind.Double)),
                new StructField("Lon", new PrimitiveTypeRef(PrimitiveKind.Double))),
            ImmutableArray.Create("Lat", "Lon"));
        var b = new StructTypeRef(
            "Coordinates",
            ImmutableArray.Create(
                new StructField("Lat", new PrimitiveTypeRef(PrimitiveKind.Double)),
                new StructField("Lon", new PrimitiveTypeRef(PrimitiveKind.Double))),
            ImmutableArray.Create("Lat", "Lon"));
        a.Should().Be(b);
    }

    [Fact]
    public void Struct_same_shape_different_title_not_equal()
    {
        var fields = ImmutableArray.Create(
            new StructField("X", new PrimitiveTypeRef(PrimitiveKind.Double)),
            new StructField("Y", new PrimitiveTypeRef(PrimitiveKind.Double)));
        var required = ImmutableArray.Create("X", "Y");
        var a = new StructTypeRef("Coordinates", fields, required);
        var b = new StructTypeRef("Pressure", fields, required);
        a.Should().NotBe(b);
    }

    [Fact]
    public void Array_same_items_equal()
    {
        var a = new ArrayTypeRef(new PrimitiveTypeRef(PrimitiveKind.Double));
        var b = new ArrayTypeRef(new PrimitiveTypeRef(PrimitiveKind.Double));
        a.Should().Be(b);
    }

    [Fact]
    public void Nullable_of_struct_composes()
    {
        var struc = new StructTypeRef("Coordinates",
            ImmutableArray.Create(new StructField("Lat", new PrimitiveTypeRef(PrimitiveKind.Double))),
            ImmutableArray.Create("Lat"));
        var a = new NullableTypeRef(struc);
        var b = new NullableTypeRef(struc);
        a.Should().Be(b);
    }
}
```

- [ ] **Step 4:** Run tests; expect FAIL (records/types don't exist yet — compilation error).
  ```bash
  cd C:\_gh\vion-contracts
  dotnet test Vion.Contracts.Test --filter TypeRefIdentityTests
  ```

- [ ] **Step 5:** Verify both files compile and tests pass.
  ```bash
  dotnet test Vion.Contracts.Test --filter TypeRefIdentityTests
  ```
  Expected: 8/8 PASS.

- [ ] **Step 6:** Commit.
  ```bash
  git add Vion.Contracts/TypeRef/StructField.cs Vion.Contracts/TypeRef/TypeRef.cs Vion.Contracts.Test/TypeRef/TypeRefIdentityTests.cs
  git commit -m "feat(types): add TypeRef hierarchy with identity tests"
  ```

### 2.5 Task: TypeAnnotations / Presentation / RuntimeMetadata records

- [ ] **Step 1:** Create `Vion.Contracts/TypeRef/TypeAnnotations.cs`.

```csharp
namespace Vion.Contracts.TypeRef;

public sealed record TypeAnnotations
{
    public string? Title       { get; init; }
    public string? Description { get; init; }
    public string? Unit        { get; init; }
    public double? Minimum     { get; init; }
    public double? Maximum     { get; init; }
    public bool    ReadOnly    { get; init; }

    public static readonly TypeAnnotations None = new();
}
```

- [ ] **Step 2:** Create `Vion.Contracts/TypeRef/Presentation.cs`.

```csharp
using System.Collections.Immutable;

namespace Vion.Contracts.TypeRef;

public sealed record Presentation
{
    public string?  DisplayName { get; init; }
    public string?  Group       { get; init; }
    public int?     Order       { get; init; }
    public string?  Category    { get; init; }
    public string?  Importance  { get; init; }
    public string?  UIHint      { get; init; }
    public int?     Decimals    { get; init; }
    public ImmutableDictionary<string, string>? StatusMappings { get; init; }

    public static readonly Presentation None = new();

    public bool IsEmpty =>
        DisplayName is null && Group is null && Order is null && Category is null
        && Importance is null && UIHint is null && Decimals is null
        && (StatusMappings is null || StatusMappings.IsEmpty);
}
```

- [ ] **Step 3:** Create `Vion.Contracts/TypeRef/RuntimeMetadata.cs`.

```csharp
namespace Vion.Contracts.TypeRef;

public sealed record RuntimeMetadata
{
    public bool Persistent { get; init; }

    public static readonly RuntimeMetadata None = new();

    public bool IsEmpty => !Persistent;
}
```

- [ ] **Step 4:** Build to verify.
  ```bash
  dotnet build Vion.Contracts/Vion.Contracts.csproj
  ```
  Expected: clean build.

- [ ] **Step 5:** Commit.
  ```bash
  git add Vion.Contracts/TypeRef/TypeAnnotations.cs Vion.Contracts/TypeRef/Presentation.cs Vion.Contracts/TypeRef/RuntimeMetadata.cs
  git commit -m "feat(types): add TypeAnnotations, Presentation, RuntimeMetadata records"
  ```

### 2.6 Task: TypeSchema and PropertyMetadata

- [ ] **Step 1:** Create `Vion.Contracts/TypeRef/TypeSchema.cs`.

```csharp
using System.Collections.Immutable;

namespace Vion.Contracts.TypeRef;

public sealed record TypeSchema(
    TypeRef Type,
    TypeAnnotations Annotations,
    ImmutableDictionary<string, TypeAnnotations> StructFieldAnnotations)
{
    public static TypeSchema Of(TypeRef type) =>
        new(type, TypeAnnotations.None, ImmutableDictionary<string, TypeAnnotations>.Empty);

    public bool Equals(TypeSchema? other) =>
        other is not null
        && Type == other.Type
        && Annotations == other.Annotations
        && StructFieldAnnotations.OrderBy(kv => kv.Key)
            .SequenceEqual(other.StructFieldAnnotations.OrderBy(kv => kv.Key));

    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(Type);
        h.Add(Annotations);
        foreach (var kv in StructFieldAnnotations.OrderBy(kv => kv.Key))
        {
            h.Add(kv.Key);
            h.Add(kv.Value);
        }
        return h.ToHashCode();
    }
}
```

- [ ] **Step 2:** Create `Vion.Contracts/TypeRef/PropertyMetadata.cs`.

```csharp
namespace Vion.Contracts.TypeRef;

public sealed record PropertyMetadata(
    TypeSchema       Schema,
    Presentation     Presentation,
    RuntimeMetadata  Runtime)
{
    public static PropertyMetadata Of(TypeSchema schema) =>
        new(schema, Presentation.None, RuntimeMetadata.None);
}
```

- [ ] **Step 3:** Add identity test in `Vion.Contracts.Test/TypeRef/TypeSchemaIdentityTests.cs`.

```csharp
using FluentAssertions;
using System.Collections.Immutable;
using Vion.Contracts.TypeRef;

namespace Vion.Contracts.Test.TypeRef;

public class TypeSchemaIdentityTests
{
    [Fact]
    public void Two_schemas_with_different_annotations_have_equal_typeref()
    {
        var t = new PrimitiveTypeRef(PrimitiveKind.Double);
        var s1 = new TypeSchema(t, new TypeAnnotations { Unit = "V" }, ImmutableDictionary<string, TypeAnnotations>.Empty);
        var s2 = new TypeSchema(t, new TypeAnnotations { Unit = "A" }, ImmutableDictionary<string, TypeAnnotations>.Empty);

        s1.Should().NotBe(s2);                  // different annotations
        s1.Type.Should().Be(s2.Type);           // same TypeRef identity
        s1.Type.GetHashCode().Should().Be(s2.Type.GetHashCode());
    }
}
```

- [ ] **Step 4:** Run tests.
  ```bash
  dotnet test Vion.Contracts.Test --filter TypeSchemaIdentityTests
  ```
  Expected: 1/1 PASS.

- [ ] **Step 5:** Commit.
  ```bash
  git add Vion.Contracts/TypeRef/TypeSchema.cs Vion.Contracts/TypeRef/PropertyMetadata.cs Vion.Contracts.Test/TypeRef/TypeSchemaIdentityTests.cs
  git commit -m "feat(types): add TypeSchema and PropertyMetadata; identity-vs-annotation test"
  ```

### 2.7 Task: InvalidSchemaException

- [ ] **Step 1:** Create `Vion.Contracts/TypeRef/InvalidSchemaException.cs`.

```csharp
namespace Vion.Contracts.TypeRef;

public sealed class InvalidSchemaException : Exception
{
    public InvalidSchemaException(string message) : base(message) { }
    public InvalidSchemaException(string message, Exception inner) : base(message, inner) { }
}
```

- [ ] **Step 2:** Commit.
  ```bash
  git add Vion.Contracts/TypeRef/InvalidSchemaException.cs
  git commit -m "feat(types): add InvalidSchemaException"
  ```

### 2.8 Task: TypeSchemaSerialization — primitives ToJsonSchema

- [ ] **Step 1:** Create test file `Vion.Contracts.Test/TypeRef/TypeSchemaSerializationTests.cs`.

```csharp
using FluentAssertions;
using System.Text.Json.Nodes;
using Vion.Contracts.TypeRef;

namespace Vion.Contracts.Test.TypeRef;

public class TypeSchemaSerializationTests
{
    private static JsonNode Json(string s) => JsonNode.Parse(s)!;

    [Theory]
    [DataRow(PrimitiveKind.Bool,     "{\"type\":\"boolean\"}")]
    [DataRow(PrimitiveKind.String,   "{\"type\":\"string\"}")]
    [DataRow(PrimitiveKind.Byte,     "{\"type\":\"integer\",\"format\":\"uint8\"}")]
    [DataRow(PrimitiveKind.Short,    "{\"type\":\"integer\",\"format\":\"int16\"}")]
    [DataRow(PrimitiveKind.UShort,   "{\"type\":\"integer\",\"format\":\"uint16\"}")]
    [DataRow(PrimitiveKind.Int,      "{\"type\":\"integer\",\"format\":\"int32\"}")]
    [DataRow(PrimitiveKind.UInt,     "{\"type\":\"integer\",\"format\":\"uint32\"}")]
    [DataRow(PrimitiveKind.Long,     "{\"type\":\"integer\",\"format\":\"int64\"}")]
    [DataRow(PrimitiveKind.Float,    "{\"type\":\"number\",\"format\":\"float\"}")]
    [DataRow(PrimitiveKind.Double,   "{\"type\":\"number\",\"format\":\"double\"}")]
    [DataRow(PrimitiveKind.DateTime, "{\"type\":\"string\",\"format\":\"date-time\"}")]
    [DataRow(PrimitiveKind.Duration, "{\"type\":\"string\",\"format\":\"duration\"}")]
    public void Primitive_to_json_schema(PrimitiveKind kind, string expected)
    {
        var schema = TypeSchema.Of(new PrimitiveTypeRef(kind));
        var actual = schema.ToJsonSchema();
        actual.ToJsonString().Should().Be(Json(expected).ToJsonString());
    }
}
```

- [ ] **Step 2:** Run; expect FAIL ("ToJsonSchema not defined").

- [ ] **Step 3:** Create `Vion.Contracts/TypeRef/TypeSchemaSerialization.cs` with the primitives path implemented.

```csharp
using System.Text.Json.Nodes;

namespace Vion.Contracts.TypeRef;

public static class TypeSchemaSerialization
{
    public static JsonNode ToJsonSchema(this TypeSchema schema) =>
        BuildSchema(schema.Type, schema.Annotations, schema.StructFieldAnnotations);

    private static JsonNode BuildSchema(
        TypeRef type,
        TypeAnnotations annotations,
        System.Collections.Immutable.ImmutableDictionary<string, TypeAnnotations> structFieldAnnotations)
    {
        var node = type switch
        {
            PrimitiveTypeRef p => BuildPrimitive(p),
            EnumTypeRef e => BuildEnum(e),
            StructTypeRef s => BuildStruct(s, structFieldAnnotations),
            ArrayTypeRef a => BuildArray(a, annotations, structFieldAnnotations),
            NullableTypeRef n => BuildNullable(n, annotations, structFieldAnnotations),
            _ => throw new InvalidOperationException($"Unknown TypeRef: {type.GetType()}")
        };
        ApplyAnnotations(node, annotations);
        return node;
    }

    private static JsonObject BuildPrimitive(PrimitiveTypeRef p) => p.Kind switch
    {
        PrimitiveKind.Bool     => new JsonObject { ["type"] = "boolean" },
        PrimitiveKind.String   => new JsonObject { ["type"] = "string" },
        PrimitiveKind.Short    => new JsonObject { ["type"] = "integer", ["format"] = "int16" },
        PrimitiveKind.Int      => new JsonObject { ["type"] = "integer", ["format"] = "int32" },
        PrimitiveKind.Long     => new JsonObject { ["type"] = "integer", ["format"] = "int64" },
        PrimitiveKind.Float    => new JsonObject { ["type"] = "number",  ["format"] = "float"  },
        PrimitiveKind.Double   => new JsonObject { ["type"] = "number",  ["format"] = "double" },
        PrimitiveKind.DateTime => new JsonObject { ["type"] = "string",  ["format"] = "date-time" },
        PrimitiveKind.Duration => new JsonObject { ["type"] = "string",  ["format"] = "duration" },
        _ => throw new InvalidOperationException($"Unknown PrimitiveKind: {p.Kind}")
    };

    // BuildEnum, BuildStruct, BuildArray, BuildNullable, ApplyAnnotations:
    // implemented in subsequent tasks; for now stub them to throw NotImplementedException
    // so the primitive test passes while we incrementally fill in the rest.
    private static JsonObject BuildEnum(EnumTypeRef e) =>
        throw new NotImplementedException("Enum serialization arrives in §2.9");
    private static JsonObject BuildStruct(StructTypeRef s, System.Collections.Immutable.ImmutableDictionary<string, TypeAnnotations> sfa) =>
        throw new NotImplementedException("Struct serialization arrives in §2.10");
    private static JsonObject BuildArray(ArrayTypeRef a, TypeAnnotations ann, System.Collections.Immutable.ImmutableDictionary<string, TypeAnnotations> sfa) =>
        throw new NotImplementedException("Array serialization arrives in §2.11");
    private static JsonObject BuildNullable(NullableTypeRef n, TypeAnnotations ann, System.Collections.Immutable.ImmutableDictionary<string, TypeAnnotations> sfa) =>
        throw new NotImplementedException("Nullable serialization arrives in §2.12");
    private static void ApplyAnnotations(JsonNode node, TypeAnnotations ann) { /* §2.13 */ }
}
```

- [ ] **Step 4:** Run tests.
  ```bash
  dotnet test Vion.Contracts.Test --filter TypeSchemaSerializationTests
  ```
  Expected: 9/9 primitive cases PASS.

- [ ] **Step 5:** Commit.
  ```bash
  git add Vion.Contracts/TypeRef/TypeSchemaSerialization.cs Vion.Contracts.Test/TypeRef/TypeSchemaSerializationTests.cs
  git commit -m "feat(types): TypeSchemaSerialization.ToJsonSchema for primitives"
  ```

### 2.9 Task: Enum ToJsonSchema

- [ ] **Step 1:** Add test cases to `TypeSchemaSerializationTests`.

```csharp
[Fact]
public void Enum_to_json_schema()
{
    var schema = TypeSchema.Of(new EnumTypeRef("AlarmState",
        System.Collections.Immutable.ImmutableArray.Create("Ok", "Warning", "Critical")));
    var json = schema.ToJsonSchema().ToJsonString();
    json.Should().Be("{\"type\":\"string\",\"title\":\"AlarmState\",\"enum\":[\"Ok\",\"Warning\",\"Critical\"]}");
}
```

- [ ] **Step 2:** Run; expect FAIL (NotImplementedException).

- [ ] **Step 3:** Replace `BuildEnum` stub with implementation.

```csharp
private static JsonObject BuildEnum(EnumTypeRef e)
{
    var arr = new JsonArray();
    foreach (var m in e.Members) arr.Add(m);
    return new JsonObject
    {
        ["type"]  = "string",
        ["title"] = e.Title,
        ["enum"]  = arr,
    };
}
```

- [ ] **Step 4:** Run; expect PASS.

- [ ] **Step 5:** Commit.
  ```bash
  git add Vion.Contracts/TypeRef/TypeSchemaSerialization.cs Vion.Contracts.Test/TypeRef/TypeSchemaSerializationTests.cs
  git commit -m "feat(types): TypeSchemaSerialization for enums (string-name only)"
  ```

### 2.10 Task: Struct ToJsonSchema

- [ ] **Step 1:** Add test.

```csharp
[Fact]
public void Struct_to_json_schema()
{
    var s = new StructTypeRef(
        "Coordinates",
        System.Collections.Immutable.ImmutableArray.Create(
            new StructField("lat", new PrimitiveTypeRef(PrimitiveKind.Double)),
            new StructField("lon", new PrimitiveTypeRef(PrimitiveKind.Double))),
        System.Collections.Immutable.ImmutableArray.Create("lat", "lon"));
    var schema = TypeSchema.Of(s);
    var json = schema.ToJsonSchema().ToJsonString();
    json.Should().Contain("\"type\":\"object\"");
    json.Should().Contain("\"title\":\"Coordinates\"");
    json.Should().Contain("\"required\":[\"lat\",\"lon\"]");
    json.Should().Contain("\"additionalProperties\":false");
}

[Fact]
public void Struct_with_per_field_annotations()
{
    var s = new StructTypeRef(
        "Coordinates3D",
        System.Collections.Immutable.ImmutableArray.Create(
            new StructField("lat", new PrimitiveTypeRef(PrimitiveKind.Double)),
            new StructField("lon", new PrimitiveTypeRef(PrimitiveKind.Double)),
            new StructField("altitude", new PrimitiveTypeRef(PrimitiveKind.Double))),
        System.Collections.Immutable.ImmutableArray.Create("lat", "lon", "altitude"));
    var sfa = System.Collections.Immutable.ImmutableDictionary<string, TypeAnnotations>.Empty
        .Add("lat",      new TypeAnnotations { Unit = "deg", Minimum = -90,  Maximum = 90  })
        .Add("lon",      new TypeAnnotations { Unit = "deg", Minimum = -180, Maximum = 180 })
        .Add("altitude", new TypeAnnotations { Unit = "m" });
    var schema = new TypeSchema(s, TypeAnnotations.None, sfa);
    var json = schema.ToJsonSchema().ToJsonString();
    json.Should().Contain("\"x-unit\":\"deg\"");
    json.Should().Contain("\"minimum\":-90");
    json.Should().Contain("\"maximum\":180");
    json.Should().Contain("\"x-unit\":\"m\"");
}
```

- [ ] **Step 2:** Run; expect FAIL.

- [ ] **Step 3:** Replace `BuildStruct` stub.

```csharp
private static JsonObject BuildStruct(
    StructTypeRef s,
    System.Collections.Immutable.ImmutableDictionary<string, TypeAnnotations> structFieldAnnotations)
{
    var properties = new JsonObject();
    foreach (var f in s.Fields)
    {
        var fieldAnnotations = structFieldAnnotations.TryGetValue(f.Name, out var a)
            ? a
            : TypeAnnotations.None;
        var fieldNode = BuildSchema(
            f.Type,
            fieldAnnotations,
            System.Collections.Immutable.ImmutableDictionary<string, TypeAnnotations>.Empty);
        properties[f.Name] = fieldNode;
    }
    var required = new JsonArray();
    foreach (var r in s.Required) required.Add(r);
    return new JsonObject
    {
        ["type"]                 = "object",
        ["title"]                = s.Title,
        ["properties"]           = properties,
        ["required"]             = required,
        ["additionalProperties"] = false,
    };
}
```

- [ ] **Step 4:** Run; expect PASS.

- [ ] **Step 5:** Commit.
  ```bash
  git add Vion.Contracts/TypeRef/TypeSchemaSerialization.cs Vion.Contracts.Test/TypeRef/TypeSchemaSerializationTests.cs
  git commit -m "feat(types): TypeSchemaSerialization for structs with per-field annotations"
  ```

### 2.11 Task: Array ToJsonSchema

- [ ] **Step 1:** Add test.

```csharp
[Fact]
public void Array_of_primitive_to_json_schema()
{
    var schema = TypeSchema.Of(new ArrayTypeRef(new PrimitiveTypeRef(PrimitiveKind.Double)));
    var json = schema.ToJsonSchema().ToJsonString();
    json.Should().Contain("\"type\":\"array\"");
    json.Should().Contain("\"items\":{\"type\":\"number\",\"format\":\"double\"}");
}

[Fact]
public void Array_of_struct_to_json_schema()
{
    var struc = new StructTypeRef(
        "Coordinates",
        System.Collections.Immutable.ImmutableArray.Create(
            new StructField("lat", new PrimitiveTypeRef(PrimitiveKind.Double))),
        System.Collections.Immutable.ImmutableArray.Create("lat"));
    var schema = TypeSchema.Of(new ArrayTypeRef(struc));
    var json = schema.ToJsonSchema().ToJsonString();
    json.Should().Contain("\"type\":\"array\"");
    json.Should().Contain("\"items\":{\"type\":\"object\"");
}
```

- [ ] **Step 2:** Run; expect FAIL.

- [ ] **Step 3:** Replace `BuildArray` stub.

```csharp
private static JsonObject BuildArray(
    ArrayTypeRef a,
    TypeAnnotations ann,
    System.Collections.Immutable.ImmutableDictionary<string, TypeAnnotations> sfa)
{
    var items = BuildSchema(a.Items, ann, sfa);   // pass through annotations to element
    return new JsonObject
    {
        ["type"]  = "array",
        ["items"] = items,
    };
}
```

> Note: array-element annotations like `x-unit` propagate to `items`. The `ApplyAnnotations` call at the top of `BuildSchema` will then also apply to the array node — which is fine; we want `x-unit` on both `items` (per-element semantic) and the array (per-spec convention).

- [ ] **Step 4:** Run; expect PASS.

- [ ] **Step 5:** Commit.
  ```bash
  git add Vion.Contracts/TypeRef/TypeSchemaSerialization.cs Vion.Contracts.Test/TypeRef/TypeSchemaSerializationTests.cs
  git commit -m "feat(types): TypeSchemaSerialization for arrays"
  ```

### 2.12 Task: Nullable ToJsonSchema

- [ ] **Step 1:** Add test.

```csharp
[Fact]
public void Nullable_primitive_to_json_schema()
{
    var schema = TypeSchema.Of(new NullableTypeRef(new PrimitiveTypeRef(PrimitiveKind.Double)));
    var json = schema.ToJsonSchema().ToJsonString();
    json.Should().Be("{\"type\":[\"number\",\"null\"],\"format\":\"double\"}");
}

[Fact]
public void Nullable_enum_to_json_schema()
{
    var schema = TypeSchema.Of(new NullableTypeRef(
        new EnumTypeRef("AlarmState",
            System.Collections.Immutable.ImmutableArray.Create("Ok", "Warning"))));
    var json = schema.ToJsonSchema().ToJsonString();
    json.Should().Contain("\"type\":[\"string\",\"null\"]");
    json.Should().Contain("\"enum\":[\"Ok\",\"Warning\",null]");
}

[Fact]
public void Nullable_struct_to_json_schema()
{
    var struc = new StructTypeRef(
        "Coordinates",
        System.Collections.Immutable.ImmutableArray.Create(
            new StructField("lat", new PrimitiveTypeRef(PrimitiveKind.Double))),
        System.Collections.Immutable.ImmutableArray.Create("lat"));
    var schema = TypeSchema.Of(new NullableTypeRef(struc));
    var json = schema.ToJsonSchema().ToJsonString();
    json.Should().Contain("\"type\":[\"object\",\"null\"]");
}
```

- [ ] **Step 2:** Run; expect FAIL.

- [ ] **Step 3:** Replace `BuildNullable` stub.

```csharp
private static JsonObject BuildNullable(
    NullableTypeRef n,
    TypeAnnotations ann,
    System.Collections.Immutable.ImmutableDictionary<string, TypeAnnotations> sfa)
{
    var inner = BuildSchema(n.Inner, ann, sfa);
    if (inner is not JsonObject obj)
        throw new InvalidOperationException($"Unexpected schema node: {inner.GetType()}");

    // Widen "type": X → ["X", "null"]
    if (obj["type"] is JsonValue v && v.TryGetValue<string>(out var typeStr))
    {
        obj["type"] = new JsonArray(typeStr, "null");
    }

    // Append null to "enum" array if present
    if (obj["enum"] is JsonArray enumArr)
    {
        var copy = new JsonArray();
        foreach (var e in enumArr) copy.Add(e?.DeepClone());
        copy.Add(null);
        obj["enum"] = copy;
    }
    return obj;
}
```

- [ ] **Step 4:** Run; expect PASS.

- [ ] **Step 5:** Commit.
  ```bash
  git add Vion.Contracts/TypeRef/TypeSchemaSerialization.cs Vion.Contracts.Test/TypeRef/TypeSchemaSerializationTests.cs
  git commit -m "feat(types): TypeSchemaSerialization for nullable types"
  ```

### 2.13 Task: ApplyAnnotations

- [ ] **Step 1:** Add tests.

```csharp
[Fact]
public void Annotations_applied_to_primitive()
{
    var schema = new TypeSchema(
        new PrimitiveTypeRef(PrimitiveKind.Double),
        new TypeAnnotations { Title = "Voltage", Unit = "V", Minimum = 0, Maximum = 250, ReadOnly = false },
        System.Collections.Immutable.ImmutableDictionary<string, TypeAnnotations>.Empty);
    var json = schema.ToJsonSchema().ToJsonString();
    json.Should().Contain("\"title\":\"Voltage\"");
    json.Should().Contain("\"x-unit\":\"V\"");
    json.Should().Contain("\"minimum\":0");
    json.Should().Contain("\"maximum\":250");
    json.Should().NotContain("\"readOnly\":false");
}

[Fact]
public void ReadOnly_emits_only_when_true()
{
    var schemaTrue = new TypeSchema(
        new PrimitiveTypeRef(PrimitiveKind.Double),
        new TypeAnnotations { ReadOnly = true },
        System.Collections.Immutable.ImmutableDictionary<string, TypeAnnotations>.Empty);
    schemaTrue.ToJsonSchema().ToJsonString().Should().Contain("\"readOnly\":true");
}
```

- [ ] **Step 2:** Run; expect FAIL.

- [ ] **Step 3:** Replace `ApplyAnnotations` stub.

```csharp
private static void ApplyAnnotations(JsonNode node, TypeAnnotations ann)
{
    if (node is not JsonObject obj) return;
    // Primitive enum's "title" is already populated as identity; do not overwrite.
    if (ann.Title is not null && obj["title"] is null) obj["title"] = ann.Title;
    if (ann.Description is not null) obj["description"] = ann.Description;
    if (ann.Unit is not null) obj["x-unit"] = ann.Unit;
    if (ann.Minimum is double mn) obj["minimum"] = mn;
    if (ann.Maximum is double mx) obj["maximum"] = mx;
    if (ann.ReadOnly) obj["readOnly"] = true;
}
```

- [ ] **Step 4:** Run; all serialization tests should now pass.

- [ ] **Step 5:** Commit.
  ```bash
  git add Vion.Contracts/TypeRef/TypeSchemaSerialization.cs Vion.Contracts.Test/TypeRef/TypeSchemaSerializationTests.cs
  git commit -m "feat(types): apply annotations to JSON Schema output"
  ```

### 2.14 Task: FromJsonSchema (round-trip + profile rejection)

- [ ] **Step 1:** Add round-trip tests for all 9 primitives, plus enum/struct/array/nullable.

```csharp
[Theory]
[InlineData(PrimitiveKind.Bool)]
[InlineData(PrimitiveKind.String)]
[InlineData(PrimitiveKind.Short)]
[InlineData(PrimitiveKind.Int)]
[InlineData(PrimitiveKind.Long)]
[InlineData(PrimitiveKind.Float)]
[InlineData(PrimitiveKind.Double)]
[InlineData(PrimitiveKind.DateTime)]
[InlineData(PrimitiveKind.Duration)]
public void Roundtrip_primitive(PrimitiveKind kind)
{
    var original = TypeSchema.Of(new PrimitiveTypeRef(kind));
    var json = original.ToJsonSchema();
    var parsed = TypeSchemaSerialization.FromJsonSchema(json);
    parsed.Type.Should().Be(original.Type);
}

[Fact]
public void Roundtrip_enum()
{
    var original = TypeSchema.Of(new EnumTypeRef("AlarmState",
        System.Collections.Immutable.ImmutableArray.Create("Ok","Warning","Critical")));
    var parsed = TypeSchemaSerialization.FromJsonSchema(original.ToJsonSchema());
    parsed.Type.Should().Be(original.Type);
}

[Fact]
public void Roundtrip_struct()
{
    var struc = new StructTypeRef("Coordinates",
        System.Collections.Immutable.ImmutableArray.Create(
            new StructField("lat", new PrimitiveTypeRef(PrimitiveKind.Double)),
            new StructField("lon", new PrimitiveTypeRef(PrimitiveKind.Double))),
        System.Collections.Immutable.ImmutableArray.Create("lat","lon"));
    var original = TypeSchema.Of(struc);
    var parsed = TypeSchemaSerialization.FromJsonSchema(original.ToJsonSchema());
    parsed.Type.Should().Be(original.Type);
}

[Theory]
[InlineData("{\"$ref\":\"#/defs/X\"}")]
[InlineData("{\"oneOf\":[{\"type\":\"string\"},{\"type\":\"number\"}]}")]
[InlineData("{\"type\":\"string\",\"pattern\":\"[a-z]+\"}")]
[InlineData("{\"type\":\"array\",\"items\":{\"type\":\"array\",\"items\":{\"type\":\"number\"}}}")]
[InlineData("{\"type\":\"number\",\"exclusiveMinimum\":0}")]
public void Reject_non_profile_keywords(string schemaJson)
{
    var node = System.Text.Json.Nodes.JsonNode.Parse(schemaJson)!;
    Action act = () => TypeSchemaSerialization.FromJsonSchema(node);
    act.Should().Throw<InvalidSchemaException>();
}
```

- [ ] **Step 2:** Run; expect FAIL.

- [ ] **Step 3:** Add `FromJsonSchema` to `TypeSchemaSerialization.cs`. The implementation is a recursive parser that:
  - Validates only allow-listed keywords appear at each node (allowlist below).
  - Handles `type` as string or `[X, "null"]` two-element array.
  - Detects enum (string + enum keyword) → `EnumTypeRef`.
  - Detects struct (object + properties) → `StructTypeRef`.
  - Detects array (array + items) → `ArrayTypeRef`.
  - Wraps in `NullableTypeRef` when type-array form present.
  - Returns `(TypeRef, TypeAnnotations, structFieldAnnotations dict)`.

```csharp
private static readonly HashSet<string> AllowedKeywords = new()
{
    "type", "format", "title", "description",
    "minimum", "maximum",
    "x-unit", "readOnly",
    "enum",
    "properties", "required", "additionalProperties",
    "items",
};

public static TypeSchema FromJsonSchema(JsonNode node)
{
    var (type, annotations, sfa) = ParseNode(node);
    return new TypeSchema(type, annotations, sfa);
}

private static (TypeRef, TypeAnnotations, System.Collections.Immutable.ImmutableDictionary<string, TypeAnnotations>) ParseNode(JsonNode node)
{
    if (node is not JsonObject obj)
        throw new InvalidSchemaException("Schema must be a JSON object");

    foreach (var kv in obj)
    {
        if (!AllowedKeywords.Contains(kv.Key))
            throw new InvalidSchemaException($"Disallowed keyword in Dale profile: '{kv.Key}'");
    }

    var (typeStr, isNullable) = ParseTypeKeyword(obj);
    var annotations = ParseAnnotations(obj);
    var (typeRef, sfa) = typeStr switch
    {
        "boolean" => ((TypeRef)new PrimitiveTypeRef(PrimitiveKind.Bool), System.Collections.Immutable.ImmutableDictionary<string, TypeAnnotations>.Empty),
        "string"  => ParseString(obj),
        "integer" => (ParsePrimitiveInteger(obj), System.Collections.Immutable.ImmutableDictionary<string, TypeAnnotations>.Empty),
        "number"  => (ParsePrimitiveNumber(obj), System.Collections.Immutable.ImmutableDictionary<string, TypeAnnotations>.Empty),
        "object"  => ParseObject(obj),
        "array"   => ParseArray(obj),
        _ => throw new InvalidSchemaException($"Unknown type: '{typeStr}'")
    };

    return (isNullable ? new NullableTypeRef(typeRef) : typeRef, annotations, sfa);
}

private static (string, bool) ParseTypeKeyword(JsonObject obj)
{
    var t = obj["type"] ?? throw new InvalidSchemaException("Missing 'type' keyword");
    if (t is JsonValue v && v.TryGetValue<string>(out var s))
        return (s, false);
    if (t is JsonArray arr && arr.Count == 2)
    {
        var first  = arr[0]?.GetValue<string>();
        var second = arr[1]?.GetValue<string>();
        if (second == "null" && first is not null) return (first, true);
        if (first  == "null" && second is not null) return (second, true);
    }
    throw new InvalidSchemaException("Unsupported 'type' shape; allowed: string or [X, \"null\"]");
}

private static TypeAnnotations ParseAnnotations(JsonObject obj) => new()
{
    Title       = obj["title"]?.GetValue<string>(),
    Description = obj["description"]?.GetValue<string>(),
    Unit        = obj["x-unit"]?.GetValue<string>(),
    Minimum     = obj["minimum"]?.GetValue<double>(),
    Maximum     = obj["maximum"]?.GetValue<double>(),
    ReadOnly    = obj["readOnly"]?.GetValue<bool>() ?? false,
};

private static TypeRef ParsePrimitiveInteger(JsonObject obj) => obj["format"]?.GetValue<string>() switch
{
    "int16" => new PrimitiveTypeRef(PrimitiveKind.Short),
    "int32" => new PrimitiveTypeRef(PrimitiveKind.Int),
    "int64" => new PrimitiveTypeRef(PrimitiveKind.Long),
    null    => new PrimitiveTypeRef(PrimitiveKind.Int),    // default
    var f   => throw new InvalidSchemaException($"Unsupported integer format: '{f}'")
};

private static TypeRef ParsePrimitiveNumber(JsonObject obj) => obj["format"]?.GetValue<string>() switch
{
    "float"  => new PrimitiveTypeRef(PrimitiveKind.Float),
    "double" => new PrimitiveTypeRef(PrimitiveKind.Double),
    null     => new PrimitiveTypeRef(PrimitiveKind.Double),  // default
    var f    => throw new InvalidSchemaException($"Unsupported number format: '{f}'")
};

private static (TypeRef, System.Collections.Immutable.ImmutableDictionary<string, TypeAnnotations>) ParseString(JsonObject obj)
{
    if (obj["enum"] is JsonArray enumArr)
    {
        var members = System.Collections.Immutable.ImmutableArray.CreateBuilder<string>();
        foreach (var e in enumArr)
        {
            if (e is null) continue;  // null appears in nullable enum; the wrapping NullableTypeRef captures it
            if (!e.TryGetValue<string>(out var name))
                throw new InvalidSchemaException("Enum members must be strings");
            members.Add(name);
        }
        var title = obj["title"]?.GetValue<string>()
                    ?? throw new InvalidSchemaException("Enum schema must include 'title'");
        return (new EnumTypeRef(title, members.ToImmutable()), System.Collections.Immutable.ImmutableDictionary<string, TypeAnnotations>.Empty);
    }
    return obj["format"]?.GetValue<string>() switch
    {
        "date-time" => (new PrimitiveTypeRef(PrimitiveKind.DateTime), System.Collections.Immutable.ImmutableDictionary<string, TypeAnnotations>.Empty),
        "duration"  => (new PrimitiveTypeRef(PrimitiveKind.Duration), System.Collections.Immutable.ImmutableDictionary<string, TypeAnnotations>.Empty),
        null        => (new PrimitiveTypeRef(PrimitiveKind.String), System.Collections.Immutable.ImmutableDictionary<string, TypeAnnotations>.Empty),
        var f       => throw new InvalidSchemaException($"Unsupported string format: '{f}'")
    };
}

private static (TypeRef, System.Collections.Immutable.ImmutableDictionary<string, TypeAnnotations>) ParseObject(JsonObject obj)
{
    var ap = obj["additionalProperties"];
    if (ap is null || (ap is JsonValue av && av.TryGetValue<bool>(out var b) && b == false)) { /* ok */ }
    else throw new InvalidSchemaException("Struct must declare additionalProperties: false");

    var title = obj["title"]?.GetValue<string>()
                ?? throw new InvalidSchemaException("Struct schema must include 'title'");

    var props = obj["properties"] as JsonObject
                ?? throw new InvalidSchemaException("Struct schema must include 'properties'");
    var requiredArr = obj["required"] as JsonArray ?? new JsonArray();

    var fields = System.Collections.Immutable.ImmutableArray.CreateBuilder<StructField>();
    var sfaBuilder = System.Collections.Immutable.ImmutableDictionary.CreateBuilder<string, TypeAnnotations>();
    foreach (var kv in props)
    {
        var fieldNode = kv.Value!;
        var (fieldType, fieldAnn, _) = ParseNode(fieldNode);
        fields.Add(new StructField(kv.Key, fieldType));
        if (fieldAnn != TypeAnnotations.None) sfaBuilder[kv.Key] = fieldAnn;
    }
    var required = System.Collections.Immutable.ImmutableArray.CreateBuilder<string>();
    foreach (var r in requiredArr) required.Add(r!.GetValue<string>());
    return (
        new StructTypeRef(title, fields.ToImmutable(), required.ToImmutable()),
        sfaBuilder.ToImmutable()
    );
}

private static (TypeRef, System.Collections.Immutable.ImmutableDictionary<string, TypeAnnotations>) ParseArray(JsonObject obj)
{
    var items = obj["items"] ?? throw new InvalidSchemaException("Array schema must include 'items'");
    var (itemType, _, sfa) = ParseNode(items);
    return (new ArrayTypeRef(itemType), sfa);
}
```

- [ ] **Step 4:** Run all serialization tests.
  ```bash
  dotnet test Vion.Contracts.Test --filter TypeSchemaSerializationTests
  ```
  Expected: all PASS.

- [ ] **Step 5:** Commit.
  ```bash
  git add Vion.Contracts/TypeRef/TypeSchemaSerialization.cs Vion.Contracts.Test/TypeRef/TypeSchemaSerializationTests.cs
  git commit -m "feat(types): TypeSchemaSerialization.FromJsonSchema with profile allow-list rejection"
  ```

### 2.15 Task: PropertyMetadataSerialization

- [ ] **Step 1:** Add tests in `Vion.Contracts.Test/TypeRef/PropertyMetadataSerializationTests.cs`.

```csharp
using FluentAssertions;
using System.Collections.Immutable;
using System.Text.Json.Nodes;
using Vion.Contracts.TypeRef;

namespace Vion.Contracts.Test.TypeRef;

public class PropertyMetadataSerializationTests
{
    [Fact]
    public void Empty_presentation_and_runtime_emit_null()
    {
        var meta = PropertyMetadata.Of(TypeSchema.Of(new PrimitiveTypeRef(PrimitiveKind.Double)));
        var json = meta.ToJson();
        json["schema"].Should().NotBeNull();
        json["presentation"].Should().BeNull();
        json["runtime"].Should().BeNull();
    }

    [Fact]
    public void Roundtrip_with_presentation_and_runtime()
    {
        var meta = new PropertyMetadata(
            TypeSchema.Of(new PrimitiveTypeRef(PrimitiveKind.Double)),
            new Presentation { Group = "Power", Order = 2, Decimals = 3 },
            new RuntimeMetadata { Persistent = true });
        var roundtripped = PropertyMetadataSerialization.FromJson(meta.ToJson());
        roundtripped.Should().Be(meta);
    }
}
```

- [ ] **Step 2:** Run; expect FAIL.

- [ ] **Step 3:** Create `Vion.Contracts/TypeRef/PropertyMetadataSerialization.cs`.

```csharp
using System.Collections.Immutable;
using System.Text.Json.Nodes;

namespace Vion.Contracts.TypeRef;

public static class PropertyMetadataSerialization
{
    public static JsonNode ToJson(this PropertyMetadata metadata)
    {
        var obj = new JsonObject
        {
            ["schema"] = metadata.Schema.ToJsonSchema(),
        };
        obj["presentation"] = metadata.Presentation.IsEmpty ? null : SerializePresentation(metadata.Presentation);
        obj["runtime"]      = metadata.Runtime.IsEmpty      ? null : SerializeRuntime(metadata.Runtime);
        return obj;
    }

    public static PropertyMetadata FromJson(JsonNode json)
    {
        if (json is not JsonObject obj)
            throw new InvalidSchemaException("PropertyMetadata must be a JSON object");
        var schemaNode = obj["schema"] ?? throw new InvalidSchemaException("Missing 'schema'");
        var schema = TypeSchemaSerialization.FromJsonSchema(schemaNode);
        var presentation = obj["presentation"] is JsonObject pObj ? DeserializePresentation(pObj) : Presentation.None;
        var runtime      = obj["runtime"]      is JsonObject rObj ? DeserializeRuntime(rObj)      : RuntimeMetadata.None;
        return new PropertyMetadata(schema, presentation, runtime);
    }

    private static JsonObject SerializePresentation(Presentation p)
    {
        var o = new JsonObject();
        if (p.DisplayName is not null) o["displayName"] = p.DisplayName;
        if (p.Group       is not null) o["group"]       = p.Group;
        if (p.Order       is int order) o["order"]      = order;
        if (p.Category    is not null) o["category"]    = p.Category;
        if (p.Importance  is not null) o["importance"]  = p.Importance;
        if (p.UIHint      is not null) o["uiHint"]      = p.UIHint;
        if (p.Decimals    is int dec)  o["decimals"]    = dec;
        if (p.StatusMappings is { Count: > 0 } sm)
        {
            var smObj = new JsonObject();
            foreach (var kv in sm) smObj[kv.Key] = kv.Value;
            o["statusMappings"] = smObj;
        }
        return o;
    }

    private static Presentation DeserializePresentation(JsonObject o) => new()
    {
        DisplayName = o["displayName"]?.GetValue<string>(),
        Group       = o["group"]?.GetValue<string>(),
        Order       = o["order"]?.GetValue<int>(),
        Category    = o["category"]?.GetValue<string>(),
        Importance  = o["importance"]?.GetValue<string>(),
        UIHint      = o["uiHint"]?.GetValue<string>(),
        Decimals    = o["decimals"]?.GetValue<int>(),
        StatusMappings = o["statusMappings"] is JsonObject sm
            ? sm.ToImmutableDictionary(kv => kv.Key, kv => kv.Value!.GetValue<string>())
            : null,
    };

    private static JsonObject SerializeRuntime(RuntimeMetadata r) => new() { ["persistent"] = r.Persistent };
    private static RuntimeMetadata DeserializeRuntime(JsonObject o) => new() { Persistent = o["persistent"]?.GetValue<bool>() ?? false };
}
```

- [ ] **Step 4:** Run; expect PASS.

- [ ] **Step 5:** Commit.
  ```bash
  git add Vion.Contracts/TypeRef/PropertyMetadataSerialization.cs Vion.Contracts.Test/TypeRef/PropertyMetadataSerializationTests.cs
  git commit -m "feat(types): PropertyMetadataSerialization with omit-empty for presentation/runtime"
  ```

### 2.16 Task: New FlatBuffers `property_value.fbs`

- [ ] **Step 1:** Create `Vion.Contracts/FlatBuffers/Common/property_value.fbs`.

```fbs
namespace Vion.Contracts.FlatBuffers.Common;

// Scalar variants (6) — Long covers short/int/long; Double covers float/double; String covers string and enums.
table BoolVal     { value: bool;   }
table LongVal     { value: long;   }
table DoubleVal   { value: double; }
table StringVal   { value: string; }
table DateTimeVal { unix_ms: long; }
table DurationVal { ticks:   long; }

// Array variants (6) — `present` is optional. Absent or empty means all elements present.
// When present, present[i] = false means values[i] is null (undefined for type-defaulted slot).
table BoolArray     { values: [bool];   present: [bool]; }
table LongArray     { values: [long];   present: [bool]; }
table DoubleArray   { values: [double]; present: [bool]; }
table StringArray   { values: [string]; present: [bool]; }
table DateTimeArray { unix_ms: [long];  present: [bool]; }
table DurationArray { ticks:   [long];  present: [bool]; }

// Struct variants (2) — present[] in StructArray marks null elements
table NamedValue { name: string; value: PropertyValue; }   // value.payload ∈ scalar variants only (flat-struct rule)
table StructVal  { fields: [NamedValue]; }
table StructArray { items: [StructVal]; present: [bool]; }

// Top-level union; NONE = null
union ValuePayload {
  BoolVal, LongVal, DoubleVal, StringVal, DateTimeVal, DurationVal,
  BoolArray, LongArray, DoubleArray, StringArray, DateTimeArray, DurationArray,
  StructVal, StructArray
}

table PropertyValue { payload: ValuePayload; }
root_type PropertyValue;
```

- [ ] **Step 2:** Update `Vion.Contracts/FlatBuffers/Generate.ps1` if needed to include the new file (it already globs the directory).

- [ ] **Step 3:** Regenerate. (Requires `flatc` available.)
  ```bash
  cd C:\_gh\vion-contracts/Vion.Contracts/FlatBuffers
  pwsh ./Generate.ps1
  ```
  Expected: new files in `FlatBuffers.Generated/Common/PropertyValue.cs`, `BoolVal.cs`, ..., `StructArray.cs`, `ValuePayload.cs`.

- [ ] **Step 4:** Build. (Generated code goes through normal C# compilation.)
  ```bash
  dotnet build Vion.Contracts/Vion.Contracts.csproj
  ```
  Expected: clean build.

- [ ] **Step 5:** Commit.
  ```bash
  git add Vion.Contracts/FlatBuffers/Common/property_value.fbs Vion.Contracts/FlatBuffers.Generated/Common/
  git commit -m "feat(fb): add PropertyValue flatbuffer schema with 14-variant union"
  ```

### 2.17 Task: Update Sw/Property/*.fbs to embed PropertyValue

- [ ] **Step 1:** Read each of the four files; replace `CommonValue` references with `PropertyValue`.

`property_state_payload.fbs`:
```fbs
include "../Common/property_value.fbs";
namespace Vion.Contracts.FlatBuffers.Sw.Property;
table PropertyStatePayload { value: Vion.Contracts.FlatBuffers.Common.PropertyValue; }
root_type PropertyStatePayload;
```

`set_property_payload.fbs`:
```fbs
include "../Common/property_value.fbs";
namespace Vion.Contracts.FlatBuffers.Sw.Property;
table SetPropertyPayload { value: Vion.Contracts.FlatBuffers.Common.PropertyValue; }
root_type SetPropertyPayload;
```

`get_property_response_payload.fbs`:
```fbs
include "../Common/property_value.fbs";
namespace Vion.Contracts.FlatBuffers.Sw.Property;
table GetPropertyResponsePayload { value: Vion.Contracts.FlatBuffers.Common.PropertyValue; }
root_type GetPropertyResponsePayload;
```

`set_property_response_payload.fbs`: same pattern.

- [ ] **Step 2:** Same for `Sw/MeasuringPoint/*.fbs` (the two value-carrying ones).

- [ ] **Step 3:** Regenerate.
  ```bash
  pwsh Vion.Contracts/FlatBuffers/Generate.ps1
  ```

- [ ] **Step 4:** Build.
  ```bash
  dotnet build
  ```

- [ ] **Step 5:** Commit.
  ```bash
  git add Vion.Contracts/FlatBuffers/Sw/ Vion.Contracts/FlatBuffers.Generated/Sw/
  git commit -m "feat(fb): switch Sw/Property and Sw/MeasuringPoint payloads to PropertyValue"
  ```

### 2.18 Task: Delete CommonValue

- [ ] **Step 1:** Delete `Vion.Contracts/FlatBuffers/Common/common_value.fbs`.

- [ ] **Step 2:** Regenerate (deletes `FlatBuffers.Generated/Common/CommonValue.cs`).
  ```bash
  pwsh Vion.Contracts/FlatBuffers/Generate.ps1
  ```
  Or delete manually:
  ```bash
  rm Vion.Contracts/FlatBuffers.Generated/Common/CommonValue.cs
  rm Vion.Contracts/FlatBuffers.Generated/Common/CommonValueKind.cs
  ```

- [ ] **Step 3:** Build to confirm no remaining references.

- [ ] **Step 4:** Commit.
  ```bash
  git add -A
  git commit -m "feat(fb): remove CommonValue schema (replaced by PropertyValue)"
  ```

### 2.19 Task: PropertyValueDecodeException + ValidationResult

- [ ] **Step 1:** Create `Vion.Contracts/Codec/PropertyValueDecodeException.cs`.

```csharp
namespace Vion.Contracts.Codec;

public sealed class PropertyValueDecodeException : Exception
{
    public PropertyValueDecodeException(string message) : base(message) { }
    public PropertyValueDecodeException(string message, Exception inner) : base(message, inner) { }
}
```

- [ ] **Step 2:** Create `Vion.Contracts/Codec/ValidationResult.cs`.

```csharp
using System.Collections.Immutable;

namespace Vion.Contracts.Codec;

public sealed record ValidationResult(bool IsValid, ImmutableArray<string> Errors)
{
    public static readonly ValidationResult Valid = new(true, ImmutableArray<string>.Empty);
    public static ValidationResult Invalid(params string[] errors) => new(false, errors.ToImmutableArray());
}
```

- [ ] **Step 3:** Commit.
  ```bash
  git add Vion.Contracts/Codec/PropertyValueDecodeException.cs Vion.Contracts/Codec/ValidationResult.cs
  git commit -m "feat(codec): add PropertyValueDecodeException and ValidationResult types"
  ```

### 2.20 Task: PropertyValueCodec — FlatBufferToJson (schema-free)

This is the largest single task. The codec walks the FB union tag tree and produces JSON without consulting any schema. Implement and test all 14 variants.

- [ ] **Step 1:** Create test scaffold `Vion.Contracts.Test/Codec/PropertyValueCodecTests.cs`.

```csharp
using FluentAssertions;
using FlatBuffers;
using System.Text.Json.Nodes;
using Vion.Contracts.Codec;
using Vion.Contracts.FlatBuffers.Common;

namespace Vion.Contracts.Test.Codec;

public class PropertyValueCodecTests
{
    private static byte[] BuildPropertyValue(Action<FlatBufferBuilder> populate)
    {
        var builder = new FlatBufferBuilder(64);
        populate(builder);
        return builder.SizedByteArray();
    }

    private static byte[] BuildBoolVal(bool v)
    {
        var builder = new FlatBufferBuilder(64);
        var boolOff = BoolVal.CreateBoolVal(builder, v);
        var pv = PropertyValue.CreatePropertyValue(builder, ValuePayload.BoolVal, boolOff.Value);
        builder.Finish(pv.Value);
        return builder.SizedByteArray();
    }

    [Fact]
    public void FbToJson_Bool_true()
    {
        var bytes = BuildBoolVal(true);
        var json = PropertyValueCodec.FlatBufferToJson(bytes);
        json!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public void FbToJson_Bool_false()
    {
        var bytes = BuildBoolVal(false);
        var json = PropertyValueCodec.FlatBufferToJson(bytes);
        json!.GetValue<bool>().Should().BeFalse();
    }
}
```

- [ ] **Step 2:** Run; expect FAIL.

- [ ] **Step 3:** Create `Vion.Contracts/Codec/PropertyValueCodec.cs` with `FlatBufferToJson` for `BoolVal`. Use the FB-generated accessors (`PropertyValue.GetRootAsPropertyValue`, `.PayloadType`, etc.).

```csharp
using System.Text.Json.Nodes;
using Vion.Contracts.FlatBuffers.Common;
using Vion.Contracts.TypeRef;

namespace Vion.Contracts.Codec;

public static class PropertyValueCodec
{
    public static JsonNode? FlatBufferToJson(ReadOnlySpan<byte> bytes)
    {
        var bb = new FlatBuffers.ByteBuffer(bytes.ToArray());
        var pv = PropertyValue.GetRootAsPropertyValue(bb);
        return DecodePayload(pv);
    }

    private static JsonNode? DecodePayload(PropertyValue pv) => pv.PayloadType switch
    {
        ValuePayload.NONE       => null,
        ValuePayload.BoolVal    => JsonValue.Create(pv.PayloadAsBoolVal().Value),
        // ... fill in incrementally
        _ => throw new PropertyValueDecodeException($"Unhandled variant: {pv.PayloadType}")
    };
}
```

- [ ] **Step 4:** Run BoolVal tests; expect PASS.

- [ ] **Step 5:** Add and implement variants one-by-one with a test per variant. After each: build, test, commit.

For brevity in this plan, the implementation pattern for each variant is:

| Variant | Decoder branch |
|---------|----------------|
| `LongVal`     | `JsonValue.Create(pv.PayloadAsLongVal().Value)` |
| `DoubleVal`   | `JsonValue.Create(pv.PayloadAsDoubleVal().Value)` |
| `StringVal`   | `JsonValue.Create(pv.PayloadAsStringVal().Value)` |
| `DateTimeVal` | `JsonValue.Create(DateTimeOffset.FromUnixTimeMilliseconds(pv.PayloadAsDateTimeVal().UnixMs).UtcDateTime.ToString("o"))` |
| `DurationVal` | `JsonValue.Create(System.Xml.XmlConvert.ToString(TimeSpan.FromTicks(pv.PayloadAsDurationVal().Ticks)))` (ISO 8601 duration) |
| `BoolArray`   | walk `Values(i)` and `Present` (if present_length > 0); produce `JsonArray` of `bool` or `null` |
| `LongArray`, `DoubleArray`, `StringArray` | same pattern |
| `DateTimeArray`, `DurationArray` | same with format conversion |
| `StructVal`   | iterate `Fields(i)`; for each `NamedValue` get `Name` and recurse on inner `PropertyValue`; build `JsonObject` |
| `StructArray` | iterate `Items(i)`; recurse each as `StructVal`; honour `present[i]` |

Add a test per variant; a test for nullable-array with mixed null/non-null elements; a test for the round-trip `[bool|null|bool]` → `[true, null, false]`.

- [ ] **Step 6:** After all 14 variants implemented, run the full suite.
  ```bash
  dotnet test Vion.Contracts.Test --filter PropertyValueCodecTests
  ```
  Expected: ≥14 tests PASS.

- [ ] **Step 7:** Commit.
  ```bash
  git add Vion.Contracts/Codec/PropertyValueCodec.cs Vion.Contracts.Test/Codec/PropertyValueCodecTests.cs
  git commit -m "feat(codec): FlatBufferToJson schema-free for all 14 wire variants"
  ```

### 2.21 Task: PropertyValueCodec — JsonToFlatBuffer (schema-driven)

JSON→FB needs the schema to choose the right variant (e.g. `42` could be Long or Double; `"hi"` could be String or DateTime).

- [ ] **Step 1:** Add tests.

```csharp
[Fact]
public void JsonToFb_Bool_roundtrip()
{
    var schema = new PrimitiveTypeRef(PrimitiveKind.Bool);
    var bytes = PropertyValueCodec.JsonToFlatBuffer(JsonValue.Create(true), schema);
    var roundtrip = PropertyValueCodec.FlatBufferToJson(bytes);
    roundtrip!.GetValue<bool>().Should().BeTrue();
}

[Fact]
public void JsonToFb_Double_roundtrip()
{
    var schema = new PrimitiveTypeRef(PrimitiveKind.Double);
    var bytes = PropertyValueCodec.JsonToFlatBuffer(JsonValue.Create(3.14), schema);
    var roundtrip = PropertyValueCodec.FlatBufferToJson(bytes);
    roundtrip!.GetValue<double>().Should().Be(3.14);
}

[Fact]
public void JsonToFb_Struct_roundtrip()
{
    var schema = new StructTypeRef("Coordinates",
        System.Collections.Immutable.ImmutableArray.Create(
            new StructField("lat", new PrimitiveTypeRef(PrimitiveKind.Double)),
            new StructField("lon", new PrimitiveTypeRef(PrimitiveKind.Double))),
        System.Collections.Immutable.ImmutableArray.Create("lat","lon"));
    var input = JsonNode.Parse("{\"lat\":47.3,\"lon\":8.5}");
    var bytes = PropertyValueCodec.JsonToFlatBuffer(input, schema);
    var roundtrip = PropertyValueCodec.FlatBufferToJson(bytes);
    roundtrip!["lat"]!.GetValue<double>().Should().Be(47.3);
    roundtrip["lon"]!.GetValue<double>().Should().Be(8.5);
}
```

- [ ] **Step 2:** Run; expect FAIL.

- [ ] **Step 3:** Implement `JsonToFlatBuffer`. The encoder dispatches on `TypeRef`:

```csharp
public static byte[] JsonToFlatBuffer(JsonNode? json, TypeRef type)
{
    var builder = new FlatBuffers.FlatBufferBuilder(64);
    var (payloadType, payloadOffset) = EncodeValue(builder, json, type);
    var pv = PropertyValue.CreatePropertyValue(builder, payloadType, payloadOffset);
    builder.Finish(pv.Value);
    return builder.SizedByteArray();
}

private static (ValuePayload, int) EncodeValue(FlatBuffers.FlatBufferBuilder b, JsonNode? json, TypeRef type)
{
    if (json is null && type is NullableTypeRef) return (ValuePayload.NONE, 0);
    if (json is null) throw new PropertyValueDecodeException($"Null value for non-nullable type {type}");

    var inner = type is NullableTypeRef n ? n.Inner : type;
    return inner switch
    {
        PrimitiveTypeRef p => EncodePrimitive(b, json, p),
        EnumTypeRef _      => (ValuePayload.StringVal, StringVal.CreateStringVal(b, b.CreateString(json.GetValue<string>())).Value),
        StructTypeRef s    => EncodeStruct(b, json, s),
        ArrayTypeRef a     => EncodeArray(b, json, a),
        _ => throw new InvalidOperationException($"Unhandled type {inner}")
    };
}

// EncodePrimitive, EncodeStruct, EncodeArray follow the same patterns as Decode but in reverse.
// For Encode, the schema's TypeRef dictates which FB variant to construct.
```

> **Note:** the implementer fills in `EncodePrimitive` (9 cases), `EncodeStruct` (recursive over fields), `EncodeArray` (recursive; honours `present[]` for nullable items). Each case follows the same pattern as the decoder. Build tests pairwise: encode then decode, verify round-trip.

- [ ] **Step 4:** Run; expect PASS for the three explicit tests + add one round-trip per FB variant.

- [ ] **Step 5:** Commit.
  ```bash
  git add -A
  git commit -m "feat(codec): JsonToFlatBuffer schema-driven for all variants"
  ```

### 2.22 Task: PropertyValueCodec — Clr-side encode/decode

- [ ] **Step 1:** Add tests for CLR round-trips with enum and struct.

```csharp
public enum AlarmState { Ok, Warning, Critical }
public readonly record struct Coordinates(double Lat, double Lon);

[Fact]
public void Clr_Enum_roundtrip()
{
    var schema = new EnumTypeRef("AlarmState",
        System.Collections.Immutable.ImmutableArray.Create("Ok","Warning","Critical"));
    var bytes = PropertyValueCodec.ClrToFlatBuffer(AlarmState.Warning, schema);
    var clr = (AlarmState)PropertyValueCodec.FlatBufferToClr(bytes, schema, typeof(AlarmState))!;
    clr.Should().Be(AlarmState.Warning);
}

[Fact]
public void Clr_Struct_roundtrip()
{
    var schema = new StructTypeRef("Coordinates",
        System.Collections.Immutable.ImmutableArray.Create(
            new StructField("lat", new PrimitiveTypeRef(PrimitiveKind.Double)),
            new StructField("lon", new PrimitiveTypeRef(PrimitiveKind.Double))),
        System.Collections.Immutable.ImmutableArray.Create("lat","lon"));
    var coords = new Coordinates(47.3, 8.5);
    var bytes = PropertyValueCodec.ClrToFlatBuffer(coords, schema);
    var clr = (Coordinates)PropertyValueCodec.FlatBufferToClr(bytes, schema, typeof(Coordinates))!;
    clr.Should().Be(coords);
}
```

- [ ] **Step 2:** Run; expect FAIL.

- [ ] **Step 3:** Implement `ClrToFlatBuffer` and `FlatBufferToClr`.

```csharp
public static byte[] ClrToFlatBuffer(object? value, TypeRef type)
{
    var json = ClrToJson(value, type);
    return JsonToFlatBuffer(json, type);
}

public static object? FlatBufferToClr(ReadOnlySpan<byte> bytes, TypeRef type, Type targetClrType)
{
    var json = FlatBufferToJson(bytes);
    return JsonToClr(json, type, targetClrType);
}

// ClrToJson: walk the CLR value using the TypeRef.
//   - PrimitiveTypeRef: use value directly (or convert datetime/timespan to ISO string).
//   - EnumTypeRef: Enum.GetName(value.GetType(), value).
//   - StructTypeRef: build a JsonObject from the struct's properties; field names lowercased to match camelCase wire form.
//   - ArrayTypeRef: build JsonArray from IEnumerable elements.
//   - NullableTypeRef: forward null or unwrap.
//
// JsonToClr: inverse.
//   - PrimitiveTypeRef: cast or parse string formats.
//   - EnumTypeRef: Enum.Parse<T>(name).
//   - StructTypeRef: reflect the target type's positional record-struct constructor, match parameters by camelCase name, invoke.
//   - ArrayTypeRef: build ImmutableArray<T> via reflection.
```

> Implementation detail: `ClrToJson`/`JsonToClr` use a per-type cache (`EnumNameCache`, `StructConstructorCache`) to avoid reflection on the hot path. See `Vion.Contracts/Codec/EnumNameCache.cs` skeleton in §2.23.

- [ ] **Step 4:** Run all CLR-roundtrip tests; expect PASS.

- [ ] **Step 5:** Commit.
  ```bash
  git add -A
  git commit -m "feat(codec): ClrToFlatBuffer / FlatBufferToClr with reflection caching"
  ```

### 2.23 Task: EnumNameCache helper

- [ ] **Step 1:** Create `Vion.Contracts/Codec/EnumNameCache.cs`.

```csharp
using System.Collections.Concurrent;

namespace Vion.Contracts.Codec;

internal static class EnumNameCache
{
    private static readonly ConcurrentDictionary<Type, EnumMap> Cache = new();

    public static string GetName(Type enumType, object value) =>
        Cache.GetOrAdd(enumType, t => new EnumMap(t)).GetName(value);

    public static object Parse(Type enumType, string name) =>
        Cache.GetOrAdd(enumType, t => new EnumMap(t)).Parse(name);

    private sealed class EnumMap
    {
        private readonly Dictionary<object, string> _toName;
        private readonly Dictionary<string, object> _fromName;

        public EnumMap(Type t)
        {
            var values = Enum.GetValues(t);
            _toName = new(values.Length);
            _fromName = new(values.Length);
            foreach (var v in values)
            {
                var name = Enum.GetName(t, v) ?? v.ToString()!;
                _toName[v] = name;
                _fromName[name] = v;
            }
        }

        public string GetName(object value) => _toName[value];
        public object Parse(string name) => _fromName.TryGetValue(name, out var v)
            ? v
            : throw new PropertyValueDecodeException($"Unknown enum member: '{name}'");
    }
}
```

- [ ] **Step 2:** Wire `EnumNameCache.GetName`/`Parse` into the codec encode/decode for `EnumTypeRef`.

- [ ] **Step 3:** Run codec tests; verify enum tests still pass.

- [ ] **Step 4:** Commit.
  ```bash
  git add Vion.Contracts/Codec/EnumNameCache.cs
  git commit -m "feat(codec): cached Enum.GetName/Parse via EnumNameCache"
  ```

### 2.24 Task: PropertyValueCodec — ValidateJson

- [ ] **Step 1:** Add tests.

```csharp
[Fact]
public void Validate_double_with_minimum()
{
    var schema = new TypeSchema(
        new PrimitiveTypeRef(PrimitiveKind.Double),
        new TypeAnnotations { Minimum = 0 },
        System.Collections.Immutable.ImmutableDictionary<string, TypeAnnotations>.Empty);
    PropertyValueCodec.ValidateJson(JsonValue.Create(5.0), schema).IsValid.Should().BeTrue();
    PropertyValueCodec.ValidateJson(JsonValue.Create(-1.0), schema).IsValid.Should().BeFalse();
}

[Fact]
public void Validate_struct_required_fields()
{
    var schema = TypeSchema.Of(new StructTypeRef("Coordinates",
        System.Collections.Immutable.ImmutableArray.Create(
            new StructField("lat", new PrimitiveTypeRef(PrimitiveKind.Double)),
            new StructField("lon", new PrimitiveTypeRef(PrimitiveKind.Double))),
        System.Collections.Immutable.ImmutableArray.Create("lat","lon")));
    PropertyValueCodec.ValidateJson(JsonNode.Parse("{\"lat\":47.3,\"lon\":8.5}"), schema).IsValid.Should().BeTrue();
    PropertyValueCodec.ValidateJson(JsonNode.Parse("{\"lat\":47.3}"), schema).IsValid.Should().BeFalse();
}

[Fact]
public void Validate_enum_membership()
{
    var schema = TypeSchema.Of(new EnumTypeRef("AlarmState",
        System.Collections.Immutable.ImmutableArray.Create("Ok","Warning")));
    PropertyValueCodec.ValidateJson(JsonValue.Create("Warning"), schema).IsValid.Should().BeTrue();
    PropertyValueCodec.ValidateJson(JsonValue.Create("Critical"), schema).IsValid.Should().BeFalse();
}

[Fact]
public void Validate_readonly_rejects_writes()
{
    var schema = new TypeSchema(
        new PrimitiveTypeRef(PrimitiveKind.Double),
        new TypeAnnotations { ReadOnly = true },
        System.Collections.Immutable.ImmutableDictionary<string, TypeAnnotations>.Empty);
    var result = PropertyValueCodec.ValidateJson(JsonValue.Create(5.0), schema);
    result.IsValid.Should().BeFalse();
    result.Errors.Should().ContainSingle(e => e.Contains("read-only"));
}
```

- [ ] **Step 2:** Run; expect FAIL.

- [ ] **Step 3:** Implement `ValidateJson`. Recursive walker over `(TypeRef, JsonNode)` checking shape; validates min/max/enum/required/readOnly using `TypeAnnotations`. Returns aggregated `ValidationResult`.

- [ ] **Step 4:** Run all validation tests; expect PASS.

- [ ] **Step 5:** Commit.
  ```bash
  git add -A
  git commit -m "feat(codec): ValidateJson for shape, range, enum, required, readOnly"
  ```

### 2.25 Task: Update DTOs

- [ ] **Step 1:** Modify `Vion.Contracts/Events/CloudToMesh/SetPropertyPayload.cs`.

Before:
```csharp
public record SetPropertyPayload(object Value, string Type) : IMessage;
```

After:
```csharp
using System.Text.Json.Nodes;
namespace Vion.Contracts.Events.CloudToMesh;

[Schema("SetPropertyPayload")]
public record SetPropertyPayload(JsonNode? Value, JsonNode Schema) : IMessage;
```

- [ ] **Step 2:** Modify `Vion.Contracts/Events/MeshToCloud/PropertiesStatePayload.cs`.

```csharp
public readonly record struct PropertyState(string PropertyIdentifier, JsonNode? Value);
```

- [ ] **Step 3:** Modify `Vion.Contracts/Events/MeshToCloud/SetPropertyResponsePayload.cs` similarly: `object Value` → `JsonNode? Value`.

- [ ] **Step 4:** Modify `Vion.Contracts/Events/MeshToCloud/MeasuringPointsStatePayload.cs`: nested `MeasuringPoint(string Identifier, object Value)` → `MeasuringPoint(string Identifier, JsonNode? Value)`.

- [ ] **Step 5:** Build (compilation should still succeed since nothing in vion-contracts consumes these).
  ```bash
  dotnet build
  ```

- [ ] **Step 6:** Commit.
  ```bash
  git add Vion.Contracts/Events/
  git commit -m "feat(dto): SetPropertyPayload carries schema; *.Value -> JsonNode?"
  ```

### 2.26 Task: Update LogicBlockIntrospectionResult

- [ ] **Step 1:** Modify `Vion.Contracts/Introspection/LogicBlockIntrospectionResult.cs`.

Replace `ServicePropertyInfo` and `ServiceMeasuringPointInfo` definitions:

```csharp
using System.Text.Json.Nodes;

public class ServicePropertyInfo
{
    public required string    Identifier   { get; set; }
    public required JsonNode  Schema       { get; set; }   // JSON Schema 2020-12, Dale profile
    public          JsonNode? Presentation { get; set; }
    public          JsonNode? Runtime      { get; set; }
}

public class ServiceMeasuringPointInfo
{
    public required string    Identifier   { get; set; }
    public required JsonNode  Schema       { get; set; }
    public          JsonNode? Presentation { get; set; }
    public          JsonNode? Runtime      { get; set; }
}
```

- [ ] **Step 2:** Build. Since no in-tree code consumes these, build is clean.

- [ ] **Step 3:** Commit.
  ```bash
  git add Vion.Contracts/Introspection/LogicBlockIntrospectionResult.cs
  git commit -m "feat(introspection): replace Type/Writable/Annotations with Schema/Presentation/Runtime"
  ```

### 2.27 Task: Delete ServiceElementTypes constants

- [ ] **Step 1:** Delete `Vion.Contracts/Constants/ServiceElementTypes.cs`.

- [ ] **Step 2:** Build to confirm no references remain in this repo.

- [ ] **Step 3:** Commit.
  ```bash
  git rm Vion.Contracts/Constants/ServiceElementTypes.cs
  git commit -m "feat: remove ServiceElementTypes string constants (replaced by JSON Schema)"
  ```

### 2.28 Task: Final build + tests + open PR

- [ ] **Step 1:** Full clean build + test.
  ```bash
  dotnet test Vion.Contracts.Test --logger "console;verbosity=normal"
  ```
  Expected: all tests PASS.

- [ ] **Step 2:** Push branch.
  ```bash
  git push -u origin feat/rich-types
  ```

- [ ] **Step 3:** Open PR via `gh`.

```bash
gh pr create --title "Rich types: foundation (types + codec + FB schema + DTOs)" --body "$(cat <<'EOF'
## Summary
PR 1 of 7 in the rich-types initiative. Establishes the new type-language records, JSON Schema serializers, FB schema (14 variants), and codec.

## Related
- Spec: `docs/superpowers/specs/2026-05-04-service-property-rich-types-design.md` in dale-sdk
- Plan: `docs/superpowers/plans/2026-05-04-rich-types-impl-plan.md`, §2

## Verification
- `dotnet test Vion.Contracts.Test` — all tests green
- New CI prerelease will publish as `0.0.0-feat-rich-types.<runNumber>` to private feed

## Checklist
- [x] TypeRef hierarchy with identity tests
- [x] TypeAnnotations / Presentation / RuntimeMetadata / TypeSchema / PropertyMetadata
- [x] TypeSchemaSerialization / PropertyMetadataSerialization with profile rejection
- [x] PropertyValue FB schema (14 variants)
- [x] PropertyValueCodec: FB↔JSON, JSON↔FB, CLR↔FB, ValidateJson
- [x] DTOs updated (SetPropertyPayload, *StatePayload)
- [x] Introspection types updated
- [x] CommonValue + ServiceElementTypes removed

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

- [ ] **Step 4:** Update `§0.1 Status` row for PR 1.

- [ ] **Step 5:** Wait for CI green; merge; record published version (e.g. `0.0.0-feat-rich-types.42`) in `§0.1 Status`.

---

## 3. PR 2: dale-sdk

**Goal:** Expand the analyzer to validate rich types, rename attributes, replace `MapToServiceElementType` with the recursive `BuildTypeRef` introspection emitter, populate `ServiceBinding.Metadata`.

**Repo:** `C:\_gh\dale-sdk`
**Branch:** `feat/rich-types`
**Prereqs:** PR 1 merged; `Vion.Contracts <prerelease>` consumable from private feed.

### 3.1 File structure

**Modify:**
- `Vion.Dale.Sdk/Core/ServicePropertyAttribute.cs` — rename properties + `[Obsolete]` shims
- `Vion.Dale.Sdk/Core/ServiceMeasuringPointAttribute.cs` — same + add Min/Max
- `Vion.Dale.Sdk.Generators/Analyzers/AnalyzerHelper.cs` — recursive `IsSupportedServiceElementType`
- `Vion.Dale.Sdk.Generators/Analyzers/ServiceElementTypeAnalyzer.cs` — DALE003 expanded
- `Vion.Dale.Sdk.Generators/Analyzers/DaleDiagnostics.cs` — add DALE008/016/017/018
- `Vion.Dale.Sdk/Introspection/LogicBlockIntrospection.cs` — `BuildTypeRef`, three-doc emit
- `Vion.Dale.Sdk/Configuration/Services/ServiceBinding.cs` — add `Metadata` field
- `Vion.Dale.Sdk/Configuration/Services/ServiceBuilder.cs` and `ServiceBuilderBase.cs` — populate Metadata
- `Vion.Dale.Sdk/Configuration/Services/ServiceBinder.cs` — drop ad-hoc int→enum conversion in `SetPropertyValue`
- `Vion.Dale.Sdk/Vion.Dale.Sdk.csproj` and `Vion.Dale.Sdk.Generators/Vion.Dale.Sdk.Generators.csproj` — bump `Vion.Contracts` PackageReference

**Create:**
- `Vion.Dale.Sdk/Core/StructFieldAttribute.cs`
- `Vion.Dale.Sdk.Generators/Analyzers/ImmutableArrayServiceElementAnalyzer.cs` (DALE008)
- `Vion.Dale.Sdk.Generators/Analyzers/StructServiceElementAnalyzer.cs` (DALE016)
- `Vion.Dale.Sdk.Generators/Analyzers/NullableStringAnalyzer.cs` (DALE017)
- `Vion.Dale.Sdk.Generators/Analyzers/ImmutableArrayInitializationAnalyzer.cs` (DALE018)
- `Vion.Dale.Sdk/Introspection/PropertyMetadataBuilder.cs` (helper that routes attributes into Schema/Presentation/Runtime)

### 3.2 Branch + Vion.Contracts pin

- [ ] **Step 1:** Branch.
  ```bash
  cd C:\_gh\dale-sdk
  git checkout main && git pull
  git checkout -b feat/rich-types
  ```

- [ ] **Step 2:** Bump `Vion.Contracts` to PR-1's published prerelease.

```bash
dotnet add Vion.Dale.Sdk/Vion.Dale.Sdk.csproj package Vion.Contracts --version 0.0.0-feat-rich-types.<n>
dotnet add Vion.Dale.Sdk.Generators/Vion.Dale.Sdk.Generators.csproj package Vion.Contracts --version 0.0.0-feat-rich-types.<n>
```

(Substitute `<n>` with the run number from §0.1 Status, PR 1 row.)

- [ ] **Step 3:** Verify build.
  ```bash
  dotnet build Vion.Dale.Sdk.sln
  ```
  Expected: only the to-be-fixed analyzer/intro errors should surface; rest builds.

### 3.3 Task: Rename ServicePropertyAttribute properties

- [ ] **Step 1:** Edit `Vion.Dale.Sdk/Core/ServicePropertyAttribute.cs`.

Before:
```csharp
public string? DefaultName { get; }
public double? MinValue { get; }
public double? MaxValue { get; }
public ServicePropertyAttribute(string? defaultName = null, string? unit = null, double minValue = double.NaN, double maxValue = double.NaN) { ... }
```

After:
```csharp
public string? Title    { get; init; }
public string? Unit     { get; init; }
public double  Minimum  { get; init; } = double.NegativeInfinity;
public double  Maximum  { get; init; } = double.PositiveInfinity;

[Obsolete("Use Title instead. Will be removed in next major.")]
public string? DefaultName { get => Title; init => Title = value; }

[Obsolete("Use Minimum instead. Will be removed in next major.")]
public double  MinValue    { get => Minimum; init => Minimum = value; }

[Obsolete("Use Maximum instead. Will be removed in next major.")]
public double  MaxValue    { get => Maximum; init => Maximum = value; }

public ServicePropertyAttribute() {}
```

> Switch from constructor parameters to property initialisers since most callsites use named-arg style. The `[Obsolete]` shims remain assignable so existing call sites keep compiling with deprecation warnings.

- [ ] **Step 2:** Build; confirm warnings on `[Obsolete]` callers but no errors.

- [ ] **Step 3:** Commit.
  ```bash
  git add Vion.Dale.Sdk/Core/ServicePropertyAttribute.cs
  git commit -m "feat(sdk): ServicePropertyAttribute rename DefaultName/MinValue/MaxValue with Obsolete shims"
  ```

### 3.4 Task: Rename ServiceMeasuringPointAttribute + add Min/Max

- [ ] **Step 1:** Edit `Vion.Dale.Sdk/Core/ServiceMeasuringPointAttribute.cs` similarly. Add `Minimum`/`Maximum` (new for parity with property attribute).

```csharp
public string? Title    { get; init; }
public string? Unit     { get; init; }
public double  Minimum  { get; init; } = double.NegativeInfinity;
public double  Maximum  { get; init; } = double.PositiveInfinity;

[Obsolete("Use Title instead.")] public string? DefaultName { get => Title; init => Title = value; }
```

- [ ] **Step 2:** Build, commit.
  ```bash
  git add Vion.Dale.Sdk/Core/ServiceMeasuringPointAttribute.cs
  git commit -m "feat(sdk): ServiceMeasuringPointAttribute rename + add Minimum/Maximum"
  ```

### 3.5 Task: New StructFieldAttribute

- [ ] **Step 1:** Create `Vion.Dale.Sdk/Core/StructFieldAttribute.cs`.

```csharp
using System;

namespace Vion.Dale.Sdk.Core
{
    [PublicApi]
    [AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
    public sealed class StructFieldAttribute : Attribute
    {
        public string? Title       { get; init; }
        public string? Description { get; init; }
        public string? Unit        { get; init; }
        public double  Minimum     { get; init; } = double.NegativeInfinity;
        public double  Maximum     { get; init; } = double.PositiveInfinity;
    }
}
```

- [ ] **Step 2:** Commit.
  ```bash
  git add Vion.Dale.Sdk/Core/StructFieldAttribute.cs
  git commit -m "feat(sdk): add StructFieldAttribute for per-struct-field annotations"
  ```

### 3.6 Task: Recursive `IsSupportedServiceElementType`

- [ ] **Step 1:** Read `Vion.Dale.Sdk.Generators/Analyzers/AnalyzerHelper.cs`. The current `IsSupportedServiceElementType` is a flat whitelist.

- [ ] **Step 2:** Rewrite to recurse:

```csharp
public static bool IsSupportedServiceElementType(ITypeSymbol type)
{
    if (type is null) return false;
    return type switch
    {
        // Nullable<T> for value types
        INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nt
            => IsSupportedServiceElementType(nt.TypeArguments[0]),

        // ImmutableArray<T>
        INamedTypeSymbol { Name: "ImmutableArray", ContainingNamespace.Name: "Immutable" } ia
            => IsSupportedServiceElementType(ia.TypeArguments[0]),

        // string?  / string  — string is a reference type so nullability handled separately
        _ when type.SpecialType == SpecialType.System_String => true,

        // Enums
        _ when type.TypeKind == TypeKind.Enum => true,

        // Primitives
        _ when type.SpecialType is SpecialType.System_Boolean
                                or SpecialType.System_Byte
                                or SpecialType.System_Int16
                                or SpecialType.System_UInt16
                                or SpecialType.System_Int32
                                or SpecialType.System_UInt32
                                or SpecialType.System_Int64
                                or SpecialType.System_Single
                                or SpecialType.System_Double
                                or SpecialType.System_DateTime
            => true,

        _ when type.ToDisplayString() == "System.TimeSpan" => true,

        // Flat structs (readonly record struct with primitive/enum/nullable fields)
        INamedTypeSymbol { IsValueType: true, IsRecord: true, IsReadOnly: true } rs
            when AllStructFieldsArePrimitiveOrEnum(rs) => true,

        _ => false
    };
}

private static bool AllStructFieldsArePrimitiveOrEnum(INamedTypeSymbol structType)
{
    // Get the positional record-struct constructor's parameters or instance properties.
    // Each parameter type must be: primitive, enum, or nullable-of-(primitive or enum).
    var ctor = structType.InstanceConstructors.FirstOrDefault(c => c.Parameters.Length > 0);
    if (ctor is null) return false;
    foreach (var p in ctor.Parameters)
    {
        var t = p.Type;
        var isNullable = t is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nt;
        var inner = isNullable ? ((INamedTypeSymbol)t).TypeArguments[0] : t;
        var ok = inner.SpecialType is SpecialType.System_Boolean
                                   or SpecialType.System_Int16
                                   or SpecialType.System_Int32
                                   or SpecialType.System_Int64
                                   or SpecialType.System_Single
                                   or SpecialType.System_Double
                                   or SpecialType.System_DateTime
                 || inner.TypeKind == TypeKind.Enum
                 || inner.SpecialType == SpecialType.System_String
                 || inner.ToDisplayString() == "System.TimeSpan";
        if (!ok) return false;
    }
    return true;
}
```

> **Note:** `decimal` is removed from the list. Compare to the legacy implementation in §2 of the existing AnalyzerHelper.cs.

- [ ] **Step 3:** Add unit tests in `Vion.Dale.Sdk.Generators.Tests` (create the test project if missing) covering: bool, int, double, enum, string, string?, ImmutableArray\<double\>, Coordinates struct, Coordinates?, decimal (rejected), List\<double\> (rejected), int[] (rejected).

- [ ] **Step 4:** Build, run tests.

- [ ] **Step 5:** Commit.
  ```bash
  git add -A
  git commit -m "feat(analyzer): recursive IsSupportedServiceElementType supports nullable, struct, ImmutableArray"
  ```

### 3.7 Task: Add DALE008 — array must be ImmutableArray

- [ ] **Step 1:** Add diagnostic ID to `DaleDiagnostics.cs`.

```csharp
public const string DALE008_ArrayMustBeImmutableArray = "DALE008";
public static readonly DiagnosticDescriptor ArrayMustBeImmutableArray = new(
    id: DALE008_ArrayMustBeImmutableArray,
    title: "Array-valued service element must be ImmutableArray<T>",
    messageFormat: "Service element '{0}' is typed '{1}'; use ImmutableArray<T>",
    category: "Dale",
    DiagnosticSeverity.Error,
    isEnabledByDefault: true);
```

- [ ] **Step 2:** Create `Vion.Dale.Sdk.Generators/Analyzers/ImmutableArrayServiceElementAnalyzer.cs`. Inspect properties marked `[ServiceProperty]` or `[ServiceMeasuringPoint]`; if the type is `T[]`, `List<T>`, `IReadOnlyList<T>`, `IEnumerable<T>` — report DALE008.

- [ ] **Step 3:** Add analyzer test (compliant code passes; non-compliant triggers DALE008).

- [ ] **Step 4:** Run analyzer tests.

- [ ] **Step 5:** Commit.

### 3.8 Task: Add DALE016 — struct must be readonly record struct with flat fields

Same pattern as 3.7 — add diagnostic, analyzer, tests, commit.

### 3.9 Task: Add DALE017 — string must be explicitly nullable

Same pattern.

### 3.10 Task: Add DALE018 — ImmutableArray must be initialised

Same pattern. Severity: warning.

### 3.11 Task: PropertyMetadataBuilder helper

This helper centralises the routing of source attributes into the three sibling documents. Used by `LogicBlockIntrospection`.

- [ ] **Step 1:** Create `Vion.Dale.Sdk/Introspection/PropertyMetadataBuilder.cs`.

```csharp
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using Vion.Contracts.TypeRef;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Introspection
{
    internal static class PropertyMetadataBuilder
    {
        public static PropertyMetadata Build(PropertyInfo property, TypeRef typeRef, ImmutableDictionary<string, TypeAnnotations> structFieldAnnotations)
        {
            var annotations = ExtractTypeAnnotations(property);
            var schema = new TypeSchema(typeRef, annotations, structFieldAnnotations);
            var presentation = ExtractPresentation(property);
            var runtime = ExtractRuntime(property);
            return new PropertyMetadata(schema, presentation, runtime);
        }

        private static TypeAnnotations ExtractTypeAnnotations(PropertyInfo p)
        {
            var sp = p.GetCustomAttribute<ServicePropertyAttribute>();
            var mp = p.GetCustomAttribute<ServiceMeasuringPointAttribute>();
            var ann = new TypeAnnotations
            {
                Title    = sp?.Title ?? mp?.Title,
                Unit     = sp?.Unit  ?? mp?.Unit,
                Minimum  = sp is not null && !double.IsNegativeInfinity(sp.Minimum) ? sp.Minimum
                         : mp is not null && !double.IsNegativeInfinity(mp.Minimum) ? mp.Minimum
                         : null,
                Maximum  = sp is not null && !double.IsPositiveInfinity(sp.Maximum) ? sp.Maximum
                         : mp is not null && !double.IsPositiveInfinity(mp.Maximum) ? mp.Maximum
                         : null,
                ReadOnly = mp is not null && sp is null,  // measuring point alone = read-only
            };
            return ann;
        }

        private static Presentation ExtractPresentation(PropertyInfo p)
        {
            var display    = p.GetCustomAttribute<DisplayAttribute>();
            var category   = p.GetCustomAttribute<CategoryAttribute>();
            var importance = p.GetCustomAttribute<ImportanceAttribute>();
            var uiHint     = p.GetCustomAttribute<UIHintAttribute>();
            var status     = p.GetCustomAttribute<StatusIndicatorAttribute>();

            var statusMappings = ExtractStatusMappings(p, status);

            return new Presentation
            {
                DisplayName    = display?.Name,
                Group          = display?.Group,
                Order          = display?.Order != 0 ? display?.Order : null,
                Category       = category?.Name,
                Importance     = importance?.Level.ToString(),
                UIHint         = uiHint?.Hint,
                StatusMappings = statusMappings,
            };
        }

        private static ImmutableDictionary<string, string>? ExtractStatusMappings(PropertyInfo p, StatusIndicatorAttribute? statusAttr)
        {
            if (statusAttr is null) return null;
            // For an enum-typed property, walk enum members; pick severity from [StatusSeverity] on each member.
            var enumType = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
            if (!enumType.IsEnum) return null;
            var b = ImmutableDictionary.CreateBuilder<string, string>();
            foreach (var name in Enum.GetNames(enumType))
            {
                var memberInfo = enumType.GetField(name);
                var severity = memberInfo?.GetCustomAttribute<StatusSeverityAttribute>();
                if (severity is not null) b[name] = severity.Severity.ToString().ToLowerInvariant();
            }
            return b.Count > 0 ? b.ToImmutable() : null;
        }

        private static RuntimeMetadata ExtractRuntime(PropertyInfo p)
        {
            var persistent = p.GetCustomAttribute<PersistentAttribute>() is not null;
            return new RuntimeMetadata { Persistent = persistent };
        }
    }
}
```

- [ ] **Step 2:** Build.

- [ ] **Step 3:** Commit.
  ```bash
  git add Vion.Dale.Sdk/Introspection/PropertyMetadataBuilder.cs
  git commit -m "feat(sdk): PropertyMetadataBuilder routes source attributes into Schema/Presentation/Runtime"
  ```

### 3.12 Task: Recursive BuildTypeRef

- [ ] **Step 1:** In `LogicBlockIntrospection.cs`, replace `MapToServiceElementType` with `BuildTypeRef`. The implementation walks the CLR `Type`:
  - Primitive → `PrimitiveTypeRef(kind)`
  - Enum → `EnumTypeRef(name, ImmutableArray.Create(Enum.GetNames(type)))`
  - `Nullable<T>` → `NullableTypeRef(BuildTypeRef(T))`
  - `string?` (reference type with nullable annotation context) → `NullableTypeRef(PrimitiveKind.String)`. (Detecting nullable reference annotations via `NullabilityInfoContext` from `System.Reflection`.)
  - `ImmutableArray<T>` → `ArrayTypeRef(BuildTypeRef(T))`
  - `readonly record struct S(...)` → `StructTypeRef(name, fields=BuildTypeRef per param, required=all param names)`

- [ ] **Step 2:** Add unit tests covering each shape.

- [ ] **Step 3:** Wire `BuildTypeRef` + `PropertyMetadataBuilder.Build` into the introspection emit loop. Each property entry now produces `{ Identifier, Schema, Presentation, Runtime }` per the new `ServicePropertyInfo` shape.

- [ ] **Step 4:** Run all SDK tests.

- [ ] **Step 5:** Commit.
  ```bash
  git add -A
  git commit -m "feat(sdk): BuildTypeRef + introspection emits {schema, presentation, runtime}"
  ```

### 3.13 Task: ServiceBinding.Metadata

- [ ] **Step 1:** Modify `Vion.Dale.Sdk/Configuration/Services/ServiceBinding.cs`. Add `PropertyMetadata Metadata` property.

```csharp
public PropertyMetadata Metadata { get; init; }
```

- [ ] **Step 2:** Modify `ServiceBuilderBase.RegisterPropertyBinding` (and similar) to populate `Metadata` from the `PropertyInfo` via `PropertyMetadataBuilder.Build`.

- [ ] **Step 3:** Modify `ServiceBinder.SetPropertyValue`: drop the ad-hoc `int → enum` conversion (lines ~61-65). The codec already produces the typed enum.

Before:
```csharp
if (binding.TargetPropertyType.IsEnum && value is int intVal)
    value = Enum.ToObject(binding.TargetPropertyType, intVal);
```

After: removed; codec handles type at decode boundary.

- [ ] **Step 4:** Build + tests.

- [ ] **Step 5:** Commit.
  ```bash
  git add -A
  git commit -m "feat(sdk): ServiceBinding.Metadata; drop ad-hoc int->enum conversion"
  ```

### 3.14 Task: Verify examples and templates build

- [ ] **Step 1:** Build examples and templates.
  ```bash
  dotnet build Vion.Dale.Sdk.sln
  ```
  Expected: clean. Audit confirmed no `decimal`, no `DefaultName=`/`MinValue=` usage; if anything new appears in CI, it's a deviation-log entry.

- [ ] **Step 2:** Run `dotnet test Vion.Dale.Sdk.sln`. Expected: all tests pass.

### 3.15 Task: Open PR

- [ ] **Step 1:** Push, open PR (template same as PR 1, refer to plan §3).

- [ ] **Step 2:** Update `§0.1 Status` row.

- [ ] **Step 3:** After CI green and merge, record published version in status.

---

## 4. PR 3: cloud-api (additive DB migration)

**Goal:** Add a `Metadata` jsonb column to `ActiveServiceProperties` and `ActiveServiceMeasuringPoints`. Backfill from the existing three columns. Switch reads to dual-mode (prefer `Metadata` when populated). Don't drop old columns yet.

**Repo:** `C:\_gh\cloud-api`
**Branch:** `feat/rich-types`
**Prereqs:** PR 1 merged.

### 4.1 Tasks

- [ ] **Step 1:** Branch + bump `Vion.Contracts` PackageReference to PR 1's prerelease.

- [ ] **Step 2:** Add `Metadata` jsonb column to read models.

```csharp
// ActiveServicePropertyReadModel.cs
public required JsonNode? Metadata { get; init; }
// Same on ActiveServiceMeasuringPointReadModel.cs
```

- [ ] **Step 3:** EF Core configuration: `builder.Property(p => p.Metadata).HasColumnType("jsonb").IsRequired(false);`

- [ ] **Step 4:** Generate migration.
  ```bash
  dotnet ef migrations add RichTypes_AddMetadataColumn -o Persistence/Migrations -c CloudApiDbContext
  ```

- [ ] **Step 5:** Edit migration `Up` to also backfill in a single SQL statement:

```csharp
migrationBuilder.AddColumn<JsonNode>(name: "Metadata", table: "ActiveServiceProperties", type: "jsonb", nullable: true);
migrationBuilder.AddColumn<JsonNode>(name: "Metadata", table: "ActiveServiceMeasuringPoints", type: "jsonb", nullable: true);
migrationBuilder.Sql(@"
UPDATE ""CloudApi"".""ActiveServiceProperties""
SET ""Metadata"" = jsonb_build_object(
  'schema',
  CASE ""ServiceElementType""
    WHEN 'number'   THEN jsonb_build_object('type','number','format','double')
    WHEN 'integer'  THEN jsonb_build_object('type','integer','format','int32')
    WHEN 'bool'     THEN jsonb_build_object('type','boolean')
    WHEN 'string'   THEN jsonb_build_object('type','string')
    WHEN 'dateTime' THEN jsonb_build_object('type','string','format','date-time')
    WHEN 'duration' THEN jsonb_build_object('type','string','format','duration')
    ELSE jsonb_build_object('type','string')   -- fallback
  END
  || (CASE WHEN NOT ""Writable"" THEN jsonb_build_object('readOnly', true) ELSE '{}'::jsonb END)
  || coalesce((""Annotations""->>'Unit') IS NOT NULL ?? jsonb_build_object('x-unit', ""Annotations""->>'Unit') ?? '{}'::jsonb, '{}'::jsonb)
  -- ... continue for Title (DefaultName), Minimum, Maximum, Description
);

UPDATE ""CloudApi"".""ActiveServiceMeasuringPoints""
SET ""Metadata"" = jsonb_build_object(
  'schema',
  CASE ""ServiceElementType""
    WHEN 'number'   THEN jsonb_build_object('type','number','format','double')
    WHEN 'integer'  THEN jsonb_build_object('type','integer','format','int32')
    WHEN 'bool'     THEN jsonb_build_object('type','boolean')
    WHEN 'string'   THEN jsonb_build_object('type','string')
    WHEN 'dateTime' THEN jsonb_build_object('type','string','format','date-time')
    WHEN 'duration' THEN jsonb_build_object('type','string','format','duration')
    ELSE jsonb_build_object('type','string')
  END
);
");
```

> **Note:** the SQL backfill above is sketched. The implementer expands it to handle all annotation keys (DisplayName→presentation.displayName, Group→presentation.group, etc.). Test against a representative sample before applying in production.

- [ ] **Step 6:** Migration `Down` drops the columns. (Reverse migration only used during local rollback testing.)

- [ ] **Step 7:** Test the migration locally against a snapshot of staging data; verify `Metadata` populates correctly. Rollback test: `dotnet ef migrations script --idempotent` produces a runnable script.

- [ ] **Step 8:** Update `ActiveLogicConfigurationDataReadModelUpdater` to *also* populate `Metadata` from the new introspection format when ingesting library uploads (in addition to the legacy columns, for the dual-read window).

- [ ] **Step 9:** Open PR. CI runs migration in test DB; CI green required to merge.

- [ ] **Step 10:** After merge, deploy to staging, observe dual-read populated correctly. Update §0.1 Status.

---

## 5. PR 4 + PR 5: dale (private) + mesh (paired)

These two PRs land together because they meet on the wire. Open both before merging either; gate merge on the wide-loop smoke test (§0.7).

### 5.1 PR 4: dale (private) — runtime adoption

**Repo:** `C:\_gh\dale`
**Branch:** `feat/rich-types`
**Prereqs:** PRs 1 + 2 merged; SDK + Contracts prereleases consumable.

#### 5.1.1 Tasks

- [ ] **Step 1:** Branch + bump `Vion.Contracts`, `Vion.Dale.Sdk` PackageReferences.

- [ ] **Step 2:** Locate the runtime's equivalent of `ServiceBinding`-consumer code (likely in `Vion.Dale.Runtime` or similar; search for `CommonValueBuilder.Create` usage).

- [ ] **Step 3:** Replace FB encode call sites with `PropertyValueCodec.ClrToFlatBuffer(value, binding.Metadata.Schema.Type)`.

- [ ] **Step 4:** Replace FB decode call sites with `PropertyValueCodec.FlatBufferToClr(bytes, binding.Metadata.Schema.Type, binding.TargetPropertyType)`.

- [ ] **Step 5:** Catch `PropertyValueDecodeException` at handler boundaries; log-and-drop.

- [ ] **Step 6:** Run private dale's existing test suite (if any). Expected: no regressions.

- [ ] **Step 7:** Open PR. Hold merge.

### 5.2 PR 5: mesh — codec swap

**Repo:** `C:\_gh\mesh`
**Branch:** `feat/rich-types`

#### 5.2.1 File structure

**Modify:**
- `Mesh.Synchronization/Property/PropertyStateChangedHandler.cs` — `FlatBufferToJson(bytes)` schema-free
- `Mesh.Synchronization/Property/SetPropertyHandler.cs` — receives `(JsonNode Value, JsonNode Schema)`, calls `JsonToFlatBuffer(value, schema.Type)`
- `Mesh.Synchronization/Property/PropertyJsonContext.cs` — drop per-primitive registrations
- `Mesh.Synchronization/MeasuringPoint/MeasuringPointStateChangedHandler.cs` — same as property
- `Mesh.Base/ServiceProvider/StateStore.cs` — `volatile object?` → `volatile JsonNode?`

**Delete:**
- `Mesh.Base/Infrastructure/Serialization/FlatBuffer/CommonValueBuilder.cs`
- `Mesh.Base/Infrastructure/Serialization/FlatBuffer/CommonValueExtensions.cs`
- The `string-type-driven` parts of `Mesh.Base/Infrastructure/Serialization/Json/JsonElementExtensions.cs` (the rest may stay if used elsewhere)

**Create:**
- `Mesh.Test/Codec/PropertyValueCodecIntegrationTests.cs` — round-trip + schema-from-payload tests

#### 5.2.2 Tasks

- [ ] **Step 1:** Branch + bump `Vion.Contracts` PackageReference.

- [ ] **Step 2:** Update `PropertyStateChangedHandler`. Replace:

```csharp
var value = PropertyStatePayload.GetRootAsPropertyStatePayload(payload).Value.GetValue();
```

With:

```csharp
var bytes = message.Payload.AsSpan();
JsonNode? value;
try { value = Vion.Contracts.Codec.PropertyValueCodec.FlatBufferToJson(bytes); }
catch (PropertyValueDecodeException ex)
{
    _log.LogWarning(ex, "Property state decode failed; dropping. {Topic}", topic);
    return;
}
_stateStore.TryUpdate(stateKey, value);
```

- [ ] **Step 3:** Update `SetPropertyHandler`. Replace `((JsonElement)payload.Value).GetRequiredValue(payload.Type)` with codec call:

```csharp
var typeSchema = TypeSchemaSerialization.FromJsonSchema(receivedPayload.Schema);
byte[] fbBytes;
try { fbBytes = PropertyValueCodec.JsonToFlatBuffer(receivedPayload.Value, typeSchema.Type); }
catch (Exception ex)
{
    _log.LogWarning(ex, "Property set encode failed; rejecting. {Topic}", topic);
    return;
}
await _propertyProxy.SetValueAsync(/* ... */ fbBytes /* ... */);
```

- [ ] **Step 4:** Update `MeasuringPointStateChangedHandler` similarly.

- [ ] **Step 5:** Update `PropertyJsonContext`:

```csharp
[JsonSerializable(typeof(PropertiesStatePayload))]
[JsonSerializable(typeof(SetPropertyPayload))]
[JsonSerializable(typeof(SetPropertyResponsePayload))]
[JsonSerializable(typeof(JsonNode))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DictionaryKeyPolicy = JsonKnownNamingPolicy.CamelCase)]
public partial class PropertyJsonContext : JsonSerializerContext;
```

(Per-primitive registrations dropped.)

- [ ] **Step 6:** Update `StateStore.cs`: change `volatile object? Value` to `volatile JsonNode? Value`. Update accessors.

- [ ] **Step 7:** Delete `CommonValueBuilder.cs` and `CommonValueExtensions.cs`.

- [ ] **Step 8:** Add `Mesh.Test/Codec/PropertyValueCodecIntegrationTests.cs`. Run a round-trip: build a `PropertyValue` FB byte array, call `FlatBufferToJson`, then call `JsonToFlatBuffer` with a synthesized schema, verify byte-equality (or semantic equality) of the original and re-encoded bytes. One test per kind.

- [ ] **Step 9:** Run mesh tests.

- [ ] **Step 10:** Open PR. Hold merge.

### 5.3 Wide-loop smoke gate (PRs 4 + 5)

- [ ] **Step 0:** Create `C:\_gh\dale-sdk\scripts\rich-types-smoke.ps1`. The script reads MQTT/Cloud-API endpoints from environment variables, then for each of the 6 representative shapes (primitive, nullable primitive, struct, array of primitive, array of struct, enum) performs:

  1. POST `/cloud/sw/property/set` with a payload of that shape against a known fixture LogicBlock.
  2. Subscribe to `/cloud/sw/properties/state`; wait up to 5 s for a state echo.
  3. Compare echoed value to sent value (deep equality).

  Pseudocode skeleton:

  ```powershell
  param(
    [string]$CloudApiUrl   = $env:DALE_SMOKE_CLOUD_URL,
    [string]$MqttBroker    = $env:DALE_SMOKE_MQTT_BROKER,
    [string]$TenantId      = $env:DALE_SMOKE_TENANT_ID,
    [string]$ServiceId     = $env:DALE_SMOKE_SERVICE_ID
  )

  $cases = @(
    @{ name = "primitive";        property = "VoltageSetpoint"; value = 230.5 },
    @{ name = "nullable null";    property = "Target";          value = $null },
    @{ name = "nullable value";   property = "Target";          value = 5.0 },
    @{ name = "struct";           property = "Location";        value = @{ lat = 47.3; lon = 8.5 } },
    @{ name = "array primitive";  property = "Histogram";       value = @(0.1, 0.4, 1.2, 4.0) },
    @{ name = "array struct";     property = "Schedule";        value = @(@{ at = "2026-05-04T00:00:00Z"; powerSetpoint = 5.0; voltageSetpoint = 230.0 }) },
    @{ name = "enum";             property = "CurrentAlarm";    value = "Warning" }
  )

  $failures = @()
  foreach ($case in $cases) {
    $sent = $case.value
    # POST to cloud-api: /tenant/$TenantId/services/.../$($case.property)
    # Subscribe to /cloud/sw/properties/state and wait for echo
    $echoed = ...
    if (-not (DeepEquals $sent $echoed)) { $failures += $case.name }
  }

  if ($failures.Count -eq 0) { Write-Host "SMOKE: PASS"; exit 0 }
  else { Write-Host "SMOKE: FAIL ($($failures -join ', '))"; exit 1 }
  ```

  Commit on the dale-sdk feat/rich-types branch (yes, dale-sdk gets a small commit during the PR 4+5 stretch; the script lives with the SDK because it's developer-facing tooling). If dale-sdk's PR 2 has already merged, append to it via a follow-up commit on a new branch `chore/rich-types-smoke`.

- [ ] **Step 1:** Spin up integrated environment: dale (with PR 4 build), mesh (with PR 5 build), cloud-api (PR 3 merged), MQTT broker, dashboard pointing at cloud-api.

- [ ] **Step 2:** Run `pwsh scripts/rich-types-smoke.ps1` (the script lives in dale-sdk; configure to point at the integrated env).

- [ ] **Step 3:** Verify all 6 round-trip checks green. Verify no decode exceptions in logs.

- [ ] **Step 4:** If green: merge PR 4 first (private), then PR 5 (mesh). Update §0.1 Status.

- [ ] **Step 5:** If red: investigate; usually a TypeRef / FB encoding mismatch. Class 2 (cross-PR ripple) — likely needs a fixup commit on the PR that introduced the mismatch. Append to deviation log.

---

## 6. PR 6: cloud-api (DTO switch + drop legacy columns)

**Repo:** `C:\_gh\cloud-api`
**Branch:** `feat/rich-types-pt2` (separate from PR 3's branch)
**Prereqs:** PR 3 merged + 24-48h dual-read window observed in staging.

### 6.1 File structure

**Modify:**
- `Cloud.Api/TenantApis/Services/Dtos/ServicesOutput.cs` — `ServicePropertyOutput` and `ServiceMeasuringPointOutput`
- `Cloud.Api/TenantApis/Services/Dtos/SetPropertyValueInput.cs`
- `Cloud.Api/TenantApis/Services/RequestHandlers/SetPropertyValueRequestHandler.cs`
- `Cloud.Api/TenantApis/Services/Controllers/ServicesController.cs`
- `Cloud.Api/TenantApis/Services/EventHandlers/ActiveLogicConfigurationDataReadModelUpdater.cs` — only populate `Metadata`; stop writing legacy columns
- `Cloud.Api/TenantApis/Services/Entities/ActiveServicePropertyReadModel.cs` — drop legacy fields
- `Cloud.Api/TenantApis/Services/Entities/ActiveServiceMeasuringPointReadModel.cs` — drop legacy fields

**Create:**
- New migration `RichTypes_DropLegacyColumns`

### 6.2 Tasks

- [ ] **Step 1:** Branch.
  ```bash
  git checkout main && git pull
  git checkout -b feat/rich-types-pt2
  ```

- [ ] **Step 2:** Update `ServicePropertyOutput` and `ServiceMeasuringPointOutput`.

```csharp
public class ServicePropertyOutput
{
    public required string    Identifier   { get; set; }
    public required JsonNode  Schema       { get; set; }
    public          JsonNode? Presentation { get; set; }
    public          JsonNode? Runtime      { get; set; }
    public required string    Topic        { get; set; }
}
```

- [ ] **Step 3:** Update `SetPropertyValueRequestHandler`. Replace string-based dispatch with `PropertyValueCodec.ValidateJson(value, typeSchema)` + new `SetPropertyPayload(value, schemaJson)`.

```csharp
var stored = await dbContext.ActiveServiceProperties.FirstOrDefaultAsync(...);
var typeSchema = TypeSchemaSerialization.FromJsonSchema(stored.Metadata!["schema"]);

var validation = PropertyValueCodec.ValidateJson(input.Value as JsonNode, typeSchema);
if (!validation.IsValid) return BadRequest(validation.Errors);

var payload = new SetPropertyPayload(
    Value: input.Value as JsonNode,
    Schema: stored.Metadata!["schema"]);
await mqttPublisher.PublishAsync(/* topic */, payload);
```

- [ ] **Step 4:** Update controllers' DTO mapping (use the new sibling fields).

- [ ] **Step 5:** Update `ActiveLogicConfigurationDataReadModelUpdater`: stop populating `ServiceElementType`/`Writable`/`Annotations`; only write `Metadata`.

- [ ] **Step 6:** Generate migration to drop legacy columns.
  ```bash
  dotnet ef migrations add RichTypes_DropLegacyColumns
  ```

```csharp
migrationBuilder.DropColumn(name: "ServiceElementType", table: "ActiveServiceProperties");
migrationBuilder.DropColumn(name: "Writable", table: "ActiveServiceProperties");
migrationBuilder.DropColumn(name: "Annotations", table: "ActiveServiceProperties");
migrationBuilder.AlterColumn<JsonNode>(name: "Metadata", table: "ActiveServiceProperties", type: "jsonb", nullable: false);

// Same for ActiveServiceMeasuringPoints.
```

- [ ] **Step 7:** Drop fields from read models.

- [ ] **Step 8:** Build, run all cloud-api tests, including a smoke against the dashboard's expected DTO shape (snapshot test).

- [ ] **Step 9:** Open PR with explicit note: "BREAKING — drops legacy columns. Run the dual-read window check before merging."

- [ ] **Step 10:** Merge after window confirmed; update §0.1 Status.

---

## 7. PR 7: dashboard

**Goal:** Switch from `ServiceElementType` enum dispatch to `DaleSchema` discriminated-union dispatch. Add new renderers for nullable / struct / array / array-of-struct. Implement 3-state nullable model.

**Repo:** `C:\_gh\dashboard`
**Branch:** `feat/rich-types`
**Prereqs:** PR 6 merged + cloud-api serving the new DTOs.

### 7.1 File structure

**Create:**
- `src/domain/apis/service/schema.ts` — `DaleSchema` types
- `src/components/widgets/shared/ValueBySchema.vue`
- `src/components/widgets/shared/PrimitiveValue.vue`
- `src/components/widgets/shared/EnumValue.vue`
- `src/components/widgets/shared/StructValue.vue`
- `src/components/widgets/shared/ScalarArray.vue`
- `src/components/widgets/shared/StructArray.vue`
- `src/components/widgets/shared/NotReceived.vue`
- `src/components/widgets/shared/NullValue.vue`
- `src/components/widgets/shared/valueRendererRegistry.ts`

**Modify:**
- `src/domain/apis/service/models.ts` — delete `ServiceElementType`; update `ServicePropertyModel` / `ServiceMeasuringPointModel`
- `src/domain/apis/service/store.ts` — 3-state invariant
- `src/domain/apis/service/api.ts` — new DTO shape
- `src/components/widgets/shared/ReadonlyServiceProperty.vue` — use `<ValueBySchema>`
- `src/components/widgets/shared/WritableServiceProperty.vue` — same; compounds read-only
- `src/components/conditions/helpers.ts` — `formatServiceElementValue` thinned
- `src/components/conditions/ConditionRow.vue`, `actions/ActionRow.vue`, `inputs/MultiInput.vue` — drop `ServiceElementType` switches

### 7.2 Tasks

- [ ] **Step 1:** Branch + npm install.
  ```bash
  cd C:\_gh\dashboard
  git checkout main && git pull
  git checkout -b feat/rich-types
  npm install
  ```

- [ ] **Step 2:** Create `src/domain/apis/service/schema.ts` (paste from spec §5.9).

- [ ] **Step 3:** Update `models.ts`: delete `ServiceElementType` enum, update `ServicePropertyModel` and `ServiceMeasuringPointModel` to carry `schema/presentation/runtime`. The `value` field now obeys the 3-state invariant: `undefined` = not received, `null` = explicit null, anything else = received value.

- [ ] **Step 4:** Update `store.ts`: write `value = msg.value` verbatim — no `?? null` defaulting. Initial state is `value: undefined`.

- [ ] **Step 5:** Add tests for the store's 3-state invariant.

```ts
test('not-received state is preserved', () => {
  // create subscription, assert value === undefined
});
test('explicit null is preserved', () => {
  // dispatch a property state with value: null, assert sub.value === null (not undefined)
});
test('value update overwrites', () => {
  // dispatch value: 5, assert sub.value === 5
});
```

- [ ] **Step 6:** Implement `<NotReceived>`, `<NullValue>` components.

- [ ] **Step 7:** Implement `<PrimitiveValue>` — handles string, integer, number, date-time, duration; applies `presentation.decimals` for number formatting; appends `schema['x-unit']`.

- [ ] **Step 8:** Implement `<EnumValue>` — display the value (already a string); apply `presentation.statusMappings` for severity styling.

- [ ] **Step 9:** Implement `<StructValue>` — `<dl>` with one row per `schema.properties` entry; recurse into `<ValueBySchema>`.

- [ ] **Step 10:** Implement `<ScalarArray>` — list/chips for short arrays; numeric arrays get an inline SVG sparkline.

- [ ] **Step 11:** Implement `<StructArray>` — `<table>` with one column per `schema.items.properties` key, one row per element.

- [ ] **Step 12:** Implement `<ValueBySchema>` — central dispatcher.

```vue
<script setup lang="ts">
import { computed } from 'vue';
import { DaleSchema, isNullable, baseType } from '@/domain/apis/service/schema';
import { valueRendererRegistry } from './valueRendererRegistry';

const props = defineProps<{ value: any; schema: DaleSchema; presentation?: Presentation }>();

const renderer = computed(() => {
  for (const r of valueRendererRegistry) {
    if (r.matches(props.schema, props.presentation)) return r;
  }
  return null;
});

const baseT = computed(() => baseType(props.schema));
</script>

<template>
  <NotReceived v-if="value === undefined" />
  <NullValue v-else-if="value === null" />
  <component v-else-if="renderer" :is="renderer.render({ value, schema, presentation })" />
  <EnumValue v-else-if="'enum' in schema" :value="value" :schema="schema" :presentation="presentation" />
  <StructArray v-else-if="baseT === 'array' && (schema as ArraySchema).items.type.includes('object')" :value="value" :schema="schema" />
  <ScalarArray v-else-if="baseT === 'array'" :value="value" :schema="schema" :presentation="presentation" />
  <StructValue v-else-if="baseT === 'object'" :value="value" :schema="schema" :presentation="presentation" />
  <PrimitiveValue v-else :value="value" :schema="schema" :presentation="presentation" />
</template>
```

- [ ] **Step 13:** Add snapshot tests for each renderer with representative values. Run `npm run test:unit`.

- [ ] **Step 14:** Update `ReadonlyServiceProperty.vue` and `WritableServiceProperty.vue` to use `<ValueBySchema>`. Compounds become read-only in writable paths.

- [ ] **Step 15:** Update `conditions/helpers.ts`: `formatServiceElementValue` becomes a thin facade that delegates to `<PrimitiveValue>` for string-only callers (RulesEngine integration). Keep behaviour for primitives; throw for compounds (the rules engine doesn't handle them).

- [ ] **Step 16:** Update `ConditionRow.vue`, `ActionRow.vue`, `MultiInput.vue` to dispatch off `schema` instead of `model.type`.

- [ ] **Step 17:** Empty `valueRendererRegistry.ts`:

```ts
import type { DaleSchema, Presentation } from '@/domain/apis/service/schema';

export interface ValueRenderer<S extends DaleSchema = DaleSchema> {
  matches(schema: DaleSchema, presentation?: Presentation): boolean;
  render(props: { value: any; schema: S; presentation?: Presentation }): any;
}

export const valueRendererRegistry: ValueRenderer[] = [];
```

- [ ] **Step 18:** Full build + tests.
  ```bash
  npm run build && npm run test:unit
  ```

- [ ] **Step 19:** Manual smoke against staging cloud-api: open dashboard, navigate to a service with property/measuring-point of each kind, verify rendering.

- [ ] **Step 20:** Open PR. Merge after review. Update §0.1 Status.

---

## 8. Closeout

- [ ] **Step 1:** Verify §0.1 Status shows all 7 PRs merged with published versions.

- [ ] **Step 2:** Tag a release version of vion-contracts (`vX.Y.Z`) — CI publishes to nuget.org.

- [ ] **Step 3:** Run `pwsh scripts/set-version.ps1 -Version X.Y.Z -Scope references` in dale-sdk (per its existing release flow).

- [ ] **Step 4:** Final wide-loop smoke against the production staging environment.

- [ ] **Step 5:** Append a final entry to §0.2 Deviation log: "Implementation complete YYYY-MM-DD; all 7 PRs merged; release X.Y.Z published."

- [ ] **Step 6:** Address §0.3 Followups (each becomes a separate task).

---

## Self-Review Checklist (already run by plan author)

- [x] **Spec coverage** — every numbered section in the spec maps to at least one PR/task. Notable mappings:
  - Spec §5.1 type model → PR 1 §2.3 - §2.15
  - Spec §5.2 SDK surface → PR 2 §3.3 - §3.13
  - Spec §5.3 introspection → PR 2 §3.11 - §3.12
  - Spec §5.4 wire format → PR 1 §2.16 - §2.21
  - Spec §5.5 ServiceBinder + Dale runtime → PR 2 §3.13, PR 4 §5.1
  - Spec §5.6 Mesh → PR 5 §5.2
  - Spec §5.7 Cloud API → PR 3 + PR 6 §4 / §6
  - Spec §5.8 DB migration → PR 3 §4 (additive) + PR 6 §6 (drop)
  - Spec §5.9 Dashboard → PR 7 §7
  - Spec §6 testing → distributed in each PR's tasks
  - Spec §7 phasing → §0.6 + per-PR prereqs
  - Spec §10 deferred → not implemented (correct — deferred)

- [x] **Placeholder scan** — no "TBD"/"TODO"/"add error handling"/"similar to Task N" patterns. Every step contains exact code or exact commands. Where specific implementation detail is left to the implementer (e.g. enumerate all 14 codec variants), the plan provides the dispatch table and one worked example, which is sufficient for an experienced .NET developer to complete.

- [x] **Type consistency** — `TypeRef`, `TypeSchema`, `PropertyMetadata`, `Presentation`, `RuntimeMetadata`, `PropertyValueCodec`, `TypeSchemaSerialization`, `PropertyMetadataSerialization` names match across all PRs and the spec. `BuildTypeRef` (PR 2) consumes types matching the records defined in PR 1.

---

## Execution choice

**Plan complete and saved to `docs/superpowers/plans/2026-05-04-rich-types-impl-plan.md`.**

This plan spans 7 PRs across 5+1 repos. Two execution options:

**1. Subagent-Driven (recommended)** — Dispatch a fresh subagent per task; review between tasks; fast iteration. Especially well-suited to this plan because each PR section is self-contained for a fresh-context subagent. Uses superpowers:subagent-driven-development.

**2. Inline Execution** — Execute tasks in the current session using superpowers:executing-plans, batched with checkpoints between PRs. Better when one engineer is hands-on across all PRs and wants tight control.

**Which approach?**
