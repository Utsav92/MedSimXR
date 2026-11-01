using UnityEngine;
using UnityEngine.InputSystem;

namespace MedSimXR.Interaction
{
    /// <summary>Desktop mode locomotion: WASD to move, mouse to look.</summary>
    [RequireComponent(typeof(CharacterController))]
    public class FirstPersonController : MonoBehaviour
    {
        public Transform cameraTransform;
        public float moveSpeed = 3.0f;
        public float lookSensitivity = 0.1f;
        public bool inputEnabled = true;

        CharacterController _cc;
        float _pitch;

        void Awake() { _cc = GetComponent<CharacterController>(); }

        void Update()
        {
            if (!inputEnabled) return;
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null || mouse == null) return;

            Vector2 look = mouse.delta.ReadValue() * lookSensitivity;
            transform.Rotate(0f, look.x, 0f);
            _pitch = Mathf.Clamp(_pitch - look.y, -80f, 80f);
            if (cameraTransform != null) cameraTransform.localEulerAngles = new Vector3(_pitch, 0f, 0f);

            Vector3 move = Vector3.zero;
            if (kb.wKey.isPressed) move += transform.forward;
            if (kb.sKey.isPressed) move -= transform.forward;
            if (kb.dKey.isPressed) move += transform.right;
            if (kb.aKey.isPressed) move -= transform.right;
            _cc.SimpleMove(move.normalized * moveSpeed);
        }

        /// <summary>Test/automation hook: move the player without keyboard input.</summary>
        public void Teleport(Vector3 position, float yaw)
        {
            _cc.enabled = false;
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            _cc.enabled = true;
        }

        public void LookAtPoint(Vector3 point)
        {
            Vector3 from = cameraTransform != null ? cameraTransform.position : transform.position;
            Vector3 d = point - from;
            transform.rotation = Quaternion.Euler(0f, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, 0f);
            float horiz = new Vector2(d.x, d.z).magnitude;
            _pitch = -Mathf.Atan2(d.y, horiz) * Mathf.Rad2Deg;
            if (cameraTransform != null) cameraTransform.localEulerAngles = new Vector3(_pitch, 0f, 0f);
        }
    }
}
