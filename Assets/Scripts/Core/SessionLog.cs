using System;
using System.Collections.Generic;
using UnityEngine;

namespace MedSimXR.Core
{
    [Serializable]
    public class LogEvent
    {
        public float t;
        public string utc;
        public string type;
        public string objectId;
        public string detail;
    }

    [Serializable]
    public class LogEventList { public List<LogEvent> events = new List<LogEvent>(); }

    /// <summary>Append-only record of learner interactions for one session. Stores no personal data.</summary>
    public static class SessionLog
    {
        public static readonly List<LogEvent> Events = new List<LogEvent>();
        public static event Action<LogEvent> OnEvent;

        public static void Clear() { Events.Clear(); }

        public static LogEvent Log(string type, string objectId = "", string detail = "")
        {
            var e = new LogEvent
            {
                t = Time.realtimeSinceStartup,
                utc = DateTime.UtcNow.ToString("o"),
                type = type,
                objectId = objectId ?? "",
                detail = detail ?? ""
            };
            Events.Add(e);
            OnEvent?.Invoke(e);
            return e;
        }

        public static int Count(string type)
        {
            int n = 0;
            foreach (var e in Events) if (e.type == type) n++;
            return n;
        }

        public static string ToJson()
        {
            return JsonUtility.ToJson(new LogEventList { events = new List<LogEvent>(Events) }, true);
        }
    }
}
