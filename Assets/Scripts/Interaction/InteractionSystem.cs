using System;
using MedSimXR.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MedSimXR.Interaction
{
    /// <summary>
    /// Desktop interaction: look at an object to highlight it.
    /// E inspect, Left click select, G grab/release, R reset, mouse wheel rotates the held object.
    /// </summary>
    public class InteractionSystem : MonoBehaviour
    {
        public Camera viewCamera;
        public Transform holdPoint;
        /// <summary>VR: the ray comes from this transform (the controller) instead of the camera.</summary>
        public Transform rayOrigin;
        /// <summary>False in VR, where the VR rig calls Select/Inspect/TryGrab/Release itself.</summary>
        public bool useKeyboard = true;
        public Vector3 RayEnd { get; private set; }
        public float reach = 3.5f;
        public bool inputEnabled = true;
        public Material highlightMaterial;

        public InteractableObject Hovered { get; private set; }
        public InteractableObject Held { get; private set; }

        public event Action<InteractableObject> OnSelected;
        public event Action<InteractableObject> OnInspected;
        public event Action<InteractableObject> OnHoverChanged;

        Transform _highlight;
        float _yaw;

        void Start()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "HighlightBox";
            Destroy(go.GetComponent<Collider>());
            if (highlightMaterial != null) go.GetComponent<Renderer>().sharedMaterial = highlightMaterial;
            _highlight = go.transform;
            go.SetActive(false);
        }

        void Update()
        {
            UpdateHover();
            if (!inputEnabled || !useKeyboard) return;
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null || mouse == null) return;

            if (mouse.leftButton.wasPressedThisFrame && Hovered != null) Select(Hovered);
            if (kb.eKey.wasPressedThisFrame && Hovered != null) Inspect(Hovered);
            if (kb.gKey.wasPressedThisFrame) { if (Held != null) Release(); else if (Hovered != null) TryGrab(Hovered); }
            if (kb.rKey.wasPressedThisFrame) { if (Held != null) ResetHeld(); else if (Hovered != null && Hovered.canGrab) Hovered.ResetToStart(); }
            if (Held != null) _yaw += mouse.scroll.ReadValue().y * 0.2f;
        }

        void FixedUpdate()
        {
            if (Held == null || holdPoint == null) return;
            var rb = Held.Body;
            Vector3 to = holdPoint.position - rb.worldCenterOfMass;
            if (to.magnitude > 2.5f) { Release(); return; } // stuck behind geometry
            rb.linearVelocity = Vector3.ClampMagnitude(to * 14f, 9f);
            var target = Quaternion.Euler(0f, holdPoint.eulerAngles.y + _yaw, 0f);
            rb.MoveRotation(Quaternion.Slerp(rb.rotation, target, 0.25f));
            rb.angularVelocity = Vector3.zero;
        }

        void UpdateHover()
        {
            InteractableObject found = null;
            var originT = rayOrigin != null ? rayOrigin : (viewCamera != null ? viewCamera.transform : null);
            float bestDist = reach;
            if (inputEnabled && originT != null)
            {
                var ray = new Ray(originT.position, originT.forward);
                var hits = Physics.RaycastAll(ray, reach, ~0, QueryTriggerInteraction.Ignore);
                float best = float.MaxValue;
                foreach (var h in hits)
                {
                    if (IsSelf(h.collider)) continue;   // the camera sits inside the player's own capsule
                    var io = h.collider.GetComponentInParent<InteractableObject>();
                    if (io == null || io == Held) continue;
                    if (h.distance < best) { best = h.distance; found = io; }
                }
                if (found != null) bestDist = best;
                // A wall or other solid in front of the object blocks it.
                if (found != null)
                {
                    foreach (var h in hits)
                        if (h.distance < best && !IsSelf(h.collider) && h.collider.GetComponentInParent<InteractableObject>() == null) { found = null; break; }
                }
            }
            SetHovered(found);
            if (originT != null) RayEnd = originT.position + originT.forward * bestDist;
            if (_highlight != null)
            {
                var target = Held != null ? Held : Hovered;
                bool show = target != null;
                if (_highlight.gameObject.activeSelf != show) _highlight.gameObject.SetActive(show);
                if (show)
                {
                    var b = target.GetWorldBounds();
                    _highlight.position = b.center;
                    _highlight.localScale = b.size + Vector3.one * 0.04f;
                }
            }
        }

        CharacterController _self;
        bool IsSelf(Collider c)
        {
            if (_self == null) _self = GetComponentInParent<CharacterController>();
            return _self != null && c.transform.IsChildOf(_self.transform);
        }

        public void SetHovered(InteractableObject io)
        {
            if (io == Hovered) return;
            Hovered = io;
            OnHoverChanged?.Invoke(io);
        }

        public void Select(InteractableObject io)
        {
            SessionLog.Log("select", io.objectId);
            OnSelected?.Invoke(io);
        }

        public void Inspect(InteractableObject io)
        {
            if (!io.canInspect) return;
            SessionLog.Log("inspect", io.objectId);
            OnInspected?.Invoke(io);
        }

        public bool TryGrab(InteractableObject io)
        {
            if (Held != null || !io.canGrab || io.isFixed || io.Body == null)
            {
                SessionLog.Log("grab_denied", io.objectId, io.isFixed ? "fixed equipment" : "not grabbable");
                return false;
            }
            Held = io;
            _yaw = 0f;
            io.Body.useGravity = false;
            io.Body.linearDamping = 6f;
            io.Body.angularDamping = 6f;
            io.NotifyGrabbed();
            return true;
        }

        public void Release()
        {
            if (Held == null) return;
            var io = Held;
            Held = null;
            io.Body.useGravity = true;
            io.Body.linearDamping = 0f;
            io.Body.angularDamping = 0.05f;
            io.NotifyReleased();
        }

        /// <summary>R key behaviour: reset the held object, or the grabbable one under the ray.</summary>
        public void ResetHeldOrHovered()
        {
            if (Held != null) ResetHeld();
            else if (Hovered != null && Hovered.canGrab) Hovered.ResetToStart();
        }

        public void RotateHeld(float degrees) { if (Held != null) _yaw += degrees; }

        void ResetHeld()
        {
            var io = Held;
            Release();
            io.ResetToStart();
        }
    }
}
