using System;
using System.Collections.Generic;
using MedSimXR.Core;
using MedSimXR.Training;
using UnityEngine;

namespace MedSimXR.Assessment
{
    [Serializable]
    public class RubricWeights
    {
        public float identification = 25f;
        public float handling = 25f;
        public float sequence = 30f;
        public float efficiency = 10f;
        public float knowledge = 10f;
    }

    [Serializable]
    public class Rubric
    {
        public string rubricId;
        public string reviewStatus;
        public float passThreshold = 70f;
        public float wrongAttemptPenalty = 0.25f;
        public float hintPenalty = 0.1f;
        public float monitoringReportPenalty = 10f;   // points off per false alarm / wrong-parameter report
        public RubricWeights weights = new RubricWeights();

        public static Rubric LoadDefault() { return DataLoader.Load<Rubric>("default_rubric"); }
    }

    public class ScoreBreakdown
    {
        // Each category is 0..100, or null when that category was not assessed in this session.
        public readonly Dictionary<string, float> Categories = new Dictionary<string, float>();
        public float Total;
        public bool Passed;
        public List<string> Feedback = new List<string>();
        public List<string> ReviewObjectIds = new List<string>();
    }

    /// <summary>
    /// Configurable scoring. Weights are provisional educational design choices, not clinically validated standards.
    /// Only categories actually assessed contribute; their weights are renormalised so the total stays 0..100.
    /// </summary>
    public class ScoringEngine
    {
        readonly Rubric _rubric;
        public ScoringEngine(Rubric rubric) { _rubric = rubric; }

        public float ItemScore(QuestionResult r)
        {
            if (r.revealed || !r.correct) return 0f;
            return Mathf.Clamp01(1f - r.wrongAttempts * _rubric.wrongAttemptPenalty - r.hintsUsed * _rubric.hintPenalty);
        }

        public static float EfficiencyScore(QuestionResult r)
        {
            if (r.revealed || !r.correct) return 0f;
            if (r.seconds <= r.targetSeconds || r.seconds <= 0f) return 1f;
            return Mathf.Clamp01(r.targetSeconds / r.seconds);
        }

        public ScoreBreakdown ScoreIdentification(IList<QuestionResult> results, Func<string, string> nameOf = null)
        {
            var b = new ScoreBreakdown();
            if (results == null || results.Count == 0) return b;

            float idSum = 0f, effSum = 0f;
            foreach (var r in results)
            {
                idSum += ItemScore(r);
                effSum += EfficiencyScore(r);
                string name = nameOf != null ? nameOf(r.correctObjectId) : r.correctObjectId;
                if (r.revealed)
                {
                    b.Feedback.Add("Not identified: " + name + ".");
                    b.ReviewObjectIds.Add(r.correctObjectId);
                }
                else if (r.wrongAttempts > 0 || r.hintsUsed > 0)
                {
                    b.Feedback.Add(name + ": needed " + r.wrongAttempts + " extra attempt(s) and " + r.hintsUsed + " hint(s).");
                    b.ReviewObjectIds.Add(r.correctObjectId);
                }
            }
            b.Categories["identification"] = 100f * idSum / results.Count;
            b.Categories["efficiency"] = 100f * effSum / results.Count;
            Compose(b);
            if (b.Feedback.Count == 0) b.Feedback.Add("All items identified first time without hints.");
            return b;
        }

        static float TimeScore(float seconds, float target)
        {
            if (target <= 0f || seconds <= target) return 1f;
            return Mathf.Clamp01(target / seconds);
        }

        static string ShortPrompt(string p)
        {
            if (string.IsNullOrEmpty(p)) return "";
            int cut = p.IndexOf(':');
            return cut > 0 && cut < 60 ? p.Substring(0, cut) : (p.Length > 60 ? p.Substring(0, 57) + "..." : p);
        }

        /// <summary>Ordered procedure (safe handling / infection prevention). Feeds the procedure's own rubric category plus efficiency.</summary>
        public ScoreBreakdown ScoreProcedure(ProcedureDef def, ProcedureResult r)
        {
            var b = new ScoreBreakdown();
            if (def == null || r == null || r.steps.Count == 0) return b;
            float sum = 0f;
            for (int i = 0; i < r.steps.Count; i++)
            {
                var st = r.steps[i];
                float s = st.done ? Mathf.Clamp01(1f - st.errors * _rubric.wrongAttemptPenalty - st.hintsUsed * _rubric.hintPenalty) : 0f;
                sum += s;
                string label = i < def.steps.Length ? ShortPrompt(def.steps[i].prompt) : st.stepId;
                if (!st.done) { b.Feedback.Add("Not completed: " + label + "."); b.ReviewObjectIds.Add(def.steps[i].objectId); }
                else if (st.errors > 0 || st.hintsUsed > 0)
                {
                    b.Feedback.Add(label + ": " + st.errors + " error(s), " + st.hintsUsed + " hint(s).");
                    b.ReviewObjectIds.Add(def.steps[i].objectId);
                }
            }
            string cat = string.IsNullOrEmpty(def.category) ? "sequence" : def.category;
            b.Categories[cat] = 100f * sum / r.steps.Count;
            b.Categories["efficiency"] = r.stepsDone < r.stepsTotal ? 0f : 100f * TimeScore(r.seconds, r.targetSeconds);
            if (r.unsafeActions > 0) b.Feedback.Add(r.unsafeActions + " unsafe action(s) were recorded.");
            Compose(b);
            if (b.Feedback.Count == 0) b.Feedback.Add("Every step was completed in order without errors or hints.");
            return b;
        }

        /// <summary>Patient monitoring: detection accuracy (knowledge) and reaction time (efficiency).</summary>
        public ScoreBreakdown ScoreMonitoring(MonitoringResult r)
        {
            var b = new ScoreBreakdown();
            if (r == null || r.scenarios.Count == 0) return b;
            float itemSum = 0f; int items = 0; int extra = 0;
            foreach (var sc in r.scenarios)
            {
                if (!sc.hadAbnormal)
                {
                    itemSum += sc.falseAlarms == 0 ? 1f : Mathf.Max(0f, 1f - 0.5f * sc.falseAlarms);
                    items++;
                    if (sc.falseAlarms > 0) b.Feedback.Add("False alarm in " + sc.scenarioId + ": " + sc.falseAlarms + " report(s) when nothing was out of range.");
                }
                else
                {
                    extra += sc.falseAlarms;
                    if (sc.falseAlarms > 0) b.Feedback.Add("False alarm in " + sc.scenarioId + ": " + sc.falseAlarms + " report(s) while every value was in range.");
                }
                extra += sc.wrongParameter;
                if (sc.wrongParameter > 0) b.Feedback.Add("Wrong parameter named " + sc.wrongParameter + " time(s) in " + sc.scenarioId + ".");
            }
            float effSum = 0f; int effItems = 0;
            foreach (var e in r.events)
            {
                itemSum += e.detected ? 1f : 0f;
                items++;
                effItems++;
                if (!e.detected)
                {
                    b.Feedback.Add("Missed: " + e.param + " went out of range at " + e.onsetSeconds.ToString("F0") + " s in " + e.scenarioId + ".");
                    b.ReviewObjectIds.Add("monitor_patient_a01");
                }
                else
                {
                    effSum += TimeScore(e.latencySeconds, e.targetLatencySeconds);
                    if (e.latencySeconds > e.targetLatencySeconds)
                        b.Feedback.Add("Slow: " + e.param + " in " + e.scenarioId + " reported after " + e.latencySeconds.ToString("F0") + " s (target " + e.targetLatencySeconds.ToString("F0") + " s).");
                }
            }
            float know = items > 0 ? 100f * itemSum / items : 0f;
            b.Categories["knowledge"] = Mathf.Clamp(know - extra * _rubric.monitoringReportPenalty, 0f, 100f);
            b.Categories["efficiency"] = effItems > 0 ? 100f * effSum / effItems : 100f;
            Compose(b);
            if (b.Feedback.Count == 0) b.Feedback.Add("All abnormal values were reported promptly with no false alarms.");
            return b;
        }

        /// <summary>Multiple-choice knowledge check.</summary>
        public ScoreBreakdown ScoreKnowledge(IList<KnowledgeResult> results, Func<string, string> labelOf = null)
        {
            var b = new ScoreBreakdown();
            if (results == null || results.Count == 0) return b;
            float sum = 0f, eff = 0f;
            foreach (var r in results)
            {
                float s = r.correct ? Mathf.Clamp01(1f - r.hintsUsed * _rubric.hintPenalty) : 0f;
                sum += s;
                eff += r.correct ? TimeScore(r.seconds, r.targetSeconds) : 0f;
                string label = labelOf != null ? labelOf(r.questionId) : r.questionId;
                if (!r.correct) { b.Feedback.Add("Incorrect: " + label + "."); b.ReviewObjectIds.Add(r.questionId); }
                else if (r.hintsUsed > 0) b.Feedback.Add(label + ": correct after " + r.hintsUsed + " hint(s).");
            }
            b.Categories["knowledge"] = 100f * sum / results.Count;
            b.Categories["efficiency"] = 100f * eff / results.Count;
            Compose(b);
            if (b.Feedback.Count == 0) b.Feedback.Add("All questions answered correctly without hints.");
            return b;
        }

        /// <summary>Combines whichever categories are present using renormalised rubric weights.</summary>
        public void Compose(ScoreBreakdown b)
        {
            float wSum = 0f, acc = 0f;
            foreach (var kv in b.Categories)
            {
                float w = WeightOf(kv.Key);
                wSum += w;
                acc += w * kv.Value;
            }
            b.Total = wSum > 0f ? acc / wSum : 0f;
            b.Passed = b.Total >= _rubric.passThreshold;
        }

        float WeightOf(string category)
        {
            switch (category)
            {
                case "identification": return _rubric.weights.identification;
                case "handling": return _rubric.weights.handling;
                case "sequence": return _rubric.weights.sequence;
                case "efficiency": return _rubric.weights.efficiency;
                case "knowledge": return _rubric.weights.knowledge;
                default: return 0f;
            }
        }

        public float PassThreshold { get { return _rubric.passThreshold; } }
    }
}
