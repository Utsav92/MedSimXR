using System;
using MedSimXR.Core;
using MedSimXR.Equipment;
using UnityEngine;

namespace MedSimXR.Interaction
{
    public enum InteractionState { Idle, Held, Placed, Dropped }

    /// <summary>
    /// Every interactive object carries one of these: unique ID, category, description, training use,
    /// allowed interactions, a reset pose, and event logging.
    /// </summary>
    public class InteractableObject : MonoBehaviour
    {
        [Header("Identity")]
        public string objectId;
        public string displayName;
        public string category;
        [TextArea] public string description;
        [TextArea] public string trainingUse;
        [TextArea] public string labelText;
        public bool isPlaceholder;
        public string sourceNote;

        [Header("Allowed interactions")]
        public bool canGrab;
        public bool canInspect = true;
        public bool isFixed = true;

        public InteractionState State { get; private set; }
        public Vector3 ResetPosition { get; private set; }
        public Quaternion ResetRotation { get; private set; }
        public Rigidbody Body { get; private set; }
        public float LastReleaseTime { get; private set; } = -100f;

        public static event Action<InteractableObject> OnDropped;

        void Awake()
        {
            Body = GetComponent<Rigidbody>();
            CaptureResetPose();
        }

        public void CaptureResetPose()
        {
            ResetPosition = transform.position;
            ResetRotation = transform.rotation;
        }

        /// <summary>Editor/build-time variant: Body is not cached yet, so trust the data flags.</summary>
        public void ApplyDataForBuild(EquipmentEntry e)
        {
            ApplyData(e);
            canGrab = e.canGrab && !e.isFixed;
        }

        public void ApplyData(EquipmentEntry e)
        {
            objectId = e.id;
            displayName = e.displayName;
            category = e.category;
            description = e.description;
            trainingUse = e.trainingUse;
            labelText = e.labelText;
            isPlaceholder = e.IsPlaceholder;
            sourceNote = e.sourceNote;
            canGrab = e.canGrab && Body != null;
            canInspect = e.canInspect;
            isFixed = e.isFixed;
        }

        public Bounds GetWorldBounds()
        {
            var rs = GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(transform.position, Vector3.one * 0.2f);
            var b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            return b;
        }

        public void NotifyGrabbed()
        {
            State = InteractionState.Held;
            SessionLog.Log("grab", objectId);
        }

        public void NotifyReleased()
        {
            State = InteractionState.Idle;
            LastReleaseTime = Time.time;
            SessionLog.Log("release", objectId, transform.position.ToString("F2"));
        }

        public void NotifyPlaced(string zoneId)
        {
            if (State == InteractionState.Placed) return;
            State = InteractionState.Placed;
            SessionLog.Log("placed", objectId, zoneId);
        }

        public void ResetStateIdle()
        {
            if (State == InteractionState.Placed) { State = InteractionState.Idle; SessionLog.Log("removed_from_zone", objectId); }
        }

        public void ResetToStart()
        {
            if (Body != null)
            {
                Body.linearVelocity = Vector3.zero;
                Body.angularVelocity = Vector3.zero;
            }
            transform.SetPositionAndRotation(ResetPosition, ResetRotation);
            if (Body != null) Body.position = ResetPosition;
            State = InteractionState.Idle;
            SessionLog.Log("reset", objectId);
        }

        void OnCollisionEnter(Collision c)
        {
            // A drop is: released recently, and then touched the floor.
            if (Body == null || State == InteractionState.Held) return;
            if (Time.time - LastReleaseTime > 6f) return;
            if (c.gameObject.name != "Floor") return;
            State = InteractionState.Dropped;
            LastReleaseTime = -100f;
            SessionLog.Log("dropped", objectId, c.relativeVelocity.magnitude.ToString("F2"));
            OnDropped?.Invoke(this);
        }

        void Update()
        {
            // Safety net: if something falls out of the world, put it back.
            if (transform.position.y < -5f && Body != null) ResetToStart();
        }
    }
}
