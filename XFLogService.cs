using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xamarin.Essentials;
using XFLog.Models;

namespace XFLog
{
    public interface IXFLogService
    {
        void Identify(string userUID, string deviceUID);
        void IdentifyDevice(string deviceUID);
        void IdentityUser(string userUID);
        void Log(Exception ex, Dictionary<string, string> properties = null, SeverityType severity = SeverityType.Error);
        void Log(string tag, string message, string messageDetails = null, Dictionary<string, string> properties = null, SeverityType severity = SeverityType.Info);
        void Register(bool autoGenerateIdentity = true, int syncFrequencyMillis = 10000, string syncEndpointUrl = null, int maximumLogCount = 300);
    }

    public class XFLogService : IXFLogService
    {
        // default endpoint to post logs to
        private const string SYNC_ENDPOINT_URL = null;

        // default frequency at which logs are sent
        private const int SYNC_FREQUENCY_MILLIS = 10000; // 10 seconds

        // maximum number of logs stored locally before truncation occurs
        private const int MAX_LOG_COUNT = 300;

        #region global exception handler
        /// <summary>
        /// Registers global exception handlers
        /// For Android: This should be called from MainActivity.OnCreate()
        /// For iOS: This should be called from AppDelegate.FinishedLaunching()
        /// </summary>
        public void Register(
            bool autoGenerateIdentity = true, 
            int syncFrequencyMillis = SYNC_FREQUENCY_MILLIS, 
            string syncEndpointUrl = SYNC_ENDPOINT_URL,
            int maximumLogCount = MAX_LOG_COUNT)
        {
            if (_instance != null) throw new Exception(nameof(XFLogService) + " can only be registered once!");

            _instance = this;

            AppDomain.CurrentDomain.UnhandledException += CurrentDomainOnUnhandledException;
            TaskScheduler.UnobservedTaskException += TaskSchedulerOnUnobservedTaskException;

            maxLogCount = maximumLogCount;
            getLogCount();
            if (autoGenerateIdentity) generateIdentity();
            startSynchronizationThread(syncFrequencyMillis, syncEndpointUrl);
        }
        private static XFLogService _instance = null;

        private void TaskSchedulerOnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs unobservedTaskExceptionEventArgs)
        {
            var newExc = new Exception("TaskSchedulerOnUnobservedTaskException", unobservedTaskExceptionEventArgs.Exception);
            Log(newExc, severity: SeverityType.Crash);
        }

        private void CurrentDomainOnUnhandledException(object sender, UnhandledExceptionEventArgs unhandledExceptionEventArgs)
        {
            var newExc = new Exception("CurrentDomainOnUnhandledException", unhandledExceptionEventArgs.ExceptionObject as Exception);
            Log(newExc, severity: SeverityType.Crash);
        }
        #endregion
        #region user identity
        /// <summary>
        /// Used to identify the user/device in any reported log events
        /// </summary>
        public void Identify(string userUID, string deviceUID)
        {
            _userUID = userUID;
            _deviceUID = deviceUID;
        }
        /// <summary>
        /// User to identify the user in any reported log events
        /// </summary>
        public void IdentityUser(string userUID)
        {
            _userUID = userUID;
        }
        /// <summary>
        /// Used to identify the device in any reported log events
        /// </summary>
        public void IdentifyDevice(string deviceUID)
        {
            _deviceUID = deviceUID;
        }
        private static string _userUID;
        private static string _deviceUID;
        private void generateIdentity()
        {
            _userUID = Preferences.Get("XFLog.UserUID", null);
            _deviceUID = Preferences.Get("XFLog.DeviceUID", null);

            if (_userUID == null)
            {
                _userUID = Guid.NewGuid().ToString();
                Preferences.Set("XFLog.UserUID", _userUID);
            }
            if (_deviceUID == null)
            {
                _deviceUID = Guid.NewGuid().ToString();
                Preferences.Set("XFLog.DeviceUID", _deviceUID);
            }

        }
        #endregion
        #region logging interface
        /// <summary>
        /// Creates a user defined info event
        /// </summary>
        /// <param name="tag"></param>
        /// <param name="message"></param>
        /// <param name="messageDetails"></param>
        /// <param name="properties"></param>
        /// <param name="severity"></param>
        public void Log(string tag, string message, string messageDetails = null, Dictionary<string, string> properties = null, SeverityType severity = SeverityType.Info)
        {
            var logEvent = new LogEvent()
            {
                Severity = severity,
                Tag = tag,
                Summary = message,
                Detail = messageDetails,
                DeviceUID = _deviceUID,
                UserUID = _userUID,
                Properties = properties
            };
        }

        /// <summary>
        /// Creates a user defined error event
        /// </summary>
        /// <param name="ex"></param>
        /// <param name="properties"></param>
        /// <param name="severity"></param>
        public void Log(Exception ex, Dictionary<string, string> properties = null, SeverityType severity = SeverityType.Error)
        {
            var logEvent = new LogEvent()
            {
                Severity = severity,
                Summary = ex.Message,
                Detail = ex.StackTrace,
                DeviceUID = _deviceUID,
                UserUID = _userUID,
                Tag = ex.GetType().Name,
                Source = ex.Source,
                Properties = new Dictionary<string, string>()
            };
            foreach (DictionaryEntry p in ex.Data)
            {
                logEvent.Properties.Add(p.Key.ToString(), p.Value.ToString());
            }
            // append additional user data (when provided)
            if (properties != null)
            {
                foreach (var p in properties)
                    logEvent.Properties.Add(p.Key, p.Value);
            }
            log(logEvent);

        }
        #endregion
        #region log persistence
        private void log(LogEvent logEvent)
        {
            // truncate log if needed
            var logCount= currentLogCount;
            if (logCount > maxLogCount)
                purgeLogs( (int)maxLogCount / 2);

            // write log
            var index = incrementLogCount();
            var json = JsonConvert.SerializeObject(logEvent);
            Preferences.Set($"XFLog[{index}]", json);
        }
        private static int maxLogCount;
        private static int currentLogCount;
        private static readonly object logLock = new object();
        private void getLogCount()
        {
            currentLogCount = Preferences.Get("XFLog.Count", 0);
        }
        private int incrementLogCount()
        {
            lock (logLock)
            {
                var result = Interlocked.Increment(ref currentLogCount);
                Preferences.Set("XFLog.Count", currentLogCount);
                return result - 1;
            }
        }
        private string getLog(int index)
        {
            return Preferences.Get($"XFLog[{index}]", null);
        }
        private List<string> getRawLogs()
        {
            var logs = new List<string>();
            var count = currentLogCount;
            for (var i = 0; i < count; i++)
            {
                var log = getLog(i);
                if (log != null)
                    logs.Add(log);
            }
            return logs;
        }
        private void purgeLogs()
        {
            lock (logLock)
            {
                var count = Interlocked.Exchange(ref currentLogCount, 0);
                Preferences.Set("XFLog.Count", 0);
                for (var i = 0; i < count; i++)
                {
                    Preferences.Remove($"XFLog[{i}]");
                }
            }
        }
        /// <summary>
        /// purge n logs, move remaining indeces up
        /// </summary>
        private void purgeLogs(int logCount)
        {
            var remainingLogCount = currentLogCount - logCount;
            if (remainingLogCount == 0)
                purgeLogs();
            else
            {
                lock (logLock)
                {
                    var count = Interlocked.Exchange(ref currentLogCount, remainingLogCount);
                    Preferences.Set("XFLog.Count", remainingLogCount);
                    for (var i = 0; i < logCount; i++)
                    {
                        Preferences.Remove($"XFLog[{i}]");
                    }
                    int j = 0;
                    for (var i = logCount; i < count; i++)
                    {
                        string newKey = $"XFLog[{j++}]";
                        Preferences.Set(newKey, Preferences.Get($"XFLog[{i}]", null));
                    }
                }
            }
        }
        #endregion
        #region log network sync
        private async Task sync()
        {
            try
            {
                var logCount = currentLogCount;
                if (logCount == 0) return; // no logs to send

                string json;
                {
                    var sb = new StringBuilder();
                    sb.Append("[");
                    sb.Append(string.Join(",", getRawLogs()));
                    sb.Append("]");
                    json = sb.ToString();
                }

                using (var client = new HttpClient())
                {
                    var uri = new Uri(_syncEndpointUrl);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");

                    HttpResponseMessage response = await client.PostAsync(uri, content);

                    if (response.IsSuccessStatusCode)
                    {
                        purgeLogs(logCount);
                    }
                }
            }
            catch
            {
                // noop
            }
        }

        private async void synchronizeData()
        {
            // Repeats sync process every N milliseconds
            while (true)
            {
                await Task.Delay(_syncFrequencyMillis);
                await sync();
            }
        }

        // Is called pretty much when app starts.
        private void startSynchronizationThread(int syncFrequencyMillis, string syncEndpointUrl)
        {
            _syncFrequencyMillis = syncFrequencyMillis;
            _syncEndpointUrl = syncEndpointUrl;
            if (!string.IsNullOrEmpty(syncEndpointUrl))
            {
                Task.Factory.StartNew(synchronizeData,
                    CancellationToken.None, TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);
            }
        }
        private static int _syncFrequencyMillis;
        private static string _syncEndpointUrl;
        #endregion
    }
}
