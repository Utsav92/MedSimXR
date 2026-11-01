using MedSimXR.Core.UI;
using MedSimXR.Equipment;
using MedSimXR.Interaction;
using MedSimXR.Training;
using UnityEngine;

namespace MedSimXR.Core
{
    /// <summary>
    /// Wires the scene together at start-up: applies equipment data to every interactable,
    /// builds world info panels, and connects the HUD, player and training manager.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class GameBootstrap : MonoBehaviour
    {
        public TrainingManager training;
        public HudView hud;
        public InteractionSystem interaction;
        public FirstPersonController player;

        void Awake()
        {
            if (training == null) training = FindFirstObjectByType<TrainingManager>();
            if (hud == null) hud = FindFirstObjectByType<HudView>();
            if (interaction == null) interaction = FindFirstObjectByType<InteractionSystem>();
            if (player == null) player = FindFirstObjectByType<FirstPersonController>();
            training.hud = hud;
            training.interaction = interaction;
            training.player = player;
        }

        void Start()
        {
            var registry = training.Equipment;
            foreach (var io in FindObjectsByType<InteractableObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                EquipmentEntry e;
                if (registry.TryGet(io.objectId, out e)) io.ApplyData(e);
                else Debug.LogError("Interactable has no equipment entry: " + io.objectId, io);
            }
            foreach (var p in FindObjectsByType<WorldInfoPanel>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                EquipmentEntry e;
                if (registry.TryGet(p.equipmentId, out e)) p.Build(e);
            }
            SessionLog.Log("scene_ready", "", "interactables=" + FindObjectsByType<InteractableObject>(FindObjectsSortMode.None).Length);
        }
    }
}
