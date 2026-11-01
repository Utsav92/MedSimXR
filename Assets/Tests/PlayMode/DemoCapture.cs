using System;
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
    /// <summary>
    /// Renders a scripted walkthrough to a JPG sequence at a fixed frame rate (Time.captureFramerate). Not part of the normal run:
    /// it only executes when MEDSIM_DEMO=1 and a GPU is present. The script calls the same methods the keys and controllers call;
    /// it is a rendered tour, not a recording of a person using the app. The VR section uses scripted controller input, not a headset.
    /// </summary>
    public class DemoCapture
    {
        const int Fps = 20;
        TrainingManager _tm;
        InteractionSystem _ints;
        FirstPersonController _fpc;
        HudView _hud;
        XrModeController _mode;
        VrRig _rig;
        string _dir;
        int _frame;
        bool _saving;
        RenderTexture _rt;
        Texture2D _tex;

        static InteractableObject Find(string id)
        {
            foreach (var io in UnityEngine.Object.FindObjectsByType<InteractableObject>(FindObjectsSortMode.None))
                if (io.objectId == id) return io;
            return null;
        }

        [UnityTest]
        [Timeout(6000000)]
        public IEnumerator RenderWalkthrough()
        {
            if (Environment.GetEnvironmentVariable("MEDSIM_DEMO") != "1") Assert.Ignore("Set MEDSIM_DEMO=1 to render the walkthrough.");
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) Assert.Ignore("Needs a GPU (run without -nographics).");

            _dir = Environment.GetEnvironmentVariable("MEDSIM_DEMO_DIR");
            if (string.IsNullOrEmpty(_dir)) _dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Documentation/demo_frames"));
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
            Directory.CreateDirectory(_dir);

            XrModeController.AutoStartXr = false;
            Time.captureFramerate = Fps;
            SessionLog.Clear();
            yield return SceneManager.LoadSceneAsync("Hospital_Phase1");
            yield return null;
            yield return null;
            _tm = UnityEngine.Object.FindFirstObjectByType<TrainingManager>();
            _ints = UnityEngine.Object.FindFirstObjectByType<InteractionSystem>();
            _fpc = UnityEngine.Object.FindFirstObjectByType<FirstPersonController>();
            _hud = UnityEngine.Object.FindFirstObjectByType<HudView>();
            _mode = UnityEngine.Object.FindFirstObjectByType<XrModeController>();
            _rig = _mode.rig;
            _tm.saveReports = false;
            _rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
            _tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);

            // warm-up so shaders and first-frame state settle; these frames are not saved
            _fpc.Teleport(new Vector3(0f, 0.05f, -3.2f), 0f);
            _fpc.LookAtPoint(new Vector3(0f, 1.2f, 3f));
            _saving = false;
            for (int i = 0; i < 20; i++) yield return Frame(1);
            _saving = true;

            // ---- 1. tour of the room and some of the newer equipment ----
            _fpc.LookAtPoint(new Vector3(0f, 1.2f, 3f));
            yield return Hold(2f);
            var start = new Vector3(0f, 0.05f, -3.2f);
            var chair = Find("chair_patient_mobile_a01");
            yield return Glide(start, new Vector3(1.0f, 0.05f, -2.0f), chair.GetWorldBounds().center, 2f);
            _ints.Inspect(_ints.Hovered != null ? _ints.Hovered : chair);
            yield return Hold(2.5f);
            Vector3 from = new Vector3(1.0f, 0.05f, -2.0f);
            foreach (var id in new[] { "robot_surgical_c01", "Xray_machine_buckyTable_a01", "instrument_artery_forceps_b01", "bed_hospital_standard_a01" })
            {
                var io = Find(id);
                if (io == null) continue;
                Vector3 eye = StandPoint(io);
                yield return Glide(from, eye, io.GetWorldBounds().center, 2f);
                _ints.Inspect(_ints.Hovered != null ? _ints.Hovered : io);
                yield return Hold(2.2f);
                from = eye;
            }

            // grab the box, carry it to the work surface, put it down, send it back
            var box = Find("box_corrugated_brown_b20");
            _fpc.Teleport(new Vector3(0.4f, 0.05f, 1.5f), 0f);
            _fpc.LookAtPoint(box.GetWorldBounds().center);
            yield return Hold(0.8f);
            _ints.TryGrab(box);
            yield return Hold(1.5f);
            var zone = UnityEngine.Object.FindFirstObjectByType<PlacementZone>();
            Vector3 target = zone.transform.position + Vector3.up * 0.1f;
            box.Body.position = target; box.transform.position = target;
            Physics.SyncTransforms();
            _ints.Release();
            yield return Hold(2f);
            box.ResetToStart();
            yield return Hold(0.6f);

            // ---- 2. module A: core identification ----
            _tm.OpenMenu();
            yield return Hold(2f);
            _tm.ChooseMenu(1);
            yield return Hold(1.2f);
            yield return RunQuiz(3, 1);
            yield return Hold(3.5f);
            _tm.ReturnToRoom();

            // ---- 3. module F: the new advanced quiz over the larger equipment set ----
            _tm.OpenMenu();
            yield return Hold(1f);
            _tm.ChooseMenu(6);
            yield return Hold(1.5f);
            yield return RunQuiz(5, 2);
            yield return Hold(3.5f);
            _tm.ReturnToRoom();

            // ---- 4. module D: infection prevention, with one deliberate out-of-order step ----
            _tm.OpenMenu();
            yield return Hold(0.8f);
            _tm.ChooseMenu(4);
            yield return Hold(2f);
            yield return LookAndSelect("bed_hospital_standard_a01", 1.4f);       // too early: patient contact before hand hygiene
            yield return Hold(1.2f);
            foreach (var id in new[] { "washBasin_a01", "cart_medical_utility_a01", "bed_hospital_standard_a01", "bin_waste_a01", "washBasin_a01" })
                yield return LookAndSelect(id, 1.2f);
            yield return Hold(3f);
            _tm.ReturnToRoom();

            // ---- 5. module C: simulated patient monitoring ----
            _tm.OpenMenu();
            yield return Hold(0.6f);
            _tm.ChooseMenu(3);
            var mon = Find("monitor_patient_a01");
            if (mon != null) { _fpc.Teleport(StandPoint(mon), 0f); _fpc.LookAtPoint(mon.GetWorldBounds().center); }
            yield return Hold(4f);
            _tm.timeOffset += 78f - 4f;                                    // scenario 2: SpO2 has dropped below its range
            yield return Hold(2f);
            _tm.OpenReportPrompt();
            yield return Hold(1.5f);
            _tm.ReportParam("spo2");
            yield return Hold(2.5f);
            _tm.CancelModule();

            // ---- 6. module E: orientation quiz ----
            _tm.OpenMenu();
            yield return Hold(0.5f);
            _tm.ChooseMenu(5);
            yield return Hold(3f);
            _tm.AnswerKnowledge(_tm.Knowledge.Current.correctIndex);
            yield return Hold(3f);
            _tm.CancelModule();

            // ---- 7. progress dashboard ----
            _tm.OpenMenu();
            yield return Hold(0.5f);
            _tm.ChooseMenu(7);
            yield return Hold(4.5f);
            _tm.ReturnToRoom();

            // ---- 8. final VR demo: scripted controllers (simulated input, no headset) ----
            var input = new ScriptedXrInput();
            _mode.EnterVr(input, true);
            yield return Hold(1f);
            yield return VrPoint(input, chair, 2f);
            yield return VrPress(input, i => i.State.rightTrigger = true, 1.2f);          // trigger selects
            yield return VrPress(input, i => i.State.rightA = true, 3f);                   // A inspects
            yield return VrGrabBoxAndPlace(input);
            yield return VrPress(input, i => i.State.leftY = true, 2f);                    // left Y opens the training menu
            for (int n = 0; n < 5; n++)                                                    // stick down to option 6 (Module F)
            {
                input.State.leftStick = new Vector2(0f, -1f);
                yield return Hold(0.15f);
                input.State.leftStick = Vector2.zero;
                yield return Hold(0.25f);
            }
            yield return Hold(1f);
            yield return VrPress(input, i => i.State.rightA = true, 1.5f);                 // A confirms
            for (int q = 0; q < 4 && _tm.Mode == AppMode.Quiz; q++)
            {
                var io = Find(_tm.Quiz.Current.correctObjectId);
                yield return VrPoint(input, io, 1.4f);
                yield return VrPress(input, i => i.State.rightTrigger = true, 1.4f);      // trigger answers
                yield return VrPress(input, i => i.State.rightA = true, 0.6f);            // A continues
            }
            yield return VrPress(input, i => i.State.leftStickClick = true, 1.5f);        // left stick click leaves the module
            yield return Hold(1f);
            _mode.ExitVr();
            yield return Hold(1f);

            Time.captureFramerate = 0;
            Debug.Log("[MedSim] demo frames written: " + _frame + " in " + _dir);
            Assert.Greater(_frame, 600);
        }

        [TearDown]
        public void Cleanup()
        {
            Time.captureFramerate = 0;
            Time.timeScale = 1f;
            XrModeController.AutoStartXr = true;
        }

        // ---------------- helpers ----------------
        IEnumerator Hold(float seconds) { return Frame(Mathf.Max(1, Mathf.RoundToInt(seconds * Fps))); }

        IEnumerator Frame(int n)
        {
            for (int i = 0; i < n; i++)
            {
                yield return null;
                if (_saving) SaveFrame();
            }
        }

        IEnumerator Glide(Vector3 from, Vector3 to, Vector3 look, float seconds)
        {
            int n = Mathf.Max(1, Mathf.RoundToInt(seconds * Fps));
            for (int i = 1; i <= n; i++)
            {
                float t = Mathf.SmoothStep(0f, 1f, i / (float)n);
                _fpc.Teleport(Vector3.Lerp(from, to, t), 0f);
                _fpc.LookAtPoint(look);
                yield return Frame(1);
            }
        }

        /// <summary>A spot about 2.3 m from the object, toward the middle of the room, kept inside the walls.</summary>
        static Vector3 StandPoint(InteractableObject io)
        {
            Vector3 c = io.GetWorldBounds().center;
            Vector3 dir = new Vector3(-c.x, 0f, -c.z);
            if (dir.sqrMagnitude < 0.01f) dir = Vector3.back;
            Vector3 eye = c + dir.normalized * 2.3f;
            eye.x = Mathf.Clamp(eye.x, -4.3f, 4.3f);
            eye.z = Mathf.Clamp(eye.z, -3.6f, 3.6f);
            eye.y = 0.05f;
            return eye;
        }

        /// <summary>Stands in front of the object, looks at it, lets the hover settle, then selects it (what a click does).</summary>
        IEnumerator LookAndSelect(string id, float dwellSeconds)
        {
            var io = Find(id);
            _fpc.Teleport(StandPoint(io), 0f);
            _fpc.LookAtPoint(io.GetWorldBounds().center);
            yield return Hold(dwellSeconds * 0.5f);
            _ints.Select(io);
            yield return Hold(dwellSeconds * 0.5f);
        }

        /// <summary>Runs the active identification quiz: the first few answers through look-and-select (with one wrong answer and a hint), the rest quickly.</summary>
        IEnumerator RunQuiz(int realQuestions, int wrongAtQuestion)
        {
            int qn = 0;
            while (_tm.Mode == AppMode.Quiz && qn < 40)
            {
                var q = _tm.Quiz.Current;
                if (qn < realQuestions)
                {
                    if (qn == wrongAtQuestion)
                    {
                        string wrongId = q.correctObjectId == "bed_hospital_standard_a01" ? "cabinet_for_drugs_a01" : "bed_hospital_standard_a01";
                        if (Find(wrongId) != null)
                        {
                            yield return LookAndSelect(wrongId, 1.4f);
                            _tm.RequestHint();
                            yield return Hold(2f);
                        }
                    }
                    yield return LookAndSelect(q.correctObjectId, 1.4f);
                    yield return Hold(1f);
                }
                else
                {
                    _tm.SubmitSelection(q.correctObjectId);
                    yield return Frame(2);
                }
                _tm.ContinueQuiz();
                yield return Frame(2);
                qn++;
            }
        }

        // ---------------- VR helpers (scripted controllers) ----------------
        Vector3 HandWorld() { return _fpc.transform.TransformPoint(new Vector3(0.25f, 1.35f, 0.3f)); }

        IEnumerator VrPoint(ScriptedXrInput input, InteractableObject io, float seconds)
        {
            Vector3 c = io.GetWorldBounds().center;
            _fpc.Teleport(StandPoint(io), 0f);
            _fpc.LookAtPoint(c);
            input.AimRightHandAt(_rig.transform, c, HandWorld());
            yield return Hold(seconds);
        }

        IEnumerator VrPress(ScriptedXrInput input, Action<ScriptedXrInput> press, float afterSeconds)
        {
            press(input);
            yield return Hold(0.2f);
            input.State.rightTrigger = input.State.rightGrip = input.State.rightA = input.State.rightB = input.State.leftX = input.State.leftY =
                input.State.menu = input.State.leftStickClick = input.State.rightStickClick = false;
            yield return Hold(afterSeconds);
        }

        IEnumerator VrGrabBoxAndPlace(ScriptedXrInput input)
        {
            var box = Find("box_corrugated_brown_b20");
            _fpc.Teleport(new Vector3(0.4f, 0.05f, 1.5f), 0f);
            _fpc.LookAtPoint(box.GetWorldBounds().center);
            input.AimRightHandAt(_rig.transform, box.GetWorldBounds().center, HandWorld());
            yield return Hold(1.2f);
            yield return VrPress(input, i => i.State.rightGrip = true, 1.8f);             // grip grabs
            var zone = UnityEngine.Object.FindFirstObjectByType<PlacementZone>();
            Vector3 target = zone.transform.position + Vector3.up * 0.1f;
            box.Body.position = target; box.transform.position = target;
            Physics.SyncTransforms();
            yield return VrPress(input, i => i.State.rightGrip = true, 2f);               // grip again releases onto the work surface
            box.ResetToStart();
            yield return Hold(0.5f);
        }

        void SaveFrame()
        {
            var cam = Camera.main;
            if (cam == null) return;
            var canvas = _hud.GetComponentInChildren<Canvas>();
            if (!_hud.IsWorldSpace)
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = cam;
                canvas.planeDistance = 0.5f;
            }
            cam.targetTexture = _rt;
            cam.Render();
            RenderTexture.active = _rt;
            _tex.ReadPixels(new Rect(0, 0, _rt.width, _rt.height), 0, 0);
            _tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;
            File.WriteAllBytes(Path.Combine(_dir, "f_" + _frame.ToString("00000") + ".jpg"), _tex.EncodeToJPG(90));
            _frame++;
        }
    }
}
