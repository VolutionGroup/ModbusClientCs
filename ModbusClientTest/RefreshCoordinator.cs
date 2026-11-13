using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace VVG.Modbus.ClientTest
{
    /// <summary>Controls polling eligibility and prevents overlapping manual/background operations.</summary>
    internal sealed class RefreshCoordinator
    {
        private int _busy;
        public bool Enabled { get; set; }
        public int IntervalMilliseconds { get; private set; } = 1000;
        public bool IntervalValid { get; private set; } = true;
        public bool IsBusy { get { return Volatile.Read(ref _busy) != 0; } }

        public bool SetInterval(string text)
        {
            int interval;
            IntervalValid = int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out interval)
                && interval >= 100 && interval <= 3600000;
            if (IntervalValid)
            {
                IntervalMilliseconds = interval;
            }
            return IntervalValid;
        }

        public bool CanRefresh(bool connected, int selectedTab)
        {
            return Enabled && IntervalValid && connected && !IsBusy && selectedTab >= 0 && selectedTab < 4;
        }

        public async Task<bool> TryRunAsync(Func<Task> operation)
        {
            if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
            {
                return false;
            }
            try
            {
                await operation();
                return true;
            }
            finally
            {
                Volatile.Write(ref _busy, 0);
            }
        }
    }
}
