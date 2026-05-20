# SimHub Control Mapper Native Integration Research

Research date: 2026-05-20

Target SimHub API version inspected locally:

- `SimHub.Plugins.dll` from the local SimHub install
- Assembly version observed during reflection: `1.0.9631.22016`

## Summary

There is no public SimHub plugin API that lets this plugin register a first-class
Control Mapper source controller from arbitrary plugin-managed button state.

The public API supports:

- Registering regular SimHub plugin inputs through `PluginManager.AddInput`.
- Triggering those inputs through `PluginManager.TriggerInputPress` and
  `TriggerInputRelease`.
- Triggering Control Mapper roles through `ControlMapperInterface`.

Those paths are useful for SimHub actions and role triggering, but they do not
make a plugin-managed device appear in Control Mapper's "Add source controller"
flow.

## How Control Mapper Source Controllers Work

The inspected Control Mapper internals are centered around:

- `ControlMapperPlugin`
- `ControlMapperPluginSettings`
- `RemapperWorker`
- `ControllerDescription`
- `ControllerSourceMapping`
- `ControllerState`

`RemapperWorker` uses SharpDX DirectInput controller discovery and builds
`ControllerDescription` objects from physical or virtual game controllers. That
matches the visible SimHub behavior: source controllers are real DirectInput
devices, vJoy devices, or SimHub's own flashed bridge device.

## Fanatec / Simucube-Style Wheel Recognition

SimHub contains an internal variant mechanism:

- Public interface:
  `SimHub.Plugins.OutputPlugins.ControlRemapper.Variants.IVariantProvider`
- Internal helper:
  `VariantHelper`
- Built-in providers:
  `FanatecVariantProvider`
  `SimucubeVariantProvider`

The interface is small:

```csharp
string GetVariant(int vendorid, int productid);
```

This appears to be how SimHub distinguishes some wheels that are connected
through a base but still show as the same Windows controller. The variant is
applied to a `ControllerDescription`, rather than creating a brand-new source
controller from plugin state.

## Possible MOZA Native Direction

The closest native path would be a MOZA variant provider:

1. Detect the MOZA wheelbase DirectInput controller by vendor/product ID.
2. Return the current MOZA SDK wheel identity as the variant.
3. Let Control Mapper's existing "Recognize individual wheels" behavior split
   mappings per current wheel variant.

This would be much closer to how SimHub handles Fanatec and Simucube.

## Blocker

The registration surface for variant providers is not public.

`VariantHelper` owns a private `VariantProviders` list. There is no discovered
public method on `PluginManager`, `ControlMapperPlugin`, or `ControlMapperPluginSettings`
to register another provider.

An experimental implementation could reflect into the active
`ControlMapperPlugin`, find its private `remapperWorker`, find the private
`variantHelper`, and append a custom provider to `VariantProviders`. That is
technically possible, but it would depend on undocumented private fields and can
break after a SimHub update.

The official SimHub plugin SDK documentation also warns that the SDK is limited
to the demonstrated core API and that reusing undocumented components can lead
to broken plugins after updates.

## Recommendation

Keep the vJoy source bridge as the supported Control Mapper integration for now.

Runtime note: do not inspect vJoy devices every frame. Repeated calls through
`vJoyInterfaceWrap` status APIs, including `vJoyEnabled`, `isVJDExists`,
`GetVJDStatus`, and `GetVJDButtonNumber`, have been observed to leak native
handles in SimHub/x86 testing. The bridge should cache inspection, throttle
failed acquire attempts, and avoid status checks after a source vJoy device has
already been acquired.

Next research step:

- Prototype the private `IVariantProvider` injection behind an explicit
  "experimental native wheel variant provider" setting.
- Log whether injection succeeds and which SimHub assembly version it matched.
- Never enable it by default until it has been tested across SimHub versions.

The prototype should be scoped so a failure falls back cleanly to vJoy and does
not affect non-MOZA devices.
