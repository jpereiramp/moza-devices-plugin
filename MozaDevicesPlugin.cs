using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Controls;
using System.Windows.Media;
using GameReaderCommon;
using MozaDevicesPlugin.Devices;
using MozaDevicesPlugin.Models;
using MozaDevicesPlugin.Sdk;
using MozaDevicesPlugin.UI;
using SimHub.Plugins;

namespace MozaDevicesPlugin
{
    [PluginDescription("MOZA device integration for SimHub through the MOZA SDK and DirectInput")]
    [PluginAuthor("joao")]
    [PluginName("MOZA Devices SDK")]
    public sealed class MozaDevicesPlugin : IPlugin, IDataPlugin, IWPFSettingsV2
    {
        internal static MozaDevicesPlugin? Instance { get; private set; }

        private readonly DiagnosticsLog _log = new DiagnosticsLog();
        private readonly MozaSdkService _sdk;
        private readonly DirectInputButtonReader _directInputButtons;
        private readonly VJoySourceBridge _vJoyBridge;
        private readonly object _inputSync = new object();
        private readonly object _buttonEventSync = new object();
        private readonly object _deviceDefinitionSync = new object();
        private readonly object _controlMapperSettingsSync = new object();
        private readonly HashSet<string> _registeredInputs = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _attemptedDeviceDefinitions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, bool> _virtualInputStates = new Dictionary<string, bool>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _virtualInputPressCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<int, int> _observedButtonEventCounts = new Dictionary<int, int>();
        private readonly Queue<string> _recentButtonEvents = new Queue<string>();
        private static readonly string[] AttachedDelegateNames =
        {
            "MozaSdk.Status",
            "MozaSdk.SdkInstalled",
            "MozaSdk.AnyDeviceConnected",
            "MozaSdk.Wheelbase",
            "MozaSdk.SteeringWheel",
            "MozaSdk.DisplayScreen",
            "MozaSdk.Pedals",
            "MozaSdk.Handbrake",
            "MozaSdk.GearShifter",
            "MozaSdk.WheelShiftIndicatorBrightness",
            "MozaSdk.WheelScreenBrightness",
            "MozaSdk.WheelScreenCurrentUi",
            "MozaSdk.HidAvailable",
            "MozaSdk.SteeringWheelAngle",
            "MozaSdk.Throttle",
            "MozaSdk.Brake",
            "MozaSdk.Clutch",
            "MozaSdk.HandbrakeAxis",
            "MozaSdk.Shift",
            "MozaSdk.ButtonCount",
            "MozaSdk.PressedButtons",
            "MozaSdk.PressEventButtons",
            "MozaSdk.ButtonPressCounts"
        };
        private PluginManager? _pluginManager;
        private string _activeVirtualInputDevice = "";
        private string _buttonEventWheelName = "";
        private int _activeVirtualInputButtonCount;
        private bool _virtualInputPressCountsInitialized;
        private volatile bool _deviceDefinitionDeployed;
        private string _deviceDefinitionStatus = "";
        private SimHubControlMapperSettingsStatus? _controlMapperSettingsStatus;
        private DateTime _controlMapperSettingsReadUtc = DateTime.MinValue;
        private volatile MozaDeviceSnapshot _runtimeSnapshot = MozaDeviceSnapshot.Empty;

        public MozaDevicesPlugin()
        {
            _sdk = new MozaSdkService(_log);
            _directInputButtons = new DirectInputButtonReader(_log);
            _vJoyBridge = new VJoySourceBridge(_log);
        }

        public PluginManager PluginManager
        {
            set => _pluginManager = value;
        }

        public ImageSource? PictureIcon => null;

        public string LeftMenuTitle => "MOZA SDK";

        internal MozaDeviceSnapshot Snapshot => _runtimeSnapshot;

        internal string LogText => _sdk.LogText;

        internal string HidPollingStatus => _sdk.HidPollingStatus;

        internal string DirectInputStatus => _directInputButtons.StatusText;

        internal bool DeviceDefinitionDeployed => _deviceDefinitionDeployed;

        internal string DeviceDefinitionStatus
        {
            get
            {
                lock (_deviceDefinitionSync)
                {
                    return _deviceDefinitionStatus;
                }
            }
        }

        internal MozaPluginSettings Settings { get; private set; } = new MozaPluginSettings();

        internal int CurrentWheelVJoySourceDeviceId =>
            ResolveWheelVJoyDeviceId(Snapshot, GetControlMapperSettingsStatus(forceRefresh: true), createIfMissing: false);

        internal string CurrentWheelVJoyAssignmentLabel
        {
            get
            {
                string wheelName = ResolveWheelInputDeviceName(Snapshot);
                if (string.IsNullOrWhiteSpace(wheelName))
                    return "Current wheel: (none)";

                int vJoyId = CurrentWheelVJoySourceDeviceId;
                return vJoyId > 0
                    ? $"Current wheel: {wheelName} -> vJoy {vJoyId}"
                    : $"Current wheel: {wheelName} -> unassigned";
            }
        }

        public void Init(PluginManager pluginManager)
        {
            Instance = this;
            _pluginManager = pluginManager;
            _log.Info("Initializing MOZA Devices SDK plugin");

            Settings = this.ReadCommonSettings<MozaPluginSettings>("GeneralSettings", () => new MozaPluginSettings());
            NormalizeSettings();
            RegisterProperties();
            _sdk.Start();
            _runtimeSnapshot = BuildRuntimeSnapshot(_sdk.Snapshot);
        }

        public void End(PluginManager pluginManager)
        {
            _log.Info("Stopping MOZA Devices SDK plugin");
            SaveSettings();
            _vJoyBridge.Release();
            ReleaseAllVirtualInputs(pluginManager);
            DetachProperties(pluginManager);
            _directInputButtons.Dispose();
            _sdk.Stop();
            _pluginManager = null;
            if (ReferenceEquals(Instance, this))
                Instance = null;
        }

        public void DataUpdate(PluginManager pluginManager, ref GameData data)
        {
            MozaDeviceSnapshot snapshot = RefreshRuntimeSnapshot();
            DeployWheelDeviceDefinitionIfNeeded(snapshot);
            CaptureButtonEvents(snapshot);
            UpdateVirtualWheelInputs(pluginManager, snapshot);
            UpdateVJoySourceBridge(snapshot);
        }

        public Control GetWPFSettingsControl(PluginManager pluginManager)
        {
            return new SettingsControl(this);
        }

        private MozaDeviceSnapshot RefreshRuntimeSnapshot()
        {
            MozaDeviceSnapshot snapshot = BuildRuntimeSnapshot(_sdk.Snapshot);
            _runtimeSnapshot = snapshot;
            return snapshot;
        }

        private MozaDeviceSnapshot BuildRuntimeSnapshot(MozaDeviceSnapshot sdkSnapshot)
        {
            MozaHidSnapshot directInput = _directInputButtons.Poll();
            return new MozaDeviceSnapshot(
                sdkSnapshot.SdkInstalled,
                sdkSnapshot.PollActive,
                sdkSnapshot.Status,
                sdkSnapshot.LastError,
                sdkSnapshot.PollCount,
                sdkSnapshot.ConsecutiveFailures,
                DateTime.UtcNow,
                sdkSnapshot.Parents,
                sdkSnapshot.Wheel,
                directInput,
                sdkSnapshot.Calls);
        }

        internal void RequestRefresh()
        {
            _sdk.RequestRefresh();
            _directInputButtons.RequestRefresh();
            _runtimeSnapshot = BuildRuntimeSnapshot(_sdk.Snapshot);
        }

        internal void SetVJoySourceBridgeEnabled(bool enabled)
        {
            Settings.EnableVJoySourceBridge = enabled;
            SaveSettings();
            if (!enabled)
                _vJoyBridge.Release();
        }

        internal void SetUsePerWheelVJoySourceDevices(bool enabled)
        {
            Settings.UsePerWheelVJoySourceDevices = enabled;
            SaveSettings();
            _vJoyBridge.Release();
        }

        internal void SetVJoySourceDeviceId(int deviceId)
        {
            Settings.VJoySourceDeviceId = Clamp(deviceId, 1, 16);
            SaveSettings();
            _vJoyBridge.Release();
        }

        internal void SetCurrentWheelVJoySourceDeviceId(int deviceId)
        {
            string wheelName = ResolveWheelInputDeviceName(Snapshot);
            if (string.IsNullOrWhiteSpace(wheelName))
                return;

            SetWheelVJoySourceDeviceId(wheelName, deviceId);
        }

        internal string RegisterCurrentWheelVJoyProfile(int preferredDeviceId)
        {
            string wheelName = ResolveWheelInputDeviceName(Snapshot);
            if (string.IsNullOrWhiteSpace(wheelName))
                return "No MOZA wheel is currently detected.";

            int deviceId = preferredDeviceId > 0
                ? Clamp(preferredDeviceId, 1, 16)
                : ChooseNextWheelVJoyDeviceId(wheelName, GetControlMapperSettingsStatus(forceRefresh: true));

            SetWheelVJoySourceDeviceId(wheelName, deviceId);
            return $"{FormatWheelDisplayName(wheelName)} registered on vJoy {deviceId}.";
        }

        internal void SetWheelProfileVJoySourceDeviceId(string wheelName, int deviceId)
        {
            if (string.IsNullOrWhiteSpace(wheelName))
                return;

            SetWheelVJoySourceDeviceId(wheelName, deviceId);
        }

        internal void RemoveWheelVJoyProfile(string wheelName)
        {
            wheelName = NormalizeInputDeviceName(wheelName);
            MozaWheelVJoyAssignment? existing = FindWheelAssignment(wheelName);
            if (existing == null)
                return;

            Settings.WheelVJoyAssignments.Remove(existing);
            SaveSettings();
            _vJoyBridge.Release();
            _log.Info($"Removed MOZA wheel vJoy assignment for '{wheelName}'");
        }

        internal IReadOnlyList<WheelProfileView> GetWheelProfiles()
        {
            string currentWheel = ResolveWheelInputDeviceName(Snapshot);
            var names = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (WheelModelMetadata model in WheelModelCatalog.Known)
                names[NormalizeInputDeviceName(model.Prefix)] = model.Prefix;

            foreach (MozaWheelVJoyAssignment assignment in Settings.WheelVJoyAssignments)
            {
                string normalized = NormalizeInputDeviceName(assignment.WheelName);
                if (!string.IsNullOrWhiteSpace(normalized))
                    names[normalized] = normalized;
            }

            if (!string.IsNullOrWhiteSpace(currentWheel))
                names[currentWheel] = currentWheel;

            return names.Keys
                .Select(name =>
                {
                    MozaWheelVJoyAssignment? assignment = FindWheelAssignment(name);
                    return new WheelProfileView(
                        wheelName: name,
                        displayName: FormatWheelDisplayName(name),
                        vJoyDeviceId: assignment?.VJoyDeviceId ?? 0,
                        isAssigned: assignment != null,
                        isCurrent: !string.IsNullOrWhiteSpace(currentWheel)
                            && string.Equals(NormalizeInputDeviceName(currentWheel), NormalizeInputDeviceName(name), StringComparison.OrdinalIgnoreCase));
                })
                .OrderByDescending(i => i.IsCurrent)
                .ThenByDescending(i => i.IsAssigned)
                .ThenBy(i => i.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        internal string BuildWheelProfilesText()
        {
            var profiles = GetWheelProfiles();
            if (profiles.Count == 0)
                return "(no wheel profiles)";

            var sb = new StringBuilder();
            foreach (WheelProfileView profile in profiles)
            {
                string current = profile.IsCurrent ? " current" : "";
                string assignment = profile.IsAssigned ? $"vJoy {profile.VJoyDeviceId}" : "unassigned";
                sb.AppendLine($"{profile.DisplayName,-28} {assignment}{current}");
            }

            return sb.ToString();
        }

        internal string BuildWheelProfileWarningsText()
        {
            var duplicateGroups = Settings.WheelVJoyAssignments
                .Where(i => i.VJoyDeviceId >= 1 && i.VJoyDeviceId <= 16)
                .GroupBy(i => i.VJoyDeviceId)
                .Where(group => group.Count() > 1)
                .OrderBy(group => group.Key)
                .ToArray();

            var warnings = new List<string>();
            foreach (var group in duplicateGroups)
            {
                string wheels = string.Join(", ", group.Select(i => FormatWheelDisplayName(i.WheelName)));
                warnings.Add($"vJoy {group.Key} is assigned to multiple wheels: {wheels}");
            }

            string currentWheel = ResolveWheelInputDeviceName(Snapshot);
            if (!string.IsNullOrWhiteSpace(currentWheel) && FindWheelAssignment(currentWheel) == null)
                warnings.Add($"{FormatWheelDisplayName(currentWheel)} is detected but not assigned to a vJoy source device.");

            return warnings.Count == 0 ? "" : string.Join(Environment.NewLine, warnings);
        }

        internal string BuildVirtualInputsText()
        {
            lock (_inputSync)
            {
                if (string.IsNullOrWhiteSpace(_activeVirtualInputDevice))
                    return "(no active wheel DirectInput source)";

                var sb = new StringBuilder();
                sb.AppendLine($"Active source:       {_activeVirtualInputDevice}");
                sb.AppendLine($"Button inputs:       {_activeVirtualInputButtonCount}");
                sb.AppendLine($"Registered total:    {_registeredInputs.Count}");
                sb.AppendLine("Input name pattern:");
                sb.AppendLine($"  {BuildWheelInputName(_activeVirtualInputDevice, 1)}");
                if (_activeVirtualInputButtonCount > 1)
                    sb.AppendLine($"  ... {BuildWheelInputName(_activeVirtualInputDevice, _activeVirtualInputButtonCount)}");
                return sb.ToString();
            }
        }

        internal string BuildControlMapperSourceText()
        {
            SimHubControlMapperSettingsStatus controlMapperSettings = GetControlMapperSettingsStatus(forceRefresh: true);
            var sb = new StringBuilder();

            sb.AppendLine($"vJoy source bridge enabled:   {Settings.EnableVJoySourceBridge}");
            sb.AppendLine($"Per-wheel vJoy IDs:           {Settings.UsePerWheelVJoySourceDevices}");
            sb.AppendLine($"Fallback vJoy source ID:      {Settings.VJoySourceDeviceId}");
            sb.AppendLine($"Auto-select fallback ID:      {Settings.AutoSelectVJoySourceDevice}");
            sb.AppendLine($"Max bridged buttons:          {Settings.MaxVJoyButtons}");
            sb.AppendLine($"MOZA SDK HID polling:         {HidPollingStatus}");
            sb.AppendLine($"DirectInput button source:    {DirectInputStatus}");
            sb.AppendLine($"Current wheel:                {Blank(ResolveWheelInputDeviceName(Snapshot))}");
            sb.AppendLine($"Current wheel vJoy ID:        {ResolveWheelVJoyDeviceId(Snapshot, controlMapperSettings, createIfMissing: false)}");
            sb.AppendLine($"Bridge status:                {_vJoyBridge.StatusText}");
            sb.AppendLine();
            sb.AppendLine("Per-wheel assignments:");
            sb.Append(BuildWheelVJoyAssignmentsText());
            sb.AppendLine();
            sb.AppendLine("SimHub Control Mapper settings:");
            sb.AppendLine($"Settings file:                {Blank(controlMapperSettings.Path)}");
            sb.AppendLine($"Settings available:           {controlMapperSettings.Available}");
            sb.AppendLine($"Settings error:               {Blank(controlMapperSettings.Error)}");
            sb.AppendLine($"Allow vJoy as source:         {controlMapperSettings.AllowVJoyAsSource}");
            sb.AppendLine($"Recognize individual wheels:  {controlMapperSettings.RecognizeIndividualWheels}");
            sb.AppendLine($"Output mode:                  {controlMapperSettings.OutputMode}");
            sb.AppendLine($"Output vJoy device ID:        {controlMapperSettings.OutputVJoyDeviceId}");
            return sb.ToString();
        }

        internal string BuildSetupStatusText()
        {
            MozaDeviceSnapshot snapshot = Snapshot;
            SimHubControlMapperSettingsStatus controlMapperSettings = GetControlMapperSettingsStatus(forceRefresh: true);
            VJoyEnvironmentStatus vJoy = _vJoyBridge.Inspect(GetConfiguredVJoyDeviceIds(controlMapperSettings));
            var sb = new StringBuilder();

            sb.AppendLine("First-run setup");
            sb.AppendLine("No serial/COM access is used. MOZA identity/configuration uses the MOZA SDK; live buttons use Windows DirectInput.");
            sb.AppendLine();

            string[] missingMozaDlls = GetMissingMozaRuntimeDlls();
            AppendSetupLine(
                sb,
                missingMozaDlls.Length == 0 ? "OK" : "ERROR",
                "MOZA SDK runtime DLLs",
                missingMozaDlls.Length == 0
                    ? "Found in the SimHub plugin folder."
                    : "Missing " + string.Join(", ", missingMozaDlls) + ". Run make deploy or copy the MOZA SDK x86 DLLs into the SimHub folder.");

            AppendSetupLine(
                sb,
                snapshot.SdkInstalled ? "OK" : "ERROR",
                "MOZA SDK initialization",
                snapshot.SdkInstalled ? snapshot.Status : "SDK not installed or failed to initialize. Check Pit House and the deployed SDK DLLs.");

            AppendSetupLine(
                sb,
                snapshot.Parents.AnyConnected ? "OK" : "WAIT",
                "Pit House / device connection",
                snapshot.Parents.AnyConnected
                    ? $"Wheelbase={Blank(snapshot.Parents.Wheelbase)}, wheel={Blank(snapshot.Parents.SteeringWheel)}"
                    : "Waiting for MOZA devices from the SDK. Start Pit House and connect the wheelbase.");

            AppendSetupLine(
                sb,
                string.IsNullOrWhiteSpace(snapshot.Parents.SteeringWheel) ? "WAIT" : "OK",
                "Current wheel",
                string.IsNullOrWhiteSpace(snapshot.Parents.SteeringWheel)
                    ? "No steering wheel identity reported yet."
                    : FormatWheelDisplayName(snapshot.Parents.SteeringWheel));

            string definitionStatus = DeviceDefinitionStatus;
            AppendSetupLine(
                sb,
                DeviceDefinitionDeployed ? "RESTART" : (string.IsNullOrWhiteSpace(definitionStatus) ? "WAIT" : "OK"),
                "SimHub wheel device definition",
                DeviceDefinitionDeployed
                    ? "Restart SimHub to load the generated wheel device definition."
                    : (string.IsNullOrWhiteSpace(definitionStatus) ? "Waiting for a detected wheel before creating a definition." : definitionStatus));

            AppendSetupLine(
                sb,
                Settings.EnableVJoySourceBridge ? "OK" : "WARN",
                "vJoy source bridge",
                Settings.EnableVJoySourceBridge
                    ? "Enabled."
                    : "Disabled. Enable it if you want Control Mapper source-controller support.");

            AppendSetupLine(
                sb,
                "OK",
                "MOZA SDK HID polling",
                "Permanently disabled. The plugin does not call getHIDData or getHIDData_C.");

            AppendSetupLine(
                sb,
                snapshot.Hid.Available ? "OK" : "WAIT",
                "DirectInput button source",
                snapshot.Hid.Available ? DirectInputStatus : $"{DirectInputStatus} Seen devices: {_directInputButtons.DeviceSummary.Replace(Environment.NewLine, " ")}");

            AppendSetupLine(
                sb,
                vJoy.WrapperFound ? "OK" : "ERROR",
                "vJoy wrapper",
                vJoy.WrapperFound
                    ? vJoy.WrapperPath
                    : "vJoyInterfaceWrap.dll was not found. Install vJoy/SimHub vJoy support, then restart SimHub.");

            AppendSetupLine(
                sb,
                vJoy.DriverEnabled ? "OK" : "WARN",
                "vJoy driver",
                vJoy.DriverEnabled
                    ? "Enabled."
                    : (string.IsNullOrWhiteSpace(vJoy.Error) ? "Not enabled or no vJoy devices are configured." : vJoy.Error));

            AppendSetupLine(
                sb,
                controlMapperSettings.Available && controlMapperSettings.AllowVJoyAsSource ? "OK" : "WARN",
                "SimHub Control Mapper vJoy sources",
                controlMapperSettings.Available
                    ? (controlMapperSettings.AllowVJoyAsSource
                        ? "Allow vJoy as source is enabled."
                        : "Allow vJoy as source is disabled. Enable vJoy source controllers in SimHub Control Mapper settings.")
                    : "Control Mapper settings file was not found yet.");

            string duplicateWarnings = BuildWheelProfileWarningsText();
            AppendSetupLine(
                sb,
                string.IsNullOrWhiteSpace(duplicateWarnings) ? "OK" : "WARN",
                "Wheel profile assignments",
                string.IsNullOrWhiteSpace(duplicateWarnings) ? "No duplicate vJoy assignments." : duplicateWarnings.Replace(Environment.NewLine, " "));

            sb.AppendLine();
            sb.AppendLine("Configured vJoy devices:");
            if (vJoy.Devices.Count == 0)
            {
                sb.AppendLine("  (none inspected)");
            }
            else
            {
                foreach (VJoyDeviceInspection device in vJoy.Devices)
                    sb.AppendLine($"  vJoy {device.DeviceId}: {(device.Exists ? device.Detail : "not configured")}");
            }

            return sb.ToString();
        }

        internal string BuildSupportBundle()
        {
            var sb = new StringBuilder();
            Assembly pluginAssembly = typeof(MozaDevicesPlugin).Assembly;
            Assembly simHubAssembly = typeof(PluginManager).Assembly;

            sb.AppendLine("MOZA Devices SDK support bundle");
            sb.AppendLine($"Generated local:       {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Generated UTC:         {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}Z");
            sb.AppendLine($"Plugin version:        {pluginAssembly.GetName().Version}");
            sb.AppendLine($"SimHub API version:    {simHubAssembly.GetName().Version}");
            sb.AppendLine($"Process bitness:       {(Environment.Is64BitProcess ? "64-bit" : "32-bit")}");
            sb.AppendLine($"Base directory:        {AppDomain.CurrentDomain.BaseDirectory}");
            sb.AppendLine($"No serial/COM access:  True");
            sb.AppendLine();
            sb.AppendLine("=== Setup status ===");
            sb.AppendLine(BuildSetupStatusText());
            sb.AppendLine("=== Recent button events ===");
            sb.AppendLine(BuildRecentButtonEventsText());
            sb.AppendLine("=== Diagnostics ===");
            sb.AppendLine(BuildDiagnosticsDump());
            return sb.ToString();
        }

        internal string BuildFilteredLogText(string severityFilter)
        {
            IReadOnlyList<string> entries = _log.GetEntries();
            if (string.IsNullOrWhiteSpace(severityFilter) || severityFilter.Equals("All", StringComparison.OrdinalIgnoreCase))
                return string.Join(Environment.NewLine, entries);

            string marker = "[" + severityFilter.ToUpperInvariant() + "]";
            return string.Join(Environment.NewLine, entries.Where(line => line.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0));
        }

        internal string BuildRecentButtonEventsText()
        {
            lock (_buttonEventSync)
            {
                return _recentButtonEvents.Count == 0
                    ? "(no button events captured yet)"
                    : string.Join(Environment.NewLine, _recentButtonEvents.Reverse().ToArray());
            }
        }

        internal string BuildDiagnosticsDump()
        {
            var snapshot = Snapshot;
            var sb = new StringBuilder();

            sb.AppendLine("MOZA Devices SDK diagnostics");
            sb.AppendLine($"Generated local: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Generated UTC:   {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}Z");
            sb.AppendLine("No serial/COM access is used; MOZA identity/configuration uses the MOZA SDK and live buttons use Windows DirectInput.");
            sb.AppendLine();

            sb.AppendLine("=== SDK ===");
            sb.AppendLine($"Status:               {snapshot.Status}");
            sb.AppendLine($"SDK installed:        {snapshot.SdkInstalled}");
            sb.AppendLine($"Poll active:          {snapshot.PollActive}");
            sb.AppendLine($"Poll count:           {snapshot.PollCount}");
            sb.AppendLine($"Consecutive failures: {snapshot.ConsecutiveFailures}");
            sb.AppendLine($"SDK HID polling:      {HidPollingStatus}");
            sb.AppendLine($"DirectInput buttons:  {DirectInputStatus}");
            sb.AppendLine($"Snapshot UTC:         {FormatUtc(snapshot.TimestampUtc)}");
            sb.AppendLine($"Last error:           {Blank(snapshot.LastError)}");
            sb.AppendLine();

            sb.AppendLine("=== SimHub device definition ===");
            sb.AppendLine($"Restart required:      {DeviceDefinitionDeployed}");
            sb.AppendLine($"Status:                {Blank(DeviceDefinitionStatus)}");
            sb.AppendLine();

            sb.AppendLine("=== Devices reported by MOZA SDK ===");
            AppendDevice(sb, "Wheelbase", snapshot.Parents.Wheelbase);
            AppendDevice(sb, "Steering wheel", snapshot.Parents.SteeringWheel);
            AppendDevice(sb, "Display screen", snapshot.Parents.DisplayScreen);
            AppendDevice(sb, "Pedals", snapshot.Parents.Pedals);
            AppendDevice(sb, "Handbrake", snapshot.Parents.Handbrake);
            AppendDevice(sb, "Gear shifter", snapshot.Parents.GearShifter);
            AppendDevice(sb, "Adapter", snapshot.Parents.Adapter);
            AppendDevice(sb, "Meter", snapshot.Parents.Meter);
            sb.AppendLine();

            sb.AppendLine("=== Wheel ===");
            sb.AppendLine($"Available:              {snapshot.Wheel.Available}");
            sb.AppendLine($"Wheel error:            {Blank(snapshot.Wheel.ErrorCode)}");
            sb.AppendLine($"Shift LED brightness:   {Format(snapshot.Wheel.ShiftIndicatorBrightness)}");
            sb.AppendLine($"Clutch paddle mode:     {Format(snapshot.Wheel.ClutchPaddleAxisMode)}");
            sb.AppendLine($"Clutch combine pos:     {Format(snapshot.Wheel.ClutchPaddleCombinePosition)}");
            sb.AppendLine($"Knob mode:              {Format(snapshot.Wheel.KnobMode)}");
            sb.AppendLine($"Joystick hatswitch:     {Format(snapshot.Wheel.JoystickHatswitchMode)}");
            sb.AppendLine($"Shift indicator switch: {Format(snapshot.Wheel.ShiftIndicatorSwitch)}");
            sb.AppendLine($"Shift indicator mode:   {Format(snapshot.Wheel.ShiftIndicatorMode)}");
            sb.AppendLine($"Speed unit:             {Format(snapshot.Wheel.SpeedUnit)}");
            sb.AppendLine($"Temperature unit:       {Format(snapshot.Wheel.TemperatureUnit)}");
            sb.AppendLine($"Screen brightness:      {Format(snapshot.Wheel.ScreenBrightness)}");
            sb.AppendLine($"Current screen UI:      {Format(snapshot.Wheel.ScreenCurrentUi)}");
            sb.AppendLine("Screen UI list:");
            if (snapshot.Wheel.ScreenUiList.Count == 0)
            {
                sb.AppendLine("  (none)");
            }
            else
            {
                foreach (var kv in snapshot.Wheel.ScreenUiList)
                    sb.AppendLine($"  {kv.Key}: {kv.Value}");
            }
            sb.AppendLine();

            sb.AppendLine("=== HID ===");
            sb.AppendLine($"Available:              {snapshot.Hid.Available}");
            sb.AppendLine($"Source:                 Windows DirectInput");
            sb.AppendLine($"Source status:          {DirectInputStatus}");
            sb.AppendLine($"Source detail:          {Blank(snapshot.Hid.ErrorCode)}");
            sb.AppendLine($"Steering angle:         {Format(snapshot.Hid.SteeringWheelAngle)}");
            sb.AppendLine($"Steering velocity:      {Format(snapshot.Hid.SteeringWheelVelocity)}");
            sb.AppendLine($"Steering acceleration:  {Format(snapshot.Hid.SteeringWheelAcceleration)}");
            sb.AppendLine($"Steering axle:          {Format(snapshot.Hid.SteeringWheelAxle)}");
            sb.AppendLine($"Throttle:               {Format(snapshot.Hid.Throttle)}");
            sb.AppendLine($"Brake:                  {Format(snapshot.Hid.Brake)}");
            sb.AppendLine($"Clutch:                 {Format(snapshot.Hid.Clutch)}");
            sb.AppendLine($"Handbrake:              {Format(snapshot.Hid.Handbrake)}");
            sb.AppendLine($"Shift:                  {Blank(snapshot.Hid.Shift)}");
            sb.AppendLine($"Button count:           {snapshot.Hid.ButtonCount}");
            sb.AppendLine($"Pressed buttons:        {PressedButtons(snapshot)}");
            sb.AppendLine($"Recent press events:    {PressEventButtons(snapshot)}");
            sb.AppendLine($"Press counters:         {ButtonPressCounts(snapshot)}");
            sb.AppendLine("DirectInput devices:");
            sb.AppendLine(_directInputButtons.DeviceSummary);
            sb.AppendLine();

            sb.AppendLine("=== Control Mapper virtual inputs ===");
            sb.AppendLine(BuildVirtualInputsText());
            sb.AppendLine();

            sb.AppendLine("=== Control Mapper source bridge ===");
            sb.AppendLine(BuildControlMapperSourceText());
            sb.AppendLine();

            sb.AppendLine("=== SDK calls ===");
            if (snapshot.Calls.Count == 0)
            {
                sb.AppendLine("(no calls captured)");
            }
            else
            {
                foreach (MozaSdkCallResult call in snapshot.Calls)
                {
                    sb.AppendLine(
                        $"{call.Operation,-44} success={call.Success,-5} err={call.ErrorCode,-18} " +
                        $"ms={call.ElapsedMs,4} detail={call.Detail}");
                }
            }
            sb.AppendLine();

            sb.AppendLine("=== Recent button events ===");
            sb.AppendLine(BuildRecentButtonEventsText());
            sb.AppendLine();

            sb.AppendLine("=== Logs ===");
            sb.AppendLine(LogText);

            return sb.ToString();
        }

        private void CaptureButtonEvents(MozaDeviceSnapshot snapshot)
        {
            string wheelName = ResolveWheelInputDeviceName(snapshot);
            if (string.IsNullOrWhiteSpace(wheelName) || !snapshot.Hid.Available)
                return;

            lock (_buttonEventSync)
            {
                if (!string.Equals(_buttonEventWheelName, wheelName, StringComparison.OrdinalIgnoreCase))
                {
                    _buttonEventWheelName = wheelName;
                    _observedButtonEventCounts.Clear();
                    foreach (var kv in snapshot.Hid.ButtonPressCounts)
                        _observedButtonEventCounts[kv.Key] = kv.Value;
                    return;
                }

                foreach (var kv in snapshot.Hid.ButtonPressCounts.OrderBy(i => i.Key))
                {
                    int button = kv.Key;
                    int currentCount = kv.Value;
                    if (!_observedButtonEventCounts.TryGetValue(button, out int previousCount))
                    {
                        _observedButtonEventCounts[button] = currentCount;
                        continue;
                    }

                    if (currentCount <= previousCount)
                    {
                        _observedButtonEventCounts[button] = currentCount;
                        continue;
                    }

                    int delta = currentCount - previousCount;
                    for (int i = 0; i < Math.Min(delta, 5); i++)
                        AddRecentButtonEvent($"{DateTime.Now:HH:mm:ss.fff}  {FormatWheelDisplayName(wheelName)}  Button {button:00}");

                    if (delta > 5)
                        AddRecentButtonEvent($"{DateTime.Now:HH:mm:ss.fff}  {FormatWheelDisplayName(wheelName)}  Button {button:00}  (+{delta - 5} more)");

                    _observedButtonEventCounts[button] = currentCount;
                }
            }
        }

        private void AddRecentButtonEvent(string line)
        {
            _recentButtonEvents.Enqueue(line);
            while (_recentButtonEvents.Count > 10)
                _recentButtonEvents.Dequeue();
        }

        private void RegisterProperties()
        {
            this.AttachDelegate("MozaSdk.Status", () => Snapshot.Status);
            this.AttachDelegate("MozaSdk.SdkInstalled", () => Snapshot.SdkInstalled);
            this.AttachDelegate("MozaSdk.AnyDeviceConnected", () => Snapshot.Parents.AnyConnected);
            this.AttachDelegate("MozaSdk.Wheelbase", () => Snapshot.Parents.Wheelbase);
            this.AttachDelegate("MozaSdk.SteeringWheel", () => Snapshot.Parents.SteeringWheel);
            this.AttachDelegate("MozaSdk.DisplayScreen", () => Snapshot.Parents.DisplayScreen);
            this.AttachDelegate("MozaSdk.Pedals", () => Snapshot.Parents.Pedals);
            this.AttachDelegate("MozaSdk.Handbrake", () => Snapshot.Parents.Handbrake);
            this.AttachDelegate("MozaSdk.GearShifter", () => Snapshot.Parents.GearShifter);
            this.AttachDelegate("MozaSdk.WheelShiftIndicatorBrightness", () => Snapshot.Wheel.ShiftIndicatorBrightness ?? -1);
            this.AttachDelegate("MozaSdk.WheelScreenBrightness", () => Snapshot.Wheel.ScreenBrightness ?? -1);
            this.AttachDelegate("MozaSdk.WheelScreenCurrentUi", () => Snapshot.Wheel.ScreenCurrentUi ?? -1);
            this.AttachDelegate("MozaSdk.HidAvailable", () => Snapshot.Hid.Available);
            this.AttachDelegate("MozaSdk.SteeringWheelAngle", () => Snapshot.Hid.SteeringWheelAngle ?? 0);
            this.AttachDelegate("MozaSdk.Throttle", () => Snapshot.Hid.Throttle ?? 0);
            this.AttachDelegate("MozaSdk.Brake", () => Snapshot.Hid.Brake ?? 0);
            this.AttachDelegate("MozaSdk.Clutch", () => Snapshot.Hid.Clutch ?? 0);
            this.AttachDelegate("MozaSdk.HandbrakeAxis", () => Snapshot.Hid.Handbrake ?? 0);
            this.AttachDelegate("MozaSdk.Shift", () => Snapshot.Hid.Shift);
            this.AttachDelegate("MozaSdk.ButtonCount", () => Snapshot.Hid.ButtonCount);
            this.AttachDelegate("MozaSdk.PressedButtons", () => PressedButtons(Snapshot));
            this.AttachDelegate("MozaSdk.PressEventButtons", () => PressEventButtons(Snapshot));
            this.AttachDelegate("MozaSdk.ButtonPressCounts", () => ButtonPressCounts(Snapshot));
        }

        private void DetachProperties(PluginManager pluginManager)
        {
            foreach (string name in AttachedDelegateNames)
            {
                try
                {
                    pluginManager.DetachDelegate(name, GetType());
                }
                catch
                {
                }
            }
        }

        private void SaveSettings()
        {
            NormalizeSettings();
            this.SaveCommonSettings("GeneralSettings", Settings);
        }

        private void NormalizeSettings()
        {
            Settings.VJoySourceDeviceId = Clamp(Settings.VJoySourceDeviceId, 1, 16);
            Settings.MaxVJoyButtons = Clamp(Settings.MaxVJoyButtons, 1, 128);
            if (Settings.WheelVJoyAssignments == null)
                Settings.WheelVJoyAssignments = new List<MozaWheelVJoyAssignment>();

            var normalized = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (MozaWheelVJoyAssignment assignment in Settings.WheelVJoyAssignments.ToArray())
            {
                string wheelName = NormalizeInputDeviceName(assignment.WheelName);
                if (string.IsNullOrWhiteSpace(wheelName))
                    continue;

                normalized[wheelName] = Clamp(assignment.VJoyDeviceId, 1, 16);
            }

            Settings.WheelVJoyAssignments = normalized
                .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .Select(kv => new MozaWheelVJoyAssignment { WheelName = kv.Key, VJoyDeviceId = kv.Value })
                .ToList();
        }

        private void DeployWheelDeviceDefinitionIfNeeded(MozaDeviceSnapshot snapshot)
        {
            if (string.IsNullOrWhiteSpace(snapshot.Parents.SteeringWheel))
                return;

            int buttonCount = ResolveButtonCount(snapshot);
            string key = NormalizeInputDeviceName(snapshot.Parents.SteeringWheel) + "|" + buttonCount;

            lock (_deviceDefinitionSync)
            {
                if (_attemptedDeviceDefinitions.Contains(key))
                    return;

                _attemptedDeviceDefinitions.Add(key);
            }

            try
            {
                DeviceDefinitionDeploymentResult result = DeviceDefinitionDeployer.DeployWheel(snapshot);
                lock (_deviceDefinitionSync)
                {
                    _deviceDefinitionStatus = string.IsNullOrWhiteSpace(result.DeviceName)
                        ? result.Message
                        : $"{result.DeviceName}: {result.Message}";
                    if (!string.IsNullOrWhiteSpace(result.Path))
                        _deviceDefinitionStatus += $" ({result.Path})";
                }

                if (result.Written)
                {
                    _deviceDefinitionDeployed = true;
                    _log.Info(_deviceDefinitionStatus);
                }
            }
            catch (Exception ex)
            {
                lock (_deviceDefinitionSync)
                {
                    _deviceDefinitionStatus = $"Wheel device definition deploy failed: {ex.GetType().Name}: {ex.Message}";
                }
                _log.Warn(_deviceDefinitionStatus);
            }
        }

        private void UpdateVirtualWheelInputs(PluginManager pluginManager, MozaDeviceSnapshot snapshot)
        {
            string deviceName = ResolveWheelInputDeviceName(snapshot);
            if (string.IsNullOrWhiteSpace(deviceName) || !snapshot.Hid.Available)
            {
                ReleaseAllVirtualInputs(pluginManager);
                return;
            }

            int buttonCount = ResolveButtonCount(snapshot);
            if (buttonCount <= 0)
            {
                ReleaseAllVirtualInputs(pluginManager);
                return;
            }

            lock (_inputSync)
            {
                if (!string.Equals(_activeVirtualInputDevice, deviceName, StringComparison.Ordinal))
                {
                    ReleaseAllVirtualInputsLocked(pluginManager);
                    _activeVirtualInputDevice = deviceName;
                    _activeVirtualInputButtonCount = buttonCount;
                    _virtualInputPressCounts.Clear();
                    _virtualInputPressCountsInitialized = false;
                    _log.Info($"Active SimHub plugin input source: {deviceName} ({buttonCount} buttons)");
                }

                if (buttonCount > _activeVirtualInputButtonCount)
                    _activeVirtualInputButtonCount = buttonCount;

                RegisterWheelInputsLocked(pluginManager, deviceName, buttonCount);

                var pressed = new HashSet<int>(snapshot.Hid.PressedButtons);
                for (int button = 1; button <= buttonCount; button++)
                {
                    string inputName = BuildWheelInputName(deviceName, button);
                    snapshot.Hid.ButtonPressCounts.TryGetValue(button, out int currentPressCount);
                    _virtualInputPressCounts.TryGetValue(inputName, out int previousPressCount);
                    bool hasNewPressEvent = _virtualInputPressCountsInitialized && currentPressCount > previousPressCount;
                    _virtualInputPressCounts[inputName] = currentPressCount;

                    bool nowPressed = pressed.Contains(button) || hasNewPressEvent;
                    bool wasPressed = _virtualInputStates.TryGetValue(inputName, out bool previous) && previous;

                    if (nowPressed == wasPressed)
                        continue;

                    _virtualInputStates[inputName] = nowPressed;
                    try
                    {
                        if (nowPressed)
                            pluginManager.TriggerInputPress(inputName, GetType());
                        else
                            pluginManager.TriggerInputRelease(inputName, GetType());
                    }
                    catch (Exception ex)
                    {
                        _log.Warn($"Control Mapper input trigger failed for '{inputName}': {ex.Message}");
                    }
                }

                _virtualInputPressCountsInitialized = true;
            }
        }

        private void RegisterWheelInputsLocked(PluginManager pluginManager, string deviceName, int buttonCount)
        {
            for (int button = 1; button <= buttonCount; button++)
            {
                string inputName = BuildWheelInputName(deviceName, button);
                if (_registeredInputs.Contains(inputName))
                    continue;

                try
                {
                    pluginManager.AddInput(inputName, GetType());
                    _registeredInputs.Add(inputName);
                }
                catch (Exception ex)
                {
                    _log.Warn($"Control Mapper input registration failed for '{inputName}': {ex.Message}");
                    _registeredInputs.Add(inputName);
                }
            }
        }

        private void ReleaseAllVirtualInputs(PluginManager pluginManager)
        {
            lock (_inputSync)
            {
                ReleaseAllVirtualInputsLocked(pluginManager);
                _activeVirtualInputDevice = "";
                _activeVirtualInputButtonCount = 0;
                _virtualInputPressCounts.Clear();
                _virtualInputPressCountsInitialized = false;
            }
        }

        private void ReleaseAllVirtualInputsLocked(PluginManager pluginManager)
        {
            foreach (string inputName in _virtualInputStates.Where(kv => kv.Value).Select(kv => kv.Key).ToArray())
            {
                try { pluginManager.TriggerInputRelease(inputName, GetType()); }
                catch { }
                _virtualInputStates[inputName] = false;
            }
        }

        private static string ResolveWheelInputDeviceName(MozaDeviceSnapshot snapshot)
        {
            if (!string.IsNullOrWhiteSpace(snapshot.Parents.SteeringWheel))
                return NormalizeInputDeviceName(snapshot.Parents.SteeringWheel);

            return "";
        }

        private static int ResolveButtonCount(MozaDeviceSnapshot snapshot)
        {
            int count = snapshot.Hid.ButtonCount;
            if (snapshot.Hid.PressedButtons.Count > 0)
                count = Math.Max(count, snapshot.Hid.PressedButtons.Max());
            if (snapshot.Hid.PressEventButtons.Count > 0)
                count = Math.Max(count, snapshot.Hid.PressEventButtons.Max());
            if (snapshot.Hid.ButtonPressCounts.Count > 0)
                count = Math.Max(count, snapshot.Hid.ButtonPressCounts.Keys.Max());

            return Math.Max(0, Math.Min(128, count));
        }

        private void UpdateVJoySourceBridge(MozaDeviceSnapshot snapshot)
        {
            string deviceName = ResolveWheelInputDeviceName(snapshot);
            int buttonCount = ResolveButtonCount(snapshot);
            SimHubControlMapperSettingsStatus controlMapperSettings = GetControlMapperSettingsStatus(forceRefresh: false);
            int targetVJoyDeviceId = ResolveWheelVJoyDeviceId(snapshot, controlMapperSettings, createIfMissing: true);
            _vJoyBridge.Update(snapshot, deviceName, targetVJoyDeviceId, buttonCount, Settings, controlMapperSettings);
        }

        private int ResolveWheelVJoyDeviceId(
            MozaDeviceSnapshot snapshot,
            SimHubControlMapperSettingsStatus controlMapperSettings,
            bool createIfMissing)
        {
            string wheelName = ResolveWheelInputDeviceName(snapshot);
            if (string.IsNullOrWhiteSpace(wheelName))
                return 0;

            if (!Settings.UsePerWheelVJoySourceDevices)
                return Settings.VJoySourceDeviceId;

            MozaWheelVJoyAssignment? existing = FindWheelAssignment(wheelName);
            if (existing != null)
                return Clamp(existing.VJoyDeviceId, 1, 16);

            if (!createIfMissing)
                return 0;

            int assignedId = ChooseNextWheelVJoyDeviceId(wheelName, controlMapperSettings);
            Settings.WheelVJoyAssignments.Add(new MozaWheelVJoyAssignment
            {
                WheelName = wheelName,
                VJoyDeviceId = assignedId
            });
            SaveSettings();
            _log.Info($"Assigned MOZA wheel '{wheelName}' to vJoy source device {assignedId}");
            return assignedId;
        }

        private void SetWheelVJoySourceDeviceId(string wheelName, int deviceId)
        {
            wheelName = NormalizeInputDeviceName(wheelName);
            deviceId = Clamp(deviceId, 1, 16);

            MozaWheelVJoyAssignment? existing = FindWheelAssignment(wheelName);
            if (existing == null)
            {
                Settings.WheelVJoyAssignments.Add(new MozaWheelVJoyAssignment
                {
                    WheelName = wheelName,
                    VJoyDeviceId = deviceId
                });
            }
            else
            {
                existing.VJoyDeviceId = deviceId;
            }

            SaveSettings();
            _vJoyBridge.Release();
            _log.Info($"MOZA wheel '{wheelName}' mapped to vJoy source device {deviceId}");
        }

        private MozaWheelVJoyAssignment? FindWheelAssignment(string wheelName)
        {
            wheelName = NormalizeInputDeviceName(wheelName);
            return Settings.WheelVJoyAssignments
                .FirstOrDefault(i => string.Equals(NormalizeInputDeviceName(i.WheelName), wheelName, StringComparison.OrdinalIgnoreCase));
        }

        private int ChooseNextWheelVJoyDeviceId(string wheelName, SimHubControlMapperSettingsStatus controlMapperSettings)
        {
            int excludedOutputId = ResolveReservedOutputVJoyDeviceId(controlMapperSettings);
            var usedIds = new HashSet<int>(
                Settings.WheelVJoyAssignments
                    .Where(i => !string.Equals(NormalizeInputDeviceName(i.WheelName), wheelName, StringComparison.OrdinalIgnoreCase))
                    .Select(i => Clamp(i.VJoyDeviceId, 1, 16)));

            var candidates = new List<int> { Clamp(Settings.VJoySourceDeviceId, 1, 16) };
            for (int id = 2; id <= 16; id++)
                if (!candidates.Contains(id))
                    candidates.Add(id);
            if (!candidates.Contains(1))
                candidates.Add(1);

            foreach (int candidate in candidates)
            {
                if (candidate == excludedOutputId || usedIds.Contains(candidate))
                    continue;

                return candidate;
            }

            foreach (int candidate in candidates)
            {
                if (candidate != excludedOutputId)
                    return candidate;
            }

            return Clamp(Settings.VJoySourceDeviceId, 1, 16);
        }

        private static int ResolveReservedOutputVJoyDeviceId(SimHubControlMapperSettingsStatus controlMapperSettings)
        {
            if (controlMapperSettings.IsVJoyOutputMode && controlMapperSettings.OutputVJoyDeviceId > 0)
                return Clamp(controlMapperSettings.OutputVJoyDeviceId, 1, 16);

            return controlMapperSettings.Available ? 0 : 1;
        }

        private string BuildWheelVJoyAssignmentsText()
        {
            if (Settings.WheelVJoyAssignments.Count == 0)
                return "  (none)\r\n";

            var sb = new StringBuilder();
            foreach (MozaWheelVJoyAssignment assignment in Settings.WheelVJoyAssignments.OrderBy(i => i.WheelName, StringComparer.OrdinalIgnoreCase))
                sb.AppendLine($"  {FormatWheelDisplayName(assignment.WheelName)}: vJoy {assignment.VJoyDeviceId}");

            return sb.ToString();
        }

        private IReadOnlyList<int> GetConfiguredVJoyDeviceIds(SimHubControlMapperSettingsStatus controlMapperSettings)
        {
            var ids = new HashSet<int>();
            ids.Add(Clamp(Settings.VJoySourceDeviceId, 1, 16));
            int current = CurrentWheelVJoySourceDeviceId;
            if (current > 0)
                ids.Add(current);

            foreach (MozaWheelVJoyAssignment assignment in Settings.WheelVJoyAssignments)
                ids.Add(Clamp(assignment.VJoyDeviceId, 1, 16));

            if (controlMapperSettings.IsVJoyOutputMode && controlMapperSettings.OutputVJoyDeviceId > 0)
                ids.Add(Clamp(controlMapperSettings.OutputVJoyDeviceId, 1, 16));

            return ids.OrderBy(i => i).ToArray();
        }

        private SimHubControlMapperSettingsStatus GetControlMapperSettingsStatus(bool forceRefresh)
        {
            lock (_controlMapperSettingsSync)
            {
                DateTime now = DateTime.UtcNow;
                if (!forceRefresh
                    && _controlMapperSettingsStatus != null
                    && (now - _controlMapperSettingsReadUtc).TotalSeconds < 2)
                {
                    return _controlMapperSettingsStatus;
                }

                _controlMapperSettingsStatus = SimHubControlMapperSettingsStatus.ReadFromSimHub();
                _controlMapperSettingsReadUtc = now;
                return _controlMapperSettingsStatus;
            }
        }

        private static string BuildWheelInputName(string deviceName, int button) =>
            $"MOZA SDK - Wheel - {deviceName} - Button {button:00}";

        private static string FormatWheelDisplayName(string wheelName)
        {
            string normalized = NormalizeInputDeviceName(wheelName);
            if (string.IsNullOrWhiteSpace(normalized))
                return "(unknown wheel)";

            string display = WheelModelCatalog.DisplayNameForSdkWheelName(normalized);
            if (string.IsNullOrWhiteSpace(display))
                display = normalized;

            return display.StartsWith("MOZA ", StringComparison.OrdinalIgnoreCase)
                ? display
                : "MOZA " + display;
        }

        private static string NormalizeInputDeviceName(string value)
        {
            string normalized = string.Join(" ", (value ?? "")
                .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));

            if (normalized.Length == 0)
                return "Unknown Wheel";

            return normalized.Length <= 60 ? normalized : normalized.Substring(0, 60);
        }

        private static void AppendDevice(StringBuilder sb, string label, string value)
        {
            sb.AppendLine($"{label + ":",-18} {Blank(value)}");
        }

        private static string Blank(string value) =>
            string.IsNullOrWhiteSpace(value) ? "(blank)" : value;

        private static string Format(int? value) =>
            value.HasValue ? value.Value.ToString() : "--";

        private static string Format(short? value) =>
            value.HasValue ? value.Value.ToString() : "--";

        private static string Format(float? value) =>
            value.HasValue ? value.Value.ToString("0.###") : "--";

        private static string FormatUtc(DateTime value) =>
            value == DateTime.MinValue ? "--" : $"{value:yyyy-MM-dd HH:mm:ss}Z";

        private static string PressedButtons(MozaDeviceSnapshot snapshot)
        {
            return snapshot.Hid.PressedButtons.Count == 0
                ? "None"
                : string.Join(", ", snapshot.Hid.PressedButtons);
        }

        private static string PressEventButtons(MozaDeviceSnapshot snapshot)
        {
            return snapshot.Hid.PressEventButtons.Count == 0
                ? "None"
                : string.Join(", ", snapshot.Hid.PressEventButtons);
        }

        private static string ButtonPressCounts(MozaDeviceSnapshot snapshot)
        {
            if (snapshot.Hid.ButtonPressCounts.Count == 0)
                return "None";

            return string.Join(", ", snapshot.Hid.ButtonPressCounts.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}"));
        }

        private static bool AreRequiredMozaRuntimeDllsPresent()
        {
            return GetMissingMozaRuntimeDlls().Length == 0;
        }

        private static string[] GetMissingMozaRuntimeDlls()
        {
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            return new[] { "MOZA_API_CSharp.dll", "MOZA_API_C.dll", "MOZA_SDK.dll" }
                .Where(file => !File.Exists(Path.Combine(baseDirectory, file)))
                .ToArray();
        }

        private static void AppendSetupLine(StringBuilder sb, string status, string label, string detail)
        {
            sb.AppendLine($"[{status,-7}] {label,-36} {detail}");
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min)
                return min;

            return value > max ? max : value;
        }
    }
}
