using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MedSimXR.Training;
using UnityEngine;

namespace MedSimXR.Assessment
{
    [Serializable]
    public class SessionReport
    {
        public string scenarioName;
        public string startedUtc;
        public string learnerId = "local-learner";   // no personal data is stored
        public int tasksTotal;
        public int tasksCompleted;
        public int totalErrors;
        public int totalHints;
        public float durationSeconds;
        public float score;
        public bool passed;
        public float passThreshold;
        public string disclaimer = "Prototype scoring. Weights and thresholds are provisional educational design choices, not clinically validated standards. Content has not been clinically reviewed.";
        public List<QuestionResult> tasks = new List<QuestionResult>();
        public List<string> feedback = new List<string>();
        public List<string> reviewAreas = new List<string>();

        public static SessionReport Build(string scenario, DateTime startedUtc, float duration,
                                          IList<QuestionResult> results, ScoreBreakdown score, float passThreshold,
                                          Func<string, string> nameOf)
        {
            var r = new SessionReport
            {
                scenarioName = scenario,
                startedUtc = startedUtc.ToString("o"),
                durationSeconds = duration,
                score = score.Total,
                passed = score.Passed,
                passThreshold = passThreshold,
                tasksTotal = results.Count,
                feedback = new List<string>(score.Feedback)
            };
            foreach (var q in results)
            {
                r.tasks.Add(q);
                if (q.correct) r.tasksCompleted++;
                r.totalErrors += q.wrongAttempts;
                r.totalHints += q.hintsUsed;
            }
            foreach (var id in score.ReviewObjectIds) r.reviewAreas.Add(nameOf != null ? nameOf(id) : id);
            return r;
        }

        public string ToJson() { return JsonUtility.ToJson(this, true); }

        static string Csv(string s)
        {
            if (s == null) return "";
            if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0) return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }

        public string ToCsv()
        {
            var sb = new StringBuilder();
            sb.AppendLine("scenario,started_utc,score,passed,pass_threshold,duration_s");
            sb.AppendLine(string.Join(",", new[] {
                Csv(scenarioName), Csv(startedUtc), score.ToString("F1", System.Globalization.CultureInfo.InvariantCulture),
                passed ? "true" : "false", passThreshold.ToString("F0", System.Globalization.CultureInfo.InvariantCulture),
                durationSeconds.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) }));
            sb.AppendLine();
            sb.AppendLine("question_id,object_id,correct,revealed,wrong_attempts,hints_used,seconds,target_seconds");
            foreach (var t in tasks)
            {
                sb.AppendLine(string.Join(",", new[] {
                    Csv(t.questionId), Csv(t.correctObjectId), t.correct ? "true" : "false", t.revealed ? "true" : "false",
                    t.wrongAttempts.ToString(), t.hintsUsed.ToString(),
                    t.seconds.ToString("F1", System.Globalization.CultureInfo.InvariantCulture),
                    t.targetSeconds.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) }));
            }
            return sb.ToString();
        }

        /// <summary>Writes JSON and CSV under the given directory and returns the JSON path.</summary>
        public string Save(string directory)
        {
            Directory.CreateDirectory(directory);
            string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            string jsonPath = Path.Combine(directory, "session_" + stamp + ".json");
            File.WriteAllText(jsonPath, ToJson());
            File.WriteAllText(Path.Combine(directory, "session_" + stamp + ".csv"), ToCsv());
            return jsonPath;
        }
    }
}
