using System;
using System.Collections.Generic;
using System.Linq;
using MozaDevicesPlugin.Models;
using SharpDX;
using SharpDX.DirectInput;

namespace MozaDevicesPlugin.Devices
{
    internal sealed class DirectInputButtonReader : IDisposable
    {
        private const int MaxButtons = 128;
        private const int EnumerationIntervalMs = 2000;

        private readonly DiagnosticsLog _log;
        private readonly object _sync = new object();
        private DirectInput? _directInput;
        private Joystick? _joystick;
        private Guid _activeInstanceGuid = Guid.Empty;
        private string _activeDeviceName = "";
        private int _activeButtonCount;
        private readonly bool[] _previousButtons = new bool[MaxButtons + 1];
        private readonly int[] _pressCounts = new int[MaxButtons + 1];
        private DateTime _nextEnumerationUtc = DateTime.MinValue;
        private string _status = "DirectInput not initialized.";
        private string _deviceSummary = "";
        private bool _disposed;

        public DirectInputButtonReader(DiagnosticsLog log)
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

        public string DeviceSummary
        {
            get
            {
                lock (_sync)
                {
                    return string.IsNullOrWhiteSpace(_deviceSummary) ? "(none enumerated)" : _deviceSummary;
                }
            }
        }

        public MozaHidSnapshot Poll()
        {
            lock (_sync)
            {
                if (_disposed)
                    return CreateUnavailableSnapshot("DirectInput reader disposed.");

                if (!EnsureDirectInput())
                    return CreateUnavailableSnapshot(_status);

                DateTime nowUtc = DateTime.UtcNow;
                if (_joystick == null || nowUtc >= _nextEnumerationUtc)
                    EnsureDevice(nowUtc);

                if (_joystick == null)
                    return CreateUnavailableSnapshot(_status);

                return PollActiveDevice();
            }
        }

        public void RequestRefresh()
        {
            lock (_sync)
            {
                _nextEnumerationUtc = DateTime.MinValue;
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed)
                    return;

                ReleaseActiveDevice();
                _directInput?.Dispose();
                _directInput = null;
                _disposed = true;
            }
        }

        private bool EnsureDirectInput()
        {
            if (_directInput != null)
                return true;

            try
            {
                _directInput = new DirectInput();
                _status = "DirectInput initialized.";
                return true;
            }
            catch (Exception ex)
            {
                _status = "DirectInput initialization failed: " + ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        private void EnsureDevice(DateTime nowUtc)
        {
            if (_directInput == null)
                return;

            _nextEnumerationUtc = nowUtc.AddMilliseconds(EnumerationIntervalMs);

            IReadOnlyList<DeviceInstance> devices;
            try
            {
                devices = _directInput.GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AttachedOnly).ToArray();
            }
            catch (Exception ex)
            {
                _status = "DirectInput enumeration failed: " + ex.GetType().Name + ": " + ex.Message;
                return;
            }

            _deviceSummary = BuildDeviceSummary(devices);

            DeviceInstance? selected = devices
                .Select(device => new { Device = device, Score = ScoreDevice(device) })
                .Where(candidate => candidate.Score > int.MinValue)
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => GetDisplayName(candidate.Device), StringComparer.OrdinalIgnoreCase)
                .Select(candidate => candidate.Device)
                .FirstOrDefault();

            if (selected == null)
            {
                if (_joystick == null)
                    _status = "Waiting: no MOZA DirectInput game controller found.";
                return;
            }

            if (_joystick != null && selected.InstanceGuid == _activeInstanceGuid)
            {
                _status = $"DirectInput active: {_activeDeviceName} ({_activeButtonCount} buttons).";
                return;
            }

            OpenDevice(selected);
        }

        private void OpenDevice(DeviceInstance device)
        {
            if (_directInput == null)
                return;

            ReleaseActiveDevice();

            string displayName = GetDisplayName(device);
            try
            {
                var joystick = new Joystick(_directInput, device.InstanceGuid);
                joystick.Properties.BufferSize = MaxButtons;
                joystick.Acquire();

                _joystick = joystick;
                _activeInstanceGuid = device.InstanceGuid;
                _activeDeviceName = displayName;
                _activeButtonCount = Math.Min(Math.Max(joystick.Capabilities.ButtonCount, 0), MaxButtons);
                ResetButtonTracking();
                _status = $"DirectInput active: {_activeDeviceName} ({_activeButtonCount} buttons).";
                _log.Info($"DirectInput button source selected: {_activeDeviceName} ({_activeButtonCount} buttons)");
            }
            catch (Exception ex)
            {
                ReleaseActiveDevice();
                _status = $"DirectInput open failed for {displayName}: {ex.GetType().Name}: {ex.Message}";
            }
        }

        private MozaHidSnapshot PollActiveDevice()
        {
            if (_joystick == null)
                return CreateUnavailableSnapshot(_status);

            try
            {
                try
                {
                    _joystick.Poll();
                }
                catch (SharpDXException)
                {
                    _joystick.Acquire();
                    _joystick.Poll();
                }

                JoystickState state = _joystick.GetCurrentState();
                bool[] buttons = state.Buttons ?? Array.Empty<bool>();
                int buttonCount = _activeButtonCount > 0
                    ? _activeButtonCount
                    : Math.Min(MaxButtons, buttons.Length);
                var pressedButtons = new List<int>();
                var pressEventButtons = new List<int>();

                for (int button = 1; button <= buttonCount; button++)
                {
                    bool isPressed = button - 1 < buttons.Length && buttons[button - 1];
                    if (isPressed)
                        pressedButtons.Add(button);

                    if (isPressed && !_previousButtons[button])
                    {
                        pressEventButtons.Add(button);
                        _pressCounts[button] = SafeAdd(_pressCounts[button], 1);
                    }

                    _previousButtons[button] = isPressed;
                }

                for (int button = buttonCount + 1; button < _previousButtons.Length; button++)
                    _previousButtons[button] = false;

                _status = $"DirectInput active: {_activeDeviceName} ({buttonCount} buttons).";
                return new MozaHidSnapshot(
                    true,
                    "DirectInput: " + _activeDeviceName,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    "",
                    buttonCount,
                    pressedButtons,
                    pressEventButtons,
                    GetPressCounts());
            }
            catch (Exception ex)
            {
                string deviceName = _activeDeviceName;
                ReleaseActiveDevice();
                _nextEnumerationUtc = DateTime.MinValue;
                _status = $"DirectInput poll failed for {deviceName}: {ex.GetType().Name}: {ex.Message}";
                return CreateUnavailableSnapshot(_status);
            }
        }

        private void ReleaseActiveDevice()
        {
            if (_joystick != null)
            {
                try { _joystick.Unacquire(); }
                catch { }
                _joystick.Dispose();
            }

            _joystick = null;
            _activeInstanceGuid = Guid.Empty;
            _activeDeviceName = "";
            _activeButtonCount = 0;
            ResetButtonTracking();
        }

        private void ResetButtonTracking()
        {
            Array.Clear(_previousButtons, 0, _previousButtons.Length);
            Array.Clear(_pressCounts, 0, _pressCounts.Length);
        }

        private IReadOnlyDictionary<int, int> GetPressCounts()
        {
            var counts = new Dictionary<int, int>();
            for (int button = 1; button < _pressCounts.Length; button++)
            {
                int count = _pressCounts[button];
                if (count > 0)
                    counts[button] = count;
            }

            return counts;
        }

        private static string BuildDeviceSummary(IReadOnlyList<DeviceInstance> devices)
        {
            if (devices.Count == 0)
                return "(none)";

            return string.Join(
                Environment.NewLine,
                devices
                    .OrderBy(GetDisplayName, StringComparer.OrdinalIgnoreCase)
                    .Take(16)
                    .Select(device =>
                    {
                        string marker = ScoreDevice(device) > int.MinValue ? "candidate" : "ignored";
                        return $"  {GetDisplayName(device)} [{marker}]";
                    }));
        }

        private static int ScoreDevice(DeviceInstance device)
        {
            string name = NormalizeSearchText(GetDisplayName(device));
            if (string.IsNullOrWhiteSpace(name))
                return int.MinValue;

            if (ContainsAny(name, "vjoy", "virtual", "simhub"))
                return int.MinValue;

            if (!name.Contains("moza"))
                return int.MinValue;

            if (ContainsAny(name, "pedal", "crp", "srp", "haptic", "handbrake", "shifter"))
                return int.MinValue;

            int score = 100;
            if (ContainsAny(name, "base", "wheelbase", "steering", "wheel", " r3", " r5", " r9", " r12", " r16", " r21", "_r3", "_r5", "_r9", "_r12", "_r16", "_r21"))
                score += 100;

            return score;
        }

        private static bool ContainsAny(string value, params string[] needles)
        {
            for (int i = 0; i < needles.Length; i++)
                if (value.Contains(needles[i]))
                    return true;

            return false;
        }

        private static string GetDisplayName(DeviceInstance device)
        {
            string instance = device.InstanceName ?? "";
            string product = device.ProductName ?? "";
            if (string.Equals(instance, product, StringComparison.OrdinalIgnoreCase))
                return instance;

            if (string.IsNullOrWhiteSpace(instance))
                return product;

            if (string.IsNullOrWhiteSpace(product))
                return instance;

            return $"{instance} ({product})";
        }

        private static string NormalizeSearchText(string value)
        {
            return (value ?? "").Replace('_', ' ').Replace('-', ' ').ToLowerInvariant();
        }

        private static MozaHidSnapshot CreateUnavailableSnapshot(string reason)
        {
            return new MozaHidSnapshot(
                false,
                reason,
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
                Array.Empty<int>(),
                Array.Empty<int>(),
                new Dictionary<int, int>());
        }

        private static int SafeAdd(int value, int delta)
        {
            if (delta <= 0)
                return value;

            return value > int.MaxValue - delta ? delta : value + delta;
        }
    }
}
