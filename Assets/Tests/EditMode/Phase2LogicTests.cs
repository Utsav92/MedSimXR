using System;
using System.Collections.Generic;
using System.IO;
using MedSimXR.Assessment;
using MedSimXR.Core;
using MedSimXR.Equipment;
using MedSimXR.Training;
using NUnit.Framework;
using UnityEngine;

namespace MedSimXR.Tests
{
    /// <summary>Pure-logic and data tests for the Phase 2 modules (B, C, D, E), assessment and progress.</summary>
    public class Phase2LogicTests
    {
        static ProcedureFile Procs() { return DataLoader.Load<ProcedureFile>("procedures"); }
        static MonitoringFile Mon() { return DataLoader.Load<MonitoringFile>("monitoring_scenarios"); }
        static KnowledgeSet Know() { return DataLoader.Load<KnowledgeSet>("ct_orientation_questions"); }
        static ScoringEngine Engine() { return new ScoringEngine(Rubric.LoadDefault()); }

        // =============== data integrity ===============
        [Test]
        public void ModuleIds_MatchProgressConstants()
        {
            Assert.IsNotNull(Procs().Find(ProgressStore.ModuleB), "procedures.json needs module B");
            Assert.IsNotNull(Procs().Find(ProgressStore.ModuleD), "procedures.json needs module D");
            Assert.AreEqual(ProgressStore.ModuleC, Mon().moduleId);
            Assert.AreEqual(ProgressStore.ModuleE, Know().moduleId);
            Assert.AreEqual(ProgressStore.ModuleA, DataLoader.Load<QuizSet>("identification_questions").moduleId);
        }

        [Test]
        public void ProcedureData_StepsAreCompleteAndReferenceRealEquipment()
        {
            var reg = EquipmentRegistry.LoadDefault();
            var triggers = new HashSet<string> { "select", "inspect", "grab", "placed", "reset" };
            foreach (var p in Procs().procedures)
            {
                Assert.GreaterOrEqual(p.steps.Length, 3, p.moduleId);
                Assert.IsTrue(p.category == "handling" || p.category == "sequence", p.moduleId + " category");
                var ids = new HashSet<string>();
                foreach (var s in p.steps)
                {
                    Assert.IsTrue(ids.Add(s.id), "duplicate step id " + s.id);
                    Assert.IsFalse(string.IsNullOrEmpty(s.prompt), s.id + " prompt");
                    Assert.IsFalse(string.IsNullOrEmpty(s.hint), s.id + " hint");
                    Assert.IsFalse(string.IsNullOrEmpty(s.explanation), s.id + " explanation");
                    Assert.IsTrue(triggers.Contains(s.trigger), s.id + " trigger " + s.trigger);
                    EquipmentEntry e;
                    Assert.IsTrue(reg.TryGet(s.objectId, out e), s.id + " references unknown object " + s.objectId);
                }
            }
        }

        [Test]
        public void MonitoringData_RangesAndKeyframesAreSane()
        {
            var f = Mon();
            foreach (var v in new[] { "hr", "spo2", "rr", "sys", "dia", "temp" })
            {
                bool found = false;
                foreach (var r in f.ranges) if (r.param == v) { found = true; Assert.Less(r.low, r.high, v); }
                Assert.IsTrue(found, "range missing for " + v);
            }
            foreach (var s in f.scenarios)
            {
                Assert.GreaterOrEqual(s.keyframes.Length, 2, s.id);
                Assert.AreEqual(0f, s.keyframes[0].t, s.id + " must start at t=0");
                Assert.GreaterOrEqual(s.keyframes[s.keyframes.Length - 1].t, s.durationSeconds - 0.01f, s.id + " keyframes must cover the duration");
                for (int i = 1; i < s.keyframes.Length; i++) Assert.Greater(s.keyframes[i].t, s.keyframes[i - 1].t, s.id + " keyframes must be ordered");
            }
        }

        [Test]
        public void MonitoringData_DerivedOnsetsMatchTheDesign()
        {
            var run = new MonitoringRun(Mon());
            run.Begin(0f);
            var byScenario = new Dictionary<string, List<MonitoringEventResult>>();
            foreach (var e in run.Result.events)
            {
                if (!byScenario.ContainsKey(e.scenarioId)) byScenario[e.scenarioId] = new List<MonitoringEventResult>();
                byScenario[e.scenarioId].Add(e);
            }
            Assert.IsFalse(byScenario.ContainsKey("mon_stable"), "stable scenario must have no abnormal values");
            Assert.AreEqual(1, byScenario["mon_spo2_drop"].Count);
            Assert.AreEqual("spo2", byScenario["mon_spo2_drop"][0].param);
            Assert.AreEqual(29.25f, byScenario["mon_spo2_drop"][0].onsetSeconds, 1.0f);
            Assert.AreEqual(2, byScenario["mon_hr_temp"].Count);
            foreach (var e in byScenario["mon_hr_temp"])
            {
                if (e.param == "hr") Assert.AreEqual(24f, e.onsetSeconds, 1.0f);
                else if (e.param == "temp") Assert.AreEqual(45.25f, e.onsetSeconds, 1.0f);
                else Assert.Fail("unexpected abnormal parameter " + e.param);
            }
        }

        [Test]
        public void KnowledgeData_QuestionsAreWellFormed()
        {
            var set = Know();
            Assert.GreaterOrEqual(set.questions.Length, 5);
            var ids = new HashSet<string>();
            foreach (var q in set.questions)
            {
                Assert.IsTrue(ids.Add(q.id), "duplicate " + q.id);
                Assert.IsFalse(string.IsNullOrEmpty(q.prompt), q.id);
                Assert.IsFalse(string.IsNullOrEmpty(q.explanation), q.id);
                Assert.IsFalse(string.IsNullOrEmpty(q.hint), q.id);
                Assert.GreaterOrEqual(q.options.Length, 3, q.id);
                Assert.LessOrEqual(q.options.Length, 4, q.id + " - keys 1-4 only");
                Assert.GreaterOrEqual(q.correctIndex, 0, q.id);
                Assert.Less(q.correctIndex, q.options.Length, q.id);
            }
        }

        [Test]
        public void KnowledgeData_AnswersAreNotAlwaysTheSamePosition()
        {
            var seen = new HashSet<int>();
            foreach (var q in Know().questions) seen.Add(q.correctIndex);
            Assert.GreaterOrEqual(seen.Count, 2, "correct answers should not all be in one position");
        }

        // =============== procedures (modules B and D) ===============
        static ProcedureRun NewRun(string moduleId, out float t)
        {
            var run = new ProcedureRun(Procs().Find(moduleId));
            t = 0f;
            run.Begin(t);
            return run;
        }

        [Test]
        public void Procedure_CorrectOrder_CompletesWithFullMarks()
        {
            float t;
            var run = NewRun(ProgressStore.ModuleD, out t);
            for (int i = 0; i < run.Total; i++)
            {
                var step = run.Current;
                t += 4f;
                var o = run.Observe(step.trigger, step.objectId, t);
                Assert.AreEqual(i == run.Total - 1 ? StepOutcome.Completed : StepOutcome.Correct, o, "step " + i);
            }
            Assert.IsTrue(run.Complete);
            Assert.AreEqual(run.Total, run.Result.stepsDone);
            var b = Engine().ScoreProcedure(run.Def, run.Result);
            Assert.AreEqual(100f, b.Categories["sequence"], 0.01f);
            Assert.AreEqual(100f, b.Categories["efficiency"], 0.01f);
            Assert.AreEqual(100f, b.Total, 0.01f);
            Assert.IsTrue(b.Passed);
        }

        [Test]
        public void Procedure_OutOfOrderStep_IsFlaggedAndCostsPoints()
        {
            float t;
            var run = NewRun(ProgressStore.ModuleD, out t);
            Assert.AreEqual(StepOutcome.OutOfOrder, run.Observe("select", "bed_hospital_standard_a01", 1f), "patient contact before hand hygiene");
            StringAssert.Contains("Out of sequence", run.LastMessage);
            Assert.AreEqual(0, run.Result.stepsDone, "an out-of-order action must not advance the procedure");
            t = 2f;
            foreach (var s in run.Def.steps) { t += 3f; run.Observe(s.trigger, s.objectId, t); }
            Assert.IsTrue(run.Complete);
            Assert.AreEqual(1, run.Result.outOfOrder);
            var b = Engine().ScoreProcedure(run.Def, run.Result);
            Assert.Less(b.Categories["sequence"], 100f);
            Assert.GreaterOrEqual(b.Categories["sequence"], 90f);
        }

        [Test]
        public void Procedure_WrongItem_IsAWrongAction()
        {
            float t;
            var run = NewRun(ProgressStore.ModuleD, out t);
            Assert.AreEqual(StepOutcome.WrongAction, run.Observe("select", "cabinet_for_drugs_a01", 1f));
            Assert.AreEqual(1, run.Result.wrongActions);
            Assert.AreEqual(1, run.Result.steps[0].errors);
        }

        [Test]
        public void Procedure_RepeatingACompletedStep_IsHarmless()
        {
            float t;
            var run = NewRun(ProgressStore.ModuleD, out t);
            Assert.AreEqual(StepOutcome.Correct, run.Observe("select", "washBasin_a01", 1f));
            Assert.AreEqual(StepOutcome.Ignored, run.Observe("select", "washBasin_a01", 2f), "hygiene again at step 2 is not an error");
            Assert.AreEqual(0, run.Result.outOfOrder);
            Assert.AreEqual(0, run.Result.wrongActions);
        }

        [Test]
        public void Procedure_UnrelatedEventTypes_AreIgnored()
        {
            float t;
            var run = NewRun(ProgressStore.ModuleB, out t);
            Assert.AreEqual(StepOutcome.Ignored, run.Observe("release", "box_corrugated_brown_b20", 1f));
            Assert.AreEqual(StepOutcome.Ignored, run.Observe("select", "bed_hospital_standard_a01", 1f), "select is not a trigger type in module B");
            Assert.AreEqual(StepOutcome.Ignored, run.Observe("inspect", "bed_hospital_standard_a01", 1f), "inspecting something harmless is not an error");
            Assert.AreEqual(0, run.Result.wrongActions);
        }

        [Test]
        public void Procedure_SafeHandling_UnsafeEventsAreRecorded()
        {
            float t;
            var run = NewRun(ProgressStore.ModuleB, out t);
            Assert.AreEqual(StepOutcome.Unsafe, run.Observe("dropped", "box_corrugated_brown_b20", 1f));
            StringAssert.Contains("floor", run.LastMessage);
            Assert.AreEqual(StepOutcome.Unsafe, run.Observe("grab_denied", "chair_patient_mobile_a01", 2f));
            StringAssert.Contains("Fixed equipment", run.LastMessage);
            Assert.AreEqual(2, run.Result.unsafeActions);
            Assert.AreEqual(2, run.Result.steps[0].errors);
        }

        [Test]
        public void Procedure_SafeHandling_GrabBeforeInspectIsOutOfOrder()
        {
            float t;
            var run = NewRun(ProgressStore.ModuleB, out t);
            Assert.AreEqual(StepOutcome.OutOfOrder, run.Observe("grab", "box_corrugated_brown_b20", 1f));
            Assert.AreEqual(StepOutcome.Correct, run.Observe("inspect", "box_corrugated_brown_b20", 2f));
            Assert.AreEqual(StepOutcome.Correct, run.Observe("grab", "box_corrugated_brown_b20", 3f));
        }

        [Test]
        public void Procedure_Hints_AreCountedAndCostPoints()
        {
            float t;
            var run = NewRun(ProgressStore.ModuleD, out t);
            Assert.IsNotNull(run.RequestHint());
            Assert.AreEqual(1, run.Result.hintsUsed);
            t = 0f;
            foreach (var s in run.Def.steps) { t += 3f; run.Observe(s.trigger, s.objectId, t); }
            var b = Engine().ScoreProcedure(run.Def, run.Result);
            Assert.AreEqual(98f, b.Categories["sequence"], 0.01f, "one hint costs 10% of one step out of five");
        }

        [Test]
        public void Procedure_Incomplete_IsScoredHonestly()
        {
            float t;
            var run = NewRun(ProgressStore.ModuleD, out t);
            run.Observe("select", "washBasin_a01", 1f);
            run.Observe("select", "cart_medical_utility_a01", 2f);
            run.Stop(10f);
            var b = Engine().ScoreProcedure(run.Def, run.Result);
            Assert.AreEqual(40f, b.Categories["sequence"], 0.01f);
            Assert.AreEqual(0f, b.Categories["efficiency"], 0.01f);
            Assert.IsFalse(b.Passed);
            Assert.IsTrue(b.Feedback.Exists(f => f.StartsWith("Not completed")));
        }

        [Test]
        public void Procedure_SlowRun_LowersEfficiencyOnly()
        {
            float t;
            var run = NewRun(ProgressStore.ModuleD, out t);
            t = 0f;
            foreach (var s in run.Def.steps) { t += 36f; run.Observe(s.trigger, s.objectId, t); }   // 180 s against a 90 s target
            var b = Engine().ScoreProcedure(run.Def, run.Result);
            Assert.AreEqual(100f, b.Categories["sequence"], 0.01f);
            Assert.AreEqual(50f, b.Categories["efficiency"], 0.01f);
        }

        [Test]
        public void Procedure_ReportContainsEveryStep_AndSerialises()
        {
            float t;
            var run = NewRun(ProgressStore.ModuleB, out t);
            t = 0f;
            foreach (var s in run.Def.steps) { t += 5f; run.Observe(s.trigger, s.objectId, t); }
            var reg = EquipmentRegistry.LoadDefault();
            var b = Engine().ScoreProcedure(run.Def, run.Result);
            var rep = ModuleReport.FromProcedure(run.Def, run.Result, b, 70f, DateTime.UtcNow, reg.NameOf);
            Assert.AreEqual(ProgressStore.ModuleB, rep.moduleId);
            Assert.AreEqual(run.Total, rep.items.Count);
            var back = JsonUtility.FromJson<ModuleReport>(rep.ToJson());
            Assert.AreEqual(rep.moduleId, back.moduleId);
            Assert.AreEqual(rep.items.Count, back.items.Count);
            Assert.AreEqual(rep.score, back.score, 0.001f);
            StringAssert.Contains("module_id,title", rep.ToCsv());
            StringAssert.Contains("category,score", rep.ToCsv());
        }

        // =============== monitoring (module C) ===============
        const float S2 = 45f;     // start of scenario 2 (stable lasts 45 s)
        const float S3 = 115f;    // start of scenario 3

        static MonitoringRun NewMon()
        {
            var run = new MonitoringRun(Mon());
            run.Begin(0f);
            return run;
        }

        [Test]
        public void Monitoring_ReportBeforeBeginOrAfterEnd_IsIgnored()
        {
            var run = new MonitoringRun(Mon());
            Assert.AreEqual(ReportOutcome.Ignored, run.Report("hr", 1f));
            run.Begin(0f);
            run.Tick(500f);
            Assert.IsTrue(run.Complete);
            Assert.AreEqual(ReportOutcome.Ignored, run.Report("hr", 501f));
        }

        [Test]
        public void Monitoring_ScenariosAdvanceByTime()
        {
            var run = NewMon();
            run.Tick(10f);
            Assert.AreEqual(0, run.ScenarioIndex);
            run.Tick(S2 + 1f);
            Assert.AreEqual(1, run.ScenarioIndex);
            Assert.AreEqual(1f, run.ScenarioElapsed, 0.01f);
            run.Tick(S3 + 1f);
            Assert.AreEqual(2, run.ScenarioIndex);
            run.Tick(run.TotalDuration + 0.1f);
            Assert.IsTrue(run.Complete);
        }

        [Test]
        public void Monitoring_PerfectRun_ScoresFullMarks()
        {
            var run = NewMon();
            Assert.AreEqual(ReportOutcome.Correct, run.Report("spo2", S2 + 31f));
            Assert.AreEqual(ReportOutcome.Correct, run.Report("hr", S3 + 26f));
            Assert.AreEqual(ReportOutcome.Correct, run.Report("temp", S3 + 47f));
            run.Tick(run.TotalDuration + 1f);
            Assert.IsTrue(run.Complete);
            Assert.AreEqual(3, run.DetectedEvents);
            var b = Engine().ScoreMonitoring(run.Result);
            Assert.AreEqual(100f, b.Categories["knowledge"], 0.01f);
            Assert.AreEqual(100f, b.Categories["efficiency"], 0.01f);
            Assert.IsTrue(b.Passed);
        }

        [Test]
        public void Monitoring_WrongParameter_ThenCorrect_ThenDuplicate()
        {
            var run = NewMon();
            Assert.AreEqual(ReportOutcome.WrongParameter, run.Report("hr", S2 + 35f), "spo2 is the abnormal value, not hr");
            Assert.AreEqual(ReportOutcome.Correct, run.Report("spo2", S2 + 36f));
            Assert.AreEqual(ReportOutcome.Duplicate, run.Report("spo2", S2 + 37f));
            Assert.AreEqual(1, run.Result.scenarios[1].wrongParameter);
        }

        [Test]
        public void Monitoring_FalseAlarm_InStableScenario_IsPenalised()
        {
            var run = NewMon();
            Assert.AreEqual(ReportOutcome.FalseAlarm, run.Report("hr", 10f));
            run.Report("spo2", S2 + 31f);
            run.Report("hr", S3 + 26f);
            run.Report("temp", S3 + 47f);
            run.Tick(run.TotalDuration + 1f);
            var b = Engine().ScoreMonitoring(run.Result);
            Assert.AreEqual(87.5f, b.Categories["knowledge"], 0.1f, "stable scenario item drops to 0.5 after one false alarm");
            Assert.IsTrue(b.Feedback.Exists(f => f.StartsWith("False alarm")));
        }

        [Test]
        public void Monitoring_ReportingTooEarly_IsAFalseAlarm()
        {
            var run = NewMon();
            Assert.AreEqual(ReportOutcome.FalseAlarm, run.Report("spo2", S2 + 10f), "spo2 is still in range at 10 s");
            Assert.AreEqual(ReportOutcome.Correct, run.Report("spo2", S2 + 31f), "a later correct report still counts");
        }

        [Test]
        public void Monitoring_NothingReported_ScoresLowAndListsMisses()
        {
            var run = NewMon();
            run.Tick(run.TotalDuration + 1f);
            var b = Engine().ScoreMonitoring(run.Result);
            Assert.AreEqual(25f, b.Categories["knowledge"], 0.01f);
            Assert.AreEqual(0f, b.Categories["efficiency"], 0.01f);
            Assert.IsFalse(b.Passed);
            int missed = 0;
            foreach (var f in b.Feedback) if (f.StartsWith("Missed")) missed++;
            Assert.AreEqual(3, missed);
        }

        [Test]
        public void Monitoring_SlowDetection_LowersEfficiency()
        {
            var run = NewMon();
            run.Report("spo2", S2 + 29.25f + 30f);   // 30 s after onset against a 15 s target
            run.Report("hr", S3 + 26f);
            run.Report("temp", S3 + 47f);
            run.Tick(run.TotalDuration + 1f);
            var b = Engine().ScoreMonitoring(run.Result);
            Assert.AreEqual(100f, b.Categories["knowledge"], 0.01f);
            Assert.Less(b.Categories["efficiency"], 100f);
            Assert.IsTrue(b.Feedback.Exists(f => f.StartsWith("Slow")));
        }

        [Test]
        public void Monitoring_Display_IsDeterministicAndCloseToTruth()
        {
            var s = Mon().scenarios[0];
            var a = MonitoringRun.DisplayVitals(s, 12.3f);
            var b = MonitoringRun.DisplayVitals(s, 12.3f);
            Assert.AreEqual(a.hr, b.hr);
            Assert.AreEqual(a.spo2, b.spo2);
            var truth = MonitoringRun.TrueVitals(s, 12.3f);
            Assert.AreEqual(truth.hr, a.hr, 2f);
            Assert.AreEqual(truth.spo2, a.spo2, 0.5f);
            Assert.AreEqual(truth.temp, a.temp, 0.1f);
        }

        [Test]
        public void Monitoring_ReportSerialises()
        {
            var run = NewMon();
            run.Report("spo2", S2 + 31f);
            run.Tick(run.TotalDuration + 1f);
            var b = Engine().ScoreMonitoring(run.Result);
            var rep = ModuleReport.FromMonitoring(Mon(), run.Result, b, 70f, DateTime.UtcNow, null);
            Assert.AreEqual(ProgressStore.ModuleC, rep.moduleId);
            Assert.AreEqual(3, rep.items.Count);
            var back = JsonUtility.FromJson<ModuleReport>(rep.ToJson());
            Assert.AreEqual(3, back.items.Count);
        }

        // =============== knowledge quiz (module E) ===============
        static KnowledgeSet SmallKnowledge()
        {
            return new KnowledgeSet
            {
                moduleId = "t", title = "T",
                questions = new[]
                {
                    new KnowledgeQuestion { id = "k1", prompt = "p1", options = new[] { "a", "b", "c" }, correctIndex = 1, hint = "h1", explanation = "x1", targetSeconds = 10 },
                    new KnowledgeQuestion { id = "k2", prompt = "p2", options = new[] { "a", "b", "c" }, correctIndex = 0, hint = "h2", explanation = "x2", targetSeconds = 10 },
                    new KnowledgeQuestion { id = "k3", prompt = "p3", options = new[] { "a", "b", "c" }, correctIndex = 2, hint = "h3", explanation = "x3", targetSeconds = 10 }
                }
            };
        }

        [Test]
        public void Knowledge_AllCorrect_ScoresFullMarks()
        {
            var q = new KnowledgeQuiz(SmallKnowledge());
            q.Begin(0f);
            float t = 0f;
            foreach (int idx in new[] { 1, 0, 2 })
            {
                t += 5f;
                Assert.AreEqual(KnowledgeOutcome.Correct, q.Answer(idx, t));
                q.Continue(t);
            }
            Assert.IsTrue(q.Complete);
            var b = Engine().ScoreKnowledge(q.Results);
            Assert.AreEqual(100f, b.Categories["knowledge"], 0.01f);
            Assert.AreEqual(100f, b.Categories["efficiency"], 0.01f);
            Assert.IsTrue(b.Passed);
        }

        [Test]
        public void Knowledge_OneWrong_HintCost_AndGuards()
        {
            var q = new KnowledgeQuiz(SmallKnowledge());
            q.Begin(0f);
            Assert.AreEqual(KnowledgeOutcome.Ignored, q.Answer(9, 1f), "out-of-range option");
            Assert.AreEqual("h1", q.RequestHint());
            Assert.AreEqual(KnowledgeOutcome.Correct, q.Answer(1, 2f));
            Assert.AreEqual(KnowledgeOutcome.Ignored, q.Answer(0, 3f), "already answered");
            Assert.IsNull(q.RequestHint(), "no hints after answering");
            q.Continue(3f);
            Assert.AreEqual(KnowledgeOutcome.Wrong, q.Answer(2, 4f));
            q.Continue(4f);
            Assert.AreEqual(KnowledgeOutcome.Correct, q.Answer(2, 5f));
            q.Continue(5f);
            Assert.IsTrue(q.Complete);
            var b = Engine().ScoreKnowledge(q.Results);
            // items: 0.9 (one hint), 0, 1  -> mean 0.6333
            Assert.AreEqual(63.33f, b.Categories["knowledge"], 0.05f);
            Assert.IsFalse(b.Passed);
            Assert.AreEqual(1, b.ReviewObjectIds.Count);
        }

        [Test]
        public void Knowledge_Report_ListsMissedQuestionsForReview()
        {
            var set = SmallKnowledge();
            var q = new KnowledgeQuiz(set);
            q.Begin(0f);
            q.Answer(0, 1f); q.Continue(1f);       // wrong
            q.Answer(0, 2f); q.Continue(2f);       // right
            q.Answer(2, 3f); q.Continue(3f);       // right
            var b = Engine().ScoreKnowledge(q.Results);
            var rep = ModuleReport.FromKnowledge(set, q.Results, b, 70f, DateTime.UtcNow, 3f);
            Assert.AreEqual(3, rep.items.Count);
            CollectionAssert.Contains(rep.reviewAreas, "p1");
        }

        // =============== progress and dashboard ===============
        static ModuleReport Rep(string id, float score, bool pass, params CategoryScore[] cats)
        {
            var r = new ModuleReport { moduleId = id, title = id, score = score, passed = pass, passThreshold = 70f };
            r.categories.AddRange(cats);
            return r;
        }

        static CategoryScore C(string name, float v) { return new CategoryScore { category = name, value = v }; }

        [Test]
        public void Progress_Overall_AveragesCategoriesAcrossModules_AndRenormalisesWeights()
        {
            var store = new ProgressStore(null);
            store.Record(Rep(ProgressStore.ModuleA, 85f, true, C("identification", 80f), C("efficiency", 100f)));
            store.Record(Rep(ProgressStore.ModuleE, 90f, true, C("knowledge", 100f), C("efficiency", 50f)));
            var o = store.Overall(Engine());
            Assert.AreEqual(80f, o.Categories["identification"], 0.01f);
            Assert.AreEqual(75f, o.Categories["efficiency"], 0.01f);
            Assert.AreEqual(100f, o.Categories["knowledge"], 0.01f);
            // weights 25 / 10 / 10 renormalised over 45
            Assert.AreEqual((25f * 80f + 10f * 75f + 10f * 100f) / 45f, o.Total, 0.01f);
            Assert.IsTrue(o.Feedback.Exists(f => f.Contains("Not yet attempted")));
        }

        [Test]
        public void Progress_RecordsAttemptsAndKeepsBestScore()
        {
            var store = new ProgressStore(null);
            store.Record(Rep(ProgressStore.ModuleB, 90f, true, C("handling", 90f)));
            store.Record(Rep(ProgressStore.ModuleB, 60f, false, C("handling", 60f)));
            var rec = store.Get(ProgressStore.ModuleB);
            Assert.AreEqual(2, rec.attempts);
            Assert.AreEqual(90f, rec.bestScore, 0.01f);
            Assert.AreEqual(60f, rec.latest.score, 0.01f);
            Assert.AreEqual(1, store.AttemptedCount);
        }

        [Test]
        public void Progress_PersistsToDiskAndReloads()
        {
            string dir = Path.Combine(Path.GetTempPath(), "medsim_prog_" + Guid.NewGuid().ToString("N"));
            try
            {
                var a = new ProgressStore(dir);
                a.Record(Rep(ProgressStore.ModuleD, 77f, true, C("sequence", 77f)));
                a.Save();
                Assert.IsTrue(File.Exists(Path.Combine(dir, "progress.json")));
                var b = new ProgressStore(dir);
                Assert.AreEqual(1, b.AttemptedCount);
                Assert.AreEqual(77f, b.Get(ProgressStore.ModuleD).latest.score, 0.01f);
                Assert.AreEqual("sequence", b.Get(ProgressStore.ModuleD).latest.categories[0].category);
            }
            finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        }

        [Test]
        public void Progress_CorruptFile_IsIgnoredNotFatal()
        {
            string dir = Path.Combine(Path.GetTempPath(), "medsim_prog_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "progress.json"), "{ not json");
                var s = new ProgressStore(dir);   // logs a warning, starts empty
                Assert.AreEqual(0, s.AttemptedCount);
            }
            finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        }

        [Test]
        public void Dashboard_EmptyAndPopulated()
        {
            var store = new ProgressStore(null);
            StringAssert.Contains("No modules completed", store.DashboardText(Engine()));
            store.Record(Rep(ProgressStore.ModuleA, 88f, true, C("identification", 88f), C("efficiency", 90f)));
            string text = store.DashboardText(Engine());
            StringAssert.Contains("OVERALL", text);
            StringAssert.Contains("Equipment identification", text);
            StringAssert.Contains("Not yet attempted", text);
            StringAssert.Contains("provisional", text);
        }

        [Test]
        public void ModuleReport_Save_WritesJsonAndCsv()
        {
            string dir = Path.Combine(Path.GetTempPath(), "medsim_rep_" + Guid.NewGuid().ToString("N"));
            try
            {
                var rep = Rep(ProgressStore.ModuleE, 83f, true, C("knowledge", 83f));
                rep.items.Add(new ReportItem { id = "e01", label = "has, comma and \"quote\"", outcome = "correct" });
                string json = rep.Save(dir);
                Assert.IsTrue(File.Exists(json));
                string csv = File.ReadAllText(Path.ChangeExtension(json, ".csv"));
                StringAssert.Contains("\"has, comma and \"\"quote\"\"\"", csv);
            }
            finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        }
    }
}
