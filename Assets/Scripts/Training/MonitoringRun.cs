using System;
using System.Collections.Generic;
using UnityEngine;

namespace MedSimXR.Training
{
    [Serializable]
    public class VitalRange
    {
        public string param;    // hr | spo2 | rr | sys | dia | temp
        public string label;
        public string unit;
        public float low;
        public float high;
    }

    [Serializable]
    public class VitalsKeyframe
    {
        public float t;
        public float hr, spo2, rr, sys, dia, temp;
    }

    [Serializable]
    public class MonitoringScenario
    {
        public string id;
        public string title;
        public string briefing;
        public float durationSeconds = 60f;
        public float targetLatencySeconds = 15f;
        public VitalsKeyframe[] keyframes;
    }

    [Serializable]
    public class MonitoringFile
    {
        public string moduleId;
        public string title;
        public string reviewStatus;
        public VitalRange[] ranges;
        public MonitoringScenario[] scenarios;
    }

    /// <summary>One synthetic set of vital signs.</summary>
    public struct Vitals
    {
        public float hr, spo2, rr, sys, dia, temp;

        public float Get(string vital)
        {
            switch (vital)
            {
                case "hr": return hr;
                case "spo2": return spo2;
                case "rr": return rr;
                case "sys": return sys;
                case "dia": return dia;
                case "temp": return temp;
                default: return 0f;
            }
        }
    }

    [Serializable]
    public class MonitoringEventResult
    {
        public string scenarioId;
        public string param;            // hr | spo2 | rr | bp | temp
        public float onsetSeconds;      // when the value first went out of range (scenario time)
        public bool detected;
        public float latencySeconds;
        public float targetLatencySeconds;
    }

    [Serializable]
    public class MonitoringScenarioResult
    {
        public string scenarioId;
        public bool hadAbnormal;
        public int falseAlarms;
        public int wrongParameter;
    }

    [Serializable]
    public class MonitoringResult
    {
        public string moduleId;
        public float seconds;
        public List<MonitoringEventResult> events = new List<MonitoringEventResult>();
        public List<MonitoringScenarioResult> scenarios = new List<MonitoringScenarioResult>();
    }

    public enum ReportOutcome { Ignored, Correct, Duplicate, WrongParameter, FalseAlarm }

    /// <summary>
    /// Runs the scenarios back to back. All values are synthetic. Abnormality is decided from the noise-free
    /// interpolated values so that display noise can never create or hide an event.
    /// </summary>
    public class MonitoringRun
    {
        public static readonly string[] ParamKeys = { "hr", "spo2", "rr", "bp", "temp" };

        readonly MonitoringFile _file;
        readonly Dictionary<string, VitalRange> _ranges = new Dictionary<string, VitalRange>();
        readonly List<List<MonitoringEventResult>> _events = new List<List<MonitoringEventResult>>();
        float _start;
        int _scenario;

        public MonitoringResult Result { get; private set; }
        public bool Started { get; private set; }
        public bool Complete { get; private set; }
        public int ScenarioIndex { get { return _scenario; } }
        public int ScenarioCount { get { return _file.scenarios.Length; } }
        public MonitoringScenario CurrentScenario { get { return Started && !Complete ? _file.scenarios[_scenario] : null; } }
        public float ScenarioElapsed { get; private set; }
        public float TotalDuration { get; private set; }

        public MonitoringRun(MonitoringFile file)
        {
            _file = file;
            foreach (var r in file.ranges) _ranges[r.param] = r;
            foreach (var s in file.scenarios) TotalDuration += s.durationSeconds;
        }

        public VitalRange RangeOf(string vital) { VitalRange r; return _ranges.TryGetValue(vital, out r) ? r : null; }

        // ---------- vitals model ----------
        public static Vitals TrueVitals(MonitoringScenario s, float t)
        {
            var k = s.keyframes;
            if (t <= k[0].t) return FromKey(k[0]);
            for (int i = 1; i < k.Length; i++)
            {
                if (t <= k[i].t)
                {
                    float u = (t - k[i - 1].t) / Mathf.Max(0.0001f, k[i].t - k[i - 1].t);
                    return Lerp(k[i - 1], k[i], u);
                }
            }
            return FromKey(k[k.Length - 1]);
        }

        static Vitals FromKey(VitalsKeyframe k) { return new Vitals { hr = k.hr, spo2 = k.spo2, rr = k.rr, sys = k.sys, dia = k.dia, temp = k.temp }; }

        static Vitals Lerp(VitalsKeyframe a, VitalsKeyframe b, float u)
        {
            return new Vitals
            {
                hr = Mathf.Lerp(a.hr, b.hr, u), spo2 = Mathf.Lerp(a.spo2, b.spo2, u), rr = Mathf.Lerp(a.rr, b.rr, u),
                sys = Mathf.Lerp(a.sys, b.sys, u), dia = Mathf.Lerp(a.dia, b.dia, u), temp = Mathf.Lerp(a.temp, b.temp, u)
            };
        }

        /// <summary>What the monitor shows: the true value plus a small deterministic wobble.</summary>
        public static Vitals DisplayVitals(MonitoringScenario s, float t)
        {
            var v = TrueVitals(s, t);
            v.hr += 1.5f * Mathf.Sin(t * 1.7f);
            v.spo2 += 0.3f * Mathf.Sin(t * 0.9f + 1f);
            v.rr += 0.5f * Mathf.Sin(t * 0.6f + 2f);
            v.sys += 2f * Mathf.Sin(t * 0.8f + 0.5f);
            v.dia += 1.5f * Mathf.Sin(t * 0.7f + 1.5f);
            v.temp += 0.03f * Mathf.Sin(t * 0.4f);
            return v;
        }

        public bool IsVitalOut(string vital, Vitals v)
        {
            VitalRange r;
            if (!_ranges.TryGetValue(vital, out r)) return false;
            float x = v.Get(vital);
            return x < r.low || x > r.high;
        }

        public bool IsParamAbnormal(string param, Vitals v)
        {
            if (param == "bp") return IsVitalOut("sys", v) || IsVitalOut("dia", v);
            return IsVitalOut(param, v);
        }

        /// <summary>Scenario-time at which each parameter first leaves its range (0.25 s resolution), or negative if never.</summary>
        public float FindOnset(MonitoringScenario s, string param)
        {
            for (float t = 0f; t <= s.durationSeconds; t += 0.25f)
                if (IsParamAbnormal(param, TrueVitals(s, t))) return t;
            return -1f;
        }

        // ---------- run ----------
        public void Begin(float now)
        {
            Started = true;
            Complete = false;
            _start = now;
            _scenario = 0;
            ScenarioElapsed = 0f;
            Result = new MonitoringResult { moduleId = _file.moduleId };
            _events.Clear();
            foreach (var s in _file.scenarios)
            {
                var list = new List<MonitoringEventResult>();
                foreach (var p in ParamKeys)
                {
                    float onset = FindOnset(s, p);
                    if (onset < 0f) continue;
                    var e = new MonitoringEventResult { scenarioId = s.id, param = p, onsetSeconds = onset, targetLatencySeconds = s.targetLatencySeconds };
                    list.Add(e);
                    Result.events.Add(e);
                }
                _events.Add(list);
                Result.scenarios.Add(new MonitoringScenarioResult { scenarioId = s.id, hadAbnormal = list.Count > 0 });
            }
        }

        public void Tick(float now)
        {
            if (!Started || Complete) return;
            float elapsed = now - _start;
            float acc = 0f;
            for (int i = 0; i < _file.scenarios.Length; i++)
            {
                float d = _file.scenarios[i].durationSeconds;
                if (elapsed < acc + d) { _scenario = i; ScenarioElapsed = elapsed - acc; return; }
                acc += d;
            }
            Complete = true;
            Result.seconds = TotalDuration;
            ScenarioElapsed = _file.scenarios[_file.scenarios.Length - 1].durationSeconds;
            _scenario = _file.scenarios.Length - 1;
        }

        public Vitals CurrentDisplay()
        {
            var s = _file.scenarios[_scenario];
            return DisplayVitals(s, ScenarioElapsed);
        }

        public Vitals CurrentTrue()
        {
            var s = _file.scenarios[_scenario];
            return TrueVitals(s, ScenarioElapsed);
        }

        /// <summary>The learner says this parameter is out of range right now.</summary>
        public ReportOutcome Report(string param, float now)
        {
            Tick(now);
            if (!Started || Complete) return ReportOutcome.Ignored;
            var s = _file.scenarios[_scenario];
            var truth = TrueVitals(s, ScenarioElapsed);
            var sr = Result.scenarios[_scenario];

            if (IsParamAbnormal(param, truth))
            {
                foreach (var e in _events[_scenario])
                {
                    if (e.param != param) continue;
                    if (e.detected) return ReportOutcome.Duplicate;
                    e.detected = true;
                    e.latencySeconds = Mathf.Max(0f, ScenarioElapsed - e.onsetSeconds);
                    return ReportOutcome.Correct;
                }
            }

            foreach (var p in ParamKeys)
            {
                if (p == param || !IsParamAbnormal(p, truth)) continue;
                sr.wrongParameter++;
                return ReportOutcome.WrongParameter;
            }
            sr.falseAlarms++;
            return ReportOutcome.FalseAlarm;
        }

        public int TotalEvents { get { int n = 0; foreach (var l in _events) n += l.Count; return n; } }
        public int DetectedEvents { get { int n = 0; foreach (var l in _events) foreach (var e in l) if (e.detected) n++; return n; } }
    }
}
