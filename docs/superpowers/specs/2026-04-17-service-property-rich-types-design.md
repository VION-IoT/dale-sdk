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

The design is driven end-to-end through a shared **TypeRef** schema object carried in the introspection JSON, replacing the current `ServiceElementTypes` string. The wire format (FlatBuffers Dale↔Mesh, JSON Mesh↔Cloud↔UI) is extended once to handle all new kinds, not per-kind in each layer.

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

### 5.1 Type model — `TypeRef`

`Vion.Contracts` gains a `TypeRef` record hierarchy that becomes the single source of truth for "what shape is this property's value":

```csharp
public abstract record TypeRef;

public sealed record PrimitiveTypeRef(PrimitiveKind Kind) : TypeRef;

public sealed record EnumTypeRef(
    string Name,
    ImmutableArray<EnumMember> Members) : TypeRef;

public sealed record StructTypeRef(
    string Name,
    ImmutableArray<StructField> Fields) : TypeRef;

public sealed record ArrayTypeRef(TypeRef Element) : TypeRef;

public sealed record NullableTypeRef(TypeRef Inner) : TypeRef;

public sealed record StructField(
    string Name,
    TypeRef Type,
    ImmutableDictionary<string, object> Annotations);

public sealed record EnumMember(string Name, int Value);

public enum PrimitiveKind
{
    Bool, String, Short, Int, Long, Float, Double, DateTime, Duration
}
```

**Composition rules** (enforced by analyzers and by `TypeRef` factory guards):

- `NullableTypeRef.Inner` may be any TypeRef except another `NullableTypeRef`.
- `ArrayTypeRef.Element` may be primitive, enum, struct, or nullable-of-those. Never another `ArrayTypeRef`.
- `StructField.Type` may be primitive, enum, nullable-primitive, or nullable-enum. No nested struct, no array field.
- `EnumTypeRef.Members` underlying representation on the wire is always `int`, regardless of C# underlying type.

**Identity rules** — critical for cross-language interoperability:

- **Primitives:** by `Kind`.
- **Enums:** by `{Name, Members}`. Name travels for display and match.
- **Structs:** by **shape** — the ordered `Fields` list of `(Name, Type)` pairs. The `Name` is a display hint only. Two independently-declared structs with the same field list are wire-interchangeable.
- **Arrays / Nullables:** by `Inner` / `Element`.
- **Annotations are *not* part of identity** — they are display metadata. Two `Coordinates` structs differing only in `MinValue` are the same type on the wire.

**JSON serialization shape** (what introspection emits, what cloud stores, what UI consumes):

```json
{ "kind": "primitive", "primitive": "double" }
{ "kind": "enum", "name": "AlarmState", "members": [
    {"name": "Ok", "value": 0}, {"name": "Warning", "value": 1}]}
{ "kind": "struct", "name": "Coordinates", "fields": [
    {"name": "Lat", "type": {"kind":"primitive","primitive":"double"}, "annotations": {}},
    {"name": "Lon", "type": {"kind":"primitive","primitive":"double"}, "annotations": {}}]}
{ "kind": "array", "element": {"kind": "primitive", "primitive": "double"} }
{ "kind": "nullable", "inner": {"kind": "primitive", "primitive": "double"} }
```

`PrimitiveKind` JSON token set: `"bool" | "string" | "short" | "int" | "long" | "float" | "double" | "dateTime" | "duration"`.

### 5.2 SDK surface (C# API)

User-facing: plain C# properties with existing attributes, expanded type whitelist. No new required attribute for the common cases.

```csharp
public class InverterService
{
    [ServiceProperty(Unit = "V", MinValue = 0)]
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
    [StructField(Unit = "deg", MinValue = -90,  MaxValue = 90)]  double Lat,
    [StructField(Unit = "deg", MinValue = -180, MaxValue = 180)] double Lon,
    [StructField(Unit = "m")]                                    double Altitude);

public enum AlarmState { Ok, Warning, Critical }
```

**New attribute**:

```csharp
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
public sealed class StructFieldAttribute : Attribute
{
    public string? DefaultName { get; init; }
    public string? Unit { get; init; }
    public double MinValue { get; init; } = double.NegativeInfinity;
    public double MaxValue { get; init; } = double.PositiveInfinity;
}
```

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

`LogicBlockIntrospection` replaces `MapToServiceElementType` with a recursive `BuildTypeRef(ITypeSymbol or Type)` method that produces the TypeRef subtree described in §5.1.

**New per-property JSON output:**
```json
{
  "Identifier": "Location",
  "Type": { "kind": "struct", "name": "Coordinates", "fields": [...] },
  "Writable": false,
  "Annotations": {}
}
```

**Removed fields:**

- `TypeFullName` — CLR names don't travel; shape and display-name are in the TypeRef.
- `ServiceElementType` (string) — replaced by `Type` (TypeRef JSON).

**Relocated data:**

- Old enum values lived in `Annotations.EnumValues`. Now they live inline in `EnumTypeRef.Members`. `Annotations` no longer carries enum metadata.
- Struct-field annotations live inside `StructField.Annotations`, not in the property-level `Annotations`.

**Property-level `Annotations` retained for:** `DefaultName`, `Unit`, `MinValue`, `MaxValue`. For arrays of primitives, these describe each element.

**LogicBlockParser tool** — unchanged CLI surface; produces the new-shape JSON after SDK upgrade. Dale runtime and cloud parse the new shape with updated parsers.

**Breaking format change for introspection JSON:** mitigated by the fact that introspection is a build-time artifact regenerated on every `dale build`.

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

Struct JSON field names are camelCase (`lat`, `lon`, `altitude`). The codec maps FB `NamedValue.name` (which mirrors the C# pascalCase field name) to camelCase on the JSON hop and back.

#### 5.4.3 Codec location and surface

`Vion.Contracts` gains a new public `PropertyValueCodec` static class. The current `mesh/Mesh.Base/Infrastructure/Serialization/FlatBuffer/CommonValueBuilder.cs` and `CommonValueExtensions.cs` are deleted in favour of this shared implementation.

```csharp
public static class PropertyValueCodec
{
    // Dale-side: CLR-typed; requires the user's struct/enum CLR types
    public static object? FlatBufferToClr(ReadOnlySpan<byte> bytes, TypeRef schema, Type targetClrType);
    public static byte[]  ClrToFlatBuffer(object? value, TypeRef schema);

    // Mesh-side: JSON pass-through; no CLR types needed
    public static JsonNode? FlatBufferToJson(ReadOnlySpan<byte> bytes, TypeRef schema);
    public static byte[]    JsonToFlatBuffer(JsonNode? json, TypeRef schema);
}
```

**Both halves walk the same TypeRef tree.** Dale-side decoder materialises user struct types by matching FB `NamedValue.name` to positional-record-struct constructor parameter names (case-insensitive). Missing FB field with default-valued constructor parameter → use default; missing required parameter → decode error; extra FB field with no matching parameter → ignored (forward-compat).

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
- `ServicePropertyOutput` DTO: `string Type` → `JsonNode Type` (TypeRef JSON object).
- `SetPropertyPayload(object Value, string ServiceElementType)` → `SetPropertyPayload(JsonNode Value, TypeRef Type)`.
- `SetPropertyValueRequestHandler`: validates incoming value against property's TypeRef via `PropertyValueCodec.JsonToFlatBuffer` (throws on malformed shape); plus range-check of numeric leaves against `Annotations.MinValue/MaxValue` on both property-level and per-struct-field.
- Stops string-based type inference. All dispatch is TypeRef-driven.

### 5.8 Database migration

- `ActiveServicePropertyReadModel.ServiceElementType` column:
  - Postgres: `varchar(50)` → `jsonb`.
  - Other databases: `nvarchar(max)` storing serialised JSON.
- Migration script rewrites each row:

  | Old string | New TypeRef JSON |
  |---|---|
  | `"number"` | `{"kind":"primitive","primitive":"double"}` — old "number" was ambiguous between float/double/decimal; canonicalise to `double`. Migration must scan introspection artifacts for `TypeFullName == "System.Decimal"` and either block migration or convert at the source. |
  | `"integer"` | `{"kind":"primitive","primitive":"int"}` |
  | `"bool"` | `{"kind":"primitive","primitive":"bool"}` |
  | `"string"` | `{"kind":"primitive","primitive":"string"}` |
  | `"dateTime"` | `{"kind":"primitive","primitive":"dateTime"}` |
  | `"duration"` | `{"kind":"primitive","primitive":"duration"}` |

- Enum rows today carry `"integer"` plus an `EnumValues` annotation. Migration promotes the annotation into a proper `EnumTypeRef` and removes `EnumValues` from `Annotations`.
- Drop `TypeFullName` column if present.
- Deploy order: DB migration first (tolerant of both old-string and new-JSON during rollout), then cloud API, then UI.

### 5.9 Dashboard UI changes

**TypeScript TypeRef mirror** (in `dashboard/src/domain/apis/service/types.ts` or similar):

```ts
export type TypeRef =
  | { kind: "primitive"; primitive: PrimitiveKind }
  | { kind: "enum";      name: string; members: EnumMember[] }
  | { kind: "struct";    name: string; fields: StructField[] }
  | { kind: "array";     element: TypeRef }
  | { kind: "nullable";  inner: TypeRef };

export type PrimitiveKind =
  "bool" | "string" | "short" | "int" | "long" | "float" | "double" | "dateTime" | "duration";

export interface StructField { name: string; type: TypeRef; annotations: Record<string, unknown>; }
export interface EnumMember  { name: string; value: number; }
```

**Model update** (`ServicePropertyModel`): `type: ServiceElementType` → `type: TypeRef`; `value?: any` kept, with the strict invariant that `value === undefined` means "no retained message cached" and `value === null` means "explicit null from the wire". The store must not coerce `undefined` ↔ `null`.

**Rendering dispatch** — a central `<ServiceValue property>` component:

```
property.value === undefined  →  <NotReceived />                 "—" subdued; tooltip "No value received yet"
property.value === null       →  <NullValue />                   "(null)" distinct style; tooltip "Explicitly null"
otherwise                      →  <ValueByKind type value />
                                    ├─ primitive            → <PrimitiveValue />       (existing formatter + unit)
                                    ├─ enum                 → <EnumValue />            (member lookup + display)
                                    ├─ struct               → <StructValue />          (definition list of fields)
                                    ├─ array (scalar/enum)  → <ScalarArray />          (chips or sparkline)
                                    ├─ array (struct elem)  → <StructArray />          (<table>)
                                    └─ nullable             → recurse into inner       (null short-circuits earlier)
```

**Scope of v1 UI components:**
- `<PrimitiveValue>`: existing formatting extended with `annotations.Unit` suffix.
- `<EnumValue>`: looks up member by name, falls back to raw value for forward-compat.
- `<StructValue>`: `<dl>` with one row per `StructField`, each recursing into `<ValueByKind>`, suffixing field-level `annotations.Unit`.
- `<ScalarArray>`: comma-separated chips for small arrays; collapsible list beyond threshold; numeric arrays get a simple inline `<svg>` sparkline (no chart library).
- `<StructArray>`: `<table>` with columns per struct field, one row per array element; collapsible past a row threshold.
- `<NotReceived>` / `<NullValue>`: small stateless components distinguishing the two.

**Edit surface:**
- Writable primitive: unchanged.
- Writable nullable primitive: input plus a "set null" / "clear" affordance.
- Writable enum: dropdown from `members` list.
- Writable compound (struct, array, array-of-struct): read-only in v1. No edit affordance.

**Registry hook:**
```ts
export interface ValueRenderer<T extends TypeRef = TypeRef> {
  matches(type: TypeRef): boolean;
  render(props: { value: JsonValue; type: T; annotations: Record<string, unknown> }): JSX.Element;
}
export const valueRendererRegistry: ValueRenderer[] = [];
```
`<ValueByKind>` consults the registry first; first match wins; falls through to the generic dispatch. v1 ships an empty registry.

**Components retired:**
- `formatServiceElementValue` as central switch → thin facade over `<PrimitiveValue>` for string-only call sites (logs, tooltips).
- `ServiceElementType` enum → deleted.

**Store audit:**
- On `PropertyState` MQTT update, write `value = msg.value` verbatim — no `?? null` defaulting. Audit `src/domain/apis/service/store.ts` and adjacent for implicit coercions.

## 6. Testing

**Dale SDK:**
- Unit tests over `LogicBlockIntrospection.BuildTypeRef` covering every TypeRef kind and composition (nullable-primitive, nullable-enum, struct, nullable-struct, array-of-each, array-of-nullable-each, array-of-struct, nullable-array-of-struct).
- Unit tests over `PropertyValueCodec` round-tripping for every kind: FB → CLR → FB byte-equality; JSON → FB → JSON byte-equality.
- Analyzer tests for each new / changed DALE diagnostic with both compliant and non-compliant code.
- Existing examples (`examples/*`) and templates (`templates/vion-iot-library/*`) updated; build-and-introspect in CI.

**Mesh:**
- Codec integration tests: FB-from-Dale → JSON → FB-to-Dale round-trip for every kind.
- Contract tests against Cloud DTO shape for every kind.

**Cloud API:**
- Validator tests: malformed JSON for each kind rejected; valid JSON accepted.
- Migration test: old `varchar(50)` strings rewritten to TypeRef JSON; enum `Annotations.EnumValues` promoted to `EnumTypeRef.Members`.

**Dashboard:**
- Snapshot tests per renderer component with representative values: primitive normal/extreme, enum known/unknown, struct happy-path, empty/non-empty arrays, array-of-struct, nullable 3-state.
- Store test: `value === undefined` is preserved across MQTT updates; explicit `null` payload sets `value === null`.

## 7. Phased rollout

Each phase is independently shippable. FB `union ValuePayload` is additive: new variants can appear without disturbing existing readers as long as producers only emit variants that consumers understand.

**Phase 1 — Foundation + nullable primitives and enums.**
- `vion-contracts`: full `TypeRef` record hierarchy (all kinds defined in code from day one, even the ones unused until later phases). Full FB `ValuePayload` union *schema* committed — implementations of per-variant tables land in this phase for the ones Phase 1 exercises (scalar variants and `EnumVal`); later-phase variants compile but are rejected by the codec with a clear "not yet implemented in this version" error. `PropertyValueCodec` for primitives + nullable-via-NONE + enums.
- `dale-sdk`: DALE003 expanded to allow nullable primitives and nullable enums; `decimal` removed from the whitelist in the same change (the analyzer is already under edit). DALE007 added (`string` ↔ `string?`).
- `mesh`: migrate to `PropertyValueCodec`, delete `CommonValueBuilder` / `CommonValueExtensions`. State store becomes `JsonNode?`.
- `cloud-api`: `PropertyState.Value` → `JsonNode?`; DTO `Type` → TypeRef JSON; DB migration executed.
- `dashboard`: TypeRef TS mirror; 3-state nullable rendering; `<PrimitiveValue>`, `<EnumValue>`, `<NotReceived>`, `<NullValue>`; `ServiceElementType` TS enum deleted.

**Phase 2 — Structs.**
- `vion-contracts`: activate `StructVal`, `NamedValue` codec paths.
- `dale-sdk`: `StructFieldAttribute`; introspection emits struct fields with per-field annotations; analyzer DALE006 (flat-field rule).
- `mesh` / `cloud-api`: no per-kind code — TypeRef-driven, everything falls out of Phase 1 plumbing.
- `dashboard`: `<StructValue>` renderer.

**Phase 3 — Arrays (incl. array-of-struct).**
- `vion-contracts`: activate all `*Array` / `Nullable*Array` / `StructArray` / `NullableStructArray` codec paths.
- `dale-sdk`: analyzer DALE005 (`ImmutableArray<T>` rule) and DALE008 (initialisation warning); introspection emits `ArrayTypeRef`.
- `dashboard`: `<ScalarArray>`, `<StructArray>` renderers.

**Phase ordering guarantee:** After Phase 1 lands, Phases 2 and 3 can ship in either order, or together. Phase 2 does not depend on Phase 3 or vice-versa; both depend only on Phase 1.

**Pre-Phase-1 prerequisites (can run in parallel with spec review):**
- Audit existing LogicBlocks (examples, templates, known customers) for `decimal` usage; plan `double` migration.
- Audit `PropertyState.Value` cast sites across Cloud API and UI tooling.

## 8. Breaking changes

- `decimal` removed from the primitive whitelist. Migration: convert affected properties to `double`.
- `Vion.Contracts.CommonValue` FB table removed.
- `Shared.Contracts.PropertyState.Value` type changes from `object` to `JsonNode?`.
- `ServicePropertyOutput.Type` changes from `string` to TypeRef JSON.
- Introspection JSON `ServiceElementType` field renamed to `Type` and reshaped.
- `TypeFullName` field dropped from introspection JSON.
- Dashboard `ServiceElementType` TypeScript enum deleted; `ServicePropertyModel.type` is now `TypeRef`.
- Cloud DB column `ServiceElementType`: `varchar(50)` → `jsonb` / `nvarchar(max)`; content shape changes.

All breaking changes land across coordinated package versions documented in the rollout runbook (produced as part of implementation planning, not this spec).

## 9. Risks

- **FB union byte-compat with older Mesh/Dale.** Mitigation: version bump of `Vion.Contracts`; coordinated roll of Dale and Mesh; pre-prod soak.
- **`ImmutableArray<T>` default-value footgun** (`IsDefault == true` throws). Mitigation: analyzer DALE00W; codec normalises to `ImmutableArray<T>.Empty` on decode rather than `default`.
- **Struct field-name case sensitivity.** FB carries pascalCase; JSON carries camelCase. Codec centralises the translation. Mitigation: shared mapping helper with round-trip tests.
- **DB migration downtime.** Mitigation: two-step migration — add new column, backfill, switch reads/writes, drop old column — avoids long table lock.
- **Cloud consumers casting `PropertyState.Value` to `object`.** Compile errors flag them; mitigation is a quick sweep at rollout time.
- **Python parity assumption** — this design assumes a future Python SDK is feasible on the shape-based TypeRef. No Python code ships in this spec, but the wire format choice is load-bearing on that future path. Review the TypeRef shape carefully against the `flatbuffers` Python library before locking in.

## 10. Deferred / out of scope (not ruled out for future)

- Recursive structs (struct-of-struct, struct-with-array).
- Nested arrays (`ImmutableArray<ImmutableArray<T>>`).
- Writable compound-type UI editors.
- Bespoke per-struct visualisations (map for `Coordinates`, chart for `double[]`) — only the registry hook ships.
- Struct-level annotation (e.g. `[Struct(DisplayName = "…")]`) — shape identity means this isn't needed; defer unless a real need appears.
- `ImmutableList<T>` or `IReadOnlyList<T>` as supported collection surfaces — `ImmutableArray<T>` is the single blessed form.
