using System;
using System.Collections.Generic;
using System.Text;

namespace XFLog.Models
{
    public class LogEvent
    {
        public LogEvent()
        {
            EventUID = Guid.NewGuid().ToString();
            TimeStamp = DateTime.UtcNow;
        }
        public SeverityType Severity { get; set; }
        public string Tag { get; set; }
        public string Summary { get; set; }
        public string Detail { get; set; }
        public string Source { get; set; }
        public string UserUID { get; set; }
        public string DeviceUID { get; set; }
        public DateTime TimeStamp { get; set; }
        public string EventUID { get; set; }
        public IDictionary<string,string> Properties { get; set; }
    }
    public enum SeverityType
    {
        Unknown =0,
        Info=1,
        Error=2,
        Crash=3
    }
}
