using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MozaDevicesPlugin.Models;

namespace MozaDevicesPlugin.UI
{
    public partial class SettingsControl : UserControl
    {
        private readonly MozaDevicesPlugin _plugin;
        private readonly DispatcherTimer _refreshTimer;
        private string _selectedWheelProfileName = "";
        private bool _updatingControls;

        public SettingsControl(MozaDevicesPlugin plugin)
        {
            _plugin = plugin;
            InitializeComponent();

            for (int i = 1; i <= 16; i++)
            {
                VJoySourceIdComboBox.Items.Add(i);
                CurrentWheelVJoyIdComboBox.Items.Add(i);
                WheelProfileVJoyIdComboBox.Items.Add(i);
            }

            LogFilterComboBox.Items.Add("All");
            LogFilterComboBox.Items.Add("INFO");
            LogFilterComboBox.Items.Add("WARN");
            LogFilterComboBox.Items.Add("ERROR");
            LogFilterComboBox.SelectedItem = "All";

            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _refreshTimer.Tick += (_, __) => RefreshDisplay();

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            RefreshDisplay();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (!_refreshTimer.IsEnabled)
                _refreshTimer.Start();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _refreshTimer.Stop();
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            _plugin.RequestRefresh();
            RefreshDisplay();
        }

        private void CopyDiagnosticsButton_Click(object sender, RoutedEventArgs e)
        {
            TryCopy(_plugin.BuildDiagnosticsDump(), "Diagnostics copied");
        }

        private void CopyLogsButton_Click(object sender, RoutedEventArgs e)
        {
            TryCopy(_plugin.BuildFilteredLogText(GetSelectedLogFilter()), "Logs copied");
        }

        private void CopySupportBundleButton_Click(object sender, RoutedEventArgs e)
        {
            TryCopy(_plugin.BuildSupportBundle(), "Support bundle copied");
        }

        private void EnableVJoySourceBridgeCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_updatingControls)
                return;

            _plugin.SetVJoySourceBridgeEnabled(EnableVJoySourceBridgeCheckBox.IsChecked == true);
            RefreshDisplay();
        }

        private void UsePerWheelVJoySourceDevicesCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_updatingControls)
                return;

            _plugin.SetUsePerWheelVJoySourceDevices(UsePerWheelVJoySourceDevicesCheckBox.IsChecked == true);
            RefreshDisplay();
        }

        private void VJoySourceIdComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_updatingControls)
                return;

            if (VJoySourceIdComboBox.SelectedItem is int deviceId)
                _plugin.SetVJoySourceDeviceId(deviceId);

            RefreshDisplay();
        }

        private void CurrentWheelVJoyIdComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_updatingControls)
                return;

            if (CurrentWheelVJoyIdComboBox.SelectedItem is int deviceId)
                _plugin.SetCurrentWheelVJoySourceDeviceId(deviceId);

            RefreshDisplay();
        }

        private void RegisterCurrentWheelButton_Click(object sender, RoutedEventArgs e)
        {
            int preferred = WheelProfileVJoyIdComboBox.SelectedItem is int id ? id : 0;
            TrySetStatus(_plugin.RegisterCurrentWheelVJoyProfile(preferred));
            RefreshDisplay();
        }

        private void WheelProfileComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_updatingControls)
                return;

            if (WheelProfileComboBox.SelectedItem is WheelProfileListItem item)
                _selectedWheelProfileName = item.WheelName;

            RefreshDisplay();
        }

        private void ApplyWheelProfileButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_selectedWheelProfileName))
                return;

            if (WheelProfileVJoyIdComboBox.SelectedItem is int deviceId)
            {
                _plugin.SetWheelProfileVJoySourceDeviceId(_selectedWheelProfileName, deviceId);
                TrySetStatus("Wheel profile updated");
                RefreshDisplay();
            }
        }

        private void RemoveWheelProfileButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_selectedWheelProfileName))
                return;

            _plugin.RemoveWheelVJoyProfile(_selectedWheelProfileName);
            TrySetStatus("Wheel profile removed");
            RefreshDisplay();
        }

        private void LogFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_updatingControls)
                RefreshDisplay();
        }

        private void RefreshDisplay()
        {
            MozaDeviceSnapshot snapshot = _plugin.Snapshot;
            _updatingControls = true;
            try
            {
                RestartBanner.Visibility = _plugin.DeviceDefinitionDeployed
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                SetTextIfChanged(
                    RestartBannerText,
                    string.IsNullOrWhiteSpace(_plugin.DeviceDefinitionStatus)
                        ? "New MOZA SDK device definition was deployed. Restart SimHub to load it."
                        : "New MOZA SDK device definition was deployed. Restart SimHub to load it. " + _plugin.DeviceDefinitionStatus);

                SetTextIfChanged(ConnectionLabel, snapshot.Status);
                SetTextIfChanged(
                    SnapshotTimeLabel,
                    snapshot.TimestampUtc == DateTime.MinValue
                        ? ""
                        : $"Updated {snapshot.TimestampUtc.ToLocalTime():HH:mm:ss}");
                ConnectionIndicator.Fill = GetStatusBrush(snapshot);

                switch (MainTabs.SelectedIndex)
                {
                    case 0:
                        SetTextIfChanged(SetupStatusBox, _plugin.BuildSetupStatusText());
                        break;

                    case 1:
                        RefreshWheelProfileControls();
                        SetTextIfChanged(CurrentWheelProfileText, _plugin.CurrentWheelVJoyAssignmentLabel);
                        SetTextIfChanged(WheelProfilesBox, _plugin.BuildWheelProfilesText());
                        SetTextIfChanged(WheelProfileWarningText, _plugin.BuildWheelProfileWarningsText());
                        RefreshButtonTester(snapshot);
                        SetTextIfChanged(WheelDevicesBox, BuildDevicesText(snapshot));
                        SetTextIfChanged(WheelInfoBox, BuildWheelText(snapshot));
                        SetTextIfChanged(HidInfoBox, BuildHidText(snapshot));
                        break;

                    case 2:
                        EnableVJoySourceBridgeCheckBox.IsChecked = _plugin.Settings.EnableVJoySourceBridge;
                        UsePerWheelVJoySourceDevicesCheckBox.IsChecked = _plugin.Settings.UsePerWheelVJoySourceDevices;
                        VJoySourceIdComboBox.SelectedItem = _plugin.Settings.VJoySourceDeviceId;
                        CurrentWheelVJoyIdComboBox.SelectedItem = _plugin.CurrentWheelVJoySourceDeviceId;
                        CurrentWheelVJoyIdComboBox.IsEnabled = !string.IsNullOrWhiteSpace(snapshot.Parents.SteeringWheel);
                        SetTextIfChanged(CurrentWheelAssignmentText, _plugin.CurrentWheelVJoyAssignmentLabel);
                        SetTextIfChanged(
                            DiagnosticsBox,
                            BuildDiagnosticsText(
                                snapshot,
                                _plugin.BuildVirtualInputsText(),
                                _plugin.BuildControlMapperSourceText(),
                                _plugin.DeviceDefinitionDeployed,
                                _plugin.DeviceDefinitionStatus));
                        SetTextIfChanged(LogsBox, _plugin.BuildFilteredLogText(GetSelectedLogFilter()));
                        SetTextIfChanged(ButtonEventsBox, _plugin.BuildRecentButtonEventsText());
                        break;
                }
            }
            finally
            {
                _updatingControls = false;
            }
        }

        private void RefreshWheelProfileControls()
        {
            IReadOnlyList<WheelProfileView> profiles = _plugin.GetWheelProfiles();
            if (string.IsNullOrWhiteSpace(_selectedWheelProfileName))
                _selectedWheelProfileName = profiles.FirstOrDefault(i => i.IsCurrent)?.WheelName
                    ?? profiles.FirstOrDefault(i => i.IsAssigned)?.WheelName
                    ?? profiles.FirstOrDefault()?.WheelName
                    ?? "";

            WheelProfileComboBox.Items.Clear();
            WheelProfileListItem? selectedItem = null;
            foreach (WheelProfileView profile in profiles)
            {
                var item = new WheelProfileListItem(profile);
                WheelProfileComboBox.Items.Add(item);
                if (string.Equals(profile.WheelName, _selectedWheelProfileName, StringComparison.OrdinalIgnoreCase))
                    selectedItem = item;
            }

            if (selectedItem == null && WheelProfileComboBox.Items.Count > 0)
            {
                selectedItem = (WheelProfileListItem)WheelProfileComboBox.Items[0];
                _selectedWheelProfileName = selectedItem.WheelName;
            }

            WheelProfileComboBox.SelectedItem = selectedItem;
            WheelProfileVJoyIdComboBox.SelectedItem = selectedItem?.VJoyDeviceId > 0
                ? selectedItem.VJoyDeviceId
                : _plugin.CurrentWheelVJoySourceDeviceId > 0
                    ? _plugin.CurrentWheelVJoySourceDeviceId
                    : _plugin.Settings.VJoySourceDeviceId;
        }

        private void RefreshButtonTester(MozaDeviceSnapshot snapshot)
        {
            ButtonTesterGrid.Children.Clear();
            int buttonCount = Math.Max(
                snapshot.Hid.ButtonCount,
                snapshot.Hid.ButtonPressCounts.Count == 0 ? 0 : snapshot.Hid.ButtonPressCounts.Keys.Max());
            buttonCount = Math.Max(buttonCount, snapshot.Hid.PressedButtons.Count == 0 ? 0 : snapshot.Hid.PressedButtons.Max());
            buttonCount = Math.Max(buttonCount, snapshot.Hid.PressEventButtons.Count == 0 ? 0 : snapshot.Hid.PressEventButtons.Max());
            buttonCount = Math.Min(Math.Max(buttonCount, 16), 128);

            var held = new HashSet<int>(snapshot.Hid.PressedButtons);
            var recent = new HashSet<int>(snapshot.Hid.PressEventButtons);
            for (int button = 1; button <= buttonCount; button++)
            {
                bool isHeld = held.Contains(button);
                bool isRecent = recent.Contains(button);
                var border = new Border
                {
                    Height = 34,
                    Margin = new Thickness(0, 0, 6, 6),
                    BorderBrush = isHeld ? Brushes.ForestGreen : isRecent ? Brushes.DarkOrange : Brushes.LightGray,
                    BorderThickness = new Thickness(1),
                    Background = isHeld ? new SolidColorBrush(Color.FromRgb(220, 252, 231))
                        : isRecent ? new SolidColorBrush(Color.FromRgb(255, 247, 237))
                        : Brushes.White
                };

                border.Child = new TextBlock
                {
                    Text = button.ToString("00"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontFamily = new FontFamily("Consolas"),
                    Foreground = isHeld ? Brushes.ForestGreen : isRecent ? Brushes.DarkOrange : Brushes.DimGray
                };
                ButtonTesterGrid.Children.Add(border);
            }
        }

        private static Brush GetStatusBrush(MozaDeviceSnapshot snapshot)
        {
            if (!snapshot.SdkInstalled)
                return Brushes.Firebrick;

            if (snapshot.Status.IndexOf("not ready", StringComparison.OrdinalIgnoreCase) >= 0
                || snapshot.Status.IndexOf("failed", StringComparison.OrdinalIgnoreCase) >= 0)
                return Brushes.DarkOrange;

            return snapshot.Parents.AnyConnected ? Brushes.ForestGreen : Brushes.Gray;
        }

        private static string BuildDevicesText(MozaDeviceSnapshot snapshot)
        {
            var sb = new StringBuilder();
            AppendDevice(sb, "Wheelbase", snapshot.Parents.Wheelbase);
            AppendDevice(sb, "Steering wheel", snapshot.Parents.SteeringWheel);
            AppendDevice(sb, "Display screen", snapshot.Parents.DisplayScreen);
            AppendDevice(sb, "Pedals", snapshot.Parents.Pedals);
            AppendDevice(sb, "Handbrake", snapshot.Parents.Handbrake);
            AppendDevice(sb, "Gear shifter", snapshot.Parents.GearShifter);
            AppendDevice(sb, "Adapter", snapshot.Parents.Adapter);
            AppendDevice(sb, "Meter", snapshot.Parents.Meter);
            return sb.ToString();
        }

        private static string BuildWheelText(MozaDeviceSnapshot snapshot)
        {
            var wheel = snapshot.Wheel;
            var sb = new StringBuilder();

            sb.AppendLine($"SDK wheel parent:        {Blank(snapshot.Parents.SteeringWheel)}");
            sb.AppendLine($"Wheel read available:    {wheel.Available}");
            sb.AppendLine($"Wheel read error:        {Blank(wheel.ErrorCode)}");
            sb.AppendLine();
            sb.AppendLine($"Shift LED brightness:    {Format(wheel.ShiftIndicatorBrightness)}");
            sb.AppendLine($"Clutch paddle mode:      {Format(wheel.ClutchPaddleAxisMode)}");
            sb.AppendLine($"Clutch combine position: {Format(wheel.ClutchPaddleCombinePosition)}");
            sb.AppendLine($"Knob mode:               {Format(wheel.KnobMode)}");
            sb.AppendLine($"Joystick hatswitch mode: {Format(wheel.JoystickHatswitchMode)}");
            sb.AppendLine($"Shift indicator switch:  {Format(wheel.ShiftIndicatorSwitch)}");
            sb.AppendLine($"Shift indicator mode:    {Format(wheel.ShiftIndicatorMode)}");
            sb.AppendLine($"Speed unit:              {Format(wheel.SpeedUnit)}");
            sb.AppendLine($"Temperature unit:        {Format(wheel.TemperatureUnit)}");
            sb.AppendLine($"Screen brightness:       {Format(wheel.ScreenBrightness)}");
            sb.AppendLine($"Current screen UI:       {Format(wheel.ScreenCurrentUi)}");
            sb.AppendLine();
            sb.AppendLine("Screen UI list:");

            if (wheel.ScreenUiList.Count == 0)
            {
                sb.AppendLine("  (none)");
            }
            else
            {
                foreach (var kv in wheel.ScreenUiList)
                    sb.AppendLine($"  {kv.Key}: {kv.Value}");
            }

            return sb.ToString();
        }

        private static string BuildHidText(MozaDeviceSnapshot snapshot)
        {
            var hid = snapshot.Hid;
            var sb = new StringBuilder();

            sb.AppendLine($"Available:              {hid.Available}");
            sb.AppendLine("Source:                 Windows DirectInput");
            sb.AppendLine($"Source detail:          {Blank(hid.ErrorCode)}");
            sb.AppendLine($"Steering angle:         {Format(hid.SteeringWheelAngle)}");
            sb.AppendLine($"Steering velocity:      {Format(hid.SteeringWheelVelocity)}");
            sb.AppendLine($"Steering acceleration:  {Format(hid.SteeringWheelAcceleration)}");
            sb.AppendLine($"Steering axle:          {Format(hid.SteeringWheelAxle)}");
            sb.AppendLine($"Throttle:               {Format(hid.Throttle)}");
            sb.AppendLine($"Brake:                  {Format(hid.Brake)}");
            sb.AppendLine($"Clutch:                 {Format(hid.Clutch)}");
            sb.AppendLine($"Handbrake:              {Format(hid.Handbrake)}");
            sb.AppendLine($"Shift:                  {Blank(hid.Shift)}");
            sb.AppendLine($"Button count:           {hid.ButtonCount}");
            sb.AppendLine($"Pressed buttons:        {(hid.PressedButtons.Count == 0 ? "None" : string.Join(", ", hid.PressedButtons))}");
            sb.AppendLine($"Recent press events:    {(hid.PressEventButtons.Count == 0 ? "None" : string.Join(", ", hid.PressEventButtons))}");
            sb.AppendLine($"Press counters:         {FormatPressCounts(hid.ButtonPressCounts)}");

            return sb.ToString();
        }

        private static string BuildDiagnosticsText(
            MozaDeviceSnapshot snapshot,
            string virtualInputsText,
            string controlMapperSourceText,
            bool deviceDefinitionDeployed,
            string deviceDefinitionStatus)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Status:               {snapshot.Status}");
            sb.AppendLine($"SDK installed:        {snapshot.SdkInstalled}");
            sb.AppendLine($"Poll active:          {snapshot.PollActive}");
            sb.AppendLine($"Poll count:           {snapshot.PollCount}");
            sb.AppendLine($"Consecutive failures: {snapshot.ConsecutiveFailures}");
            sb.AppendLine($"Snapshot UTC:         {(snapshot.TimestampUtc == DateTime.MinValue ? "--" : snapshot.TimestampUtc.ToString("yyyy-MM-dd HH:mm:ss") + "Z")}");
            sb.AppendLine($"Last error:           {Blank(snapshot.LastError)}");
            sb.AppendLine();
            sb.AppendLine("SimHub device definition:");
            sb.AppendLine($"Restart required:    {deviceDefinitionDeployed}");
            sb.AppendLine($"Status:              {Blank(deviceDefinitionStatus)}");
            sb.AppendLine();
            sb.AppendLine("Control Mapper virtual inputs:");
            sb.AppendLine(virtualInputsText);
            sb.AppendLine();
            sb.AppendLine("Control Mapper source bridge:");
            sb.AppendLine(controlMapperSourceText);
            sb.AppendLine();
            sb.AppendLine("SDK calls:");

            if (snapshot.Calls.Count == 0)
            {
                sb.AppendLine("(no calls captured)");
                return sb.ToString();
            }

            foreach (MozaSdkCallResult call in snapshot.Calls)
            {
                sb.AppendLine(
                    $"{call.Operation,-44} success={call.Success,-5} err={call.ErrorCode,-18} " +
                    $"ms={call.ElapsedMs,4} detail={call.Detail}");
            }

            return sb.ToString();
        }

        private void TryCopy(string text, string successMessage)
        {
            try
            {
                Clipboard.SetText(text ?? "");
                SetTextIfChanged(CopyStatusText, successMessage);
            }
            catch (Exception ex)
            {
                SetTextIfChanged(CopyStatusText, $"Copy failed: {ex.Message}");
            }
        }

        private void TrySetStatus(string message)
        {
            SetTextIfChanged(CopyStatusText, message);
        }

        private static void SetTextIfChanged(TextBlock textBlock, string text)
        {
            text = text ?? "";
            if (!string.Equals(textBlock.Text, text, StringComparison.Ordinal))
                textBlock.Text = text;
        }

        private static void SetTextIfChanged(TextBox textBox, string text)
        {
            text = text ?? "";
            if (!string.Equals(textBox.Text, text, StringComparison.Ordinal))
                textBox.Text = text;
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

        private static string FormatPressCounts(IReadOnlyDictionary<int, int> counts)
        {
            if (counts == null || counts.Count == 0)
                return "None";

            return string.Join(", ", counts.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}"));
        }

        private string GetSelectedLogFilter() =>
            LogFilterComboBox.SelectedItem?.ToString() ?? "All";

        private sealed class WheelProfileListItem
        {
            public WheelProfileListItem(WheelProfileView profile)
            {
                WheelName = profile.WheelName;
                VJoyDeviceId = profile.VJoyDeviceId;
                Text = profile.DisplayName
                    + (profile.IsAssigned ? $" -> vJoy {profile.VJoyDeviceId}" : " -> unassigned")
                    + (profile.IsCurrent ? " (current)" : "");
            }

            public string WheelName { get; }

            public int VJoyDeviceId { get; }

            private string Text { get; }

            public override string ToString() => Text;
        }
    }
}
