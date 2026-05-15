# Dale CLI

CLI for developing and publishing Dale LogicBlock libraries.

## Install

```bash
dotnet tool install -g Vion.Dale.Cli
```

## Quick Start

```bash
dale new MyLibrary          # scaffold a new project
cd MyLibrary
dale build                  # build
dale test                   # run tests
dale dev                    # start DevHost with web UI
dale list                   # show logic blocks, properties, interfaces
dale add logicblock Sensor  # add a new LogicBlock
dale pack                   # create .nupkg
dale login                  # authenticate with Vion Cloud
dale upload                 # publish to cloud
```

## Documentation

See [AGENTS.md](https://github.com/VION-IoT/dale) in your scaffolded project for SDK conventions and patterns.
