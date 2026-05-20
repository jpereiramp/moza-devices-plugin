# MOZA Devices Plugin for SimHub

SimHub plugin for MOZA wheels and devices using the official MOZA SDK.

The plugin is built around one important rule: it does not open COM ports and it
does not speak MOZA serial protocols directly. All MOZA communication goes
through the MOZA SDK, so MOZA Pit House can remain the owner of the hardware
connection.

## What It Does

- Detects MOZA devices reported by the MOZA SDK and Pit House.
- Creates SimHub user device definitions for detected MOZA wheels, such as
  `MOZA SDK W17` and `MOZA SDK W18`.
- Adds a MOZA SDK page to those generated wheel devices only.
- Updates each generated wheel device status based on the currently connected
  wheel identity.
- Exposes wheel button input to SimHub Control Mapper through an optional vJoy
  source bridge.
- Supports per-wheel vJoy assignments so different wheels can have different
  SimHub Control Mapper layouts.
- Provides Wheel and Diagnostics tabs inside the plugin for SDK state, detected
  devices, HID/button data, vJoy bridge status, and copyable debug logs.

## Current Limitations

- SimHub device pages and SimHub Control Mapper source controllers are separate
  systems. The generated wheel device pages do not automatically appear as
  source controllers.
- Control Mapper input currently uses the vJoy source bridge.
- Button state is polled from the MOZA SDK. The plugin polls HID data every
  20 ms and uses SDK press counters to avoid losing short taps between samples.
- Fine-grained wheel LED control is not exposed. SimHub LED pages are disabled
  for generated MOZA SDK wheel definitions.

## Requirements

- Windows
- SimHub
- MOZA Pit House
- MOZA SDK C# x86 runtime DLLs
- .NET SDK capable of building `net48`
- vJoy, only if you want Control Mapper source-controller support
- GNU Make for the `make` commands, or use `dotnet build` directly

Third-party SimHub and MOZA SDK binaries are not committed to this repository.
See [libs/README.md](libs/README.md).

## Dependency Setup

Copy required local dependencies into `libs/`:

```powershell
.\scripts\Prepare-Dependencies.ps1 `
  -SimHubPath "C:\Program Files (x86)\SimHub" `
  -MozaSdkPath "C:\Path\To\MOZA_SDK\SDK_CSharp\x86"
```

Or with `make`:

```powershell
make deps MOZA_SDK_PATH="C:/Path/To/MOZA_SDK/SDK_CSharp/x86"
```

The script copies:

- SimHub plugin API assemblies into `libs/SimHub/`
- MOZA SDK runtime assemblies into `libs/MOZA_SDK/`

## Build

```powershell
make build
```

Equivalent direct command:

```powershell
dotnet build .\MozaDevicesPlugin.sln -c Release -p:Platform=x86
```

Build output:

```text
bin\x86\Release\MozaDevicesPlugin.dll
bin\x86\Release\MOZA_API_CSharp.dll
bin\x86\Release\MOZA_API_C.dll
bin\x86\Release\MOZA_SDK.dll
```

## Deploy to SimHub

Close SimHub before deploying, because Windows locks loaded plugin DLLs.

```powershell
make deploy
```

To deploy to a non-default SimHub install:

```powershell
make deploy SIMHUB_PATH="D:/Apps/SimHub"
```

Restart SimHub after deployment.

## First Run

1. Start MOZA Pit House.
2. Start SimHub.
3. Open the MOZA Devices plugin settings.
4. Confirm the Wheel and Diagnostics tabs show SDK connection data.
5. Let the plugin create/update the current wheel device definition.
6. Restart SimHub so the new device definition appears in Devices.

For Control Mapper, configure vJoy and enable the plugin's vJoy source bridge.
Use one vJoy device per wheel layout when you want separate mappings for
different wheels.

## Architecture

The detailed architecture notes are in [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).
Control Mapper native-integration research is in
[docs/SIMHUB_CONTROL_MAPPER_RESEARCH.md](docs/SIMHUB_CONTROL_MAPPER_RESEARCH.md).

Important boundaries:

- MOZA hardware access must stay behind the MOZA SDK.
- Generated SimHub device pages must be scoped to MOZA SDK wheel definitions
  only.
- Non-MOZA devices must not receive MOZA tabs, status changes, or extensions.
- SimHub LED features stay disabled until the SDK supports the level of control
  needed for meaningful wheel LED mapping.

## Repository Layout

```text
Devices/      SimHub device definitions, extensions, identity, vJoy bridge
Models/       Immutable snapshots, settings models, wheel catalog
Sdk/          MOZA SDK polling service
UI/           WPF controls for plugin and generated device pages
docs/         Architecture and implementation notes
libs/         Local dependency cache, ignored except README
scripts/      Local setup helpers
```

## License

No license file is currently included. Third-party SimHub and MOZA SDK binaries
are not redistributed by this repository.
