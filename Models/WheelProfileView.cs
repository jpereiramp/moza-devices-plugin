namespace MozaDevicesPlugin.Models
{
    internal sealed class WheelProfileView
    {
        public WheelProfileView(
            string wheelName,
            string displayName,
            int vJoyDeviceId,
            bool isAssigned,
            bool isCurrent)
        {
            WheelName = wheelName ?? "";
            DisplayName = displayName ?? "";
            VJoyDeviceId = vJoyDeviceId;
            IsAssigned = isAssigned;
            IsCurrent = isCurrent;
        }

        public string WheelName { get; }

        public string DisplayName { get; }

        public int VJoyDeviceId { get; }

        public bool IsAssigned { get; }

        public bool IsCurrent { get; }
    }
}
