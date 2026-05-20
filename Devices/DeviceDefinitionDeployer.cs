using System;
using System.IO;
using System.Text;
using MozaDevicesPlugin.Models;

namespace MozaDevicesPlugin.Devices
{
    internal sealed class DeviceDefinitionDeploymentResult
    {
        public DeviceDefinitionDeploymentResult(bool written, string deviceName, string path, string message)
        {
            Written = written;
            DeviceName = deviceName ?? "";
            Path = path ?? "";
            Message = message ?? "";
        }

        public bool Written { get; }

        public string DeviceName { get; }

        public string Path { get; }

        public string Message { get; }
    }

    internal static class DeviceDefinitionDeployer
    {
        private const string MozaVid = "0x346E";
        private const string FallbackWheelbasePid = "0x0006";

        public static DeviceDefinitionDeploymentResult DeployWheel(MozaDeviceSnapshot snapshot)
        {
            string wheelName = snapshot.Parents.SteeringWheel;
            if (string.IsNullOrWhiteSpace(wheelName))
            {
                return new DeviceDefinitionDeploymentResult(false, "", "", "No SDK steering wheel identity is available.");
            }

            int maxPressedButton = snapshot.Hid.PressedButtons.Count == 0 ? 0 : Max(snapshot.Hid.PressedButtons);
            WheelModelMetadata model = WheelModelCatalog.FromSdkWheelName(wheelName);
            int buttonCount = Math.Max(Math.Max(snapshot.Hid.ButtonCount, maxPressedButton), model.ButtonLedCount);
            buttonCount = Math.Max(1, Math.Min(128, buttonCount));

            string productName = NormalizeWhitespace(wheelName);
            string deviceName = MozaSdkDeviceIdentity.BuildDeviceDirectoryName(productName);
            string safeDeviceName = SanitizePathSegment(deviceName);
            string descriptorGuid = MozaSdkDeviceIdentity.StableWheelGuid(WheelModelCatalog.ExtractPrefix(productName)).ToString();
            string displayName = WheelModelCatalog.DisplayNameForSdkWheelName(productName);

            string simHubDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string userDefinitionsDirectory = Path.Combine(simHubDirectory, "DevicesDefinitions", "User");
            string deviceDirectory = Path.Combine(userDefinitionsDirectory, safeDeviceName);
            string deviceJsonPath = Path.Combine(deviceDirectory, "device.json");
            string json = GenerateWheelDeviceJson(descriptorGuid, productName, displayName, model, buttonCount);

            if (File.Exists(deviceJsonPath))
            {
                string existing = File.ReadAllText(deviceJsonPath);
                if (string.Equals(existing, json, StringComparison.Ordinal))
                    return new DeviceDefinitionDeploymentResult(false, deviceName, deviceJsonPath, "Device definition is already current.");
            }

            Directory.CreateDirectory(deviceDirectory);
            File.WriteAllText(deviceJsonPath, json, Encoding.UTF8);

            return new DeviceDefinitionDeploymentResult(true, deviceName, deviceJsonPath, "Device definition written. Restart SimHub to load it.");
        }

        private static string GenerateWheelDeviceJson(
            string descriptorGuid,
            string sdkProductName,
            string displayName,
            WheelModelMetadata model,
            int buttonCount)
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            AppendJsonProperty(sb, "DescriptorUniqueId", descriptorGuid, 1, comma: true);
            AppendJsonProperty(sb, "SchemaVersion", "1", 1, quoteValue: false, comma: true);
            AppendJsonProperty(sb, "MinimumSimHubVersion", "9.11.8", 1, comma: true);
            sb.AppendLine("  \"DeviceDescription\": {");
            AppendJsonProperty(sb, "BrandName", "MOZA", 2, comma: true);
            AppendJsonProperty(sb, "ProductName", "SDK " + displayName, 2, comma: false);
            sb.AppendLine("  },");
            sb.AppendLine("  \"LedsFeature\": {");
            sb.AppendLine("    \"IsIndividualLedsSectionEnabled\": false,");
            sb.AppendLine("    \"PhysicalLedsMappings\": { \"Items\": [] },");
            sb.AppendLine("    \"LogicalTelemetryLeds\": {");
            sb.AppendLine("      \"LedCount\": 0,");
            sb.AppendLine("      \"Segments\": [],");
            sb.AppendLine("      \"IsEnabled\": false");
            sb.AppendLine("    },");
            sb.AppendLine("    \"LogicalButtonsSection\": {");
            sb.AppendLine("      \"IsButtonEditorEnabled\": false,");
            sb.AppendLine("      \"Items\": [");
            for (int i = 0; i < buttonCount; i++)
            {
                sb.Append("        { \"Left\": 20, \"Top\": 20, \"Width\": 40 }");
                if (i < buttonCount - 1)
                    sb.Append(",");
                sb.AppendLine();
            }
            sb.AppendLine("      ],");
            sb.AppendLine("      \"IsEnabled\": false");
            sb.AppendLine("    },");
            sb.AppendLine("    \"IsEnabled\": false");
            sb.AppendLine("  },");
            sb.AppendLine("  \"HardwareInterface\": {");
            sb.AppendLine("    \"HardwareInterface\": {");
            AppendJsonProperty(sb, "TypeName", "LedsStandardHIDProtocol", 3, comma: true);
            AppendJsonProperty(sb, "IsSerialNumberPickerEnabled", "false", 3, quoteValue: false, comma: true);
            AppendJsonProperty(sb, "HIDUsagePage", "0xFF00", 3, comma: true);
            AppendJsonProperty(sb, "HIDUsage", "0x77", 3, comma: true);
            AppendJsonProperty(sb, "HIDReportId", "0x68", 3, comma: true);
            AppendJsonProperty(sb, "HIDReportSize", "64", 3, quoteValue: false, comma: true);
            sb.AppendLine("      \"DeviceDetection\": {");
            AppendJsonProperty(sb, "Vid", MozaVid, 4, comma: true);
            AppendJsonProperty(sb, "Pid", FallbackWheelbasePid, 4, comma: false);
            sb.AppendLine("      }");
            sb.AppendLine("    }");
            sb.AppendLine("  },");
            sb.AppendLine("  \"MozaSdkMetadata\": {");
            AppendJsonProperty(sb, "SdkWheelName", sdkProductName, 2, comma: true);
            AppendJsonProperty(sb, "ModelPrefix", model.Prefix, 2, comma: true);
            AppendJsonProperty(sb, "FriendlyName", model.FriendlyName, 2, comma: true);
            AppendJsonProperty(sb, "RpmLedCount", model.RpmLedCount.ToString(), 2, quoteValue: false, comma: true);
            AppendJsonProperty(sb, "ButtonLedCount", model.ButtonLedCount.ToString(), 2, quoteValue: false, comma: true);
            AppendJsonProperty(sb, "KnobCount", model.KnobCount.ToString(), 2, quoteValue: false, comma: true);
            AppendJsonProperty(sb, "HasDisplay", model.HasDisplay ? "true" : "false", 2, quoteValue: false, comma: false);
            sb.AppendLine("  },");
            AppendJsonProperty(sb, "IsLocked", "true", 1, quoteValue: false, comma: false);
            sb.AppendLine("}");

            return sb.ToString();
        }

        private static int Max(System.Collections.Generic.IReadOnlyList<int> values)
        {
            int max = 0;
            for (int i = 0; i < values.Count; i++)
                if (values[i] > max)
                    max = values[i];

            return max;
        }

        private static void AppendJsonProperty(
            StringBuilder sb,
            string name,
            string value,
            int indent,
            bool quoteValue = true,
            bool comma = true)
        {
            sb.Append(new string(' ', indent * 2));
            sb.Append('"');
            sb.Append(EscapeJson(name));
            sb.Append("\": ");
            if (quoteValue)
            {
                sb.Append('"');
                sb.Append(EscapeJson(value));
                sb.Append('"');
            }
            else
            {
                sb.Append(value);
            }
            if (comma)
                sb.Append(',');
            sb.AppendLine();
        }

        private static string NormalizeWhitespace(string value)
        {
            return string.Join(" ", (value ?? "")
                .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
        }

        private static string SanitizePathSegment(string value)
        {
            string normalized = NormalizeWhitespace(value);
            foreach (char c in Path.GetInvalidFileNameChars())
                normalized = normalized.Replace(c, '_');

            return normalized.Length <= 80 ? normalized : normalized.Substring(0, 80);
        }

        private static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            var sb = new StringBuilder(value.Length + 8);
            foreach (char c in value)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ')
                            sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else
                            sb.Append(c);
                        break;
                }
            }

            return sb.ToString();
        }
    }
}
