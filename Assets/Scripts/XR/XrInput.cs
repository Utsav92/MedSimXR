using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MedSimXR.XR
{
    /// <summary>One frame of headset and controller input, in rig-local space. Poses are only applied when tracked.</summary>
    public struct XrInputState
    {
        public bool headTracked;
        public Vector3 headPos;
        public Quaternion headRot;

        public bool rightTracked;
        public Vector3 rightPos;
        public Quaternion rightRot;     // aim pose: forward is the pointing ray

        public bool leftTracked;
        public Vector3 leftPos;
        public Quaternion leftRot;

        public bool rightTrigger, rightGrip, leftGrip;
        public bool rightA, rightB, leftX, leftY, menu;
        public bool leftStickClick, rightStickClick;
        public Vector2 leftStick, rightStick;

        public static XrInputState Neutral()
        {
            return new XrInputState
            {
                headPos = new Vector3(0f, 1.65f, 0f), headRot = Quaternion.identity,
                rightPos = new Vector3(0.25f, 1.35f, 0.3f), rightRot = Quaternion.identity,
                leftPos = new Vector3(-0.25f, 1.35f, 0.3f), leftRot = Quaternion.identity
            };
        }
    }

    public interface IXrInputSource : IDisposable
    {
        string Description { get; }
        XrInputState Read(float deltaTime);
    }

    /// <summary>Fully scripted input. Used by automated tests.</summary>
    public class ScriptedXrInput : IXrInputSource
    {
        public XrInputState State = XrInputState.Neutral();
        public string Description { get { return "scripted"; } }
        public XrInputState Read(float deltaTime) { return State; }
        public void Dispose() { }

        /// <summary>Points the right controller from the given position at a world-space target, expressed relative to a rig transform.</summary>
        public void AimRightHandAt(Transform rig, Vector3 worldTarget, Vector3 worldHandPos)
        {
            State.rightTracked = true;
            State.rightPos = rig.InverseTransformPoint(worldHandPos);
            Vector3 dirLocal = rig.InverseTransformDirection((worldTarget - worldHandPos).normalized);
            State.rightRot = Quaternion.LookRotation(dirLocal, Vector3.up);
        }
    }

    /// <summary>
    /// Real headset input through Input System actions. Bindings use XR usages ({RightHand}, {TriggerButton}, ...), which
    /// OpenXR interaction profiles (Oculus/Meta Touch and others) map onto. NOT verified on hardware in this project.
    /// </summary>
    public class XrActionInputSource : IXrInputSource
    {
        InputAction _headPos, _headRot;
        InputAction _rPtrPos, _rPtrRot, _rDevPos, _rDevRot, _lPtrPos, _lPtrRot, _lDevPos, _lDevRot;
        InputAction _trigger, _rGrip, _lGrip, _a, _b, _x, _y, _menu, _lClick, _rClick, _lStick, _rStick;

        public string Description { get { return "OpenXR actions"; } }

        static InputAction Value(string path, string type = null)
        {
            var a = new InputAction(type: InputActionType.Value, binding: path);
            a.Enable();
            return a;
        }

        static InputAction Button(string path)
        {
            var a = new InputAction(type: InputActionType.Button, binding: path);
            a.Enable();
            return a;
        }

        public XrActionInputSource()
        {
            _headPos = Value("<XRHMD>/centerEyePosition");
            _headRot = Value("<XRHMD>/centerEyeRotation");
            _rPtrPos = Value("<XRController>{RightHand}/pointerPosition");
            _rPtrRot = Value("<XRController>{RightHand}/pointerRotation");
            _rDevPos = Value("<XRController>{RightHand}/devicePosition");
            _rDevRot = Value("<XRController>{RightHand}/deviceRotation");
            _lPtrPos = Value("<XRController>{LeftHand}/pointerPosition");
            _lPtrRot = Value("<XRController>{LeftHand}/pointerRotation");
            _lDevPos = Value("<XRController>{LeftHand}/devicePosition");
            _lDevRot = Value("<XRController>{LeftHand}/deviceRotation");
            _trigger = Button("<XRController>{RightHand}/{TriggerButton}");
            _rGrip = Button("<XRController>{RightHand}/{GripButton}");
            _lGrip = Button("<XRController>{LeftHand}/{GripButton}");
            _a = Button("<XRController>{RightHand}/{PrimaryButton}");
            _b = Button("<XRController>{RightHand}/{SecondaryButton}");
            _x = Button("<XRController>{LeftHand}/{PrimaryButton}");
            _y = Button("<XRController>{LeftHand}/{SecondaryButton}");
            _menu = Button("<XRController>{LeftHand}/{Menu}");
            _lClick = Button("<XRController>{LeftHand}/{Primary2DAxisClick}");
            _rClick = Button("<XRController>{RightHand}/{Primary2DAxisClick}");
            _lStick = Value("<XRController>{LeftHand}/{Primary2DAxis}");
            _rStick = Value("<XRController>{RightHand}/{Primary2DAxis}");
        }

        static bool Has(InputAction a) { return a.activeControl != null; }

        public XrInputState Read(float deltaTime)
        {
            var s = XrInputState.Neutral();
            if (Has(_headPos) && Has(_headRot)) { s.headTracked = true; s.headPos = _headPos.ReadValue<Vector3>(); s.headRot = _headRot.ReadValue<Quaternion>(); }

            if (Has(_rPtrPos) && Has(_rPtrRot)) { s.rightTracked = true; s.rightPos = _rPtrPos.ReadValue<Vector3>(); s.rightRot = _rPtrRot.ReadValue<Quaternion>(); }
            else if (Has(_rDevPos) && Has(_rDevRot)) { s.rightTracked = true; s.rightPos = _rDevPos.ReadValue<Vector3>(); s.rightRot = _rDevRot.ReadValue<Quaternion>(); }
            if (Has(_lPtrPos) && Has(_lPtrRot)) { s.leftTracked = true; s.leftPos = _lPtrPos.ReadValue<Vector3>(); s.leftRot = _lPtrRot.ReadValue<Quaternion>(); }
            else if (Has(_lDevPos) && Has(_lDevRot)) { s.leftTracked = true; s.leftPos = _lDevPos.ReadValue<Vector3>(); s.leftRot = _lDevRot.ReadValue<Quaternion>(); }

            s.rightTrigger = _trigger.IsPressed();
            s.rightGrip = _rGrip.IsPressed();
            s.leftGrip = _lGrip.IsPressed();
            s.rightA = _a.IsPressed(); s.rightB = _b.IsPressed();
            s.leftX = _x.IsPressed(); s.leftY = _y.IsPressed();
            s.menu = _menu.IsPressed();
            s.leftStickClick = _lClick.IsPressed(); s.rightStickClick = _rClick.IsPressed();
            if (Has(_lStick)) s.leftStick = _lStick.ReadValue<Vector2>();
            if (Has(_rStick)) s.rightStick = _rStick.ReadValue<Vector2>();
            return s;
        }

        public void Dispose()
        {
            foreach (var a in new[] { _headPos, _headRot, _rPtrPos, _rPtrRot, _rDevPos, _rDevRot, _lPtrPos, _lPtrRot, _lDevPos, _lDevRot,
                                      _trigger, _rGrip, _lGrip, _a, _b, _x, _y, _menu, _lClick, _rClick, _lStick, _rStick })
                if (a != null) { a.Disable(); a.Dispose(); }
        }
    }

    /// <summary>
    /// Desktop stand-in for a headset and two controllers, so the VR code path can be exercised without hardware.
    /// Mouse = head look (the right controller points where you look), LMB = trigger, WASD = left stick,
    /// Left/Right arrows = right stick (snap turn), Up/Down arrows = right stick Y, G = grip, Space/Enter = A, R = B,
    /// H = X, Tab = Y, Esc = menu, X = left stick click, N = right stick click.
    /// This is a simulation of controls, not of tracking quality, latency or comfort.
    /// </summary>
    public class XrKeyboardSimulator : IXrInputSource
    {
        float _yaw, _pitch;
        public string Description { get { return "keyboard/mouse VR simulator"; } }

        public XrInputState Read(float deltaTime)
        {
            var s = XrInputState.Neutral();
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (mouse != null)
            {
                Vector2 d = mouse.delta.ReadValue() * 0.1f;
                _yaw += d.x;
                _pitch = Mathf.Clamp(_pitch - d.y, -80f, 80f);
                s.rightTrigger = mouse.leftButton.isPressed;
            }
            s.headTracked = true;
            s.headRot = Quaternion.Euler(_pitch, _yaw, 0f);
            s.rightTracked = true;
            s.rightRot = s.headRot;
            s.rightPos = s.headPos + s.headRot * new Vector3(0.2f, -0.25f, 0.3f);
            s.leftTracked = true;
            s.leftRot = s.headRot;
            s.leftPos = s.headPos + s.headRot * new Vector3(-0.2f, -0.25f, 0.3f);
            if (kb != null)
            {
                s.leftStick = new Vector2((kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f), (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f));
                s.rightStick = new Vector2((kb.rightArrowKey.isPressed ? 1f : 0f) - (kb.leftArrowKey.isPressed ? 1f : 0f),
                                           (kb.upArrowKey.isPressed ? 1f : 0f) - (kb.downArrowKey.isPressed ? 1f : 0f));
                s.rightGrip = kb.gKey.isPressed;
                s.rightA = kb.spaceKey.isPressed || kb.enterKey.isPressed;
                s.rightB = kb.rKey.isPressed;
                s.leftX = kb.hKey.isPressed;
                s.leftY = kb.tabKey.isPressed;
                s.menu = kb.escapeKey.isPressed;
                s.leftStickClick = kb.xKey.isPressed;
                s.rightStickClick = kb.nKey.isPressed;
            }
            return s;
        }

        public void Dispose() { }
    }
}
