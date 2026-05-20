# Architecture

This plugin exists to make MOZA devices connected behind a MOZA wheelbase more
useful in SimHub without competing with MOZA Pit House for direct hardware
ownership.

## Core Rule

Do not use serial protocols, COM ports, or USB probing to communicate with MOZA
devices.

All device identity, configuration, and HID/button data must come from the MOZA
SDK. This avoids the conflict where Pit House locks the MOZA USB serial port and
prevents another process from opening it.

## Main Components

### `MozaDevicesPlugin`

The SimHub plugin entry point. It owns plugin settings, starts the SDK service,
registers SimHub properties/actions, exposes the Wheel and Diagnostics settings
tabs, deploys generated wheel definitions, and coordinates the vJoy bridge.

### `MozaSdkService`

Background polling service for the MOZA SDK.

- Loads and initializes the SDK.
- Polls device parent names, such as wheelbase, steering wheel, display,
  pedals, handbrake, and shifter.
- Polls wheel settings available through SDK getter calls.
- Polls `getHIDData` every 20 ms for button and axis data.
- Uses `LastPressState()` for held buttons and `PressNum()` counters for press
  events, so short taps can still be emitted even if they happen between slower
  SimHub updates.

### `DeviceDefinitionDeployer`

Writes generated SimHub user device definitions under:

```text
<SimHub>\DevicesDefinitions\User\MOZA SDK <prefix>\device.json
```

The generated definitions are intentionally conservative:

- Stable descriptor GUID per wheel model prefix.
- MOZA brand and SDK product name.
- Button layout metadata for SimHub's Devices page.
- LED features disabled.
- Custom `MozaSdkMetadata` section for the plugin extension.

SimHub must be restarted before newly written device definitions are loaded.

### `MozaSdkDeviceIdentity`

Central identity gate for generated devices.

The extension filter must only attach to devices that are known generated MOZA
SDK wheel definitions. This protects unrelated devices, such as non-MOZA haptic
reactors, from receiving MOZA tabs or status changes.

Matching is based on:

- Stable generated wheel GUIDs, or
- Explicit MOZA brand plus `SDK` / `MOZA SDK` generated naming.

### `MozaSdkWheelDeviceExtension`

SimHub Devices page extension for generated MOZA SDK wheels.

Responsibilities:

- Adds the `MOZA SDK` tab to generated MOZA wheel devices.
- Shows expected wheel metadata and current SDK state.
- Updates SimHub's built-in device status for the generated wheel based on the
  currently connected SDK wheel identity.

This extension must never be attached to arbitrary SimHub devices.

### `VJoySourceBridge`

Bridge from MOZA SDK HID button data to vJoy source controllers.

SimHub generated device pages and SimHub Control Mapper source controllers are
not the same thing. A generated `device.json` can make a wheel appear in
SimHub's Devices page, but that alone does not make it selectable as a Control
Mapper source controller.

The bridge writes current and recently pressed wheel buttons into a configured
vJoy device. SimHub can then add that vJoy device as a source controller.

The bridge supports per-wheel vJoy IDs, allowing wheels such as a CS Pro and KS
Pro to use different Control Mapper source controllers and layouts.

## Runtime Flow

1. SimHub loads `MozaDevicesPlugin.dll`.
2. The plugin starts `MozaSdkService`.
3. The SDK service initializes the MOZA SDK and begins polling.
4. Once a wheel identity is available, `DeviceDefinitionDeployer` writes or
   updates the generated SimHub `device.json`.
5. After a SimHub restart, SimHub loads the generated wheel definition.
6. `MozaSdkDeviceExtensionFilter` attaches `MozaSdkWheelDeviceExtension` only
   to generated MOZA SDK wheel devices.
7. The extension compares its expected wheel prefix with the current SDK wheel
   and updates the SimHub device connection status.
8. If enabled, `VJoySourceBridge` mirrors wheel button input to the assigned
   vJoy device for Control Mapper.

## Polling And Latency

The MOZA C# SDK exposes HID data through polling rather than a button callback.
The plugin polls HID data every 20 ms. Expected added latency is therefore
usually below one poll interval, plus normal SimHub and vJoy processing time.

To reduce missed short taps, the plugin does not rely only on sampled held
state. It also reads per-button press counters from the SDK and emits a short
vJoy pulse when a counter changes.

## Device Status

The plugin updates SimHub's built-in status for generated MOZA wheel devices by
forcing the base `DeviceInstance` state from the wheel extension.

The status is model-specific:

- The generated device for the currently connected wheel is `Connected`.
- Generated devices for other known MOZA wheels are left scanning/not connected.

No status override is applied to non-MOZA or non-generated devices.

## LED Policy

SimHub LED features are disabled in generated MOZA SDK wheel definitions.

The SDK can expose some wheel LED-related settings, but the plugin does not yet
have a reliable fine-grained LED output path suitable for SimHub LED mapping.
Until that exists, adding SimHub LED tabs would imply support that the plugin
does not provide.

## Public Repository Dependency Policy

Do not commit SimHub assemblies, MOZA SDK DLLs, extracted SDK documentation, or
build outputs.

Local binaries belong in `libs/` and are ignored by git. Use
`scripts/Prepare-Dependencies.ps1` to populate them.

## Related Research

Native Control Mapper source-controller research is documented in
`docs/SIMHUB_CONTROL_MAPPER_RESEARCH.md`.
