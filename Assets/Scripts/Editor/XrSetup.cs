using System.IO;
using UnityEditor;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.Interactions;

namespace MedSimXR.EditorTools
{
    /// <summary>
    /// Configures XR Plug-in Management for Windows standalone: OpenXR loader assigned, controller profiles enabled,
    /// and XR NOT started automatically by Unity (XrModeController starts it, so a missing headset never breaks desktop mode).
    /// Android/Quest is configured in Phase 4, once the Android Build Support module is installed.
    /// </summary>
    public static class XrSetup
    {
        public const string SettingsPath = "Assets/XR/XRGeneralSettingsPerBuildTarget.asset";
        public static readonly string OpenXRLoaderName = typeof(OpenXRLoader).FullName;

        [MenuItem("MedSim XR/Configure OpenXR (Windows)")]
        public static void Configure()
        {
            var group = BuildTargetGroup.Standalone;
            XRGeneralSettingsPerBuildTarget perBuild;
            EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out perBuild);
            if (perBuild == null)
            {
                perBuild = AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(SettingsPath);
                if (perBuild == null)
                {
                    Directory.CreateDirectory(Path.Combine(Application.dataPath, "XR"));
                    perBuild = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                    AssetDatabase.CreateAsset(perBuild, SettingsPath);
                }
                EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, perBuild, true);
            }
            if (!perBuild.HasSettingsForBuildTarget(group)) perBuild.CreateDefaultSettingsForBuildTarget(group);
            if (!perBuild.HasManagerSettingsForBuildTarget(group)) perBuild.CreateDefaultManagerSettingsForBuildTarget(group);

            var general = perBuild.SettingsForBuildTarget(group);
            general.InitManagerOnStart = false;
            var manager = perBuild.ManagerSettingsForBuildTarget(group);
            if (!XRPackageMetadataStore.IsLoaderAssigned(OpenXRLoaderName, group))
            {
                if (!XRPackageMetadataStore.AssignLoader(manager, OpenXRLoaderName, group))
                    Debug.LogError("[MedSim] Could not assign the OpenXR loader for Standalone.");
            }
            EditorUtility.SetDirty(general);
            EditorUtility.SetDirty(manager);
            EditorUtility.SetDirty(perBuild);

            EnableProfiles(group);
            AssetDatabase.SaveAssets();
            Debug.Log("[MedSim] OpenXR configured for Standalone (loader assigned: " + XRPackageMetadataStore.IsLoaderAssigned(OpenXRLoaderName, group) + ")");
        }

        static void EnableProfiles(BuildTargetGroup group)
        {
            FeatureHelpers.RefreshFeatures(group);   // creates the feature instances (interaction profiles etc.) if they do not exist yet
            var oxr = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
            if (oxr == null) { Debug.LogWarning("[MedSim] OpenXR package settings not available yet; controller profiles not enabled."); return; }
            int n = 0;
            var oculus = oxr.GetFeature<OculusTouchControllerProfile>();
            if (oculus != null) { oculus.enabled = true; EditorUtility.SetDirty(oculus); n++; }
            var pro = oxr.GetFeature<MetaQuestTouchProControllerProfile>();
            if (pro != null) { pro.enabled = true; EditorUtility.SetDirty(pro); n++; }
            var simple = oxr.GetFeature<KHRSimpleControllerProfile>();
            if (simple != null) { simple.enabled = true; EditorUtility.SetDirty(simple); n++; }
            EditorUtility.SetDirty(oxr);
            if (n == 0) Debug.LogError("[MedSim] No OpenXR controller profile could be enabled; controller input would not work.");
            else Debug.Log("[MedSim] enabled " + n + " OpenXR controller profile(s)");
        }

        public static bool TouchProfileEnabled(BuildTargetGroup group)
        {
            var oxr = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
            var f = oxr != null ? oxr.GetFeature<OculusTouchControllerProfile>() : null;
            return f != null && f.enabled;
        }

        public static bool LoaderAssigned(BuildTargetGroup group)
        {
            return XRPackageMetadataStore.IsLoaderAssigned(OpenXRLoaderName, group);
        }
    }
}
