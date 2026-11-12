using System;
using System.Collections.Generic;
using System.Linq;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using WildernessExtraction.Map01;
using WildernessExtraction.Map01.EditorTools;
using Object = UnityEngine.Object;

namespace WildernessExtraction.Tutorial.EditorTools
{
    /// <summary>
    /// 生成开局教学关《最后一段路》白盒场景：一条蛇形废弃车道分 8 段，每段末尾一道阻隔，完成后点亮下一盏引路灯。
    /// 玩法沿用 Map01 的权威层、背包、合成、HUD 和玩家预制体，只换一张空地图（没有随机物资和怪物点位）。
    /// 配置资产（教学文案、教学配方、教学怪物数值）只在缺失时创建，重建场景不会覆盖策划改过的数值。
    /// 可由菜单、UnityMCP 或命令行 -executeMethod WildernessExtraction.Tutorial.EditorTools.TutorialBuilder.Build 调用。
    /// </summary>
    public static class TutorialBuilder
    {
        public const string RootFolder = "Assets/Tutorial";
        public const string ScenePath = GameScenes.Tutorial;
        public const string ConfigPath = DataFolder + "/TutorialConfig.asset";

        private const string DataFolder = RootFolder + "/Data";
        private const string ScenesFolder = RootFolder + "/Scenes";
        private const string MaterialsFolder = RootFolder + "/Materials";
        private const string NavMeshPath = DataFolder + "/Tutorial_NavMesh.asset";
        private const string MapDefinitionPath = DataFolder + "/Tutorial_MapDefinition.asset";
        private const string RecipesPath = DataFolder + "/Tutorial_Recipes.asset";
        private const string PinnedEnemyConfigPath = DataFolder + "/Tutorial_EnemyPinned.asset";
        private const string PatrolEnemyConfigPath = DataFolder + "/Tutorial_EnemyPatrol.asset";
        private const string ItemsFolder = "Assets/Game/Data/Items";
        private const string LampPrefabPath = "Assets/Game/Prefabs/SafeLamp.prefab";
        private const string ContainerPrefabPath = "Assets/Game/Prefabs/LootContainer.prefab";
        private const string EnemyPrefabPath = "Assets/Game/Prefabs/MeleeEnemy_Firelost.prefab";
        private const string BaseEnemyConfigPath = "Assets/Game/Data/EnemyConfig.asset";

        private const float WallHeight = 1.5f;
        private const float WallThickness = 0.4f;

        // 车道（XZ，米）：上排沿 +X，拐角向南，下排沿 −X 回到西端的庇护所门。
        private static readonly Rect RowA = Rect.MinMaxRect(-4f, -4f, 70f, 4f);
        private static readonly Rect Turn = Rect.MinMaxRect(62f, -28f, 70f, 4f);
        private static readonly Rect RowB = Rect.MinMaxRect(-4f, -28f, 70f, -20f);

        public static readonly Vector3 Spawn = new Vector3(1f, 0f, 0f);
        public static readonly Vector3 ExitDoor = new Vector3(1f, 0f, -24f);
        /// <summary>最后那间屋子的外墙（可炸开）与墙后通往地下安全屋的台阶口。</summary>
        public static readonly Vector3 BreachPoint = new Vector3(-4f, 0f, -24f);
        public static readonly Vector3 StairsEntry = new Vector3(-8.5f, 0f, -24f);

        private sealed class Palette
        {
            public Material Ground, Wall, Barrier, Wagon, Target, Door, Ember, Rock, Cracked, Stair;
        }

        [MenuItem("Wilderness/Tutorial/Build Tutorial")]
        private static void BuildFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build();
        }

        [MenuItem("Wilderness/Tutorial/Open Tutorial")]
        private static void OpenFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null) Build();
            else EditorSceneManager.OpenScene(ScenePath);
        }

        public static void Build()
        {
            EnsureFolder(RootFolder);
            EnsureFolder(DataFolder);
            EnsureFolder(ScenesFolder);
            EnsureFolder(MaterialsFolder);

            var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Map01Builder.PlayerPrefabPath)
                               ?? throw new InvalidOperationException($"缺少 {Map01Builder.PlayerPrefabPath}");
            // 新建场景会卸载未被引用的资产，配置资产要在新场景建好之后再取。
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var config = Config();
            var recipes = Recipes();
            var palette = CreatePalette();
            CreateLighting();
            var root = new GameObject("Tutorial_Map").transform;
            var level = Group(root, "Level");
            CreateGround(level, palette);
            CreateWalls(level, palette);
            var wagon = CreateWagon(level, palette);
            CreateStairsAlcove(level, palette);
            var breachPiece = Cube(level, "OuterWall_Cracked (可炸开)", BreachPoint + new Vector3(0f, WallHeight * 0.5f, 0f),
                new Vector3(WallThickness + 0.2f, WallHeight, 3f), palette.Cracked, true);
            BakeNavMesh(root.gameObject);

            // 阻隔在烘焙之后放：它们会被移开，用 NavMeshObstacle 临时挖掉导航面。
            var barriers = Group(root, "Barriers");
            var gateMove = Barrier(barriers, palette, "Gate_1_Move", new Vector3(9f, 0f, 0f), true);
            var gateLoot = Barrier(barriers, palette, "Gate_3_Loot", new Vector3(34f, 0f, 0f), true);
            var gateCraft = Barrier(barriers, palette, "Gate_4_Craft", new Vector3(42f, 0f, 0f), true);
            var gateMelee = Barrier(barriers, palette, "Gate_6_Melee", new Vector3(66f, 0f, -17f), false);
            var gateShoot = Barrier(barriers, palette, "Gate_7_Shoot", new Vector3(48f, 0f, -24f), true);

            var items = Group(root, "Items");
            var cloth = Item("Cloth");
            var chemicals = Item("Chemicals");
            var scrap = Item("ScrapMetal");
            var fuel = Item("FuelPack");
            var pistol = Item("OldPistol");
            var ammo = Item("Ammo9mm");
            var knife = Item("ShortKnife");
            var explosive = ExplosiveItem();
            Crate(items, "Crate_Cloth", "crate_cloth", "旧货箱", new Vector3(26f, 0f, 2.6f), cloth, cloth, scrap);
            Crate(items, "Crate_Chemicals", "crate_chemicals", "工具箱", new Vector3(30f, 0f, 2.6f), chemicals);
            var relic = Crate(items, "Crate_SeekerRelic", "relic_searched", "寻火者的遗物", new Vector3(68.2f, 0f, -21.5f),
                Enumerable.Repeat(pistol, 1).Concat(Enumerable.Repeat(ammo, 8)).ToArray());
            Crate(items, "Crate_Explosive", "crate_explosive", "炸药箱", new Vector3(5f, 0f, -26.2f), explosive);

            var lights = Group(root, "Beacons");
            var startLamp = Lamp(lights, "Lamp_0_Start", new Vector3(2f, 0f, 0f), 6f);
            Beacon(startLamp, string.Empty, null, null, null);
            Beacon(Lamp(lights, "Lamp_1", new Vector3(12f, 0f, 0f), 4f), "move", new[] { gateMove }, null, null);
            Beacon(Lamp(lights, "Lamp_2", new Vector3(28f, 0f, 0f), 4.5f), "light", null, null, null);
            Beacon(Lamp(lights, "Lamp_3", new Vector3(38f, 0f, 0f), 4.5f), "loot", new[] { gateLoot }, null, null);
            var craftPoint = new GameObject("Checkpoint_Craft").transform;
            craftPoint.SetParent(lights, false);
            craftPoint.position = new Vector3(40f, 0f, 0f);
            craftPoint.gameObject.AddComponent<TutorialBeacon>().Configure("craft", null, new[] { gateCraft }, null, null);
            // 长暗段中间的灯：火堆点起来时被火光引燃，接上对岸的灯，整段路都有光。
            Beacon(Lamp(lights, "Lamp_4_Middle", new Vector3(54f, 0f, 0f), 6f), string.Empty, null, null, null, TutorialFacts.CampfireBuilt);
            Beacon(Lamp(lights, "Lamp_5_FarSide", new Vector3(66f, 0f, -5f), 8f), string.Empty, null, null, null, TutorialFacts.CampfireBuilt);
            Beacon(Lamp(lights, "Lamp_6_Corner", new Vector3(66f, 0f, -24f), 5f), "melee", new[] { gateMelee }, new[] { relic.gameObject }, null);
            var lampHome = Lamp(lights, "Lamp_7_Range", new Vector3(42f, 0f, -24f), 5f);
            Beacon(lampHome, "shoot", new[] { gateShoot }, null, null);
            Beacon(Lamp(lights, "Lamp_8_Midway", new Vector3(24f, 0f, -24f), 5f), "shoot", null, null, lampHome.transform);
            Beacon(Lamp(lights, "Lamp_9_LastRoom", ExitDoor + new Vector3(1f, 0f, 0f), 6f, 5f), "shoot", null, null, lampHome.transform);
            // 墙后台阶口透上来的光：墙炸开时亮起。
            Beacon(Lamp(lights, "Lamp_10_Stairs", StairsEntry + new Vector3(1.5f, 0f, -1.4f), 3.5f, 3f), string.Empty, null, null, null, TutorialFacts.WallBlown);

            var zones = Group(root, "Zones");
            // 区域都要横跨整条车道（车道宽 8 米），玩家贴边走也不会漏踩而卡关。
            Zone(zones, "Zone_Lamp2", "reached_lamp_2", new Vector3(28f, 0f, 0f), 4.5f);
            Zone(zones, "Zone_FarSide", "reached_far_lamp", new Vector3(66f, 0f, -2f), 5f);
            Zone(zones, "Zone_LastRoom", TutorialFacts.AtExit, ExitDoor + new Vector3(2f, 0f, 0f), 4.2f);
            // 墙洞口：走到这里由过场接管，角色自己走下台阶。
            Zone(zones, "Zone_Stairs", TutorialFacts.StairsReached, BreachPoint + new Vector3(-2f, 0f, 0f), 2f);

            var targets = Group(root, "Range");
            foreach (var z in new[] { -22f, -24f, -26f })
            {
                Target(targets, palette, $"OilCan_{z:0}", new Vector3(52f, 1.3f, z));
            }

            var rangeGlow = new GameObject("Range_DecorLight (不是安全光)");
            rangeGlow.transform.SetParent(targets, false);
            rangeGlow.transform.position = new Vector3(53f, 2.6f, -24f);
            var glow = rangeGlow.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.color = new Color(1f, 0.6f, 0.3f);
            glow.range = 5f;
            glow.intensity = 1.6f;

            var enemies = Group(root, "Enemies");
            var pinned = Enemy(enemies, "Firelost_Pinned", wagon.position, PinnedEnemyConfig(), "pinned_killed", true, false, Quaternion.Euler(0f, 0f, 0f));
            pinned.transform.rotation = Quaternion.LookRotation(Vector3.forward);
            var patrolA = Enemy(enemies, "Firelost_PatrolA", new Vector3(33f, 0f, -24f), PatrolEnemyConfig(), "patrol_a_killed", false, true, Quaternion.identity);
            Route(patrolA, new Vector3(35f, 0f, -22.5f), new Vector3(30f, 0f, -25.5f));
            var patrolB = Enemy(enemies, "Firelost_PatrolB", new Vector3(13f, 0f, -24f), PatrolEnemyConfig(), "patrol_b_killed", false, true, Quaternion.identity);
            Route(patrolB, new Vector3(17f, 0f, -26f), new Vector3(12f, 0f, -22f));

            // 走到庇护所门口时身后落石，来路被堵死。
            CreateRockfall(root, palette);
            CreateBreach(breachPiece, explosive, palette);

            var player = CreatePlayerAndCamera(playerPrefab, Spawn, scene, knife);
            CreateGame(root, player, config, recipes, fuel, pistol);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new InvalidOperationException($"保存场景失败：{ScenePath}");
            RegisterInBuildSettings();
            AssetDatabase.SaveAssets();
            Debug.Log($"[Tutorial] 场景已生成：{ScenePath}");
        }

        // ---------- 玩法对象 ----------

        private static void CreateGame(Transform root, GameObject player, TutorialConfig config, CraftingRecipeBook recipes, ItemDefinition fuel, ItemDefinition pistol)
        {
            var game = new GameObject("Tutorial_Game");
            var authority = game.AddComponent<Map01Authority>();
            var serialized = new SerializedObject(authority);
            serialized.FindProperty("map").objectReferenceValue = EmptyMap();
            serialized.FindProperty("tables").objectReferenceValue = AssetDatabase.LoadAssetAtPath<ContentTables>(Map01Content.ContentTablesPath);
            serialized.FindProperty("player").objectReferenceValue = player.GetComponent<PlayerCharacter>();
            serialized.FindProperty("containerPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<LootContainer>(ContainerPrefabPath);
            serialized.FindProperty("recipes").objectReferenceValue = recipes;
            serialized.FindProperty("questMode").boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            var interactor = player.AddComponent<Map01Interactor>();
            var interactorSerialized = new SerializedObject(interactor);
            interactorSerialized.FindProperty("authority").objectReferenceValue = authority;
            interactorSerialized.ApplyModifiedPropertiesWithoutUndo();

            var flow = game.AddComponent<Map01RunFlow>();
            Assign(flow, "authority", authority);

            var hud = game.AddComponent<Map01Hud>();
            var hudSerialized = new SerializedObject(hud);
            hudSerialized.FindProperty("authority").objectReferenceValue = authority;
            hudSerialized.FindProperty("interactor").objectReferenceValue = interactor;
            hudSerialized.FindProperty("tutorialLayout").boolValue = true;
            hudSerialized.ApplyModifiedPropertiesWithoutUndo();

            var crafting = game.AddComponent<Map01CraftingPanel>();
            var craftingSerialized = new SerializedObject(crafting);
            craftingSerialized.FindProperty("authority").objectReferenceValue = authority;
            craftingSerialized.FindProperty("flow").objectReferenceValue = flow;
            craftingSerialized.FindProperty("interactor").objectReferenceValue = interactor;
            craftingSerialized.ApplyModifiedPropertiesWithoutUndo();

            var director = game.AddComponent<TutorialDirector>();
            var directorSerialized = new SerializedObject(director);
            directorSerialized.FindProperty("config").objectReferenceValue = config;
            directorSerialized.FindProperty("authority").objectReferenceValue = authority;
            directorSerialized.FindProperty("flow").objectReferenceValue = flow;
            directorSerialized.FindProperty("fuelItem").objectReferenceValue = fuel;
            directorSerialized.FindProperty("pistolItem").objectReferenceValue = pistol;
            directorSerialized.FindProperty("recipes").objectReferenceValue = recipes;
            // 下台阶过场：洞口 → 台阶顶 → 沿台阶往西往下，没入地面以下。
            var descent = directorSerialized.FindProperty("descentPath");
            var points = new[] { BreachPoint + new Vector3(-1.4f, 0f, 0f), StairsEntry, StairsEntry + new Vector3(-2.2f, -1.8f, 0f) };
            descent.arraySize = points.Length;
            for (var i = 0; i < points.Length; i++) descent.GetArrayElementAtIndex(i).vector3Value = points[i];
            directorSerialized.FindProperty("cutsceneCameraPosition").vector3Value = StairsEntry + new Vector3(4.5f, 14f, -9f);
            directorSerialized.FindProperty("cutsceneCameraEuler").vector3Value = new Vector3(53.6f, -29f, 0f);
            directorSerialized.ApplyModifiedPropertiesWithoutUndo();

            var tutorialHud = game.AddComponent<TutorialHud>();
            var tutorialHudSerialized = new SerializedObject(tutorialHud);
            tutorialHudSerialized.FindProperty("director").objectReferenceValue = director;
            tutorialHudSerialized.FindProperty("flow").objectReferenceValue = flow;
            tutorialHudSerialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static GameObject CreatePlayerAndCamera(GameObject prefab, Vector3 spawn, UnityEngine.SceneManagement.Scene scene, ItemDefinition knife)
        {
            var player = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            player.transform.SetPositionAndRotation(spawn, Quaternion.LookRotation(Vector3.right));
            var exposure = player.GetComponent<LightExposure>();
            if (exposure != null)
            {
                exposure.enabled = true;
                Assign(exposure, "config", Map01Lighting.DarknessConfig());
                PrefabUtility.RecordPrefabInstancePropertyModifications(exposure);
            }

            // 教学开局只有一把短刀：没有燃料（要自己合成）、没有手枪（射击场前拾取）。
            var inventory = player.GetComponent<PlayerInventory>();
            var inventorySerialized = new SerializedObject(inventory);
            var starting = inventorySerialized.FindProperty("startingItems");
            starting.arraySize = 1;
            starting.GetArrayElementAtIndex(0).objectReferenceValue = knife;
            inventorySerialized.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.RecordPrefabInstancePropertyModifications(inventory);

            player.AddComponent<RevealViewer>();

            var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            var camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = FixedObliqueCamera.DefaultFieldOfView;
            camera.nearClipPlane = FixedObliqueCamera.DefaultNearClip;
            camera.farClipPlane = 200f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.02f, 0.02f, 0.03f);
            cameraObject.AddComponent<AudioListener>();
            var rig = cameraObject.AddComponent<FixedObliqueCamera>();
            rig.Target = player.transform;
            cameraObject.transform.SetPositionAndRotation(spawn + Vector3.up * FixedObliqueCamera.DefaultFollowHeight + FixedObliqueCamera.DefaultOffset,
                Quaternion.Euler(FixedObliqueCamera.DefaultEulerAngles));
            return player;
        }

        private static SafeLightSource Lamp(Transform parent, string name, Vector3 position, float radius, float intensity = 4f)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(LampPrefabPath);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            instance.name = name;
            instance.transform.position = position;
            var source = instance.GetComponent<SafeLightSource>();
            var serialized = new SerializedObject(source);
            serialized.FindProperty("config").objectReferenceValue = Map01Lighting.DarknessConfig();
            serialized.FindProperty("kind").enumValueIndex = (int)SafeLightKind.FixedLamp;
            serialized.FindProperty("radiusOverride").floatValue = radius;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            var light = instance.GetComponentInChildren<Light>();
            if (light != null) light.intensity = intensity;
            source.RefreshVisuals();
            foreach (var component in instance.GetComponentsInChildren<Component>())
            {
                PrefabUtility.RecordPrefabInstancePropertyModifications(component);
            }

            return source;
        }

        private static void Beacon(SafeLightSource lamp, string afterStep, GameObject[] hide, GameObject[] show, Transform respawn, string afterFact = null)
        {
            lamp.gameObject.AddComponent<TutorialBeacon>().Configure(afterStep, lamp, hide, show, respawn, afterFact);
        }

        private static LootContainer Crate(Transform parent, string name, string fact, string displayName, Vector3 position, params ItemDefinition[] contents)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<LootContainer>(ContainerPrefabPath);
            var container = (LootContainer)PrefabUtility.InstantiatePrefab(prefab, parent);
            container.name = name;
            container.transform.position = position;
            container.gameObject.AddComponent<TutorialCrate>().Configure(fact, displayName, 1.2f, contents);
            return container;
        }

        private static MeleeEnemy Enemy(Transform parent, string name, Vector3 position, EnemyConfig config, string killFact, bool pinned, bool easeable, Quaternion rotation)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<MeleeEnemy>(EnemyPrefabPath);
            var enemy = (MeleeEnemy)PrefabUtility.InstantiatePrefab(prefab, parent);
            enemy.name = name;
            enemy.transform.SetPositionAndRotation(position, rotation);
            Assign(enemy, "config", config);
            PrefabUtility.RecordPrefabInstancePropertyModifications(enemy);
            enemy.gameObject.AddComponent<TutorialEnemy>().Configure(killFact, pinned, easeable);
            return enemy;
        }

        private static void Route(MeleeEnemy enemy, params Vector3[] points)
        {
            var route = new GameObject($"{enemy.name}_Route").transform;
            route.SetParent(enemy.transform.parent, false);
            var nodes = new List<Transform>();
            for (var i = 0; i < points.Length; i++)
            {
                var node = new GameObject($"Patrol_{i}").transform;
                node.SetParent(route, false);
                node.position = NavMesh.SamplePosition(points[i], out var hit, 2f, NavMesh.AllAreas) ? hit.position : points[i];
                nodes.Add(node);
            }

            var serialized = new SerializedObject(enemy);
            var property = serialized.FindProperty("patrolPoints");
            property.arraySize = nodes.Count;
            for (var i = 0; i < nodes.Count; i++) property.GetArrayElementAtIndex(i).objectReferenceValue = nodes[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.RecordPrefabInstancePropertyModifications(enemy);
        }

        private static void Zone(Transform parent, string name, string fact, Vector3 position, float radius)
        {
            var zone = new GameObject(name);
            zone.transform.SetParent(parent, false);
            zone.transform.position = position;
            zone.AddComponent<TutorialZone>().Configure(fact, radius);
        }

        private static void Target(Transform parent, Palette palette, string name, Vector3 position)
        {
            var rope = Cube(parent, $"{name}_Rope", position + new Vector3(0f, 0.85f, 0f), new Vector3(0.04f, 1.2f, 0.04f), palette.Wall, false);
            rope.name = $"{name}_Rope";
            var can = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            can.name = name;
            can.transform.SetParent(parent, false);
            can.transform.position = position;
            can.transform.localScale = new Vector3(0.5f, 0.3f, 0.5f);
            can.GetComponent<Renderer>().sharedMaterial = palette.Target;
            can.AddComponent<Health>();
            can.AddComponent<TutorialTarget>();
        }

        // ---------- 场景几何 ----------

        private static void CreateLighting()
        {
            var sunObject = new GameObject("Directional Light (Moon)");
            var sun = sunObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(0.55f, 0.62f, 0.85f);
            sun.intensity = 0.08f;
            sun.shadows = LightShadows.None;
            sunObject.transform.rotation = Quaternion.Euler(50f, -35f, 0f);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.05f, 0.055f, 0.075f);
        }

        private static void CreateGround(Transform parent, Palette palette)
        {
            var bounds = Rect.MinMaxRect(-12f, -30f, 72f, 6f);
            Cube(parent, "Ground", new Vector3(bounds.center.x, -0.5f, bounds.center.y), new Vector3(bounds.width, 1f, bounds.height), palette.Ground, true);
        }

        private static void CreateWalls(Transform parent, Palette palette)
        {
            var walls = Group(parent, "Walls");
            // 上排
            Wall(walls, palette, "North_A", new Vector2(RowA.xMin, RowA.yMax), new Vector2(RowA.xMax, RowA.yMax));
            Wall(walls, palette, "West_A", new Vector2(RowA.xMin, RowA.yMin), new Vector2(RowA.xMin, RowA.yMax));
            Wall(walls, palette, "South_A", new Vector2(RowA.xMin, RowA.yMin), new Vector2(Turn.xMin, RowA.yMin));
            // 拐角
            Wall(walls, palette, "East_Turn", new Vector2(Turn.xMax, Turn.yMin), new Vector2(Turn.xMax, Turn.yMax));
            Wall(walls, palette, "West_Turn", new Vector2(Turn.xMin, RowB.yMax), new Vector2(Turn.xMin, RowA.yMin));
            // 下排
            Wall(walls, palette, "North_B", new Vector2(RowB.xMin, RowB.yMax), new Vector2(Turn.xMin, RowB.yMax));
            Wall(walls, palette, "South_B", new Vector2(RowB.xMin, RowB.yMin), new Vector2(RowB.xMax, RowB.yMin));
            Wall(walls, palette, "West_B_South", new Vector2(RowB.xMin, RowB.yMin), new Vector2(RowB.xMin, BreachPoint.z - 1.5f));
            Wall(walls, palette, "West_B_North", new Vector2(RowB.xMin, BreachPoint.z + 1.5f), new Vector2(RowB.xMin, RowB.yMax));
        }

        /// <summary>卡住失火者的废车厢：两侧和背后挡住，只留朝北的开口。</summary>
        private static Transform CreateWagon(Transform parent, Palette palette)
        {
            var wagon = Group(parent, "Wagon");
            wagon.position = new Vector3(66f, 0f, -13f);
            Cube(wagon, "Wagon_Left", wagon.position + new Vector3(-1.6f, 0.9f, -0.6f), new Vector3(0.4f, 1.8f, 3f), palette.Wagon, true);
            Cube(wagon, "Wagon_Right", wagon.position + new Vector3(1.6f, 0.9f, -0.6f), new Vector3(0.4f, 1.8f, 3f), palette.Wagon, true);
            Cube(wagon, "Wagon_Back", wagon.position + new Vector3(0f, 0.9f, -2f), new Vector3(3.6f, 1.8f, 0.4f), palette.Wagon, true);
            return wagon;
        }

        /// <summary>外墙后面的小间：一段往下的台阶，通向地下安全屋。</summary>
        private static void CreateStairsAlcove(Transform parent, Palette palette)
        {
            var alcove = Group(parent, "StairsAlcove");
            const float west = -11f;
            Wall(alcove, palette, "Alcove_North", new Vector2(west, BreachPoint.z + 2f), new Vector2(BreachPoint.x, BreachPoint.z + 2f));
            Wall(alcove, palette, "Alcove_South", new Vector2(west, BreachPoint.z - 2f), new Vector2(BreachPoint.x, BreachPoint.z - 2f));
            Wall(alcove, palette, "Alcove_West", new Vector2(west, BreachPoint.z - 2f), new Vector2(west, BreachPoint.z + 2f));
            // 台阶只做地面上的一级级暗板（视觉），往西越来越暗，像是往下走。
            for (var i = 0; i < 4; i++)
            {
                var shade = Color.Lerp(new Color(0.3f, 0.26f, 0.22f), new Color(0.04f, 0.035f, 0.03f), i / 3f);
                var step = Cube(alcove, $"Stair_{i}", new Vector3(StairsEntry.x - 0.4f - i * 0.5f, 0.02f, BreachPoint.z), new Vector3(0.5f, 0.04f, 2.6f), palette.Stair, false);
                step.GetComponent<Renderer>().sharedMaterial = Material($"M_Tutorial_Stair{i}", shade);
            }
        }

        private static void CreateBreach(GameObject wallPiece, ItemDefinition explosive, Palette palette)
        {
            var breach = new GameObject("BreachWall");
            breach.transform.SetParent(wallPiece.transform.parent, false);
            breach.transform.position = BreachPoint;
            var charge = Cube(breach.transform, "Charge", BreachPoint + new Vector3(0.45f, 0.5f, 0f), new Vector3(0.25f, 0.35f, 0.5f), palette.Barrier, false);
            Cube(charge.transform, "Charge_Fuse", BreachPoint + new Vector3(0.6f, 0.75f, 0f), new Vector3(0.05f, 0.2f, 0.05f), palette.Ember, false);
            breach.AddComponent<TutorialBreachWall>().Configure(explosive, new[] { wallPiece }, charge, palette.Cracked);
        }

        private static void CreateRockfall(Transform parent, Palette palette)
        {
            var rockfall = new GameObject("Rockfall_BehindDoor");
            rockfall.transform.SetParent(parent, false);
            rockfall.transform.position = new Vector3(9f, 0f, -24f);
            var blocker = rockfall.AddComponent<BoxCollider>();
            blocker.center = new Vector3(0f, 1.5f, 0f);
            blocker.size = new Vector3(2f, 3f, 8f);
            var obstacle = rockfall.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.center = blocker.center;
            obstacle.size = blocker.size;
            obstacle.carving = true;
            var rocks = new[]
            {
                (new Vector3(0f, 0.7f, -3f), new Vector3(1.8f, 1.4f, 1.9f), 12f),
                (new Vector3(0.2f, 0.9f, -1.2f), new Vector3(2.1f, 1.8f, 1.8f), -20f),
                (new Vector3(-0.2f, 1.1f, 0.6f), new Vector3(2.2f, 2.2f, 2f), 35f),
                (new Vector3(0.1f, 0.8f, 2.3f), new Vector3(1.9f, 1.6f, 1.8f), -8f),
                (new Vector3(0f, 0.6f, 3.3f), new Vector3(1.6f, 1.2f, 1.4f), 25f),
                (new Vector3(0f, 2.3f, -0.4f), new Vector3(1.4f, 1.2f, 1.6f), 50f)
            };
            for (var i = 0; i < rocks.Length; i++)
            {
                var rock = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                rock.name = $"Rock_{i}";
                rock.transform.SetParent(rockfall.transform, false);
                rock.transform.localPosition = rocks[i].Item1;
                rock.transform.localScale = rocks[i].Item2;
                rock.transform.localRotation = Quaternion.Euler(rocks[i].Item3, rocks[i].Item3 * 2f, 0f);
                rock.GetComponent<Renderer>().sharedMaterial = palette.Rock;
                Object.DestroyImmediate(rock.GetComponent<Collider>());
            }

            var safeSide = new GameObject("Rockfall_SafeSide").transform;
            safeSide.SetParent(rockfall.transform, false);
            safeSide.position = ExitDoor + new Vector3(3f, 0f, 0f);
            rockfall.AddComponent<TutorialRockfall>().Configure(safeSide);
        }

        private static GameObject Barrier(Transform parent, Palette palette, string name, Vector3 center, bool acrossX)
        {
            var size = acrossX ? new Vector3(0.5f, 1.4f, 8f) : new Vector3(8f, 1.4f, 0.5f);
            var barrier = Cube(parent, name, center + new Vector3(0f, 0.7f, 0f), size, palette.Barrier, true);
            var obstacle = barrier.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.size = Vector3.one;
            obstacle.carving = true;
            return barrier;
        }

        private static void Wall(Transform parent, Palette palette, string name, Vector2 from, Vector2 to)
        {
            var center = (from + to) * 0.5f;
            var length = Vector2.Distance(from, to) + WallThickness;
            var horizontal = Mathf.Abs(to.x - from.x) > Mathf.Abs(to.y - from.y);
            var size = horizontal ? new Vector3(length, WallHeight, WallThickness) : new Vector3(WallThickness, WallHeight, length);
            Cube(parent, name, new Vector3(center.x, WallHeight * 0.5f, center.y), size, palette.Wall, true);
        }

        private static GameObject Cube(Transform parent, string name, Vector3 position, Vector3 size, Material material, bool collider)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent, false);
            cube.transform.position = position;
            cube.transform.localScale = size;
            cube.GetComponent<Renderer>().sharedMaterial = material;
            if (!collider) Object.DestroyImmediate(cube.GetComponent<Collider>());
            return cube;
        }

        private static void BakeNavMesh(GameObject root)
        {
            var surface = root.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.BuildNavMesh();
            if (surface.navMeshData == null) throw new InvalidOperationException("教学关 NavMesh 烘焙失败。");
            if (AssetDatabase.LoadAssetAtPath<Object>(NavMeshPath) != null) AssetDatabase.DeleteAsset(NavMeshPath);
            AssetDatabase.CreateAsset(surface.navMeshData, NavMeshPath);
        }

        private static Transform Group(Transform parent, string name)
        {
            var group = new GameObject(name).transform;
            group.SetParent(parent, false);
            return group;
        }

        // ---------- 资产（只在缺失时创建） ----------

        private static MapDefinition EmptyMap()
        {
            var map = AssetDatabase.LoadAssetAtPath<MapDefinition>(MapDefinitionPath);
            if (map != null) return map;
            map = ScriptableObject.CreateInstance<MapDefinition>();
            map.mapId = "tutorial";
            map.version = 1;
            map.worldSize = new Vector2(80f, 40f);
            map.safehouseId = "tutorial_shelter";
            // 权威层要求一局有一个撤离候选点；教学里它永远不会激活（没有电量可交），只为满足规则。
            map.points.Add(new SpawnPointData { pointId = "tutorial_exit", type = SpawnPointType.ExtractionCandidate, position = ExitDoor, radius = 2.5f });
            AssetDatabase.CreateAsset(map, MapDefinitionPath);
            return map;
        }

        /// <summary>教学只教一条配方：燃料包，开局就能做（不挂任务解锁）。</summary>
        public static CraftingRecipeBook Recipes()
        {
            var book = AssetDatabase.LoadAssetAtPath<CraftingRecipeBook>(RecipesPath);
            if (book != null) return book;
            var basic = Map01CraftingSetup.BasicRecipes().recipes.Find(r => r.recipeId == "fuel_pack");
            book = ScriptableObject.CreateInstance<CraftingRecipeBook>();
            book.recipes = new List<CraftingRecipe>
            {
                new CraftingRecipe
                {
                    recipeId = "fuel_pack",
                    category = "火",
                    unlockKey = string.Empty,
                    description = basic != null ? basic.description : "布料浸透化学品，卷紧压实，能烧很久。",
                    ingredients = new[] { new CraftingIngredient(Item("Cloth"), 2), new CraftingIngredient(Item("Chemicals"), 1) },
                    result = Item("FuelPack"),
                    resultAmount = 1
                }
            };
            AssetDatabase.CreateAsset(book, RecipesPath);
            return book;
        }

        /// <summary>卡在车厢里的失火者：不移动，抬爪更慢（方便练翻滚），伤害减半。</summary>
        private static EnemyConfig PinnedEnemyConfig() => EnemyVariant(PinnedEnemyConfigPath, c =>
        {
            c.moveSpeed = 0f;
            c.damage = 10f;
            c.attackWindup = 0.8f;
            c.attackInterval = 1.8f;
            c.maxHealth = 56f;
        });

        /// <summary>最后一段的巡逻失火者：稍慢、伤害略低，单次伤害须小于教学的倒下阈值。</summary>
        private static EnemyConfig PatrolEnemyConfig() => EnemyVariant(PatrolEnemyConfigPath, c =>
        {
            c.moveSpeed = 3f;
            c.damage = 12f;
        });

        private static EnemyConfig EnemyVariant(string path, Action<EnemyConfig> tune)
        {
            var config = AssetDatabase.LoadAssetAtPath<EnemyConfig>(path);
            if (config != null) return config;
            var source = AssetDatabase.LoadAssetAtPath<EnemyConfig>(BaseEnemyConfigPath);
            config = source != null ? Object.Instantiate(source) : ScriptableObject.CreateInstance<EnemyConfig>();
            tune(config);
            AssetDatabase.CreateAsset(config, path);
            return config;
        }

        public static TutorialConfig Config()
        {
            var config = AssetDatabase.LoadAssetAtPath<TutorialConfig>(ConfigPath);
            if (config != null) return config;
            config = ScriptableObject.CreateInstance<TutorialConfig>();
            config.steps = new[]
            {
                Step("move", "余烬", new[] { TutorialFacts.Moved, TutorialFacts.Sprinted },
                    ("", "WASD 移动，按住 Shift 快跑。"),
                    (TutorialFacts.Moved, "按住 Shift 快跑，前面还有路。")),
                Step("light", "灯与暗", new[] { "reached_lamp_2" },
                    ("", "光里安全。走出光圈，黑暗会开始伤害你。\n前面那盏灯还没点，走过去点亮它。"),
                    (TutorialFacts.FeltWarning, "离光了！回到光里，或者赶快走到下一盏灯。")),
                Step("loot", "翻货箱", new[] { "crate_cloth", "crate_chemicals", TutorialFacts.BackpackOpened },
                    ("", "灯下有两只箱子。靠近按 F 搜索，两只都搜一搜。"),
                    ("crate_cloth", "两只都搜一搜，再按 Tab 看看背包。")),
                Step("craft", "拼一份燃料", new[] { TutorialFacts.HasFuel },
                    ("", "前面那段路又长又黑。\n按 Tab 打开背包，切到合成页，做一份燃料包。")),
                Step("fire", "长暗段", new[] { TutorialFacts.CampfireBuilt, "reached_far_lamp" },
                    ("", "这盏灯坏了，前面这段路跑不过去。\n走到暗处边缘按 Q，选一块地生火。"),
                    (TutorialFacts.CampfirePreview, "左键确认生火，右键取消。生火时别乱动。"),
                    (TutorialFacts.CampfireBuilt, "火堆会烧完。趁它亮着，走到对面那盏灯。")),
                Step("melee", "卡住的失火者", new[] { TutorialFacts.Dodged, "pinned_killed" },
                    ("", "车厢里卡着一只失火者，只能向前挥爪。\n靠近一点，看它抬爪。"),
                    (TutorialFacts.EnemyAttacking, "它抬爪时按空格翻滚躲开，再按 V 用短刀反击。"),
                    (TutorialFacts.Dodged, "躲得好。按 V 用短刀砍它，直到它倒下。")),
                Step("shoot", "射击场", new[] { TutorialFacts.TargetsCleared, TutorialFacts.Reloaded },
                    ("", "它身后有个遗物包，按 F 搜一搜。"),
                    (TutorialFacts.PistolTaken, "按 1 拿出手枪。鼠标瞄准，左键打碎前面三个油罐。"),
                    (TutorialFacts.MagazineEmpty, "弹匣空了，按 R 换弹。"),
                    (TutorialFacts.Reloaded, "换好了，把剩下的油罐打碎。"),
                    (TutorialFacts.TargetsCleared, "油罐都碎了。按 R 换一次弹，别等打空才换。")),
                Step("home", "最后的屋子", new[] { TutorialFacts.AtExit },
                    ("", "尽头那间屋子的灯亮了。路上有失火者游荡，可以绕，也可以打。")),
                Step("blast", "炸开外墙", new[] { TutorialFacts.WallBlown, "entered_shelter" },
                    ("", "来路被落石堵死了。屋角有个炸药箱，按 F 搜一搜。"),
                    (TutorialFacts.ExplosiveTaken, "西边外墙有道裂缝。站到墙边按 F 安放炸药，然后退开。"),
                    (TutorialFacts.ChargePlaced, "引信点着了，快退开！"),
                    (TutorialFacts.WallBlown, "墙后有一段往下的台阶，还透着光。走下去。"))
            };
            AssetDatabase.CreateAsset(config, ConfigPath);
            return config;
        }

        private static TutorialStep Step(string id, string title, string[] facts, params (string after, string text)[] lines)
        {
            return new TutorialStep
            {
                stepId = id,
                title = title,
                requiredFacts = facts,
                lines = lines.Select(l => new TutorialLine { afterFact = l.after, text = l.text }).ToArray()
            };
        }

        /// <summary>教学专用的炸药：只用来炸开最后那面外墙，不会带进安全屋。</summary>
        private static ItemDefinition ExplosiveItem()
        {
            const string path = DataFolder + "/Item_Explosive.asset";
            var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
            if (item != null) return item;
            item = ScriptableObject.CreateInstance<ItemDefinition>();
            item.itemId = "tutorial_explosive";
            item.displayName = "炸药";
            item.category = ItemCategory.Consumable;
            item.size = 1;
            item.maxStack = 1;
            item.unitWeight = 1.5f;
            item.stashValue = 0;
            item.iconColor = new Color(0.85f, 0.3f, 0.2f);
            AssetDatabase.CreateAsset(item, path);
            return item;
        }

        private static ItemDefinition Item(string file)
        {
            return AssetDatabase.LoadAssetAtPath<ItemDefinition>($"{ItemsFolder}/Item_{file}.asset")
                   ?? throw new InvalidOperationException($"缺少物品 Item_{file}");
        }

        private static Palette CreatePalette()
        {
            return new Palette
            {
                Ground = Material("M_Tutorial_Ground", new Color(0.24f, 0.22f, 0.2f)),
                Wall = Material("M_Tutorial_Wall", new Color(0.46f, 0.45f, 0.43f)),
                Barrier = Material("M_Tutorial_Barrier", new Color(0.42f, 0.28f, 0.16f)),
                Wagon = Material("M_Tutorial_Wagon", new Color(0.36f, 0.38f, 0.42f)),
                Target = Material("M_Tutorial_OilCan", new Color(0.72f, 0.3f, 0.18f)),
                Door = Material("M_Tutorial_Door", new Color(0.75f, 0.52f, 0.25f)),
                Ember = Material("M_Tutorial_Ember", new Color(0.5f, 0.12f, 0.06f)),
                Rock = Material("M_Tutorial_Rock", new Color(0.38f, 0.35f, 0.32f)),
                Cracked = Material("M_Tutorial_CrackedWall", new Color(0.55f, 0.47f, 0.4f)),
                Stair = Material("M_Tutorial_Stair0", new Color(0.3f, 0.26f, 0.22f))
            };
        }

        private static Material Material(string name, Color color)
        {
            var path = $"{MaterialsFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = color };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static void Assign(Object target, string property, Object value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(property).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void RegisterInBuildSettings()
        {
            // 教学走完进入地下安全屋，打包时两个场景都要在列表里。
            var scenes = EditorBuildSettings.scenes.ToList();
            foreach (var path in new[] { ScenePath, GameScenes.Shelter })
            {
                if (!scenes.Any(s => s.path == path)) scenes.Add(new EditorBuildSettingsScene(path, true));
            }

            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var slash = path.LastIndexOf('/');
            AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
        }
    }
}
