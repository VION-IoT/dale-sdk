# Identifier stability — practical guidance

The rules live in the spec page: **[`specs/introspection.md`](specs/introspection.md)** — which C#
name keys which element, where the two decoupling knobs are, and what the package identity
namespaces. This page is the part that is advice rather than contract.

The one sentence everything here follows from:

> **Renaming an identifier mints a new translation key.** Translations authored against the old one
> are orphaned — they keep existing and stay visible, but nothing re-attaches them. Re-attaching is
> manual, per language. Nothing *breaks*: an untranslated string falls back to the string compiled
> into your package.

- **Get the names right before the first upload.** That is the only moment renaming is free.
- **After translations exist, prefer additive change.** A new property is a new key and costs
  nothing; a renamed property costs its translations.
- **Where a knob exists — a contract binding, an interface binding — rename the C# member and pin the
  old identifier.** That is what the knob is for.
- **Where it does not — a service, a service property, a measuring point — expect to re-attach.** The
  Translations tab keeps the orphaned row visible so its text can be copied onto the new key, one
  language at a time. There is no override attribute for these, and none is planned.
- **Changing a display string is not a rename.** The key survives; existing translations keep being
  served and are flagged as outdated so the author can revisit them.
- **Treat your `<PackageId>` as fixed once the library has been uploaded.** It namespaces every key
  in the library, so changing it is the single most expensive rename available. It is also the id the
  platform registers your library under, globally and case-insensitively: `dale upload` fails with a
  409 when the id is already taken by someone else. Use a vendor-prefixed id (`Acme.Chargers`), the
  same discipline nuget.org asks for.
- **The contract-type token is the one identifier that is not a translation key**, and the one whose
  rename actually breaks something: the platform pairs a binding to its handler through it, so an
  orphaned contract type does not match where an orphaned translation merely falls back. Pick it once.

`dale list` prints the block identities and the service, member, contract and interface identifiers
exactly as the introspection document emits them, which is the cheapest way to see what a rename
would cost before you make it.
