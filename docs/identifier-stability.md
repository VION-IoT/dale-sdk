# Identifier stability

Everything a logic block declares carries two kinds of string: an **identifier** (a C# name, or an
explicit `Identifier =` value) and a **display string** (`Title`, `Description`, `DefaultName`,
`[EnumLabel("…")]`, …). The Vion cloud lets integrators translate the display strings — per language,
without a re-upload — and it keys those translations by the identifiers.

The keys are *derived*, not declared. Nothing in your package names them, no attribute overrides them,
and the SDK has no i18n feature to opt into. That is what makes this page necessary:

> **Renaming an identifier mints a new translation key.** Translations authored against the old key are
> orphaned — they keep existing and stay visible in the dashboard's Translations tab, but nothing
> re-attaches them. Re-attaching is manual, per language.

Nothing breaks: a string with no translation always falls back to the string compiled into your package.
The cost of a rename is re-translation work for whoever owns the library, in every language they author.

## What counts as an identifier

| Display string | Keyed by | Orphaned by |
|---|---|---|
| Block name — `[LogicBlock(Name = …)]` | the block's **full type name** (namespace + class) | renaming the class, **or moving it to another namespace** |
| Property / measuring-point `Title`, `Description` | the block, its **service identifier**, and the **C# property name** | renaming the property, its service, or the class |
| Service identifier (root service) | the **logic-block class name** | renaming the class |
| Service identifier (component service) | the **holding property's name** | renaming that property |
| Contract name — `[ServiceProviderContractBinding(DefaultName = …)]` | the binding's `Identifier`, defaulting to the **holding property's name** | renaming the property while leaving `Identifier` unset |
| Interface name and role names | the binding's `Identifier`, defaulting to `{PropertyName}_{InterfaceName}` (property-bound) or the bare **interface name** (class-implemented) | renaming the property, the interface, or the class |
| Enum labels — `[EnumLabel("…")]` | the enum's **short type name** and the **member name** | renaming the enum type or a member (its namespace is not part of the key) |
| Struct field `Title` / `Description` — `[StructField]` | the struct's **short type name** and the **camelCase field name** | renaming the struct type or a constructor parameter |
| Custom group labels | the **group-key string** itself | editing the key |
| *all of the above* | the library's **PackageId** | changing `<PackageId>` (see below) |

Four consequences worth knowing before you rename anything:

- A member carrying **both** `[ServiceProperty]` and `[ServiceMeasuringPoint]` is one translatable
  member: it has one title key and one description key, not two.
- **Enum members are cataloged exhaustively.** A member with no `[EnumLabel]` is translatable too — its
  source string is the raw C# member name (which is what the dashboard renders for it). Adding a label
  later changes the source string, not the key.
- The role names on `[LogicBlockContract]` (`BetweenDefaultName` / `AndDefaultName`) are keyed by the
  *binding block's* interface identifier, so two blocks binding the same contract translate the same
  role name twice. The duplication is deliberate — there is no key aliasing anywhere in this scheme.
- Well-known group keys (the `PropertyGroup` constants) are translated by the dashboard itself. Only
  **custom** keys — `"acme.powertrain"` — resolve through your library's translations.

Identifiers are not source-code trivia you have to reverse-engineer: `dale list` prints the block full
names, service, property, contract and interface identifiers exactly as introspection emits them.

## One identifier that is not a translation key: the contract-type token

`[ServiceProviderContractType("DigitalOutputProvider")]` names a *contract type*, not a display
string. Nothing translates it — but it is the token the platform matches contracts on, so it is a
stable identifier all the same, and it must be chosen once. It reaches introspection as
`matchingContractType`, the runtime pairs a binding to its handler through it, and the DevHost's
topology editor shows it beside each endpoint when authoring a `contractPairings` entry. Renaming one
is a breaking change for every configuration that binds it, in a way a rename of a *translated*
identifier is not — orphaned translations degrade gracefully; an orphaned contract type does not
match. Provider faces (the inverse surfaces a simulator binds, see
[`simulator-authoring.md`](simulator-authoring.md)) each add one, so pick the name with the same care
as the consumer face's: the SDK's four are `DigitalOutputProvider`, `DigitalInputProvider`,
`AnalogOutputProvider` and `AnalogInputProvider`.

## The one decoupling knob

`Identifier =` on `[ServiceProviderContractBinding]` and `[LogicBlockInterfaceBinding]` pins the
identifier, which frees the C# name to change without minting a new key:

```csharp
// Renaming the property from Relay to MainContactor: pin the old identifier and the
// introspected identifier — hence the translation key — is unchanged.
[ServiceProviderContractBinding(Identifier = "Relay", DefaultName = "Main contactor")]
public IDigitalOutput MainContactor { get; private set; } = null!;
```

Services, service properties and measuring points have **no** such override — their C# names *are*
their identifiers. This is a deliberate design decision, not a gap: there is no translation-key
override attribute in the SDK, and none is planned.

## PackageId is a platform-global namespace

Every key is prefixed with the library's `<PackageId>` — the value in your library `.csproj`, which
`dotnet pack` writes into the package's `.nuspec`. Two rules follow:

1. **PackageId is unique across all integrators on the platform**, compared case-insensitively
   (NuGet id semantics). `dale upload` — and creating a library — fails with **409 Conflict** when the
   id is already registered by someone else, and reports it as such even under `--skip-duplicate`,
   which skips only a re-upload of a version you already published. The message says the id is taken;
   it deliberately does not say by whom. Use a vendor-prefixed id (`Acme.Chargers`), the same
   discipline nuget.org asks for.
   Re-uploading *your own* library is matched case-insensitively too, so a nuspec that differs only in
   casing lands on the library it belongs to instead of colliding with it.
2. **Changing your own PackageId re-namespaces every key in the library** — the single most expensive
   rename available. Treat it as fixed once the library has been uploaded.

## Practical guidance

- Get the names right **before the first upload**. That is the only moment renaming is free.
- After translations exist, prefer additive change: a new property is a new key and costs nothing; a
  renamed property costs its translations.
- Where a knob exists (contracts, interfaces), rename the C# member and pin the old `Identifier`.
- Where it does not, expect to re-attach: the Translations tab keeps the orphaned row visible so its
  text can be copied onto the new key — a manual step, per language.
- Changing a *display string* (a `Title`, a `[EnumLabel]`) is not a rename. The key survives; existing
  translations keep being served and are flagged as outdated so the author can revisit them.

Translation keys are one consumer of these identifiers, and the one whose cost is invisible from this
repository. The exact key grammar belongs to the cloud platform and is not part of the SDK's contract —
the durable rule is the one above: the identifier goes in, the key comes out.
