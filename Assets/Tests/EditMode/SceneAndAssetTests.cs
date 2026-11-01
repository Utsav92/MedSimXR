using System.Collections.Generic;
using System.IO;
using MedSimXR.Equipment;
using MedSimXR.Interaction;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MedSimXR.Tests
{
    /// <summary>Asset validation: mesh import, colliders, scale, prefab integrity, scene wiring.</summary>
    public class SceneAndAssetTests
    {
        const string ScenePath = "Assets/Scenes/Hospital_Phase1.unity";

        static readonly Dictionary<string, Vector2> HeightRangeMeters = new Dictionary<string, Vector2>
        {
            { "chair_patient_mobile_a01", new Vector2(1.0f, 1.6f) },
            { "CT_injection_system_a01", new Vector2(1.4f, 2.2f) },
            { "box_corrugated_brown_b20", new Vector2(0.2f, 0.5f) },
            { "rack_large_empty_a02", new Vector2(1.8f, 3.5f) },
        };

        [Test]
        public void SimReadyModels_ImportedWithMeshesAndMaterials()
        {
            foreach (var e in EquipmentRegistry.LoadDefault().All)
            {
                if (e.source != "simready") continue;
                var go = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/SimReady/" + e.simreadyId + ".glb");
                Assert.IsNotNull(go, "model not imported: " + e.simreadyId);
                var rs = go.GetComponentsInChildren<MeshRenderer>();
                Assert.Greater(rs.Length, 0, e.id + " has no mesh renderers");
                foreach (var r in rs)
                {
                    Assert.IsNotNull(r.sharedMaterial, e.id + " has a null material on " + r.name);
                    Assert.IsNotNull(r.sharedMaterial.shader, e.id + " material has no shader");
                    Assert.AreNotEqual("Hidden/InternalErrorShader", r.sharedMaterial.shader.name, e.id + " material shader is broken");
                }
            }
        }

        [Test]
        public void SimReadyPrefabs_HaveColliderIdAndPlausibleScale()
        {
            foreach (var e in EquipmentRegistry.LoadDefault().All)
            {
                if (e.source != "simready") continue;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/SimReady/" + e.id + ".prefab");
                Assert.IsNotNull(prefab, "prefab missing: " + e.id);
                Assert.IsNotNull(prefab.GetComponent<Collider>(), e.id + " has no collider");
                var io = prefab.GetComponent<InteractableObject>();
                Assert.IsNotNull(io, e.id + " has no InteractableObject");
                Assert.AreEqual(e.id, io.objectId);
                Assert.AreEqual(e.canGrab && !e.isFixed, prefab.GetComponent<Rigidbody>() != null, e.id + " rigidbody should match grab policy");

                var rs = prefab.GetComponentsInChildren<Renderer>();
                var b = rs[0].bounds;
                foreach (var r in rs) b.Encapsulate(r.bounds);
                Vector2 range;
                if (HeightRangeMeters.TryGetValue(e.id, out range))
                    Assert.That(b.size.y, Is.InRange(range.x, range.y), e.id + " height " + b.size.y + " m outside plausible range");
                Assert.AreEqual(0f, b.min.y, 0.02f, e.id + " should sit on the floor");
            }
        }

        [Test]
        public void Scene_ObjectsAreUniqueCompleteAndCollidable()
        {
            EditorSceneManager.OpenScene(ScenePath);
            var reg = EquipmentRegistry.LoadDefault();
            var seen = new HashSet<string>();
            var all = Object.FindObjectsByType<InteractableObject>(FindObjectsSortMode.None);
            Assert.GreaterOrEqual(all.Length, 11);
            foreach (var io in all)
            {
                Assert.IsTrue(seen.Add(io.objectId), "duplicate objectId in scene: " + io.objectId);
                EquipmentEntry e;
                Assert.IsTrue(reg.TryGet(io.objectId, out e), "no data entry for " + io.objectId);
                Assert.IsNotNull(io.GetComponent<Collider>(), io.objectId + " has no collider");
                Assert.IsFalse(string.IsNullOrEmpty(io.displayName), io.objectId + " missing name");
                Assert.IsFalse(string.IsNullOrEmpty(io.description), io.objectId + " missing description");
                Assert.IsFalse(string.IsNullOrEmpty(io.category), io.objectId + " missing category");
                if (io.canGrab) Assert.IsNotNull(io.GetComponent<Rigidbody>(), io.objectId + " grabbable but no rigidbody");
            }
            foreach (var e in reg.All) Assert.IsTrue(seen.Contains(e.id), "equipment entry not placed in scene: " + e.id);
        }

        [Test]
        public void Scene_HasPlayerSystemsAndPlacementZone()
        {
            EditorSceneManager.OpenScene(ScenePath);
            Assert.IsNotNull(Object.FindFirstObjectByType<FirstPersonController>());
            Assert.IsNotNull(Object.FindFirstObjectByType<InteractionSystem>());
            Assert.IsNotNull(Object.FindFirstObjectByType<MedSimXR.Training.TrainingManager>());
            Assert.IsNotNull(Object.FindFirstObjectByType<MedSimXR.Core.GameBootstrap>());
            Assert.IsNotNull(Object.FindFirstObjectByType<WorldInfoPanel>());
            var zone = Object.FindFirstObjectByType<PlacementZone>();
            Assert.IsNotNull(zone);
            Assert.IsTrue(zone.GetComponent<BoxCollider>().isTrigger);
            Assert.IsNotNull(Camera.main, "MainCamera tag missing");
        }

        [Test]
        public void Scene_NoMissingScripts()
        {
            EditorSceneManager.OpenScene(ScenePath);
            foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Assert.AreEqual(0, GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go), "missing script on " + go.name);
        }

        [Test]
        public void AssetManifest_ExistsAndCoversAllSimReadyAssets()
        {
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "../Data/Equipment/asset_manifest.json"));
            Assert.IsTrue(File.Exists(path), "run Tools/AssetValidator/build_manifest.py");
            string text = File.ReadAllText(path);
            foreach (var id in new[] { "chair_patient_mobile_a01", "CT_injection_system_a01", "box_corrugated_brown_b20", "rack_large_empty_a02" })
                StringAssert.Contains(id, text);
            StringAssert.Contains("cc-by-4.0", text.ToLowerInvariant());
        }
    }
}
