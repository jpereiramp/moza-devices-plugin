namespace MozaDevicesPlugin.Models
{
    internal sealed class MozaDeviceParentSnapshot
    {
        public static readonly MozaDeviceParentSnapshot Empty = new MozaDeviceParentSnapshot(
            wheelbase: "",
            steeringWheel: "",
            displayScreen: "",
            pedals: "",
            handbrake: "",
            gearShifter: "",
            adapter: "",
            meter: "");

        public MozaDeviceParentSnapshot(
            string wheelbase,
            string steeringWheel,
            string displayScreen,
            string pedals,
            string handbrake,
            string gearShifter,
            string adapter,
            string meter)
        {
            Wheelbase = wheelbase ?? "";
            SteeringWheel = steeringWheel ?? "";
            DisplayScreen = displayScreen ?? "";
            Pedals = pedals ?? "";
            Handbrake = handbrake ?? "";
            GearShifter = gearShifter ?? "";
            Adapter = adapter ?? "";
            Meter = meter ?? "";
        }

        public string Wheelbase { get; }

        public string SteeringWheel { get; }

        public string DisplayScreen { get; }

        public string Pedals { get; }

        public string Handbrake { get; }

        public string GearShifter { get; }

        public string Adapter { get; }

        public string Meter { get; }

        public bool AnyConnected =>
            !string.IsNullOrWhiteSpace(Wheelbase)
            || !string.IsNullOrWhiteSpace(SteeringWheel)
            || !string.IsNullOrWhiteSpace(DisplayScreen)
            || !string.IsNullOrWhiteSpace(Pedals)
            || !string.IsNullOrWhiteSpace(Handbrake)
            || !string.IsNullOrWhiteSpace(GearShifter)
            || !string.IsNullOrWhiteSpace(Adapter)
            || !string.IsNullOrWhiteSpace(Meter);
    }
}
