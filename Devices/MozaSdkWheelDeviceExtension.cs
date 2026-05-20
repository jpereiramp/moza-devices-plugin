using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using System.Text;
using System.Windows.Controls;
using GameReaderCommon;
using MozaDevicesPlugin.Models;
using MozaDevicesPlugin.UI;
using Newtonsoft.Json.Linq;
using SimHub.Plugins;
using SimHub.Plugins.Devices;
using SimHub.Plugins.Devices.DeviceExtensions;

namespace MozaDevicesPlugin.Devices
{
    internal sealed class MozaSdkWheelDeviceExtension : DeviceExtension
    {
        private static readonly BindingFlags DeviceInstanceMemberFlags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly MethodInfo? CurrentDeviceStateSetter =
            typeof(DeviceInstance)
                .GetProperty(nameof(DeviceInstance.CurrentDeviceState), DeviceInstanceMemberFlags)
                ?.GetSetMethod(nonPublic: true);

        private static readonly MethodInfo? PrimaryDeviceMissingSetter =
            typeof(DeviceInstance)
                .GetProperty(nameof(DeviceInstance.PrimaryDeviceMissing), DeviceInstanceMemberFlags)
                ?.GetSetMethod(nonPublic: true);

        private static readonly MethodInfo? OnPropertyChangedMethod =
            typeof(DeviceInstance).GetMethod("<>OnPropertyChanged", DeviceInstanceMemberFlags);

        private string _expectedPrefix = "";
        private string _expectedFriendlyName = "";
        private bool _lastConnected;

        public override string ExtentionTabTitle => "MOZA SDK";

        internal string ExpectedPrefix => _expectedPrefix;

        internal string ExpectedFriendlyName => _expectedFriendlyName;

        internal bool IsExpectedWheelConnected()
        {
            MozaDevicesPlugin? plugin = MozaDevicesPlugin.Instance;
            if (plugin == null)
                return false;

            return WheelModelCatalog.Matches(_expectedPrefix, plugin.Snapshot.Parents.SteeringWheel);
        }

        public override void Init(PluginManager pluginManager)
        {
            _expectedPrefix = MozaSdkDeviceIdentity.ResolveExpectedPrefix(LinkedDevice.DeviceDescriptor);
            _expectedFriendlyName = WheelModelCatalog.FriendlyNameForPrefix(_expectedPrefix);
            UpdateDeviceState();

            string delegatePrefix = LinkedDevice.DeviceDescriptor.Name + "_MozaSdk";
            pluginManager.AttachDelegate(delegatePrefix + "_ExpectedWheel", GetType(), () => ExpectedDisplayName());
            pluginManager.AttachDelegate(delegatePrefix + "_Connected", GetType(), () => IsExpectedWheelConnected());
            pluginManager.AttachDelegate(delegatePrefix + "_CurrentWheel", GetType(), () => MozaDevicesPlugin.Instance?.Snapshot.Parents.SteeringWheel ?? "");
        }

        public override void End(PluginManager pluginManager)
        {
            string delegatePrefix = LinkedDevice.DeviceDescriptor.Name + "_MozaSdk";
            pluginManager.DetachDelegate(delegatePrefix + "_ExpectedWheel", GetType());
            pluginManager.DetachDelegate(delegatePrefix + "_Connected", GetType());
            pluginManager.DetachDelegate(delegatePrefix + "_CurrentWheel", GetType());
        }

        public override void DataUpdate(PluginManager pluginManager, ref GameData data)
        {
            UpdateDeviceState();
        }

        public override void ConnectionStatusChanged(DeviceState currentDeviceState)
        {
            UpdateDeviceState();
        }

        public override void LoadDefaultSettings()
        {
        }

        public override JToken GetSettings()
        {
            var settings = new JObject
            {
                ["ExpectedPrefix"] = _expectedPrefix,
                ["ExpectedFriendlyName"] = _expectedFriendlyName
            };
            return settings;
        }

        public override void SetSettings(JToken settings, bool isDefault)
        {
        }

        public override Control CreateSettingControl()
        {
            return new MozaSdkWheelDeviceControl(this);
        }

        public override IEnumerable<DynamicButtonAction> GetDynamicButtonActions()
        {
            yield break;
        }

        internal string BuildDeviceStatusText()
        {
            MozaDevicesPlugin? plugin = MozaDevicesPlugin.Instance;
            MozaDeviceSnapshot snapshot = plugin?.Snapshot ?? MozaDeviceSnapshot.Empty;
            WheelModelMetadata expected = WheelModelCatalog.FromSdkWheelName(_expectedPrefix);
            bool connected = IsExpectedWheelConnected();

            var sb = new StringBuilder();
            sb.AppendLine($"Device page:            {ExpectedDisplayName()}");
            sb.AppendLine($"Connection:             {(connected ? "Connected" : "Not connected")}");
            sb.AppendLine($"SDK status:             {snapshot.Status}");
            sb.AppendLine($"SDK current wheel:      {Blank(snapshot.Parents.SteeringWheel)}");
            sb.AppendLine($"Wheelbase:              {Blank(snapshot.Parents.Wheelbase)}");
            sb.AppendLine($"Display screen:         {Blank(snapshot.Parents.DisplayScreen)}");
            sb.AppendLine($"Pedals:                 {Blank(snapshot.Parents.Pedals)}");
            sb.AppendLine($"Handbrake:              {Blank(snapshot.Parents.Handbrake)}");
            sb.AppendLine($"Gear shifter:           {Blank(snapshot.Parents.GearShifter)}");
            sb.AppendLine();
            sb.AppendLine("Expected wheel metadata:");
            sb.AppendLine($"Model prefix:           {Blank(_expectedPrefix)}");
            sb.AppendLine($"Friendly name:          {Blank(_expectedFriendlyName)}");
            sb.AppendLine($"Telemetry LEDs:         {expected.RpmLedCount}");
            sb.AppendLine($"Button LEDs:            {expected.ButtonLedCount}");
            sb.AppendLine($"Knobs:                  {expected.KnobCount}");
            sb.AppendLine($"Integrated display:     {expected.HasDisplay}");
            sb.AppendLine();
            sb.AppendLine("DirectInput buttons:");
            sb.AppendLine($"Available:              {snapshot.Hid.Available}");
            sb.AppendLine($"Button count:           {snapshot.Hid.ButtonCount}");
            sb.AppendLine($"Held buttons:           {FormatList(snapshot.Hid.PressedButtons)}");
            sb.AppendLine($"Recent press events:    {FormatList(snapshot.Hid.PressEventButtons)}");
            sb.AppendLine();
            sb.AppendLine("Control Mapper:");
            sb.AppendLine($"vJoy assignment:        {plugin?.CurrentWheelVJoyAssignmentLabel ?? "(plugin not loaded)"}");
            return sb.ToString();
        }

        private void UpdateDeviceState()
        {
            bool connected = IsExpectedWheelConnected();
            if (LinkedDevice != null)
            {
                DeviceState state = connected ? DeviceState.Connected : DeviceState.Scanning;
                ForceDeviceState(LinkedDevice, state, !connected);

                foreach (DeviceInstance instance in LinkedDevice.GetInstances())
                {
                    if (!ReferenceEquals(instance, LinkedDevice))
                        ForceDeviceState(instance, state, !connected);
                }
            }

            if (connected != _lastConnected)
                _lastConnected = connected;
        }

        private string ExpectedDisplayName()
        {
            if (string.IsNullOrWhiteSpace(_expectedPrefix))
                return "Unknown MOZA SDK wheel";

            if (string.IsNullOrWhiteSpace(_expectedFriendlyName)
                || _expectedFriendlyName.Equals(_expectedPrefix, StringComparison.OrdinalIgnoreCase))
                return _expectedPrefix;

            return $"{_expectedFriendlyName} ({_expectedPrefix})";
        }

        private static string Blank(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "(blank)" : value;
        }

        private static string FormatList(IReadOnlyList<int> values)
        {
            return values == null || values.Count == 0
                ? "None"
                : string.Join(", ", values);
        }

        private static void ForceDeviceState(DeviceInstance device, DeviceState state, bool primaryDeviceMissing)
        {
            try
            {
                CurrentDeviceStateSetter?.Invoke(device, new object[] { state });
                PrimaryDeviceMissingSetter?.Invoke(device, new object[] { primaryDeviceMissing });
                NotifyDevicePropertyChanged(device, nameof(DeviceInstance.CurrentDeviceState));
                NotifyDevicePropertyChanged(device, nameof(DeviceInstance.IsConnected));
                NotifyDevicePropertyChanged(device, nameof(DeviceInstance.PrimaryDeviceMissing));
            }
            catch
            {
            }
        }

        private static void NotifyDevicePropertyChanged(DeviceInstance device, string propertyName)
        {
            try
            {
                OnPropertyChangedMethod?.Invoke(device, new object[] { new PropertyChangedEventArgs(propertyName) });
            }
            catch
            {
            }
        }
    }
}
