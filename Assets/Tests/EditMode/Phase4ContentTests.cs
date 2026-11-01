using System.Collections.Generic;
using System.IO;
using MedSimXR.Assessment;
using MedSimXR.Core;
using MedSimXR.Equipment;
using MedSimXR.Interaction;
using MedSimXR.Training;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MedSimXR.Tests
{
    /// <summary>The expanded equipment set: layout data, the advanced identification quiz, and physical sanity of the room.</summary>
    public class Phase4ContentTests
    {
        const string ScenePath = "Assets/Scenes/Hospital_Phase1.unity";

        [System.Serializable] class LItem { public string id; public float x, y, z, yaw; public string restOn; public float dx, dz; }
        [System.Serializable] class LFile { public string zoneTable; public string zoneObject; public LItem[] items; }

        static LFile Layout()
        {
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "../Data/Layout/layout.json"));
            return JsonUtility.FromJson<LFile>(File.ReadAllText(path));
        }

        [Test]
        public void Layout_EveryEntryIsPlacedOnce_AndEveryPlacementHasAnEntry()
        {
            var reg = EquipmentRegistry.LoadDefault();
            var placed = new HashSet<string>();
            foreach (var li in Layout().items)
            {
                EquipmentEntry e;
                Assert.IsTrue(reg.TryGet(li.id, out e), "layout places unknown equipment: " + li.id);
                Assert.IsTrue(placed.Add(li.id), "placed twice: " + li.id);
                if (!string.IsNullOrEmpty(li.restOn)) Assert.IsTrue(placed.Contains(li.restOn), li.id + " rests on " + li.restOn + " which is not placed before it");
            }
            foreach (var e in reg.All) Assert.IsTrue(placed.Contains(e.id), "equipment entry with no layout slot: " + e.id);
            Assert.IsTrue(placed.Contains(Layout().zoneTable));
            Assert.IsTrue(placed.Contains(Layout().zoneObject));
        }

        [Test]
        public void EquipmentSet_IsMuchLargerThanPhase1_AndHasNoPlaceholders()
        {
            int n = 0;
            foreach (var e in EquipmentRegistry.LoadDefault().All)
            {
                n++;
                Assert.AreEqual("simready", e.source, e.id + " should be a real SimReady asset");
                Assert.AreEqual("pending", e.clinicalReview, e.id);
                Assert.IsFalse(string.IsNullOrEmpty(e.description), e.id);
            }
            Assert.GreaterOrEqual(n, 20, "expected a substantially larger equipment set");
        }

        [Test]
        public void AdvancedQuiz_IsWellFormed_AndReferencesPlacedEquipment()
        {
            var set = DataLoader.Load<QuizSet>("identification_advanced");
            Assert.AreEqual(ProgressStore.ModuleF, set.moduleId);
            Assert.GreaterOrEqual(set.questions.Length, 8);
            var reg = EquipmentRegistry.LoadDefault();
            var ids = new HashSet<string>();
            var targets = new HashSet<string>();
            foreach (var q in set.questions)
            {
                Assert.IsTrue(ids.Add(q.id), "duplicate question id " + q.id);
                Assert.IsTrue(targets.Add(q.correctObjectId), "two questions about " + q.correctObjectId);
                EquipmentEntry e;
                Assert.IsTrue(reg.TryGet(q.correctObjectId, out e), q.id + " asks about an unknown item");
                StringAssert.Contains(e.displayName, q.prompt);
                Assert.GreaterOrEqual(q.hints.Length, 2, q.id);
                Assert.IsFalse(string.IsNullOrEmpty(q.explanation), q.id);
                foreach (var h in q.hints) StringAssert.DoesNotContain(e.displayName, h, q.id + " hint must not give the name away");
            }
        }

        [Test]
        public void AdvancedQuiz_DoesNotRepeatTheCoreQuiz()
        {
            var core = DataLoader.Load<QuizSet>("identification_questions");
            var adv = DataLoader.Load<QuizSet>("identification_advanced");
            var coreIds = new HashSet<string>();
            foreach (var q in core.questions) coreIds.Add(q.correctObjectId);
            foreach (var q in adv.questions) Assert.IsFalse(coreIds.Contains(q.correctObjectId), q.correctObjectId + " is already in the core quiz");
        }

        [Test]
        public void Room_PlayerStartIsClear_AndFixedFloorItemsDoNotCollide()
        {
            EditorSceneManager.OpenScene(ScenePath);
            var all = Object.FindObjectsByType<InteractableObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var floorItems = new List<InteractableObject>();
            foreach (var io in all)
            {
                var b = io.GetWorldBounds();
                bool onFloor = b.min.y < 0.2f;          // not resting on something and not wall-mounted
                if (onFloor) floorItems.Add(io);
            }
            Assert.Greater(floorItems.Count, 10);

            // the player must be able to start where the scene puts them
            var start = new Vector3(0f, 0f, -3.2f);
            foreach (var io in floorItems)
            {
                var b = io.GetWorldBounds();
                bool inside = start.x > b.min.x - 0.4f && start.x < b.max.x + 0.4f && start.z > b.min.z - 0.4f && start.z < b.max.z + 0.4f;
                Assert.IsFalse(inside, io.objectId + " blocks the player start at " + start + " (bounds " + b + ")");
            }

            // items on the floor should not interpenetrate (small overlap tolerated for rounded footprints)
            var problems = new List<string>();
            for (int i = 0; i < floorItems.Count; i++)
                for (int j = i + 1; j < floorItems.Count; j++)
                {
                    var a = floorItems[i].GetWorldBounds();
                    var c = floorItems[j].GetWorldBounds();
                    float ox = Mathf.Min(a.max.x, c.max.x) - Mathf.Max(a.min.x, c.min.x);
                    float oz = Mathf.Min(a.max.z, c.max.z) - Mathf.Max(a.min.z, c.min.z);
                    if (ox > 0.2f && oz > 0.2f) problems.Add(floorItems[i].objectId + " overlaps " + floorItems[j].objectId + " by " + ox.ToString("F2") + " x " + oz.ToString("F2") + " m");
                }
            Assert.IsEmpty(problems, string.Join("\n", problems));
        }

        [Test]
        public void Room_EverythingIsInsideTheWalls()
        {
            EditorSceneManager.OpenScene(ScenePath);
            var problems = new List<string>();
            foreach (var io in Object.FindObjectsByType<InteractableObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var b = io.GetWorldBounds();
                if (b.min.x < -5.05f || b.max.x > 5.05f || b.min.z < -4.05f || b.max.z > 4.05f || b.max.y > 3.45f)
                    problems.Add(io.objectId + " sticks out of the room: " + b);
            }
            Assert.IsEmpty(problems, string.Join("\n", problems));
        }
    }
}
