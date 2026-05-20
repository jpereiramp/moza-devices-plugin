using System;
using System.Collections.Generic;
using SimHub.Plugins.Devices;
using SimHub.Plugins.Devices.DeviceExtensions;

namespace MozaDevicesPlugin.Devices
{
    public sealed class MozaSdkDeviceExtensionFilter : IDeviceExtensionFilter
    {
        public IEnumerable<Type> GetExtensionsTypes(DeviceInstance device)
        {
            if (device?.DeviceDescriptor == null)
                yield break;

            if (MozaSdkDeviceIdentity.IsMozaSdkWheel(device.DeviceDescriptor))
                yield return typeof(MozaSdkWheelDeviceExtension);
        }
    }
}
