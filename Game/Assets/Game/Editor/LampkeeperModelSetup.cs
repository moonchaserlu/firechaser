using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace WildernessExtraction.EditorTools
{
    /// <summary>
    /// 守灯人（教学关事件选择 NPC）模型接入：配置 FBX 导入、生成 URP 材质（提灯自发光）和只有 Idle 的动画控制器，
    /// 生成 Assets/Tutorial/Art/NPC/Tutorial_NPC.prefab（单根节点、脚底/木箱底为原点、面朝 +Z、自带 Animator）。
    /// TutorialBuilder 发现这个预制体后会用它替换寻火者替身；本脚本不改教学关场景和教学关脚本。
    /// 模型由 ArtSource/Characters/Lampkeeper/lampkeeper_build.py 生成。
    /// </summary>
    public static class LampkeeperModelSetup
    {
        public const string Folder = "Assets/Tutorial/Art/NPC";
        public const string ModelPath = Folder + "/Lampkeeper.fbx";
        public const string MaterialFolder = Folder + "/Materials";
        public const string TextureFolder = Folder + "/Textures";
        public const string ControllerPath = Folder + "/Lampkeeper.controller";
        public const string PrefabPath = Folder + "/Tutorial_NPC.prefab";
        private const string NpcObjectName = "Seeker_Wounded (事件选择)";

        private static readonly (string name, string texture, float smoothness, float emission)[] Materials =
        {
            ("M_Keeper_Body", "T_Keeper_Body", 0.12f, 0f),
            ("M_Keeper_Clothes", "T_Keeper_Clothes", 0.05f, 0f),
            ("M_Keeper_Props", "T_Keeper_Props", 0.08f, 0f),
            // 提灯是点着的：贴图同时当自发光贴图，玻璃和火苗亮，铁皮只微微透光。
            ("M_Keeper_Lamp", "T_Keeper_Lamp", 0.3f, 1.4f),
        };

        [MenuItem("Wilderness/Tutorial/Setup Lampkeeper NPC Model")]
        public static void Setup()
        {
            ConfigureImporter();
            BuildController();
            BuildPrefab();
            AssetDatabase.SaveAssets();
            foreach (var r in AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath).GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                Debug.Log($"[Lampkeeper] {r.name}: {r.sharedMesh.triangles.Length / 3} tris, {r.bones.Length} bones");
            }

            Debug.Log($"[Lampkeeper] 已生成 {PrefabPath}");
        }

        private static void ConfigureImporter()
        {
            Directory.CreateDirectory(MaterialFolder);
            AssetDatabase.Refresh();
            var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
            if (importer == null) throw new FileNotFoundException(ModelPath);

            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importAnimation = true;
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.resampleCurves = true;
            importer.importNormals = ModelImporterNormals.Import;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;

            var lit = Shader.Find("Universal Render Pipeline/Lit");
            foreach (var (name, texture, smoothness, emission) in Materials)
            {
                var path = $"{MaterialFolder}/{name}.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null)
                {
                    mat = new Material(lit) { name = name };
                    AssetDatabase.CreateAsset(mat, path);
                }

                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TextureFolder}/{texture}.png");
                mat.SetTexture("_BaseMap", tex);
                mat.SetColor("_BaseColor", Color.white);
                mat.SetFloat("_Smoothness", smoothness);
                mat.SetFloat("_Metallic", 0f);
                if (emission > 0f)
                {
                    mat.EnableKeyword("_EMISSION");
                    mat.SetTexture("_EmissionMap", tex);
                    mat.SetColor("_EmissionColor", new Color(1f, 0.85f, 0.65f) * emission);
                    mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                }

                EditorUtility.SetDirty(mat);
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), name), mat);
            }

            importer.SaveAndReimport();

            // Blender 导出的动作名为 "Lampkeeper_Rig|Idle"：改短名并循环
            var clips = importer.defaultClipAnimations;
            foreach (var clip in clips)
            {
                var bar = clip.takeName.LastIndexOf('|');
                clip.name = bar >= 0 ? clip.takeName.Substring(bar + 1) : clip.takeName;
                clip.loopTime = true;
                clip.loopPose = false;
            }

            importer.clipAnimations = clips;
            importer.SaveAndReimport();
        }

        public static AnimationClip Clip(string name) =>
            AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>().FirstOrDefault(c => c.name == name);

        private static void BuildController()
        {
            AssetDatabase.DeleteAsset(ControllerPath);
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            var idle = ctrl.layers[0].stateMachine.AddState("Idle");
            idle.motion = Clip("Idle");
            ctrl.layers[0].stateMachine.defaultState = idle;
            EditorUtility.SetDirty(ctrl);
        }

        private static void BuildPrefab()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            try
            {
                instance.name = "Tutorial_NPC";
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var animator = instance.GetComponent<Animator>();
                if (animator == null) animator = instance.AddComponent<Animator>();
                animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                foreach (var r in instance.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    r.shadowCastingMode = ShadowCastingMode.On;
                    r.updateWhenOffscreen = false;
                }

                PrefabUtility.SaveAsPrefabAsset(instance, PrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        // ---------- 截图 ----------

        /// <summary>批处理入口：转台截图 + 教学关游戏镜头截图。目录由 -lampkeeperShots 指定。不保存任何场景。</summary>
        [MenuItem("Wilderness/Tutorial/Capture Lampkeeper Screenshots")]
        public static void Capture()
        {
            var args = System.Environment.GetCommandLineArgs();
            var folder = "LampkeeperShots";
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-lampkeeperShots") folder = args[i + 1];
            }

            var pipeline = GraphicsSettings.currentRenderPipeline;
            var batcher = pipeline != null ? pipeline.GetType().GetProperty("useSRPBatcher") : null;
            var batching = batcher != null && (bool)batcher.GetValue(pipeline);
            batcher?.SetValue(pipeline, false);
            try
            {
                CaptureTurnaround(folder);
                CaptureTutorial(folder);
            }
            finally
            {
                batcher?.SetValue(pipeline, batching);
            }
        }

        private static Light sun;

        private static void CaptureTurnaround(string folder)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.localScale = Vector3.one * 3f;
            ground.GetComponent<MeshRenderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Map01/Materials/M_Map01_Floor.mat");
            sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.6f;
            sun.shadows = LightShadows.Soft;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.5f, 0.5f, 0.52f);

            var npc = Spawn(Vector3.zero, Quaternion.identity, 0f);
            var center = new Vector3(0.05f, 1.0f, 0.1f);
            var size = new Vector2Int(900, 1100);
            Shoot(folder, "Lampkeeper_Unity_Front", center + Vector3.forward * 6f, center, 1.2f, size);
            Shoot(folder, "Lampkeeper_Unity_Side", center + Vector3.left * 6f, center, 1.2f, size);
            Shoot(folder, "Lampkeeper_Unity_Back", center + Vector3.back * 6f, center, 1.2f, size);
            Shoot(folder, "Lampkeeper_Unity_ThreeQuarter", center + new Vector3(-3.5f, 1.8f, 4.5f), center, 0f, size);

            sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            npc.transform.rotation = Quaternion.Euler(0f, 180f, 0f);   // 教学关里面朝南（镜头方向）
            ShootGameCamera(folder, "Lampkeeper_Unity_GameCamera", Vector3.zero, 1f);
            ShootGameCamera(folder, "Lampkeeper_Unity_GameCamera_Zoom", Vector3.zero, 0.3f);
            ShootGameCamera(folder, "Lampkeeper_Unity_GameCamera_Close", Vector3.zero, 0.12f);
            Object.DestroyImmediate(npc);
            npc = Spawn(Vector3.zero, Quaternion.Euler(0f, 150f, 0f), 2f);   // Idle 中段：低头看伤腿
            ShootGameCamera(folder, "Lampkeeper_Unity_GameCamera_LookDown_Close", Vector3.zero, 0.12f);
            Object.DestroyImmediate(npc);
            sun = null;
        }

        /// <summary>打开教学关场景（只在内存里），把 NPC 换成守灯人后用游戏镜头拍。教学关已重建时直接拍现有的。</summary>
        private static void CaptureTutorial(string folder)
        {
            const string scenePath = "Assets/Tutorial/Scenes/Tutorial.unity";
            if (!File.Exists(scenePath)) return;
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            var holder = GameObject.Find(NpcObjectName);
            if (holder == null) return;
            var existing = holder.transform.Find("NpcModel");
            if (existing == null)
            {
                foreach (Transform child in holder.transform)
                {
                    if (child.name == "SeekerModel" || child.name == "Body") child.gameObject.SetActive(false);   // 寻火者替身 / 白盒胶囊
                }

                var npc = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath), holder.transform);
                npc.transform.localPosition = Vector3.zero;
                npc.transform.localRotation = Quaternion.identity;
                Clip("Idle").SampleAnimation(npc, 0f);
            }

            ShootGameCamera(folder, "Lampkeeper_Tutorial_GameCamera", holder.transform.position, 1f);
            ShootGameCamera(folder, "Lampkeeper_Tutorial_GameCamera_Zoom", holder.transform.position, 0.35f);
        }

        private static GameObject Spawn(Vector3 position, Quaternion rotation, float time)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
            go.transform.SetPositionAndRotation(position, rotation);
            Clip("Idle").SampleAnimation(go, time);
            return go;
        }

        private static void ShootGameCamera(string folder, string name, Vector3 target, float distanceScale)
        {
            var go = new GameObject("ShotCamera");
            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = FixedObliqueCamera.DefaultFieldOfView;
            cam.nearClipPlane = distanceScale < 1f ? 0.3f : FixedObliqueCamera.DefaultNearClip;
            cam.farClipPlane = 400f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.02f, 0.02f, 0.03f);
            var focus = target + Vector3.up * FixedObliqueCamera.DefaultFollowHeight;
            go.transform.SetPositionAndRotation(focus + FixedObliqueCamera.DefaultOffset * distanceScale,
                Quaternion.Euler(FixedObliqueCamera.DefaultEulerAngles));
            Render(cam, folder, name, new Vector2Int(1600, 900));
            Object.DestroyImmediate(go);
        }

        private static void Shoot(string folder, string name, Vector3 position, Vector3 target, float orthoSize, Vector2Int size)
        {
            var go = new GameObject("ShotCamera");
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.82f, 0.81f, 0.79f);
            cam.orthographic = orthoSize > 0f;
            cam.orthographicSize = orthoSize;
            cam.fieldOfView = 24f;
            go.transform.position = position;
            go.transform.LookAt(target);
            if (sun != null) sun.transform.rotation = Quaternion.Euler(40f, go.transform.eulerAngles.y + 35f, 0f);
            Render(cam, folder, name, size);
            Object.DestroyImmediate(go);
        }

        private static void Render(Camera cam, string folder, string name, Vector2Int size)
        {
            Directory.CreateDirectory(folder);
            var rt = new RenderTexture(size.x, size.y, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.Render();
            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(size.x, size.y, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0);
            tex.Apply();
            RenderTexture.active = previous;
            cam.targetTexture = null;
            File.WriteAllBytes(Path.Combine(folder, name + ".png"), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            rt.Release();
            Object.DestroyImmediate(rt);
        }
    }
}
