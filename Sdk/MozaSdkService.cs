using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using MozaDevicesPlugin.Models;
using MozaApi = mozaAPI.mozaAPI;
using MozaErrorCode = mozaAPI.ERRORCODE;
using MozaProductType = mozaAPI.PRODUCTTYPE;

namespace MozaDevicesPlugin.Sdk
{
    internal sealed class MozaSdkService : IDisposable
    {
        private readonly DiagnosticsLog _log;
        private readonly AutoResetEvent _refreshRequested = new AutoResetEvent(false);
        private readonly object _lifecycleSync = new object();
        private readonly HashSet<MozaProductType> _disabledDeviceParentQueries = new HashSet<MozaProductType>();

        private CancellationTokenSource? _cts;
        private Thread? _worker;
        private volatile MozaDeviceSnapshot _snapshot = MozaDeviceSnapshot.Empty;
        private MozaDeviceParentSnapshot _lastParents = MozaDeviceParentSnapshot.Empty;
        private MozaWheelSnapshot _lastWheel = MozaWheelSnapshot.Empty;
        private MozaSdkCallResult[] _lastDeviceCalls = new MozaSdkCallResult[0];
        private bool _sdkInstalled;
        private bool _disposed;
        private long _pollCount;
        private int _consecutiveFailures;
        private string _lastLoggedStatus = "";
        private string _lastLoggedDevices = "";

        public MozaSdkService(DiagnosticsLog log)
        {
            _log = log;
        }

        private delegate int IntSdkGetter(ref MozaErrorCode error);

        private delegate Dictionary<int, string> ScreenUiListGetter(ref MozaErrorCode error);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool SetDllDirectory(string? lpPathName);

        public MozaDeviceSnapshot Snapshot => _snapshot;

        public string LogText => _log.GetText();

        public string HidPollingStatus
        {
            get
            {
                return "Permanently disabled; live wheel buttons are read through Windows DirectInput.";
            }
        }

        public void Start()
        {
            lock (_lifecycleSync)
            {
                if (_disposed)
                    throw new ObjectDisposedException(nameof(MozaSdkService));

                if (_worker != null)
                    return;

                _cts = new CancellationTokenSource();
                _worker = new Thread(() => WorkerLoop(_cts.Token))
                {
                    IsBackground = true,
                    Name = "MOZA SDK poller"
                };

                _worker.Start();
                _log.Info("MOZA SDK poller started");
            }
        }

        public void Stop()
        {
            Thread? worker;
            CancellationTokenSource? cts;

            lock (_lifecycleSync)
            {
                worker = _worker;
                cts = _cts;
                _worker = null;
                _cts = null;
            }

            if (cts != null)
            {
                try
                {
                    cts.Cancel();
                    _refreshRequested.Set();
                }
                catch
                {
                }
            }

            if (worker != null && worker.IsAlive)
            {
                try { worker.Join(3000); }
                catch { }
            }

            cts?.Dispose();
        }

        public void RequestRefresh()
        {
            try { _refreshRequested.Set(); }
            catch { }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            Stop();
            _refreshRequested.Dispose();
        }

        private void WorkerLoop(CancellationToken token)
        {
            TryInstallSdk();
            PollOnce(pollDevices: true);

            while (!token.IsCancellationRequested)
            {
                int waitResult = WaitHandle.WaitAny(
                    new[] { token.WaitHandle, _refreshRequested });
                if (waitResult == 1)
                    PollOnce(pollDevices: true);
            }

            TryRemoveSdk();
            _snapshot = new MozaDeviceSnapshot(
                sdkInstalled: false,
                pollActive: false,
                status: "Stopped",
                lastError: "",
                pollCount: Interlocked.Read(ref _pollCount),
                consecutiveFailures: _consecutiveFailures,
                timestampUtc: DateTime.UtcNow,
                parents: _snapshot.Parents,
                wheel: _snapshot.Wheel,
                hid: _snapshot.Hid,
                calls: _snapshot.Calls);
            _log.Info("MOZA SDK poller stopped");
        }

        private void TryInstallSdk()
        {
            try
            {
                string assemblyDirectory = Path.GetDirectoryName(typeof(MozaSdkService).Assembly.Location) ?? "";
                if (!string.IsNullOrWhiteSpace(assemblyDirectory))
                    SetDllDirectory(assemblyDirectory);

                MozaApi.installMozaSDK();
                _sdkInstalled = true;
                _log.Info("MOZA SDK installed");
            }
            catch (Exception ex)
            {
                _sdkInstalled = false;
                _log.Error("MOZA SDK install failed", ex);
                _snapshot = new MozaDeviceSnapshot(
                    sdkInstalled: false,
                    pollActive: false,
                    status: "SDK load failed",
                    lastError: $"{ex.GetType().Name}: {ex.Message}",
                    pollCount: Interlocked.Read(ref _pollCount),
                    consecutiveFailures: _consecutiveFailures,
                    timestampUtc: DateTime.UtcNow,
                    parents: MozaDeviceParentSnapshot.Empty,
                    wheel: MozaWheelSnapshot.Empty,
                    hid: MozaHidSnapshot.Empty,
                    calls: new MozaSdkCallResult[0]);
            }
        }

        private void TryRemoveSdk()
        {
            if (!_sdkInstalled)
                return;

            try
            {
                MozaApi.removeMozaSDK();
                _log.Info("MOZA SDK removed");
            }
            catch (Exception ex)
            {
                _log.Warn($"MOZA SDK remove failed: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                _sdkInstalled = false;
            }
        }

        private void PollOnce(bool pollDevices)
        {
            if (!_sdkInstalled)
                return;

            var calls = new List<MozaSdkCallResult>();
            long pollCount = Interlocked.Increment(ref _pollCount);

            try
            {
                MozaDeviceParentSnapshot parents = _lastParents;
                MozaWheelSnapshot wheel = _lastWheel;

                if (pollDevices)
                {
                    var deviceCalls = new List<MozaSdkCallResult>();
                    parents = new MozaDeviceParentSnapshot(
                        wheelbase: GetDeviceParent("Wheelbase", MozaProductType.PRODUCT_WHEELBASE, deviceCalls),
                        steeringWheel: GetDeviceParent("Steering wheel", MozaProductType.PRODUCT_STEERINGWHEEL, deviceCalls),
                        displayScreen: GetDeviceParent("Display screen", MozaProductType.PRODUCT_DISPLAYSCREEN, deviceCalls),
                        pedals: GetDeviceParent("Pedals", MozaProductType.PRODUCT_PEDALS, deviceCalls),
                        handbrake: GetDeviceParent("Handbrake", MozaProductType.PRODUCT_HANDBRAKE, deviceCalls),
                        gearShifter: GetDeviceParent("Gear shifter", MozaProductType.PRODUCT_GEARSHIFTER, deviceCalls, optional: true),
                        adapter: GetDeviceParent("Adapter", MozaProductType.PRODUCT_ADAPTER, deviceCalls, optional: true),
                        meter: GetDeviceParent("Meter", MozaProductType.PRODUCT_METER, deviceCalls, optional: true));

                    wheel = !string.IsNullOrWhiteSpace(parents.SteeringWheel)
                        ? PollWheel(deviceCalls)
                        : MozaWheelSnapshot.Empty;

                    _lastParents = parents;
                    _lastWheel = wheel;
                    _lastDeviceCalls = deviceCalls.ToArray();
                    calls.AddRange(deviceCalls);
                }
                else
                {
                    calls.AddRange(_lastDeviceCalls);
                }

                MozaHidSnapshot hid = CreateUnavailableHidSnapshot(HidPollingStatus);

                _consecutiveFailures = 0;
                string status = BuildStatus(parents, calls);
                string lastError = FindLastError(calls);

                var snapshot = new MozaDeviceSnapshot(
                    sdkInstalled: true,
                    pollActive: true,
                    status: status,
                    lastError: lastError,
                    pollCount: pollCount,
                    consecutiveFailures: _consecutiveFailures,
                    timestampUtc: DateTime.UtcNow,
                    parents: parents,
                    wheel: wheel,
                    hid: hid,
                    calls: calls);

                _snapshot = snapshot;
                LogStateChanges(snapshot);
            }
            catch (Exception ex)
            {
                _consecutiveFailures++;
                _log.Error("MOZA SDK poll failed", ex);

                _snapshot = new MozaDeviceSnapshot(
                    sdkInstalled: true,
                    pollActive: true,
                    status: "SDK poll failed",
                    lastError: $"{ex.GetType().Name}: {ex.Message}",
                    pollCount: pollCount,
                    consecutiveFailures: _consecutiveFailures,
                    timestampUtc: DateTime.UtcNow,
                    parents: _snapshot.Parents,
                    wheel: _snapshot.Wheel,
                    hid: _snapshot.Hid,
                    calls: calls);
            }
        }

        private static string BuildStatus(MozaDeviceParentSnapshot parents, IReadOnlyList<MozaSdkCallResult> calls)
        {
            foreach (MozaSdkCallResult call in calls)
            {
                if (call.ErrorCode == MozaErrorCode.PITHOUSENOTREADY.ToString())
                    return "Pit House not ready";
            }

            return parents.AnyConnected
                ? "Connected through MOZA SDK"
                : "SDK loaded, no devices reported";
        }

        private static string FindLastError(IReadOnlyList<MozaSdkCallResult> calls)
        {
            for (int i = calls.Count - 1; i >= 0; i--)
            {
                if (!calls[i].Success)
                    return $"{calls[i].Operation}: {calls[i].ErrorCode} {calls[i].Detail}".Trim();
            }

            return "";
        }

        private void LogStateChanges(MozaDeviceSnapshot snapshot)
        {
            string devices =
                $"base='{snapshot.Parents.Wheelbase}', wheel='{snapshot.Parents.SteeringWheel}', " +
                $"display='{snapshot.Parents.DisplayScreen}', pedals='{snapshot.Parents.Pedals}', " +
                $"handbrake='{snapshot.Parents.Handbrake}', shifter='{snapshot.Parents.GearShifter}'";

            if (!string.Equals(snapshot.Status, _lastLoggedStatus, StringComparison.Ordinal)
                || !string.Equals(devices, _lastLoggedDevices, StringComparison.Ordinal))
            {
                _lastLoggedStatus = snapshot.Status;
                _lastLoggedDevices = devices;
                _log.Info($"{snapshot.Status}: {devices}");
            }
        }

        private string GetDeviceParent(
            string label,
            MozaProductType productType,
            ICollection<MozaSdkCallResult> calls,
            bool optional = false)
        {
            if (_disabledDeviceParentQueries.Contains(productType))
            {
                calls.Add(new MozaSdkCallResult(
                    $"getDeviceParent({label})",
                    "SKIPPED",
                    true,
                    0,
                    "disabled after previous AccessViolationException"));
                return "";
            }

            MozaErrorCode error = MozaErrorCode.NORMAL;
            var sw = Stopwatch.StartNew();
            try
            {
                string value = MozaApi.getDeviceParent(productType, ref error) ?? "";
                sw.Stop();
                calls.Add(new MozaSdkCallResult(
                    $"getDeviceParent({label})",
                    error.ToString(),
                    error == MozaErrorCode.NORMAL,
                    sw.ElapsedMilliseconds,
                    string.IsNullOrWhiteSpace(value) ? "(empty)" : value));
                return error == MozaErrorCode.NORMAL ? value : "";
            }
            catch (AccessViolationException ex)
            {
                sw.Stop();
                calls.Add(new MozaSdkCallResult(
                    $"getDeviceParent({label})",
                    ex.GetType().Name,
                    false,
                    sw.ElapsedMilliseconds,
                    ex.Message));

                if (optional)
                {
                    _disabledDeviceParentQueries.Add(productType);
                    _log.Warn($"Disabled optional MOZA SDK parent query for {label} after AccessViolationException. Restart SimHub to try it again.");
                }

                return "";
            }
            catch (Exception ex)
            {
                sw.Stop();
                calls.Add(new MozaSdkCallResult(
                    $"getDeviceParent({label})",
                    ex.GetType().Name,
                    false,
                    sw.ElapsedMilliseconds,
                    ex.Message));
                return "";
            }
        }

        private static MozaWheelSnapshot PollWheel(ICollection<MozaSdkCallResult> calls)
        {
            int? shiftIndicatorBrightness = ReadInt(
                "getSteeringWheelShiftIndicatorBrightness",
                MozaApi.getSteeringWheelShiftIndicatorBrightness,
                calls);
            int? clutchPaddleAxisMode = ReadInt(
                "getSteeringWheelClutchPaddleAxisMode",
                MozaApi.getSteeringWheelClutchPaddleAxisMode,
                calls);
            int? clutchPaddleCombinePosition = ReadInt(
                "getSteeringWheelClutchPaddleCombinePos",
                MozaApi.getSteeringWheelClutchPaddleCombinePos,
                calls);
            int? knobMode = ReadInt(
                "getSteeringWheelKnobMode",
                MozaApi.getSteeringWheelKnobMode,
                calls);
            int? joystickHatswitchMode = ReadInt(
                "getSteeringWheelJoystickHatswitchMode",
                MozaApi.getSteeringWheelJoystickHatswitchMode,
                calls);
            int? shiftIndicatorSwitch = ReadInt(
                "getSteeringWheelShiftIndicatorSwitch",
                MozaApi.getSteeringWheelShiftIndicatorSwitch,
                calls);
            int? shiftIndicatorMode = ReadInt(
                "getSteeringWheelShiftIndicatorMode",
                MozaApi.getSteeringWheelShiftIndicatorMode,
                calls);
            int? speedUnit = ReadInt(
                "getSteeringWheelSpeedUnit",
                MozaApi.getSteeringWheelSpeedUnit,
                calls);
            int? temperatureUnit = ReadInt(
                "getSteeringWheelTemperatureUnit",
                MozaApi.getSteeringWheelTemperatureUnit,
                calls);
            int? screenBrightness = ReadInt(
                "getSteeringWheelScreenBrightness",
                MozaApi.getSteeringWheelScreenBrightness,
                calls);
            int? screenCurrentUi = ReadInt(
                "getSteeringWheelScreenCurrentUI",
                MozaApi.getSteeringWheelScreenCurrentUI,
                calls);
            Dictionary<int, string>? screenUiList = ReadScreenUiList(
                "getSteeringWheelScreenUIList",
                MozaApi.getSteeringWheelScreenUIList,
                calls);

            string lastError = "";
            foreach (MozaSdkCallResult call in calls)
            {
                if (call.Operation.StartsWith("getSteeringWheel", StringComparison.Ordinal)
                    && !call.Success)
                {
                    lastError = call.ErrorCode;
                }
            }

            bool available =
                shiftIndicatorBrightness.HasValue
                || clutchPaddleAxisMode.HasValue
                || knobMode.HasValue
                || screenBrightness.HasValue
                || screenUiList != null;

            return new MozaWheelSnapshot(
                available,
                lastError,
                shiftIndicatorBrightness,
                clutchPaddleAxisMode,
                clutchPaddleCombinePosition,
                knobMode,
                joystickHatswitchMode,
                shiftIndicatorSwitch,
                shiftIndicatorMode,
                speedUnit,
                temperatureUnit,
                screenBrightness,
                screenCurrentUi,
                screenUiList);
        }

        private static int? ReadInt(string operation, IntSdkGetter getter, ICollection<MozaSdkCallResult> calls)
        {
            MozaErrorCode error = MozaErrorCode.NORMAL;
            var sw = Stopwatch.StartNew();
            try
            {
                int value = getter(ref error);
                sw.Stop();
                calls.Add(new MozaSdkCallResult(
                    operation,
                    error.ToString(),
                    error == MozaErrorCode.NORMAL,
                    sw.ElapsedMilliseconds,
                    value.ToString()));

                return error == MozaErrorCode.NORMAL ? value : (int?)null;
            }
            catch (Exception ex)
            {
                sw.Stop();
                calls.Add(new MozaSdkCallResult(
                    operation,
                    ex.GetType().Name,
                    false,
                    sw.ElapsedMilliseconds,
                    ex.Message));
                return null;
            }
        }

        private static Dictionary<int, string>? ReadScreenUiList(
            string operation,
            ScreenUiListGetter getter,
            ICollection<MozaSdkCallResult> calls)
        {
            MozaErrorCode error = MozaErrorCode.NORMAL;
            var sw = Stopwatch.StartNew();
            try
            {
                Dictionary<int, string> value = getter(ref error) ?? new Dictionary<int, string>();
                sw.Stop();
                calls.Add(new MozaSdkCallResult(
                    operation,
                    error.ToString(),
                    error == MozaErrorCode.NORMAL,
                    sw.ElapsedMilliseconds,
                    $"{value.Count} items"));

                return error == MozaErrorCode.NORMAL ? value : null;
            }
            catch (Exception ex)
            {
                sw.Stop();
                calls.Add(new MozaSdkCallResult(
                    operation,
                    ex.GetType().Name,
                    false,
                    sw.ElapsedMilliseconds,
                    ex.Message));
                return null;
            }
        }

        private static MozaHidSnapshot CreateUnavailableHidSnapshot(string errorCode)
        {
            return new MozaHidSnapshot(
                false,
                errorCode,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                "",
                0,
                new int[0],
                new int[0],
                new Dictionary<int, int>());
        }
    }
}
