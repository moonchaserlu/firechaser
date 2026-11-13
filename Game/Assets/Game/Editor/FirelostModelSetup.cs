using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

namespace WildernessExtraction.EditorTools
{
    /// <summary>
    /// 失火者（基础近战怪物）模型接入：配置 FBX 导入与动作片段、生成 URP 材质和动画控制器，
    /// 以 MeleeEnemy.prefab 为基础生成预制体变体（加 FirelostAnimationDriver），并输出检查截图。
    /// 不修改原 MeleeEnemy.prefab、MeleeEnemy 逻辑和任何场景。
    /// </summary>
    public static class FirelostModelSetup
    {
        public const string ModelPath = "Assets/Game/Art/Characters/Firelost/Firelost.fbx";
        public const string MaterialFolder = "Assets/Game/Art/Characters/Firelost/Materials";
        public const string TextureFolder = "Assets/Game/Art/Characters/Firelost/Textures";
        public const string EnemyPrefabPath = "Assets/Game/Prefabs/MeleeEnemy.prefab";
        public const string VariantPath = "Assets/Game/Prefabs/MeleeEnemy_Firelost.prefab";
        public const string ModelChildName = "FirelostModel";
        public const string ControllerPath = "Assets/Game/Art/Characters/Firelost/Firelost.controller";

        // 死亡后留下的遗物箱：LootContainer.prefab 的变体，外观换成 Firelost_Cache 模型
        public const string CacheModelPath = "Assets/Game/Art/Characters/Firelost/Firelost_Cache.fbx";
        public const string ContainerPrefabPath = "Assets/Game/Prefabs/LootContainer.prefab";
        public const string CachePrefabPath = "Assets/Game/Prefabs/LootBox_Firelost.prefab";
        public const string CacheChildName = "FirelostCacheModel";
        public const string LootTablePath = "Assets/Game/Data/LootTables/Loot_Firelost.asset";
        private static readonly Vector3 CacheSize = new Vector3(0.66f, 0.5f, 0.5f);

        private static readonly string[] LoopClips = { "Idle", "Walk", "Run" };

        // 与 firelost_rig.py 的 ATTACK_IMPACT_FRAME（30fps）一致：爪击起手到命中的时长。
        public const float AttackImpactTime = 11f / 30f;

        // 运动混合阈值（m/s）：巡逻速度约 moveSpeed × patrolSpeedMultiplier = 2.1，追击 3.5。
        private const float WalkSpeed = 1.6f;
        private const float RunSpeed = 3.5f;

        // Blender 导出的模型面朝方向与 MeleeEnemy 前方（+Z）之间的偏航修正。
        private const float ModelYaw = 0f;

        // 每个部件一张 Blender 烘焙的颜色贴图。旧灯熄灭：没有自发光，也不挂任何光源。
        private static readonly (string name, string texture, float smoothness)[] Materials =
        {
            ("M_Firelost_Body", "T_Firelost_Body", 0.12f),
            ("M_Firelost_Clothes", "T_Firelost_Clothes", 0.05f),
            ("M_Firelost_Lantern", "T_Firelost_Lantern", 0.2f),
        };

        [MenuItem("Wilderness/Firelost/Setup Model And Variant")]
        public static void Setup()
        {
            ConfigureImporter();
            ConfigureClips();
            BuildController();
            ConfigureCache();
            EnsureLootTable();
            BuildCachePrefab();
            BuildVariant();
            AssetDatabase.SaveAssets();
            foreach (var r in AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath).GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var mesh = r.sharedMesh;
                Debug.Log($"[Firelost] {r.name}: {mesh.triangles.Length / 3} tris, {r.bones.Length} bones, bounds {mesh.bounds.size}");
            }

            Debug.Log($"[Firelost] clips = {string.Join(", ", Clips().Select(c => $"{c.name}({c.length:0.00}s)"))}");

            Debug.Log($"[Firelost] 已生成 {VariantPath}");
        }

        /// <summary>批处理入口：只截图（需与 Setup 分开运行，否则 SRP Batcher 可能还没上传新材质颜色）。目录由 -firelostShots 指定。</summary>
        [MenuItem("Wilderness/Firelost/Capture Screenshots")]
        public static void Capture()
        {
            var args = System.Environment.GetCommandLineArgs();
            var folder = "FirelostShots";
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-firelostShots") folder = args[i + 1];
            }

            // 同 SeekerModelSetup：批处理截图时临时关闭 SRP Batcher（反射改属性，不把 URP 资产写回磁盘）。
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

            importer.SaveAndReimport();
        }

        /// <summary>Blender 导出的动作名为 "Firelost_Rig|Idle"，改成短名并给待机、走、跑打开循环。</summary>
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

        private static AnimationClip[] Clips() =>
            AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview")).ToArray();

        public static AnimationClip Clip(string name) => Clips().FirstOrDefault(c => c.name == name);

        private static void BuildController()
        {
            AssetDatabase.DeleteAsset(ControllerPath);
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            ctrl.AddParameter("Speed", AnimatorControllerParameterType.Float);
            ctrl.AddParameter("AttackR", AnimatorControllerParameterType.Trigger);
            ctrl.AddParameter("AttackL", AnimatorControllerParameterType.Trigger);
            ctrl.AddParameter("Hit", AnimatorControllerParameterType.Trigger);
            ctrl.AddParameter("Die", AnimatorControllerParameterType.Trigger);

            var sm = ctrl.layers[0].stateMachine;
            var locomotion = ctrl.CreateBlendTreeInController("Locomotion", out var tree, 0);
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;
            tree.AddChild(Clip("Idle"), 0f);
            tree.AddChild(Clip("Walk"), WalkSpeed);
            tree.AddChild(Clip("Run"), RunSpeed);
            sm.defaultState = locomotion;

            foreach (var (clip, trigger) in new[] { ("Attack_R", "AttackR"), ("Attack_L", "AttackL") })
            {
                var attack = sm.AddState(clip);
                attack.motion = Clip(clip);
                attack.tag = "Attack";
                var enter = sm.AddAnyStateTransition(attack);
                enter.AddCondition(AnimatorConditionMode.If, 0f, trigger);
                enter.hasExitTime = false;
                enter.duration = 0.06f;
                enter.canTransitionToSelf = false;
                var exit = attack.AddTransition(locomotion);
                exit.hasExitTime = true;
                exit.exitTime = 0.85f;
                exit.duration = 0.12f;
            }

            var hit = sm.AddState("Hit");
            hit.motion = Clip("Hit");
            var toHit = sm.AddAnyStateTransition(hit);
            toHit.AddCondition(AnimatorConditionMode.If, 0f, "Hit");
            toHit.hasExitTime = false;
            toHit.duration = 0.04f;
            toHit.canTransitionToSelf = true;
            var fromHit = hit.AddTransition(locomotion);
            fromHit.hasExitTime = true;
            fromHit.exitTime = 0.9f;
            fromHit.duration = 0.1f;

            // 死亡：停在最后一帧，不再离开
            var death = sm.AddState("Death");
            death.motion = Clip("Death");
            death.tag = "Dead";
            var toDeath = sm.AddAnyStateTransition(death);
            toDeath.AddCondition(AnimatorConditionMode.If, 0f, "Die");
            toDeath.hasExitTime = false;
            toDeath.duration = 0.08f;
            toDeath.canTransitionToSelf = false;
            EditorUtility.SetDirty(ctrl);
        }

        private static void ConfigureCache()
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(CacheModelPath);
            if (importer == null) throw new FileNotFoundException(CacheModelPath);
            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importAnimation = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importNormals = ModelImporterNormals.Import;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;

            var path = $"{MaterialFolder}/M_Firelost_Cache.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "M_Firelost_Cache" };
                AssetDatabase.CreateAsset(mat, path);
            }

            mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{TextureFolder}/T_Firelost_Cache.png"));
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Smoothness", 0.08f);
            mat.SetFloat("_Metallic", 0f);
            EditorUtility.SetDirty(mat);
            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "M_Firelost_Cache"), mat);
            importer.SaveAndReimport();
        }

        /// <summary>失火者掉落表（占位数值，主创/策划可直接改资源）：只在缺失时创建，不覆盖已调好的数值。</summary>
        private static void EnsureLootTable()
        {
            if (AssetDatabase.LoadAssetAtPath<LootTable>(LootTablePath) != null) return;
            LootTable.Entry Roll(string item, float weight, int min, int max) => new LootTable.Entry
            {
                item = AssetDatabase.LoadAssetAtPath<ItemDefinition>($"Assets/Game/Data/Items/{item}.asset"),
                weight = weight,
                minCount = min,
                maxCount = max,
            };

            var table = ScriptableObject.CreateInstance<LootTable>();
            table.randomRolls = 2;
            table.entries.Add(Roll("Item_Cloth", 30f, 1, 2));
            table.entries.Add(Roll("Item_ScrapMetal", 25f, 1, 2));
            table.entries.Add(Roll("Item_DirtyBandage", 20f, 1, 1));
            table.entries.Add(Roll("Item_MechanicalParts", 15f, 1, 1));
            table.entries.Add(Roll("Item_CannedFood", 10f, 1, 1));
            AssetDatabase.CreateAsset(table, LootTablePath);
        }

        private static void BuildCachePrefab()
        {
            var basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ContainerPrefabPath);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(basePrefab);
            try
            {
                instance.name = "LootBox_Firelost";
                // 原容器的方块只留碰撞：缩到箱子大小并隐藏渲染
                var body = instance.transform.Find("Body");
                body.localScale = CacheSize;
                body.localPosition = new Vector3(0f, CacheSize.y * 0.5f, 0f);
                body.GetComponent<MeshRenderer>().enabled = false;

                var visual = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(CacheModelPath), instance.transform);
                visual.name = CacheChildName;
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.identity;
                var renderer = visual.GetComponentInChildren<MeshRenderer>();
                renderer.shadowCastingMode = ShadowCastingMode.On;

                // 搜空后变灰的提示改到新模型上；尸体类型、显示名在掉落时由 EnemyLootDrop 写入
                var so = new SerializedObject(instance.GetComponent<LootContainer>());
                so.FindProperty("bodyRenderer").objectReferenceValue = renderer;
                so.FindProperty("displayName").stringValue = "失火者的遗物";
                so.FindProperty("containerType").enumValueIndex = (int)ContainerType.Corpse;
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(instance, CachePrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        private static void BuildVariant()
        {
            var enemyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefabPath);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(enemyPrefab);
            try
            {
                // 胶囊只是占位外观：变体里关掉渲染，GameObject 和 MeleeEnemy.bodyRenderer 引用保留，
                // 状态变色仍写在它的材质上，只是不显示。碰撞体、NavMeshAgent、AI 都沿用原预制体。
                var body = instance.transform.Find("Body");
                if (body != null && body.TryGetComponent<MeshRenderer>(out var placeholder)) placeholder.enabled = false;

                var visual = (GameObject)PrefabUtility.InstantiatePrefab(model, instance.transform);
                visual.name = ModelChildName;
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.Euler(0f, ModelYaw, 0f);
                visual.transform.localScale = Vector3.one;
                foreach (var r in visual.GetComponentsInChildren<Renderer>())
                {
                    r.shadowCastingMode = ShadowCastingMode.On;
                }

                // 死亡、扑击时肢体会离开原始包围盒，打开屏幕外更新，避免画面边缘被错误剔除
                foreach (var skinned in visual.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    skinned.updateWhenOffscreen = true;
                }

                var animator = visual.GetComponent<Animator>();
                if (animator == null) animator = visual.AddComponent<Animator>();
                animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

                // 攻击节奏读同一份 EnemyConfig，保证命中帧与出伤害时间一致
                var melee = instance.GetComponent<MeleeEnemy>();
                var config = new SerializedObject(melee).FindProperty("config").objectReferenceValue as EnemyConfig;
                var driver = instance.GetComponent<FirelostAnimationDriver>();
                if (driver == null) driver = instance.AddComponent<FirelostAnimationDriver>();
                driver.Configure(animator, visual.transform, config, AttackImpactTime);

                var drop = instance.GetComponent<EnemyLootDrop>();
                if (drop == null) drop = instance.AddComponent<EnemyLootDrop>();
                drop.Configure(AssetDatabase.LoadAssetAtPath<LootContainer>(CachePrefabPath),
                    AssetDatabase.LoadAssetAtPath<LootTable>(LootTablePath), visual);

                PrefabUtility.SaveAsPrefabAsset(instance, VariantPath);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        // ---------- 截图 ----------

        private static Light turnaroundSun;

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
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.5f, 0.5f, 0.52f);
            DynamicGI.UpdateEnvironment();
            turnaroundSun = sun;

            var enemy = Spawn(Vector3.zero, Quaternion.identity);
            var center = new Vector3(0f, 0.9f, 0f);
            var size = new Vector2Int(900, 1100);
            Shoot(folder, "Firelost_Unity_Front", center + Vector3.forward * 6f, center, 1.15f, size);
            Shoot(folder, "Firelost_Unity_Side", center + Vector3.left * 6f, center, 1.15f, size);
            Shoot(folder, "Firelost_Unity_Back", center + Vector3.back * 6f, center, 1.15f, size);
            Shoot(folder, "Firelost_Unity_ThreeQuarter", center + new Vector3(-3.5f, 1.8f, 4.5f), center, 0f, size);

            // 游戏镜头：面朝右下（镜头能看到正侧面）和背对镜头两种朝向
            sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            enemy.transform.rotation = Quaternion.Euler(0f, 150f, 0f);
            ShootGameCamera(folder, "Firelost_Unity_GameCamera", enemy.transform.position);
            ShootGameCamera(folder, "Firelost_Unity_GameCamera_Zoom", enemy.transform.position, 0.3f);
            ShootGameCamera(folder, "Firelost_Unity_GameCamera_Close", enemy.transform.position, 0.12f);
            enemy.transform.rotation = Quaternion.Euler(0f, 20f, 0f);
            ShootGameCamera(folder, "Firelost_Unity_GameCamera_Away_Close", enemy.transform.position, 0.12f);
            Object.DestroyImmediate(enemy);

            // 已渲染过的实例再次采样不会刷新蒙皮，所以每个姿势用新实例
            foreach (var (clip, frame, label) in new[] { ("Attack_R", 8, "AttackWindup"), ("Attack_R", 11, "AttackImpact"), ("Run", 5, "Run"), ("Death", 40, "Dead") })
            {
                var posed = Spawn(Vector3.zero, Quaternion.Euler(0f, 150f, 0f), clip, frame / 30f);
                ShootGameCamera(folder, $"Firelost_Unity_GameCamera_{label}_Close", posed.transform.position, 0.12f);
                Object.DestroyImmediate(posed);
            }

            var cache = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(CachePrefabPath));
            cache.transform.rotation = Quaternion.Euler(0f, 150f, 0f);
            ShootGameCamera(folder, "Firelost_Unity_GameCamera_LootBox_Close", Vector3.zero, 0.12f);
            Object.DestroyImmediate(cache);

            CapturePoseSheet(folder);
            turnaroundSun = null;
        }

        private static void CaptureMap01(string folder)
        {
            const string scenePath = "Assets/Map01/Scenes/Map01.unity";
            if (!File.Exists(scenePath)) return;
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            var player = GameObject.Find("Player_Seeker") ?? GameObject.Find("Player");
            if (player == null) return;

            // 只在内存中放一只失火者在玩家右前方、面朝玩家扑击（命中帧），不保存场景。
            var pos = player.transform.position + new Vector3(1.4f, 0f, 0.8f);
            var toPlayer = player.transform.position - pos;
            toPlayer.y = 0f;
            Spawn(pos, Quaternion.LookRotation(toPlayer), "Attack_R", AttackImpactTime);
            // 另一只已被击杀、留下的遗物箱（玩家左前方）
            var cache = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(CachePrefabPath));
            cache.transform.SetPositionAndRotation(player.transform.position + new Vector3(-1.6f, 0f, 1.0f), Quaternion.Euler(0f, 200f, 0f));
            // 运行时进屋会隐藏屋顶；截图时同样关掉 BuildingReveal 管理的屋顶渲染（按序列化字段读取，Editor 程序集不引用 Map01）。
            foreach (var behaviour in Object.FindObjectsByType<MonoBehaviour>())
            {
                if (behaviour == null || behaviour.GetType().Name != "BuildingReveal") continue;
                var roofs = new SerializedObject(behaviour).FindProperty("revealRenderers");
                for (var i = 0; roofs != null && i < roofs.arraySize; i++)
                {
                    if (roofs.GetArrayElementAtIndex(i).objectReferenceValue is Renderer roof) roof.enabled = false;
                }
            }

            Debug.Log($"[Firelost] Map01 player at {player.transform.position}, firelost at {pos}");
            var mid = (player.transform.position + pos) * 0.5f;
            ShootGameCamera(folder, "Firelost_Map01_GameCamera", mid);
            ShootGameCamera(folder, "Firelost_Map01_GameCamera_Zoom", mid, 0.3f);
        }

        private static GameObject Spawn(Vector3 position, Quaternion rotation, string clip = "Idle", float time = 0f)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(VariantPath));
            if (go.TryGetComponent<NavMeshAgent>(out var agent)) agent.enabled = false;
            foreach (var behaviour in go.GetComponentsInChildren<MonoBehaviour>()) behaviour.enabled = false;
            go.transform.SetPositionAndRotation(position, rotation);
            Clip(clip).SampleAnimation(go.transform.Find(ModelChildName).gameObject, time);
            return go;
        }

        /// <summary>关键姿势一字排开（正面一张、侧面一张），检查蒙皮穿插和动作幅度。</summary>
        private static void CapturePoseSheet(string folder)
        {
            var shots = new (string clip, int frame)[]
            {
                ("Idle", 0), ("Walk", 8), ("Walk", 24), ("Run", 5), ("Run", 15), ("Attack_R", 8), ("Attack_R", 11),
                ("Attack_R", 16), ("Attack_L", 11), ("Hit", 3), ("Death", 16), ("Death", 24), ("Death", 40),
            };
            const float spacing = 1.5f;
            var size = new Vector2Int(3900, 640);
            var center = new Vector3(0f, 0.85f, 0f);
            foreach (var (label, yaw) in new[] { ("Front", 180f), ("Side", 90f) })
            {
                var row = new GameObject[shots.Length];
                for (var i = 0; i < shots.Length; i++)
                {
                    var pos = new Vector3((i - (shots.Length - 1) * 0.5f) * spacing, 0f, 0f);
                    row[i] = Spawn(pos, Quaternion.Euler(0f, yaw, 0f), shots[i].clip, shots[i].frame / 30f);
                }

                Shoot(folder, $"Firelost_Poses_{label}", center + Vector3.back * 12f, center, 1.65f, size);
                foreach (var go in row) Object.DestroyImmediate(go);
            }
        }

        private static void ShootGameCamera(string folder, string name, Vector3 target, float distanceScale = 1f)
        {
            // 与运行时 FixedObliqueCamera 默认值一致：偏移、俯角、FOV、跟随点抬高。
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
            cam.fieldOfView = 22f;
            go.transform.position = position;
            go.transform.LookAt(target);
            if (turnaroundSun != null)
            {
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
