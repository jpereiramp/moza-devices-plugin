using System;
using System.Collections.Generic;

namespace MozaDevicesPlugin.Models
{
    internal sealed class WheelModelMetadata
    {
        public WheelModelMetadata(
            string prefix,
            string friendlyName,
            int rpmLedCount,
            int buttonLedCount,
            int knobCount,
            bool hasDisplay)
        {
            Prefix = prefix ?? "";
            FriendlyName = friendlyName ?? "";
            RpmLedCount = rpmLedCount;
            ButtonLedCount = buttonLedCount;
            KnobCount = knobCount;
            HasDisplay = hasDisplay;
        }

        public string Prefix { get; }

        public string FriendlyName { get; }

        public int RpmLedCount { get; }

        public int ButtonLedCount { get; }

        public int KnobCount { get; }

        public bool HasDisplay { get; }
    }

    internal static class WheelModelCatalog
    {
        private static readonly WheelModelMetadata Default = new WheelModelMetadata(
            prefix: "",
            friendlyName: "",
            rpmLedCount: 10,
            buttonLedCount: 14,
            knobCount: 0,
            hasDisplay: false);

        private static readonly WheelModelMetadata[] KnownModels =
        {
            new WheelModelMetadata("GS V2P", "GS V2 Pro", 10, 10, 0, false),
            new WheelModelMetadata("CS V2.1", "CS V2", 10, 6, 0, false),
            new WheelModelMetadata("W17", "CS Pro", 16, 8, 4, true),
            new WheelModelMetadata("W18", "KS Pro", 18, 14, 5, true),
            new WheelModelMetadata("KS", "KS", 10, 10, 0, false),
            new WheelModelMetadata("W13", "FSR V2", 16, 10, 0, true),
            new WheelModelMetadata("VGS", "Vision GS", 10, 8, 0, true),
            new WheelModelMetadata("TSW", "TSW", 10, 14, 0, false),
            new WheelModelMetadata("RS V2", "RS V2", 10, 14, 0, false)
        };

        public static WheelModelMetadata FromSdkWheelName(string sdkWheelName)
        {
            string prefix = ExtractPrefix(sdkWheelName);
            foreach (WheelModelMetadata model in KnownModels)
                if (model.Prefix.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                    return model;

            return new WheelModelMetadata(
                prefix,
                string.IsNullOrWhiteSpace(prefix) ? Default.FriendlyName : prefix,
                Default.RpmLedCount,
                Default.ButtonLedCount,
                Default.KnobCount,
                Default.HasDisplay);
        }

        public static string ExtractPrefix(string sdkWheelName)
        {
            string value = Normalize(sdkWheelName);
            if (value.Length == 0)
                return "";

            foreach (WheelModelMetadata model in KnownModels)
                if (value.StartsWith(model.Prefix, StringComparison.OrdinalIgnoreCase))
                    return model.Prefix;

            return value;
        }

        public static bool Matches(string expectedPrefix, string sdkWheelName)
        {
            if (string.IsNullOrWhiteSpace(expectedPrefix) || string.IsNullOrWhiteSpace(sdkWheelName))
                return false;

            return Normalize(sdkWheelName).StartsWith(Normalize(expectedPrefix), StringComparison.OrdinalIgnoreCase);
        }

        public static string FriendlyNameForPrefix(string prefix)
        {
            foreach (WheelModelMetadata model in KnownModels)
                if (model.Prefix.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                    return model.FriendlyName;

            return Normalize(prefix);
        }

        public static string DisplayNameForSdkWheelName(string sdkWheelName)
        {
            WheelModelMetadata model = FromSdkWheelName(sdkWheelName);
            if (string.IsNullOrWhiteSpace(model.Prefix))
                return Normalize(sdkWheelName);

            if (model.FriendlyName.Equals(model.Prefix, StringComparison.OrdinalIgnoreCase))
                return model.FriendlyName;

            return $"{model.FriendlyName} ({model.Prefix})";
        }

        public static IEnumerable<WheelModelMetadata> Known => KnownModels;

        private static string Normalize(string value)
        {
            return string.Join(" ", (value ?? "")
                .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
        }
    }
}
