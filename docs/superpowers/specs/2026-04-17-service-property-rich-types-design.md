# Rich Data Types for ServiceProperty and ServiceMeasuringPoint

**Status:** Design, awaiting review
**Date:** 2026-04-17
**Scope:** `dale-sdk`, `vion-contracts`, `mesh`, `cloud-api`, `dashboard`
**Author / sponsor:** Jonas Bertsch

## 1. Summary

The Vion Dale SDK today restricts `[ServiceProperty]` and `[ServiceMeasuringPoint]` values to a flat whitelist of primitive types (`bool, string, int, long, short, float, double, decimal, DateTime, TimeSpan`, any `enum`). This blocks common IoT modelling patterns: nullable values (no `double.NaN`-as-sentinel hacks), named composite values (coordinates, three-phase currents), and variable-length samples (histogram buckets, time series).

This spec extends the permitted types to:

- **Nullable primitives and nullable enums** (`double?`, `MyEnum?`)
- **Flat immutable structs** (`readonly record struct Coordinates(double Lat, double Lon)`) — fields must themselves be primitives or nullable-primitives-or-enums
- **Arrays** of primitives, enums, nullable-primitives-or-enums, and structs, carried as `ImmutableArray<T>`
- **Nullable struct** (`Coordinates?`)
- Any non-nested composition of the above (e.g. `ImmutableArray<Coordinates?>?`)

As a side effect, `decimal` is **removed** from the primitive whitelist (breaking change for any existing LogicBlock using it — deliberate simplification; Python parity was the forcing function, but existing LogicBlocks that use `decimal` must migrate to `double`).

The design is driven end-to-end through a constrained profile of **JSON Schema 2020-12** (the draft OpenAPI 3.1 aligns with) as the canonical external type language, replacing the current `ServiceElementTypes` string. C# models keep a typed `TypeRef` record hierarchy internally for pattern-matching, but the on-disk and on-wire type representation is standard JSON Schema. Value payloads (FlatBuffers Dale↔Mesh, JSON Mesh↔Cloud↔UI) are extended once to handle all new kinds, not per-kind in each layer.

Adopting JSON Schema gets us value validation, form generators, API documentation, and cross-language typed-model generators (pydantic, NJsonSchema, json-schema-to-ts) for free, and positions the Cloud API as consumable by any OpenAPI-aware client.

## 2. Goals

- Authors of LogicBlocks can model real IoT data naturally without sentinel hacks or parallel properties.
- The type system composes: nullable-of-struct, array-of-struct, array-of-nullable-primitive, etc. all work without ad-hoc per-variant plumbing.
- The wire format is language-agnostic and forward-compatible: a future Python Dale runtime produces bytes indistinguishable from C# Dale for the same TypeRef.
- The UI renders every new kind generically with no per-struct code, while leaving a registered extension point for bespoke future renderers.
- `[Observable]` reactivity continues to work for every permitted type without new Metalama fabric.

## 3. Non-goals

- **Recursive struct composition.** Struct fields stay primitive-only; no struct-of-struct, no struct-with-array-field, no arrays-of-arrays. (Decision in §5.4.)
- **Writable compound-type UI editors.** Structs, arrays, and arrays-of-structs render read-only in the dashboard v1. Cloud API still accepts writes from other clients.
- **Per-struct custom visualizations** (map pin, chart) in v1 — only the registry hook ships; consumers populate later.
- **Storing measuring-point time series in a TimescaleDB-shaped path.** That is a cloud-side concern, decoupled from this spec.
- **Backwards-compatible wire format.** This is a breaking change to the FlatBuffers schema and the mesh↔cloud JSON DTOs. Rollout is coordinated across packages.
- **Keeping `decimal` in the whitelist.** Removed.

## 4. Open decisions taken during brainstorming

Each row is a closed decision; the full Q&A transcript is preserved in session history and not duplicated here.

| Topic | Decision | Rationale |
|---|---|---|
| Scope phasing | Design full scope at once; ship in phases | Avoids type-registry cruft from piecemeal additions |
| Type-tag model | Orthogonal structured TypeRef (not flat strings, not hybrid) | Composes cleanly; struct schemas need to travel somewhere anyway |
| Canonical type language | JSON Schema 2020-12, constrained to a "Dale profile" | Free validation/forms/docs across C#/Python/TS; Cloud API is OpenAPI-consumable; drops bespoke envelope in favour of industry standard |
| Nullable transport | 2-state on the wire (null \| value); "not yet received" = no retained MQTT message | Matches MQTT retention semantics; UI distinguishes via cache lookup |
| Struct depth | Flat only — struct fields are primitives (+ nullable, + enum) | Motivating use cases are flat; keeps wire & UI shallow; upgradeable later |
| Wire philosophy | FB: explicit `union` with built-in tag; JSON: contextual, no `$type` | Idiomatic for each format; FB tag is free; JSON readable |
| Struct identity | Shape = `{(fieldName, fieldType)}` ordered; `name` is a display hint | Enables structurally-shared renderers; required for Python parity |
| `decimal` | Dropped from whitelist | Simpler wire format; no native support in FB/JSON/Python |
| Collection type | `ImmutableArray<T>` required (not `T[]`, not `List<T>`) | Prevents in-place mutation trap with `[Observable]` |
| Measuring points | In scope — same type system, same wire | Code paths already unified; arrays more natural for metrics |
| Annotation granularity | Annotations attach to leaves: property-level for primitives/arrays, per-struct-field inside structs | Unlocks `Coordinates3D(Lat°, Lon°, Altitude m)` and similar |
| Enum JSON form | Member name as string, not int | Readable; cheap to translate via TypeRef; UI never has to join |
| UI ambition v1 | Generic per-kind renderers + empty registry hook | Bounded work; avoids "here's some JSON"; extensible |
| SDK writability of compounds | Allowed — UI-only read-only | Writability is a UI concern, not an attribute concern |

## 5. Design

### 5.1 Type model — Dale profile of JSON Schema 2020-12

**Canonical external form:** each property's type is a JSON Schema document conforming to the *Dale profile* (Appendix A below). That is what introspection emits, what `Vion.Contracts` defines as the wire-at-rest representation, what the Cloud API stores in its DB, and what the dashboard consumes.

**Internal C# form:** `Vion.Contracts` keeps a typed `TypeRef` record hierarchy as a convenience for pattern-matching in C# code. It is a thin, lossless wrapper over the JSON Schema profile — not a parallel universe.

```csharp
public abstract record TypeRef;

public sealed record PrimitiveTypeRef(
    PrimitiveKind Kind,
    string? Title,              // JSON Schema title
    string? Description,
    string? Unit,               // x-unit extension
    double? Minimum,            // JSON Schema minimum
    double? Maximum) : TypeRef;

public sealed record EnumTypeRef(
    string Title,
    ImmutableArray<EnumMember> Members,
    string? Description) : TypeRef;

public sealed record StructTypeRef(
    string? Title,
    ImmutableArray<StructField> Fields,
    string? Description) : TypeRef;

public sealed record ArrayTypeRef(
    TypeRef Items,
    string? Title,
    string? Description,
    string? Unit) : TypeRef;     // unit applies to each element, consistent with §5.2 annotation rule

public sealed record NullableTypeRef(TypeRef Inner) : TypeRef;

public sealed record StructField(string Name, TypeRef Type, bool Required);
public sealed record EnumMember(string Name, int Value);

public enum PrimitiveKind
{
    Bool, String, Short, Int, Long, Float, Double, DateTime, Duration
}

public static class TypeRefSerialization
{
    public static JsonNode  ToJsonSchema(this TypeRef type);
    public static TypeRef   FromJsonSchema(JsonNode schema);  // rejects schemas outside the Dale profile
}
```

**Primitive → JSON Schema mapping:**

| `PrimitiveKind` | JSON Schema |
|---|---|
| `Bool`     | `{"type":"boolean"}` |
| `String`   | `{"type":"string"}` |
| `Short`    | `{"type":"integer","format":"int16"}` (custom format; see Appendix A) |
| `Int`      | `{"type":"integer","format":"int32"}` |
| `Long`     | `{"type":"integer","format":"int64"}` |
| `Float`    | `{"type":"number","format":"float"}` |
| `Double`   | `{"type":"number","format":"double"}` |
| `DateTime` | `{"type":"string","format":"date-time"}` — RFC 3339 string, millisecond precision preserved, tick-level precision best-effort |
| `Duration` | `{"type":"string","format":"duration"}` — RFC 3339 Appendix A (ISO 8601) duration string, millisecond precision preserved |

**Composite → JSON Schema mapping:**

- **Nullable of T:** the JSON Schema for `T` with its `type` keyword widened from `X` to `[X, "null"]`. Applies recursively: nullable-of-object → `type: ["object", "null"]`.
- **Enum of int:** `{"type":"string","enum":["Ok","Warning","Critical"],"x-enum-values":{"Ok":0,"Warning":1,"Critical":2},"title":"AlarmState"}`. The JSON value is the *name string* (Q5 decision). `x-enum-values` carries the int mapping for the FB wire encoding.
- **Struct:** `{"type":"object","title":"Coordinates3D","properties":{…},"required":["lat","lon","altitude"],"additionalProperties":false}`. Each property value is itself a subschema.
- **Array:** `{"type":"array","items":{…}}`. `items` is a subschema.

**Full example — `Coordinates3D` from §5.2:**
```json
{
  "type": "object",
  "title": "Coordinates3D",
  "properties": {
    "lat": {"type":"number","format":"double","minimum":-90,"maximum":90,"x-unit":"deg"},
    "lon": {"type":"number","format":"double","minimum":-180,"maximum":180,"x-unit":"deg"},
    "altitude": {"type":"number","format":"double","x-unit":"m"}
  },
  "required": ["lat","lon","altitude"],
  "additionalProperties": false
}
```

**Full example — `ImmutableArray<Coordinates?>`:**
```json
{
  "type": "array",
  "items": {
    "type": ["object","null"],
    "title": "Coordinates",
    "properties": {
      "lat": {"type":"number","format":"double"},
      "lon": {"type":"number","format":"double"}
    },
    "required": ["lat","lon"],
    "additionalProperties": false
  }
}
```

**Identity rules** — unchanged in intent from the earlier spec draft, now expressed in JSON Schema terms:

- **Primitives:** by `(type, format)` pair.
- **Enums:** by `(title, x-enum-values)`. `title` is identity-bearing for enums (not display-only), because enum name disambiguates C# nominal identity.
- **Structs:** by **shape** — the ordered `(propertyName, propertySubschema)` list plus the `required` set. `title` is *display only*, not identity-bearing. Two structs with identical `properties` and `required` are wire-interchangeable regardless of `title`.
- **Arrays / Nullables:** by `items` / the nullability-widened inner.
- **Display metadata (`title` on non-enum, `description`, `x-unit`, `minimum`/`maximum`) is not part of identity.** Two `Coordinates` schemas differing only in `x-unit` are the same type on the wire.

**Appendix A — Dale profile of JSON Schema 2020-12**

A schema is a valid Dale TypeRef iff it matches one of the productions in the mapping tables above. In particular, the Dale codec accepts:

- `type` as a single string from `{"boolean","string","integer","number","array","object"}` or a two-element array `[X, "null"]` for nullability.
- `format` only as listed in the primitive mapping (`int16`, `int32`, `int64`, `float`, `double`, `date-time`, `duration`).
- `enum` only in combination with `type: "string"` (plus the `x-enum-values` extension carrying integer members).
- `properties` + `required` + `additionalProperties: false` for structs.
- `items` as a single subschema for arrays.
- Optional display keywords: `title`, `description`.
- Optional numeric constraints: `minimum`, `maximum` — inclusive bounds only; no `exclusiveMinimum`/`exclusiveMaximum`.
- The `x-unit` extension string for physical unit annotations.
- The `readOnly: true` keyword to mark non-writable service elements (replaces the introspection JSON's `Writable: false`; see §5.3).

**The profile explicitly rejects** (codec `FromJsonSchema` throws `InvalidSchemaException`):

- `$ref`, `$dynamicRef`, `$defs` — everything is inline; struct identity is by shape.
- `oneOf`, `anyOf`, `allOf`, `not`, `if`/`then`/`else` (except nothing models nullability via these; nullability uses the type array form).
- `patternProperties`, `additionalProperties: true`, `additionalProperties: <schema>`.
- `minLength`, `maxLength`, `pattern` on strings (deferred — see §10).
- `minItems`, `maxItems`, `uniqueItems` on arrays (deferred).
- `exclusiveMinimum`, `exclusiveMaximum`, `multipleOf`.
- Nested arrays (array with `items.type === "array"`) and array-with-object-items whose object has further nested object/array properties beyond the flat-struct rule from Q4.
- `format` values outside the listed set.
- Any combination of keywords producing ambiguity outside the productions above.

Profile conformance is checked at every producer/consumer boundary: introspection emission, Cloud API `POST /services`, DB insert, Mesh schema cache load. A non-conforming schema is rejected at source — it never reaches the codec dispatch path.

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
}

public readonly record struct Coordinates(double Lat, double Lon);

public readonly record struct Coordinates3D(
    [StructField(Unit = "deg", Minimum = -90,  Maximum = 90)]  double Lat,
    [StructField(Unit = "deg", Minimum = -180, Maximum = 180)] double Lon,
    [StructField(Unit = "m")]                                  double Altitude);

public enum AlarmState { Ok, Warning, Critical }
```

**New attribute** — note the property names mirror the JSON Schema keywords they serialise to:

```csharp
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
public sealed class StructFieldAttribute : Attribute
{
    public string? Title       { get; init; }   // → "title"
    public string? Description { get; init; }   // → "description"
    public string? Unit        { get; init; }   // → "x-unit"
    public double Minimum      { get; init; } = double.NegativeInfinity;  // → "minimum"
    public double Maximum      { get; init; } = double.PositiveInfinity;  // → "maximum"
}
```

**`ServicePropertyAttribute` and `ServiceMeasuringPointAttribute` naming alignment** — rename the existing properties to match JSON Schema keywords, with `[Obsolete]` shims for the old names during one minor release:

| Old name | New name | JSON Schema keyword |
|---|---|---|
| `DefaultName` | `Title` | `title` |
| `Unit` | (unchanged in C#) | `x-unit` |
| `MinValue` | `Minimum` | `minimum` |
| `MaxValue` | `Maximum` | `maximum` |

The C# `Unit` property name is kept as-is — the serialisation layer maps it to `x-unit`. Renaming to match the JSON keyword would just add noise.

**Declaration rules** (analyzer-enforced):

- Struct used as a service-element value must be `readonly record struct` with flat primitive/enum/nullable-primitive-or-enum fields only.
- Array-valued service elements must be `ImmutableArray<T>`. `T[]`, `List<T>`, `IReadOnlyList<T>`, `IEnumerable<T>` produce a diagnostic with a code-fix suggesting `ImmutableArray<T>`.
- `string` on a service element must be explicitly `string?` if null is a valid value; `string` alone is treated as a non-null contract.
- `ImmutableArray<T>` properties must be initialised (field initialiser, constructor, or property initialiser) to `ImmutableArray<T>.Empty` or a concrete value — otherwise the default `IsDefault == true` state will throw at publish time. Analyzer warning if not initialised.

**Analyzer changes** (in `Vion.Dale.Sdk.Generators`):

| Diagnostic | Severity | Rule |
|---|---|---|
| DALE003 (changed) | error | Whitelist expanded to recursive validation over the full TypeRef-expressible set. `decimal` removed. |
| DALE004 (unchanged) | error | Measuring-point read-only |
| DALE005 (new) | error | Array-valued service element must be `ImmutableArray<T>` |
| DALE006 (new) | error | Struct used as service element must be `readonly record struct` with flat primitive/enum/nullable fields |
| DALE007 (new) | error | `string` on a service element must be explicitly `string?` when null is intended |
| DALE008 (new) | warning | `ImmutableArray<T>` service element should be initialised to avoid `IsDefault` at publish time |

Final diagnostic ID assignments may shift if any DALE IDs have been claimed between spec writing and implementation; confirm during planning.

`ServiceElementTypeAnalyzer` (hosting DALE003) grows a recursive TypeRef-validator method replacing its flat whitelist.

**`ServiceBinder` / `ServiceBuilder` changes:**

- `BindProperty<T>` / `BindMeasuringPoint<T>` signatures unchanged. `T` expands to any legal TypeRef-expressible type; expression-tree lambdas compile for all.
- `ServiceBinding` record gains a `TypeRef Type` field alongside getter/setter/metadata.
- `SetPropertyValue` simplified: the ad-hoc `int → enum` conversion is removed. The codec (§5.4) produces the exact declared type.
- `ServicePropertyValueChanged` / `ServiceMeasuringPointValueChanged` unchanged (already `object?`).

**Metalama `[Observable]` interaction:** unchanged. Wholesale property assignment fires `PropertyChanged` for every supported kind. `ImmutableArray<T>` is a struct wrapper over `T[]`; assigning a new one fires INPC. In-place mutation is a compile error — `ImmutableArray<T>` exposes no mutators.

### 5.3 Introspection output

`LogicBlockIntrospection` replaces `MapToServiceElementType` with a recursive `BuildTypeRef(ITypeSymbol or Type)` method that builds the internal `TypeRef` record tree, then emits it as JSON Schema (§5.1 Dale profile) on the `schema` field of each property entry.

**New per-property JSON output:**
```json
{
  "Identifier": "VoltageSetpoint",
  "schema": {"type":"number","format":"double","minimum":0,"x-unit":"V"}
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
  }
}

{
  "Identifier": "CurrentAlarm",
  "schema": {
    "type": ["string","null"],
    "title": "AlarmState",
    "enum": ["Ok","Warning","Critical",null],
    "x-enum-values": {"Ok":0,"Warning":1,"Critical":2}
  }
}
```

**Removed from the introspection shape:**

- `TypeFullName` — CLR names don't travel.
- `ServiceElementType` (string) — replaced by `schema` (JSON Schema).
- `Writable` (bool) — replaced by the JSON Schema `readOnly` keyword on the schema itself. Absent or `false` means writable; `true` means read-only.
- `Annotations` dictionary — folded into JSON Schema keywords: `title`, `description`, `minimum`, `maximum`, `x-unit`.

Everything that used to live in `Annotations` is now a first-class JSON Schema field on the property's `schema`. Arrays of primitives put `x-unit` on the *array* schema (since it describes each element by convention, per §5.2).

**Enum members:** inline in the `schema` via `enum` + `x-enum-values`, as shown above. Not in a separate annotations bag.

**LogicBlockParser tool:** unchanged CLI surface; emits the new-shape JSON after SDK upgrade. Dale runtime and cloud parse the new shape with updated parsers.

**Breaking format change for introspection JSON:** mitigated by the fact that introspection is a build-time artifact regenerated on every `dale build`.

**One side benefit:** each `schema` blob is a valid JSON Schema on its own, so tools like the VS Code JSON Schema extension, NJsonSchema validators, or the Swagger UI component can consume it directly without a translation layer.

### 5.4 Wire format

#### 5.4.1 FlatBuffers schema (`vion-contracts/Vion.Contracts/FlatBuffers/Common/property_value.fbs`)

Replace the current `CommonValue` table with a `PropertyValue` table wrapping a union of variant tables. The `CommonValue` table is deleted after all consumers migrate.

```fbs
namespace Vion.Contracts.FlatBuffers.Common;

// Scalar variants
table BoolVal     { value: bool; }
table StringVal   { value: string; }
table ShortVal    { value: short; }
table IntVal      { value: int; }
table LongVal     { value: long; }
table FloatVal    { value: float; }
table DoubleVal   { value: double; }
table DateTimeVal { unix_ms: long; }
table TimeSpanVal { ticks: long; }
table EnumVal     { value: int; }

// Arrays of primitives / enum
table BoolArray     { values: [bool]; }
table StringArray   { values: [string]; }
table ShortArray    { values: [short]; }
table IntArray      { values: [int]; }
table LongArray     { values: [long]; }
table FloatArray    { values: [float]; }
table DoubleArray   { values: [double]; }
table DateTimeArray { unix_ms: [long]; }
table TimeSpanArray { ticks: [long]; }
table EnumArray     { values: [int]; }

// Arrays of nullable primitives — parallel present[] bit-vector
table NullableBoolArray     { values: [bool];   present: [bool]; }
table NullableStringArray   { values: [string]; present: [bool]; }
table NullableShortArray    { values: [short];  present: [bool]; }
table NullableIntArray      { values: [int];    present: [bool]; }
table NullableLongArray     { values: [long];   present: [bool]; }
table NullableFloatArray    { values: [float];  present: [bool]; }
table NullableDoubleArray   { values: [double]; present: [bool]; }
table NullableDateTimeArray { unix_ms: [long];  present: [bool]; }
table NullableTimeSpanArray { ticks: [long];    present: [bool]; }
table NullableEnumArray     { values: [int];    present: [bool]; }

// Struct — shape-identified flat list
table NamedValue  { name: string; value: PropertyValue; }  // value.payload ∈ {scalar variants, EnumVal, NONE}; never a struct/array variant (Q4 flat-struct rule)
table StructVal   { fields: [NamedValue]; }

// Array of struct and nullable array of struct
table StructArray         { items: [StructVal]; }
table NullableStructArray { items: [StructVal]; present: [bool]; }

// Top-level union; NONE = null
union ValuePayload {
  BoolVal, StringVal, ShortVal, IntVal, LongVal, FloatVal, DoubleVal,
  DateTimeVal, TimeSpanVal, EnumVal,
  BoolArray, StringArray, ShortArray, IntArray, LongArray, FloatArray, DoubleArray,
  DateTimeArray, TimeSpanArray, EnumArray,
  NullableBoolArray, NullableStringArray, NullableShortArray, NullableIntArray,
  NullableLongArray, NullableFloatArray, NullableDoubleArray,
  NullableDateTimeArray, NullableTimeSpanArray, NullableEnumArray,
  StructVal, StructArray, NullableStructArray
}

table PropertyValue { payload: ValuePayload; }
```

**Semantic rules:**

- **Top-level null** (nullable scalar / nullable struct with null value) = `payload = NONE`.
- **"Not yet received"** = no retained MQTT message for the topic. Not a wire state.
- **Null inside an array** = parallel `present` bit-vector in the Nullable*Array variants. `present[i] = false` means element is null; `values[i]` is undefined.
- **Enums** = always `int` on the wire. `TypeRef` carries member names for display translation.

#### 5.4.2 JSON on mesh↔cloud and cloud↔UI

Contextual, no `$type` tag. Natural JSON shape per TypeRef kind:

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
{ "propertyIdentifier": "Route",
  "value": [ {"lat":47.3,"lon":8.5}, {"lat":47.4,"lon":8.6} ] }
// enum (by member name)
{ "propertyIdentifier": "CurrentAlarm", "value": "Warning" }
```

Struct field names on the wire and in the schema are camelCase (`lat`, `lon`, `altitude`) — matching JSON Schema `properties` keys. FB `NamedValue.name` and the schema's `properties` keys agree verbatim; no FB ↔ JSON name translation needed. Only the Dale CLR boundary does case translation between C# pascalCase (`Lat`) and wire camelCase (`lat`).

#### 5.4.3 Codec location and surface

`Vion.Contracts` gains a new public `PropertyValueCodec` static class. The current `mesh/Mesh.Base/Infrastructure/Serialization/FlatBuffer/CommonValueBuilder.cs` and `CommonValueExtensions.cs` are deleted in favour of this shared implementation.

The codec operates on `TypeRef` (the typed C# view of the JSON Schema). Callers that hold a raw `JsonNode` schema convert via `TypeRef.FromJsonSchema(schema)` once and cache. This keeps the hot path pattern-matching on typed records rather than walking untyped trees.

```csharp
public static class PropertyValueCodec
{
    // Dale-side: CLR-typed; requires the user's struct/enum CLR types
    public static object? FlatBufferToClr(ReadOnlySpan<byte> bytes, TypeRef schema, Type targetClrType);
    public static byte[]  ClrToFlatBuffer(object? value, TypeRef schema);

    // Mesh-side: JSON pass-through; no CLR types needed
    public static JsonNode? FlatBufferToJson(ReadOnlySpan<byte> bytes, TypeRef schema);
    public static byte[]    JsonToFlatBuffer(JsonNode? json, TypeRef schema);

    // Schema-only validation — accepts a JSON value against a TypeRef,
    // used by Cloud API to validate incoming writes before forwarding.
    public static ValidationResult ValidateJson(JsonNode? json, TypeRef schema);
}
```

**Both halves walk the same `TypeRef` tree.** Dale-side decoder materialises user struct types by matching FB `NamedValue.name` to positional-record-struct constructor parameter names (case-insensitive). Missing FB field with default-valued constructor parameter → use default; missing required parameter → decode error; extra FB field with no matching parameter → ignored (forward-compat).

The `ValidateJson` method can either be hand-rolled (walks the TypeRef) or delegate to an off-the-shelf JSON Schema validator (NJsonSchema, JsonSchema.Net) applied against `schema.ToJsonSchema()`. Recommendation: **hand-rolled for the Dale profile only**, since the profile is narrow enough to validate in ~100 lines and doesn't pull in a ~200 KB dependency. Off-the-shelf validators are available in the larger ecosystem if third parties want to validate independently.

**Defensive validation:** `FlatBufferTo*` methods assert `payload_tag` matches `schema.kind` on ingress; mismatch throws `PropertyValueDecodeException` logged at warn.

### 5.5 ServiceBinder and Dale runtime

Data flow on property set from cloud:

```
/cloud/sw/property/set (JSON)  →  Mesh: JsonToFlatBuffer(json, schema)  →  FB bytes
                               →  /sw/property/set (FB) via MQTT        →  Dale runtime
                               →  FlatBufferToClr(bytes, schema, clrType) → object? typedValue
                               →  ServiceBinder.SetPropertyValue(iface, name, typedValue)
                               →  compiled setter assigns
                               →  [Observable] fires INotifyPropertyChanged
                               →  ServicePropertyValueChanged
                               →  Dale runtime: ClrToFlatBuffer(getter(), schema) → FB bytes
                               →  /sw/property/state (FB retained publish)
```

Data flow on property state from Dale:

```
/sw/property/state (FB retained)  →  Mesh: FlatBufferToJson(bytes, schema)
                                  →  JsonNode stored in state cache
                                  →  /cloud/sw/properties/state (JSON) via MQTT
                                  →  Cloud API: stores JsonNode, forwards to UI subscribers
```

**Dale runtime responsibilities** (private repo, out-of-tree change):

- Populate `ServiceBinding.TypeRef` during `Configure()`.
- Call `PropertyValueCodec` on the FB ingress/egress edge.
- Maintain the `ServiceBinding` for each property keyed by `(serviceId, interfaceType, propertyName)`.

**What does not change:**

- MQTT topic structure (`/sw/property/get|set|state`, `/cloud/sw/property/set`, `/cloud/sw/properties/state`).
- ServicePropertyValueChanged / ServiceMeasuringPointValueChanged event signatures.
- `BindProperty<T>` / `BindMeasuringPoint<T>` signatures.
- Metalama `[Observable]` fabric.

### 5.6 Mesh changes

- Delete `CommonValueBuilder` and `CommonValueExtensions`; Mesh calls `PropertyValueCodec` instead.
- `PropertyStateChangedHandler`: uses `FlatBufferToJson(bytes, schema)`; forwards `JsonNode` to cloud.
- `SetPropertyHandler`: uses `JsonToFlatBuffer(json, schema)`; forwards FB bytes to Dale.
- State store value type changes from `object` to `JsonNode?`. Mesh never holds user CLR types.
- Mesh keeps a `Dictionary<PropertyKey, TypeRef>` cache, populated from per-service introspection JSON already published to Mesh at service registration time.
- `PropertyJsonContext` STJ source-gen setup: simplified; STJ handles `JsonNode` natively. Drop per-primitive registrations.
- Defensive assert (payload tag vs schema kind) lives inside `PropertyValueCodec`; Mesh handles `PropertyValueDecodeException` by log-and-drop.

### 5.7 Cloud API changes

- `Shared.Contracts.PropertyState`: `object Value` → `JsonNode? Value`. Wire JSON bytes identical for existing primitives; C#-side consumers that cast `object` to a concrete type need updating.
- `ServicePropertyOutput` DTO: replace `Type`, `Writable`, `Annotations` with a single `schema: JsonNode` field (the JSON Schema for the property). The DTO becomes:
  ```csharp
  public class ServicePropertyOutput {
      public required string   Identifier { get; set; }
      public required JsonNode schema     { get; set; }   // JSON Schema 2020-12, Dale profile
      public required string   Topic      { get; set; }
  }
  ```
  `schema.readOnly === true` encodes "non-writable"; `x-unit`, `minimum`, `maximum`, `title`, `description` are all first-class fields on the schema, no separate annotations bag.
- `SetPropertyPayload(object Value, string ServiceElementType)` → `SetPropertyPayload(JsonNode Value)`. The cloud already knows the property's schema via the DB lookup; no need to re-transmit.
- `SetPropertyValueRequestHandler`: validates incoming value against property's schema via `PropertyValueCodec.ValidateJson(value, typeRef)` — covers type shape, `required`, enum membership, `minimum`/`maximum`, and nullability. No separate handler for primitives vs compounds.
- **OpenAPI spec** of Cloud API: the auto-generated OpenAPI description now embeds the property's schema directly — third-party clients can discover types via `/services` and drive UI with any JSON Schema form generator.
- Stops string-based type inference. All dispatch is schema-driven.

### 5.8 Database migration

- Collapse three columns (`ServiceElementType`, `Writable`, `Annotations`) and any `TypeFullName` column into a single `schema` column holding a JSON Schema document:
  - Postgres: `jsonb`.
  - Other databases: `nvarchar(max)` storing serialised JSON.
- Migration script rewrites each row:

  | Old `ServiceElementType` | New `schema` JSON |
  |---|---|
  | `"number"` | `{"type":"number","format":"double"}` — old "number" was ambiguous between float/double/decimal; canonicalise to `double`. Migration must scan introspection artifacts for `TypeFullName == "System.Decimal"` and either block migration or convert at the source. |
  | `"integer"` | `{"type":"integer","format":"int32"}` |
  | `"bool"` | `{"type":"boolean"}` |
  | `"string"` | `{"type":"string"}` |
  | `"dateTime"` | `{"type":"string","format":"date-time"}` |
  | `"duration"` | `{"type":"string","format":"duration"}` |

- Merge in old `Writable`: `Writable = false` → set `"readOnly": true` on the schema.
- Merge in old `Annotations`:
  - `DefaultName` → `title`
  - `Unit` → `x-unit`
  - `MinValue` → `minimum` (when numeric)
  - `MaxValue` → `maximum` (when numeric)
- Enum rows today carry `"integer"` plus an `EnumValues` annotation. Migration promotes them into `{"type":"string","enum":[…],"x-enum-values":{…},"title":"…"}` per §5.1 enum mapping.
- Deploy order: add new `schema` column, backfill, dual-read during rollout, switch code paths to `schema`, drop the old columns. Two-step to avoid long table lock.

### 5.9 Dashboard UI changes

**TypeScript schema type** (in `dashboard/src/domain/apis/service/schema.ts` or similar) — a thin hand-rolled TS mirror of the Dale profile. We deliberately don't pull in a heavy JSON Schema library in the UI; the profile is small enough to type by hand:

```ts
// Dale profile of JSON Schema 2020-12
export type DaleSchema =
  | PrimitiveSchema
  | EnumSchema
  | StructSchema
  | ArraySchema;

type Nullable<T> = T | null;

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
  "x-enum-values": Record<string, number>;
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
```

(The UI does not need to handle arbitrary JSON Schema — only the Dale profile. Any schema coming back from the Cloud API that doesn't match these types is a contract violation.)

**Model update** (`ServicePropertyModel`): `type: ServiceElementType` → `schema: DaleSchema`; `value?: any` kept, with the strict invariant that `value === undefined` means "no retained message cached" and `value === null` means "explicit null from the wire". The store must not coerce `undefined` ↔ `null`.

**Rendering dispatch** — a central `<ServiceValue property>` component dispatches on the schema shape:

```
property.value === undefined  →  <NotReceived />                 "—" subdued; tooltip "No value received yet"
property.value === null       →  <NullValue />                   "(null)" distinct style; tooltip "Explicitly null"
otherwise                      →  <ValueBySchema schema value />
                                    ├─ "enum" keyword present      → <EnumValue />     (member lookup + display)
                                    ├─ type includes "object"      → <StructValue />   (<dl> per property)
                                    ├─ type includes "array"
                                    │     └─ items is "object"     → <StructArray />   (<table>)
                                    │     └─ items is primitive    → <ScalarArray />   (chips or sparkline)
                                    └─ otherwise (primitive)       → <PrimitiveValue /> (formatter + x-unit)
```

Dispatch helper: `type` may be a single string or `[X,"null"]`. A small `baseType(schema)` helper returns the non-null kind, and `isNullable(schema)` returns whether `"null"` appears in the array form. The null case is already short-circuited above, so dispatchers work against `baseType`.

**Scope of v1 UI components:**
- `<PrimitiveValue>`: existing formatting extended with `schema["x-unit"]` suffix. Uses `schema.format` to pick int/number/date-time/duration formatting path.
- `<EnumValue>`: the value is already the member name string (Q5); component looks it up in `schema.enum` for display; falls back to raw value for forward-compat.
- `<StructValue>`: `<dl>` with one row per entry of `schema.properties`, each recursing into `<ValueBySchema>`, suffixing field-level `x-unit`.
- `<ScalarArray>`: comma-separated chips for small arrays; collapsible list beyond threshold; numeric arrays get a simple inline `<svg>` sparkline (no chart library). Unit from `schema["x-unit"]`.
- `<StructArray>`: `<table>` with columns per `schema.items.properties`, one row per array element; collapsible past a row threshold.
- `<NotReceived>` / `<NullValue>`: small stateless components distinguishing the two.

**Edit surface:**
- Writable primitive: unchanged.
- Writable nullable primitive: input plus a "set null" / "clear" affordance.
- Writable enum: dropdown from `members` list.
- Writable compound (struct, array, array-of-struct): read-only in v1. No edit affordance.

**Registry hook:**
```ts
export interface ValueRenderer<S extends DaleSchema = DaleSchema> {
  matches(schema: DaleSchema): boolean;
  render(props: { value: JsonValue; schema: S }): JSX.Element;
}
export const valueRendererRegistry: ValueRenderer[] = [];
```
`<ValueBySchema>` consults the registry first; first match wins; falls through to the generic dispatch. v1 ships an empty registry. Match predicates can key on any schema field — `title === "Coordinates"` for nominal, a `properties`-shape check for structural, or `x-unit === "V"` for unit-driven overrides.

**Components retired:**
- `formatServiceElementValue` as central switch → thin facade over `<PrimitiveValue>` for string-only call sites (logs, tooltips).
- `ServiceElementType` enum → deleted; replaced by `DaleSchema` types.

**Store audit:**
- On `PropertyState` MQTT update, write `value = msg.value` verbatim — no `?? null` defaulting. Audit `src/domain/apis/service/store.ts` and adjacent for implicit coercions.

## 6. Testing

**Dale SDK:**
- Unit tests over `LogicBlockIntrospection.BuildTypeRef` covering every TypeRef kind and composition (nullable-primitive, nullable-enum, struct, nullable-struct, array-of-each, array-of-nullable-each, array-of-struct, nullable-array-of-struct).
- Unit tests over `TypeRef.ToJsonSchema` / `FromJsonSchema` round-tripping for every kind: C# → JSON Schema → C# structural equality.
- **Profile-conformance tests:** `TypeRef.FromJsonSchema` rejects each excluded keyword (`$ref`, `oneOf`, `patternProperties`, `exclusiveMinimum`, nested arrays, etc.) with a clear error message.
- **Compatibility test** against an off-the-shelf JSON Schema validator (NJsonSchema): any schema Dale emits must validate against JSON Schema 2020-12 meta-schema.
- Unit tests over `PropertyValueCodec` round-tripping for every kind: FB → CLR → FB byte-equality; JSON → FB → JSON byte-equality.
- Analyzer tests for each new / changed DALE diagnostic with both compliant and non-compliant code.
- Existing examples (`examples/*`) and templates (`templates/vion-iot-library/*`) updated; build-and-introspect in CI.

**Mesh:**
- Codec integration tests: FB-from-Dale → JSON → FB-to-Dale round-trip for every kind.
- Contract tests against Cloud DTO shape for every kind.

**Cloud API:**
- Validator tests: malformed JSON for each kind rejected; valid JSON accepted.
- Migration test: old `varchar(50)` strings rewritten to JSON Schema; old `Writable` merged as `readOnly`; old `Annotations.EnumValues` promoted to inline `enum` + `x-enum-values`.
- OpenAPI-consumer contract test: generated OpenAPI doc validates against OpenAPI 3.1 meta-schema; a sample third-party client can roundtrip values using a stock JSON Schema validator.

**Dashboard:**
- Snapshot tests per renderer component with representative values: primitive normal/extreme, enum known/unknown, struct happy-path, empty/non-empty arrays, array-of-struct, nullable 3-state.
- Store test: `value === undefined` is preserved across MQTT updates; explicit `null` payload sets `value === null`.

## 7. Phased rollout

Each phase is independently shippable. FB `union ValuePayload` is additive: new variants can appear without disturbing existing readers as long as producers only emit variants that consumers understand.

**Phase 1 — Foundation + nullable primitives and enums.**
- `vion-contracts`: full `TypeRef` record hierarchy plus `ToJsonSchema` / `FromJsonSchema` covering every production in the Dale profile (including struct and array productions, so Phases 2 and 3 only activate codec paths, not add schema kinds). Full FB `ValuePayload` union *schema* committed — implementations of per-variant tables land in this phase for the ones Phase 1 exercises (scalar variants and `EnumVal`); later-phase variants compile but are rejected by the codec with a clear "not yet implemented in this version" error. `PropertyValueCodec` for primitives + nullable-via-NONE + enums.
- `dale-sdk`: DALE003 expanded to allow nullable primitives and nullable enums; `decimal` removed from the whitelist in the same change. DALE007 added (`string` ↔ `string?`). Attribute rename (`DefaultName`→`Title`, `MinValue`→`Minimum`, `MaxValue`→`Maximum`) with `[Obsolete]` shims. Introspection emits JSON Schema.
- `mesh`: migrate to `PropertyValueCodec`, delete `CommonValueBuilder` / `CommonValueExtensions`. State store becomes `JsonNode?`.
- `cloud-api`: `PropertyState.Value` → `JsonNode?`; `ServicePropertyOutput` collapses `Type`/`Writable`/`Annotations` into `schema`; DB migration executed (3 columns → 1).
- `dashboard`: `DaleSchema` TS types; 3-state nullable rendering; `<PrimitiveValue>`, `<EnumValue>`, `<NotReceived>`, `<NullValue>`; `ServiceElementType` TS enum deleted.

**Phase 2 — Structs.**
- `vion-contracts`: activate `StructVal`, `NamedValue` codec paths.
- `dale-sdk`: `StructFieldAttribute`; introspection emits object schemas with per-property `minimum`/`maximum`/`title`/`x-unit` inline; analyzer DALE006 (flat-field rule).
- `mesh` / `cloud-api`: no per-kind code — schema-driven, everything falls out of Phase 1 plumbing.
- `dashboard`: `<StructValue>` renderer.

**Phase 3 — Arrays (incl. array-of-struct).**
- `vion-contracts`: activate all `*Array` / `Nullable*Array` / `StructArray` / `NullableStructArray` codec paths.
- `dale-sdk`: analyzer DALE005 (`ImmutableArray<T>` rule) and DALE008 (initialisation warning); introspection emits array schemas.
- `dashboard`: `<ScalarArray>`, `<StructArray>` renderers.

**Phase ordering guarantee:** After Phase 1 lands, Phases 2 and 3 can ship in either order, or together. Phase 2 does not depend on Phase 3 or vice-versa; both depend only on Phase 1.

**Pre-Phase-1 prerequisites (can run in parallel with spec review):**
- Audit existing LogicBlocks (examples, templates, known customers) for `decimal` usage; plan `double` migration.
- Audit `PropertyState.Value` cast sites across Cloud API and UI tooling.

## 8. Breaking changes

- `decimal` removed from the primitive whitelist. Migration: convert affected properties to `double`.
- `Vion.Contracts.CommonValue` FB table removed.
- `Shared.Contracts.PropertyState.Value` type changes from `object` to `JsonNode?`.
- Introspection JSON: `ServiceElementType` (string), `Writable` (bool), `Annotations` (dict), `TypeFullName` (string) all removed — collapsed into a single `schema` field holding a JSON Schema 2020-12 (Dale profile) document.
- Cloud API `ServicePropertyOutput`: `Type` / `Writable` / `Annotations` fields removed; new `schema` field added.
- `SetPropertyPayload(object Value, string ServiceElementType)` → `SetPropertyPayload(JsonNode Value)`; `ServiceElementType` parameter removed (cloud derives from stored schema).
- `ServicePropertyAttribute` / `ServiceMeasuringPointAttribute`: `DefaultName` → `Title`, `MinValue` → `Minimum`, `MaxValue` → `Maximum`; the old names are `[Obsolete]`-shimmed for one minor release then removed.
- Dashboard `ServiceElementType` TypeScript enum deleted; `ServicePropertyModel.type` removed; `ServicePropertyModel.schema: DaleSchema` added.
- Cloud DB: three columns (`ServiceElementType`, `Writable`, `Annotations`) collapsed into one `schema jsonb` column; `TypeFullName` column dropped if present.

All breaking changes land across coordinated package versions documented in the rollout runbook (produced as part of implementation planning, not this spec).

## 9. Risks

- **FB union byte-compat with older Mesh/Dale.** Mitigation: version bump of `Vion.Contracts`; coordinated roll of Dale and Mesh; pre-prod soak.
- **`ImmutableArray<T>` default-value footgun** (`IsDefault == true` throws). Mitigation: analyzer DALE008; codec normalises to `ImmutableArray<T>.Empty` on decode rather than `default`.
- **Struct field-name case sensitivity.** Wire / schema / JSON all agree on camelCase; only the Dale CLR boundary translates to/from C# pascalCase. Mitigation: single translation helper in the codec, round-trip tests.
- **DB migration downtime.** Mitigation: two-step migration — add new column, backfill, switch reads/writes, drop old column — avoids long table lock.
- **Cloud consumers casting `PropertyState.Value` to `object`.** Compile errors flag them; mitigation is a quick sweep at rollout time.
- **Python parity assumption** — this design assumes a future Python SDK is feasible on the JSON Schema type language and the FlatBuffers wire format. No Python code ships in this spec. Review the Dale profile against `jsonschema` / `pydantic` / `flatbuffers` Python libraries before locking in.
- **Dale profile drift** — someone adds a JSON Schema keyword (say, `pattern` on a string) to a schema, expecting the UI or Mesh to honour it. The codec ignores it; the constraint is silently lost. Mitigation: `TypeRef.FromJsonSchema` rejects every unexpected keyword strictly (allow-list, not deny-list) at the source boundary. A later decision to *add* `pattern` support is an explicit profile bump, not a silent drift.

## 10. Deferred / out of scope (not ruled out for future)

- Recursive structs (struct-of-struct, struct-with-array).
- Nested arrays (`ImmutableArray<ImmutableArray<T>>`).
- Writable compound-type UI editors.
- Bespoke per-struct visualisations (map for `Coordinates`, chart for `double[]`) — only the registry hook ships.
- Struct-level annotation (e.g. `[Struct(Title = "…")]`) — shape identity means this isn't needed; defer unless a real need appears.
- `ImmutableList<T>` or `IReadOnlyList<T>` as supported collection surfaces — `ImmutableArray<T>` is the single blessed form.
- **Expanded JSON Schema profile support:**
  - `pattern` / `minLength` / `maxLength` on strings.
  - `minItems` / `maxItems` / `uniqueItems` on arrays.
  - `exclusiveMinimum` / `exclusiveMaximum` / `multipleOf` on numbers.
  - `default` keyword (initial value provisioning).
  - `$ref` for shared struct definitions.
  - `oneOf` / discriminator-based polymorphism for tagged-union service elements.

  Each is a self-contained extension of the Dale profile; add when a concrete use case appears.
