---
trace: enforced
---

# The `dale` command-line tool

What the `dale` global tool guarantees a logic-block author and their continuous integration. It is
the primary interface consumers of this SDK actually touch: it scaffolds a library, generates members
inside it, wraps `dotnet` for building and packing, bridges to the introspection tool and the
development host, and publishes to the Vion Cloud. Area code `CLI`. Process:
[`../spec-process.md`](../spec-process.md).

The spine is the order a caller meets the machinery: how an invocation is read and where its output
goes, the command tree, project discovery and the `dotnet` wrappers, the two generators, the bridges
to other areas, `upload`, identity and auth, the surface gate, and the test discipline this area's
own suite follows.

Cited rather than restated: [`devhost-control.md`](devhost-control.md) for what the host does with
what `dale dev` hands it — its § *The process contract* already says this option surface is the
command-line tool's; [`introspection.md`](introspection.md) for the document `dale list` renders and
for the identity rules it carries through (`AC-INTRO-001.2`, `AC-INTRO-004.1`, `AC-INTRO-002.7`, and
`AC-INTRO-014.3`, which refuses a blank binding identifier at the source);
[`scenarios.md`](scenarios.md) for the file rules `dale scenario validate` mirrors;
[`config-gating.md`](config-gating.md) (`AC-GATE-012.7`) for the gate predicate the listing carries.
Architecture decisions
[`0111`](../../architecture/decisions/0111-packageid-globally-unique.md) (a package id is globally
unique) and [`0032`](../../architecture/decisions/0032-first-party-logic-block-library-lane.md) (the
first-party library lane) are the design authority for the upload's conflict handling and the
publishing lane, and are cited, never re-argued.

**No `DALE` diagnostic judges any declaration in this tool or its tests.** Neither project carries
the analyzer `ProjectReference` that `Vion.Dale.Sdk.DigitalIo` and `.AnalogIo` do, so
`AC-ANLZ-018.*` puts every type and XML doc here outside the pack. The tool publishes no types
either — it carries no `[PublicApi]` mark and no manifest row — so its surface is its command line,
and the gate over that is the help snapshot described below.

## The invocation, the two output modes, and the two streams

Every command reads the same three global options and answers on the same two streams. The rule that
matters most to a script: **in table mode a failure goes to standard error and everything else to
standard output; in JSON mode there is one stream, and it carries only JSON.**

- `AC-CLI-001.1` (Event-driven): WHEN `--version` or `-v` appears before any command name THE SYSTEM
  SHALL print the tool's version and exit 0 without running a command, and SHALL leave the option to
  the command WHERE a command has already been named.
- `AC-CLI-001.2` (Ubiquitous): THE SYSTEM SHALL print that version with the source-link commit
  suffix removed.
- `AC-CLI-001.3` (Event-driven): WHEN an option is given a value it does not accept THE SYSTEM SHALL
  report it as a command-line error and exit 1.
- `AC-CLI-001.4` (State-driven): WHERE the output format is `table` THE SYSTEM SHALL write a failure
  to standard error and every other line to standard output.
- `AC-CLI-001.5` (State-driven): WHERE the output format is `json` THE SYSTEM SHALL suppress every
  human-readable line and write both results and failures to standard output as JSON, so the mode
  has one stream.
- `AC-CLI-001.6` (Ubiquitous): THE SYSTEM SHALL report a failure in JSON mode as an object carrying
  an `error` member.
- `AC-CLI-001.7` (Ubiquitous): THE SYSTEM SHALL suppress verbose output unless `--verbose` was
  given, and in JSON mode always.
- `AC-CLI-001.8` (Ubiquitous): THE SYSTEM SHALL write its output as UTF-8 whatever the console's default encoding, so a redirected stream carries the characters it wrote. GAP: the observable is the byte sequence a *redirected* process emits, which no in-process test can construct; verified by probe (`dale --version > file` carries `e2 8094` for the em dash).
- `AC-CLI-001.9` (State-driven): WHERE the output format is `json` THE SYSTEM SHALL capture every
  child process's standard output and relay it to standard error, so standard output carries the
  tool's document and nothing else.
- `AC-CLI-001.10` (Ubiquitous): THE SYSTEM SHALL emit every JSON document with camel-cased member
  names, enum values as strings and indentation, and SHALL read JSON case-insensitively — every
  document but the scenario schema, whose own escaping `AC-CLI-010.8` states.

The JSON-mode rule is the reason the child-process rule exists: every command in this tool that does
real work starts `dotnet`, and MSBuild's restore banner in front of a JSON document is what makes
that document unparseable. Standard error is never redirected — it is already where a diagnostic
belongs.

## The command tree

- `AC-CLI-002.1` (Ubiquitous): THE SYSTEM SHALL offer thirteen top-level commands — `new`, `build`,
  `test`, `dev`, `list`, `scenario`, `add`, `pack`, `upload`, `login`, `logout`, `whoami` and
  `config` — and SHALL make `--output`, `--project` and `--verbose` available on every one of them
  and on their subcommands.
- `AC-CLI-002.2` (Event-driven): WHEN no command or an unknown command is given THE SYSTEM SHALL
  refuse and exit 1.

Exit codes are 0 and 1, plus whatever `dotnet` returns from a wrapped `build`, `test`, `pack` or
`dev`. A command-line error is the parser's own: its diagnostic on standard error, its help on
standard output, exit 1.

## Finding the project, and the `dotnet` it wraps

- `AC-CLI-003.1` (Ubiquitous): THE SYSTEM SHALL treat a project as a Dale project exactly when it
  references the Vion Dale SDK, as a package or as a project.
- `AC-CLI-003.2` (Event-driven): WHEN no project is named THE SYSTEM SHALL use the nearest Dale
  project walking up from the working directory, and WHEN one is named THE SYSTEM SHALL use it
  without walking up.
- `AC-CLI-003.3` (Ubiquitous): THE SYSTEM SHALL take the nearest solution walking up, preferring the
  classic format over the XML one in the same directory and breaking a tie within one format by
  ordinal name.
- `AC-CLI-003.4` (Event-driven): WHEN `--project` names a project THE SYSTEM SHALL build and test
  that project, whatever solution sits above the working directory.
- `AC-CLI-003.5` (Event-driven): WHEN no `--project` is given THE SYSTEM SHALL build the nearest
  solution walking up from the working directory, and the nearest Dale project where there is none.
- `AC-CLI-003.6` (Event-driven): WHEN `--project` names a file that does not exist, or one that
  references neither the SDK package nor the SDK project, THE SYSTEM SHALL refuse rather than
  resolve another project.
- `AC-CLI-003.7` (Event-driven): WHEN a supplied `--project` cannot be used THE SYSTEM SHALL name
  that path and why, and SHALL NOT instruct the caller to supply the option they already supplied.
- `AC-CLI-003.8` (Ubiquitous): THE SYSTEM SHALL read a solution's projects from both the classic and
  the XML solution format, at any folder depth, and SHALL treat only those referencing the SDK as
  Dale projects.
- `AC-CLI-003.9` (Ubiquitous): THE SYSTEM SHALL report a project's package identity as its declared
  package id, falling back to its file name, and its root namespace the same way.

`--project` is an instruction, not a hint: it wins over a solution above the working directory, and
a path that cannot be used is refused rather than worked around. That is what keeps a typo from
editing or publishing a project the caller did not name.

- `AC-CLI-004.1` (Ubiquitous): THE SYSTEM SHALL hand `dotnet` the verb followed by the caller's
  arguments, each as its own argument-list entry and in the order given, so a path with spaces and a
  filter expression survive unquoted.
- `AC-CLI-004.2` (Ubiquitous): THE SYSTEM SHALL run `dotnet` without a shell, in the directory the
  command names.
- `AC-CLI-004.3` (Ubiquitous): THE SYSTEM SHALL forward tokens it does not recognise to the wrapped
  `dotnet` invocation on `build`, `test` and `dev`, and SHALL refuse them on every other command.

## `dale new`, and the bundled template

- `AC-CLI-005.1` (Ubiquitous): THE SYSTEM SHALL accept a project name of a letter followed by letters, digits, dots, hyphens and underscores, and SHALL refuse any other name before running anything. GAP: `dale new` runs `dotnet new` twice and `dotnet restore`; no test in this area spawns a process.
- `AC-CLI-005.2` (Ubiquitous): THE SYSTEM SHALL refuse to scaffold into a directory that already exists, set the new project's package identity to its name, and scaffold from the template bundled inside the tool. GAP: same spawned-process path as `AC-CLI-005.1`.
- `AC-CLI-005.3` (Event-driven): WHEN a project name is refused THE SYSTEM SHALL state the whole rule, first character included. GAP: the refusal is inside `dale new`'s action, which runs `dotnet new` twice and `dotnet restore`; no test in this area spawns a process.
- `AC-CLI-005.4` (Event-driven): WHEN no project name is given and the session cannot prompt THE SYSTEM SHALL refuse naming the reason that applies — the option that was passed, or the output format that made it non-interactive. GAP: same spawned-process path as `AC-CLI-005.1`.
- `AC-CLI-005.5` (Event-driven): WHEN installing the bundled template fails THE SYSTEM SHALL report what the installer said and refuse, and WHEN restoring the new project fails THE SYSTEM SHALL warn with what the restore said and keep the scaffold. GAP: the failures reported are those of the spawned `dotnet new install` and `dotnet restore`.
- `AC-CLI-005.6` (Ubiquitous): THE SYSTEM SHALL report the logic blocks it scaffolded, read from the project it wrote. GAP: the blocks are read from a tree `dotnet new` has just written.
- `AC-CLI-005.7` (State-driven): WHERE the scaffolded project references an SDK version other than the tool's own THE SYSTEM SHALL say which it wrote. GAP: the comparison is against a scaffold `dotnet new` has just written.

The template travels inside the tool, and a pack-time target rewrites its SDK references to the
tool's own version — except for a `0.0.0` version, which is on no feed and would scaffold a project
that cannot restore. The criterion above about naming the SDK version that was written is why that
exception is visible rather than silent.

## The generators

- `AC-CLI-006.1` (Ubiquitous): THE SYSTEM SHALL find a project's logic blocks by reading its source
  for classes deriving from the SDK's logic-block base, excluding build output.
- `AC-CLI-006.2` (Ubiquitous): THE SYSTEM SHALL target the project's only logic block where it has
  one, the one `--to` names case-insensitively where it is given, and SHALL refuse listing them
  otherwise.
- `AC-CLI-006.3` (Ubiquitous): THE SYSTEM SHALL escape a backslash and a double quote in every
  author-supplied value it writes into an emitted string literal.
- `AC-CLI-006.4` (Ubiquitous): THE SYSTEM SHALL refuse a member or logic-block name that is not a C#
  identifier, before writing anything.
- `AC-CLI-006.5` (Ubiquitous): THE SYSTEM SHALL refuse a `--type` that does not read as a C# type
  reference, before writing anything.
- `AC-CLI-006.6` (Ubiquitous): THE SYSTEM SHALL read an existing member's declared type and its
  attributes before deciding whether a name collides.
- `AC-CLI-006.7` (Event-driven): WHEN the named member already exists as a property of the same type
  and does not yet carry the annotation being added THE SYSTEM SHALL add that annotation to it, and
  SHALL refuse every other collision.
- `AC-CLI-006.8` (Ubiquitous): THE SYSTEM SHALL render every number it writes into generated source
  in the invariant culture.
- `AC-CLI-006.9` (Ubiquitous): THE SYSTEM SHALL refuse a timer interval that is not a positive
  finite number of seconds.
- `AC-CLI-006.10` (Ubiquitous): THE SYSTEM SHALL emit a logic-block class deriving from the SDK's
  base, carrying the declared name and icon only where they were given.
- `AC-CLI-006.11` (Ubiquitous): THE SYSTEM SHALL emit the presentation attribute — on a member it
  creates and on one it annotates — only where at least one presentation option was given and the
  member does not already carry one, in a stable argument order, rendering a well-known property
  group as its member reference and any other group as a string.
- `AC-CLI-006.12` (Ubiquitous): THE SYSTEM SHALL emit a timer method carrying its interval in
  seconds.
- `AC-CLI-006.13` (Ubiquitous): THE SYSTEM SHALL emit a service property with the declared setter
  visibility, and a measuring point always privately set, each carrying its title, its optional
  persistence and — for a measuring point — its optional kind, and SHALL carry that same title,
  persistence and kind onto a member it annotates.
- `AC-CLI-006.14` (Ubiquitous): THE SYSTEM SHALL read as a C# identifier a letter or underscore
  followed by letters, digits or underscores, and as a type reference an optionally dotted identifier
  carrying any mix of generic arguments, array ranks and a nullable mark.

`AC-CLI-006.7` is what lets a generator express the SDK's dual-annotation shape — one property
carrying both a service property and a measuring point, which [`emission.md`](emission.md)
specifies and which publishes to two independent streams. Every other name collision is still
refused.

`AC-CLI-006.4`, `AC-CLI-006.5` and `AC-CLI-006.9` all refuse **before** anything is written, in every
`add` verb: a generator that reports success and leaves a file that will not compile sends the author
looking for a fault in code they did not write. The two name shapes those refusals rest on are
`AC-CLI-006.14`'s, stated once and checked by each verb.

The presentation and persistence options reach a member the generator *annotates* exactly as they
reach one it creates — one builder with two callers — minus any attribute the member already carries,
because a second presentation or persistence attribute on one member does not compile.

## Inserting into a source file

- `AC-CLI-007.1` (Ubiquitous): THE SYSTEM SHALL insert a generated member before the target class's
  closing brace, at the indentation its existing members use, separated by a blank line.
- `AC-CLI-007.2` (Ubiquitous): THE SYSTEM SHALL find that brace by counting braces, which counts braces inside string literals and comments too. GAP: a stated limitation, not a guarantee: no assertion can pin what brace counting does to every shape of string literal, and the shapes it does handle are covered by `AC-CLI-007.1`.
- `AC-CLI-007.3` (Ubiquitous): THE SYSTEM SHALL write a source file it inserts into with that file's
  predominant line ending, applied to every line, and with its byte-order mark, changing nothing else
  about the lines it did not insert.
- `AC-CLI-007.4` (Ubiquitous): THE SYSTEM SHALL add the SDK's using directive after the file's last
  using directive, or at the top where there is none, and SHALL NOT add it twice.

The brace-counting criterion above is a stated limitation, not an oversight: this area manipulates
source with regular expressions and brace counting rather than a compiler, and
`Vion.Dale.Cli/CLAUDE.md` records Roslyn as the improvement. `AC-CLI-007.3` is what keeps that
limitation from also costing the author a whole-file diff. Its rule is the file's *predominant*
ending rather than each line's own: a file of mixed endings — a merge artifact, a generated partial —
comes out consistent, which is a second small edit to it and is stated here rather than discovered.
Every insertion the generators make goes through it, the dependency registration `dale add logicblock`
writes included.

## `dale list` — the bridge to the introspection document

The document is [`introspection.md`](introspection.md)'s. What this tool adds is the publish, the
tool lookup, and the two projections.

- `AC-CLI-008.1` (Ubiquitous): THE SYSTEM SHALL publish the project and run the introspection tool
  over the published output, finding that tool in the package matching the project's SDK version and
  falling back to a sibling source tree.
- `AC-CLI-008.2` (Ubiquitous): THE SYSTEM SHALL read the introspection document tolerantly, ignoring
  members it does not know and defaulting members it does not find.
- `AC-CLI-008.3` (Ubiquitous): THE SYSTEM SHALL mark a logic block that binds a contract the
  document annotates development-only, and SHALL list it like any other.
- `AC-CLI-008.4` (Ubiquitous): THE SYSTEM SHALL render a logic block's short name as the last
  segment of its identity, past the nesting separator as well as the namespace separator.
- `AC-CLI-008.5` (Ubiquitous): THE SYSTEM SHALL render the same set of contract and interface
  bindings in both output modes, whatever identifier each carries.
- `AC-CLI-008.6` (Event-driven): WHEN the document reports a block's identity, contracts, services
  or interfaces as absent THE SYSTEM SHALL render the block rather than fail, in either output mode.
- `AC-CLI-008.7` (Ubiquitous): THE SYSTEM SHALL report the package identity and version the document
  carries, falling back to the project's own where it carries none, and SHALL report the SDK version
  the project references.
- `AC-CLI-008.8` (Ubiquitous): THE SYSTEM SHALL render each service property's and measuring point's
  type from its introspection schema, and an empty type where the document carries no schema.
- `AC-CLI-008.9` (Ubiquitous): THE SYSTEM SHALL render every identifier in the table literally,
  whatever characters it carries.
- `AC-CLI-008.11` (Ubiquitous): THE SYSTEM SHALL head the table listing with the project's name and
  version and the SDK version it references, and SHALL say so where the project declares no logic
  block rather than print an empty listing.
- `AC-CLI-008.10` (Ubiquitous): THE SYSTEM SHALL run the introspection tool with the project's package identity, and SHALL NOT ask it to exclude development-only blocks — a block the packed artifact omits is listed, marked. GAP: the argument list is handed to a spawned introspection tool; what it omits is proven by the absence of an option, which no assertion can observe.

A development-only block is listed, marked, and never hidden: `dale list` answers "what is in this
project", not "what reaches the cloud". `dale pack` filters it out of the artifact and says so with
`AC-INTRO-002.7`'s notice, which `AC-CLI-011.9` relays.

## `dale dev` — the bridge to the development host

What the host does with what it is handed is [`devhost-control.md`](devhost-control.md)'s. What this
tool does is find the project, set four variables, compose the run, and bound the export.

- `AC-CLI-009.1` (Ubiquitous): THE SYSTEM SHALL find a runnable development-host project in the working directory, then its subdirectories, then its siblings, treating a project as runnable when it builds an executable or sits beside a program entry point. GAP: discovery walks a real directory tree and reads csproj files for a runnable host; the fixture is a DevHost project, which this area's suite does not build.
- `AC-CLI-009.2` (Ubiquitous): THE SYSTEM SHALL hand the host its browser, stepping and export instructions through the four `DALE_DEVHOST_` environment variables, resolving each export path to an absolute one. GAP: the variables are set on the tool's own process for a child to inherit; the observable is what the spawned host reads, which is `devhost-control.md`'s.
- `AC-CLI-009.3` (Ubiquitous): THE SYSTEM SHALL pass the preset as the host application's first
  program argument followed by any forwarded tokens, delimited so the host reads them verbatim, and
  SHALL pass no delimiter where there is nothing to forward.
- `AC-CLI-009.4` (Event-driven): WHEN an export is asked for THE SYSTEM SHALL remove the target
  first and SHALL refuse when it cannot, distinguishing a missing parent directory from an
  undeletable file.
- `AC-CLI-009.5` (Event-driven): WHEN an export is asked for THE SYSTEM SHALL bound the wait for the
  file to appear, allow the host a grace period to exit afterwards, and stop a host that never wrote
  it.
- `AC-CLI-009.6` (State-driven): WHERE no export was asked for THE SYSTEM SHALL run the host until it exits and return its exit code. GAP: the exit code is the spawned host's; no test in this area spawns one.
- `AC-CLI-009.7` (Event-driven): WHEN `--project` points into a directory that does not exist THE SYSTEM SHALL refuse naming it rather than fail while enumerating it. GAP: the refusal is reached through `dale dev`'s action, which then looks for a DevHost project to run.
- `AC-CLI-009.8` (Event-driven): WHEN an export was asked for THE SYSTEM SHALL exit 0 whenever every
  export file was written, whatever code the host itself exited with, and SHALL report a failure
  only when nothing was written.
- `AC-CLI-009.9` (Ubiquitous): THE SYSTEM SHALL announce what it is about to do — a web UI, a
  control API, or a one-shot export that starts no server.

The address the tool announces is the configured default, not a bound port — nothing here checks
what the host actually binds, and the readiness handshake that would answer it is the host's.

## The `scenario` group

The scenario file's own rules are [`scenarios.md`](scenarios.md)'s, and the validator here is a
deliberately lite, language-neutral mirror of them. The command surface is this page's.

- `AC-CLI-010.1` (Ubiquitous): THE SYSTEM SHALL offer five scenario subcommands — run, validate, schema, scaffold and open — and SHALL let every one that addresses a running host name its port, defaulting to 5000; scaffolding reads and writes files only.
- `AC-CLI-010.2` (Event-driven): WHEN a scenario is run THE SYSTEM SHALL apply it, wait for that run's own report to leave the running state, and exit 0 only where it succeeded. GAP: the run is applied to a live host over HTTP and polled until its report settles; no test in this area starts one.
- `AC-CLI-010.3` (Event-driven): WHEN the host answers an apply by recycling THE SYSTEM SHALL wait for it to return and re-apply, up to a bounded number of attempts. GAP: recycling is a live host's answer to an apply.
- `AC-CLI-010.4` (Event-driven): WHEN no host answers on the port THE SYSTEM SHALL refuse naming the port and the command that starts one. GAP: proven for `open` by `AC-CLI-010.12`; the other three verbs reach it through their own live-host calls.
- `AC-CLI-010.5` (Event-driven): WHEN every scenario file is validated THE SYSTEM SHALL check them in ordinal name order and exit 1 where any has an error, skipping the name-path checks of a file targeting another topology and saying so. GAP: the ordering and the per-file outcome are `scenarios.md`'s rules over a directory of files; the empty-directory refusal is `AC-CLI-010.11`.
- `AC-CLI-010.6` (Ubiquitous): THE SYSTEM SHALL take the configuration to validate against from the export file where one is named and from the running host otherwise, refusing where neither is available. GAP: the second source is a live host's `/api/configuration`; the file source is proven by `AC-CLI-010.8`'s offline run.
- `AC-CLI-010.7` (Event-driven): WHEN a scenario is scaffolded THE SYSTEM SHALL refuse a missing file, one that is not JSON, one carrying no id, and one whose id differs from its name, each with its own message. GAP: the four refusals are inside `scenario scaffold`'s action, which then writes a file; the file-locating half is proven by `AC-CLI-010.9`.
- `AC-CLI-010.8` (Ubiquitous): THE SYSTEM SHALL carry the generic scenario schema inside itself, so
  the schema can be produced with no host running, SHALL enrich it with a configuration's name paths
  where one is available, and SHALL write it with relaxed escaping, keeping every character literal.
- `AC-CLI-010.9` (Ubiquitous): THE SYSTEM SHALL emit from a scenario file a test that carries its
  id, topology, steps, watch list and one marker per human judgment, and an unimplemented host
  factory.
- `AC-CLI-010.10` (Ubiquitous): THE SYSTEM SHALL take `scenario schema`'s and `scenario scaffold`'s
  destination from `--out`, whose short form is `-O` and whose deprecated alias is `-o`, and SHALL
  leave `--output` meaning the tool's output format everywhere.
- `AC-CLI-010.11` (Event-driven): WHEN the scenarios directory holds no scenario file THE SYSTEM
  SHALL refuse naming it rather than report a successful validation.
- `AC-CLI-010.12` (Event-driven): WHEN no host answers on the port THE SYSTEM SHALL refuse to report
  a scenario as opened.
- `AC-CLI-010.13` (Event-driven): WHEN the host stops answering while a run is in flight THE SYSTEM SHALL report that and exit 1. GAP: a host that answers the apply and then dies mid-poll is a window no seam in this area constructs; the guarded and unguarded calls are eleven lines apart and the fix is the `catch` the apply already had.

`AC-CLI-010.10` is a deliberate asymmetry with a deprecation in it: `--out` is the file, `--output`
is the format, `-O` is the file's short form, and `-o` remains an alias for it for one release
because two committed consumer scripts pass it that way. The schema is also the one document
`AC-CLI-001.10` does not govern: it is written with relaxed escaping, because it is a committed file
an editor reads and the strict encoder's numeric escapes made every regeneration a phantom diff
against the apostrophes and dashes the source schema holds literal.

## `dale upload`

- `AC-CLI-011.1` (Ubiquitous): THE SYSTEM SHALL upload the packed package as multipart form data bearing the resolved access token, to the integrator's library-versions endpoint. GAP: the upload runs inside the command's own action, which packs a real project first; the request's shape is stated and its transport is proven by `AC-CLI-017.1`.
- `AC-CLI-011.2` (State-driven): WHERE no release notes were given THE SYSTEM SHALL omit that part rather than send an empty one. GAP: same path as `AC-CLI-011.1`.
- `AC-CLI-011.3` (Ubiquitous): THE SYSTEM SHALL pack in Release with packing forced on, and SHALL
  pass an explicit version to the pack only when one was given.
- `AC-CLI-011.4` (Ubiquitous): THE SYSTEM SHALL report the version it packed by reading it back from
  the produced package's manifest, and SHALL report none where the package carries no manifest.
- `AC-CLI-011.5` (Ubiquitous): THE SYSTEM SHALL take as the project's package the most recently
  written one whose file name is the project's package id followed by a version, and SHALL find none
  rather than take a differently named package.
- `AC-CLI-011.6` (Event-driven): WHEN the pack fails THE SYSTEM SHALL report the errors its output
  named.
- `AC-CLI-011.7` (Ubiquitous): THE SYSTEM SHALL report an upload in JSON mode in a shape it owns — a
  status, the package identity and version, the parser's notices, and the endpoint's own answer
  nested under a member of its own.
- `AC-CLI-011.8` (State-driven): WHERE duplicates are to be skipped THE SYSTEM SHALL treat only a
  conflict naming an existing version as a skip, and SHALL fail on every other conflict.
- `AC-CLI-011.9` (Ubiquitous): THE SYSTEM SHALL relay the introspection tool's pack-time notices,
  and nothing else of the pack's output, in both output modes.
- `AC-CLI-011.10` (State-driven): WHERE a continuous-integration upload supplies no integrator THE
  SYSTEM SHALL resolve it from the credential's own memberships, which holds while each service
  account maps to exactly one integrator.

`AC-CLI-011.8` rests on the endpoint's message text, because both conflicts arrive as the same
status and the same exception type. That is a known weakness with six readers — every
`--skip-duplicate` invocation across this repository's two upload workflows and the first consumer's
two release workflows — and the fix belongs to the platform API, which would carry a
distinguishable field. Until then the substring match is the contract.

The production gate is a GitHub Environment with a required reviewer, not a flag, a scope or a
credential this tool knows about. The two systems share the word "environment" and are unrelated:
`--environment` selects the cloud host, `environment:` in a workflow selects the approval gate and
the credentials.

## Identity: the token, the environments, the integrator, the stores

- `AC-CLI-012.1` (Ubiquitous): THE SYSTEM SHALL treat a stored access token as expired thirty
  seconds before its stated expiry.
- `AC-CLI-012.2` (Ubiquitous): THE SYSTEM SHALL resolve an access token from the client-id and
  client-secret options first, from `DALE_CLIENT_ID` with `DALE_CLIENT_SECRET` second, and from the
  stored login third, and SHALL fall through to the next source WHERE only one half of a credential
  pair is supplied.
- `AC-CLI-012.3` (Event-driven): WHEN no credential source yields a token THE SYSTEM SHALL refuse
  naming all three ways to supply one.
- `AC-CLI-012.4` (Event-driven): WHEN the stored token has expired and carries a refresh token THE
  SYSTEM SHALL exchange it for a new one and store the result.
- `AC-CLI-012.5` (Event-driven): WHEN a refresh fails, and WHEN the stored token has expired with no
  refresh token, THE SYSTEM SHALL refuse with a distinct message each.
- `AC-CLI-012.6` (Event-driven): WHEN the stored login was minted for an environment other than the
  resolved one THE SYSTEM SHALL refuse naming both rather than send that token to the other
  environment.

- `AC-CLI-013.1` (Ubiquitous): THE SYSTEM SHALL know two named environments — `production` and
  `test` — and SHALL resolve each to its identity-provider realm and its cloud API base URL,
  matching the name case-insensitively.
- `AC-CLI-013.2` (Ubiquitous): THE SYSTEM SHALL report no URL for an environment that is not one of
  the two named ones, and SHALL name only those two as known.
- `AC-CLI-013.3` (Ubiquitous): THE SYSTEM SHALL resolve the environment from the `--environment`
  option first, the stored configuration second, and `production` third.
- `AC-CLI-013.4` (State-driven): WHERE the environment is not a named one THE SYSTEM SHALL take its
  auth and API URLs from the stored configuration.
- `AC-CLI-013.5` (Ubiquitous): THE SYSTEM SHALL exchange credentials against the resolved
  environment's realm, taking a custom environment's realm from the stored configuration, and SHALL
  refuse before contacting anything WHEN no realm can be resolved.

- `AC-CLI-014.1` (Ubiquitous): THE SYSTEM SHALL resolve the integrator from `--integrator-id` first,
  `DALE_INTEGRATOR_ID` second, the stored configuration third, and the token's memberships fourth.
- `AC-CLI-014.2` (Event-driven): WHEN `DALE_INTEGRATOR_ID` is set to a value that is not an
  integrator id THE SYSTEM SHALL refuse naming the variable and the value.
- `AC-CLI-014.3` (Event-driven): WHEN the integrator must come from the token's memberships THE
  SYSTEM SHALL take the only one, refuse when there is none, and refuse listing them when there are
  several.
- `AC-CLI-014.4` (Ubiquitous): THE SYSTEM SHALL ask the cloud API for the memberships of the token
  it holds, at `/me`, bearing that token.
- `AC-CLI-014.5` (Ubiquitous): THE SYSTEM SHALL read that answer tolerantly — an absent email, no
  memberships and members it does not know are all read — and SHALL refuse an answer that is not a
  membership document.

- `AC-CLI-015.1` (Ubiquitous): THE SYSTEM SHALL keep stored credentials and stored configuration as
  two files in a `.dale` directory under the user profile, creating the directory on first write.
- `AC-CLI-015.2` (Ubiquitous): THE SYSTEM SHALL restrict the credentials file to its owner on
  platforms that carry file modes, and SHALL NOT fail the write where it cannot.
- `AC-CLI-015.3` (Event-driven): WHEN the credentials file is absent or unreadable THE SYSTEM SHALL
  report no stored credentials rather than fail.
- `AC-CLI-015.4` (Event-driven): WHEN the configuration file is absent or unreadable THE SYSTEM
  SHALL report the default configuration rather than fail.
- `AC-CLI-015.5` (Ubiquitous): THE SYSTEM SHALL round-trip a stored credential's access token,
  refresh token, expiry and environment.
- `AC-CLI-015.6` (Ubiquitous): THE SYSTEM SHALL clear only the credentials file when credentials are
  deleted, leaving the configuration in place, and SHALL complete when there was nothing to clear.

## The interactive login

- `AC-CLI-016.1` (Ubiquitous): THE SYSTEM SHALL authenticate interactively with authorization code and proof key exchange, over a loopback redirect on a free port, checking the returned state against the one it sent. GAP: the flow opens a browser and binds a loopback listener; no test in this area reaches either.
- `AC-CLI-016.2` (Ubiquitous): THE SYSTEM SHALL abandon an interactive login that is not completed within five minutes, and SHALL report the identity provider's own error description where it returns one. GAP: same browser-bound path as `AC-CLI-016.1`.
- `AC-CLI-016.3` (Event-driven): WHEN the browser cannot be opened THE SYSTEM SHALL print the authorization URL so the login can be completed by hand. GAP: same browser-bound path as `AC-CLI-016.1`.

## Talking to the cloud API

- `AC-CLI-017.1` (Ubiquitous): THE SYSTEM SHALL send every cloud API request bearing the resolved
  access token and identifying itself as `Vion.Dale.Cli` with its own version.
- `AC-CLI-017.2` (Event-driven): WHEN the cloud API refuses a request THE SYSTEM SHALL name the
  recovery for an unauthorized and a forbidden answer, name the endpoint for a not-found answer, and
  report the status code with the server's own sentence for any other failure.
- `AC-CLI-017.3` (Event-driven): WHEN a request times out or its host cannot be reached THE SYSTEM
  SHALL report which request it was and what to check.
- `AC-CLI-017.4` (Ubiquitous): THE SYSTEM SHALL report the human sentence out of the platform's
  error envelope whatever case its members carry, and the raw body when the answer is not that
  shape.
- `AC-CLI-017.5` (State-driven): WHERE a caller allows a status THE SYSTEM SHALL return that answer
  instead of refusing.
- `AC-CLI-017.6` (Ubiquitous): THE SYSTEM SHALL bound every cloud API request by thirty seconds. GAP: the only observable is a thirty-second wall-clock wait, which `../testing-conventions.md` § 16 forbids a test from standing on.

The thirty-second bound is a single ceiling over every request, the package upload included. It has
not been reached by any of the four workflow uploads, and it is a limit nobody chose for that one
request rather than a considered one.

## The identity commands

- `AC-CLI-018.1` (Event-driven): WHEN no valid login is stored THE SYSTEM SHALL refuse to report an
  identity and exit 1.
- `AC-CLI-018.2` (Ubiquitous): THE SYSTEM SHALL report the stored identity in whichever output mode
  was asked for, and SHALL report the token's own lifetime even where the identity cannot be
  fetched.
- `AC-CLI-018.3` (Ubiquitous): THE SYSTEM SHALL clear only the stored credentials on logout, report
  whether there was anything to clear, and exit 0 either way.
- `AC-CLI-018.4` (Event-driven): WHEN an interactive login cannot fetch the account's memberships THE SYSTEM SHALL keep the stored integrator, save the environment's URLs, and exit non-zero. GAP: the path runs behind `AuthService.AcquireInteractiveAsync`, which opens a browser and binds a loopback listener; no test in this area reaches a browser.
- `AC-CLI-018.5` (Event-driven): WHEN the account has exactly one integrator membership THE SYSTEM
  SHALL select it without asking, and WHERE it has several and cannot ask THE SYSTEM SHALL refuse
  naming them and the option that chooses one.
- `AC-CLI-018.6` (Event-driven): WHEN switching environment would clear a configured integrator THE
  SYSTEM SHALL confirm first, treat `--force` as that confirmation, and exit non-zero when the
  change was declined.
- `AC-CLI-018.7` (Event-driven): WHEN an interactive login selects an integrator THE SYSTEM SHALL store it, and SHALL leave a previously stored one in place where it selected none. GAP: same browser-bound path as `AC-CLI-018.4`.
- `AC-CLI-018.8` (Event-driven): WHEN `--integrator-id` names a value that is not one of the
  account's own integrator memberships THE SYSTEM SHALL refuse naming it, leaving the stored one in
  place.

None of these prompts in JSON mode: the mode exists so an agent can drive the tool, and a selection
prompt has no one to answer it. Each refuses instead, naming the option that decides. And the option
that decides is itself checked against the account's own memberships: `dale config set-integrator` is
the repair command, so an identifier that is not the caller's to publish under is refused rather than
stored.

## The surface gate, and the two constants

- `AC-CLI-019.1` (Ubiquitous): THE SYSTEM SHALL keep the committed help snapshot equal to the packed tool's own help over every command surface it publishes, its subcommands included. GAP: the observable is a diff between the committed file and the help of a *packed and installed* tool, which lives in `.github/workflows/publish.yml` and which no in-process test can construct.
- `AC-CLI-019.2` (Ubiquitous): THE SYSTEM SHALL authenticate as the Keycloak public client `dale-cli`, and SHALL read and write only environment variables prefixed `DALE_`. GAP: both are constants of external identity — the client is provisioned outside this repository (`../../architecture/systems/keycloak.md`) and the prefix is a naming rule over seven variables; a test can only restate them.

The snapshot is regenerated from the **packed** tool and auto-committed onto the pull request's head;
a change to it opens a drift issue on the architecture repository. A locally installed `dale` is not
the packed one — regenerating against a stale global tool produces a snapshot of the wrong surface.

## Test discipline

This area's suite is MSTest, inside the package, and reaches **no network, no browser, no identity
provider, no spawned process and no developer home directory**. Three things make that possible and
are part of the contract, not conveniences: the transport behind the cloud client and behind the
token exchanges is settable, the credential store's root is settable, and the `dotnet` invocation's
composition is a function rather than a side effect. A criterion that still needs a browser or a real
identity provider carries its `GAP` and says which.

Those seams are not test-only accessors: the tool composes itself through them. Its entry point sets
the real transport for both HTTP clients and the real user profile for the store root before it
answers anything, so a test overrides a value production also sets rather than filling a hole
production leaves empty — and that composition is the one place either is chosen. The `dale list`
renderer takes its console the same way, and writes its heading and its empty-project line through
it too, so the whole rendering is observable and not only the tables inside it.

The generators are exercised against real temporary directories, cleaned up. Numbers written into
generated source and into messages are rendered in the invariant culture, and the tests that pin them
run under a comma-decimal and a `U+2212`-negative culture, because one machine is one locale.
