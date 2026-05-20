using System.Collections.Generic;
using System.Linq;

namespace MozaDevicesPlugin.Models
{
    internal sealed class MozaHidSnapshot
    {
        public static readonly MozaHidSnapshot Empty = new MozaHidSnapshot(
            available: false,
            errorCode: "",
            steeringWheelAngle: null,
            steeringWheelVelocity: null,
            steeringWheelAcceleration: null,
            steeringWheelAxle: null,
            throttle: null,
            brake: null,
            clutch: null,
            handbrake: null,
            shift: "",
            buttonCount: 0,
            pressedButtons: new int[0],
            pressEventButtons: new int[0],
            buttonPressCounts: new Dictionary<int, int>());

        public MozaHidSnapshot(
            bool available,
            string errorCode,
            float? steeringWheelAngle,
            float? steeringWheelVelocity,
            float? steeringWheelAcceleration,
            short? steeringWheelAxle,
            short? throttle,
            short? brake,
            short? clutch,
            short? handbrake,
            string shift,
            int buttonCount,
            IEnumerable<int> pressedButtons,
            IEnumerable<int> pressEventButtons,
            IReadOnlyDictionary<int, int> buttonPressCounts)
        {
            Available = available;
            ErrorCode = errorCode ?? "";
            SteeringWheelAngle = steeringWheelAngle;
            SteeringWheelVelocity = steeringWheelVelocity;
            SteeringWheelAcceleration = steeringWheelAcceleration;
            SteeringWheelAxle = steeringWheelAxle;
            Throttle = throttle;
            Brake = brake;
            Clutch = clutch;
            Handbrake = handbrake;
            Shift = shift ?? "";
            ButtonCount = buttonCount;
            PressedButtons = pressedButtons?.ToArray() ?? new int[0];
            PressEventButtons = pressEventButtons?.ToArray() ?? new int[0];
            ButtonPressCounts = buttonPressCounts != null
                ? buttonPressCounts.ToDictionary(kv => kv.Key, kv => kv.Value)
                : new Dictionary<int, int>();
        }

        public bool Available { get; }

        public string ErrorCode { get; }

        public float? SteeringWheelAngle { get; }

        public float? SteeringWheelVelocity { get; }

        public float? SteeringWheelAcceleration { get; }

        public short? SteeringWheelAxle { get; }

        public short? Throttle { get; }

        public short? Brake { get; }

        public short? Clutch { get; }

        public short? Handbrake { get; }

        public string Shift { get; }

        public int ButtonCount { get; }

        public IReadOnlyList<int> PressedButtons { get; }

        public IReadOnlyList<int> PressEventButtons { get; }

        public IReadOnlyDictionary<int, int> ButtonPressCounts { get; }
    }
}
