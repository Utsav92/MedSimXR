using System.Collections;
using System.IO;
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
    /// <summary>Runs against the real built scene with real physics and the real interaction pipeline.</summary>
    public class Phase1PlayTests
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
            var op = SceneManager.LoadSceneAsync("Hospital_Phase1");
            yield return op;
            yield return null;
            yield return null;
            _tm = Object.FindFirstObjectByType<TrainingManager>();
            _ints = Object.FindFirstObjectByType<InteractionSystem>();
            _fpc = Object.FindFirstObjectByType<FirstPersonController>();
            _hud = Object.FindFirstObjectByType<HudView>();
            _reportDir = Path.Combine(Path.GetTempPath(), "medsim_play_" + System.Guid.NewGuid().ToString("N"));
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

        string RayDiagnostic()
        {
            var cam = _ints.viewCamera;
            var ray = new Ray(cam.transform.position, cam.transform.forward);
            var sb = new System.Text.StringBuilder();
            sb.Append("cam=" + cam.transform.position.ToString("F2") + " fwd=" + cam.transform.forward.ToString("F2")
                      + " reach=" + _ints.reach + " inputEnabled=" + _ints.inputEnabled + " mode=" + _tm.Mode + " hits:");
            foreach (var h in Physics.RaycastAll(ray, 20f, ~0, QueryTriggerInteraction.Ignore))
                sb.Append(" [" + h.collider.name + " d=" + h.distance.ToString("F2") + "]");
            return sb.ToString();
        }

        IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }
        IEnumerator Fixed(int n) { for (int i = 0; i < n; i++) yield return new WaitForFixedUpdate(); }

        [UnityTest]
        public IEnumerator Scene_BootsAndAppliesEquipmentData()
        {
            yield return null;
            var chair = Find("chair_patient_mobile_a01");
            var box = Find("box_corrugated_brown_b20");
            Assert.IsNotNull(chair);
            Assert.AreEqual("Patient Transport Chair", chair.displayName);
            Assert.IsFalse(chair.canGrab, "chair is fixed equipment");
            Assert.IsTrue(box.canGrab, "supply box is a permitted grabbable");
            Assert.AreEqual(AppMode.Explore, _tm.Mode);
            Assert.AreEqual(1, SessionLog.Count("scene_ready"));
        }

        [UnityTest]
        public IEnumerator Hover_RealRaycastFindsChair()
        {
            var chair = Find("chair_patient_mobile_a01");
            _fpc.Teleport(new Vector3(1.0f, 0.05f, -2.0f), 0f);
            _fpc.LookAtPoint(chair.GetWorldBounds().center);
            yield return Frames(4);
            Assert.IsNotNull(_ints.Hovered, "nothing under the crosshair. " + RayDiagnostic());
            Assert.AreEqual("chair_patient_mobile_a01", _ints.Hovered.objectId);
        }

        [UnityTest]
        public IEnumerator FixedEquipment_CannotBeGrabbed()
        {
            yield return null;
            Assert.IsFalse(_ints.TryGrab(Find("chair_patient_mobile_a01")));
            Assert.IsFalse(_ints.TryGrab(Find("CT_injection_system_a01")));
            Assert.AreEqual(2, SessionLog.Count("grab_denied"));
            Assert.IsNull(_ints.Held);
        }

        [UnityTest]
        public IEnumerator Grab_Place_OnWorkSurface_ThenReset()
        {
            var box = Find("box_corrugated_brown_b20");
            _fpc.Teleport(new Vector3(0.4f, 0.05f, 1.5f), 0f);
            yield return Frames(2);

            Assert.IsTrue(_ints.TryGrab(box));
            Assert.AreEqual(InteractionState.Held, box.State);
            yield return Fixed(40);
            float d = Vector3.Distance(box.Body.worldCenterOfMass, _ints.holdPoint.position);
            Assert.Less(d, 0.6f, "held object should follow the hold point");

            var zone = Object.FindFirstObjectByType<PlacementZone>();
            Vector3 target = zone.transform.position + Vector3.up * 0.1f;
            box.Body.position = target;
            box.transform.position = target;
            Physics.SyncTransforms();
            _ints.Release();
            Assert.AreEqual(1, SessionLog.Count("release"));
            yield return Fixed(150);

            Assert.AreEqual(InteractionState.Placed, box.State, "box should register as placed on the work surface");
            Assert.AreEqual(1, SessionLog.Count("placed"));

            box.ResetToStart();
            yield return Fixed(2);
            Assert.Less(Vector3.Distance(box.transform.position, box.ResetPosition), 0.05f);
            Assert.AreEqual(InteractionState.Idle, box.State);
        }

        [UnityTest]
        public IEnumerator Drop_OnFloor_IsLogged()
        {
            var box = Find("box_corrugated_brown_b20");
            _fpc.Teleport(new Vector3(-0.5f, 0.05f, -0.5f), 0f);
            yield return Frames(2);
            Vector3 hp = _ints.holdPoint.position;
            box.Body.position = hp; box.transform.position = hp;
            Physics.SyncTransforms();
            Assert.IsTrue(_ints.TryGrab(box));
            yield return Fixed(10);
            _ints.Release();
            yield return Fixed(120);
            Assert.GreaterOrEqual(SessionLog.Count("dropped"), 1, "a box released over open floor must register as dropped");
            Assert.AreEqual(InteractionState.Dropped, box.State);
            box.ResetToStart();
        }

        [UnityTest]
        public IEnumerator Quiz_FullRun_AllCorrect_PassesAndSavesReport()
        {
            yield return null;
            _tm.StartIdentification();
            Assert.AreEqual(AppMode.Quiz, _tm.Mode);
            StringAssert.Contains("Locate the patient transport chair", _hud.DebugText());
            int guard = 0;
            while (_tm.Mode == AppMode.Quiz && guard++ < 20)
            {
                string correct = _tm.Quiz.Current.correctObjectId;
                Assert.AreEqual(SubmitOutcome.Correct, _tm.SubmitSelection(correct));
                _tm.ContinueQuiz();
                yield return null;
            }
            Assert.AreEqual(AppMode.Results, _tm.Mode);
            Assert.IsNotNull(_tm.LastReport);
            Assert.AreEqual(10, _tm.LastReport.tasksTotal);
            Assert.AreEqual(10, _tm.LastReport.tasksCompleted);
            Assert.AreEqual(100f, _tm.LastReport.score, 0.5f);
            Assert.IsTrue(_tm.LastReport.passed);
            Assert.IsTrue(File.Exists(_tm.LastReportPath), "report json not written");
            Assert.IsTrue(File.Exists(Path.ChangeExtension(_tm.LastReportPath, ".csv")), "report csv not written");
            StringAssert.Contains("RESULTS", _hud.DebugText());
        }

        [UnityTest]
        public IEnumerator Quiz_WrongAnswerHintAndRecovery_LowersScore()
        {
            yield return null;
            _tm.StartIdentification();
            Assert.AreEqual(SubmitOutcome.Wrong, _tm.SubmitSelection("bed_hospital_standard_a01"));
            Assert.IsNotNull(_tm.RequestHint());
            Assert.AreEqual(SubmitOutcome.Correct, _tm.SubmitSelection(_tm.Quiz.Current.correctObjectId));
            _tm.ContinueQuiz();
            while (_tm.Mode == AppMode.Quiz)
            {
                _tm.SubmitSelection(_tm.Quiz.Current.correctObjectId);
                _tm.ContinueQuiz();
                yield return null;
            }
            Assert.Less(_tm.LastReport.score, 100f);
            Assert.AreEqual(1, _tm.LastReport.totalErrors);
            Assert.AreEqual(1, _tm.LastReport.totalHints);
            CollectionAssert.Contains(_tm.LastReport.reviewAreas, "Patient Transport Chair");
        }

        [UnityTest]
        public IEnumerator Quiz_SelectViaRealHoverPipeline()
        {
            _tm.StartIdentification();
            var chair = Find("chair_patient_mobile_a01");
            _fpc.Teleport(new Vector3(1.0f, 0.05f, -2.0f), 0f);
            _fpc.LookAtPoint(chair.GetWorldBounds().center);
            yield return Frames(4);
            Assert.AreEqual("chair_patient_mobile_a01", _ints.Hovered.objectId);
            _ints.Select(_ints.Hovered);   // same call the left-click path makes
            Assert.AreEqual(1, _tm.Quiz.Results.Count);
            Assert.IsTrue(_tm.Quiz.Results[0].correct);
        }

        [UnityTest]
        public IEnumerator Screenshots_HudInExploreAndQuiz()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("Screenshot capture needs a GPU; run without -nographics to produce Documentation/screens/1*.png");
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Documentation/screens"));
            Directory.CreateDirectory(dir);
            var chair = Find("chair_patient_mobile_a01");
            _fpc.Teleport(new Vector3(1.0f, 0.05f, -2.0f), 0f);
            _fpc.LookAtPoint(chair.GetWorldBounds().center);
            yield return Frames(5);
            _ints.Inspect(_ints.Hovered);
            yield return null;
            Save(dir, "10_play_explore_inspect.png");

            _tm.StartIdentification();
            yield return Frames(3);
            _tm.SubmitSelection("bed_hospital_standard_a01");
            yield return null;
            Save(dir, "11_play_quiz_wrong.png");

            _tm.OpenMenu();
            yield return null;
            Save(dir, "12_play_menu.png");
        }

        // Batch mode never raises WaitForEndOfFrame, so render the main camera (with the HUD canvas switched
        // to camera space, same CanvasScaler layout) into a texture instead.
        void Save(string dir, string name)
        {
            var cam = Camera.main;
            var canvas = _hud.GetComponentInChildren<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 0.5f;
            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            File.WriteAllBytes(Path.Combine(dir, name), tex.EncodeToPNG());
            RenderTexture.active = null;
            cam.targetTexture = null;
            Debug.Log("[MedSim] saved " + name);
            Object.Destroy(tex);
            Object.Destroy(rt);
        }

        [UnityTest]
        public IEnumerator Quiz_DoesNotRevealObjectNames()
        {
            var chair = Find("chair_patient_mobile_a01");
            _fpc.Teleport(new Vector3(1.0f, 0.05f, -2.0f), 0f);
            _fpc.LookAtPoint(chair.GetWorldBounds().center);
            yield return Frames(4);
            StringAssert.Contains("Patient Transport Chair", _hud.HoverText, "explore mode shows the name");

            _tm.StartIdentification();
            yield return Frames(3);
            Assert.AreEqual("chair_patient_mobile_a01", _ints.Hovered.objectId);
            StringAssert.DoesNotContain("Chair", _hud.HoverText, "hover label must not name the object during the quiz");
            StringAssert.DoesNotContain("Inspect", _hud.HoverText);
            foreach (var p in Object.FindObjectsByType<MedSimXR.Equipment.WorldInfoPanel>(FindObjectsSortMode.None))
                Assert.IsFalse(p.Visible, "world info panel must be hidden during the quiz");

            _ints.Inspect(_ints.Hovered);
            StringAssert.DoesNotContain("Chair", _hud.InfoText.Replace("Inspection is turned off", ""));

            // finishing the quiz brings the panels back
            int guard = 0;
            while (_tm.Mode == AppMode.Quiz && guard++ < 20) { _tm.SubmitSelection(_tm.Quiz.Current.correctObjectId); _tm.ContinueQuiz(); }
            _tm.SetMode(AppMode.Explore);
            foreach (var p in Object.FindObjectsByType<MedSimXR.Equipment.WorldInfoPanel>(FindObjectsSortMode.None))
                Assert.IsTrue(p.Visible);
        }

        [UnityTest]
        public IEnumerator Pause_FreezesTimeAndResumes()
        {
            yield return null;
            _tm.TogglePause();
            Assert.AreEqual(AppMode.Paused, _tm.Mode);
            Assert.AreEqual(0f, Time.timeScale);
            Assert.IsFalse(_fpc.inputEnabled);
            _tm.TogglePause();
            Assert.AreEqual(AppMode.Explore, _tm.Mode);
            Assert.AreEqual(1f, Time.timeScale);
            Assert.IsTrue(_fpc.inputEnabled);
        }

        [UnityTest]
        public IEnumerator WorldInfoPanel_ShowsChairData()
        {
            yield return null;
            var panel = Object.FindFirstObjectByType<MedSimXR.Equipment.WorldInfoPanel>();
            StringAssert.Contains("PATIENT TRANSPORT CHAIR", panel.CurrentText);
            StringAssert.Contains("NVIDIA SimReady", panel.CurrentText);
        }
    }
}
