namespace MozaDevicesPlugin.Models
{
    public sealed class MozaPluginSettings
    {
        public bool EnableVJoySourceBridge { get; set; } = true;

        public bool UsePerWheelVJoySourceDevices { get; set; } = true;

        public bool AutoSelectVJoySourceDevice { get; set; } = true;

        public int VJoySourceDeviceId { get; set; } = 2;

        public int MaxVJoyButtons { get; set; } = 128;

        public System.Collections.Generic.List<MozaWheelVJoyAssignment> WheelVJoyAssignments { get; set; } =
            new System.Collections.Generic.List<MozaWheelVJoyAssignment>();
    }

    public sealed class MozaWheelVJoyAssignment
    {
        public string WheelName { get; set; } = "";

        public int VJoyDeviceId { get; set; }
    }
}
