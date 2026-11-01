using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using MedSimXR.Training;
using UnityEngine;

namespace MedSimXR.Assessment
{
    [Serializable]
    public class CategoryScore
    {
        public string category;
        public float value;
    }

    [Serializable]
    public class ReportItem
    {
        public string id;
        public string label;
        public string outcome;      // done | not_done | correct | incorrect | detected | missed | identified | revealed
        public int errors;
        public int hints;
        public float seconds;
    }

    /// <summary>Result of one run of any training module. Stores no personal data.</summary>
    [Serializable]
    public class ModuleReport
    {
        public string moduleId;
        public string title;
        public string startedUtc;
        public string learnerId = "local-learner";
        public float durationSeconds;
        public float score;
        public bool passed;
        public float passThreshold;
        public List<CategoryScore> categories = new List<CategoryScore>();
        public List<ReportItem> items = new List<ReportItem>();
        public List<string> feedback = new List<string>();
        public List<string> reviewAreas = new List<string>();
        public string disclaimer = "Prototype scoring. Weights and thresholds are provisional educational design choices, not clinically validated standards. Content has not been clinically reviewed.";

        public float CategoryValue(string category, float fallback = -1f)
        {
            foreach (var c in categories) if (c.category == category) return c.value;
            return fallback;
        }

        static ModuleReport Base(string moduleId, string title, DateTime started, float duration, ScoreBreakdown b, float pass)
        {
            var r = new ModuleReport
            {
                moduleId = moduleId, title = title, startedUtc = started.ToString("o"), durationSeconds = duration,
                score = b.Total, passed = b.Passed, passThreshold = pass, feedback = new List<string>(b.Feedback)
            };
            foreach (var kv in b.Categories) r.categories.Add(new CategoryScore { category = kv.Key, value = kv.Value });
            return r;
        }

        public static ModuleReport FromIdentification(string moduleId, SessionReport s, ScoreBreakdown b)
        {
            var r = Base(moduleId, s.scenarioName, DateTime.Parse(s.startedUtc, null, DateTimeStyles.RoundtripKind), s.durationSeconds, b, s.passThreshold);
            foreach (var t in s.tasks)
                r.items.Add(new ReportItem { id = t.questionId, label = t.correctObjectId, outcome = t.revealed ? "revealed" : (t.correct ? "identified" : "incorrect"),
                                             errors = t.wrongAttempts, hints = t.hintsUsed, seconds = t.seconds });
            r.reviewAreas = new List<string>(s.reviewAreas);
            return r;
        }

        public static ModuleReport FromProcedure(ProcedureDef def, ProcedureResult res, ScoreBreakdown b, float pass, DateTime started, Func<string, string> nameOf)
        {
            var r = Base(def.moduleId, def.title, started, res.seconds, b, pass);
            for (int i = 0; i < res.steps.Count; i++)
            {
                var st = res.steps[i];
                r.items.Add(new ReportItem { id = st.stepId, label = i < def.steps.Length ? def.steps[i].prompt : st.stepId, outcome = st.done ? "done" : "not_done",
                                             errors = st.errors, hints = st.hintsUsed, seconds = st.seconds });
            }
            foreach (var id in b.ReviewObjectIds) r.reviewAreas.Add(nameOf != null ? nameOf(id) : id);
            return r;
        }

        public static ModuleReport FromMonitoring(MonitoringFile f, MonitoringResult res, ScoreBreakdown b, float pass, DateTime started, Func<string, string> nameOf)
        {
            var r = Base(f.moduleId, f.title, started, res.seconds, b, pass);
            foreach (var e in res.events)
                r.items.Add(new ReportItem { id = e.scenarioId + ":" + e.param, label = e.param + " out of range at " + e.onsetSeconds.ToString("F0", CultureInfo.InvariantCulture) + " s",
                                             outcome = e.detected ? "detected" : "missed", errors = 0, hints = 0, seconds = e.detected ? e.latencySeconds : 0f });
            foreach (var sc in res.scenarios)
                if (sc.falseAlarms + sc.wrongParameter > 0)
                    r.items.Add(new ReportItem { id = sc.scenarioId + ":reports", label = "false alarms / wrong parameter", outcome = "incorrect", errors = sc.falseAlarms + sc.wrongParameter });
            foreach (var id in b.ReviewObjectIds) if (!r.reviewAreas.Contains(nameOf != null ? nameOf(id) : id)) r.reviewAreas.Add(nameOf != null ? nameOf(id) : id);
            return r;
        }

        public static ModuleReport FromKnowledge(KnowledgeSet set, IList<KnowledgeResult> res, ScoreBreakdown b, float pass, DateTime started, float duration)
        {
            var r = Base(set.moduleId, set.title, started, duration, b, pass);
            foreach (var q in res)
            {
                string label = q.questionId;
                foreach (var sq in set.questions) if (sq.id == q.questionId) { label = sq.prompt; break; }
                r.items.Add(new ReportItem { id = q.questionId, label = label, outcome = q.correct ? "correct" : "incorrect", errors = q.correct ? 0 : 1, hints = q.hintsUsed, seconds = q.seconds });
                if (!q.correct) r.reviewAreas.Add(label);
            }
            return r;
        }

        public string ToJson() { return JsonUtility.ToJson(this, true); }

        static string Csv(string s)
        {
            if (s == null) return "";
            if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0) return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }

        static string F(float v, string fmt = "F1") { return v.ToString(fmt, CultureInfo.InvariantCulture); }

        public string ToCsv()
        {
            var sb = new StringBuilder();
            sb.AppendLine("module_id,title,started_utc,score,passed,pass_threshold,duration_s");
            sb.AppendLine(string.Join(",", new[] { Csv(moduleId), Csv(title), Csv(startedUtc), F(score), passed ? "true" : "false", F(passThreshold, "F0"), F(durationSeconds) }));
            sb.AppendLine();
            sb.AppendLine("category,score");
            foreach (var c in categories) sb.AppendLine(Csv(c.category) + "," + F(c.value));
            sb.AppendLine();
            sb.AppendLine("item_id,label,outcome,errors,hints,seconds");
            foreach (var i in items)
                sb.AppendLine(string.Join(",", new[] { Csv(i.id), Csv(i.label), Csv(i.outcome), i.errors.ToString(), i.hints.ToString(), F(i.seconds) }));
            return sb.ToString();
        }

        /// <summary>Writes JSON and CSV under the directory and returns the JSON path.</summary>
        public string Save(string directory)
        {
            Directory.CreateDirectory(directory);
            string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
            string baseName = "module_" + moduleId + "_" + stamp;
            string jsonPath = Path.Combine(directory, baseName + ".json");
            File.WriteAllText(jsonPath, ToJson());
            File.WriteAllText(Path.Combine(directory, baseName + ".csv"), ToCsv());
            return jsonPath;
        }
    }
}
