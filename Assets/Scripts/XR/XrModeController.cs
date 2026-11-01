using MedSimXR.Core.UI;
using MedSimXR.Interaction;
using MedSimXR.Training;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using UnityEngine.XR.Management;

namespace MedSimXR.XR
{
    /// <summary>
    /// Chooses between desktop and VR at start-up and can switch at runtime.
    /// - Desktop is the default and never needs a headset.
    /// - If an OpenXR runtime starts and a headset is active, the VR rig takes over.
    /// - "-vrsim" on the command line, or F2, switches to the keyboard/mouse VR simulator (no hardware needed).
    /// - "-novr" skips the OpenXR start-up attempt.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class XrModeController : MonoBehaviour
    {
        /// <summary>Tests set this to false before loading the scene so no OpenXR start-up is attempted.</summary>
        public static bool AutoStartXr = true;

        public VrRig rig;
        public FirstPersonController player;
        public Camera desktopCamera;
        public InteractionSystem interaction;
        public TrainingManager training;
        public HudView hud;

        public bool VrActive { get; private set; }
        public bool Simulated { get; private set; }
        public string Status { get; private set; } = "desktop";

        Transform _desktopHold;
        bool _startedLoader;
        float _checkTimer;
        bool _tryingHeadset;

        void Awake()
        {
            if (interaction != null) _desktopHold = interaction.holdPoint;
            if (rig != null) rig.gameObject.SetActive(false);
        }

        void Start()
        {
            var args = System.Environment.GetCommandLineArgs();
            bool noVr = System.Array.IndexOf(args, "-novr") >= 0;
            if (System.Array.IndexOf(args, "-vrsim") >= 0) { EnterVr(new XrKeyboardSimulator(), true); return; }
            if (AutoStartXr && !noVr) _tryingHeadset = TryStartOpenXr();
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.f2Key.wasPressedThisFrame)
            {
                if (VrActive) ExitVr();
                else EnterVr(new XrKeyboardSimulator(), true);
            }

            // A headset can become active after start-up (put on, runtime ready).
            if (_tryingHeadset && !VrActive)
            {
                _checkTimer -= Time.unscaledDeltaTime;
                if (_checkTimer <= 0f)
                {
                    _checkTimer = 1f;
                    if (XRSettings.isDeviceActive) EnterVr(new XrActionInputSource(), false);
                }
            }
        }

        bool TryStartOpenXr()
        {
            var settings = XRGeneralSettings.Instance;
            if (settings == null || settings.Manager == null || settings.Manager.activeLoaders.Count == 0)
            {
                Status = "desktop (no XR loader configured)";
                return false;
            }
            var mgr = settings.Manager;
            if (!mgr.isInitializationComplete) mgr.InitializeLoaderSync();
            if (mgr.activeLoader == null)
            {
                Status = "desktop (OpenXR runtime or headset not available)";
                return false;
            }
            mgr.StartSubsystems();
            _startedLoader = true;
            Status = "OpenXR started, waiting for a headset";
            return true;
        }

        public void EnterVr(IXrInputSource source, bool simulated)
        {
            if (rig == null) { Debug.LogWarning("No VR rig in this scene."); return; }
            if (VrActive) ExitVr();
            VrActive = true;
            Simulated = simulated;
            Status = simulated ? "VR (simulated input)" : "VR (OpenXR headset)";

            if (desktopCamera != null)
            {
                desktopCamera.enabled = false;
                var al = desktopCamera.GetComponent<AudioListener>();
                if (al != null) al.enabled = false;
                desktopCamera.tag = "Untagged";
            }
            if (player != null) player.enabled = false;

            rig.gameObject.SetActive(true);
            rig.Source = source;
            rig.xrCamera.enabled = true;
            rig.xrCamera.tag = "MainCamera";
            var xal = rig.xrCamera.GetComponent<AudioListener>();
            if (xal != null) xal.enabled = true;

            if (interaction != null)
            {
                interaction.useKeyboard = false;
                interaction.rayOrigin = rig.rightHand;
                interaction.holdPoint = rig.holdAttach;
            }
            if (hud != null) hud.SetWorldSpace(rig.xrCamera);
            if (training != null) training.VrActive = true;
            if (training != null && training.hud != null) training.SetMode(training.Mode);   // refresh crosshair etc.
        }

        public void ExitVr()
        {
            if (!VrActive) return;
            VrActive = false;
            Status = "desktop";
            if (rig != null)
            {
                rig.xrCamera.enabled = false;
                rig.xrCamera.tag = "Untagged";
                var xal = rig.xrCamera.GetComponent<AudioListener>();
                if (xal != null) xal.enabled = false;
                if (rig.Source != null) rig.Source.Dispose();
                rig.Source = null;
                rig.gameObject.SetActive(false);
            }
            if (desktopCamera != null)
            {
                desktopCamera.enabled = true;
                desktopCamera.tag = "MainCamera";
                var al = desktopCamera.GetComponent<AudioListener>();
                if (al != null) al.enabled = true;
            }
            if (player != null) player.enabled = true;
            if (interaction != null)
            {
                interaction.useKeyboard = true;
                interaction.rayOrigin = null;
                interaction.holdPoint = _desktopHold;
            }
            if (hud != null) hud.SetScreenSpace();
            if (training != null) training.VrActive = false;
            if (training != null && training.hud != null) training.SetMode(training.Mode);
        }

        void OnDestroy()
        {
            if (rig != null && rig.Source != null) rig.Source.Dispose();
            if (_startedLoader)
            {
                var mgr = XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager : null;
                if (mgr != null && mgr.isInitializationComplete)
                {
                    mgr.StopSubsystems();
                    mgr.DeinitializeLoader();
                }
            }
        }
    }
}
