using System;
using System.Collections.Generic;
using System.IO;
using MedSimXR.Core;
using MedSimXR.Core.UI;
using MedSimXR.Equipment;
using MedSimXR.Interaction;
using MedSimXR.Training;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MedSimXR.EditorTools
{
    /// <summary>Generates the URP settings, materials, SimReady prefabs and the Phase 1 scene from the Data/ folder.</summary>
    [Serializable] public class LayoutItem { public string id; public float x, y, z, yaw; public string restOn; public float dx, dz; }
    [Serializable] public class LayoutShot { public string name; public Vector3 pos; public Vector3 look; }
    [Serializable]
    public class LayoutFile
    {
        public string zoneTable;
        public string zoneObject;
        public float zoneDx, zoneDz;
        public LayoutItem[] items;
        public LayoutShot[] shots;
    }

    public static class ProjectBuilder
    {
        const string ScenePath = "Assets/Scenes/Hospital_Phase1.unity";
        const float RoomW = 10f, RoomD = 8f, RoomH = 3.4f;

        static string ProjectRoot { get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..")); } }

        // ---------------- entry points ----------------
        [MenuItem("MedSim XR/Build All")]
        public static void BuildAll()
        {
            Run(() => { SyncData(); ConfigureProject(); XrSetup.Configure(); BuildScene(); });
        }

        public static void BuildAllAndExit() { BuildAll(); Exit(0); }

        public static void CaptureScreensAndExit()
        {
            Run(CaptureScreens);
            Exit(0);
        }

        static void Run(Action a)
        {
            try { a(); }
            catch (Exception e)
            {
                Debug.LogError("[MedSim] build failed: " + e);
                Exit(1);
            }
        }

        static void Exit(int code)
        {
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        // ---------------- data ----------------
        public static void SyncData()
        {
            string dst = Path.Combine(Application.dataPath, "Resources/Data");
            Directory.CreateDirectory(dst);
            int n = 0;
            foreach (var f in Directory.GetFiles(Path.Combine(ProjectRoot, "Data"), "*.json", SearchOption.AllDirectories))
            {
                File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
                n++;
            }
            AssetDatabase.Refresh();
            Debug.Log("[MedSim] synced " + n + " data files to Resources/Data");
        }

        // ---------------- project settings ----------------
        public static void ConfigureProject()
        {
            Directory.CreateDirectory(Path.Combine(Application.dataPath, "Settings"));
            const string rdPath = "Assets/Settings/URP_LOW_Renderer.asset";
            const string rpPath = "Assets/Settings/URP_LOW.asset";
            var rp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(rpPath);
            if (rp == null)
            {
                var rd = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(rd, rdPath);
                rp = UniversalRenderPipelineAsset.Create(rd);
                rp.shadowDistance = 15f;
                rp.supportsHDR = false;
                rp.msaaSampleCount = 1;
                AssetDatabase.CreateAsset(rp, rpPath);
            }
            GraphicsSettings.defaultRenderPipeline = rp;
            QualitySettings.renderPipeline = rp;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.companyName = "MedSimXR";
            PlayerSettings.productName = "MedSim XR";
            PlayerSettings.runInBackground = true;
            AssetDatabase.SaveAssets();
            Debug.Log("[MedSim] URP LOW asset configured");
        }

        // ---------------- materials ----------------
        static Material MakeMat(string name, Color c, float smooth = 0.3f, float metal = 0f, Color? emission = null)
        {
            string path = "Assets/Materials/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", metal);
            if (emission.HasValue)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", emission.Value);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
            }
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material MakeHighlightMat()
        {
            string path = "Assets/Materials/Highlight.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_ZWrite", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
            m.SetColor("_BaseColor", new Color(0.1f, 0.9f, 1f, 0.12f));
            EditorUtility.SetDirty(m);
            return m;
        }

        // ---------------- helpers ----------------
        static Bounds Combined(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.one * 0.1f);
            var b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            return b;
        }

        static GameObject Prim(PrimitiveType t, string name, Transform parent, Vector3 pos, Vector3 scale, Material mat, bool keepCollider = false)
        {
            var g = GameObject.CreatePrimitive(t);
            g.name = name;
            g.transform.SetParent(parent, false);
            g.transform.localPosition = pos;
            g.transform.localScale = scale;
            g.GetComponent<Renderer>().sharedMaterial = mat;
            if (!keepCollider) UnityEngine.Object.DestroyImmediate(g.GetComponent<Collider>());
            return g;
        }

        static void FitBoxCollider(GameObject root)
        {
            var old = root.GetComponent<BoxCollider>();
            if (old != null) UnityEngine.Object.DestroyImmediate(old);
            var b = Combined(root);
            var col = root.AddComponent<BoxCollider>();
            col.center = root.transform.InverseTransformPoint(b.center);
            col.size = b.size;
        }

        static InteractableObject MakeInteractable(GameObject root, EquipmentEntry e)
        {
            var io = root.AddComponent<InteractableObject>();
            io.objectId = e.id;
            io.ApplyDataForBuild(e);
            if (e.canGrab && !e.isFixed)
            {
                var rb = root.AddComponent<Rigidbody>();
                rb.mass = 3f;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            }
            return io;
        }

        // ---------------- SimReady prefabs ----------------
        static GameObject MakeSimReadyPrefab(EquipmentEntry e)
        {
            string glb = "Assets/SimReady/" + e.simreadyId + ".glb";
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(glb);
            if (src == null) throw new FileNotFoundException("Model not imported: " + glb);

            var root = new GameObject(e.id);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(src);
            model.name = "Model";
            model.transform.SetParent(root.transform, false);
            var b = Combined(root);
            model.transform.position += new Vector3(-b.center.x, -b.min.y, -b.center.z);   // centre footprint, sit on floor
            FitBoxCollider(root);
            MakeInteractable(root, e);
            Directory.CreateDirectory(Path.Combine(Application.dataPath, "Prefabs/SimReady"));
            string path = "Assets/Prefabs/SimReady/" + e.id + ".prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            UnityEngine.Object.DestroyImmediate(root);
            return prefab;
        }

        // ---------------- scene ----------------
        public static void BuildScene()
        {
            var db = JsonUtility.FromJson<EquipmentDatabaseFile>(File.ReadAllText(Path.Combine(ProjectRoot, "Data/Equipment/equipment.json")));
            var entries = new Dictionary<string, EquipmentEntry>();
            foreach (var e in db.items) entries[e.id] = e;

            var mFloor = MakeMat("Floor", new Color(0.62f, 0.66f, 0.68f), 0.35f);
            var mWall = MakeMat("Wall", new Color(0.86f, 0.9f, 0.9f), 0.1f);
            var mCeil = MakeMat("Ceiling", new Color(0.95f, 0.95f, 0.95f), 0.05f);
            var mWhite = MakeMat("PlasticWhite", new Color(0.92f, 0.93f, 0.94f), 0.5f);
            var mGrey = MakeMat("PlasticGrey", new Color(0.35f, 0.38f, 0.4f), 0.4f);
            var mDark = MakeMat("Dark", new Color(0.06f, 0.07f, 0.08f), 0.6f);
            var mMetal = MakeMat("Metal", new Color(0.7f, 0.72f, 0.74f), 0.7f, 0.8f);
            var mMattress = MakeMat("Mattress", new Color(0.55f, 0.75f, 0.85f), 0.2f);
            var mAccent = MakeMat("AccentGreen", new Color(0.1f, 0.6f, 0.4f), 0.4f);
            var mPad = MakeMat("WorkPad", new Color(0.0f, 0.55f, 0.75f), 0.2f, 0f, new Color(0.0f, 0.5f, 0.7f));
            var mScreen = MakeMat("MonitorScreen", new Color(0.02f, 0.06f, 0.05f), 0.8f, 0f, new Color(0.0f, 0.25f, 0.15f));
            var mLight = MakeMat("LightPanel", Color.white, 0.1f, 0f, new Color(1.0f, 1.0f, 0.95f));
            var mHighlight = MakeHighlightMat();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.42f, 0.44f, 0.46f);
            RenderSettings.skybox = null;

            // --- shell ---
            var shell = new GameObject("Room_Shell").transform;
            var floor = Prim(PrimitiveType.Cube, "Floor", shell, new Vector3(0, -0.05f, 0), new Vector3(RoomW, 0.1f, RoomD), mFloor, true);
            Prim(PrimitiveType.Cube, "Ceiling", shell, new Vector3(0, RoomH + 0.05f, 0), new Vector3(RoomW, 0.1f, RoomD), mCeil, true);
            Prim(PrimitiveType.Cube, "Wall_Back", shell, new Vector3(0, RoomH / 2, RoomD / 2 + 0.05f), new Vector3(RoomW + 0.2f, RoomH, 0.1f), mWall, true);
            Prim(PrimitiveType.Cube, "Wall_Front", shell, new Vector3(0, RoomH / 2, -RoomD / 2 - 0.05f), new Vector3(RoomW + 0.2f, RoomH, 0.1f), mWall, true);
            Prim(PrimitiveType.Cube, "Wall_Left", shell, new Vector3(-RoomW / 2 - 0.05f, RoomH / 2, 0), new Vector3(0.1f, RoomH, RoomD), mWall, true);
            Prim(PrimitiveType.Cube, "Wall_Right", shell, new Vector3(RoomW / 2 + 0.05f, RoomH / 2, 0), new Vector3(0.1f, RoomH, RoomD), mWall, true);
            foreach (Transform t in shell) t.gameObject.isStatic = true;

            // ceiling light panels + a few real lights (no shadows, LOW preset)
            var lights = new GameObject("Lighting").transform;
            float[] lx = { -2.5f, 2.5f }, lz = { -2f, 2f };
            foreach (float x in lx) foreach (float z in lz)
            {
                Prim(PrimitiveType.Cube, "LightPanel", lights, new Vector3(x, RoomH - 0.02f, z), new Vector3(1.2f, 0.04f, 0.6f), mLight).isStatic = true;
                var l = new GameObject("PointLight").AddComponent<Light>();
                l.transform.SetParent(lights, false);
                l.transform.localPosition = new Vector3(x, RoomH - 1.0f, z);
                l.type = LightType.Point;
                l.range = 7f;
                l.intensity = 1.1f;
                l.color = new Color(1f, 0.97f, 0.92f);
                l.shadows = LightShadows.None;
            }

            // --- equipment: everything is placed from Data/Layout/layout.json ---
            var layout = JsonUtility.FromJson<LayoutFile>(File.ReadAllText(Path.Combine(ProjectRoot, "Data/Layout/layout.json")));
            var equip = new GameObject("Equipment").transform;
            var ph = new GameObject("Placeholders").transform;
            var placed = new Dictionary<string, GameObject>();
            var prefabs = new Dictionary<string, GameObject>();
            foreach (var li in layout.items)
            {
                EquipmentEntry e;
                if (!entries.TryGetValue(li.id, out e)) { Debug.Log("[MedSim] layout item skipped (no equipment entry yet): " + li.id); continue; }
                Vector3 pos = new Vector3(li.x, li.y, li.z);
                if (!string.IsNullOrEmpty(li.restOn))
                {
                    GameObject host;
                    if (!placed.TryGetValue(li.restOn, out host)) throw new InvalidOperationException(li.id + " rests on " + li.restOn + " which must be listed earlier");
                    var hb = Combined(host);
                    pos = new Vector3(hb.center.x + li.dx, hb.max.y + li.y, hb.center.z + li.dz);
                }
                GameObject go;
                if (e.IsPlaceholder)
                {
                    if (e.id != "monitor_patient_a01") throw new InvalidOperationException("no placeholder builder for " + e.id);
                    go = PlaceholderMonitor(ph, e, pos, mGrey, mScreen, mDark);
                }
                else
                {
                    GameObject prefab;
                    if (!prefabs.TryGetValue(e.id, out prefab)) { prefab = MakeSimReadyPrefab(e); prefabs[e.id] = prefab; }
                    go = Place(prefab, equip, pos, li.yaw);
                }
                placed[li.id] = go;
            }
            foreach (var e in db.items)
                if (!placed.ContainsKey(e.id)) throw new InvalidOperationException("equipment entry is not in the layout: " + e.id);

            // work-surface pad + placement zone on top of the inspection table
            var tableBounds = Combined(placed[layout.zoneTable]);
            Vector3 zoneCentre = new Vector3(tableBounds.center.x + layout.zoneDx, tableBounds.max.y, tableBounds.center.z + layout.zoneDz);
            Prim(PrimitiveType.Cube, "WorkSurfacePad", equip, zoneCentre + Vector3.up * 0.003f, new Vector3(0.6f, 0.006f, 0.6f), mPad);
            var zoneGo = new GameObject("PlacementZone_WorkSurface");
            zoneGo.transform.SetParent(equip, false);
            zoneGo.transform.position = zoneCentre + Vector3.up * 0.17f;
            var zc = zoneGo.AddComponent<BoxCollider>();
            zc.isTrigger = true;
            zc.size = new Vector3(0.55f, 0.35f, 0.55f);
            var zone = zoneGo.AddComponent<PlacementZone>();
            zone.zoneId = "work_surface";
            zone.acceptedObjectId = layout.zoneObject;

            // --- info panel for the chair ---
            var panelGo = new GameObject("InfoPanel_Chair");
            panelGo.transform.position = new Vector3(3.6f, 1.45f, -1.9f);
            var panel = panelGo.AddComponent<WorldInfoPanel>();
            panel.equipmentId = "chair_patient_mobile_a01";

            // --- player ---
            var player = new GameObject("Player");
            player.transform.position = new Vector3(0f, 0.05f, -3.2f);
            var cc = player.AddComponent<CharacterController>();
            cc.height = 1.8f; cc.radius = 0.3f; cc.center = new Vector3(0, 0.9f, 0); cc.stepOffset = 0.25f;
            var fpc = player.AddComponent<FirstPersonController>();
            var camGo = new GameObject("MainCamera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(player.transform, false);
            camGo.transform.localPosition = new Vector3(0, 1.65f, 0);
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f; cam.farClipPlane = 60f; cam.fieldOfView = 70f;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.05f, 0.06f, 0.08f);
            camGo.AddComponent<AudioListener>();
            camGo.AddComponent<UniversalAdditionalCameraData>();
            fpc.cameraTransform = camGo.transform;
            var hold = new GameObject("HoldPoint").transform;
            hold.SetParent(camGo.transform, false);
            hold.localPosition = new Vector3(0.1f, -0.25f, 1.1f);
            var ints = camGo.AddComponent<InteractionSystem>();
            ints.viewCamera = cam; ints.holdPoint = hold; ints.highlightMaterial = mHighlight;

            // --- VR rig (inactive until a headset or the VR simulator turns it on) ---
            var rigGo = new GameObject("XRRig");
            rigGo.transform.SetParent(player.transform, false);
            var xrCamGo = new GameObject("XRCamera");
            xrCamGo.transform.SetParent(rigGo.transform, false);
            xrCamGo.transform.localPosition = new Vector3(0, 1.65f, 0);
            var xrCam = xrCamGo.AddComponent<Camera>();
            xrCam.nearClipPlane = 0.05f; xrCam.farClipPlane = 60f;
            xrCam.clearFlags = CameraClearFlags.SolidColor; xrCam.backgroundColor = new Color(0.05f, 0.06f, 0.08f);
            xrCamGo.AddComponent<UniversalAdditionalCameraData>();
            var xrListener = xrCamGo.AddComponent<AudioListener>();
            xrListener.enabled = false;
            xrCam.enabled = false;
            var mLaser = MakeMat("Laser", new Color(0.1f, 0.9f, 1f), 0f, 0f, new Color(0.1f, 0.9f, 1f));
            var mCtrl = MakeMat("Controller", new Color(0.12f, 0.13f, 0.15f), 0.5f);
            Transform MakeHand(string handName, Vector3 pos)
            {
                var h = new GameObject(handName).transform;
                h.SetParent(rigGo.transform, false);
                h.localPosition = pos;
                var vis = GameObject.CreatePrimitive(PrimitiveType.Cube);
                vis.name = "Visual";
                UnityEngine.Object.DestroyImmediate(vis.GetComponent<Collider>());
                vis.transform.SetParent(h, false);
                vis.transform.localPosition = new Vector3(0, 0, 0.04f);
                vis.transform.localScale = new Vector3(0.045f, 0.035f, 0.13f);
                vis.GetComponent<Renderer>().sharedMaterial = mCtrl;
                return h;
            }
            var leftHand = MakeHand("LeftHand", new Vector3(-0.25f, 1.35f, 0.3f));
            var rightHand = MakeHand("RightHand", new Vector3(0.25f, 1.35f, 0.3f));
            var attach = new GameObject("HoldAttach").transform;
            attach.SetParent(rightHand, false);
            attach.localPosition = new Vector3(0f, -0.05f, 0.6f);
            var laser = rightHand.gameObject.AddComponent<LineRenderer>();
            laser.useWorldSpace = true;
            laser.widthMultiplier = 0.006f;
            laser.sharedMaterial = mLaser;
            laser.shadowCastingMode = ShadowCastingMode.Off;
            laser.receiveShadows = false;
            laser.positionCount = 2;
            laser.enabled = false;
            var vrRig = rigGo.AddComponent<MedSimXR.XR.VrRig>();
            vrRig.xrCamera = xrCam; vrRig.leftHand = leftHand; vrRig.rightHand = rightHand; vrRig.holdAttach = attach; vrRig.laser = laser;
            vrRig.interaction = ints; vrRig.body = cc;
            rigGo.SetActive(false);

            // --- systems ---
            var sys = new GameObject("Systems");
            var hud = sys.AddComponent<HudView>();
            var tm = sys.AddComponent<TrainingManager>();
            var boot = sys.AddComponent<GameBootstrap>();
            boot.training = tm; boot.hud = hud; boot.interaction = ints; boot.player = fpc;
            tm.hud = hud; tm.interaction = ints; tm.player = fpc;
            vrRig.training = tm; vrRig.hud = hud;
            var xrMode = sys.AddComponent<MedSimXR.XR.XrModeController>();
            xrMode.rig = vrRig; xrMode.player = fpc; xrMode.desktopCamera = cam; xrMode.interaction = ints; xrMode.training = tm; xrMode.hud = hud;

            Directory.CreateDirectory(Path.Combine(Application.dataPath, "Scenes"));
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("[MedSim] scene built: " + ScenePath);
        }

        static GameObject Place(GameObject prefab, Transform parent, Vector3 pos, float yaw)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            var io = go.GetComponent<InteractableObject>();
            if (io != null) io.CaptureResetPose();
            return go;
        }

        static GameObject Wrap(Transform parent, EquipmentEntry e, Vector3 pos, out Transform t)
        {
            var root = new GameObject(e.id);
            root.transform.SetParent(parent, false);
            root.transform.position = pos;
            t = root.transform;
            return root;
        }

        static void Finish(GameObject root, EquipmentEntry e)
        {
            FitBoxCollider(root);
            var io = MakeInteractable(root, e);
            io.CaptureResetPose();
            root.isStatic = false;
        }


        static GameObject PlaceholderMonitor(Transform p, EquipmentEntry e, Vector3 pos, Material grey, Material screen, Material dark)
        {
            Transform t; var r = Wrap(p, e, pos, out t);
            Prim(PrimitiveType.Cylinder, "Pole", t, new Vector3(0, 0.75f, 0), new Vector3(0.06f, 0.75f, 0.06f), grey);
            Prim(PrimitiveType.Cube, "Base", t, new Vector3(0, 0.03f, 0), new Vector3(0.5f, 0.06f, 0.5f), dark);
            Prim(PrimitiveType.Cube, "Body", t, new Vector3(0, 1.55f, 0), new Vector3(0.55f, 0.36f, 0.08f), grey);
            Prim(PrimitiveType.Cube, "Screen", t, new Vector3(0, 1.55f, 0.045f), new Vector3(0.5f, 0.3f, 0.01f), screen);
            // synthetic readout text, clearly labelled
            var cg = new GameObject("ScreenCanvas", typeof(Canvas));
            cg.transform.SetParent(t, false);
            cg.transform.localPosition = new Vector3(0, 1.55f, 0.054f);
            var rt = (RectTransform)cg.transform;
            rt.sizeDelta = new Vector2(500, 300);
            rt.localScale = new Vector3(0.001f, 0.001f, 0.001f);
            cg.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            var tg = new GameObject("Text", typeof(RectTransform), typeof(Text));
            tg.transform.SetParent(rt, false);
            var trt = (RectTransform)tg.transform;
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = new Vector2(10, 6); trt.offsetMax = new Vector2(-10, -6);
            var txt = tg.GetComponent<Text>();
            txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            txt.fontSize = 40; txt.color = new Color(0.4f, 1f, 0.6f);
            txt.text = "HR 72   SpO2 98%\nRR 16   BP 118/76\nTemp 36.8\nSYNTHETIC TRAINING DATA";
            txt.horizontalOverflow = HorizontalWrapMode.Wrap;
            Finish(r, e);
            return r;
        }






        // ---------------- diagnostics ----------------
        public static void DiagnoseModelsAndExit()
        {
            Run(() =>
            {
                foreach (string guid in AssetDatabase.FindAssets("t:Shader glTF"))
                {
                    string p = AssetDatabase.GUIDToAssetPath(guid);
                    if (p.Contains("gltfast")) AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate);
                }
                foreach (string id in new[] { "chair_patient_mobile_a01", "CT_injection_system_a01", "box_corrugated_brown_b20", "rack_large_empty_a02" })
                {
                    string path = "Assets/SimReady/" + id + ".glb";
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                    var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    var seen = new HashSet<Material>();
                    foreach (var r in go.GetComponentsInChildren<Renderer>())
                        foreach (var m in r.sharedMaterials)
                        {
                            if (m == null || !seen.Add(m)) continue;
                            var sh = m.shader;
                            string msg = "[MedSim][diag] " + id + " mat=" + m.name + " shader=" + (sh != null ? sh.name : "null")
                                         + " supported=" + (sh != null && sh.isSupported);
                            if (sh != null && ShaderUtil.ShaderHasError(sh))
                            {
                                msg += " HAS_ERRORS";
                                foreach (var sm in ShaderUtil.GetShaderMessages(sh)) { msg += " | " + sm.severity + ": " + sm.message; break; }
                            }
                            Debug.Log(msg);
                        }
                }
            });
            Exit(0);
        }

        // ---------------- screenshots ----------------
        public static void RebuildAndCaptureAndExit()
        {
            Run(() => { SyncData(); ConfigureProject(); BuildScene(); CaptureScreens(); });
            Exit(0);
        }

        public static void CaptureScreens()
        {
            // Without this, the editor draws magenta until shader variants finish compiling in the background.
            ShaderUtil.allowAsyncCompilation = false;
            EditorSettings.asyncShaderCompilation = false;
            EditorSceneManager.OpenScene(ScenePath);
            string dir = Path.Combine(ProjectRoot, "Documentation/screens");
            Directory.CreateDirectory(dir);
            var views = new (string name, Vector3 pos, Vector3 look)[]
            {
                ("01_player_start", new Vector3(0, 1.65f, -3.2f), new Vector3(0, 1.2f, 3f)),
                ("02_overview_left", new Vector3(-4.6f, 2.8f, -3.6f), new Vector3(1f, 0.8f, 1.5f)),
                ("03_overview_right", new Vector3(4.6f, 2.8f, -3.6f), new Vector3(-1f, 0.8f, 1.5f)),
                ("04_chair", new Vector3(1.2f, 1.5f, -2.8f), new Vector3(2.6f, 0.6f, -0.9f)),
                ("05_ct_injector", new Vector3(2.4f, 1.6f, 0.4f), new Vector3(4.25f, 0.9f, 2.9f)),
                ("06_rack_and_table", new Vector3(1.0f, 1.8f, -0.8f), new Vector3(-1.5f, 1.2f, 2.9f)),
            };
            var layoutFile = JsonUtility.FromJson<LayoutFile>(File.ReadAllText(Path.Combine(ProjectRoot, "Data/Layout/layout.json")));
            if (layoutFile.shots != null && layoutFile.shots.Length > 0)
            {
                var all = new List<(string name, Vector3 pos, Vector3 look)>(views);
                foreach (var sh in layoutFile.shots) all.Add((sh.name, sh.pos, sh.look));
                views = all.ToArray();
            }
            var camGo = new GameObject("CaptureCam");
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 70; cam.nearClipPlane = 0.05f;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black;
            camGo.AddComponent<UniversalAdditionalCameraData>();
            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            foreach (var v in views)
            {
                camGo.transform.position = v.pos;
                camGo.transform.LookAt(v.look);
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply();
                File.WriteAllBytes(Path.Combine(dir, v.name + ".png"), tex.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(tex);
            }
            RenderTexture.active = null;
            UnityEngine.Object.DestroyImmediate(rt);
            UnityEngine.Object.DestroyImmediate(camGo);
            Debug.Log("[MedSim] screenshots written to " + dir);
        }
    }
}
