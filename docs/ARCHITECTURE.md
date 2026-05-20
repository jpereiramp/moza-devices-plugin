# Architecture

This plugin exists to make MOZA devices connected behind a MOZA wheelbase more
useful in SimHub without competing with MOZA Pit House for direct hardware
ownership.

## Core Rule

Do not use serial protocols, COM ports, or USB probing to communicate with MOZA
devices.

Device identity and configuration come from the MOZA SDK. Live wheel button
state comes from the normal Windows DirectInput game-controller interface. The
plugin must not call MOZA SDK HID APIs such as `getHIDData` or `getHIDData_C`,
because those paths are unnecessary for runtime button input and have shown
unstable behavior during leak investigation.

## Main Components

### `MozaDevicesPlugin`

The SimHub plugin entry point. It owns plugin settings, starts the SDK service,
registers SimHub properties/actions, exposes the Wheel and Diagnostics settings
tabs, deploys generated wheel definitions, and coordinates the vJoy bridge.

### `MozaSdkService`

Background service for MOZA SDK identity/configuration reads.

- Loads and initializes the SDK.
- Reads device parent names, such as wheelbase, steering wheel, display,
  pedals, handbrake, and shifter, at startup and when the user requests a
  refresh.
- Reads wheel settings available through SDK getter calls at startup and on
  refresh.
- Does not poll SDK HID data.
- Avoids continuous SDK polling. If a wheel rim is changed while SimHub is
  running, the user should press Refresh in the plugin settings to recapture SDK
  identity/configuration.

### `DirectInputButtonReader`

Windows game-controller input reader.

- Enumerates attached DirectInput game controllers.
- Selects the MOZA wheelbase/controller device while ignoring vJoy and other
  virtual devices.
- Polls held buttons and maintains per-button press counters for downstream
  SimHub/vJoy logic.

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

Bridge from DirectInput button data to vJoy source controllers.

SimHub generated device pages and SimHub Control Mapper source controllers are
not the same thing. A generated `device.json` can make a wheel appear in
SimHub's Devices page, but that alone does not make it selectable as a Control
Mapper source controller.

The bridge writes current and recently pressed wheel buttons into a configured
vJoy device. SimHub can then add that vJoy device as a source controller.

The bridge supports per-wheel vJoy IDs, allowing wheels such as a CS Pro and KS
Pro to use different Control Mapper source controllers and layouts.

vJoy wrapper status APIs are treated as expensive/leaky. In particular,
repeated calls to `vJoyEnabled`, `isVJDExists`, `GetVJDStatus`, and
`GetVJDButtonNumber` have been observed to leak native handles when called in a
tight loop. The bridge therefore validates/acquires only when necessary,
throttles failed acquire attempts, and caches settings-page inspection results.

## Runtime Flow

1. SimHub loads `MozaDevicesPlugin.dll`.
2. The plugin starts `MozaSdkService`.
3. The SDK service initializes the MOZA SDK and captures identity/configuration once.
4. Once a wheel identity is available, `DeviceDefinitionDeployer` writes or
   updates the generated SimHub `device.json`.
5. After a SimHub restart, SimHub loads the generated wheel definition.
6. `MozaSdkDeviceExtensionFilter` attaches `MozaSdkWheelDeviceExtension` only
   to generated MOZA SDK wheel devices.
7. The extension compares its expected wheel prefix with the current SDK wheel
   and updates the SimHub device connection status.
8. `DirectInputButtonReader` reads live wheel buttons from the Windows
   game-controller interface.
9. If enabled, `VJoySourceBridge` mirrors wheel button input to the assigned
   vJoy device for Control Mapper.

## Polling And Latency

Live buttons are sampled from DirectInput during SimHub data updates. Expected
added latency is the SimHub update cadence plus normal DirectInput and vJoy
processing time.

To reduce missed short taps, the plugin does not rely only on sampled held
state. It maintains per-button rising-edge counters and emits a short vJoy pulse
when a counter changes.

vJoy device status is not polled every update. Once a vJoy source device is
acquired, button writes continue without re-running status discovery unless the
source wheel or target vJoy assignment changes.

## Leak Investigation Findings

The current runtime shape is based on handle-count testing inside SimHub and
standalone x86 probes:

- Repeated vJoy status/inspection calls leaked native handles.
- DirectInput polling of the MOZA wheelbase did not leak handles.
- `installMozaSDK()` by itself did not leak handles.
- Continuous MOZA SDK HID polling remains disabled because DirectInput provides
  the needed live button data without using MOZA SDK HID APIs.

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
