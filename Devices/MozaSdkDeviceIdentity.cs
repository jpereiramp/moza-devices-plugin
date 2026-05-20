using System;
using System.Security.Cryptography;
using System.Text;
using MozaDevicesPlugin.Models;
using SimHub.Plugins.Devices;

namespace MozaDevicesPlugin.Devices
{
    internal static class MozaSdkDeviceIdentity
    {
        public const string DeviceNamePrefix = "MOZA SDK ";
        private const string StableGuidSeedPrefix = "moza-sdk-wheel:";

        public static Guid StableWheelGuid(string sdkWheelNameOrPrefix)
        {
            using (var md5 = MD5.Create())
            {
                byte[] bytes = md5.ComputeHash(Encoding.UTF8.GetBytes(StableGuidSeedPrefix + (sdkWheelNameOrPrefix ?? "")));
                bytes[6] = (byte)((bytes[6] & 0x0F) | 0x30);
                bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
                return new Guid(bytes);
            }
        }

        public static bool IsMozaSdkWheel(DeviceDescriptor descriptor)
        {
            if (descriptor == null)
                return false;

            if (TryResolveKnownWheelPrefix(descriptor.DeviceTypeID ?? "", out _))
                return true;

            return HasMozaSdkBrandAndName(descriptor);
        }

        public static string ResolveExpectedPrefix(DeviceDescriptor descriptor)
        {
            if (descriptor == null)
                return "";

            if (TryResolveKnownWheelPrefix(descriptor.DeviceTypeID ?? "", out string prefix))
                return prefix;

            if (!HasMozaSdkBrandAndName(descriptor))
                return "";

            string name = descriptor.Name ?? "";
            if (name.StartsWith(DeviceNamePrefix, StringComparison.OrdinalIgnoreCase))
                name = name.Substring(DeviceNamePrefix.Length);
            else if (name.StartsWith("SDK ", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(4);

            int markerStart = name.LastIndexOf('(');
            int markerEnd = name.LastIndexOf(')');
            if (markerStart >= 0 && markerEnd > markerStart)
                name = name.Substring(markerStart + 1, markerEnd - markerStart - 1);

            return WheelModelCatalog.ExtractPrefix(name);
        }

        public static string BuildDeviceDirectoryName(string sdkWheelName)
        {
            return DeviceNamePrefix + WheelModelCatalog.ExtractPrefix(sdkWheelName);
        }

        private static bool MatchesDeviceTypeId(string deviceTypeId, string guid)
        {
            return deviceTypeId.Equals(guid, StringComparison.OrdinalIgnoreCase)
                || deviceTypeId.StartsWith(guid + "_", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryResolveKnownWheelPrefix(string deviceTypeId, out string prefix)
        {
            foreach (WheelModelMetadata model in WheelModelCatalog.Known)
            {
                if (MatchesDeviceTypeId(deviceTypeId, StableWheelGuid(model.Prefix).ToString()))
                {
                    prefix = model.Prefix;
                    return true;
                }
            }

            prefix = "";
            return false;
        }

        private static bool HasMozaSdkBrandAndName(DeviceDescriptor descriptor)
        {
            if (!string.Equals(descriptor.Brand, "MOZA", StringComparison.OrdinalIgnoreCase))
                return false;

            string name = descriptor.Name ?? "";
            return name.StartsWith("SDK ", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith(DeviceNamePrefix, StringComparison.OrdinalIgnoreCase);
        }
    }
}
