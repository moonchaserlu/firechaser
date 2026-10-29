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
    /// 寻火者模型接入：配置 FBX 导入、生成 URP 材质、以 Player.prefab 为基础生成预制体变体，并输出检查截图。
    /// 不修改原 Player.prefab，也不保存任何场景。
    /// </summary>
    public static class SeekerModelSetup
    {
        public const string ModelPath = "Assets/Game/Art/Characters/Seeker/Seeker.fbx";
        public const string MaterialFolder = "Assets/Game/Art/Characters/Seeker/Materials";
        public const string PlayerPrefabPath = "Assets/Game/Prefabs/Player.prefab";
        public const string VariantPath = "Assets/Game/Prefabs/Player_Seeker.prefab";
        public const string ModelChildName = "SeekerModel";
        public const string SelfLightName = "SeekerSelfLight";
        public const string ControllerPath = "Assets/Game/Art/Characters/Seeker/Seeker.controller";
        public const string UpperMaskPath = "Assets/Game/Art/Characters/Seeker/Seeker_UpperBody.mask";
        public const string PlayerConfigPath = "Assets/Game/Data/PlayerConfig.asset";

        private static readonly string[] LoopClips = { "Idle", "Run_F", "Run_B", "Run_L", "Run_R" };

        // Blender 导出的模型面朝方向与 Player 前方（+Z）之间的偏航修正。
        private const float ModelYaw = 0f;

        public const string TextureFolder = "Assets/Game/Art/Characters/Seeker/Textures";

        // 每个部件一张 Blender 烘焙的颜色贴图（布面斑驳 + 下摆尘土渐变）。
        private static readonly (string name, string texture, float smoothness)[] Materials =
        {
            ("M_Seeker_Body", "T_Seeker_Body", 0.12f),
            ("M_Seeker_Cloak", "T_Seeker_Cloak", 0.05f),
            ("M_Seeker_Backpack", "T_Seeker_Backpack", 0.1f),
        };

        [MenuItem("Wilderness/Seeker/Setup Model And Variant")]
        public static void Setup()
        {
            ConfigureImporter();
            ConfigureClips();
            BuildController();
            BuildVariant();
            AssetDatabase.SaveAssets();
            foreach (var r in AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath).GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var mesh = r.sharedMesh;
                Debug.Log($"[Seeker] {r.name}: {mesh.triangles.Length / 3} tris, {mesh.subMeshCount} submeshes, materials = {string.Join(", ", System.Array.ConvertAll(r.sharedMaterials, m => m != null ? m.name : "null"))}");
            }

            Debug.Log($"[Seeker] clips = {string.Join(", ", AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview")).Select(c => $"{c.name}({c.length:0.00}s)"))}");
            Debug.Log($"[Seeker] 已生成 {VariantPath}");
        }

        /// <summary>批处理入口：接入并截图。截图目录由 -seekerShots 参数指定。</summary>
        public static void SetupAndCapture()
        {
            Setup();
            Capture();
        }

        /// <summary>批处理入口：只截图（材质在同一次会话中刚创建时，SRP Batcher 可能还没上传颜色，建议单独运行）。</summary>
        public static void Capture()
        {
            var args = System.Environment.GetCommandLineArgs();
            var folder = "SeekerShots";
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-seekerShots") folder = args[i + 1];
            }

            // 批处理截图时 SRP Batcher 只上传了一份材质常量，所有物体会套用同一颜色。
            // 截图期间通过属性临时关闭（不走 SerializedObject，避免把 URP 资产标脏写回磁盘），结束后恢复。
            var pipeline = GraphicsSettings.currentRenderPipeline;
            var batcher = pipeline != null ? pipeline.GetType().GetProperty("useSRPBatcher") : null;
            var batching = batcher != null && (bool)batcher.GetValue(pipeline);
            batcher?.SetValue(pipeline, false);
            try
            {
                CaptureTurnaround(folder);
                CaptureMap01(folder);
            }
            finally
            {
                batcher?.SetValue(pipeline, batching);
            }
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

            // 清掉早期纯色分块方案留下的重映射。
            foreach (var remap in importer.GetExternalObjectMap())
            {
                importer.RemoveRemap(remap.Key);
            }

            var lit = Shader.Find("Universal Render Pipeline/Lit");
            foreach (var (name, texture, smoothness) in Materials)
            {
                var path = $"{MaterialFolder}/{name}.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null)
                {
                    mat = new Material(lit) { name = name };
                    AssetDatabase.CreateAsset(mat, path);
                }

                mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{TextureFolder}/{texture}.png"));
                mat.SetColor("_BaseColor", Color.white);
                mat.SetFloat("_Smoothness", smoothness);
                mat.SetFloat("_Metallic", 0f);
                EditorUtility.SetDirty(mat);
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), name), mat);
            }

            var pistol = $"{MaterialFolder}/M_Seeker_Pistol.mat";
            var pistolMat = AssetDatabase.LoadAssetAtPath<Material>(pistol);
            if (pistolMat == null)
            {
                pistolMat = new Material(lit) { name = "M_Seeker_Pistol" };
                AssetDatabase.CreateAsset(pistolMat, pistol);
            }

            pistolMat.SetColor("_BaseColor", new Color(0.12f, 0.12f, 0.12f));
            pistolMat.SetFloat("_Metallic", 0.7f);
            pistolMat.SetFloat("_Smoothness", 0.45f);
            EditorUtility.SetDirty(pistolMat);
            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "M_Seeker_Pistol"), pistolMat);

            importer.SaveAndReimport();
        }

        /// <summary>Blender 导出的动作名为 "Seeker_Rig|Idle"，改成短名并给待机、跑步打开循环。</summary>
        private static void ConfigureClips()
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
            var clips = importer.defaultClipAnimations;
            foreach (var clip in clips)
            {
                var bar = clip.takeName.LastIndexOf('|');
                clip.name = bar >= 0 ? clip.takeName.Substring(bar + 1) : clip.takeName;
                clip.loopTime = LoopClips.Contains(clip.name);
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
            ctrl.AddParameter("MoveX", AnimatorControllerParameterType.Float);
            ctrl.AddParameter("MoveZ", AnimatorControllerParameterType.Float);
            ctrl.AddParameter("Dodge", AnimatorControllerParameterType.Trigger);
            ctrl.AddParameter("Fire", AnimatorControllerParameterType.Trigger);
            ctrl.AddParameter("Reload", AnimatorControllerParameterType.Trigger);
            ctrl.AddParameter(new AnimatorControllerParameter { name = "RollSpeed", type = AnimatorControllerParameterType.Float, defaultFloat = 1f });
            ctrl.AddParameter(new AnimatorControllerParameter { name = "ReloadSpeed", type = AnimatorControllerParameterType.Float, defaultFloat = 1f });

            // 基础层：持枪跑步混合 + 翻滚
            var baseSm = ctrl.layers[0].stateMachine;
            var locomotion = ctrl.CreateBlendTreeInController("Locomotion", out var tree, 0);
            tree.blendType = BlendTreeType.FreeformDirectional2D;
            tree.blendParameter = "MoveX";
            tree.blendParameterY = "MoveZ";
            tree.AddChild(Clip("Idle"), Vector2.zero);
            tree.AddChild(Clip("Run_F"), new Vector2(0f, 1f));
            tree.AddChild(Clip("Run_B"), new Vector2(0f, -1f));
            tree.AddChild(Clip("Run_L"), new Vector2(-1f, 0f));
            tree.AddChild(Clip("Run_R"), new Vector2(1f, 0f));
            baseSm.defaultState = locomotion;

            var roll = baseSm.AddState("Roll");
            roll.motion = Clip("Roll");
            roll.speedParameterActive = true;
            roll.speedParameter = "RollSpeed";
            var toRoll = baseSm.AddAnyStateTransition(roll);
            toRoll.AddCondition(AnimatorConditionMode.If, 0f, "Dodge");
            toRoll.hasExitTime = false;
            toRoll.duration = 0.05f;
            toRoll.canTransitionToSelf = false;
            var fromRoll = roll.AddTransition(locomotion);
            fromRoll.hasExitTime = true;
            fromRoll.exitTime = 0.92f;
            fromRoll.duration = 0.08f;

            // 上半身层：射击后坐、换弹（遮罩只含胸部以上，不含斗篷骨骼）
            ctrl.AddLayer("UpperBody");
            var layers = ctrl.layers;
            layers[1].defaultWeight = 1f;
            layers[1].blendingMode = AnimatorLayerBlendingMode.Override;
            layers[1].avatarMask = BuildUpperMask();
            ctrl.layers = layers;
            var upperSm = ctrl.layers[1].stateMachine;
            var empty = upperSm.AddState("Empty");
            empty.writeDefaultValues = false;
            upperSm.defaultState = empty;
            AddUpperAction(upperSm, empty, "Fire", "Fire", null, true);
            AddUpperAction(upperSm, empty, "Reload", "Reload", "ReloadSpeed", false);
            EditorUtility.SetDirty(ctrl);
        }

        private static void AddUpperAction(AnimatorStateMachine sm, AnimatorState empty, string clip, string trigger, string speedParameter, bool restartable)
        {
            var state = sm.AddState(clip);
            state.motion = Clip(clip);
            state.writeDefaultValues = false;
            if (speedParameter != null)
            {
                state.speedParameterActive = true;
                state.speedParameter = speedParameter;
            }

            var enter = sm.AddAnyStateTransition(state);
            enter.AddCondition(AnimatorConditionMode.If, 0f, trigger);
            enter.hasExitTime = false;
            enter.duration = 0.02f;
            enter.canTransitionToSelf = restartable;
            var exit = state.AddTransition(empty);
            exit.hasExitTime = true;
            exit.exitTime = 1f;
            exit.duration = 0.08f;
        }

        private static AvatarMask BuildUpperMask()
        {
            AssetDatabase.DeleteAsset(UpperMaskPath);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var mask = new AvatarMask();
            mask.AddTransformPath(model.transform, true);
            for (var i = 0; i < mask.transformCount; i++)
            {
                var path = mask.GetTransformPath(i);
                mask.SetTransformActive(i, path.Contains("/chest") && !path.Contains("cloak"));
            }

            AssetDatabase.CreateAsset(mask, UpperMaskPath);
            return mask;
        }

        private static void BuildVariant()
        {
            var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab);
            try
            {
                // 胶囊和朝向方块只是占位外观，变体里关掉渲染，GameObject 保留。
                foreach (var placeholder in new[] { "Body", "FacingMarker" })
                {
                    var t = instance.transform.Find(placeholder);
                    if (t != null && t.TryGetComponent<MeshRenderer>(out var r)) r.enabled = false;
                }

                var visual = (GameObject)PrefabUtility.InstantiatePrefab(model, instance.transform);
                visual.name = ModelChildName;
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.Euler(0f, ModelYaw, 0f);
                visual.transform.localScale = Vector3.one;
                foreach (var r in visual.GetComponentsInChildren<Renderer>())
                {
                    r.shadowCastingMode = ShadowCastingMode.On;
                }

                // 翻滚时斗篷会离开原始包围盒，打开屏幕外更新，避免画面边缘处被错误剔除
                foreach (var skinned in visual.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    skinned.updateWhenOffscreen = true;
                }

                var animator = visual.GetComponent<Animator>();
                if (animator == null) animator = visual.AddComponent<Animator>();
                animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

                var driver = instance.GetComponent<PlayerAnimationDriver>();
                if (driver == null) driver = instance.AddComponent<PlayerAnimationDriver>();
                driver.Configure(animator, visual.transform, AssetDatabase.LoadAssetAtPath<PlayerConfig>(PlayerConfigPath),
                    Clip("Roll").length, Clip("Reload").length);

                if (instance.GetComponent<PlayerStatusHud>() == null) instance.AddComponent<PlayerStatusHud>();
                AddSelfLight(instance.transform);
                PrefabUtility.SaveAsPrefabAsset(instance, VariantPath);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        // ---------- 截图 ----------

        private static void CaptureTurnaround(string folder)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.localScale = Vector3.one * 3f;
            ground.GetComponent<MeshRenderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Map01/Materials/M_Map01_Floor.mat");

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.6f;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(45f, -30f, 0f);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.5f, 0.5f, 0.52f);
            DynamicGI.UpdateEnvironment();
            turnaroundSun = sun;

            var variant = AssetDatabase.LoadAssetAtPath<GameObject>(VariantPath);
            var player = (GameObject)PrefabUtility.InstantiatePrefab(variant);
            DisableGameplay(player);

            Pose(player, "Idle", 0f);
            var center = new Vector3(0f, 0.95f, 0f);
            Shoot(folder, "Seeker_Unity_Front", center + Vector3.forward * 6f, center, 1.2f, new Vector2Int(900, 1100));
            Shoot(folder, "Seeker_Unity_Side", center + Vector3.left * 6f, center, 1.2f, new Vector2Int(900, 1100));
            Shoot(folder, "Seeker_Unity_Back", center + Vector3.back * 6f, center, 1.2f, new Vector2Int(900, 1100));
            Shoot(folder, "Seeker_Unity_ThreeQuarter", center + new Vector3(3.5f, 1.8f, 4.5f), center, 0f, new Vector2Int(900, 1100));
            sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            ShootGameCamera(folder, "Seeker_Unity_GameCamera", player.transform.position);
            ShootGameCamera(folder, "Seeker_Unity_GameCamera_Zoom", player.transform.position, 0.28f);
            Object.DestroyImmediate(player);
            // 已渲染过的实例再次采样不会刷新蒙皮，所以每个姿势用新实例
            foreach (var (clip, frame, label) in new[] { ("Run_F", 5, "Run"), ("Roll", 8, "Roll"), ("Fire", 1, "Fire") })
            {
                var posed = (GameObject)PrefabUtility.InstantiatePrefab(variant);
                DisableGameplay(posed);
                posed.transform.rotation = Quaternion.Euler(0f, 135f, 0f);  // 瞄准方向朝右下，镜头能看到正侧面
                Pose(posed, clip, frame / 30f);
                ShootGameCamera(folder, $"Seeker_Unity_GameCamera_{label}_Zoom", posed.transform.position, 0.28f);
                Object.DestroyImmediate(posed);
            }

            CapturePoseSheet(folder);
            turnaroundSun = null;
        }

        private static Light turnaroundSun;

        private static void CaptureMap01(string folder)
        {
            const string scenePath = "Assets/Map01/Scenes/Map01.unity";
            if (!File.Exists(scenePath)) return;
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            var player = GameObject.Find("Player");
            if (player == null) return;

            // 场景已接入 Player_Seeker 时直接用；否则只在内存中换外观。都不保存场景。
            if (player.transform.Find(ModelChildName) == null)
            {
                foreach (var placeholder in new[] { "Body", "FacingMarker" })
                {
                    var t = player.transform.Find(placeholder);
                    if (t != null && t.TryGetComponent<MeshRenderer>(out var r)) r.enabled = false;
                }

                var visual = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath), player.transform);
                visual.name = ModelChildName;
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.Euler(0f, ModelYaw, 0f);
                AddSelfLight(player.transform);
            }

            Pose(player, "Idle", 0f);
            // 运行时进屋会隐藏屋顶；截图时手动关掉屋顶渲染，模拟玩家所在位置的实际视角。
            // Editor 程序集不引用 Map01，按序列化字段读取 BuildingReveal.revealRenderers。
            foreach (var behaviour in Object.FindObjectsByType<MonoBehaviour>())
            {
                if (behaviour == null || behaviour.GetType().Name != "BuildingReveal") continue;
                var roofs = new SerializedObject(behaviour).FindProperty("revealRenderers");
                for (var i = 0; roofs != null && i < roofs.arraySize; i++)
                {
                    if (roofs.GetArrayElementAtIndex(i).objectReferenceValue is Renderer roof) roof.enabled = false;
                }
            }

            ShootGameCamera(folder, "Seeker_Map01_GameCamera", player.transform.position);
            ShootGameCamera(folder, "Seeker_Map01_GameCamera_Zoom", player.transform.position, 0.28f);
        }

        /// <summary>
        /// 玩家身周的一点微光，只用来看清角色和脚下。纯视觉 Light，不挂 SafeLightSource，
        /// 不注册到 LightRegistry，所以不抵消黑暗伤害，也不算随身安全光。
        /// </summary>
        private static void AddSelfLight(Transform player)
        {
            var existing = player.Find(SelfLightName);
            var go = existing != null ? existing.gameObject : new GameObject(SelfLightName);
            go.transform.SetParent(player, false);
            go.transform.localPosition = new Vector3(0f, 2.2f, -0.6f);
            if (!go.TryGetComponent<Light>(out var light)) light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.78f, 0.55f);
            light.range = 4.5f;
            light.intensity = 1.6f;
            light.shadows = LightShadows.None;
        }

        private static void Pose(GameObject player, string clip, float time)
        {
            var visual = player.transform.Find(ModelChildName);
            Clip(clip).SampleAnimation(visual != null ? visual.gameObject : player, time);
        }

        /// <summary>关键姿势一字排开（正面一张、侧面一张），用于检查跑步、持枪、翻滚时的穿插。</summary>
        private static void CapturePoseSheet(string folder)
        {
            var shots = new (string clip, int frame)[]
            {
                ("Idle", 0), ("Run_F", 5), ("Run_F", 15), ("Run_B", 5), ("Run_L", 5), ("Run_R", 5),
                ("Fire", 1), ("Reload", 12), ("Roll", 4), ("Roll", 8), ("Roll", 12),
            };
            var variant = AssetDatabase.LoadAssetAtPath<GameObject>(VariantPath);
            var row = new GameObject[shots.Length];
            const float spacing = 1.3f;
            for (var i = 0; i < shots.Length; i++)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(variant);
                DisableGameplay(go);
                go.transform.position = new Vector3((i - (shots.Length - 1) * 0.5f) * spacing, 0f, 0f);
                go.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
                Pose(go, shots[i].clip, shots[i].frame / 30f);
                row[i] = go;
            }

            var size = new Vector2Int(3600, 640);
            var center = new Vector3(0f, 0.9f, 0f);
            Shoot(folder, "Seeker_Poses_Front", center + Vector3.back * 10f, center, 1.3f, size);
            foreach (var go in row) go.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            Shoot(folder, "Seeker_Poses_Side", center + Vector3.back * 10f, center, 1.3f, size);
            foreach (var go in row) Object.DestroyImmediate(go);
        }

        private static void DisableGameplay(GameObject player)
        {
            foreach (var behaviour in player.GetComponentsInChildren<MonoBehaviour>()) behaviour.enabled = false;
        }

        private static void ShootGameCamera(string folder, string name, Vector3 target, float distanceScale = 1f)
        {
            // 与 Map01Builder / FixedObliqueCamera 一致：偏移 (0,16,-11)、俯角 55°、FOV 50。
            var go = new GameObject("ShotCamera");
            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = 50f;
            cam.farClipPlane = 400f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.02f, 0.02f, 0.03f);
            go.transform.SetPositionAndRotation(target + new Vector3(0f, 16f, -11f) * distanceScale, Quaternion.Euler(55f, 0f, 0f));
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
            cam.fieldOfView = 22f;
            go.transform.position = position;
            go.transform.LookAt(target);
            if (turnaroundSun != null)
            {
                // 主光放在相机一侧偏上，保证每个视图的受光面朝向镜头。
                turnaroundSun.transform.rotation = Quaternion.Euler(40f, go.transform.eulerAngles.y + 35f, 0f);
            }
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
