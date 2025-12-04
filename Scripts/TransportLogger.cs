using Epic.OnlineServices.Logging;
using System.Text.RegularExpressions;
using UnityEngine;

namespace EpicTransport
{
    public class TransportLogger : MonoBehaviour
    {
        private static LogLevel Level;
        private static TransportLogLevel TLevel;

        internal void Initialize(LogLevel level, TransportLogLevel tlog)
        {
            LoggingInterface.SetCallback(OnLog);

            Level = level;
            TLevel = tlog;
            LoggingInterface.SetLogLevel(LogCategory.AllCategories, level);
        }

        private void OnLog(ref LogMessage msg)
        {
            if (msg.Category == "LogEOS" && msg.Message.ToString().Contains("Platform Properties"))
            {
                //redact all the auth keys for safety
                msg.Message = Regex.Replace(msg.Message, @"\b[A-Za-z]*Id=[^,\]\s]+", m =>
                {
                    string key = m.Value.Split('=')[0];
                    return $"{key}=<Redacted>";
                });
            }

            if (msg.Message.ToString().Contains("DeviceId access credentials already exist")) return;

            //remove very verbose from non-dev builds for safety + security reasons
#if !DEVELOPMENT_BUILD && !UNITY_EDITOR
            if (msg.Level == LogLevel.VeryVerbose) return;
#endif

            switch (msg.Level)
            {
                case LogLevel.VeryVerbose:
                case LogLevel.Verbose:
                case LogLevel.Info:
#if UNITY_EDITOR
                    Debug.Log($"<color=#00b7ff>[EOSTransport]</color> Category: {msg.Category}, Message: {msg.Message}");
#else
                    Debug.Log($"[EOSTransport] Category: {msg.Category}, Message: {msg.Message}");
#endif
                    break;

                case LogLevel.Warning:
#if UNITY_EDITOR
                    Debug.LogWarning($"<color=#00b7ff>[EOSTransport]</color> Category: {msg.Category}, Message: {msg.Message}");
#else
                    Debug.LogWarning($"[EOSTransport] Category: {msg.Category}, Message: {msg.Message}");
#endif
                    break;

                case LogLevel.Error:
                case LogLevel.Fatal:
#if UNITY_EDITOR
                    Debug.LogError($"<color=#00b7ff>[EOSTransport]</color> Category: {msg.Category}, Message: {msg.Message}");
#else
                    Debug.LogError($"[EOSTransport] Category: {msg.Category}, Message: {msg.Message}");
#endif
                    break;
            }
        }

        internal static void Log(string msg)
        {
            if (TLevel is TransportLogLevel.Warning or TransportLogLevel.Error or TransportLogLevel.None) return;

#if UNITY_EDITOR
            Debug.Log($"<color=#00b7ff>[EOSTransport]</color> {msg}");
#else
            Debug.Log($"[EOSTransport] {msg}");
#endif
        }

        internal static void LogWarning(string msg)
        {
            if (TLevel is TransportLogLevel.Error or TransportLogLevel.None) return;

#if UNITY_EDITOR
            Debug.LogWarning($"<color=#00b7ff>[EOSTransport]</color> {msg}");
#else
            Debug.LogWarning($"[EOSTransport] {msg}");
#endif
        }

        internal static void LogError(string msg)
        {
            if (TLevel is TransportLogLevel.None) return;

#if UNITY_EDITOR
            Debug.LogError($"<color=#00b7ff>[EOSTransport]</color> {msg}");
#else
            Debug.LogError($"[EOSTransport] {msg}");
#endif
        }
    }

    public enum TransportLogLevel { None, Info, Warning, Error }
}
