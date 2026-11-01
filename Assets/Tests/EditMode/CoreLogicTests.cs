using System.Collections.Generic;
using System.IO;
using MedSimXR.AIInstructor;
using MedSimXR.Assessment;
using MedSimXR.Core;
using MedSimXR.Equipment;
using MedSimXR.Training;
using NUnit.Framework;
using UnityEngine;

namespace MedSimXR.Tests
{
    public class CoreLogicTests
    {
        static QuizSet SmallSet(int n = 3)
        {
            var qs = new List<QuizQuestion>();
            for (int i = 0; i < n; i++)
                qs.Add(new QuizQuestion { id = "t" + i, objectId = "obj" + i, correctObjectId = "obj" + i, prompt = "Find " + i,
                                          hints = new[] { "h1", "h2" }, explanation = "why", maxAttempts = 3, targetSeconds = 10 });
            return new QuizSet { moduleId = "test", title = "Test", questions = qs.ToArray() };
        }

        // ---------- data integrity ----------
        [Test]
        public void EquipmentData_LoadsAndIdsAreUnique()
        {
            var reg = EquipmentRegistry.LoadDefault();
            int count = 0;
            foreach (var e in reg.All)
            {
                count++;
                Assert.IsFalse(string.IsNullOrEmpty(e.displayName), e.id);
                Assert.IsFalse(string.IsNullOrEmpty(e.description), e.id);
                Assert.IsFalse(string.IsNullOrEmpty(e.category), e.id);
                Assert.AreEqual("pending", e.clinicalReview, e.id + " must not claim clinical review");
            }
            Assert.GreaterOrEqual(count, 11);
        }

        [Test]
        public void EquipmentData_PlaceholdersAreLabelledAsSuch()
        {
            foreach (var e in EquipmentRegistry.LoadDefault().All)
            {
                if (e.source == "placeholder") StringAssert.Contains("PLACEHOLDER", e.labelText.ToUpperInvariant(), e.id);
                else Assert.AreEqual("simready", e.source, e.id);
            }
        }

        [Test]
        public void IdentificationQuestions_AreCompleteAndReferenceKnownObjects()
        {
            var reg = EquipmentRegistry.LoadDefault();
            var set = DataLoader.Load<QuizSet>("identification_questions");
            Assert.GreaterOrEqual(set.questions.Length, 10, "spec requires at least 10 questions");
            var ids = new HashSet<string>();
            foreach (var q in set.questions)
            {
                Assert.IsTrue(ids.Add(q.id), "duplicate question id " + q.id);
                EquipmentEntry e;
                Assert.IsTrue(reg.TryGet(q.correctObjectId, out e), q.id + " references unknown object " + q.correctObjectId);
                Assert.IsFalse(string.IsNullOrEmpty(q.prompt), q.id);
                Assert.IsFalse(string.IsNullOrEmpty(q.explanation), q.id);
                Assert.IsFalse(string.IsNullOrEmpty(q.assessmentCriteria), q.id);
                Assert.IsNotNull(q.hints, q.id);
                Assert.GreaterOrEqual(q.hints.Length, 1, q.id);
            }
        }

        [Test]
        public void Rubric_WeightsMatchSpecAndThresholdIsConfigurable()
        {
            var r = Rubric.LoadDefault();
            Assert.AreEqual(25f, r.weights.identification);
            Assert.AreEqual(25f, r.weights.handling);
            Assert.AreEqual(30f, r.weights.sequence);
            Assert.AreEqual(10f, r.weights.efficiency);
            Assert.AreEqual(10f, r.weights.knowledge);
            StringAssert.Contains("PROVISIONAL", r.reviewStatus);
            Assert.Greater(r.passThreshold, 0f);
        }

        // ---------- quiz state machine ----------
        [Test]
        public void Quiz_CorrectFirstTry()
        {
            var q = new IdentificationQuiz(SmallSet());
            q.Begin(0f);
            Assert.AreEqual(SubmitOutcome.Correct, q.Submit("obj0", 4f));
            Assert.IsTrue(q.AwaitingContinue);
            Assert.AreEqual(1, q.Results.Count);
            Assert.IsTrue(q.Results[0].correct);
            Assert.AreEqual(0, q.Results[0].wrongAttempts);
            Assert.AreEqual(4f, q.Results[0].seconds, 0.001f);
        }

        [Test]
        public void Quiz_WrongThenCorrect_CountsAttempt()
        {
            var q = new IdentificationQuiz(SmallSet());
            q.Begin(0f);
            Assert.AreEqual(SubmitOutcome.Wrong, q.Submit("nope", 1f));
            Assert.AreEqual(SubmitOutcome.Correct, q.Submit("obj0", 2f));
            Assert.AreEqual(1, q.Results[0].wrongAttempts);
            CollectionAssert.AreEqual(new[] { "nope" }, q.Results[0].wrongSelections);
        }

        [Test]
        public void Quiz_RevealsAnswerAfterMaxAttempts()
        {
            var q = new IdentificationQuiz(SmallSet());
            q.Begin(0f);
            q.Submit("a", 1f); q.Submit("b", 2f);
            Assert.AreEqual(SubmitOutcome.Revealed, q.Submit("c", 3f));
            Assert.IsTrue(q.Results[0].revealed);
            Assert.IsFalse(q.Results[0].correct);
        }

        [Test]
        public void Quiz_SubmitIgnoredWhileAwaitingContinue()
        {
            var q = new IdentificationQuiz(SmallSet());
            q.Begin(0f);
            q.Submit("obj0", 1f);
            Assert.AreEqual(SubmitOutcome.Ignored, q.Submit("obj1", 2f));
            q.Continue(2f);
            Assert.AreEqual(1, q.Index);
        }

        [Test]
        public void Quiz_HintsRunOutAndAreCounted()
        {
            var q = new IdentificationQuiz(SmallSet());
            q.Begin(0f);
            Assert.AreEqual("h1", q.RequestHint());
            Assert.AreEqual("h2", q.RequestHint());
            Assert.IsNull(q.RequestHint());
            Assert.AreEqual(2, q.CurrentResult.hintsUsed);
        }

        [Test]
        public void Quiz_RunsToCompletion()
        {
            var q = new IdentificationQuiz(SmallSet(3));
            q.Begin(0f);
            for (int i = 0; i < 3; i++) { q.Submit("obj" + i, i + 1f); q.Continue(i + 1f); }
            Assert.IsTrue(q.Complete);
            Assert.AreEqual(3, q.Results.Count);
        }

        [Test]
        public void Quiz_SkipMarksRevealed()
        {
            var q = new IdentificationQuiz(SmallSet());
            q.Begin(0f);
            Assert.IsTrue(q.Skip(1f));
            Assert.IsTrue(q.Results[0].revealed);
        }

        // ---------- scoring ----------
        static ScoringEngine Engine() { return new ScoringEngine(Rubric.LoadDefault()); }

        static QuestionResult R(bool correct, int wrong = 0, int hints = 0, float sec = 5f, float target = 30f, bool revealed = false)
        {
            return new QuestionResult { questionId = "x", correctObjectId = "chair_patient_mobile_a01", correct = correct, wrongAttempts = wrong,
                                        hintsUsed = hints, seconds = sec, targetSeconds = target, revealed = revealed };
        }

        [Test]
        public void Scoring_PerfectRunIs100AndPasses()
        {
            var s = Engine().ScoreIdentification(new[] { R(true), R(true) });
            Assert.AreEqual(100f, s.Total, 0.01f);
            Assert.IsTrue(s.Passed);
        }

        [Test]
        public void Scoring_AllRevealedIsZeroAndFails()
        {
            var s = Engine().ScoreIdentification(new[] { R(false, 3, 0, 10, 30, true), R(false, 3, 0, 10, 30, true) });
            Assert.AreEqual(0f, s.Total, 0.01f);
            Assert.IsFalse(s.Passed);
            Assert.AreEqual(2, s.ReviewObjectIds.Count);
        }

        [Test]
        public void Scoring_PenalisesWrongAttemptsAndHints()
        {
            var e = Engine();
            float clean = e.ItemScore(R(true));
            float oneWrong = e.ItemScore(R(true, 1));
            float oneHint = e.ItemScore(R(true, 0, 1));
            Assert.AreEqual(1f, clean, 0.001f);
            Assert.AreEqual(0.75f, oneWrong, 0.001f);
            Assert.AreEqual(0.9f, oneHint, 0.001f);
        }

        [Test]
        public void Scoring_SlowAnswerLowersEfficiencyOnly()
        {
            var s = Engine().ScoreIdentification(new[] { R(true, 0, 0, 60f, 30f) });
            Assert.AreEqual(100f, s.Categories["identification"], 0.01f);
            Assert.AreEqual(50f, s.Categories["efficiency"], 0.01f);
            Assert.Less(s.Total, 100f);
        }

        [Test]
        public void Scoring_WeightsRenormaliseOverAssessedCategories()
        {
            // identification 25 + efficiency 10 => 100*25/35 + 50*10/35
            var s = Engine().ScoreIdentification(new[] { R(true, 0, 0, 60f, 30f) });
            Assert.AreEqual((100f * 25f + 50f * 10f) / 35f, s.Total, 0.01f);
        }

        [Test]
        public void Scoring_PassThresholdIsApplied()
        {
            // 4 of 10 correct first try, rest revealed => 40 id, 40 eff => 40 total => fail at 70
            var list = new List<QuestionResult>();
            for (int i = 0; i < 4; i++) list.Add(R(true));
            for (int i = 0; i < 6; i++) list.Add(R(false, 3, 0, 5, 30, true));
            var s = Engine().ScoreIdentification(list);
            Assert.AreEqual(40f, s.Total, 0.01f);
            Assert.IsFalse(s.Passed);
        }

        // ---------- report ----------
        [Test]
        public void Report_JsonRoundTripAndCsvShape()
        {
            var results = new List<QuestionResult> { R(true), R(true, 1, 1) };
            var score = Engine().ScoreIdentification(results, id => "Chair, \"special\"");
            var rep = SessionReport.Build("Scenario", System.DateTime.UtcNow, 42f, results, score, 70f, id => "Chair, \"special\"");
            Assert.AreEqual(2, rep.tasksCompleted);
            Assert.AreEqual(1, rep.totalErrors);
            Assert.AreEqual(1, rep.totalHints);

            var back = JsonUtility.FromJson<SessionReport>(rep.ToJson());
            Assert.AreEqual(rep.score, back.score, 0.001f);
            Assert.AreEqual(2, back.tasks.Count);

            string csv = rep.ToCsv();
            StringAssert.Contains("question_id,object_id", csv);
            var lines = csv.Split('\n');
            Assert.GreaterOrEqual(lines.Length, 6);
        }

        [Test]
        public void Report_SaveWritesJsonAndCsv()
        {
            var results = new List<QuestionResult> { R(true) };
            var score = Engine().ScoreIdentification(results);
            var rep = SessionReport.Build("S", System.DateTime.UtcNow, 1f, results, score, 70f, null);
            string dir = Path.Combine(Path.GetTempPath(), "medsim_test_" + System.Guid.NewGuid().ToString("N"));
            string json = rep.Save(dir);
            Assert.IsTrue(File.Exists(json));
            Assert.IsTrue(File.Exists(Path.ChangeExtension(json, ".csv")));
            StringAssert.Contains("not clinically validated", File.ReadAllText(json));
            Directory.Delete(dir, true);
        }

        // ---------- instructor ----------
        [Test]
        public void Instructor_SaysSoWhenNoVerifiedInformation()
        {
            var ins = new LocalInstructor(EquipmentRegistry.LoadDefault());
            Assert.AreEqual(LocalInstructor.NoInfo, ins.Explain("definitely_not_real"));
            StringAssert.Contains("Patient Transport Chair", ins.Explain("chair_patient_mobile_a01"));
            StringAssert.Contains("Hospital Bed", ins.Explain("bed_hospital_standard_a01"));
            StringAssert.DoesNotContain("Placeholder", ins.Explain("bed_hospital_standard_a01"));
        }

        [Test]
        public void Instructor_WrongAnswerDoesNotRevealCorrectAnswer()
        {
            var ins = new LocalInstructor(EquipmentRegistry.LoadDefault());
            string msg = ins.AfterWrong("bed_hospital_standard_a01", 2);
            StringAssert.DoesNotContain("chair", msg.ToLowerInvariant());
        }

        [Test]
        public void SessionLog_RecordsEventsInOrder()
        {
            SessionLog.Clear();
            SessionLog.Log("a", "x"); SessionLog.Log("b", "y");
            Assert.AreEqual(2, SessionLog.Events.Count);
            Assert.AreEqual("a", SessionLog.Events[0].type);
            Assert.AreEqual(1, SessionLog.Count("b"));
            StringAssert.Contains("\"type\"", SessionLog.ToJson());
        }
    }
}
