using MedSimXR.EditorTools;
using MedSimXR.Interaction;
using MedSimXR.XR;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEngine;
using UnityEngine.XR.Management;

namespace MedSimXR.Tests
{
    /// <summary>Project configuration and scene wiring for Phase 3. Says nothing about real headsets.</summary>
    public class Phase3XrEditTests
    {
        const string ScenePath = "Assets/Scenes/Hospital_Phase1.unity";

        [Test]
        public void OpenXR_LoaderIsAssignedForStandalone_AndNotAutoStarted()
        {
            Assert.IsTrue(XrSetup.LoaderAssigned(BuildTargetGroup.Standalone), "OpenXR loader must be assigned for Windows standalone");
            var general = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Standalone);
            Assert.IsNotNull(general);
            Assert.IsFalse(general.InitManagerOnStart, "XR is started by XrModeController, so desktop mode never depends on a headset");
        }

        [Test]
        public void OpenXR_TouchControllerProfileIsEnabled()
        {
            Assert.IsTrue(XrSetup.TouchProfileEnabled(BuildTargetGroup.Standalone), "without an interaction profile OpenXR delivers no controller input");
        }

        [Test]
        public void Scene_HasVrRig_WiredAndInactiveByDefault()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var rig = Object.FindFirstObjectByType<VrRig>(FindObjectsInactive.Include);
            Assert.IsNotNull(rig, "VrRig missing from the scene");
            Assert.IsFalse(rig.gameObject.activeSelf, "VR rig must be off until VR starts");
            Assert.IsNotNull(rig.xrCamera);
            Assert.IsFalse(rig.xrCamera.enabled);
            Assert.IsNotNull(rig.leftHand);
            Assert.IsNotNull(rig.rightHand);
            Assert.IsNotNull(rig.holdAttach);
            Assert.IsNotNull(rig.laser);
            Assert.IsNotNull(rig.interaction);
            Assert.IsNotNull(rig.training);
            Assert.IsNotNull(rig.hud);
            Assert.IsNotNull(rig.body);
            Assert.IsTrue(rig.transform.IsChildOf(rig.body.transform), "rig must sit under the player so the CharacterController collides");
            Assert.AreSame(rig.holdAttach.parent, rig.rightHand);
            foreach (var c in rig.GetComponentsInChildren<Collider>(true)) Assert.Fail("rig visuals must not have colliders: " + c.name);
        }

        [Test]
        public void Scene_ModeControllerIsWired_AndDesktopCameraStaysMain()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var mc = Object.FindFirstObjectByType<XrModeController>();
            Assert.IsNotNull(mc);
            Assert.IsNotNull(mc.rig);
            Assert.IsNotNull(mc.player);
            Assert.IsNotNull(mc.desktopCamera);
            Assert.IsNotNull(mc.interaction);
            Assert.IsNotNull(mc.training);
            Assert.IsNotNull(mc.hud);
            Assert.AreEqual("MainCamera", mc.desktopCamera.tag);
            Assert.IsTrue(mc.desktopCamera.enabled);
            Assert.AreEqual("Untagged", mc.rig.xrCamera.tag, "only one camera may be tagged MainCamera");
            Assert.IsTrue(mc.interaction.useKeyboard);
            Assert.IsNull(mc.interaction.rayOrigin);
        }

        [Test]
        public void Scene_StillHasExactlyOneAudioListenerEnabled()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            int enabled = 0;
            foreach (var l in Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Include, FindObjectsSortMode.None)) if (l.enabled) enabled++;
            Assert.AreEqual(1, enabled);
        }
    }
}
