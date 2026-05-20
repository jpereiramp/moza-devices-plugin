using System;
using System.Collections.Generic;
using System.Linq;

namespace MozaDevicesPlugin.Models
{
    internal sealed class MozaDeviceSnapshot
    {
        public static readonly MozaDeviceSnapshot Empty = new MozaDeviceSnapshot(
            sdkInstalled: false,
            pollActive: false,
            status: "Not started",
            lastError: "",
            pollCount: 0,
            consecutiveFailures: 0,
            timestampUtc: DateTime.MinValue,
            parents: MozaDeviceParentSnapshot.Empty,
            wheel: MozaWheelSnapshot.Empty,
            hid: MozaHidSnapshot.Empty,
            calls: new MozaSdkCallResult[0]);

        public MozaDeviceSnapshot(
            bool sdkInstalled,
            bool pollActive,
            string status,
            string lastError,
            long pollCount,
            int consecutiveFailures,
            DateTime timestampUtc,
            MozaDeviceParentSnapshot parents,
            MozaWheelSnapshot wheel,
            MozaHidSnapshot hid,
            IEnumerable<MozaSdkCallResult> calls)
        {
            SdkInstalled = sdkInstalled;
            PollActive = pollActive;
            Status = status ?? "";
            LastError = lastError ?? "";
            PollCount = pollCount;
            ConsecutiveFailures = consecutiveFailures;
            TimestampUtc = timestampUtc;
            Parents = parents ?? MozaDeviceParentSnapshot.Empty;
            Wheel = wheel ?? MozaWheelSnapshot.Empty;
            Hid = hid ?? MozaHidSnapshot.Empty;
            Calls = calls?.ToArray() ?? new MozaSdkCallResult[0];
        }

        public bool SdkInstalled { get; }

        public bool PollActive { get; }

        public string Status { get; }

        public string LastError { get; }

        public long PollCount { get; }

        public int ConsecutiveFailures { get; }

        public DateTime TimestampUtc { get; }

        public MozaDeviceParentSnapshot Parents { get; }

        public MozaWheelSnapshot Wheel { get; }

        public MozaHidSnapshot Hid { get; }

        public IReadOnlyList<MozaSdkCallResult> Calls { get; }
    }
}
