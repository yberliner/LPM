using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MSGS
{
    public class LogTimer
    {
        private DateTime _lastLogTime = DateTime.MinValue;
        private readonly TimeSpan _interval;

        public LogTimer(int intervalMinutes)
        {
            if (intervalMinutes <= 0)
                intervalMinutes = 10; // default to 10 minutes

            _interval = TimeSpan.FromMinutes(intervalMinutes);
        }

        public bool TimeToLog()
        {
            var now = DateTime.UtcNow; // use UTC to avoid DST issues
            if (now - _lastLogTime >= _interval)
            {
                _lastLogTime = now;
                return true;
            }
            return false;
        }
    }
}
