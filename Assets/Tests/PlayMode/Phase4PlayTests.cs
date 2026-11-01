using System.Collections;
using System.IO;
using MedSimXR.Assessment;
using MedSimXR.Core;
using MedSimXR.Core.UI;
using MedSimXR.Interaction;
using MedSimXR.Training;
using MedSimXR.XR;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace MedSimXR.Tests
{
    /// <summary>Module F (advanced identification) and the expanded room, in the real scene.</summary>
    public class Phase4PlayTests
    {
        TrainingManager _tm;
        HudView _hud;
        string _reportDir;

        [UnitySetUp]
        public IEnumerator Load()
        {
            Time.timeScale = 1f;
            XrModeController.AutoStartXr = false;
            SessionLog.Clear();
            yield return SceneManager.LoadSceneAsync("Hospital_Phase1");
            yield return null;
            yield return null;
            _tm = Object.FindFirstObjectByType<TrainingManager>();
            _hud = Object.FindFirstObjectByType<HudView>();
            _reportDir = Path.Combine(Path.GetTempPath(), "medsim_play4_" + System.Guid.NewGuid().ToString("N"));
            _tm.reportDirectory = _reportDir;
        }

        [TearDown]
        public void Cleanup()
        {
            Time.timeScale = 1f;
            XrModeController.AutoStartXr = true;
            if (_reportDir != null && Directory.Exists(_reportDir)) Directory.Delete(_reportDir, true);
        }

        [UnityTest]
        public IEnumerator MenuListsModuleF_AndDashboardIsOptionSeven()
        {
            yield return null;
            _tm.OpenMenu();
            StringAssert.Contains("Module F", _hud.DebugText());
            StringAssert.Contains("Progress dashboard", _hud.DebugText());
            Assert.AreEqual(8, _tm.VrOptionCount);
            _tm.ChooseMenu(7);
            StringAssert.Contains("PROGRESS DASHBOARD", _hud.DebugText());
        }

        [UnityTest]
        public IEnumerator ModuleF_FullRun_IsRecordedSeparatelyFromModuleA()
        {
            yield return null;
            _tm.OpenMenu();
            _tm.ChooseMenu(6);
            Assert.AreEqual(AppMode.Quiz, _tm.Mode);
            int total = _tm.Quiz.Total;
            Assert.GreaterOrEqual(total, 8);
            int guard = 0;
            while (_tm.Mode == AppMode.Quiz && guard++ < 60)
            {
                Assert.AreEqual(SubmitOutcome.Correct, _tm.SubmitSelection(_tm.Quiz.Current.correctObjectId));
                _tm.ContinueQuiz();
                yield return null;
            }
            Assert.AreEqual(AppMode.Results, _tm.Mode);
            Assert.AreEqual(total, _tm.LastReport.tasksTotal);
            Assert.AreEqual(100f, _tm.LastReport.score, 0.5f);
            Assert.AreEqual(1, _tm.Progress.Get(ProgressStore.ModuleF).attempts);
            Assert.IsNull(_tm.Progress.Get(ProgressStore.ModuleA), "module A must not be marked as done by module F");
            Assert.AreEqual(ProgressStore.ModuleF, _tm.LastModuleReport.moduleId);
        }

        [UnityTest]
        public IEnumerator ModuleF_HidesNamesLikeTheCoreQuiz()
        {
            yield return null;
            _tm.StartAdvancedIdentification();
            var q = _tm.Quiz.Current;
            string name = _tm.Equipment.NameOf(q.correctObjectId);
            Assert.AreNotEqual(q.correctObjectId, name);
            foreach (var p in Object.FindObjectsByType<MedSimXR.Equipment.WorldInfoPanel>(FindObjectsSortMode.None))
                Assert.IsFalse(p.Visible, "info panels must be hidden during identification quizzes");
            string h = _tm.RequestHint();
            Assert.IsNotNull(h);
            StringAssert.DoesNotContain(name, h);
        }

        [UnityTest]
        public IEnumerator EveryEquipmentItem_HasAWorkingColliderInTheRealScene()
        {
            yield return null;
            int n = 0;
            foreach (var io in Object.FindObjectsByType<InteractableObject>(FindObjectsSortMode.None))
            {
                n++;
                var col = io.GetComponent<Collider>();
                Assert.IsNotNull(col, io.objectId);
                Assert.IsTrue(col.enabled, io.objectId);
                Assert.Greater(io.GetWorldBounds().size.magnitude, 0.03f, io.objectId + " has an implausibly small size");
            }
            Assert.GreaterOrEqual(n, 20);
        }
    }
}
