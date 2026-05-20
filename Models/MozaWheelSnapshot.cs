using System.Collections.Generic;
using System.Linq;

namespace MozaDevicesPlugin.Models
{
    internal sealed class MozaWheelSnapshot
    {
        public static readonly MozaWheelSnapshot Empty = new MozaWheelSnapshot(
            available: false,
            errorCode: "",
            shiftIndicatorBrightness: null,
            clutchPaddleAxisMode: null,
            clutchPaddleCombinePosition: null,
            knobMode: null,
            joystickHatswitchMode: null,
            shiftIndicatorSwitch: null,
            shiftIndicatorMode: null,
            speedUnit: null,
            temperatureUnit: null,
            screenBrightness: null,
            screenCurrentUi: null,
            screenUiList: new Dictionary<int, string>());

        public MozaWheelSnapshot(
            bool available,
            string errorCode,
            int? shiftIndicatorBrightness,
            int? clutchPaddleAxisMode,
            int? clutchPaddleCombinePosition,
            int? knobMode,
            int? joystickHatswitchMode,
            int? shiftIndicatorSwitch,
            int? shiftIndicatorMode,
            int? speedUnit,
            int? temperatureUnit,
            int? screenBrightness,
            int? screenCurrentUi,
            IDictionary<int, string>? screenUiList)
        {
            Available = available;
            ErrorCode = errorCode ?? "";
            ShiftIndicatorBrightness = shiftIndicatorBrightness;
            ClutchPaddleAxisMode = clutchPaddleAxisMode;
            ClutchPaddleCombinePosition = clutchPaddleCombinePosition;
            KnobMode = knobMode;
            JoystickHatswitchMode = joystickHatswitchMode;
            ShiftIndicatorSwitch = shiftIndicatorSwitch;
            ShiftIndicatorMode = shiftIndicatorMode;
            SpeedUnit = speedUnit;
            TemperatureUnit = temperatureUnit;
            ScreenBrightness = screenBrightness;
            ScreenCurrentUi = screenCurrentUi;
            ScreenUiList = screenUiList == null
                ? new Dictionary<int, string>()
                : screenUiList.ToDictionary(k => k.Key, v => v.Value ?? "");
        }

        public bool Available { get; }

        public string ErrorCode { get; }

        public int? ShiftIndicatorBrightness { get; }

        public int? ClutchPaddleAxisMode { get; }

        public int? ClutchPaddleCombinePosition { get; }

        public int? KnobMode { get; }

        public int? JoystickHatswitchMode { get; }

        public int? ShiftIndicatorSwitch { get; }

        public int? ShiftIndicatorMode { get; }

        public int? SpeedUnit { get; }

        public int? TemperatureUnit { get; }

        public int? ScreenBrightness { get; }

        public int? ScreenCurrentUi { get; }

        public IReadOnlyDictionary<int, string> ScreenUiList { get; }
    }
}
