using System;
using System.Collections.Generic;
using System.Linq;

namespace MozaDevicesPlugin.Models
{
    internal sealed class DiagnosticsLog
    {
        private readonly object _sync = new object();
        private readonly Queue<string> _entries = new Queue<string>();
        private readonly int _maxEntries;

        public DiagnosticsLog(int maxEntries = 500)
        {
            _maxEntries = Math.Max(50, maxEntries);
        }

        public void Info(string message) => Add("INFO", message);

        public void Warn(string message) => Add("WARN", message);

        public void Error(string message) => Add("ERROR", message);

        public void Error(string message, Exception exception) =>
            Add("ERROR", $"{message}: {exception.GetType().Name}: {exception.Message}");

        public string GetText()
        {
            lock (_sync)
            {
                return string.Join(Environment.NewLine, _entries.ToArray());
            }
        }

        public IReadOnlyList<string> GetEntries()
        {
            lock (_sync)
            {
                return _entries.ToArray();
            }
        }

        private void Add(string level, string message)
        {
            string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";

            lock (_sync)
            {
                _entries.Enqueue(line);
                while (_entries.Count > _maxEntries)
                    _entries.Dequeue();
            }
        }
    }
}
