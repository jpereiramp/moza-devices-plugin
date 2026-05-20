using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MozaDevicesPlugin.Models;

namespace MozaDevicesPlugin.Devices
{
    internal sealed class VJoyEnvironmentStatus
    {
        public VJoyEnvironmentStatus(
            bool wrapperFound,
            string wrapperPath,
            bool driverEnabled,
            string error,
            IReadOnlyList<VJoyDeviceInspection> devices)
        {
            WrapperFound = wrapperFound;
            WrapperPath = wrapperPath ?? "";
            DriverEnabled = driverEnabled;
            Error = error ?? "";
            Devices = devices ?? Array.Empty<VJoyDeviceInspection>();
        }

        public bool WrapperFound { get; }

        public string WrapperPath { get; }

        public bool DriverEnabled { get; }

        public string Error { get; }

        public IReadOnlyList<VJoyDeviceInspection> Devices { get; }
    }

    internal sealed class VJoyDeviceInspection
    {
        public VJoyDeviceInspection(int deviceId, bool exists, string status, int buttonCount, string detail)
        {
            DeviceId = deviceId;
            Exists = exists;
            Status = status ?? "";
            ButtonCount = buttonCount;
            Detail = detail ?? "";
        }

        public int DeviceId { get; }

        public bool Exists { get; }

        public string Status { get; }

        public int ButtonCount { get; }

        public string Detail { get; }

        public bool IsUsable => Exists && ButtonCount > 0 && (Status == "VJD_STAT_FREE" || Status == "VJD_STAT_OWN");
    }

    internal sealed class VJoySourceBridge : IDisposable
    {
        private const int SyntheticPressPulseMs = 50;
        private static readonly TimeSpan InspectionCacheDuration = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan AcquireRetryDelay = TimeSpan.FromSeconds(5);

        private readonly DiagnosticsLog _log;
        private readonly object _sync = new object();
        private object? _vJoy;
        private Type? _vJoyType;
        private uint _activeDeviceId;
        private string _activeSourceName = "";
        private bool[] _buttonStates = Array.Empty<bool>();
        private DateTime[] _syntheticPressUntilUtc = Array.Empty<DateTime>();
        private int[] _observedPressCounts = Array.Empty<int>();
        private bool _pressCountBaselineInitialized;
        private string _status = "Not initialized.";
        private VJoyEnvironmentStatus? _lastInspection;
        private string _lastInspectionKey = "";
        private DateTime _lastInspectionUtc = DateTime.MinValue;
        private DateTime _nextAcquireAttemptUtc = DateTime.MinValue;
        private bool _disposed;

        public VJoySourceBridge(DiagnosticsLog log)
        {
            _log = log;
        }

        public string StatusText
        {
            get
            {
                lock (_sync)
                {
                    return _status;
                }
            }
        }

        public void Update(
            MozaDeviceSnapshot snapshot,
            string sourceName,
            int targetVJoyDeviceId,
            int requestedButtonCount,
            MozaPluginSettings pluginSettings,
            SimHubControlMapperSettingsStatus controlMapperSettings)
        {
            lock (_sync)
            {
                if (_disposed)
                    return;

                if (!pluginSettings.EnableVJoySourceBridge)
                {
                    ReleaseActiveDevice();
                    SetStatus("Disabled in MOZA SDK plugin settings.");
                    return;
                }

                if (controlMapperSettings.Available && !controlMapperSettings.AllowVJoyAsSource)
                {
                    ReleaseActiveDevice();
                    SetStatus("Waiting: SimHub Control Mapper has AllowVJOYAsAsource=false.");
                    return;
                }

                if (!snapshot.Hid.Available || string.IsNullOrWhiteSpace(sourceName) || requestedButtonCount <= 0)
                {
                    ReleaseActiveDevice();
                    SetStatus("Waiting: no MOZA DirectInput buttons are available.");
                    return;
                }

                if (_activeDeviceId != 0
                    && string.Equals(_activeSourceName, sourceName, StringComparison.Ordinal))
                {
                    DriveButtons(snapshot, requestedButtonCount, pluginSettings.MaxVJoyButtons);
                    return;
                }

                DateTime nowUtc = DateTime.UtcNow;
                if (nowUtc < _nextAcquireAttemptUtc)
                    return;

                if (!EnsureLoaded())
                {
                    DelayNextAcquireAttempt();
                    return;
                }

                if (!InvokeBoolean("vJoyEnabled"))
                {
                    ReleaseActiveDevice();
                    DelayNextAcquireAttempt();
                    SetStatus("Waiting: vJoy driver is not enabled or no vJoy device is configured.");
                    return;
                }

                uint targetDeviceId = ResolveTargetDeviceId(
                    targetVJoyDeviceId,
                    pluginSettings,
                    controlMapperSettings,
                    requestedButtonCount,
                    out string resolveMessage);

                if (targetDeviceId == 0)
                {
                    ReleaseActiveDevice();
                    DelayNextAcquireAttempt();
                    SetStatus(resolveMessage);
                    return;
                }

                if (_activeDeviceId != targetDeviceId || !string.Equals(_activeSourceName, sourceName, StringComparison.Ordinal))
                {
                    ReleaseActiveDevice();
                    if (!AcquireDevice(targetDeviceId, sourceName, out string acquireMessage))
                    {
                        DelayNextAcquireAttempt();
                        SetStatus(acquireMessage);
                        return;
                    }
                }

                DriveButtons(snapshot, requestedButtonCount, pluginSettings.MaxVJoyButtons);
            }
        }

        public void Release()
        {
            lock (_sync)
            {
                ReleaseActiveDevice();
                InvalidateInspectionCache();
                _nextAcquireAttemptUtc = DateTime.MinValue;
                SetStatus("Released.");
            }
        }

        public VJoyEnvironmentStatus Inspect(IEnumerable<int> deviceIds)
        {
            lock (_sync)
            {
                string inspectionKey = BuildInspectionKey(deviceIds);
                DateTime nowUtc = DateTime.UtcNow;
                if (_lastInspection != null
                    && string.Equals(_lastInspectionKey, inspectionKey, StringComparison.Ordinal)
                    && nowUtc - _lastInspectionUtc < InspectionCacheDuration)
                {
                    return _lastInspection;
                }

                string wrapperPath = FindVJoyWrapperPath();
                if (string.IsNullOrWhiteSpace(wrapperPath))
                {
                    return CacheInspection(inspectionKey, nowUtc, new VJoyEnvironmentStatus(
                        wrapperFound: false,
                        wrapperPath: "",
                        driverEnabled: false,
                        error: "vJoyInterfaceWrap.dll was not found in the SimHub folder.",
                        devices: Array.Empty<VJoyDeviceInspection>()));
                }

                if (!EnsureLoaded())
                {
                    return CacheInspection(inspectionKey, nowUtc, new VJoyEnvironmentStatus(
                        wrapperFound: true,
                        wrapperPath: wrapperPath,
                        driverEnabled: false,
                        error: StatusText,
                        devices: Array.Empty<VJoyDeviceInspection>()));
                }

                try
                {
                    bool driverEnabled = InvokeBoolean("vJoyEnabled");
                    var devices = new List<VJoyDeviceInspection>();
                    foreach (int id in ParseInspectionKey(inspectionKey))
                        devices.Add(InspectDevice(id));

                    return CacheInspection(inspectionKey, nowUtc, new VJoyEnvironmentStatus(
                        wrapperFound: true,
                        wrapperPath: wrapperPath,
                        driverEnabled: driverEnabled,
                        error: driverEnabled ? "" : "vJoy driver is not enabled or no vJoy device is configured.",
                        devices: devices));
                }
                catch (Exception ex)
                {
                    return CacheInspection(inspectionKey, nowUtc, new VJoyEnvironmentStatus(
                        wrapperFound: true,
                        wrapperPath: wrapperPath,
                        driverEnabled: false,
                        error: ex.GetType().Name + ": " + ex.Message,
                        devices: Array.Empty<VJoyDeviceInspection>()));
                }
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed)
                    return;

                ReleaseActiveDevice();
                _disposed = true;
            }
        }

        private VJoyEnvironmentStatus CacheInspection(string key, DateTime timestampUtc, VJoyEnvironmentStatus status)
        {
            _lastInspectionKey = key;
            _lastInspectionUtc = timestampUtc;
            _lastInspection = status;
            return status;
        }

        private void InvalidateInspectionCache()
        {
            _lastInspection = null;
            _lastInspectionKey = "";
            _lastInspectionUtc = DateTime.MinValue;
        }

        private void DelayNextAcquireAttempt()
        {
            _nextAcquireAttemptUtc = DateTime.UtcNow.Add(AcquireRetryDelay);
        }

        private static string BuildInspectionKey(IEnumerable<int> deviceIds)
        {
            return string.Join(",", deviceIds.Where(i => i >= 1 && i <= 16).Distinct().OrderBy(i => i));
        }

        private static IEnumerable<int> ParseInspectionKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                yield break;

            foreach (string part in key.Split(','))
            {
                if (int.TryParse(part, out int id) && id >= 1 && id <= 16)
                    yield return id;
            }
        }

        private bool EnsureLoaded()
        {
            if (_vJoy != null && _vJoyType != null)
                return true;

            try
            {
                string assemblyPath = FindVJoyWrapperPath();

                if (string.IsNullOrWhiteSpace(assemblyPath))
                {
                    SetStatus("Waiting: vJoyInterfaceWrap.dll was not found in the SimHub folder.");
                    return false;
                }

                Assembly assembly = Assembly.LoadFrom(assemblyPath);
                _vJoyType = assembly.GetType("vJoyInterfaceWrap.vJoy", throwOnError: true);
                _vJoy = Activator.CreateInstance(_vJoyType);
                SetStatus("vJoy wrapper loaded.");
                return true;
            }
            catch (Exception ex)
            {
                ReleaseActiveDevice();
                SetStatus("vJoy wrapper load failed: " + ex.GetType().Name + ": " + ex.Message);
                return false;
            }
        }

        private static string FindVJoyWrapperPath()
        {
            string localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "vJoyInterfaceWrap.dll");
            if (File.Exists(localPath))
                return localPath;

            string fallbackPath = @"C:\Program Files (x86)\SimHub\vJoyInterfaceWrap.dll";
            return File.Exists(fallbackPath) ? fallbackPath : "";
        }

        private VJoyDeviceInspection InspectDevice(int deviceId)
        {
            uint id = (uint)deviceId;
            if (!InvokeBoolean("isVJDExists", id))
                return new VJoyDeviceInspection(deviceId, false, "", 0, $"vJoy device {deviceId} does not exist.");

            string status = InvokeEnumName("GetVJDStatus", id);
            int buttonCount = Math.Max(0, InvokeInt32("GetVJDButtonNumber", id));
            string detail = buttonCount <= 0
                ? $"vJoy device {deviceId} has no buttons."
                : $"vJoy device {deviceId}: {status}, {buttonCount} buttons.";

            return new VJoyDeviceInspection(deviceId, true, status, buttonCount, detail);
        }

        private uint ResolveTargetDeviceId(
            int targetVJoyDeviceId,
            MozaPluginSettings pluginSettings,
            SimHubControlMapperSettingsStatus controlMapperSettings,
            int requestedButtonCount,
            out string message)
        {
            int preferred = Clamp(targetVJoyDeviceId > 0 ? targetVJoyDeviceId : pluginSettings.VJoySourceDeviceId, 1, 16);
            bool exactTarget = pluginSettings.UsePerWheelVJoySourceDevices && targetVJoyDeviceId > 0;
            int excludedOutputId = ResolveReservedOutputVJoyDeviceId(controlMapperSettings);

            if (preferred == excludedOutputId)
            {
                message = controlMapperSettings.Available
                    ? $"Waiting: vJoy device {preferred} is assigned to this MOZA wheel but is reserved by SimHub Control Mapper output."
                    : $"Waiting: vJoy device {preferred} is reserved until SimHub Control Mapper settings can be read.";
                return 0;
            }

            if (IsDeviceCandidateUsable((uint)preferred, requestedButtonCount, requireEnoughButtons: true, out string preferredDetail))
            {
                message = "";
                return (uint)preferred;
            }

            if (IsDeviceCandidateUsable((uint)preferred, requestedButtonCount, requireEnoughButtons: false, out preferredDetail))
            {
                message = "";
                return (uint)preferred;
            }

            if (exactTarget)
            {
                message = string.IsNullOrWhiteSpace(preferredDetail)
                    ? $"Waiting: assigned vJoy device {preferred} is not usable."
                    : "Waiting: " + preferredDetail;
                return 0;
            }

            var candidates = new List<int>();
            if (preferred > 0)
                candidates.Add(preferred);

            if (pluginSettings.AutoSelectVJoySourceDevice)
            {
                for (int id = 2; id <= 16; id++)
                    if (!candidates.Contains(id))
                        candidates.Add(id);

                if (!candidates.Contains(1))
                    candidates.Add(1);
            }

            foreach (int candidate in candidates)
            {
                if (candidate == excludedOutputId)
                    continue;

                if (!IsDeviceCandidateUsable((uint)candidate, requestedButtonCount, requireEnoughButtons: true, out _))
                    continue;

                message = "";
                return (uint)candidate;
            }

            foreach (int candidate in candidates)
            {
                if (candidate == excludedOutputId)
                    continue;

                if (!IsDeviceCandidateUsable((uint)candidate, requestedButtonCount, requireEnoughButtons: false, out _))
                    continue;

                message = "";
                return (uint)candidate;
            }

            if (excludedOutputId > 0)
            {
                message = $"Waiting: no free vJoy source device was found. vJoy device {excludedOutputId} is reserved by SimHub Control Mapper output.";
            }
            else
            {
                message = "Waiting: no free configured vJoy device with enough buttons was found.";
            }

            return 0;
        }

        private static int ResolveReservedOutputVJoyDeviceId(SimHubControlMapperSettingsStatus controlMapperSettings)
        {
            if (controlMapperSettings.IsVJoyOutputMode && controlMapperSettings.OutputVJoyDeviceId > 0)
                return Clamp(controlMapperSettings.OutputVJoyDeviceId, 1, 16);

            return controlMapperSettings.Available ? 0 : 1;
        }

        private bool IsDeviceCandidateUsable(uint deviceId, int requestedButtonCount, bool requireEnoughButtons, out string detail)
        {
            detail = "";

            if (!InvokeBoolean("isVJDExists", deviceId))
            {
                detail = $"vJoy device {deviceId} does not exist.";
                return false;
            }

            string status = InvokeEnumName("GetVJDStatus", deviceId);
            if (status != "VJD_STAT_FREE" && status != "VJD_STAT_OWN")
            {
                detail = $"vJoy device {deviceId} is {status}.";
                return false;
            }

            int buttonCount = InvokeInt32("GetVJDButtonNumber", deviceId);
            if (buttonCount <= 0)
            {
                detail = $"vJoy device {deviceId} has no buttons.";
                return false;
            }

            int needed = Math.Min(Math.Max(1, requestedButtonCount), 128);
            if (requireEnoughButtons && buttonCount < needed)
            {
                detail = $"vJoy device {deviceId} has {buttonCount} buttons, needs {needed}.";
                return false;
            }

            return true;
        }

        private bool AcquireDevice(uint deviceId, string sourceName, out string message)
        {
            message = "";

            try
            {
                string status = InvokeEnumName("GetVJDStatus", deviceId);
                if (status == "VJD_STAT_FREE" && !InvokeBoolean("AcquireVJD", deviceId))
                {
                    message = $"Failed to acquire vJoy device {deviceId}.";
                    return false;
                }

                if (status != "VJD_STAT_FREE" && status != "VJD_STAT_OWN")
                {
                    message = $"Cannot acquire vJoy device {deviceId}; status is {status}.";
                    return false;
                }

                int buttonCount = Math.Max(0, InvokeInt32("GetVJDButtonNumber", deviceId));
                InvokeBoolean("ResetButtons", deviceId);
                _activeDeviceId = deviceId;
                _activeSourceName = sourceName;
                _buttonStates = new bool[Math.Min(buttonCount, 128) + 1];
                _syntheticPressUntilUtc = new DateTime[_buttonStates.Length];
                _observedPressCounts = new int[_buttonStates.Length];
                _pressCountBaselineInitialized = false;
                _log.Info($"vJoy source bridge acquired device {deviceId} for {sourceName} ({buttonCount} buttons)");
                return true;
            }
            catch (Exception ex)
            {
                message = $"vJoy device {deviceId} acquire failed: {ex.GetType().Name}: {ex.Message}";
                return false;
            }
        }

        private void DriveButtons(MozaDeviceSnapshot snapshot, int requestedButtonCount, int configuredMaxButtons)
        {
            if (_activeDeviceId == 0)
                return;

            int maxButtons = Math.Min(_buttonStates.Length - 1, Clamp(configuredMaxButtons, 1, 128));
            int buttonLimit = Math.Min(Math.Max(1, requestedButtonCount), maxButtons);
            var pressed = new HashSet<int>(snapshot.Hid.PressedButtons.Where(button => button > 0 && button <= buttonLimit));
            DateTime nowUtc = DateTime.UtcNow;
            DateTime pulseUntilUtc = nowUtc.AddMilliseconds(SyntheticPressPulseMs);

            if (!_pressCountBaselineInitialized)
            {
                foreach (var kv in snapshot.Hid.ButtonPressCounts)
                {
                    int button = kv.Key;
                    if (button > 0 && button <= buttonLimit)
                        _observedPressCounts[button] = kv.Value;
                }

                _pressCountBaselineInitialized = true;
            }
            else
            {
                foreach (var kv in snapshot.Hid.ButtonPressCounts)
                {
                    int button = kv.Key;
                    if (button <= 0 || button > buttonLimit)
                        continue;

                    int currentCount = kv.Value;
                    int previousCount = _observedPressCounts[button];
                    if (currentCount == previousCount)
                        continue;

                    if (currentCount > previousCount && _syntheticPressUntilUtc[button] < pulseUntilUtc)
                        _syntheticPressUntilUtc[button] = pulseUntilUtc;

                    _observedPressCounts[button] = currentCount;
                }
            }

            for (int button = 1; button <= buttonLimit; button++)
            {
                bool isPressed = pressed.Contains(button) || _syntheticPressUntilUtc[button] > nowUtc;
                if (_buttonStates[button] == isPressed)
                    continue;

                if (InvokeBoolean("SetBtn", isPressed, _activeDeviceId, (uint)button))
                    _buttonStates[button] = isPressed;
            }

            string truncation = requestedButtonCount > maxButtons
                ? $"; configure vJoy for {requestedButtonCount} buttons to expose the rest"
                : "";
            int syntheticCount = 0;
            for (int button = 1; button <= buttonLimit; button++)
                if (_syntheticPressUntilUtc[button] > nowUtc && !pressed.Contains(button))
                    syntheticCount++;

            string synthetic = syntheticCount > 0 ? $"; synthetic taps {syntheticCount}" : "";
            SetStatus($"Driving vJoy device {_activeDeviceId} from {_activeSourceName}; buttons {buttonLimit}/{maxButtons}; held {pressed.Count}{synthetic}{truncation}.");
        }

        private void ReleaseActiveDevice()
        {
            if (_activeDeviceId == 0 || _vJoy == null)
                return;

            try
            {
                InvokeBoolean("ResetButtons", _activeDeviceId);
                InvokeVoid("RelinquishVJD", _activeDeviceId);
                _log.Info($"vJoy source bridge released device {_activeDeviceId}");
            }
            catch (Exception ex)
            {
                _log.Warn($"vJoy source bridge release failed for device {_activeDeviceId}: {ex.Message}");
            }
            finally
            {
                _activeDeviceId = 0;
                _activeSourceName = "";
                _buttonStates = Array.Empty<bool>();
                _syntheticPressUntilUtc = Array.Empty<DateTime>();
                _observedPressCounts = Array.Empty<int>();
                _pressCountBaselineInitialized = false;
            }
        }

        private bool InvokeBoolean(string methodName, params object[] args)
        {
            object? value = Invoke(methodName, args);
            return value is bool b && b;
        }

        private int InvokeInt32(string methodName, params object[] args)
        {
            object? value = Invoke(methodName, args);
            return value is int i ? i : 0;
        }

        private string InvokeEnumName(string methodName, params object[] args)
        {
            object? value = Invoke(methodName, args);
            return value?.ToString() ?? "";
        }

        private void InvokeVoid(string methodName, params object[] args) => Invoke(methodName, args);

        private object? Invoke(string methodName, params object[] args)
        {
            if (_vJoy == null || _vJoyType == null)
                throw new InvalidOperationException("vJoy wrapper is not loaded.");

            MethodInfo? method = _vJoyType.GetMethod(methodName, args.Select(arg => arg.GetType()).ToArray());
            if (method == null)
                throw new MissingMethodException(_vJoyType.FullName, methodName);

            try
            {
                return method.Invoke(_vJoy, args);
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                throw ex.InnerException;
            }
        }

        private void SetStatus(string status)
        {
            _status = status ?? "";
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min)
                return min;

            return value > max ? max : value;
        }
    }
}
