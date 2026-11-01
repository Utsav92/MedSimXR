using System.Collections;
using System.IO;
using MedSimXR.Assessment;
using MedSimXR.Core;
using MedSimXR.Core.UI;
using MedSimXR.Interaction;
using MedSimXR.Training;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace MedSimXR.Tests
{
    /// <summary>Phase 2 modules running inside the real built scene with real physics and the real interaction pipeline.</summary>
    public class Phase2PlayTests
    {
        TrainingManager _tm;
        InteractionSystem _ints;
        FirstPersonController _fpc;
        HudView _hud;
        string _reportDir;

        [UnitySetUp]
        public IEnumerator Load()
        {
            Time.timeScale = 1f;
            SessionLog.Clear();
            yield return SceneManager.LoadSceneAsync("Hospital_Phase1");
            yield return null;
            yield return null;
            _tm = Object.FindFirstObjectByType<TrainingManager>();
            _ints = Object.FindFirstObjectByType<InteractionSystem>();
            _fpc = Object.FindFirstObjectByType<FirstPersonController>();
            _hud = Object.FindFirstObjectByType<HudView>();
            _reportDir = Path.Combine(Path.GetTempPath(), "medsim_play2_" + System.Guid.NewGuid().ToString("N"));
            _tm.reportDirectory = _reportDir;
        }

        [TearDown]
        public void Cleanup()
        {
            Time.timeScale = 1f;
            if (_reportDir != null && Directory.Exists(_reportDir)) Directory.Delete(_reportDir, true);
        }

        static InteractableObject Find(string id)
        {
            foreach (var io in Object.FindObjectsByType<InteractableObject>(FindObjectsSortMode.None))
                if (io.objectId == id) return io;
            return null;
        }

        IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }
        IEnumerator Fixed(int n) { for (int i = 0; i < n; i++) yield return new WaitForFixedUpdate(); }

        [UnityTest]
        public IEnumerator MenuAndModuleData_AreLoaded()
        {
            yield return null;
            _tm.OpenMenu();
            StringAssert.Contains("Module B", _hud.DebugText());
            StringAssert.Contains("Module C", _hud.DebugText());
            StringAssert.Contains("Module D", _hud.DebugText());
            StringAssert.Contains("Module E", _hud.DebugText());
            StringAssert.Contains("dashboard", _hud.DebugText());
            _tm.CloseMenu();
            Assert.AreEqual(AppMode.Explore, _tm.Mode);
        }

        [UnityTest]
        public IEnumerator ModuleD_RealSelectionPipeline_CompletesAndIsRecorded()
        {
            yield return null;
            Assert.IsTrue(_tm.StartProcedure(ProgressStore.ModuleD));
            Assert.AreEqual(AppMode.Procedure, _tm.Mode);
            StringAssert.Contains("hand hygiene", _hud.DebugText());

            // step 1 through the real hover pipeline
            var station = Find("washBasin_a01");
            _fpc.Teleport(new Vector3(1.8f, 0.05f, -2.6f), 0f);
            _fpc.LookAtPoint(station.GetWorldBounds().center);
            yield return Frames(4);
            Assert.IsNotNull(_ints.Hovered, "nothing under the crosshair");
            Assert.AreEqual("washBasin_a01", _ints.Hovered.objectId);
            _ints.Select(_ints.Hovered);
            Assert.AreEqual(1, _tm.Procedure.Index);

            // a step from later in the sequence is out of order
            _ints.Select(Find("bin_waste_a01"));
            Assert.AreEqual(1, _tm.Procedure.Result.outOfOrder);
            StringAssert.Contains("Out of sequence", _hud.DebugText());
            Assert.AreEqual(1, _tm.Procedure.Index, "must not advance");

            foreach (var id in new[] { "cart_medical_utility_a01", "bed_hospital_standard_a01", "bin_waste_a01", "washBasin_a01" })
                _ints.Select(Find(id));
            yield return null;

            Assert.AreEqual(AppMode.Results, _tm.Mode);
            Assert.IsNotNull(_tm.LastModuleReport);
            Assert.AreEqual(ProgressStore.ModuleD, _tm.LastModuleReport.moduleId);
            Assert.Less(_tm.LastModuleReport.score, 100f, "the out-of-order action must cost points");
            Assert.IsTrue(File.Exists(_tm.LastModuleReportPath), "module report json not written");
            Assert.IsTrue(File.Exists(Path.ChangeExtension(_tm.LastModuleReportPath, ".csv")));
            Assert.IsTrue(File.Exists(Path.Combine(_reportDir, "progress.json")));
            Assert.AreEqual(1, _tm.Progress.Get(ProgressStore.ModuleD).attempts);
            StringAssert.Contains("RESULTS", _hud.DebugText());
        }

        [UnityTest]
        public IEnumerator ModuleB_RealPhysics_InspectGrabPlaceReturn()
        {
            yield return null;
            Assert.IsTrue(_tm.StartProcedure(ProgressStore.ModuleB));
            var box = Find("box_corrugated_brown_b20");
            _fpc.Teleport(new Vector3(0.4f, 0.05f, 1.5f), 0f);
            yield return Frames(2);

            _ints.Inspect(box);
            Assert.AreEqual(1, _tm.Procedure.Index);
            Assert.IsTrue(_ints.TryGrab(box));
            Assert.AreEqual(2, _tm.Procedure.Index);
            yield return Fixed(40);

            var zone = Object.FindFirstObjectByType<PlacementZone>();
            Vector3 target = zone.transform.position + Vector3.up * 0.1f;
            box.Body.position = target;
            box.transform.position = target;
            Physics.SyncTransforms();
            _ints.Release();
            yield return Fixed(150);
            Assert.AreEqual(InteractionState.Placed, box.State);
            Assert.AreEqual(3, _tm.Procedure.Index, "placing on the work surface completes step 3");

            box.ResetToStart();
            yield return null;
            Assert.AreEqual(AppMode.Results, _tm.Mode);
            Assert.AreEqual(ProgressStore.ModuleB, _tm.LastModuleReport.moduleId);
            Assert.AreEqual(100f, _tm.LastModuleReport.CategoryValue("handling"), 0.5f);
            Assert.AreEqual(0, _tm.Procedure.Result.unsafeActions);
        }

        [UnityTest]
        public IEnumerator ModuleB_TryingToMoveFixedEquipment_IsAnUnsafeAction()
        {
            yield return null;
            _tm.StartProcedure(ProgressStore.ModuleB);
            Assert.IsFalse(_ints.TryGrab(Find("chair_patient_mobile_a01")));
            Assert.AreEqual(1, _tm.Procedure.Result.unsafeActions);
            StringAssert.Contains("Fixed equipment", _hud.DebugText());
        }

        [UnityTest]
        public IEnumerator ModuleB_DroppingTheBox_IsAnUnsafeAction()
        {
            yield return null;
            _tm.StartProcedure(ProgressStore.ModuleB);
            var box = Find("box_corrugated_brown_b20");
            _fpc.Teleport(new Vector3(-0.5f, 0.05f, -0.5f), 0f);
            yield return Frames(2);
            Vector3 hp = _ints.holdPoint.position;
            box.Body.position = hp; box.transform.position = hp;
            Physics.SyncTransforms();
            _ints.TryGrab(box);
            yield return Fixed(10);
            _ints.Release();
            yield return Fixed(120);
            Assert.GreaterOrEqual(_tm.Procedure.Result.unsafeActions, 1, "a dropped box must be recorded as unsafe");
            box.ResetToStart();
        }

        [UnityTest]
        public IEnumerator ModuleC_Monitoring_ReportsScoresAndRecords()
        {
            yield return null;
            Assert.IsTrue(_tm.StartMonitoring());
            Assert.AreEqual(AppMode.Monitoring, _tm.Mode);
            yield return Frames(2);
            StringAssert.Contains("SIMULATED", _hud.MonitorText);
            StringAssert.Contains("SpO2", _hud.MonitorText);

            // false alarm while the first (stable) scenario is running
            Assert.AreEqual(ReportOutcome.FalseAlarm, _tm.ReportParam("hr"));

            _tm.timeOffset += 76f;                 // jump into scenario 2, where SpO2 has fallen below its range
            Assert.AreEqual(ReportOutcome.WrongParameter, _tm.ReportParam("rr"));
            Assert.AreEqual(ReportOutcome.Correct, _tm.ReportParam("spo2"));

            _tm.timeOffset += 86f;                 // scenario 3: heart rate and temperature are both out of range
            Assert.AreEqual(ReportOutcome.Correct, _tm.ReportParam("hr"));
            Assert.AreEqual(ReportOutcome.Correct, _tm.ReportParam("temp"));

            _tm.timeOffset += 100f;                // run out the clock
            yield return Frames(2);
            Assert.AreEqual(AppMode.Results, _tm.Mode);
            Assert.AreEqual(ProgressStore.ModuleC, _tm.LastModuleReport.moduleId);
            float know = _tm.LastModuleReport.CategoryValue("knowledge");
            Assert.Less(know, 100f, "the false alarm and wrong parameter must cost points");
            Assert.Greater(know, 60f);
            Assert.AreEqual(3, _tm.Monitoring.DetectedEvents);
            Assert.AreEqual(1, _tm.Progress.Get(ProgressStore.ModuleC).attempts);
            Assert.AreEqual("", _hud.MonitorText, "the monitor panel is hidden after the module");
        }

        [UnityTest]
        public IEnumerator ModuleC_ClickingTheMonitor_OpensTheReportPrompt()
        {
            yield return null;
            _tm.StartMonitoring();
            _ints.Select(Find("monitor_patient_a01"));
            StringAssert.Contains("Which value is out of range", _hud.DebugText());
        }

        [UnityTest]
        public IEnumerator ModuleE_Knowledge_FullRunWithHintAndWrongAnswer()
        {
            yield return null;
            Assert.IsTrue(_tm.StartKnowledge());
            Assert.AreEqual(AppMode.Knowledge, _tm.Mode);
            StringAssert.Contains("What is a CT injection system used for", _hud.DebugText());
            Assert.IsNotNull(_tm.RequestKnowledgeHint());
            StringAssert.Contains("Hint:", _hud.DebugText());

            // first question wrong on purpose, everything else right
            int wrong = (_tm.Knowledge.Current.correctIndex + 1) % _tm.Knowledge.Current.options.Length;
            Assert.AreEqual(KnowledgeOutcome.Wrong, _tm.AnswerKnowledge(wrong));
            StringAssert.Contains("Not quite", _hud.DebugText());
            _tm.ContinueKnowledge();
            int guard = 0;
            while (_tm.Mode == AppMode.Knowledge && guard++ < 20)
            {
                Assert.AreEqual(KnowledgeOutcome.Correct, _tm.AnswerKnowledge(_tm.Knowledge.Current.correctIndex));
                _tm.ContinueKnowledge();
                yield return null;
            }
            Assert.AreEqual(AppMode.Results, _tm.Mode);
            Assert.AreEqual(ProgressStore.ModuleE, _tm.LastModuleReport.moduleId);
            Assert.Less(_tm.LastModuleReport.CategoryValue("knowledge"), 100f);
            Assert.Greater(_tm.LastModuleReport.CategoryValue("knowledge"), 70f);
            Assert.AreEqual(1, _tm.LastModuleReport.reviewAreas.Count);
        }

        [UnityTest]
        public IEnumerator Pause_InKnowledgeModule_RestoresTheQuestion()
        {
            yield return null;
            _tm.StartKnowledge();
            string before = _hud.OverlayBody;
            _tm.TogglePause();
            Assert.AreEqual(AppMode.Paused, _tm.Mode);
            StringAssert.Contains("PAUSED", _hud.DebugText());
            _tm.TogglePause();
            Assert.AreEqual(AppMode.Knowledge, _tm.Mode);
            Assert.AreEqual(before, _hud.OverlayBody, "the question must come back after resuming");
        }

        [UnityTest]
        public IEnumerator CancelModule_ReturnsToRoom_AndRecordsNothing()
        {
            yield return null;
            _tm.StartProcedure(ProgressStore.ModuleD);
            _tm.CancelModule();
            Assert.AreEqual(AppMode.Explore, _tm.Mode);
            Assert.IsNull(_tm.Procedure);
            Assert.AreEqual(0, _tm.Progress.AttemptedCount);
            Assert.AreEqual(1, SessionLog.Count("module_abandoned"));
        }

        [UnityTest]
        public IEnumerator Dashboard_ShowsCombinedProgress()
        {
            yield return null;
            // Module E perfect run
            _tm.StartKnowledge();
            int guard = 0;
            while (_tm.Mode == AppMode.Knowledge && guard++ < 20) { _tm.AnswerKnowledge(_tm.Knowledge.Current.correctIndex); _tm.ContinueKnowledge(); }
            _tm.ReturnToRoom();
            // Module A perfect run
            _tm.StartIdentification();
            guard = 0;
            while (_tm.Mode == AppMode.Quiz && guard++ < 20) { _tm.SubmitSelection(_tm.Quiz.Current.correctObjectId); _tm.ContinueQuiz(); }
            _tm.ReturnToRoom();

            Assert.AreEqual(2, _tm.Progress.AttemptedCount);
            _tm.ShowDashboard();
            Assert.AreEqual(AppMode.Results, _tm.Mode);
            StringAssert.Contains("PROGRESS DASHBOARD", _hud.DebugText());
            StringAssert.Contains("OVERALL", _hud.OverlayBody);
            StringAssert.Contains("Not yet attempted", _hud.OverlayBody);
            Assert.GreaterOrEqual(_tm.Progress.Overall(_tm.Scoring).Total, 90f);

            // progress survives a reload of the store from disk
            var again = new ProgressStore(_reportDir);
            Assert.AreEqual(2, again.AttemptedCount);
        }
    }
}
