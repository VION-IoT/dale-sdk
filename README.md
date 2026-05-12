# Vion Dale SDK

An IoT runtime SDK for actor-based logic blocks. Logic blocks are composable units of behaviour that talk to hardware (Modbus, digital/analog I/O, HTTP) and to each other, hosted by the Vion Dale runtime.

Full documentation: **https://docs.vion.swiss**

## Quick start

```bash
dotnet tool install -g Vion.Dale.Cli

dale new MyLibrary
cd MyLibrary
dale dev            # local DevHost with a web UI on http://localhost:5000
```

## Building from source

```bash
dotnet build Vion.Dale.Sdk.sln -c Release
```

For guaranteed reproducibility, build from a release tag (`git checkout vX.Y.Z`). The `main` branch may briefly pin pre-release versions of internal dependencies that aren't yet on nuget.org — those windows close on the next release.

## Packages

Shipped from this repository on every release:

| Package | Purpose |
|---|---|
| `Vion.Dale.Cli` | `dale` — the .NET global tool developers use day-to-day |
| `Vion.Dale.Sdk` | Core SDK — `LogicBlockBase`, attributes, introspection, source generator |
| `Vion.Dale.Sdk.Http` | HTTP client extensions |
| `Vion.Dale.Sdk.DigitalIo` / `Vion.Dale.Sdk.AnalogIo` | Digital and analog I/O contracts |
| `Vion.Dale.Sdk.Modbus.Core` / `.Tcp` / `.Rtu` | Modbus bindings |
| `Vion.Dale.Sdk.TestKit` (+ I/O-specific TestKits) | Test helpers for logic blocks |
| `Vion.Dale.ProtoActor` / `Vion.Dale.Plugin` | Actor system + plugin load context |
| `Vion.Dale.DevHost` / `Vion.Dale.DevHost.Web` | Local development host + dashboard |

## Source-available

This repository is source-available under [Apache 2.0](LICENSE). Issues and pull requests are not accepted from outside the `vion-iot` organization. See [CONTRIBUTING.md](CONTRIBUTING.md), [SUPPORT.md](SUPPORT.md), and [SECURITY.md](SECURITY.md).

Maintainers: the release process lives in [docs/releasing.md](docs/releasing.md).
