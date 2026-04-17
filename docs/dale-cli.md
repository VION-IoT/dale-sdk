# Dale CLI

Command-line tool for developing, building, and publishing Dale LogicBlock libraries to Vion Cloud.

## Installation

```bash
# From nuget.org (once published):
dotnet tool install -g Vion.Dale.Cli

# From the private feed (internal CI builds, pre-release validation):
dotnet tool install -g Vion.Dale.Cli \
  --add-source https://pkgs.dev.azure.com/ecocoachsmarthome/Ecocoach/_packaging/ecocoach.csharplogicsystem/nuget/v3/index.json
```

Verify the installation:

```bash
dale --version
```

## Quick Start

```bash
# 1. Create a new project from template
dale new MyLibrary
cd MyLibrary

# 2. Add a logic block
dale add logicblock TemperatureController

# 3. Add a service property to it
dale add serviceproperty CurrentTemperature --type double --persistent

# 4. Add a periodic timer
dale add timer PollSensor --interval 5

# 5. Build the project
dale build

# 6. Run tests
dale test

# 7. Inspect the project (logic blocks, contracts, properties)
dale list

# 8. Authenticate with Vion Cloud
dale auth login

# 9. Upload to Vion Cloud
dale upload
```

## Command Reference

### Development

#### `dale build`

Build the project. Delegates to `dotnet build`, preferring a `.sln` file if one exists in the current directory.

```bash
dale build
dale build --project path/to/MyLib.csproj
```

Extra arguments are forwarded to `dotnet build`:

```bash
dale build -- -c Release
```

#### `dale test`

Run tests. Delegates to `dotnet test`, preferring a `.sln` file if one exists.

```bash
dale test
dale test --project path/to/MyLib.Test.csproj
```

Extra arguments are forwarded to `dotnet test`:

```bash
dale test -- --filter "FullyQualifiedName~SomeTest"
```

#### `dale list`

Build the project, run introspection, and display all logic blocks with their contracts, properties, measuring points, and interfaces.

```bash
dale list
dale list --output json
dale list --project path/to/MyLib.csproj
```

JSON output includes `packageId`, `version`, `sdkVersion`, and full logic block metadata.

#### `dale new`

Create a new LogicBlock library project from the `Vion.Library.Template` template.

```bash
dale new <name>
```

| Argument | Description |
|----------|-------------|
| `name`   | Name of the new project (required) |

This creates three sub-projects:

- `<name>/<name>.csproj` -- logic block library
- `<name>/<name>.DevHost.csproj` -- local development host
- `<name>/<name>.Test.csproj` -- tests

#### `dale pack`

Pack the project into a `.nupkg` file (Release configuration).

```bash
dale pack
dale pack --project path/to/MyLib.csproj
```

Output shows the path to the generated `.nupkg`.

---

### Code Generation

#### `dale add logicblock`

Scaffold a new `LogicBlockBase` subclass and register it in `DependencyInjection.cs`.

```bash
dale add logicblock <name>
```

| Argument | Description |
|----------|-------------|
| `name`   | Class name for the new LogicBlock (required) |

Example:

```bash
dale add logicblock ChargingController
```

Creates `ChargingController.cs` with a skeleton `LogicBlockBase` subclass and auto-registers it in `DependencyInjection.cs` if that file exists.

#### `dale add serviceproperty`

Add a `[ServiceProperty]` to an existing LogicBlock class.

```bash
dale add serviceproperty <name> --type <type> [options]
```

| Argument / Option   | Description                                              |
|----------------------|----------------------------------------------------------|
| `name`               | Property name (required)                                 |
| `--type`, `-t`       | C# type (`double`, `string`, `bool`, etc.) (required)    |
| `--to`               | Target LogicBlock class name (auto-detected if only one) |
| `--setter`           | Setter visibility: `public` or `private` (default: `private`) |
| `--default-name`     | `DefaultName` parameter for `[ServiceProperty]`          |
| `--persistent`       | Add `[Persistent]` attribute                             |

Example:

```bash
dale add serviceproperty BatteryLevel --type double --persistent --default-name "Battery Level"
dale add serviceproperty Mode --type string --setter public --to ChargingController
```

#### `dale add timer`

Add a `[Timer]` method to an existing LogicBlock class.

```bash
dale add timer <name> --interval <seconds> [options]
```

| Argument / Option   | Description                                              |
|----------------------|----------------------------------------------------------|
| `name`               | Timer method name (required)                             |
| `--interval`, `-i`   | Interval in seconds (required, must be > 0)              |
| `--to`               | Target LogicBlock class name (auto-detected if only one) |

Example:

```bash
dale add timer PollSensor --interval 5
dale add timer Heartbeat --interval 60 --to ChargingController
```

---

### Authentication

Credentials are stored in `~/.dale/credentials.json`. Config is stored in `~/.dale/config.json`.

#### `dale auth login`

Authenticate via browser-based OAuth flow.

```bash
dale auth login
dale auth login --environment staging
```

| Option               | Description                                              |
|----------------------|----------------------------------------------------------|
| `--environment`, `-e` | Target environment (default: from config, or `production`) |

After authentication, the CLI fetches your user info and integrator memberships. If you belong to multiple integrators, you will be prompted to select one.

#### `dale auth whoami`

Show the current user, environment, integrator, and token status.

```bash
dale auth whoami
```

#### `dale auth logout`

Clear stored credentials.

```bash
dale auth logout
```

---

### Configuration

#### `dale config show`

Display the current configuration: environment, auth/API URLs, integrator, and login status.

```bash
dale config show
```

#### `dale config set-integrator`

Interactively select the active integrator from your memberships.

```bash
dale config set-integrator
```

Requires an active login. Fetches integrators from the Vion Cloud API and presents a selection prompt.

#### `dale config set-environment`

Switch to a named or custom environment.

```bash
dale config set-environment <name> [options]
```

| Argument / Option | Description                                                    |
|-------------------|----------------------------------------------------------------|
| `name`            | Environment name: `test`, `staging`, `production`, or custom   |
| `--auth-url`      | Custom auth base URL (required for custom environments)        |
| `--api-url`       | Custom API base URL (required for custom environments)         |
| `--force`, `-f`   | Skip confirmation prompt when an integrator is already set     |

Switching environments clears the active integrator. Run `dale auth login` or `dale config set-integrator` afterward.

Named environment example:

```bash
dale config set-environment staging
```

Custom (self-hosted) environment example:

```bash
dale config set-environment myenv \
  --auth-url https://auth.example.com/realms/vion \
  --api-url https://api.example.com
```

---

### Publishing

#### `dale upload`

Pack the project and upload the `.nupkg` to Vion Cloud.

```bash
dale upload [options]
```

| Option              | Description                                               |
|---------------------|-----------------------------------------------------------|
| `--client-id`       | Keycloak client ID (for CI / non-interactive auth)        |
| `--client-secret`   | Keycloak client secret (for CI / non-interactive auth)    |
| `--release-notes`   | Release notes for this version                            |
| `--environment`, `-e` | Target environment (overrides stored config)            |
| `--integrator-id`   | Integrator ID (overrides stored config)                   |

Example (interactive):

```bash
dale upload --release-notes "Added battery monitoring"
```

Example (CI):

```bash
dale upload \
  --client-id $DALE_CLIENT_ID \
  --client-secret $DALE_CLIENT_SECRET \
  --integrator-id 00000000-0000-0000-0000-000000000000 \
  --environment production
```

---

## Environment Configuration

The CLI supports three named environments and custom self-hosted environments.

### Named Environments

| Name         | Auth URL                                    | API URL                              |
|--------------|---------------------------------------------|--------------------------------------|
| `test`       | `https://auth.test.ecocoa.ch/realms/vion`  | `https://cloudapi.test.ecocoa.ch`    |
| `staging`    | `https://auth.staging.ecocoa.ch/realms/vion` | `https://cloudapi.staging.ecocoa.ch` |
| `production` | `https://auth.ecocoa.ch/realms/vion`       | `https://cloudapi.ecocoa.ch`         |

The default environment is `production`.

### Custom Environments

For self-hosted Vion Cloud instances, configure a custom environment with explicit URLs:

```bash
dale config set-environment onprem \
  --auth-url https://auth.mycompany.com/realms/vion \
  --api-url https://api.mycompany.com
```

Then log in as usual:

```bash
dale auth login --environment onprem
```

### Configuration Storage

All configuration is stored under `~/.dale/`:

| File                | Contents                              |
|---------------------|---------------------------------------|
| `config.json`       | Environment, URLs, active integrator  |
| `credentials.json`  | Access token, refresh token, expiry   |

On Unix, `credentials.json` is set to `0600` (owner read/write only).

---

## CI/CD Usage

For non-interactive pipelines, use the **client credentials** flow instead of browser-based login.

### Authentication Priority

The CLI resolves access tokens in this order:

1. `--client-id` and `--client-secret` flags
2. `DALE_CLIENT_ID` and `DALE_CLIENT_SECRET` environment variables
3. Stored token from `dale auth login`

### Environment Variables

| Variable              | Description                                    |
|-----------------------|------------------------------------------------|
| `DALE_CLIENT_ID`      | Keycloak client ID for service account auth    |
| `DALE_CLIENT_SECRET`  | Keycloak client secret for service account auth |
| `DALE_INTEGRATOR_ID`  | Integrator ID (overrides stored config)        |

### Example Pipeline

```yaml
# GitHub Actions example
steps:
  - name: Upload to Vion Cloud
    env:
      DALE_CLIENT_ID: ${{ secrets.DALE_CLIENT_ID }}
      DALE_CLIENT_SECRET: ${{ secrets.DALE_CLIENT_SECRET }}
      DALE_INTEGRATOR_ID: ${{ vars.INTEGRATOR_ID }}
    run: |
      dale upload --environment production --release-notes "Build ${{ github.run_number }}"
```

Or pass credentials as flags:

```bash
dale upload \
  --client-id "$CLIENT_ID" \
  --client-secret "$CLIENT_SECRET" \
  --integrator-id "$INTEGRATOR_ID" \
  --environment production
```

---

## Global Options

These options are available on all commands:

| Option       | Description                            | Default  |
|--------------|----------------------------------------|----------|
| `--output`, `-o` | Output format: `table` or `json`  | `table`  |
| `--project`  | Path to `.csproj` file                 | auto-detected |
| `--verbose`  | Show detailed output                   | `false`  |
| `--version`  | Print CLI version and exit             |          |
