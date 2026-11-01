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
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace MedSimXR.Tests
{
    /// <summary>
    /// Runs the VR code path in the real scene with scripted controller input. This proves the control scheme and mode
    /// switching work. It does NOT prove anything about real OpenXR runtimes, tracking, comfort or performance.
    /// </summary>
    public class Phase3PlayTests
    {
        TrainingManager _tm;
        InteractionSystem _ints;
        FirstPersonController _fpc;
        HudView _hud;
        XrModeController _mode;
        VrRig _rig;
        ScriptedXrInput _in;
        Transform _player;
        string _reportDir;

        [UnitySetUp]
        public IEnumerator Load()
        {
            Time.timeScale = 1f;
            XrModeController.AutoStartXr = false;      // never try to start an OpenXR runtime in tests
            SessionLog.Clear();
            yield return SceneManager.LoadSceneAsync("Hospital_Phase1");
            yield return null;
            yield return null;
            _tm = Object.FindFirstObjectByType<TrainingManager>();
            _ints = Object.FindFirstObjectByType<InteractionSystem>();
            _fpc = Object.FindFirstObjectByType<FirstPersonController>();
            _hud = Object.FindFirstObjectByType<HudView>();
            _mode = Object.FindFirstObjectByType<XrModeController>();
            _rig = _mode.rig;
            _player = _fpc.transform;
            _reportDir = Path.Combine(Path.GetTempPath(), "medsim_play3_" + System.Guid.NewGuid().ToString("N"));
            _tm.reportDirectory = _reportDir;
            _in = new ScriptedXrInput();
        }

        [TearDown]
        public void Cleanup()
        {
            Time.timeScale = 1f;
            XrModeController.AutoStartXr = true;
            if (_reportDir != null && Directory.Exists(_reportDir)) Directory.Delete(_reportDir, true);
        }

        static InteractableObject Find(string id)
        {
            foreach (var io in Object.FindObjectsByType<InteractableObject>(FindObjectsSortMode.None))
                if (io.objectId == id) return io;
            return null;
        }

        IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }
        IEnumerator Seconds(float sec) { float t = 0f; while (t < sec) { t += Time.deltaTime; yield return null; } }
        IEnumerator Fixed(int n) { for (int i = 0; i < n; i++) yield return new WaitForFixedUpdate(); }

        void EnterVr() { _mode.EnterVr(_in, true); }

        /// <summary>Aims the right controller at an object from a spot in front of it, in world space.</summary>
        void PointAt(InteractableObject io, Vector3 handWorldPos)
        {
            _in.AimRightHandAt(_rig.transform, io.GetWorldBounds().center, handWorldPos);
        }

        IEnumerator Click(System.Action<ScriptedXrInput> press)
        {
            press(_in);
            yield return null;
            yield return null;
            _in.State.rightTrigger = _in.State.rightGrip = _in.State.rightA = _in.State.rightB = _in.State.leftX = _in.State.leftY =
                _in.State.menu = _in.State.leftStickClick = _in.State.rightStickClick = false;
            yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator Desktop_IsTheDefault_AndNeedsNoHeadset()
        {
            yield return null;
            Assert.IsFalse(_mode.VrActive);
            Assert.IsFalse(_rig.gameObject.activeSelf);
            Assert.IsTrue(_mode.desktopCamera.enabled);
            Assert.IsFalse(_hud.IsWorldSpace);
            Assert.IsTrue(_ints.useKeyboard);
            Assert.AreEqual(AppMode.Explore, _tm.Mode);
        }

        [UnityTest]
        public IEnumerator EnterVr_SwapsCameraHudAndInteractionSource()
        {
            yield return null;
            EnterVr();
            yield return Frames(2);
            Assert.IsTrue(_mode.VrActive);
            Assert.IsTrue(_tm.VrActive);
            Assert.IsTrue(_rig.gameObject.activeSelf);
            Assert.IsTrue(_rig.xrCamera.enabled);
            Assert.IsFalse(_mode.desktopCamera.enabled);
            Assert.AreEqual(_rig.xrCamera, Camera.main, "the VR camera must become the main camera");
            Assert.IsFalse(_fpc.enabled, "desktop look/move is off in VR");
            Assert.IsFalse(_ints.useKeyboard);
            Assert.AreSame(_rig.rightHand, _ints.rayOrigin);
            Assert.AreSame(_rig.holdAttach, _ints.holdPoint);
            Assert.IsTrue(_hud.IsWorldSpace, "a screen-space overlay is invisible in a headset");
            Assert.IsTrue(_hud.VrMode);
        }

        [UnityTest]
        public IEnumerator ExitVr_RestoresDesktop()
        {
            yield return null;
            EnterVr();
            yield return Frames(2);
            _mode.ExitVr();
            yield return Frames(2);
            Assert.IsFalse(_mode.VrActive);
            Assert.IsTrue(_mode.desktopCamera.enabled);
            Assert.AreEqual(_mode.desktopCamera, Camera.main);
            Assert.IsFalse(_rig.gameObject.activeSelf);
            Assert.IsTrue(_fpc.enabled);
            Assert.IsTrue(_ints.useKeyboard);
            Assert.IsNull(_ints.rayOrigin);
            Assert.IsFalse(_hud.IsWorldSpace);
            Assert.IsFalse(_tm.VrActive);
        }

        [UnityTest]
        public IEnumerator HeadAndHandPoses_AreAppliedToTheRig()
        {
            yield return null;
            _in.State.headTracked = true;
            _in.State.headPos = new Vector3(0.1f, 1.5f, 0.05f);
            _in.State.headRot = Quaternion.Euler(0f, 40f, 0f);
            _in.State.rightTracked = true;
            _in.State.rightPos = new Vector3(0.3f, 1.2f, 0.4f);
            EnterVr();
            yield return Frames(2);
            Assert.AreEqual(1.5f, _rig.xrCamera.transform.localPosition.y, 0.001f);
            Assert.AreEqual(40f, _rig.xrCamera.transform.localEulerAngles.y, 0.1f);
            Assert.AreEqual(0.3f, _rig.rightHand.localPosition.x, 0.001f);
        }

        [UnityTest]
        public IEnumerator PointingTheControllerHoversAnObject_AndTriggerSelects()
        {
            yield return null;
            EnterVr();
            var chair = Find("chair_patient_mobile_a01");
            _fpc.Teleport(new Vector3(1.0f, 0.05f, -2.0f), 0f);
            PointAt(chair, _player.TransformPoint(new Vector3(0.25f, 1.35f, 0.3f)));
            yield return Frames(4);
            Assert.IsNotNull(_ints.Hovered, "controller ray found nothing");
            Assert.AreEqual("chair_patient_mobile_a01", _ints.Hovered.objectId);
            StringAssert.Contains("Patient Transport Chair", _hud.HoverText);
            Assert.AreEqual(1, _rig.laser.positionCount > 1 ? 1 : 0);
            Assert.IsTrue(_rig.laser.enabled);

            int before = SessionLog.Count("select");
            yield return Click(i => i.State.rightTrigger = true);
            Assert.AreEqual(before + 1, SessionLog.Count("select"), "trigger must select exactly once per press");
        }

        [UnityTest]
        public IEnumerator Aiming_At_EmptyAir_HoversNothing()
        {
            yield return null;
            EnterVr();
            _fpc.Teleport(new Vector3(0f, 0.05f, 0f), 0f);
            _in.State.rightTracked = true;
            _in.State.rightPos = new Vector3(0.25f, 1.35f, 0.3f);
            _in.State.rightRot = Quaternion.Euler(-80f, 0f, 0f);   // up at the ceiling
            yield return Frames(3);
            Assert.IsNull(_ints.Hovered);
        }

        [UnityTest]
        public IEnumerator A_Button_InspectsTheHoveredObject()
        {
            yield return null;
            EnterVr();
            var chair = Find("chair_patient_mobile_a01");
            _fpc.Teleport(new Vector3(1.0f, 0.05f, -2.0f), 0f);
            PointAt(chair, _player.TransformPoint(new Vector3(0.25f, 1.35f, 0.3f)));
            yield return Frames(4);
            yield return Click(i => i.State.rightA = true);
            StringAssert.Contains("PATIENT TRANSPORT CHAIR", _hud.InfoText);
        }

        [UnityTest]
        public IEnumerator LeftStick_MovesThePlayer_RelativeToHeadYaw()
        {
            yield return null;
            EnterVr();
            _fpc.Teleport(new Vector3(0f, 0.05f, -3.0f), 0f);
            yield return Frames(2);
            Vector3 start = _player.position;
            _in.State.leftStick = new Vector2(0f, 1f);
            yield return Seconds(0.6f);
            _in.State.leftStick = Vector2.zero;
            Vector3 d = _player.position - start;
            Assert.Greater(d.z, 0.3f, "pushing the stick forward should walk forward. moved " + d);
            Assert.Less(Mathf.Abs(d.x), 0.2f);

            // looking 90 degrees right turns "forward" into +X
            _fpc.Teleport(new Vector3(0f, 0.05f, -3.0f), 0f);
            _in.State.headTracked = true;
            _in.State.headRot = Quaternion.Euler(0f, 90f, 0f);
            yield return Frames(2);
            start = _player.position;
            _in.State.leftStick = new Vector2(0f, 1f);
            yield return Seconds(0.6f);
            _in.State.leftStick = Vector2.zero;
            d = _player.position - start;
            Assert.Greater(d.x, 0.3f, "forward should follow where the head faces. moved " + d);
        }

        [UnityTest]
        public IEnumerator RightStick_SnapTurnsOncePerFlick()
        {
            yield return null;
            EnterVr();
            _fpc.Teleport(new Vector3(0f, 0.05f, -3.0f), 0f);
            yield return Frames(2);
            float yaw0 = _player.eulerAngles.y;
            _in.State.rightStick = new Vector2(1f, 0f);
            for (int i = 0; i < 10; i++) yield return null;           // held: still only one turn
            Assert.AreEqual(30f, Mathf.DeltaAngle(yaw0, _player.eulerAngles.y), 0.5f);
            _in.State.rightStick = Vector2.zero;
            yield return Frames(2);
            _in.State.rightStick = new Vector2(-1f, 0f);
            yield return Frames(3);
            _in.State.rightStick = Vector2.zero;
            Assert.AreEqual(0f, Mathf.DeltaAngle(yaw0, _player.eulerAngles.y), 0.5f);
        }

        [UnityTest]
        public IEnumerator Grip_GrabsTheBoxWithPhysics_AndReleases()
        {
            yield return null;
            EnterVr();
            var box = Find("box_corrugated_brown_b20");
            _fpc.Teleport(new Vector3(0.4f, 0.05f, 1.5f), 0f);
            var bb = box.GetWorldBounds();   // aim at the top of the box so the table edge cannot catch the ray first
            _in.AimRightHandAt(_rig.transform, bb.center + Vector3.up * (bb.extents.y * 0.8f), _player.TransformPoint(new Vector3(0.25f, 1.35f, 0.3f)));
            yield return Frames(4);
            Assert.AreEqual("box_corrugated_brown_b20", _ints.Hovered != null ? _ints.Hovered.objectId : "nothing");
            yield return Click(i => i.State.rightGrip = true);
            Assert.AreSame(box, _ints.Held, "grip should grab the hovered box");
            yield return Fixed(60);
            float d = Vector3.Distance(box.Body.worldCenterOfMass, _rig.holdAttach.position);
            Assert.Less(d, 0.6f, "held box should follow the controller");
            yield return Click(i => i.State.rightGrip = true);
            Assert.IsNull(_ints.Held, "second grip press releases");
            box.ResetToStart();
        }

        [UnityTest]
        public IEnumerator Grip_OnFixedEquipment_IsDenied()
        {
            yield return null;
            EnterVr();
            var chair = Find("chair_patient_mobile_a01");
            _fpc.Teleport(new Vector3(1.0f, 0.05f, -2.0f), 0f);
            PointAt(chair, _player.TransformPoint(new Vector3(0.25f, 1.35f, 0.3f)));
            yield return Frames(4);
            yield return Click(i => i.State.rightGrip = true);
            Assert.IsNull(_ints.Held);
            Assert.AreEqual(1, SessionLog.Count("grab_denied"));
        }

        [UnityTest]
        public IEnumerator Menu_ChooseModuleWithStickAndA_ThenFinishProcedureByPointing()
        {
            yield return null;
            EnterVr();
            _fpc.Teleport(new Vector3(1.8f, 0.05f, -2.6f), 0f);
            yield return Click(i => i.State.leftY = true);
            Assert.AreEqual(AppMode.Menu, _tm.Mode);
            Assert.AreEqual(8, _tm.VrOptionCount);

            // module D is the fourth entry: three presses of "down"
            for (int n = 0; n < 3; n++)
            {
                _in.State.leftStick = new Vector2(0f, -1f);
                yield return Frames(2);
                _in.State.leftStick = Vector2.zero;
                yield return Frames(2);
            }
            Assert.AreEqual(3, _tm.VrCursor);
            StringAssert.Contains("option 4 of 8", _hud.StatusText);
            yield return Click(i => i.State.rightA = true);
            Assert.AreEqual(AppMode.Procedure, _tm.Mode);

            var station = Find("washBasin_a01");
            PointAt(station, _player.TransformPoint(new Vector3(0.25f, 1.35f, 0.3f)));
            yield return Frames(4);
            Assert.AreEqual("washBasin_a01", _ints.Hovered != null ? _ints.Hovered.objectId : "nothing");
            yield return Click(i => i.State.rightTrigger = true);
            Assert.AreEqual(1, _tm.Procedure.Index, "pointing at the station and pulling the trigger completes step 1");

            // hint with left X
            yield return Click(i => i.State.leftX = true);
            Assert.AreEqual(1, _tm.Procedure.Result.hintsUsed);

            // leave with left stick click
            yield return Click(i => i.State.leftStickClick = true);
            Assert.AreEqual(AppMode.Explore, _tm.Mode);
        }

        [UnityTest]
        public IEnumerator Knowledge_AnswerWithCursorAndA()
        {
            yield return null;
            EnterVr();
            _tm.StartKnowledge();
            yield return Frames(2);
            int correct = _tm.Knowledge.Current.correctIndex;
            for (int n = 0; n < correct; n++)
            {
                _in.State.leftStick = new Vector2(0f, -1f);
                yield return Frames(2);
                _in.State.leftStick = Vector2.zero;
                yield return Frames(2);
            }
            Assert.AreEqual(correct, _tm.VrCursor);
            yield return Click(i => i.State.rightA = true);
            Assert.IsTrue(_tm.Knowledge.CurrentResult.correct);
            StringAssert.Contains("Correct", _hud.OverlayBody);
            yield return Click(i => i.State.rightA = true);      // A again continues
            Assert.AreEqual(1, _tm.Knowledge.Index);
            Assert.AreEqual(0, _tm.VrCursor, "cursor resets for the next question");
        }

        [UnityTest]
        public IEnumerator MenuButton_PausesAndResumes()
        {
            yield return null;
            EnterVr();
            yield return Click(i => i.State.menu = true);
            Assert.AreEqual(AppMode.Paused, _tm.Mode);
            yield return null;
            // the menu button must still work while paused (time is frozen, input is read in Update)
            _in.State.menu = true;
            yield return null;
            _in.State.menu = false;
            yield return null;
            Assert.AreEqual(AppMode.Explore, _tm.Mode);
            Assert.AreEqual(1f, Time.timeScale);
        }

        [UnityTest]
        public IEnumerator Monitoring_ReportViaRightStickClickAndCursor()
        {
            yield return null;
            EnterVr();
            _tm.StartMonitoring();
            yield return Frames(2);
            _tm.timeOffset += 76f;                               // SpO2 is now out of range in scenario 2
            yield return Click(i => i.State.rightStickClick = true);
            Assert.AreEqual(6, _tm.VrOptionCount);
            _in.State.leftStick = new Vector2(0f, -1f);          // move to option 2 (SpO2)
            yield return Frames(2);
            _in.State.leftStick = Vector2.zero;
            yield return Frames(2);
            Assert.AreEqual(1, _tm.VrCursor);
            yield return Click(i => i.State.rightA = true);
            Assert.AreEqual(1, _tm.Monitoring.DetectedEvents);
        }

        [UnityTest]
        public IEnumerator Hud_FollowsTheHeadInWorldSpace()
        {
            yield return null;
            EnterVr();
            yield return Frames(3);
            var canvas = _hud.GetComponentInChildren<Canvas>();
            Assert.AreEqual(RenderMode.WorldSpace, canvas.renderMode);
            float dist = Vector3.Distance(canvas.transform.position, _rig.xrCamera.transform.position);
            Assert.AreEqual(1.6f, dist, 0.3f);
            Assert.AreEqual(1.8f / 1280f, canvas.transform.localScale.x, 0.0001f);
        }

        [UnityTest]
        public IEnumerator KeyboardSimulator_MapsKeysToVirtualControllers()
        {
            yield return null;
            var kb = InputSystem.AddDevice<Keyboard>();
            var mouse = InputSystem.AddDevice<Mouse>();
            try
            {
                var sim = new XrKeyboardSimulator();
                InputSystem.QueueStateEvent(kb, new KeyboardState(Key.W, Key.G, Key.Space));
                yield return null;
                yield return null;
                if (!kb.wKey.isPressed) Assert.Inconclusive("Keyboard events were not delivered to the test device (editor focus in batch mode).");
                var s = sim.Read(0.016f);
                Assert.AreEqual(1f, s.leftStick.y, 0.01f, "W = left stick forward");
                Assert.IsTrue(s.rightGrip, "G = grip");
                Assert.IsTrue(s.rightA, "Space = A");
                Assert.IsTrue(s.headTracked && s.rightTracked && s.leftTracked);
                InputSystem.QueueStateEvent(kb, new KeyboardState());
                yield return null;
                yield return null;
                s = sim.Read(0.016f);
                Assert.AreEqual(0f, s.leftStick.y, 0.01f);
                Assert.IsFalse(s.rightGrip);
            }
            finally
            {
                InputSystem.RemoveDevice(kb);
                InputSystem.RemoveDevice(mouse);
            }
        }

        [UnityTest]
        public IEnumerator ModeStatus_ReportsDesktopWhenNoHeadset()
        {
            yield return null;
            Assert.AreEqual("desktop", _mode.Status);
        }
    }
}
