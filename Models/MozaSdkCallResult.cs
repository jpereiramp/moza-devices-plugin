using System;

namespace MozaDevicesPlugin.Models
{
    internal sealed class MozaSdkCallResult
    {
        public MozaSdkCallResult(
            string operation,
            string errorCode,
            bool success,
            long elapsedMs,
            string detail)
        {
            Operation = operation;
            ErrorCode = errorCode;
            Success = success;
            ElapsedMs = elapsedMs;
            Detail = detail;
            TimestampUtc = DateTime.UtcNow;
        }

        public DateTime TimestampUtc { get; }

        public string Operation { get; }

        public string ErrorCode { get; }

        public bool Success { get; }

        public long ElapsedMs { get; }

        public string Detail { get; }
    }
}
