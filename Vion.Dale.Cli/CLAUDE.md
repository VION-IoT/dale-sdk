# Vion.Dale.Cli

CLI tool for developing and publishing Dale LogicBlock libraries. Installed as a .NET global tool (`dale`).

## Adding a New Command

Every command is a **static class** with a `Create()` method that returns `System.CommandLine.Command`. There are three patterns:

### Pattern 1: Project command (like `dale pack`, `dale dev`)

```csharp
public static class MyCommand
{
    public static Command Create()
    {
        var command = new Command("mycommand", "Description here");

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var projectPath = parseResult.GetValue<string?>("--project");

            var project = CommandHelpers.RequireProject(projectPath);
            if (project == null) return 1;  // error already printed

            // ... do work with project ...

            if (DaleConsole.JsonMode)
            {
                DaleConsole.WriteJsonResult(new { result = "value" });
            }
            else
            {
                DaleConsole.Success("Did", "the thing");
            }

            return 0;
        });

        return command;
    }
}
```

Register in `Program.cs` under the appropriate section (Local / Publishing / Auth).

### Pattern 2: Add command (like `dale add serviceproperty`)

```csharp
var project = CommandHelpers.RequireProject(projectPath);
if (project == null) return 1;

var target = CommandHelpers.RequireTarget(project, toOption);
if (target == null) return 1;

// Check for duplicates BEFORE inserting
var source = File.ReadAllText(target.FilePath);
if (Regex.IsMatch(source, @"\bMyThing\s*{")) { DaleConsole.Error("Already exists"); return 1; }

// Build snippet, insert, ensure usings
SourceInserter.InsertIntoClass(target.FilePath, target.ClassName, snippet);
SourceInserter.EnsureUsing(target.FilePath, "Vion.Dale.Sdk.Core");
```

Register under the `addCommand` group in `Program.cs`.

### Pattern 3: Cloud command (like `dale upload`)

```csharp
CommandContext ctx;
try
{
    ctx = await CommandContext.ResolveAsync(environment, integratorId, clientId, clientSecret);
}
catch (DaleAuthException ex)
{
    DaleConsole.Error(ex.Message);
    return 1;
}

var response = await DaleHttpClient.GetAsync(url, ctx.AccessToken);
```

`CommandContext.ResolveAsync` handles the full auth chain: token acquisition, integrator resolution (auto-selects via `/me` if only one membership), URL resolution from environment name.

## Key Design Decisions

**No SDK dependency.** The CLI operates on files and processes. Introspection shells out to `Vion.Dale.LogicBlockParser` (discovered from NuGet cache at `~/.nuget/packages/vion.dale.sdk/{version}/tools/`), following the `dotnet ef` pattern. Falls back to local project reference when running from the dale repo.

**Source manipulation is regex-based, not Roslyn.** `SourceInserter` uses brace-counting and regex to find insertion points. This works for typical LogicBlock files but will break on braces inside string literals or `#if` blocks. Acceptable for Phase 1.

**Template is bundled in the CLI package.** `dale new` uses `AppContext.BaseDirectory/Templates/vion-iot-library/` instead of requiring a separate NuGet template install. Template SDK references are updated via `set-version.ps1 -Scope references` (always one version behind SDK, which is fine).

**IsPackable hack in template.** Template source has `<IsPackable Condition="true">false</IsPackable>` so it doesn't produce nupkgs during solution-level `dotnet pack`. The `template.json` replaces this with `<IsPackable>true</IsPackable>` on instantiation. The CLI passes `-p:IsPackable=true` when packing to override this for example projects.

**JSON mode is dual-purpose.** `-o json` suppresses all human output. Errors also emit structured JSON on stdout. This makes the CLI agent-friendly.

**Auth resolution chain:** `--client-id`/`--client-secret` flags (CI) → `DALE_CLIENT_ID`/`DALE_CLIENT_SECRET` env vars → stored credentials from `dale login`. Integrator: `--integrator-id` flag → `DALE_INTEGRATOR_ID` env var → stored config → auto-resolve via `/me` (auto-selects if one membership).

**Environment configuration.** `dale config set-environment production|test|<custom>` sets the Cloud API and Keycloak URLs. Custom environments need `dale config set-api-url` and `dale config set-auth-url`. Keycloak client is `dale-cli` (public, PKCE).

## Known Limitations

- **Brace-counting in SourceInserter** counts braces in strings/comments. Rare in practice.
- **`RemoveExamples` in NewCommand is fragile** — pattern-matches on `HelloWorld`, `SmartLedController`. If template examples change, this code must be updated manually.
- **`DevCommand` finds DevHost by convention** — looks for `*.DevHost.csproj` in siblings/subdirectories.
- **No `--dry-run`** for code generation commands.
- **Static `DaleConsole.JsonMode`** makes parallel test execution impossible for output-dependent tests.
- **Upload endpoint is temporary** — currently posts to `POST /Integrator/{integratorId}/LogicBlockLibraryVersions` with pre-existing libraryId. Will move to PackageId-based `POST /api/integrators/{integratorId}/library-versions` with auto-create.

## Ideas for Improvement

- **Roslyn-based SourceInserter** — `Microsoft.CodeAnalysis.CSharp` SyntaxTree for reliable insertion. No SDK dependency needed.
- **`dale init`** — for existing projects adopting the CLI.
- **`dale status`** — project health: builds? tests pass? logged in? integrator set?
- **Incremental `dale list`** — cache introspection, skip `dotnet publish` if unchanged.
- **Extract handler methods** from `SetAction` lambdas for better testability.

## Local Development

```bash
# Run directly
dotnet run --project Vion.Dale.Cli -- <command>

# Install as global tool (uninstall first to force update)
dotnet pack Vion.Dale.Cli -c Release -o ./packages/cli
dotnet tool uninstall -g Vion.Dale.Cli
dotnet tool install --global --add-source ./packages/cli Vion.Dale.Cli --ignore-failed-sources
```

## Testing

```bash
dotnet test Vion.Dale.Cli.Test/Vion.Dale.Cli.Test.csproj
```

57 tests covering: ProjectDiscovery, SourceInserter, AddCommand snippet builders, TokenStore, CommandContext, ParserRunner, AddMeasuringPoint.

## Versioning

CLI version is managed alongside SDK packages via `scripts/set-version.ps1`:
- `-Scope sdk` bumps `<Version>` in Vion.Dale.Cli.csproj (alongside all SDK projects)
- `-Scope references` updates template PackageReferences to match (run after SDK packages are published to the feed)

## CI/CD

- **Publish pipeline** (`publish.yml`): builds solution, packs, pushes to Azure DevOps private feed on every push to `main`; on tags `v*` also publishes to nuget.org.
- **Examples pipeline** (`examples.yml`): installs `Vion.Dale.Cli` from the private feed, runs `dale upload --skip-duplicate` for each example using service-account credentials. Triggered on `examples/**` changes or manually.
