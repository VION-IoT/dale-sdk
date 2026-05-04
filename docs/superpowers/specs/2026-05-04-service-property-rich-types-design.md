# Rich Data Types for ServiceProperty and ServiceMeasuringPoint (v2)

**Status:** Design, awaiting review
**Date:** 2026-05-04
**Supersedes:** [2026-04-17-service-property-rich-types-design.md](2026-04-17-service-property-rich-types-design.md)
**Scope:** `dale-sdk`, `vion-contracts`, `mesh`, `cloud-api`, `dashboard`
**Author / sponsor:** Jonas Bertsch

## 1. Summary

The Vion Dale SDK today restricts `[ServiceProperty]` and `[ServiceMeasuringPoint]` values to a flat whitelist of primitive types (`bool, string, int, long, short, float, double, decimal, DateTime, TimeSpan`, any `enum`). This blocks common IoT modelling patterns: nullable values without sentinel hacks, named composite values (coordinates, three-phase currents), and variable-length samples (histogram buckets, time series, scheduled set-points).

This spec extends the permitted types to:

- **Nullable primitives and nullable enums** (`double?`, `MyEnum?`)
- **Flat immutable structs** (`readonly record struct Coordinates(double Lat, double Lon)`) — fields must themselves be primitives or nullable-primitives-or-enums
- **Arrays** of primitives, enums, nullable-primitives-or-enums, and structs, carried as `ImmutableArray<T>`
- **Nullable struct** (`Coordinates?`)
- Any non-nested composition of the above (e.g. `ImmutableArray<Coordinates?>`)

`decimal` is **removed** from the primitive whitelist. No public LogicBlocks use it today; the existing examples and templates contain zero `decimal` properties.

The design uses a constrained profile of **JSON Schema 2020-12** (the draft OpenAPI 3.1 aligns with) as the canonical external type language for *data shape*. UI hints and runtime behavior live in two sibling documents (`presentation`, `runtime`), keeping the data schema clean. Value payloads (FlatBuffers Dale↔Mesh, JSON Mesh↔Cloud↔UI) are extended once via a small set of FB union variants.

**Mesh stays schema-free.** Today Mesh decodes Dale-side FlatBuffers using the FB tag tree and encodes Cloud-side JSON using a per-message type hint in `SetPropertyPayload`. We preserve that pattern: schema-with-payload on the Cloud→Mesh set path, schema-blind FB→JSON on the Dale→Mesh state path. Schemas are library-pinned (Cloud parses on upload; Dale parses on load — same parser, same artifact, deterministic).

## 2. Goals

- **(A, non-negotiable)** Authors of LogicBlocks model real IoT data naturally — no sentinel hacks, no parallel properties for split coordinates, no inability to express a 96-entry parameter schedule.
- **(B, non-negotiable)** The external type language is a genuine, conformant subset of JSON Schema 2020-12. Standard adherence — not tooling consumption — is the bar; we use `x-` extensions only where the standard genuinely runs out.
- **(C, nice-to-have)** A future Python or TypeScript Dale runtime emits and consumes bytes indistinguishable from C# Dale for the same TypeRef. Wire format choices are reviewed through this lens.

## 3. Non-goals

- **Recursive struct composition.** Struct fields stay primitive-only; no struct-of-struct, no struct-with-array-field, no arrays-of-arrays.
- **Writable compound-type UI editors.** Structs, arrays, and arrays-of-structs render read-only in the dashboard v1. Cloud API still accepts writes from other clients (cloud-side schedulers, partner systems).
- **Per-struct custom visualizations** (map pin, chart) in v1 — only the registry hook ships.
- **Time-series storage** of measuring-point arrays in TimescaleDB. Cloud-side concern, decoupled.
- **Backwards-compatible wire format.** No public LogicBlocks consume the SDK today; coordinated rollout across packages is internal.
- **Keeping `decimal` in the whitelist.** Removed.
- **Runtime-published schemas via retained MQTT.** Schemas are library-pinned and parsed deterministically; no `/sw/introspection/{serviceId}/state` topic, no Mesh schema cache, no race buffer.

## 4. Open decisions taken during brainstorming

| Topic | Decision | Rationale |
|---|---|---|
| Forcing function | Author ergonomics (A) and JSON Schema standard adherence (B) both non-negotiable; Python parity (C) nice-to-have | Sets the bias for every trade-off below |
| Scope phasing | Single coordinated release across all 5 repos in dependency order | No public consumers; no value in a half-state |
| Type-tag model | Orthogonal structured TypeRef (not flat strings, not hybrid) | Composes cleanly; struct schemas need to travel somewhere anyway |
| Canonical type language | JSON Schema 2020-12, constrained to a "Dale profile" | (B) standard adherence; free pydantic / NJsonSchema / json-schema-to-ts gen for free |
| Annotation split | Three sibling documents per property: `schema` (data shape, JSON Schema profile), `presentation` (UI hints), `runtime` (behavior) | Keeps the JSON Schema clean; non-shape concerns don't pollute it |
| Schema source of truth | Library binary, parsed deterministically. Cloud parses on upload (today's path); Dale parses on load. No runtime publish | Single source; no Cloud-DB-vs-Mesh-cache drift; same parser everywhere |
| Mesh schema model | Schema-free | Today's invariant; runtime schema declaration via retained MQTT was an unnecessary regression |
| Set-path mechanism | `SetPropertyPayload` carries the schema alongside the value | Mesh stays stateless; same pattern as today's `Type` hint, expanded to a JSON Schema |
| Enum wire form (everywhere) | Member name string, end-to-end | Idiomatic JSON Schema / OpenAPI; no translation hop; cross-language-natural (pydantic StrEnum, TS string-literal unions, C# `Enum.GetName` / `Enum.Parse`). Drops the `x-enum-values` extension and the int↔name translation logic in Cloud |
| Nullable transport | 2-state on the wire (null \| value); "not yet received" = no retained MQTT message | Matches MQTT retention semantics; UI distinguishes via cache lookup |
| Struct depth | Flat — struct fields are primitives (+ nullable, + enum) | Motivating use cases are flat; keeps wire & UI shallow |
| Wire variant count | ~14 FB union variants, not 33; numeric precision narrowing happens at the C# binding boundary using schema | Cross-language-natural; Python/TS would emit one numeric variant anyway. Codec is half the size |
| Wire philosophy | FB: explicit `union` with built-in tag; JSON: contextual, no `$type` tag | Idiomatic for each format |
| Struct identity | `(Title, ordered fields, ordered required)` — `Title` identity-bearing for nominal disambiguation, mirroring enum identity | Two semantically-different structs that happen to share the same field shape stay distinct; registry-hook matching (`title === "Coordinates"`) is unambiguous |
| `decimal` | Dropped from whitelist | Simpler wire; no native support in FB/JSON/Python; zero existing usage |
| Collection type | `ImmutableArray<T>` required (not `T[]`, not `List<T>`) | Prevents in-place mutation trap with `[Observable]` |
| Measuring points | In scope — same type system, same wire | Code paths already unified; arrays more natural for metrics |
| Annotation granularity | Annotations attach to leaves: property-level for primitives/arrays, per-struct-field inside structs | Unlocks `Coordinates3D(Lat°, Lon°, Altitude m)` |
| UI ambition v1 | Generic per-kind renderers + empty registry hook | Bounded; extensible |
| SDK writability of compounds | Allowed — UI-only read-only | Writability is a UI concern, not an attribute concern |
| Identity / annotation split | `TypeRef` carries identity-bearing fields only; `TypeAnnotations` carries display + validation; `TypeSchema` is the pair | Default record equality on `TypeRef` is type identity (codec, registry, cache get it free) |
| Cloud DB column shape | Single composite `metadata` jsonb column containing `{ schema, presentation, runtime }` | One fetch, one document; no use case for indexing presentation/runtime independently |
| Existing presentation attributes | Keep `[Display]`, `[Category]`, `[Importance]`, `[UIHint]`, `[StatusIndicator]` as-is; route their values into the `presentation` block | Minimal source-side disruption; UI is fluid and may drop them later |
| `[Persistent]` | Routes into `runtime.persistent` | Behavior, not data shape, not UI |

## 5. Design

### 5.1 Type model — Dale profile of JSON Schema 2020-12

**Canonical external form:** each property emits three sibling documents:

```
{
  "schema":       <JSON Schema, Dale profile>,    // data shape only — what the value IS
  "presentation": <Presentation document> | null, // UI hints — how the value is displayed
  "runtime":      <Runtime document> | null       // dale-runtime behavior — persistence etc.
}
```

`schema` is the only piece that must round-trip with the wire format. `presentation` and `runtime` are advisory — Mesh and the codec ignore them entirely.

**Internal C# form:** `Vion.Contracts` exposes typed records for all three documents. The schema layer keeps the spec-v1 identity-vs-annotations split (codec dispatch, registry matching, and DB deduplication compare `TypeRef` directly via default record equality).

```csharp
// ─────────────── Schema layer — data shape ───────────────

public abstract record TypeRef;

public sealed record PrimitiveTypeRef(PrimitiveKind Kind) : TypeRef;

public sealed record EnumTypeRef(
    string Title,                              // identity-bearing — nominal disambiguation
    ImmutableArray<string> Members) : TypeRef;  // ordered member names; integer underlying values are a C#-side concern, not on the wire

public sealed record StructTypeRef(
    string Title,                              // identity-bearing — nominal disambiguation, mirrors EnumTypeRef
    ImmutableArray<StructField> Fields,        // ordered
    ImmutableArray<string> Required) : TypeRef;

public sealed record ArrayTypeRef(TypeRef Items) : TypeRef;
public sealed record NullableTypeRef(TypeRef Inner) : TypeRef;

public sealed record StructField(string Name, TypeRef Type);

public enum PrimitiveKind { Bool, String, Short, Int, Long, Float, Double, DateTime, Duration }

// Annotations layer — JSON Schema keywords that don't bear identity
public sealed record TypeAnnotations
{
    public string? Title       { get; init; }   // → JSON Schema "title"
    public string? Description { get; init; }   // → "description"
    public string? Unit        { get; init; }   // → "x-unit"
    public double? Minimum     { get; init; }   // → "minimum"
    public double? Maximum     { get; init; }   // → "maximum"
    public bool    ReadOnly    { get; init; }   // → "readOnly" (property-level only)

    public static readonly TypeAnnotations None = new();
}

public sealed record TypeSchema(
    TypeRef Type,
    TypeAnnotations Annotations,
    ImmutableDictionary<string, TypeAnnotations> StructFieldAnnotations);

// ─────────────── Presentation layer — UI hints ───────────────

public sealed record Presentation
{
    public string?  DisplayName { get; init; }                  // overrides schema.title for UI
    public string?  Group       { get; init; }
    public int?     Order       { get; init; }
    public string?  Category    { get; init; }
    public string?  Importance  { get; init; }
    public string?  UIHint      { get; init; }
    public int?     Decimals    { get; init; }
    public ImmutableDictionary<string, string>? StatusMappings { get; init; }  // enum-member name → severity (Ok/Warning/Critical)

    public static readonly Presentation None = new();
}

// ─────────────── Runtime layer — dale-runtime behavior ───────────────

public sealed record RuntimeMetadata
{
    public bool Persistent { get; init; }

    public static readonly RuntimeMetadata None = new();
}

// ─────────────── Combined per-property metadata ───────────────

public sealed record PropertyMetadata(
    TypeSchema       Schema,
    Presentation     Presentation,
    RuntimeMetadata  Runtime);

// ─────────────── Serialization ───────────────

// Round-trips a TypeSchema (data-shape only) through the JSON Schema 2020-12 Dale profile.
// Used by Mesh (decoding the `schema` field of SetPropertyPayload), by Cloud's validator,
// and internally by PropertyMetadataSerialization.
public static class TypeSchemaSerialization
{
    public static JsonNode   ToJsonSchema(this TypeSchema schema);
    public static TypeSchema FromJsonSchema(JsonNode jsonSchema);  // rejects non-profile schemas
}

// Round-trips the full per-property document {schema, presentation, runtime}.
// Used by introspection emit/consume and by Cloud's `ServicePropertyOutput` mapping.
public static class PropertyMetadataSerialization
{
    public static JsonNode         ToJson(this PropertyMetadata metadata);
    public static PropertyMetadata FromJson(JsonNode json);
}
```

**Primitive → JSON Schema mapping:**

| `PrimitiveKind` | JSON Schema |
|---|---|
| `Bool`     | `{"type":"boolean"}` |
| `String`   | `{"type":"string"}` |
| `Short`    | `{"type":"integer","format":"int16"}` |
| `Int`      | `{"type":"integer","format":"int32"}` |
| `Long`     | `{"type":"integer","format":"int64"}` |
| `Float`    | `{"type":"number","format":"float"}` |
| `Double`   | `{"type":"number","format":"double"}` |
| `DateTime` | `{"type":"string","format":"date-time"}` — RFC 3339 string, ms precision |
| `Duration` | `{"type":"string","format":"duration"}` — ISO 8601 duration string, ms precision |

**Composite → JSON Schema mapping:**

- **Nullable of T:** widen `T`'s `type` keyword from `X` to `[X, "null"]`. Recursive.
- **Enum:** `{"type":"string","enum":["Ok","Warning","Critical"],"title":"AlarmState"}`. JSON value is the member name string everywhere (Mesh↔Cloud, Cloud↔UI, FB wire). Idiomatic JSON Schema / OpenAPI; no extension required. The C# enum's underlying integer is a binding-boundary concern — the codec calls `Enum.GetName` / `Enum.Parse` to bridge — and never appears on the wire.
- **Struct:** `{"type":"object","title":"Coordinates3D","properties":{…},"required":["lat","lon","altitude"],"additionalProperties":false}`.
- **Array:** `{"type":"array","items":{…}}`.

**Full example — `ImmutableArray<ScheduledSetpoint>`:**

```json
// schema
{
  "type": "array",
  "items": {
    "type": "object",
    "title": "ScheduledSetpoint",
    "properties": {
      "at":             {"type":"string","format":"date-time"},
      "powerSetpoint":  {"type":"number","format":"double","x-unit":"kW"},
      "voltageSetpoint":{"type":"number","format":"double","x-unit":"V"}
    },
    "required": ["at","powerSetpoint","voltageSetpoint"],
    "additionalProperties": false
  }
}

// presentation, runtime — both null/absent for this property
```

**Identity rules** — encoded by the C# records via default record equality:

- **Primitives:** by `Kind` (1:1 with `(type, format)` in JSON Schema).
- **Enums:** by `(Title, Members)` — `Title` is identity-bearing.
- **Structs:** by `(Title, Fields, Required)` — `Title` is identity-bearing, mirroring enums. Two semantically-different structs that happen to share the same field shape (e.g. `Coordinates(Lat, Lon)` vs. `Pressure(Min, Max)`) stay distinct types.
- **Arrays / Nullables:** by `Items` / `Inner`.
- **Annotations** (`Description`, `Unit`, `Minimum`, `Maximum`, `ReadOnly` — note `Title` is *not* annotation; it's an identity-bearing field on `EnumTypeRef` / `StructTypeRef`. For primitives, arrays, and nullables, `Title` is annotation only) live in `TypeAnnotations` / `TypeSchema.StructFieldAnnotations` and never affect `TypeRef` equality.

**Appendix A — Dale profile of JSON Schema 2020-12**

A schema is a valid Dale TypeRef iff it matches one of the productions in the mapping tables above. The Dale codec accepts:

- `type` as a single string from `{"boolean","string","integer","number","array","object"}` or a two-element array `[X, "null"]` for nullability.
- `format` only as listed in the primitive mapping.
- `enum` only with `type: "string"` — array of member name strings.
- `properties` + `required` + `additionalProperties: false` for structs.
- `items` as a single subschema for arrays.
- Optional display keywords: `title`, `description`.
- Optional numeric constraints: `minimum`, `maximum` — inclusive bounds only.
- The `x-unit` extension for physical unit annotations.
- The `readOnly: true` keyword to mark non-writable elements.

**Explicitly rejected** (codec `FromJson` throws `InvalidSchemaException`):

- `$ref`, `$dynamicRef`, `$defs` — everything is inline; struct identity is by shape.
- `oneOf`, `anyOf`, `allOf`, `not`, `if`/`then`/`else`.
- `patternProperties`, `additionalProperties: true`, `additionalProperties: <schema>`.
- `minLength`, `maxLength`, `pattern`, `minItems`, `maxItems`, `uniqueItems` (deferred — see §10).
- `exclusiveMinimum`, `exclusiveMaximum`, `multipleOf`.
- Nested arrays and array-of-object whose object has nested object/array properties.
- `format` values outside the listed set.

Profile conformance is checked at every producer/consumer boundary. A non-conforming schema is rejected at source.

### 5.2 SDK surface (C# API)

User-facing: plain C# properties with existing attributes, expanded type whitelist. No new required attribute for the common cases.

```csharp
public class InverterService
{
    [ServiceProperty(Unit = "V", Minimum = 0)]
    public double VoltageSetpoint { get; set; }

    [ServiceProperty]
    public double? Target { get; set; }

    [ServiceMeasuringPoint(Unit = "A")]
    public ImmutableArray<double> HistogramBuckets { get; private set; } = ImmutableArray<double>.Empty;

    [ServiceMeasuringPoint]
    public Coordinates Location { get; private set; }

    [ServiceMeasuringPoint]
    public ImmutableArray<Coordinates> Route { get; private set; } = ImmutableArray<Coordinates>.Empty;

    [ServiceMeasuringPoint]
    public Coordinates? LastKnownLocation { get; private set; }

    [ServiceMeasuringPoint]
    public AlarmState? CurrentAlarm { get; private set; }

    [ServiceMeasuringPoint]
    public string? ErrorMessage { get; private set; }

    [ServiceProperty]
    public ImmutableArray<ScheduledSetpoint> Schedule { get; set; } = ImmutableArray<ScheduledSetpoint>.Empty;
}

public readonly record struct Coordinates(double Lat, double Lon);

public readonly record struct Coordinates3D(
    [StructField(Unit = "deg", Minimum = -90,  Maximum = 90)]  double Lat,
    [StructField(Unit = "deg", Minimum = -180, Maximum = 180)] double Lon,
    [StructField(Unit = "m")]                                  double Altitude);

public readonly record struct ScheduledSetpoint(
    DateTime At,
    [StructField(Unit = "kW")] double PowerSetpoint,
    [StructField(Unit = "V")]  double VoltageSetpoint);

public enum AlarmState { Ok, Warning, Critical }
```

**New attribute** — property names mirror JSON Schema keywords:

```csharp
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
public sealed class StructFieldAttribute : Attribute
{
    public string? Title       { get; init; }
    public string? Description { get; init; }
    public string? Unit        { get; init; }
    public double Minimum      { get; init; } = double.NegativeInfinity;
    public double Maximum      { get; init; } = double.PositiveInfinity;
}
```

**`ServicePropertyAttribute` / `ServiceMeasuringPointAttribute` rename** with `[Obsolete]` shims for one minor release:

| Old name | New name | JSON Schema keyword |
|---|---|---|
| `DefaultName` | `Title` | `title` |
| `Unit` | (unchanged in C#) | `x-unit` |
| `MinValue` | `Minimum` | `minimum` |
| `MaxValue` | `Maximum` | `maximum` |

`ServiceMeasuringPointAttribute` gains `Minimum` and `Maximum` for parity with `ServicePropertyAttribute`.

**Existing attributes routing into `presentation` / `runtime`:**

| Attribute | Field | Lands in |
|---|---|---|
| `[Display(Name)]` | name | `presentation.displayName` |
| `[Display(Group)]` | group | `presentation.group` |
| `[Display(Order)]` | order | `presentation.order` |
| `[Category]` | name | `presentation.category` |
| `[Importance]` | level | `presentation.importance` |
| `[UIHint]` | hint | `presentation.uiHint` |
| `[StatusIndicator]` on enum | per-member severity | `presentation.statusMappings` (member name → severity) |
| `[Persistent]` | flag | `runtime.persistent` |

The introspection emitter routes them into the appropriate sibling document. No SDK author changes.

**Declaration rules** (analyzer-enforced):

- Struct used as a service-element value must be `readonly record struct` with flat primitive/enum/nullable-primitive-or-enum fields only.
- Array-valued service elements must be `ImmutableArray<T>`. `T[]`, `List<T>`, `IReadOnlyList<T>`, `IEnumerable<T>` produce a diagnostic with a code-fix suggesting `ImmutableArray<T>`.
- `string` on a service element must be explicitly `string?` if null is a valid value; `string` alone is a non-null contract.
- `ImmutableArray<T>` properties must be initialised — otherwise `IsDefault == true` throws at publish time.

**Analyzer changes** (in `Vion.Dale.Sdk.Generators`). Existing IDs DALE001-DALE015 are taken; new diagnostics start at DALE008 for the next free slot, then DALE016+:

| Diagnostic | Severity | Rule |
|---|---|---|
| DALE003 (changed) | error | Whitelist expanded to recursive validation over the full TypeRef-expressible set. `decimal` removed |
| DALE004 (unchanged) | error | Measuring-point read-only |
| DALE008 (new) | error | Array-valued service element must be `ImmutableArray<T>` |
| DALE016 (new) | error | Struct used as service element must be `readonly record struct` with flat fields |
| DALE017 (new) | error | `string` on a service element must be explicitly `string?` when null is intended |
| DALE018 (new) | warning | `ImmutableArray<T>` service element should be initialised |

Final IDs are confirmed during planning against any DALE numbers claimed between spec writing and implementation.

`ServiceElementTypeAnalyzer` (hosting DALE003) grows a recursive TypeRef-validator method replacing its flat whitelist.

**`ServiceBinder` / `ServiceBuilder` changes:**

- `BindProperty<T>` / `BindMeasuringPoint<T>` signatures unchanged. `T` expands to any legal TypeRef-expressible type; expression-tree lambdas compile for all.
- `ServiceBinding` record gains a `PropertyMetadata Metadata` field. The codec uses `Metadata.Schema.Type` for FB encode/decode.
- `SetPropertyValue` simplified: ad-hoc `int → enum` conversion removed. The codec produces the exact declared type.
- `ServicePropertyValueChanged` / `ServiceMeasuringPointValueChanged` unchanged (`object?`).

**Metalama `[Observable]` interaction:** unchanged. `ImmutableArray<T>` is a struct wrapper over `T[]`; assigning a new one fires INPC. In-place mutation is a compile error — `ImmutableArray<T>` exposes no mutators.

### 5.3 Introspection output

`LogicBlockIntrospection` replaces `MapToServiceElementType` with a recursive `BuildTypeRef(ITypeSymbol or Type)` that builds the internal `TypeRef` tree. The introspection JSON for a property contains the three sibling documents.

**New per-property output:**

```json
{
  "Identifier": "VoltageSetpoint",
  "schema": {"type":"number","format":"double","minimum":0,"x-unit":"V"},
  "presentation": null,
  "runtime": {"persistent": true}
}

{
  "Identifier": "Location",
  "schema": {
    "type": "object",
    "title": "Coordinates",
    "properties": {
      "lat": {"type":"number","format":"double"},
      "lon": {"type":"number","format":"double"}
    },
    "required": ["lat","lon"],
    "additionalProperties": false,
    "readOnly": true
  },
  "presentation": {"group":"Position","order":1},
  "runtime": null
}

{
  "Identifier": "CurrentAlarm",
  "schema": {
    "type": ["string","null"],
    "title": "AlarmState",
    "enum": ["Ok","Warning","Critical",null]
  },
  "presentation": {
    "statusMappings": {"Ok":"ok","Warning":"warning","Critical":"critical"}
  },
  "runtime": null
}
```

`presentation: null` and `runtime: null` are valid and common (most properties have neither). Serialisers may omit null sibling documents for compactness; the deserialiser treats omitted as null.

**Removed from the introspection shape:**

- `TypeFullName` — CLR names don't travel.
- `ServiceElementType` (string) — replaced by `schema`.
- `Writable` (bool) — replaced by JSON Schema `readOnly` keyword on the schema itself.
- `Annotations` dictionary — split across the three sibling documents.

**Enum members:** inline in the `schema` via `enum` (array of name strings). Per-member severity moves to `presentation.statusMappings` keyed by member name.

**LogicBlockParser tool:** unchanged CLI surface; emits the new-shape JSON after SDK upgrade.

### 5.4 Wire format

#### 5.4.1 FlatBuffers schema (`vion-contracts/Vion.Contracts/FlatBuffers/Common/property_value.fbs`)

`CommonValue` is replaced by `PropertyValue`. **14 union variants**, vs. the 33 of spec v1. Numeric precision narrowing happens at the C# binding boundary using the schema; the wire collapses `short`/`int`/`long` into `LongVal` and `float`/`double` into `DoubleVal`. Enums travel as their member name string in `StringVal` (or `StringArray`), idiomatic for JSON Schema / OpenAPI. Mesh stays schema-blind because each variant maps unambiguously to a JSON shape (Bool→`true`/`false`, Long→number, Double→number, String→string, DateTime→ISO-8601 string, Duration→ISO-8601 duration string, structs→object, arrays→array).

```fbs
namespace Vion.Contracts.FlatBuffers.Common;

// Scalar variants (6) — Long covers short/int/long; Double covers float/double; String covers string and enums
table BoolVal     { value: bool;   }
table LongVal     { value: long;   }
table DoubleVal   { value: double; }
table StringVal   { value: string; }
table DateTimeVal { unix_ms: long; }
table DurationVal { ticks: long;   }

// Array variants (6) — `present` is optional; absent or empty means all elements present.
// When present, present[i] = false marks element i as null; values[i] is undefined.
table BoolArray     { values: [bool];   present: [bool]; }
table LongArray     { values: [long];   present: [bool]; }
table DoubleArray   { values: [double]; present: [bool]; }
table StringArray   { values: [string]; present: [bool]; }
table DateTimeArray { unix_ms: [long];  present: [bool]; }
table DurationArray { ticks: [long];    present: [bool]; }

// Struct variants (2) — present[] marks null elements in StructArray
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
```

**Semantic rules:**

- **Top-level null** (nullable scalar / nullable struct with null value) = `payload = NONE`.
- **"Not yet received"** = no retained MQTT message for the topic. Not a wire state.
- **Null inside an array** = `present[i] == false`. `values[i]` is undefined.
- **Numeric precision** = stored at full width (`long` / `double`); narrowed at the C# binding boundary using `schema.format`. Out-of-range values throw `PropertyValueDecodeException` at decode.
- **Enums** = `StringVal` carrying the member name. The codec uses `Enum.GetName` / `Enum.Parse<T>` (results cached per `Type`) to bridge name↔C#-value at the binding boundary. The C# integer underlying value is never on the wire.
- **Flag enums** (`[Flags]`) are out of scope for v1 — none used in current LogicBlocks. Future support would either compose names (`"Read|Write"`) or introduce a separate wire form; deferred.

#### 5.4.2 JSON on Cloud↔Mesh and Cloud↔UI

Contextual JSON, no `$type` tag. Natural shape per kind:

```json
// primitive
{ "propertyIdentifier": "VoltageSetpoint", "value": 3.14 }
// nullable primitive (null)
{ "propertyIdentifier": "Target", "value": null }
// array
{ "propertyIdentifier": "HistogramBuckets", "value": [0.1, 0.4, 1.2, 4.0] }
// array with null elements
{ "propertyIdentifier": "Samples", "value": [1.1, null, 1.3] }
// struct
{ "propertyIdentifier": "Location", "value": { "lat": 47.3, "lon": 8.5 } }
// array of struct
{ "propertyIdentifier": "Schedule",
  "value": [
    {"at":"2026-05-04T00:00:00Z","powerSetpoint":5.0,"voltageSetpoint":230.0},
    {"at":"2026-05-04T00:15:00Z","powerSetpoint":5.2,"voltageSetpoint":230.0}
  ] }
// enum — same name string everywhere (no Mesh/Cloud translation)
{ "propertyIdentifier": "CurrentAlarm", "value": "Warning" }
```

Struct field names are camelCase (`lat`, `lon`, `altitude`) — matching JSON Schema `properties` keys. FB `NamedValue.name` and the schema's `properties` keys agree verbatim. Only the Dale CLR boundary translates between C# pascalCase (`Lat`) and wire camelCase (`lat`).

#### 5.4.3 Codec location and surface

`Vion.Contracts` gains `PropertyValueCodec`. The current `mesh/Mesh.Base/Infrastructure/Serialization/FlatBuffer/CommonValueBuilder.cs` and `CommonValueExtensions.cs` are deleted in favour of this shared implementation.

```csharp
public static class PropertyValueCodec
{
    // ── Dale-side ── CLR-typed; requires the user's struct/enum CLR types
    public static object? FlatBufferToClr(ReadOnlySpan<byte> bytes, TypeRef type, Type targetClrType);
    public static byte[]  ClrToFlatBuffer(object? value, TypeRef type);

    // ── Mesh-side ingress (Dale → Cloud) ── schema-free, FB tag tree → JSON
    public static JsonNode? FlatBufferToJson(ReadOnlySpan<byte> bytes);

    // ── Mesh-side egress (Cloud → Dale) ── uses schema carried in the message envelope
    public static byte[]    JsonToFlatBuffer(JsonNode? json, TypeRef type);

    // ── Optional defensive overload for callers that have a schema and want a tag-vs-schema check ──
    public static JsonNode? FlatBufferToJson(ReadOnlySpan<byte> bytes, TypeRef expected);

    // ── Schema-driven validation ──
    public static ValidationResult ValidateJson(JsonNode? json, TypeSchema schema);
}
```

**Encode/decode walks the `TypeRef` tree only.** `ValidateJson` walks both `TypeRef` (shape) and `TypeAnnotations` (ranges). Hand-rolled for the Dale profile, no off-the-shelf validator dependency. Stock JSON Schema validators (NJsonSchema, JsonSchema.Net) remain available for third parties consuming the OpenAPI spec.

**Notable: the schema-free `FlatBufferToJson` overload takes no schema.** Mesh uses this one. The FB union tag tree is fully self-describing for the JSON shape. Numeric precision distinctions disappear at this hop (JSON has no int16-vs-int32 distinction anyway). Enums emit as their name string (FB carries the name in `StringVal`).

**Defensive validation:** the Dale-side `FlatBufferToClr` asserts payload tag matches the expected schema variant family; mismatch throws `PropertyValueDecodeException` logged at warn.

### 5.5 ServiceBinder and Dale runtime

Data flow on property set from cloud:

```
/cloud/sw/property/set (JSON: {value, schema})  →  Mesh: JsonToFlatBuffer(json.value, schema.Type)
                                               →  /sw/property/set (FB) via MQTT
                                               →  Dale runtime: FlatBufferToClr(bytes, schema.Type, clrType)
                                               →  ServiceBinder.SetPropertyValue(iface, name, typedValue)
                                               →  compiled setter assigns
                                               →  [Observable] fires INotifyPropertyChanged
                                               →  ServicePropertyValueChanged
                                               →  Dale runtime: ClrToFlatBuffer(getter(), schema.Type)
                                               →  /sw/property/state (FB retained publish)
```

Data flow on property state from Dale:

```
/sw/property/state (FB retained)  →  Mesh: FlatBufferToJson(bytes)         (no schema needed)
                                  →  JsonNode stored in state cache
                                  →  /cloud/sw/properties/state (JSON)
                                  →  Cloud API: receives JsonNode, validates against stored schema, forwards to UI as-is (enum names already on the wire)
```

**Dale runtime responsibilities** (private repo, out-of-tree change):

- Populate `ServiceBinding.Metadata` (`PropertyMetadata`) during `Configure()`.
- Call `PropertyValueCodec` on the FB ingress/egress edge with `binding.Metadata.Schema.Type`.
- Maintain the `ServiceBinding` per `(serviceId, interfaceType, propertyName)`.

**What does not change:**

- MQTT topic structure (`/sw/property/get|set|state`, `/cloud/sw/property/set`, `/cloud/sw/properties/state`).
- ServicePropertyValueChanged / ServiceMeasuringPointValueChanged event signatures.
- `BindProperty<T>` / `BindMeasuringPoint<T>` signatures.
- Metalama `[Observable]` fabric.

### 5.6 Mesh changes

Mesh stays **schema-free**, exactly as today. The data flow change is mechanical: the codec calls swap from `CommonValueBuilder` / `CommonValueExtensions` to `PropertyValueCodec`.

- Delete `CommonValueBuilder` and `CommonValueExtensions`. Mesh calls `PropertyValueCodec` instead.
- `PropertyStateChangedHandler`: `FlatBufferToJson(bytes)` (schema-blind tree walk); forwards `JsonNode` to cloud.
- `SetPropertyHandler`: receives `SetPropertyPayload(JsonNode Value, JsonNode Schema)`. Calls `JsonToFlatBuffer(value, TypeSchemaSerialization.FromJsonSchema(schema).Type)`. Forwards FB bytes to Dale. The `Schema` field on the wire is the data-shape JSON Schema document only — Mesh has no use for `presentation` or `runtime` and Cloud doesn't include them.
- State store value type changes from `object` to `JsonNode?`. Mesh never holds user CLR types.
- `PropertyJsonContext` STJ source-gen: simplified; STJ handles `JsonNode` natively. Drop per-primitive registrations.
- `PropertyValueDecodeException` from the codec is caught at the Mesh handler boundary; log-and-drop.

**No schema cache. No retained-MQTT introspection topic. No race buffer. No drain window. No `SchemaUnknown` rejection.** All eliminated by carrying the schema in the set payload and walking the FB tag tree on the state path.

**Cross-restart story:**

- Dale restart → republishes retained values on `/sw/property/state`. Mesh receives FB, decodes via tag tree, caches JsonNode. No schema needed.
- Mesh restart → resubscribes; broker replays retained FB messages; Mesh rebuilds JsonNode cache. No schema needed.
- Cloud restart → no Mesh-side impact.

This is identical to today's behaviour for the property/measuring-point flow, just with richer types.

### 5.7 Cloud API changes

- **`Shared.Contracts.PropertyState`:** `object Value` → `JsonNode? Value`. Wire JSON bytes identical for existing primitives; C#-side consumers casting to a concrete type need updating.
- **`ServicePropertyOutput` DTO:** replace `Type`, `Writable`, `Annotations` with three sibling fields:
  ```csharp
  public class ServicePropertyOutput
  {
      public required string         Identifier   { get; set; }
      public required JsonNode       Schema       { get; set; }   // JSON Schema 2020-12, Dale profile
      public          JsonNode?      Presentation { get; set; }   // optional
      public          JsonNode?      Runtime      { get; set; }   // optional
      public required string         Topic        { get; set; }
  }
  ```
  `schema.readOnly === true` encodes "non-writable". `presentation` and `runtime` are advisory and may be absent.
- **`SetPropertyPayload`:** `(object Value, string ServiceElementType)` → `(JsonNode Value, JsonNode Schema)`. Cloud retrieves the schema from its DB (single fetch) and includes it on the wire so Mesh can construct FB without state.
- **`SetPropertyValueRequestHandler`:** validates the incoming value against the property's stored `TypeSchema` via `PropertyValueCodec.ValidateJson(value, typeSchema)` — covers shape, `required`, enum membership (string ∈ `schema.enum`), `minimum`/`maximum` from both property-level and per-struct-field annotations, nullability, and `readOnly` rejection. Single dispatch path; no separate handler for primitives vs compounds.
- **No enum translation.** Enum values are member name strings end-to-end. UI ↔ Cloud ↔ Mesh ↔ Dale all see the same `"Warning"`. The C# integer underlying value never leaves the Dale binding boundary.
- **OpenAPI spec:** the auto-generated description embeds the property's schema directly. Third-party clients can discover types via `/services` and drive UI with any JSON Schema form generator.
- Stops string-based type inference. All dispatch is schema-driven.

### 5.8 Database migration

Cloud DB collapses `ServiceElementType` (varchar 50) + `Writable` (bool) + `Annotations` (jsonb) into a **single composite column** `Metadata` (jsonb) on `ActiveServiceProperties` and `ActiveServiceMeasuringPoints`. No `TypeFullName` column exists today (per audit), so nothing to drop.

The `Metadata` jsonb document holds:

```json
{
  "schema":       { "type": "...", ... },
  "presentation": { "group": "...", "order": 1 } | null,
  "runtime":      { "persistent": true } | null
}
```

**Migration script:**

| Old `ServiceElementType` | New `schema` document |
|---|---|
| `"number"`  | `{"type":"number","format":"double"}` (decimal usage audited; zero hits in examples/templates — production audit confirms before migration) |
| `"integer"` | `{"type":"integer","format":"int32"}` |
| `"bool"`    | `{"type":"boolean"}` |
| `"string"`  | `{"type":"string"}` |
| `"dateTime"`| `{"type":"string","format":"date-time"}` |
| `"duration"`| `{"type":"string","format":"duration"}` |

Merge in old `Writable` (`false` → `schema.readOnly = true`).

Merge in old `Annotations`:
- `DefaultName` → `schema.title`
- `Unit` → `schema["x-unit"]`
- `MinValue` → `schema.minimum` (when numeric)
- `MaxValue` → `schema.maximum` (when numeric)
- `EnumValues` → promoted into the schema as `enum` (array of member name strings) per §5.1; the legacy integer mapping is dropped (no longer on the wire)
- `DisplayName`, `Group`, `Order`, `Category`, `Importance`, `UIHint` → `presentation.{displayName, group, order, category, importance, uiHint}`
- `StatusMappings` → `presentation.statusMappings`
- `Persistent` → `runtime.persistent`

**Deploy order:** add the new `Metadata` jsonb column, backfill, dual-read during rollout, switch code paths to `Metadata`, drop the three old columns. Two-step to avoid long table lock.

### 5.9 Dashboard UI changes

**TypeScript schema type** (`dashboard/src/domain/apis/service/schema.ts`) — hand-rolled mirror of the Dale profile. The UI does not need to handle arbitrary JSON Schema, only the Dale profile.

```ts
export type DaleSchema =
  | PrimitiveSchema
  | EnumSchema
  | StructSchema
  | ArraySchema;

interface SchemaBase {
  title?: string;
  description?: string;
  readOnly?: boolean;
  "x-unit"?: string;
}

export type PrimitiveType = "boolean" | "integer" | "number" | "string";
export type PrimitiveFormat = "int16" | "int32" | "int64" | "float" | "double" | "date-time" | "duration";

export interface PrimitiveSchema extends SchemaBase {
  type: PrimitiveType | [PrimitiveType, "null"];
  format?: PrimitiveFormat;
  minimum?: number;
  maximum?: number;
}

export interface EnumSchema extends SchemaBase {
  type: "string" | ["string", "null"];
  enum: (string | null)[];
}

export interface StructSchema extends SchemaBase {
  type: "object" | ["object", "null"];
  properties: Record<string, DaleSchema>;
  required: string[];
  additionalProperties: false;
}

export interface ArraySchema extends SchemaBase {
  type: "array" | ["array", "null"];
  items: DaleSchema;
}

export interface Presentation {
  displayName?: string;
  group?: string;
  order?: number;
  category?: string;
  importance?: string;
  uiHint?: string;
  decimals?: number;
  statusMappings?: Record<string, string>;
}

export interface RuntimeMetadata {
  persistent?: boolean;
}
```

**Model update** (`ServicePropertyModel`): replaces `type: ServiceElementType` and `annotations: Annotations` with three sibling fields:

```ts
interface ServicePropertyModel {
  identifier: string;
  schema: DaleSchema;
  presentation?: Presentation;
  runtime?: RuntimeMetadata;
  topic: string;
  // ...id fields, value, pendingValue
  value?: any;          // strict invariant: undefined = "no value cached", null = "explicitly null from wire"
}
```

**Strict invariant on `value`:** `value === undefined` means "no retained message cached"; `value === null` means "explicit null from the wire". The store must not coerce `undefined` ↔ `null`.

**Rendering dispatch** — central `<ServiceValue property>` component:

```
property.value === undefined  →  <NotReceived />          "—" subdued
property.value === null       →  <NullValue />            "(null)" distinct style
otherwise                      →  <ValueBySchema schema value />
                                    ├─ "enum" keyword       → <EnumValue />     (member display)
                                    ├─ type includes "object" → <StructValue />   (<dl>)
                                    ├─ type includes "array"
                                    │     └─ items is "object" → <StructArray />  (<table>)
                                    │     └─ items is primitive → <ScalarArray /> (chips / sparkline)
                                    └─ otherwise (primitive)    → <PrimitiveValue /> (formatter + x-unit)
```

A `baseType(schema)` helper returns the non-null kind; `isNullable(schema)` returns whether `"null"` appears in the type array form. The null case is short-circuited above so dispatchers work against `baseType`.

**Scope of v1 UI components:**
- `<PrimitiveValue>`: existing formatting extended with `schema["x-unit"]` suffix and `presentation.decimals` precision. Uses `schema.format` to pick int/number/date-time/duration formatting path.
- `<EnumValue>`: value is the member name string (same on every wire hop — no translation); component looks up `schema.enum` for display; styles via `presentation.statusMappings` if present. Falls back to raw value for forward-compat.
- `<StructValue>`: `<dl>` with one row per entry of `schema.properties`, recursing into `<ValueBySchema>`, suffixing field-level `x-unit`.
- `<ScalarArray>`: comma-separated chips for small arrays; collapsible list beyond threshold; numeric arrays get a simple inline `<svg>` sparkline. Unit from `schema["x-unit"]`.
- `<StructArray>`: `<table>` with columns per `schema.items.properties`, one row per array element; collapsible past a row threshold.
- `<NotReceived>` / `<NullValue>`: small stateless components.

**Edit surface:**
- Writable primitive: unchanged.
- Writable nullable primitive: input plus a "set null" / "clear" affordance.
- Writable enum: dropdown from `schema.enum` member list.
- Writable compound: read-only in v1.

**Registry hook:**
```ts
export interface ValueRenderer<S extends DaleSchema = DaleSchema> {
  matches(schema: DaleSchema, presentation?: Presentation): boolean;
  render(props: { value: JsonValue; schema: S; presentation?: Presentation }): JSX.Element;
}
export const valueRendererRegistry: ValueRenderer[] = [];
```
v1 ships an empty registry. Match predicates can key on schema fields, presentation hints, or both.

**Components retired:**
- `formatServiceElementValue` central switch → thin facade over `<PrimitiveValue>` for string-only call sites.
- `ServiceElementType` enum → deleted.
- `annotations` flat dict access → replaced by `schema.*`, `presentation.*`, `runtime.*` typed access.

**Store audit:**
On `PropertyState` MQTT update, write `value = msg.value` verbatim — no `?? null` defaulting. Audit `src/domain/apis/service/store.ts` for implicit coercions.

## 6. Testing

**Dale SDK:**
- Unit tests over `LogicBlockIntrospection.BuildTypeRef` covering every TypeRef kind and composition.
- Round-trip tests `PropertyMetadataSerialization.ToJson` / `FromJson` for every kind.
- **Identity-vs-annotation tests:** `TypeSchema(t, a1) != TypeSchema(t, a2)` (record inequality) but `TypeSchema(t, a1).Type == TypeSchema(t, a2).Type`.
- **Profile-conformance tests:** `FromJson` rejects each excluded keyword (`$ref`, `oneOf`, `patternProperties`, etc.) with a clear error message.
- **Compatibility test** against an off-the-shelf JSON Schema validator (NJsonSchema): every Dale-emitted schema validates against JSON Schema 2020-12 meta-schema.
- **Cross-language wire test:** spec-by-example FB byte vectors for every variant; codec round-trip.
- Analyzer tests for each new / changed DALE diagnostic with both compliant and non-compliant code.
- Existing examples (`examples/*`) and templates (`templates/vion-iot-library/*`) updated; build-and-introspect in CI.

**Mesh:**
- Codec integration tests: FB-from-Dale → JSON → FB-to-Dale round-trip for every kind.
- Contract tests against Cloud DTO shape for every kind.
- **Schema-free invariants:** Mesh handlers compile and pass tests with no schema cache, no schema lookup, no `Dictionary<PropertyKey, TypeSchema>` declared anywhere.
- **Cross-restart test:** Mesh restarts; subscribers receive last-known retained values from the broker; cache is rebuilt from FB tag walks alone.
- Mesh.Test currently has a placeholder; test infrastructure is greenfield. This work includes setting up the codec round-trip harness.

**Cloud API:**
- Validator tests: malformed JSON for each kind rejected; valid JSON accepted.
- Migration test: old `varchar(50)` strings rewritten to JSON Schema; old `Writable` merged as `readOnly`; old `Annotations.EnumValues` promoted to inline `enum` (member-name array); old `Annotations.{DisplayName,Group,Order,...}` routed into `presentation`; old `Annotations.Persistent` routed into `runtime`.
- OpenAPI-consumer contract test: generated OpenAPI doc validates against OpenAPI 3.1 meta-schema; a sample third-party client roundtrips values using a stock JSON Schema validator (including string-enum properties to confirm idiomatic OpenAPI consumption).

**Dashboard:**
- Snapshot tests per renderer component with representative values: primitive normal/extreme, enum known/unknown, struct happy-path, empty/non-empty arrays, array-of-struct, nullable 3-state.
- Store test: `value === undefined` is preserved across MQTT updates; explicit `null` payload sets `value === null`.

## 7. Phased rollout

**Single coordinated release** across all 5 repos in dependency order. With no public LogicBlocks, there is no compat reason to phase; phasing only adds half-states to maintain.

Dependency order (7 PRs across 5 repos; cloud-api lands in two steps to enable a dual-read window):

1. **vion-contracts** — `TypeRef` hierarchy + `PropertyMetadata` + `PropertyValueCodec` + new FB schema (~14 variants) + new DTOs (`SetPropertyPayload`, `ServicePropertyOutput`, `PropertyState`). Published to the private feed.
2. **dale-sdk** — analyzer changes (DALE003 expanded, new diagnostics), `StructFieldAttribute`, attribute renames with `[Obsolete]` shims, introspection emits the three sibling documents, examples and templates updated.
3. **cloud-api (additive DB migration)** — add `Metadata` jsonb column, backfill from `ServiceElementType` / `Writable` / `Annotations`, dual-read enabled. Old DTOs still served on the wire.
4. **dale (private)** — runtime adopts `PropertyMetadata` on `ServiceBinding`; codec calls on the FB edge. Encodes new FB format.
5. **mesh** — replace `CommonValueBuilder` / `CommonValueExtensions` with `PropertyValueCodec` calls, state store `JsonNode`, simplified STJ context. Decodes new FB, expects schema-on-set.
6. **cloud-api (DTO switch)** — switch outbound DTOs to `Schema`/`Presentation`/`Runtime`, send schema-with-payload on sets, drop old `ServiceElementType`/`Writable`/`Annotations` columns from the schema (after the dual-read window expires).
7. **dashboard** — `DaleSchema` TS types, 3-state nullable model, new renderers (`StructValue`, `ScalarArray`, `StructArray`, `NotReceived`, `NullValue`).

Steps 4 and 5 must land together — they meet on the wire and either alone breaks the integrated path. Step 6 follows 5. Step 7 follows 6.

**Pre-flight prerequisites** (run in parallel with spec review):
- Audit any non-public LogicBlocks in vion-iot/* repos for `decimal` usage; convert to `double` if any found.
- Audit `PropertyState.Value` cast sites across Cloud API and UI tooling.
- Audit dashboard call sites that read `annotations.{decimals, group, order, ...}` so they migrate cleanly to `presentation.*`.

**Coordination and rollback.**
The 7 PRs above are not fully independently shippable — steps 4 and 5 meet on the wire and must land together. Verify the integrated path end-to-end on a staging environment before each production cutover. The dual-read window between steps 3 and 6 (additive DB migration → breaking DTO switch + column drop) is the rollback safety net.

**Rollback path:** the only one-way step is the *drop* of the old `ServiceElementType` / `Writable` / `Annotations` columns at the tail of step 6. Snapshot the DB before that drop. If issues surface within the rollout window, revert PRs in reverse order; while the dual-read window is open, Cloud API continues to serve old DTOs from the still-populated old columns. Plan for a 24–48 h dual-read window in production before dropping the old columns.

## 8. Breaking changes

- `decimal` removed from the primitive whitelist. Migration: convert to `double`. (Audit confirms zero usages in current examples/templates; confirm production audit before release.)
- `Vion.Contracts.CommonValue` FB table removed.
- All `Sw/Property/*.fbs` and `Sw/MeasuringPoint/*.fbs` schemas updated to embed `PropertyValue` instead of `CommonValue`.
- `Shared.Contracts.PropertyState.Value` type changes from `object` to `JsonNode?`.
- Introspection JSON: `ServiceElementType`, `Writable`, `Annotations`, `TypeFullName` (where present) all removed. Replaced by sibling documents `schema` / `presentation` / `runtime`.
- `Vion.Contracts.SetPropertyPayload(object Value, string Type)` → `SetPropertyPayload(JsonNode Value, JsonNode Schema)`.
- `Vion.Contracts.PropertiesStatePayload` and `MeasuringPointsStatePayload`: `object Value` → `JsonNode? Value`.
- Cloud API `ServicePropertyOutput` / `ServiceMeasuringPointOutput`: `Type` / `Writable` / `Annotations` removed; `Schema` / `Presentation` / `Runtime` added.
- `ServicePropertyAttribute` / `ServiceMeasuringPointAttribute`: `DefaultName` → `Title`, `MinValue` → `Minimum`, `MaxValue` → `Maximum`. Old names `[Obsolete]`-shimmed for one minor release.
- `ServiceMeasuringPointAttribute` gains `Minimum` / `Maximum` for parity with `ServicePropertyAttribute`.
- Dashboard `ServiceElementType` TypeScript enum deleted. `ServicePropertyModel.{type, annotations}` removed; `{schema, presentation, runtime}` added.
- Cloud DB: three columns (`ServiceElementType`, `Writable`, `Annotations`) collapsed into one `Metadata` jsonb column on `ActiveServiceProperties` and `ActiveServiceMeasuringPoints`.
- Mesh `PropertyJsonContext` STJ: per-primitive registrations dropped; only payload types remain.

All breaking changes land across coordinated package versions documented in the rollout runbook.

## 9. Risks

- **`ImmutableArray<T>` default-value footgun** (`IsDefault == true` throws). Mitigation: analyzer DALE018; codec normalises to `ImmutableArray<T>.Empty` on decode rather than `default`.
- **Struct field-name case sensitivity.** Wire / schema / JSON all agree on camelCase; only the Dale CLR boundary translates to/from C# pascalCase. Mitigation: single translation helper in the codec, round-trip tests.
- **Cloud consumers casting `PropertyState.Value` to `object`.** Compile errors flag them; quick sweep at rollout time.
- **Dale profile drift** — someone adds a JSON Schema keyword (e.g. `pattern`) expecting the UI or Mesh to honour it. The codec ignores it; constraint silently lost. Mitigation: `PropertyMetadataSerialization.FromJson` rejects every unexpected keyword strictly (allow-list, not deny-list) at the source boundary. A later decision to *add* `pattern` is an explicit profile bump, not silent drift.
- **Numeric range overflow at Dale's binding boundary.** A wire `LongVal { value: 70000 }` for a property typed `short` overflows. Mitigation: codec range-checks against `schema.format` and throws `PropertyValueDecodeException` (logged at warn, value dropped). UI continues to display last-known.
- **Enum name typos in cloud sets.** A client sends `"Warnig"` instead of `"Warning"`; without int translation we don't have a "wrong int" path that fails noisily — it's just a string not in `schema.enum`. Mitigation: `ValidateJson` rejects with a clear error (member must be one of `[…]`); same defense as today's `ServiceElementType`-string mismatch.
- **Schema in every set payload adds bytes.** Worst-case ~800 bytes of schema for a complex array-of-struct write. Realistic load: human-triggered or scheduler-triggered sets, ≪100/sec across a deployment; ≪50 KB/sec aggregate. Acceptable. Burst case (fleet-wide schedule push to 10k gateways) ≈ 5 MB; absorbed by MQTT broker buffers. If profiling later shows this is hot, optimisation is straightforward (schema fingerprint + per-connection cache); deferred.
- **DB migration downtime.** Mitigation: two-step migration — add new column, backfill, switch reads/writes, drop old columns.
- **`ImmutableArray<T>` PropertyChanged semantics.** Wholesale assignment fires INPC; in-place mutation is impossible (`ImmutableArray<T>` exposes no mutators). Mitigation: existing Metalama fabric handles this; analyzer DALE018 prevents the common "forgot to initialise" footgun.

## 10. Deferred / out of scope (not ruled out for future)

- Recursive structs (struct-of-struct, struct-with-array).
- Nested arrays (`ImmutableArray<ImmutableArray<T>>`).
- Nullable struct *fields* inside structs (relax the flat-only rule on field types) — currently nullable composes everywhere except inside structs.
- **Map / dictionary types** (`Dictionary<string, double>`, e.g. channel-keyed readings, named flags). Workaround: array of `{key, value}` structs.
- **Polymorphic / tagged-union property values** (an "Alert" property whose payload differs per alert kind). JSON Schema's `oneOf` with discriminator would be the natural extension. Workaround: separate properties per variant.
- **Flag enums** (`[Flags]`). Need a separate wire form (`"Read|Write"` composition or a bit-mask variant). Not used in current LogicBlocks.
- Writable compound-type UI editors.
- Bespoke per-struct visualisations (map for `Coordinates`, chart for `double[]`) — only the registry hook ships.
- Struct-level title-override attribute (e.g. `[Struct(Title = "…")]`). Currently the struct's C# type name is its identity-bearing `Title`. An override would let a renamed type keep its old title (or vice versa); not needed today, defer.
- `ImmutableList<T>` / `IReadOnlyList<T>` as supported collection surfaces — `ImmutableArray<T>` is the single blessed form.
- **Expanded JSON Schema profile support:**
  - `pattern` / `minLength` / `maxLength` on strings.
  - `minItems` / `maxItems` / `uniqueItems` on arrays.
  - `exclusiveMinimum` / `exclusiveMaximum` / `multipleOf` on numbers.
  - `default` keyword (initial value provisioning).
  - `$ref` for shared struct definitions.
  - `oneOf` / discriminator-based polymorphism for tagged-union service elements.
- Runtime-published schemas for instance-config-shaped properties. Prerequisite: a real use case where a LogicBlock's schema depends on instance configuration (currently every property is statically declared via `[ServiceProperty]`). Re-introducing the retained-MQTT introspection topic at that point is straightforward; deferring keeps the v1 design simple.
- **A Python or TypeScript Dale SDK.** Wire format choices already accommodate; no Python code ships in this spec.
- Bringing back `[Display]` / `[Category]` / `[Importance]` / `[UIHint]` source attributes — kept in v1 (b1 decision); evolution depends on what the dashboard refactor concretely needs.
