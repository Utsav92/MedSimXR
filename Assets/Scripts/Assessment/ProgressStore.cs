using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace MedSimXR.Assessment
{
    [Serializable]
    public class ModuleRecord
    {
        public string moduleId;
        public int attempts;
        public float bestScore;
        public ModuleReport latest;
    }

    [Serializable]
    public class ProgressData
    {
        public string note = "Local training progress. Synthetic prototype data, no personal information.";
        public List<ModuleRecord> modules = new List<ModuleRecord>();
    }

    /// <summary>
    /// Keeps the latest result and attempt count per module and combines them into an overall provisional score.
    /// Persisted as progress.json in the report directory (local file only).
    /// </summary>
    public class ProgressStore
    {
        public const string ModuleA = "A_equipment_identification";
        public const string ModuleB = "B_safe_handling";
        public const string ModuleC = "C_patient_monitoring";
        public const string ModuleD = "D_infection_prevention";
        public const string ModuleE = "E_ct_orientation";
        public const string ModuleF = "F_advanced_identification";
        public static readonly string[] AllModules = { ModuleA, ModuleB, ModuleC, ModuleD, ModuleE, ModuleF };

        static readonly Dictionary<string, string> ShortNames = new Dictionary<string, string>
        {
            { ModuleA, "A  Equipment identification" },
            { ModuleB, "B  Safe handling" },
            { ModuleC, "C  Patient monitoring" },
            { ModuleD, "D  Infection prevention" },
            { ModuleE, "E  CT orientation" },
            { ModuleF, "F  Advanced identification" }
        };

        static readonly string[] CategoryOrder = { "identification", "handling", "sequence", "knowledge", "efficiency" };

        readonly string _path;
        public ProgressData Data { get; private set; }

        public ProgressStore(string directoryOrNull)
        {
            _path = string.IsNullOrEmpty(directoryOrNull) ? null : Path.Combine(directoryOrNull, "progress.json");
            Data = new ProgressData();
            Load();
        }

        public static string NameOf(string moduleId)
        {
            string s;
            return ShortNames.TryGetValue(moduleId, out s) ? s : moduleId;
        }

        void Load()
        {
            if (_path == null || !File.Exists(_path)) return;
            try
            {
                var d = JsonUtility.FromJson<ProgressData>(File.ReadAllText(_path));
                if (d != null && d.modules != null) Data = d;
            }
            catch (Exception e) { Debug.LogWarning("Could not read progress file: " + e.Message); }
        }

        public void Save()
        {
            if (_path == null) return;
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            File.WriteAllText(_path, JsonUtility.ToJson(Data, true));
        }

        public ModuleRecord Get(string moduleId)
        {
            foreach (var m in Data.modules) if (m.moduleId == moduleId) return m;
            return null;
        }

        public void Record(ModuleReport report)
        {
            var rec = Get(report.moduleId);
            if (rec == null) { rec = new ModuleRecord { moduleId = report.moduleId }; Data.modules.Add(rec); }
            rec.attempts++;
            rec.bestScore = Mathf.Max(rec.bestScore, report.score);
            rec.latest = report;
        }

        public int AttemptedCount { get { int n = 0; foreach (var id in AllModules) if (Get(id) != null) n++; return n; } }

        /// <summary>
        /// Overall = renormalised rubric weights over the category averages of the latest attempt of every attempted module.
        /// Unattempted modules do not count against the learner, and are listed in the feedback.
        /// </summary>
        public ScoreBreakdown Overall(ScoringEngine scoring)
        {
            var b = new ScoreBreakdown();
            var sums = new Dictionary<string, float>();
            var counts = new Dictionary<string, int>();
            var missing = new List<string>();
            foreach (var id in AllModules)
            {
                var rec = Get(id);
                if (rec == null || rec.latest == null) { missing.Add(NameOf(id)); continue; }
                foreach (var c in rec.latest.categories)
                {
                    float s; int n;
                    sums.TryGetValue(c.category, out s); counts.TryGetValue(c.category, out n);
                    sums[c.category] = s + c.value;
                    counts[c.category] = n + 1;
                }
                foreach (var area in rec.latest.reviewAreas) if (!b.ReviewObjectIds.Contains(area)) b.ReviewObjectIds.Add(area);
            }
            foreach (var kv in sums) b.Categories[kv.Key] = kv.Value / counts[kv.Key];
            scoring.Compose(b);
            if (missing.Count > 0) b.Feedback.Add("Not yet attempted: " + string.Join(", ", missing) + ".");
            if (b.Categories.Count == 0) b.Feedback.Add("No modules completed yet.");
            return b;
        }

        public string DashboardText(ScoringEngine scoring)
        {
            var o = Overall(scoring);
            var sb = new StringBuilder();
            if (AttemptedCount == 0)
            {
                sb.AppendLine("No modules completed yet.");
                sb.AppendLine("Open the training menu (Tab) and complete a module to see progress here.");
                return sb.ToString();
            }
            sb.AppendLine("OVERALL (provisional): " + o.Total.ToString("F0") + " / 100   " + (o.Passed ? "PASS" : "not yet passed")
                          + "   (threshold " + scoring.PassThreshold.ToString("F0") + ", " + AttemptedCount + " of " + AllModules.Length + " modules)");
            sb.AppendLine();
            sb.AppendLine("Module                          Latest   Best   Tries");
            foreach (var id in AllModules)
            {
                var rec = Get(id);
                string name = NameOf(id).PadRight(30);
                if (rec == null || rec.latest == null) sb.AppendLine(name + "  --      --     0");
                else sb.AppendLine(name + "  " + rec.latest.score.ToString("F0").PadLeft(3) + (rec.latest.passed ? " ok" : "   ") + "  " + rec.bestScore.ToString("F0").PadLeft(3) + "    " + rec.attempts);
            }
            sb.AppendLine();
            var cats = new List<string>();
            foreach (var c in CategoryOrder) if (o.Categories.ContainsKey(c)) cats.Add(c + " " + o.Categories[c].ToString("F0"));
            sb.AppendLine("Categories: " + string.Join("   ", cats));
            if (o.ReviewObjectIds.Count > 0) sb.AppendLine("Suggested review: " + string.Join(", ", o.ReviewObjectIds) + ".");
            foreach (var f in o.Feedback) sb.AppendLine(f);
            sb.AppendLine();
            sb.AppendLine("Weights and thresholds are provisional and not clinically validated.");
            return sb.ToString();
        }
    }
}
