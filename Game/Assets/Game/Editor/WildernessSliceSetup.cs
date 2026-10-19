#if false // Disabled: duplicate setup from an overlapping Claude session; VerticalSliceBuilder is authoritative.
using System.Collections.Generic;
using System.IO;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace WildernessExtraction.EditorTools
{
    /// <summary>
    /// 幂等搭建首个可玩切片：补齐输入动作、创建配置资产、重建灰盒场景（含 NavMesh、敌人、容器、撤离点、HUD），并更新 Build Settings。
    /// 配置资产只在缺失时创建，保留已调整的数值；场景只重建本工具生成的根对象。
    /// 命令行：-executeMethod WildernessExtraction.EditorTools.WildernessSliceSetup.RunAll
    /// </summary>
    public static class WildernessSliceSetup
    {
        public const string RootFolder = "Assets/Game";
        public const string ScenePath = RootFolder + "/Scenes/WildernessSlice.unity";
        public const string InputActionsPath = "Assets/InputSystem_Actions.inputactions";

        private const string DataFolder = RootFolder + "/Data";
        private const string MaterialFolder = DataFolder + "/Materials";
        private const string NavMeshDataPath = RootFolder + "/Scenes/WildernessSlice/NavMesh-Level.asset";

        private const string EnvironmentRoot = "[Environment]";
        private const string LevelRoot = "[Level]";
        private const string GameplayRoot = "[Gameplay]";
        private const string UiRoot = "[UI]";
        private static readonly string[] GeneratedRoots = { EnvironmentRoot, LevelRoot, GameplayRoot, UiRoot };

        // 灰盒布局（30×30 米，原点居中）。出生点在西南，撤离区在东南；南侧走廊为安全近路。
        private static readonly Vector3 SpawnPosition = new Vector3(-12f, 0f, -11f);
        private static readonly Vector3 ExtractionPosition = new Vector3(12f, 0f, -11f);
        private static readonly Vector3 EnemyPosition = new Vector3(4f, 0f, 3f);
        private static readonly Vector3 CameraOffset = new Vector3(0f, 16f, -11f);

        private sealed class SliceAssets
        {
            public PlayerConfig Player;
            public WeaponConfig Weapon;
            public EnemyConfig Enemy;
            public RunRulesConfig Rules;
            public LootTable TableA;
            public LootTable TableB;
            public LootTable TableC;
            public InputActionAsset Input;
            public Material Ground;
            public Material Wall;
            public Material PlayerBody;
            public Material EnemyBody;
            public Material Container;
            public Material Extraction;
            public Material Tracer;
        }

        [MenuItem("Wilderness/Setup/Run Full Setup", priority = 0)]
        public static void RunAll()
        {
            UpdateInputActions();
            var assets = CreateOrLoadAssets();
            if (BuildScene(assets))
            {
                UpdateBuildSettings();
                Debug.Log($"[WildernessSliceSetup] 完成：{ScenePath}");
            }
        }

        [MenuItem("Wilderness/Setup/Update Input Actions", priority = 20)]
        public static void UpdateInputActions()
        {
            var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);
            if (asset == null)
            {
                Debug.LogWarning($"[WildernessSliceSetup] 未找到 {InputActionsPath}，运行时将使用代码默认绑定。");
                return;
            }

            var map = asset.FindActionMap("Player", false) ?? asset.AddActionMap("Player");
            var changed = false;
            changed |= EnsureAction(map, "Move", InputActionType.Value, null, "Vector2");
            changed |= EnsureAction(map, "Aim", InputActionType.Value, "<Pointer>/position", "Vector2");
            changed |= EnsureAction(map, "Attack", InputActionType.Button, "<Mouse>/leftButton", "Button");
            changed |= EnsureAction(map, "Dodge", InputActionType.Button, "<Keyboard>/space", "Button");
            changed |= EnsureAction(map, "Reload", InputActionType.Button, "<Keyboard>/r", "Button");
            changed |= EnsureAction(map, "Interact", InputActionType.Button, "<Keyboard>/e", "Button");
            changed |= EnsureAction(map, "Pause", InputActionType.Button, "<Keyboard>/escape", "Button");
            if (!changed)
            {
                return;
            }

            File.WriteAllText(InputActionsPath, asset.ToJson());
            AssetDatabase.ImportAsset(InputActionsPath);
            Debug.Log("[WildernessSliceSetup] 已补齐 Player 动作表中的 Aim/Dodge/Reload/Pause 等动作。");
        }

        [MenuItem("Wilderness/Setup/Create Config Assets", priority = 21)]
        public static void CreateConfigAssetsMenu()
        {
            CreateOrLoadAssets();
        }

        [MenuItem("Wilderness/Setup/Rebuild Graybox Scene", priority = 22)]
        public static void RebuildSceneMenu()
        {
            if (BuildScene(CreateOrLoadAssets()))
            {
                UpdateBuildSettings();
            }
        }

        private static bool EnsureAction(InputActionMap map, string name, InputActionType type, string binding, string controlType)
        {
            if (map.FindAction(name, false) != null)
            {
                return false;
            }

            var action = map.AddAction(name, type, expectedControlType: controlType);
            if (name == "Move")
            {
                action.AddCompositeBinding("2DVector")
                    .With("Up", "<Keyboard>/w", "Keyboard&Mouse")
                    .With("Down", "<Keyboard>/s", "Keyboard&Mouse")
                    .With("Left", "<Keyboard>/a", "Keyboard&Mouse")
                    .With("Right", "<Keyboard>/d", "Keyboard&Mouse");
            }
            else
            {
                action.AddBinding(binding, groups: "Keyboard&Mouse");
            }

            return true;
        }

        // ---------- 资产 ----------

        private static SliceAssets CreateOrLoadAssets()
        {
            EnsureFolder(DataFolder + "/Config");
            EnsureFolder(DataFolder + "/Items");
            EnsureFolder(DataFolder + "/LootTables");
            EnsureFolder(MaterialFolder);

            var scrap = LoadOrCreateItem("ScrapMetal", "scrap_metal", "废金属", 1, new Color(0.6f, 0.65f, 0.7f));
            var can = LoadOrCreateItem("CannedFood", "canned_food", "罐头", 2, new Color(0.95f, 0.6f, 0.25f));
            var battery = LoadOrCreateItem("OldBattery", "old_battery", "旧电池", 4, new Color(0.45f, 0.95f, 0.45f));

            var assets = new SliceAssets
            {
                Player = LoadOrCreate<PlayerConfig>(DataFolder + "/Config/PlayerConfig.asset", null),
                Weapon = LoadOrCreate<WeaponConfig>(DataFolder + "/Config/WeaponConfig.asset", null),
                Enemy = LoadOrCreate<EnemyConfig>(DataFolder + "/Config/EnemyConfig.asset", null),
                Rules = LoadOrCreate<RunRulesConfig>(DataFolder + "/Config/RunRulesConfig.asset", null),
                // 掉落内容为灰盒临时值：A 低收益近路，B 中央中等收益，C 远端提供旧电池。合计 9 件 > 8 格，可触发背包已满。
                TableA = LoadOrCreateTable("LootTable_A", new LootTable.Entry(scrap, 2)),
                TableB = LoadOrCreateTable("LootTable_B", new LootTable.Entry(scrap, 2), new LootTable.Entry(can, 2)),
                TableC = LoadOrCreateTable("LootTable_C", new LootTable.Entry(can, 1), new LootTable.Entry(battery, 2)),
                Input = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath),
                Ground = LoadOrCreateMaterial("Ground", new Color(0.42f, 0.5f, 0.36f), false),
                Wall = LoadOrCreateMaterial("Wall", new Color(0.55f, 0.52f, 0.48f), false),
                PlayerBody = LoadOrCreateMaterial("Player", new Color(0.25f, 0.55f, 0.95f), false),
                EnemyBody = LoadOrCreateMaterial("Enemy", new Color(0.75f, 0.2f, 0.2f), false),
                Container = LoadOrCreateMaterial("Container", new Color(0.65f, 0.45f, 0.2f), false),
                Extraction = LoadOrCreateMaterial("Extraction", new Color(0.2f, 1f, 0.45f), true),
                Tracer = LoadOrCreateMaterial("Tracer", new Color(1f, 0.9f, 0.4f), true)
            };

            AssetDatabase.SaveAssets();
            return assets;
        }

        private static T LoadOrCreate<T>(string path, System.Action<T> init) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null)
            {
                return asset;
            }

            asset = ScriptableObject.CreateInstance<T>();
            init?.Invoke(asset);
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static ItemDefinition LoadOrCreateItem(string fileName, string id, string displayName, int value, Color color)
        {
            return LoadOrCreate<ItemDefinition>($"{DataFolder}/Items/{fileName}.asset", item =>
            {
                item.itemId = id;
                item.displayName = displayName;
                item.stashValue = value;
                item.iconColor = color;
            });
        }

        private static LootTable LoadOrCreateTable(string fileName, params LootTable.Entry[] entries)
        {
            return LoadOrCreate<LootTable>($"{DataFolder}/LootTables/{fileName}.asset",
                table => table.entries = new List<LootTable.Entry>(entries));
        }

        private static Material LoadOrCreateMaterial(string name, Color color, bool unlit)
        {
            var path = $"{MaterialFolder}/M_{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                var shader = Shader.Find(unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit")
                             ?? Shader.Find(unlit ? "Unlit/Color" : "Standard");
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }

            EditorUtility.SetDirty(material);
            return material;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent))
            {
                EnsureFolder(parent);
            }

            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        // ---------- 场景 ----------

        private static bool BuildScene(SliceAssets assets)
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return false;
            }

            EnsureFolder(Path.GetDirectoryName(NavMeshDataPath)?.Replace('\\', '/'));
            var scene = File.Exists(ScenePath)
                ? EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single)
                : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            foreach (var root in scene.GetRootGameObjects())
            {
                if (System.Array.IndexOf(GeneratedRoots, root.name) >= 0)
                {
                    Object.DestroyImmediate(root);
                }
            }

            if (string.IsNullOrEmpty(scene.path))
            {
                EditorSceneManager.SaveScene(scene, ScenePath);
            }

            BuildEnvironment();
            var level = BuildLevel(assets, out var containers);
            BakeNavMesh(level);
            var gameplay = BuildGameplay(assets, containers);
            BuildHud(gameplay.Session, assets.Rules.inventoryCapacity);

            EditorSceneManager.MarkSceneDirty(scene);
            return EditorSceneManager.SaveScene(scene, ScenePath);
        }

        private static void BuildEnvironment()
        {
            var root = new GameObject(EnvironmentRoot);
            var lightObject = new GameObject("Directional Light");
            lightObject.transform.SetParent(root.transform, false);
            lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.shadows = LightShadows.Soft;
        }

        private static GameObject BuildLevel(SliceAssets assets, out LootContainer[] containers)
        {
            var root = new GameObject(LevelRoot);

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.SetParent(root.transform, false);
            ground.transform.localScale = new Vector3(3f, 1f, 3f);
            ground.GetComponent<Renderer>().sharedMaterial = assets.Ground;

            var walls = new GameObject("Walls").transform;
            walls.SetParent(root.transform, false);
            // 外墙
            Wall(walls, assets, "Wall_North", new Vector3(0f, 0f, 15f), new Vector3(30.5f, 2f, 0.5f));
            Wall(walls, assets, "Wall_South", new Vector3(0f, 0f, -15f), new Vector3(30.5f, 2f, 0.5f));
            Wall(walls, assets, "Wall_East", new Vector3(15f, 0f, 0f), new Vector3(0.5f, 2f, 30.5f));
            Wall(walls, assets, "Wall_West", new Vector3(-15f, 0f, 0f), new Vector3(0.5f, 2f, 30.5f));
            // 南侧安全走廊北墙；x∈[-3,1] 的缺口是穿过敌人区域的危险捷径，x<-8 为西侧安全绕行入口。
            Wall(walls, assets, "Wall_Corridor_W", new Vector3(-5.5f, 0f, -5f), new Vector3(5f, 2f, 0.5f));
            Wall(walls, assets, "Wall_Corridor_E", new Vector3(8f, 0f, -5f), new Vector3(14f, 2f, 0.5f));
            // 西侧绕行通道与北侧通道的遮挡墙
            Wall(walls, assets, "Wall_WestLane", new Vector3(-8f, 0f, 2f), new Vector3(0.5f, 2f, 14f));
            Wall(walls, assets, "Wall_NorthLane", new Vector3(-2f, 0f, 9f), new Vector3(12f, 2f, 0.5f));
            // 东北死角：入口在 x=7、z>12，需经北侧通道进入
            Wall(walls, assets, "Wall_Pocket_W", new Vector3(7f, 0f, 9f), new Vector3(0.5f, 2f, 6f));
            Wall(walls, assets, "Wall_Pocket_S", new Vector3(11f, 0f, 6f), new Vector3(8f, 2f, 0.5f));

            var containerRoot = new GameObject("Containers").transform;
            containerRoot.SetParent(root.transform, false);
            containers = new[]
            {
                Container(containerRoot, assets, "Container_A", "近路补给箱 A", new Vector3(-4f, 0f, -12.5f), assets.TableA),
                Container(containerRoot, assets, "Container_B", "中央补给箱 B", new Vector3(0f, 0f, 2f), assets.TableB),
                Container(containerRoot, assets, "Container_C", "死角补给箱 C", new Vector3(12.5f, 0f, 12.5f), assets.TableC)
            };
            return root;
        }

        private static void Wall(Transform parent, SliceAssets assets, string name, Vector3 position, Vector3 size)
        {
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = name;
            wall.transform.SetParent(parent, false);
            wall.transform.localPosition = position + Vector3.up * (size.y * 0.5f);
            wall.transform.localScale = size;
            wall.GetComponent<Renderer>().sharedMaterial = assets.Wall;
        }

        private static LootContainer Container(Transform parent, SliceAssets assets, string name, string displayName, Vector3 position, LootTable table)
        {
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = name;
            body.transform.SetParent(parent, false);
            body.transform.localPosition = position + Vector3.up * 0.5f;
            body.transform.localScale = new Vector3(1.2f, 1f, 0.8f);
            var renderer = body.GetComponent<Renderer>();
            renderer.sharedMaterial = assets.Container;

            var container = body.AddComponent<LootContainer>();
            Set(container, "displayName", displayName);
            Set(container, "lootTable", table);
            Set(container, "bodyRenderer", renderer);
            return container;
        }

        private static void BakeNavMesh(GameObject level)
        {
            var surface = level.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.BuildNavMesh();

            if (AssetDatabase.LoadAssetAtPath<NavMeshData>(NavMeshDataPath) != null)
            {
                AssetDatabase.DeleteAsset(NavMeshDataPath);
            }

            AssetDatabase.CreateAsset(surface.navMeshData, NavMeshDataPath);
            EditorUtility.SetDirty(surface);
        }

        private sealed class GameplayRefs
        {
            public RunSession Session;
        }

        private static GameplayRefs BuildGameplay(SliceAssets assets, LootContainer[] containers)
        {
            var root = new GameObject(GameplayRoot);

            var spawn = new GameObject("PlayerSpawn").transform;
            spawn.SetParent(root.transform, false);
            spawn.localPosition = SpawnPosition + Vector3.up;
            spawn.localRotation = Quaternion.Euler(0f, 90f, 0f);

            var camera = BuildCamera(root.transform);
            var player = BuildPlayer(root.transform, assets, spawn, camera);
            Set(camera.GetComponent<FixedObliqueCamera>(), "target", player.transform);

            BuildEnemy(root.transform, assets, player);
            var zone = BuildExtractionZone(root.transform, assets, player);

            var sessionObject = new GameObject("RunSession");
            sessionObject.transform.SetParent(root.transform, false);
            var session = sessionObject.AddComponent<RunSession>();
            Set(session, "player", player);
            Set(session, "extractionZone", zone);
            Set(session, "playerSpawn", spawn);
            return new GameplayRefs { Session = session };
        }

        private static Camera BuildCamera(Transform parent)
        {
            var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            cameraObject.transform.SetParent(parent, false);
            cameraObject.transform.SetPositionAndRotation(SpawnPosition + Vector3.up + CameraOffset, Quaternion.Euler(55f, 0f, 0f));
            var camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = 50f;
            cameraObject.AddComponent<AudioListener>();
            var follow = cameraObject.AddComponent<FixedObliqueCamera>();
            Set(follow, "offset", CameraOffset);
            Set(follow, "eulerAngles", new Vector3(55f, 0f, 0f));
            return camera;
        }

        private static PlayerCharacter BuildPlayer(Transform parent, SliceAssets assets, Transform spawn, Camera camera)
        {
            var player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            player.name = "Player";
            player.transform.SetParent(parent, false);
            player.transform.SetPositionAndRotation(spawn.position, spawn.rotation);
            Object.DestroyImmediate(player.GetComponent<CapsuleCollider>());
            player.GetComponent<Renderer>().sharedMaterial = assets.PlayerBody;

            var controller = player.AddComponent<CharacterController>();
            controller.height = 2f;
            controller.radius = 0.4f;
            controller.center = Vector3.zero;

            Visual(player.transform, "Facing", PrimitiveType.Cube, new Vector3(0f, 0.45f, 0.45f), new Vector3(0.3f, 0.2f, 0.4f), assets.Wall);

            var muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(player.transform, false);
            muzzle.localPosition = new Vector3(0f, 0.2f, 0.7f);
            var tracer = muzzle.gameObject.AddComponent<LineRenderer>();
            tracer.useWorldSpace = true;
            tracer.positionCount = 2;
            tracer.widthMultiplier = 0.06f;
            tracer.shadowCastingMode = ShadowCastingMode.Off;
            tracer.sharedMaterial = assets.Tracer;
            tracer.enabled = false;

            // RequireComponent 会一并添加 Health、输入、移动、射击、背包和交互组件。
            var character = player.AddComponent<PlayerCharacter>();
            var input = player.GetComponent<PlayerInputReader>();
            var motor = player.GetComponent<PlayerMotor>();
            Set(character, "config", assets.Player);
            Set(input, "actions", assets.Input);
            Set(motor, "config", assets.Player);
            Set(motor, "input", input);
            Set(motor, "viewCamera", camera);
            var shooter = player.GetComponent<PlayerShooter>();
            Set(shooter, "config", assets.Weapon);
            Set(shooter, "input", input);
            Set(shooter, "motor", motor);
            Set(shooter, "muzzle", muzzle);
            Set(shooter, "tracer", tracer);
            Set(player.GetComponent<PlayerInventory>(), "rules", assets.Rules);
            var interactor = player.GetComponent<PlayerInteractor>();
            Set(interactor, "rules", assets.Rules);
            Set(interactor, "input", input);
            Set(interactor, "inventory", player.GetComponent<PlayerInventory>());
            Set(interactor, "health", player.GetComponent<Health>());
            Set(player.GetComponent<Health>(), "maxHealth", assets.Player.maxHealth);
            return character;
        }

        private static void BuildEnemy(Transform parent, SliceAssets assets, PlayerCharacter player)
        {
            var enemy = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            enemy.name = "MeleeEnemy";
            enemy.transform.SetParent(parent, false);
            enemy.transform.localPosition = EnemyPosition + Vector3.up;
            enemy.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            var body = enemy.GetComponent<Renderer>();
            body.sharedMaterial = assets.EnemyBody;
            Visual(enemy.transform, "Facing", PrimitiveType.Cube, new Vector3(0f, 0.45f, 0.45f), new Vector3(0.5f, 0.15f, 0.3f), assets.Wall);

            var agent = enemy.AddComponent<NavMeshAgent>();
            agent.baseOffset = 1f;
            agent.height = 2f;
            agent.radius = 0.45f;
            agent.angularSpeed = 720f;
            agent.acceleration = 20f;
            agent.speed = assets.Enemy.moveSpeed;

            var melee = enemy.AddComponent<MeleeEnemy>();
            var health = enemy.GetComponent<Health>();
            Set(health, "maxHealth", assets.Enemy.maxHealth);
            Set(melee, "config", assets.Enemy);
            Set(melee, "target", player.GetComponent<Health>());
            Set(melee, "bodyRenderer", body);
        }

        private static ExtractionZone BuildExtractionZone(Transform parent, SliceAssets assets, PlayerCharacter player)
        {
            const float radius = 2.5f;
            var zoneObject = new GameObject("ExtractionZone");
            zoneObject.transform.SetParent(parent, false);
            zoneObject.transform.localPosition = ExtractionPosition;
            var zone = zoneObject.AddComponent<ExtractionZone>();
            Set(zone, "rules", assets.Rules);
            Set(zone, "radius", radius);
            Set(zone, "player", player);

            Visual(zoneObject.transform, "Area", PrimitiveType.Cylinder, new Vector3(0f, 0.02f, 0f), new Vector3(radius * 2f, 0.01f, radius * 2f), assets.Extraction);
            Visual(zoneObject.transform, "Beacon", PrimitiveType.Cylinder, new Vector3(radius, 2f, radius), new Vector3(0.25f, 2f, 0.25f), assets.Extraction);
            return zone;
        }

        /// <summary>无碰撞的装饰几何体。</summary>
        private static void Visual(Transform parent, string name, PrimitiveType type, Vector3 localPosition, Vector3 localScale, Material material)
        {
            var visual = GameObject.CreatePrimitive(type);
            visual.name = name;
            Object.DestroyImmediate(visual.GetComponent<Collider>());
            visual.transform.SetParent(parent, false);
            visual.transform.localPosition = localPosition;
            visual.transform.localScale = localScale;
            visual.GetComponent<Renderer>().sharedMaterial = material;
        }

        // ---------- HUD ----------

        private static void BuildHud(RunSession session, int slotCount)
        {
            var root = new GameObject(UiRoot);

            var eventSystem = new GameObject("EventSystem", typeof(EventSystem));
            eventSystem.transform.SetParent(root.transform, false);
            eventSystem.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();

            var canvasObject = new GameObject("HUD", typeof(RectTransform));
            canvasObject.transform.SetParent(root.transform, false);
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasObject.AddComponent<GraphicRaycaster>();
            var hudRoot = canvasObject.transform;

            // 左上：生命、体力、弹药
            var vitals = Panel(hudRoot, "Vitals", new Vector2(0f, 1f), new Vector2(24f, -24f), new Vector2(380f, 150f), new Color(0f, 0f, 0f, 0.45f));
            var healthLabel = Label(vitals, "HealthLabel", new Vector2(0f, 1f), new Vector2(16f, -10f), new Vector2(348f, 26f), 22, TextAnchor.MiddleLeft);
            var healthFill = Bar(vitals, "HealthBar", new Vector2(16f, -40f), new Vector2(348f, 12f), new Color(0.9f, 0.25f, 0.25f));
            var staminaLabel = Label(vitals, "StaminaLabel", new Vector2(0f, 1f), new Vector2(16f, -58f), new Vector2(348f, 26f), 22, TextAnchor.MiddleLeft);
            var staminaFill = Bar(vitals, "StaminaBar", new Vector2(16f, -88f), new Vector2(348f, 12f), new Color(0.95f, 0.8f, 0.25f));
            var ammoLabel = Label(vitals, "AmmoLabel", new Vector2(0f, 1f), new Vector2(16f, -106f), new Vector2(348f, 30f), 24, TextAnchor.MiddleLeft);

            // 右上：仓库
            var stashPanel = Panel(hudRoot, "Stash", new Vector2(1f, 1f), new Vector2(-24f, -24f), new Vector2(520f, 60f), new Color(0f, 0f, 0f, 0.45f));
            var stashLabel = Label(stashPanel, "StashLabel", new Vector2(0f, 1f), new Vector2(12f, -6f), new Vector2(496f, 48f), 20, TextAnchor.MiddleLeft);

            // 底部居中：背包
            const float slotSize = 56f;
            const float slotSpacing = 8f;
            var inventoryWidth = slotCount * slotSize + (slotCount - 1) * slotSpacing + 24f;
            var inventoryPanel = Panel(hudRoot, "Inventory", new Vector2(0.5f, 0f), new Vector2(0f, 24f), new Vector2(Mathf.Max(inventoryWidth, 320f), 110f), new Color(0f, 0f, 0f, 0.45f));
            var inventoryLabel = Label(inventoryPanel, "InventoryLabel", new Vector2(0f, 1f), new Vector2(12f, -6f), new Vector2(300f, 28f), 20, TextAnchor.MiddleLeft);
            var slots = new Image[slotCount];
            for (var i = 0; i < slotCount; i++)
            {
                var slot = CreateRect(inventoryPanel, $"Slot_{i + 1}", new Vector2(0f, 0f), new Vector2(12f + i * (slotSize + slotSpacing), 12f), new Vector2(slotSize, slotSize));
                slots[i] = slot.gameObject.AddComponent<Image>();
            }

            // 屏幕中下：交互提示与撤离进度；顶部居中：消息
            var promptLabel = Label(hudRoot, "PromptLabel", new Vector2(0.5f, 0.5f), new Vector2(0f, -160f), new Vector2(900f, 40f), 28, TextAnchor.MiddleCenter);
            var extractionPanel = Panel(hudRoot, "Extraction", new Vector2(0.5f, 0.5f), new Vector2(0f, -220f), new Vector2(520f, 64f), new Color(0f, 0f, 0f, 0.55f));
            var extractionLabel = Label(extractionPanel, "ExtractionLabel", new Vector2(0f, 1f), new Vector2(12f, -4f), new Vector2(496f, 30f), 22, TextAnchor.MiddleCenter);
            var extractionFill = Bar(extractionPanel, "ExtractionBar", new Vector2(12f, -40f), new Vector2(496f, 14f), new Color(0.3f, 1f, 0.5f));
            var messageLabel = Label(hudRoot, "MessageLabel", new Vector2(0.5f, 1f), new Vector2(0f, -110f), new Vector2(1000f, 44f), 30, TextAnchor.MiddleCenter);

            // 暂停与结算全屏面板
            var pausePanel = FullScreenPanel(hudRoot, "PausePanel", new Color(0f, 0f, 0f, 0.6f));
            var pauseLabel = Label(pausePanel, "PauseLabel", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(800f, 120f), 40, TextAnchor.MiddleCenter);
            pauseLabel.text = "已暂停\n按 Esc 继续";

            var resultPanel = FullScreenPanel(hudRoot, "ResultPanel", new Color(0f, 0f, 0f, 0.75f));
            var resultTitle = Label(resultPanel, "ResultTitle", new Vector2(0.5f, 0.5f), new Vector2(0f, 170f), new Vector2(900f, 80f), 56, TextAnchor.MiddleCenter);
            var resultBody = Label(resultPanel, "ResultBody", new Vector2(0.5f, 0.5f), new Vector2(0f, 10f), new Vector2(1100f, 220f), 26, TextAnchor.MiddleCenter);
            var buttonRect = CreateRect(resultPanel, "RestartButton", new Vector2(0.5f, 0.5f), new Vector2(0f, -170f), new Vector2(280f, 64f));
            var buttonImage = buttonRect.gameObject.AddComponent<Image>();
            buttonImage.color = new Color(0.25f, 0.55f, 0.95f);
            var restartButton = buttonRect.gameObject.AddComponent<Button>();
            restartButton.targetGraphic = buttonImage;
            var buttonLabel = Label(buttonRect, "Label", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(280f, 64f), 28, TextAnchor.MiddleCenter);
            buttonLabel.text = "重新开始 (E)";

            var hud = canvasObject.AddComponent<RunHud>();
            Set(hud, "session", session);
            Set(hud, "healthLabel", healthLabel);
            Set(hud, "healthFill", healthFill);
            Set(hud, "staminaLabel", staminaLabel);
            Set(hud, "staminaFill", staminaFill);
            Set(hud, "ammoLabel", ammoLabel);
            Set(hud, "inventoryLabel", inventoryLabel);
            SetArray(hud, "inventorySlots", slots);
            Set(hud, "promptLabel", promptLabel);
            Set(hud, "extractionPanel", extractionPanel.gameObject);
            Set(hud, "extractionFill", extractionFill);
            Set(hud, "extractionLabel", extractionLabel);
            Set(hud, "messageLabel", messageLabel);
            Set(hud, "stashLabel", stashLabel);
            Set(hud, "pausePanel", pausePanel.gameObject);
            Set(hud, "resultPanel", resultPanel.gameObject);
            Set(hud, "resultTitle", resultTitle);
            Set(hud, "resultBody", resultBody);
            Set(hud, "restartButton", restartButton);

            pausePanel.gameObject.SetActive(false);
            resultPanel.gameObject.SetActive(false);
            extractionPanel.gameObject.SetActive(false);
        }

        /// <summary>以 anchor 为锚点和轴心创建矩形。</summary>
        private static RectTransform CreateRect(Transform parent, string name, Vector2 anchor, Vector2 position, Vector2 size)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        private static RectTransform Panel(Transform parent, string name, Vector2 anchor, Vector2 position, Vector2 size, Color color)
        {
            var rect = CreateRect(parent, name, anchor, position, size);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return rect;
        }

        private static RectTransform FullScreenPanel(Transform parent, string name, Color color)
        {
            var rect = Panel(parent, name, Vector2.zero, Vector2.zero, Vector2.zero, color);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return rect;
        }

        private static Text Label(Transform parent, string name, Vector2 anchor, Vector2 position, Vector2 size, int fontSize, TextAnchor alignment)
        {
            var rect = CreateRect(parent, name, anchor, position, size);
            var text = rect.gameObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            rect.gameObject.AddComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.8f);
            return text;
        }

        /// <summary>背景条 + 填充条；RunHud 通过填充条的 anchorMax.x 表示进度。</summary>
        private static RectTransform Bar(Transform parent, string name, Vector2 position, Vector2 size, Color fillColor)
        {
            var background = Panel(parent, name, new Vector2(0f, 1f), position, size, new Color(1f, 1f, 1f, 0.15f));
            var fill = new GameObject("Fill", typeof(RectTransform)).GetComponent<RectTransform>();
            fill.SetParent(background, false);
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = Vector2.one;
            fill.pivot = new Vector2(0f, 0.5f);
            fill.offsetMin = Vector2.zero;
            fill.offsetMax = Vector2.zero;
            var image = fill.gameObject.AddComponent<Image>();
            image.color = fillColor;
            image.raycastTarget = false;
            return fill;
        }

        // ---------- Build Settings ----------

        [MenuItem("Wilderness/Setup/Update Build Settings", priority = 23)]
        public static void UpdateBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(ScenePath, true) };
            foreach (var existing in EditorBuildSettings.scenes)
            {
                if (existing.path != ScenePath)
                {
                    scenes.Add(existing);
                }
            }

            EditorBuildSettings.scenes = scenes.ToArray();
        }

        // ---------- 序列化引用 ----------

        private static void Set(Object target, string propertyName, Object value)
        {
            var serialized = new SerializedObject(target);
            Find(serialized, propertyName).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Set(Object target, string propertyName, float value)
        {
            var serialized = new SerializedObject(target);
            Find(serialized, propertyName).floatValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Set(Object target, string propertyName, string value)
        {
            var serialized = new SerializedObject(target);
            Find(serialized, propertyName).stringValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Set(Object target, string propertyName, Vector3 value)
        {
            var serialized = new SerializedObject(target);
            Find(serialized, propertyName).vector3Value = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetArray(Object target, string propertyName, Object[] values)
        {
            var serialized = new SerializedObject(target);
            var property = Find(serialized, propertyName);
            property.arraySize = values.Length;
            for (var i = 0; i < values.Length; i++)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static SerializedProperty Find(SerializedObject serialized, string propertyName)
        {
            var property = serialized.FindProperty(propertyName);
            if (property == null)
            {
                throw new System.InvalidOperationException(
                    $"[WildernessSliceSetup] {serialized.targetObject.GetType().Name} 缺少序列化字段 '{propertyName}'。");
            }

            return property;
        }
    }
}
#endif
