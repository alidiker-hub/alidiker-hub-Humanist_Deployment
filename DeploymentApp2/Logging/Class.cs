using System;
using System.Collections.Generic;

namespace DeploymentApp2.Logging
{
    public sealed class LiveLogService
    {
        private readonly object _gate = new();
        private readonly List<string> _lines = new(4096);
        private readonly int _max = 5000;

        public event EventHandler? Changed;

        public void Add(string line)
        {
            lock (_gate)
            {
                _lines.Add(line);
                if (_lines.Count > _max)
                {
                    // Eskiyi buda (batched)
                    _lines.RemoveRange(0, _lines.Count - _max);
                }
            }
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public string[] Snapshot()
        {
            lock (_gate)
            {
                return _lines.ToArray();
            }
        }

        public void Clear()
        {
            lock (_gate) { _lines.Clear(); }
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
