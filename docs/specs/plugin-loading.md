---
trace: enforced
---

# Plugin loading ABI

How the Dale runtime and `Vion.Dale.LogicBlockParser` load a logic-block plugin: which assemblies a
plugin gets its own copy of, which it shares with the host, which it shares with every other plugin,
and when a plugin is rejected outright. Area code `PLUG`. Process:
[`../spec-process.md`](../spec-process.md).

A **plugin** is a directory containing a logic-block assembly and every dependency it was published
with. Each plugin is loaded into its own `PluginLoadContext` — an `AssemblyLoadContext` whose
resolution rules are the subject of this page.

## Context lifetime

- `AC-PLUG-001.1` (Ubiquitous): THE SYSTEM SHALL create every plugin load context as
  non-collectible, so a loaded plugin remains in the host process until the process exits.

There is no unload path. A host that needs to replace a plugin replaces the process.

## The SDK version gate

A plugin built against a `Vion.Dale.Sdk` with a different **major** version than the host's is
binary-incompatible, and the failure it produces without a gate is a `MissingMethodException` or
`TypeLoadException` raised much later, from deep inside the runtime. The gate turns that into one
actionable error at load time.

Minor and patch skew is **not** rejected — it is logged and the load continues. Because the SDK is
pre-1.0, every released major is `0`, so the gate is dormant today; it arms itself at 1.0.

- `AC-PLUG-002.1` (Event-driven): WHEN a plugin load context is created over a directory containing
  an assembly built against a `Vion.Dale.Sdk` whose major version differs from the SDK the host has
  loaded, THE SYSTEM SHALL reject the plugin with a `PluginSdkVersionMismatchException` before
  loading any plugin assembly into the context.
- `AC-PLUG-002.2` (State-driven): WHILE the host and plugin `Vion.Dale.Sdk` major versions are
  equal, THE SYSTEM SHALL create the context whatever the minor and patch versions are.
- `AC-PLUG-002.3` (Event-driven): WHEN the version gate rejects a plugin THE SYSTEM SHALL name the
  package id, both `Vion.Dale.Sdk` versions and the rebuild remedy in the exception message, and
  record the rejection at error level.
- `AC-PLUG-002.4` (Event-driven): WHEN the plugin directory does not exist THE SYSTEM SHALL create
  the context without inspecting any assembly.
- `AC-PLUG-002.5` (Event-driven): WHEN a file in the plugin directory is not a readable .NET
  assembly, or is not built against `Vion.Dale.Sdk`, THE SYSTEM SHALL exclude it from the version
  check and continue with the rest of the directory.
- `AC-PLUG-002.6` (Ubiquitous): THE SYSTEM SHALL determine the `Vion.Dale.Sdk` version a plugin was
  built against without loading that plugin into any load context.
- `AC-PLUG-002.7` (Event-driven): WHEN more than one assembly in the directory fails the version
  check THE SYSTEM SHALL reject the plugin on the first and leave the remaining assemblies
  uninspected.

## Resolution rules

A plugin's assembly binding is answered by the first rule below that applies.

### 1. Assemblies shared with the host

`Vion.Dale.Sdk` and everything it references cross the host/plugin boundary in signatures, so the
host and every plugin must agree on their identity. The set is **derived, not curated**: adding a
`PackageReference` to `Vion.Dale.Sdk` widens it silently
([`../../Vion.Dale.Sdk/Vion.Dale.Sdk.csproj`](../../Vion.Dale.Sdk/Vion.Dale.Sdk.csproj) records the
same rule at the build file).

- `AC-PLUG-003.1` (Ubiquitous): THE SYSTEM SHALL treat `Vion.Dale.Sdk` and every assembly
  `Vion.Dale.Sdk` references as shared with the host, deriving that set from the SDK build the host
  has loaded.
- `AC-PLUG-003.2` (Event-driven): WHEN a plugin binds to an assembly shared with the host that the
  host has already loaded, THE SYSTEM SHALL resolve the host's instance even when the plugin
  directory contains its own copy of that assembly.
- `AC-PLUG-003.3` (Event-driven): WHEN a plugin binds to an assembly shared with the host that the
  host has not loaded, THE SYSTEM SHALL resolve it through the host's default load context rather
  than loading a copy into the plugin's context.

Membership in the set is what decides, not whether the host got there first: an assembly the host
has never touched still lands in the host's context, so the plugin that binds it next sees the same
instance.

### 2. Framework assemblies

- `AC-PLUG-004.1` (Ubiquitous): THE SYSTEM SHALL treat an assembly as a framework assembly when its
  simple name begins with `System.` or `Microsoft.`, or equals `System`, `netstandard` or
  `mscorlib`.
- `AC-PLUG-004.2` (Event-driven): WHEN a plugin binds to a framework assembly the host has loaded,
  THE SYSTEM SHALL resolve the host's instance.
- `AC-PLUG-004.3` (Event-driven): WHEN a plugin binds to a framework assembly the host has not
  loaded and the plugin directory contains it, THE SYSTEM SHALL load it from the plugin directory
  into that plugin's context.
- `AC-PLUG-004.4` (Event-driven): WHEN a plugin binds to a framework assembly present neither in the
  host nor in the plugin directory, THE SYSTEM SHALL resolve it through the host's default load
  context.

The prefix rule is the contract, not a heuristic: an assembly a plugin author names `Microsoft.*` or
`System.*` is resolved by these rules, whoever wrote it.

### 3. Shared extension assemblies — `[DaleSharedAssembly]`

A library whose types travel between plugins — contract handler actors, contract message types —
must have **one** identity across all of them, or actor message routing stops matching. The library
opts in with `[assembly: DaleSharedAssembly]`; the first plugin to bind it loads it, and every other
plugin gets that same instance.

Applied today by `Vion.Dale.Sdk.DigitalIo`, `Vion.Dale.Sdk.AnalogIo`, `Vion.Dale.Sdk.Modbus.Core`
and `Vion.Dale.Sdk.Modbus.Rtu`.

- `AC-PLUG-005.1` (Event-driven): WHEN a plugin binds to an assembly in its own directory that is
  marked `[DaleSharedAssembly]` and no shared instance for that simple name exists, THE SYSTEM SHALL
  load it from that directory and retain it as the shared instance for that simple name.
- `AC-PLUG-005.2` (Event-driven): WHEN a plugin binds to an assembly in its own directory that is
  marked `[DaleSharedAssembly]` and a shared instance for that simple name exists, THE SYSTEM SHALL
  resolve the existing shared instance.
- `AC-PLUG-005.3` (Event-driven): WHEN a plugin binds to an assembly in its own directory that is
  not marked `[DaleSharedAssembly]`, THE SYSTEM SHALL load a copy private to that plugin even when a
  shared instance with the same simple name exists.
- `AC-PLUG-005.4` (Ubiquitous): THE SYSTEM SHALL recognise an assembly as marked
  `[DaleSharedAssembly]` from that assembly's own metadata without loading it, including when it was
  built against a different `Vion.Dale.Sdk` version than the host's.
- `AC-PLUG-005.5` (Conditional): IF an assembly's metadata cannot be read THEN THE SYSTEM SHALL
  treat it as not marked `[DaleSharedAssembly]`.
- `AC-PLUG-005.6` (State-driven): WHILE two plugins bind to the same shared extension concurrently,
  THE SYSTEM SHALL load it exactly once and resolve the same instance for both.
- `AC-PLUG-005.7` (Ubiquitous): THE SYSTEM SHALL retain shared extension instances for the lifetime
  of the host process, across every plugin load context.

`AC-PLUG-005.3` is why sharing is opt-in in both directions: a plugin that never applied the
attribute keeps its own copy, and cannot be handed another plugin's assembly because the simple
names happen to collide.

### 4. Plugin-private assemblies, and the fall-through

- `AC-PLUG-006.1` (Event-driven): WHEN a plugin binds to an assembly present in its own directory
  that matches no sharing rule, THE SYSTEM SHALL load a copy private to that plugin.
- `AC-PLUG-006.2` (Event-driven): WHEN a plugin binds to an assembly absent from its own directory
  that matches no sharing rule, THE SYSTEM SHALL resolve it through the host's default load context.

Isolation is the default. Two plugins shipping the same unmarked library get one copy each, and
their types are distinct.

## Eager loading and the shared registry

The runtime needs handler actors present before it scans for them, and needs to invoke
`IConfigureServices` from shared libraries that no plugin's own registration mentions. Both are
served by eagerly loading a plugin directory's marked assemblies and by the registry of shared
instances loaded so far — which `Vion.Dale.LogicBlockParser` enumerates for exactly that purpose.

- `AC-PLUG-007.1` (Event-driven): WHEN eager loading of shared extensions is requested THE SYSTEM
  SHALL load every assembly in the plugin directory that is marked `[DaleSharedAssembly]`.
- `AC-PLUG-007.2` (Event-driven): GAP: no independent observable, see below. WHEN eager loading
  reaches an assembly whose simple name already has a shared instance THE SYSTEM SHALL leave that
  instance in place.
- `AC-PLUG-007.3` (Ubiquitous): THE SYSTEM SHALL expose the shared extension instances loaded so
  far, whichever plugin load context loaded each one.
- `AC-PLUG-007.4` (Event-driven): WHEN eager loading of shared extensions is requested for a
  directory that does not exist THE SYSTEM SHALL complete without loading anything.
- `AC-PLUG-007.5` (Event-driven): WHEN eager loading reaches a marked assembly that the plugin's
  context has already loaded THE SYSTEM SHALL record it as the shared instance for its simple name.

`AC-PLUG-007.2` carries no test and cannot: removing the skip changes nothing a caller can see,
because the resolution rules hand back the same shared instance either way. It is stated because it
is the intent, and marked so the trace gate reports it instead of failing on it.

`AC-PLUG-007.4` and `AC-PLUG-007.5` pair with `AC-PLUG-002.4` and `AC-PLUG-007.3`: a directory that
construction tolerates, eager loading also tolerates; and an assembly the registry is expected to
carry reaches the registry however it was loaded.
