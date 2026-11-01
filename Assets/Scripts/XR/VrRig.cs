using MedSimXR.Core.UI;
using MedSimXR.Interaction;
using MedSimXR.Training;
using UnityEngine;

namespace MedSimXR.XR
{
    /// <summary>
    /// Head and hand rig plus the VR control scheme. Sits under the Player so the player's CharacterController
    /// provides collision. Drives the same InteractionSystem and TrainingManager the desktop mode uses.
    ///
    /// Right trigger select | right grip grab/release | A inspect / confirm | B reset | left X hint | left Y menu |
    /// menu button pause | left stick move or choose option | right stick X snap turn, Y rotate held object |
    /// left stick click leave module | right stick click report (module C).
    /// </summary>
    public class VrRig : MonoBehaviour
    {
        public Camera xrCamera;
        public Transform leftHand, rightHand, holdAttach;
        public LineRenderer laser;
        public InteractionSystem interaction;
        public TrainingManager training;
        public HudView hud;
        public CharacterController body;

        public float moveSpeed = 2.0f;
        public float snapTurnDegrees = 30f;
        public float stickThreshold = 0.65f;

        public IXrInputSource Source { get; set; }
        public XrInputState Last { get; private set; }
        public bool Active { get { return gameObject.activeInHierarchy; } }

        XrInputState _prev;
        bool _snapLatched, _optionLatched;
        float _dt = -1f;

        /// <summary>Test hook: when set, used instead of Time.deltaTime for locomotion.</summary>
        public float fixedDeltaTime = -1f;

        void Awake() { _prev = XrInputState.Neutral(); }

        void Update()
        {
            if (Source == null) return;
            float dt = fixedDeltaTime > 0f ? fixedDeltaTime : Time.deltaTime;
            _dt = dt;
            var s = Source.Read(dt);
            Last = s;
            ApplyPoses(s);
            HandleButtons(s);
            HandleSticks(s, dt);
            UpdateLaser();
            _prev = s;
        }


        void ApplyPoses(XrInputState s)
        {
            if (xrCamera != null && s.headTracked) xrCamera.transform.SetLocalPositionAndRotation(s.headPos, s.headRot);
            else if (xrCamera != null) xrCamera.transform.SetLocalPositionAndRotation(s.headPos, s.headRot);
            if (rightHand != null && s.rightTracked) rightHand.SetLocalPositionAndRotation(s.rightPos, s.rightRot);
            if (leftHand != null && s.leftTracked) leftHand.SetLocalPositionAndRotation(s.leftPos, s.leftRot);
        }

        static bool Down(bool now, bool before) { return now && !before; }

        void HandleButtons(XrInputState s)
        {
            if (interaction == null || training == null) return;
            if (Down(s.menu, _prev.menu)) training.TogglePause();
            if (training.Mode == AppMode.Paused) return;

            if (Down(s.leftY, _prev.leftY)) training.VrMenu();
            if (Down(s.leftX, _prev.leftX)) training.VrHint();
            if (Down(s.leftStickClick, _prev.leftStickClick)) training.VrQuit();
            if (Down(s.rightStickClick, _prev.rightStickClick)) training.OpenReportPrompt();
            if (Down(s.rightA, _prev.rightA))
            {
                if (!training.VrConfirm() && interaction.Hovered != null) interaction.Inspect(interaction.Hovered);
            }
            if (!interaction.inputEnabled) return;
            if (Down(s.rightTrigger, _prev.rightTrigger) && interaction.Hovered != null) interaction.Select(interaction.Hovered);
            if (Down(s.rightGrip, _prev.rightGrip))
            {
                if (interaction.Held != null) interaction.Release();
                else if (interaction.Hovered != null) interaction.TryGrab(interaction.Hovered);
            }
            if (Down(s.rightB, _prev.rightB)) interaction.ResetHeldOrHovered();
        }

        void HandleSticks(XrInputState s, float dt)
        {
            if (training == null) return;
            if (training.Mode == AppMode.Paused) return;

            // Option lists (menu, quiz answers, report prompt) take the left stick.
            if (training.VrOptionCount > 0)
            {
                float y = s.leftStick.y;
                if (Mathf.Abs(y) > stickThreshold)
                {
                    if (!_optionLatched) { training.VrOptionMove(y > 0f ? -1 : 1); _optionLatched = true; }
                }
                else _optionLatched = false;
            }
            else
            {
                _optionLatched = false;
                if (body != null && training.Mode != AppMode.Results && training.Mode != AppMode.Knowledge && training.Mode != AppMode.Menu && s.leftStick.sqrMagnitude > 0.04f)
                {
                    var head = xrCamera != null ? xrCamera.transform : transform;
                    float headYaw = head.localEulerAngles.y;
                    Vector3 local = Quaternion.Euler(0f, headYaw, 0f) * new Vector3(s.leftStick.x, 0f, s.leftStick.y);
                    Vector3 world = body.transform.TransformDirection(local);
                    body.SimpleMove(world * moveSpeed);   // SimpleMove applies deltaTime itself
                }
            }

            // Snap turn on the right stick.
            if (Mathf.Abs(s.rightStick.x) > stickThreshold)
            {
                if (!_snapLatched && body != null)
                {
                    body.transform.Rotate(0f, Mathf.Sign(s.rightStick.x) * snapTurnDegrees, 0f);
                    _snapLatched = true;
                }
            }
            else _snapLatched = false;

            if (interaction != null && interaction.Held != null && Mathf.Abs(s.rightStick.y) > 0.2f)
                interaction.RotateHeld(s.rightStick.y * dt * 90f * 0.2f);
        }

        void UpdateLaser()
        {
            if (laser == null || rightHand == null || interaction == null) return;
            bool show = interaction.inputEnabled;
            laser.enabled = show;
            if (!show) return;
            laser.positionCount = 2;
            laser.SetPosition(0, rightHand.position);
            laser.SetPosition(1, interaction.RayEnd);
        }

        void OnDisable()
        {
            if (laser != null) laser.enabled = false;
        }
    }
}
