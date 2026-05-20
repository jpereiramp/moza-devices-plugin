# Repository Guidelines

## Project Structure & Module Organization

This is a C# `net48` SimHub plugin for MOZA devices. The entry point is
`MozaDevicesPlugin.cs`. Core code is grouped by responsibility:

- `Devices/`: SimHub device definitions, extension filters, deployment,
  identity checks, and vJoy bridging.
- `Sdk/`: MOZA SDK polling and integration service.
- `Models/`: settings, immutable snapshots, diagnostics, and wheel catalogs.
- `UI/`: WPF XAML controls and code-behind for plugin and device pages.
- `docs/`: architecture notes and Control Mapper research.
- `scripts/`: local setup helpers.
- `libs/`: local SimHub and MOZA SDK binaries; do not commit them.

Build outputs under `bin/` and `obj/` should stay untracked.

## Build, Test, and Development Commands

- `make deps MOZA_SDK_PATH="C:/Path/To/MOZA_SDK/SDK_CSharp/x86"` copies local dependencies into `libs/`.
- `make check-deps` verifies required local DLLs exist before building.
- `make build` builds `MozaDevicesPlugin.sln` in `Release` for `x86`.
- `make rebuild` performs a clean rebuild through MSBuild.
- `make clean` removes build artifacts for the selected configuration.
- `make deploy SIMHUB_PATH="C:/Program Files (x86)/SimHub"` builds and copies plugin DLLs into SimHub.

Direct build equivalent: `dotnet build .\MozaDevicesPlugin.sln -c Release -p:Platform=x86`.

## Coding Style & Naming Conventions

Follow `.editorconfig`: UTF-8, CRLF line endings, final newline, trimmed trailing
whitespace, 4-space indentation for C#, and 2-space indentation for XAML, XML,
JSON, YAML, and Markdown. Nullable reference types are enabled. Use PascalCase
for types and public members, camelCase for locals and parameters, and names
that match SimHub or MOZA SDK terms.

## Testing Guidelines

There is no automated test project yet. Validate changes with `make build` at
minimum. For runtime behavior, deploy to SimHub, restart SimHub, start MOZA Pit
House, and verify the Wheel and Diagnostics tabs, generated device definitions,
connection status, and relevant vJoy bridge behavior.

## Commit & Pull Request Guidelines

Recent commits use short, direct messages such as `Initial commit` and
`Improve UX`. Keep commits concise, imperative or descriptive, and scoped to one
logical change. Pull requests should include a short summary, manual validation
steps, linked issues when applicable, and screenshots for UI changes.

## Architecture & Dependency Rules

All MOZA hardware communication must go through the MOZA SDK. Do not add direct
COM-port, serial-protocol, or USB-probing code. Generated SimHub device behavior
must remain scoped to MOZA SDK wheel definitions only. Do not commit SimHub
assemblies, MOZA SDK binaries, extracted SDK docs, or build outputs.
