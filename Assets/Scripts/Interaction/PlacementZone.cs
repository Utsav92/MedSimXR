using System;
using UnityEngine;

namespace MedSimXR.Interaction
{
    /// <summary>
    /// A designated destination (for example a work surface). An object counts as placed when the
    /// correct object is resting inside the zone and no longer held.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class PlacementZone : MonoBehaviour
    {
        public string zoneId;
        public string acceptedObjectId;   // empty = any grabbable object

        public static event Action<PlacementZone, InteractableObject> OnPlaced;

        void Reset() { GetComponent<BoxCollider>().isTrigger = true; }

        void OnTriggerStay(Collider other)
        {
            var io = other.GetComponentInParent<InteractableObject>();
            if (io == null || io.Body == null) return;
            if (io.State == InteractionState.Held || io.State == InteractionState.Placed) return;
            if (!string.IsNullOrEmpty(acceptedObjectId) && io.objectId != acceptedObjectId) return;
            if (io.Body.linearVelocity.sqrMagnitude > 0.01f) return; // still moving
            io.NotifyPlaced(zoneId);
            OnPlaced?.Invoke(this, io);
        }

        void OnTriggerExit(Collider other)
        {
            var io = other.GetComponentInParent<InteractableObject>();
            if (io != null && io.State == InteractionState.Placed) io.ResetStateIdle();
        }
    }
}
