using System;
using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace WildernessExtraction.EditorTools
{
    /// <summary>
    /// 一键生成首个可玩切片：配置资产、物品、掉落表、材质、预制体、灰盒场景、NavMesh 和 Build Settings。
    /// 配置/物品/掉落表/材质仅在不存在时创建（保留策划调整过的数值）；预制体、场景和 NavMesh 每次重建。
    /// </summary>
    public static class VerticalSliceBuilder
    {
        public const string RootFolder = "Assets/Game";
        public const string ScenePath = ScenesFolder + "/VerticalSlice.unity";

        private const string DataFolder = RootFolder + "/Data";
        private const string ItemsFolder = DataFolder + "/Items";
        private const string LootFolder = DataFolder + "/LootTables";
        private const string MaterialsFolder = RootFolder + "/Materials";
        private const string PrefabsFolder = RootFolder + "/Prefabs";
        private const string ScenesFolder = RootFolder + "/Scenes";
        private const string NavMeshPath = ScenesFolder + "/VerticalSlice_NavMesh.asset";

        private const float WallHeight = 2f;
        private const float CoverHeight = 1.2f;

        // 灰盒布局（30×30 米，X 向右、Z 向前/北）。
        private static readonly Vector3 SpawnPosition = new Vector3(-12f, 0f, -11f);
        private static readonly Vector3 ExtractionPosition = new Vector3(11.5f, 0f, 11.5f);
        private static readonly Vector3 EnemyPosition = new Vector3(2f, 0f, -2f);
        private static readonly Vector3 ContainerAPosition = new Vector3(-12f, 0f, 5f);
        private static readonly Vector3 ContainerBPosition = new Vector3(0f, 0f, 1f);
        private static readonly Vector3 ContainerCPosition = new Vector3(12.5f, 0f, -12.5f);
        private static readonly Vector3 MedicalPosition = new Vector3(-5f, 0f, 9f);
        private static readonly Vector3 SafePosition = new Vector3(9f, 0f, 7f);
        private static readonly Vector3 CorpsePosition = new Vector3(3f, 0f, -4f);

        private sealed class SliceAssets
        {
            public PlayerConfig Player;
            public WeaponConfig Weapon;
            public WeaponConfig Knife;
            public WeaponConfig Shotgun;
            public WeaponConfig Rifle;
            public ItemDefinition PistolItem;
            public ItemDefinition KnifeItem;
            public EnemyConfig Enemy;
            public RunRulesConfig Rules;
            public LootTable LootA;
            public LootTable LootB;
            public LootTable LootC;
            public LootTable LootMedical;
            public LootTable LootSafe;
            public LootTable LootCorpse;
            public InputActionAsset InputActions;
            public Material Ground;
            public Material Wall;
            public Material Cover;
            public Material PlayerBody;
            public Material EnemyBody;
            public Material Container;
            public Material Extraction;
            public Material Tracer;
            public GameObject PlayerPrefab;
            public GameObject EnemyPrefab;
            public GameObject ContainerPrefab;
            public GameObject ExtractionPrefab;
        }

        [MenuItem("Wilderness/Build Vertical Slice")]
        private static void BuildFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            Build();
        }

        [MenuItem("Wilderness/Open Vertical Slice")]
        private static void OpenFromMenu()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                Debug.LogWarning("[Wilderness] 场景不存在，请先运行 Wilderness/Build Vertical Slice。");
                return;
            }

            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                EditorSceneManager.OpenScene(ScenePath);
            }
        }

        /// <summary>可由菜单、UnityMCP 或命令行 -executeMethod 调用。</summary>
        public static void Build()
        {
            EnsureFolders();
            InputActionsSetup.EnsureSliceActions();

            var assets = new SliceAssets();
            CreateConfigs(assets);
            CreateItemsAndLoot(assets);
            CreateMaterials(assets);
            assets.InputActions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsSetup.AssetPath);

            assets.PlayerPrefab = BuildPlayerPrefab(assets);
            assets.EnemyPrefab = BuildEnemyPrefab(assets);
            assets.ContainerPrefab = BuildContainerPrefab(assets);
            assets.ExtractionPrefab = BuildExtractionPrefab(assets);

            BuildScene(assets);
            RegisterSceneInBuildSettings();
            AssetDatabase.SaveAssets();
            Debug.Log($"[Wilderness] 首个可玩切片已生成：{ScenePath}");
        }

        // ---------- 资产 ----------

        private static void EnsureFolders()
        {
            EnsureFolder(RootFolder);
            EnsureFolder(DataFolder);
            EnsureFolder(ItemsFolder);
            EnsureFolder(LootFolder);
            EnsureFolder(MaterialsFolder);
            EnsureFolder(PrefabsFolder);
            EnsureFolder(ScenesFolder);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            var slash = path.LastIndexOf('/');
            AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
        }

        private static void CreateConfigs(SliceAssets assets)
        {
            // 字段初值即策划表数值，因此只需在缺失时创建。
            assets.Player = LoadOrCreate<PlayerConfig>(DataFolder + "/PlayerConfig.asset");
            assets.Weapon = LoadOrCreate<WeaponConfig>(DataFolder + "/WeaponConfig.asset");
            assets.Knife = LoadOrCreate<WeaponConfig>(DataFolder + "/Weapon_Knife.asset");
            assets.Shotgun = LoadOrCreate<WeaponConfig>(DataFolder + "/Weapon_Shotgun.asset");
            assets.Rifle = LoadOrCreate<WeaponConfig>(DataFolder + "/Weapon_Rifle.asset");
            InitWeapon(assets.Weapon, "old_pistol", "旧手枪", WeaponKind.Firearm, AmmoCaliber.NineMillimeter, 25f, .25f, 8, 1.4f, 25f, 18f, .7f, 1, 2f, 4f, 14f);
            InitWeapon(assets.Shotgun, "pump_shotgun", "泵动霰弹枪", WeaponKind.Firearm, AmmoCaliber.TwelveGauge, 18f, .9f, 4, 2.2f, 18f, 9f, .35f, 6, 10f, 10f, 24f);
            InitWeapon(assets.Rifle, "semi_rifle", "半自动步枪", WeaponKind.Firearm, AmmoCaliber.FiveFiveSix, 42f, .45f, 10, 1.8f, 35f, 28f, .8f, 1, .8f, 2f, 20f);
            InitWeapon(assets.Knife, "short_knife", "短刀", WeaponKind.Melee, AmmoCaliber.None, 28f, .55f, 1, 0f, 1.6f, 1.6f, 1f, 1, 0f, 0f, 3f);
            assets.Knife.staminaCost = 12f;
            assets.Knife.meleeAngle = 80f;
            assets.Enemy = LoadOrCreate<EnemyConfig>(DataFolder + "/EnemyConfig.asset");
            assets.Rules = LoadOrCreate<RunRulesConfig>(DataFolder + "/RunRulesConfig.asset");
            assets.Rules.inventoryCapacity = 16;
            EditorUtility.SetDirty(assets.Rules);
        }

        private static void CreateItemsAndLoot(SliceAssets assets)
        {
            var scrap = MakeItem("ScrapMetal", "scrap_metal", "废金属", ItemCategory.Material, ItemRarity.Common, 5, .4f, 5, ItemTags.Part, new Color(.62f,.66f,.72f));
            var canned = MakeItem("CannedFood", "canned_food", "罐头", ItemCategory.Consumable, ItemRarity.Common, 2, .5f, 10, ItemTags.Food, new Color(.95f,.6f,.2f));
            var battery = MakeItem("OldBattery", "old_battery", "旧电池", ItemCategory.Material, ItemRarity.Uncommon, 2, .7f, 20, ItemTags.Part, new Color(.45f,.95f,.35f));
            var bandage = MakeItem("DirtyBandage", "dirty_bandage", "脏绷带", ItemCategory.Consumable, ItemRarity.Common, 3, .2f, 8, ItemTags.Medical, Color.white);
            var spray = MakeItem("FirstAidSpray", "first_aid_spray", "急救喷雾", ItemCategory.Consumable, ItemRarity.Uncommon, 1, .6f, 22, ItemTags.Medical, Color.red);
            var cloth = MakeItem("Cloth", "cloth", "布料", ItemCategory.Material, ItemRarity.Common, 5, .1f, 4, ItemTags.Part, Color.gray);
            var chemicals = MakeItem("Chemicals", "chemicals", "化学品", ItemCategory.Material, ItemRarity.Uncommon, 3, .3f, 12, ItemTags.Part, Color.cyan);
            var parts = MakeItem("MechanicalParts", "mechanical_parts", "机械零件", ItemCategory.Material, ItemRarity.Uncommon, 3, .5f, 16, ItemTags.Part, Color.gray);
            var watch = MakeItem("Watch", "intact_watch", "完好怀表", ItemCategory.Valuable, ItemRarity.Rare, 1, .2f, 75, ItemTags.Valuable, Color.yellow);
            var data = MakeItem("DataDrive", "encrypted_drive", "加密数据盘", ItemCategory.Valuable, ItemRarity.Rare, 1, .1f, 110, ItemTags.Valuable | ItemTags.Quest, Color.magenta);
            var ammo9 = MakeItem("Ammo9mm", "ammo_9mm", "9mm 手枪弹", ItemCategory.Ammo, ItemRarity.Common, 24, .012f, 1, ItemTags.Ammo, Color.yellow);
            var ammo12 = MakeItem("Ammo12G", "ammo_12g", "12G 猎枪弹", ItemCategory.Ammo, ItemRarity.Uncommon, 8, .045f, 3, ItemTags.Ammo, Color.red);
            var ammo556 = MakeItem("Ammo556", "ammo_556", "5.56 步枪弹", ItemCategory.Ammo, ItemRarity.Uncommon, 20, .014f, 2, ItemTags.Ammo, Color.green);
            assets.PistolItem = MakeItem("OldPistol", "old_pistol", "旧手枪", ItemCategory.Weapon, ItemRarity.Common, 1, 1.1f, 45, ItemTags.None, new Color(.55f,.65f,.72f));
            assets.PistolItem.size = 2;
            assets.KnifeItem = MakeItem("ShortKnife", "short_knife", "短刀", ItemCategory.Weapon, ItemRarity.Common, 1, .45f, 18, ItemTags.None, new Color(.72f,.72f,.76f));
            assets.KnifeItem.size = 1;
            EditorUtility.SetDirty(assets.PistolItem); EditorUtility.SetDirty(assets.KnifeItem);
            assets.Weapon.ammoItem = ammo9; assets.Shotgun.ammoItem = ammo12; assets.Rifle.ammoItem = ammo556;

            assets.LootA = MakeLoot("Civilian", 3, (canned,40f,1,2),(cloth,35f,1,2),(scrap,25f,1,2));
            assets.LootB = MakeLoot("Ammo", 3, (ammo9,55f,4,10),(ammo12,25f,2,4),(ammo556,18f,3,6),(parts,2f,1,1));
            assets.LootC = MakeLoot("Toolbox", 3, (scrap,40f,1,3),(parts,28f,1,2),(battery,22f,1,2),(chemicals,10f,1,1));
            assets.LootMedical = MakeLoot("Medical", 2, (bandage,60f,1,2),(spray,20f,1,1),(chemicals,20f,1,1));
            assets.LootSafe = MakeLoot("Safe", 2, (watch,55f,1,1),(data,15f,1,1),(battery,30f,1,2));
            assets.LootCorpse = MakeLoot("Corpse", 2, (ammo9,45f,2,6),(bandage,30f,1,1),(watch,5f,1,1),(canned,20f,1,1));
            EditorUtility.SetDirty(assets.Weapon); EditorUtility.SetDirty(assets.Shotgun); EditorUtility.SetDirty(assets.Rifle);
        }

        private static ItemDefinition MakeItem(string file, string id, string name, ItemCategory category, ItemRarity rarity, int stack, float weight, int value, ItemTags tags, Color color)
        {
            var item = LoadOrCreate<ItemDefinition>($"{ItemsFolder}/Item_{file}.asset");
            InitItem(item, id, name, value, color); item.category = category; item.rarity = rarity; item.maxStack = stack; item.unitWeight = weight; item.tags = tags;
            EditorUtility.SetDirty(item); return item;
        }

        private static LootTable MakeLoot(string file, int rolls, params (ItemDefinition item, float weight, int min, int max)[] entries)
        {
            var table = LoadOrCreate<LootTable>($"{LootFolder}/Loot_{file}.asset"); table.randomRolls = rolls; table.entries.Clear();
            foreach (var entry in entries) table.entries.Add(new LootTable.Entry(entry.item, 0) { weight = entry.weight, minCount = entry.min, maxCount = entry.max });
            EditorUtility.SetDirty(table); return table;
        }

        private static void InitWeapon(WeaponConfig weapon, string id, string name, WeaponKind kind, AmmoCaliber caliber, float damage, float interval, int magazine, float reload, float range, float effective, float minimum, int rays, float spread, float movingSpread, float sound)
        {
            weapon.weaponId=id; weapon.displayName=name; weapon.kind=kind; weapon.caliber=caliber; weapon.damage=damage; weapon.fireInterval=interval; weapon.magazineSize=magazine; weapon.reloadDuration=reload; weapon.range=range; weapon.effectiveRange=effective; weapon.minimumDamageMultiplier=minimum; weapon.rayCount=rays; weapon.standingSpread=spread; weapon.movingSpread=movingSpread; weapon.soundRadius=sound; EditorUtility.SetDirty(weapon);
        }

        private static void InitItem(ItemDefinition item, string id, string displayName, int value, Color color)
        {
            item.itemId = id;
            item.displayName = displayName;
            item.stashValue = value;
            item.iconColor = color;
        }

        private static void InitLoot(LootTable table, params (ItemDefinition item, int count)[] entries)
        {
            table.entries = new List<LootTable.Entry>();
            foreach (var (item, count) in entries)
            {
                table.entries.Add(new LootTable.Entry(item, count));
            }
        }

        private static void CreateMaterials(SliceAssets assets)
        {
            assets.Ground = LoadOrCreateMaterial("M_Ground", new Color(0.42f, 0.5f, 0.36f), false);
            assets.Wall = LoadOrCreateMaterial("M_Wall", new Color(0.58f, 0.55f, 0.5f), false);
            assets.Cover = LoadOrCreateMaterial("M_Cover", new Color(0.45f, 0.38f, 0.3f), false);
            assets.PlayerBody = LoadOrCreateMaterial("M_Player", new Color(0.25f, 0.55f, 0.95f), false);
            assets.EnemyBody = LoadOrCreateMaterial("M_Enemy", new Color(0.75f, 0.2f, 0.2f), false);
            assets.Container = LoadOrCreateMaterial("M_Container", new Color(0.85f, 0.65f, 0.3f), false);
            assets.Extraction = LoadOrCreateMaterial("M_Extraction", new Color(0.3f, 0.95f, 0.45f), true);
            assets.Tracer = LoadOrCreateMaterial("M_Tracer", new Color(1f, 0.92f, 0.45f), true);
        }

        private static Material LoadOrCreateMaterial(string materialName, Color color, bool unlit)
        {
            var path = $"{MaterialsFolder}/{materialName}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
            {
                return material;
            }

            var shader = Shader.Find(unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit");
            if (shader == null)
            {
                throw new InvalidOperationException("未找到 URP 着色器，请确认 URP 已正确配置。");
            }

            material = new Material(shader) { color = color };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static T LoadOrCreate<T>(string path, Action<T> initialize = null) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null)
            {
                return asset;
            }

            asset = ScriptableObject.CreateInstance<T>();
            initialize?.Invoke(asset);
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        // ---------- 预制体 ----------

        private static GameObject BuildPlayerPrefab(SliceAssets assets)
        {
            var root = new GameObject("Player");
            try
            {
                var controller = root.AddComponent<CharacterController>();
                controller.center = new Vector3(0f, 1f, 0f);
                controller.height = 2f;
                controller.radius = 0.4f;
                controller.stepOffset = 0.3f;

                CreatePrimitive(PrimitiveType.Capsule, "Body", root.transform, new Vector3(0f, 1f, 0f), new Vector3(0.8f, 1f, 0.8f), assets.PlayerBody, false);
                CreatePrimitive(PrimitiveType.Cube, "FacingMarker", root.transform, new Vector3(0f, 1.35f, 0.45f), new Vector3(0.25f, 0.2f, 0.4f), assets.Tracer, false);

                var muzzle = new GameObject("Muzzle").transform;
                muzzle.SetParent(root.transform, false);
                muzzle.localPosition = new Vector3(0f, 1.1f, 0.55f);

                var tracerObject = new GameObject("Tracer");
                tracerObject.transform.SetParent(root.transform, false);
                var tracer = tracerObject.AddComponent<LineRenderer>();
                tracer.useWorldSpace = true;
                tracer.positionCount = 2;
                tracer.startWidth = 0.06f;
                tracer.endWidth = 0.06f;
                tracer.sharedMaterial = assets.Tracer;
                tracer.shadowCastingMode = ShadowCastingMode.Off;
                tracer.receiveShadows = false;
                tracer.enabled = false;

                var health = root.AddComponent<Health>();
                var input = root.AddComponent<PlayerInputReader>();
                var motor = root.AddComponent<PlayerMotor>();
                var shooter = root.AddComponent<PlayerShooter>();
                var inventory = root.AddComponent<PlayerInventory>();
                var interactor = root.AddComponent<PlayerInteractor>();
                var character = root.AddComponent<PlayerCharacter>();

                SetReference(input, "actions", assets.InputActions);
                SetReference(motor, "config", assets.Player);
                SetReference(motor, "input", input);
                SetReference(shooter, "config", assets.Weapon);
                SetReference(shooter, "secondaryWeapon", assets.Knife);
                SetReference(shooter, "primaryWeaponItem", assets.PistolItem);
                SetReference(shooter, "secondaryWeaponItem", assets.KnifeItem);
                SetReference(shooter, "input", input);
                SetReference(shooter, "motor", motor);
                SetReference(shooter, "muzzle", muzzle);
                SetReference(shooter, "tracer", tracer);
                SetReference(inventory, "rules", assets.Rules);
                SetObjectArray(inventory, "startingItems", assets.PistolItem, assets.KnifeItem);
                SetReference(interactor, "rules", assets.Rules);
                SetReference(interactor, "input", input);
                SetReference(interactor, "inventory", inventory);
                SetReference(interactor, "health", health);
                SetReference(character, "config", assets.Player);

                return PrefabUtility.SaveAsPrefabAsset(root, PrefabsFolder + "/Player.prefab");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static GameObject BuildEnemyPrefab(SliceAssets assets)
        {
            var root = new GameObject("MeleeEnemy");
            try
            {
                var hitbox = root.AddComponent<CapsuleCollider>();
                hitbox.center = new Vector3(0f, 1f, 0f);
                hitbox.height = 2f;
                hitbox.radius = 0.45f;

                var agent = root.AddComponent<NavMeshAgent>();
                agent.radius = 0.45f;
                agent.height = 2f;
                agent.baseOffset = 0f;
                agent.angularSpeed = 720f;
                agent.acceleration = 20f;

                var body = CreatePrimitive(PrimitiveType.Capsule, "Body", root.transform, new Vector3(0f, 1f, 0f), new Vector3(0.9f, 1f, 0.9f), assets.EnemyBody, false);
                root.AddComponent<Health>();
                var enemy = root.AddComponent<MeleeEnemy>();
                SetReference(enemy, "config", assets.Enemy);
                SetReference(enemy, "bodyRenderer", body.GetComponent<Renderer>());

                return PrefabUtility.SaveAsPrefabAsset(root, PrefabsFolder + "/MeleeEnemy.prefab");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static GameObject BuildContainerPrefab(SliceAssets assets)
        {
            var root = new GameObject("LootContainer");
            try
            {
                var body = CreatePrimitive(PrimitiveType.Cube, "Body", root.transform, new Vector3(0f, 0.5f, 0f), new Vector3(1.2f, 1f, 1.2f), assets.Container, true);
                var container = root.AddComponent<LootContainer>();
                SetReference(container, "bodyRenderer", body.GetComponent<Renderer>());
                return PrefabUtility.SaveAsPrefabAsset(root, PrefabsFolder + "/LootContainer.prefab");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static GameObject BuildExtractionPrefab(SliceAssets assets)
        {
            var root = new GameObject("ExtractionZone");
            try
            {
                const float radius = 2.5f;
                CreatePrimitive(PrimitiveType.Cylinder, "Pad", root.transform, new Vector3(0f, 0.02f, 0f), new Vector3(radius * 2f, 0.02f, radius * 2f), assets.Extraction, false);
                CreatePrimitive(PrimitiveType.Cylinder, "Beacon", root.transform, new Vector3(0f, 3f, 0f), new Vector3(0.35f, 3f, 0.35f), assets.Extraction, false);
                var zone = root.AddComponent<ExtractionZone>();
                SetReference(zone, "rules", assets.Rules);
                SetFloat(zone, "radius", radius);
                return PrefabUtility.SaveAsPrefabAsset(root, PrefabsFolder + "/ExtractionZone.prefab");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // ---------- 场景 ----------

        private static void BuildScene(SliceAssets assets)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var lightObject = new GameObject("Directional Light");
            var sun = lightObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.2f;
            sun.shadows = LightShadows.Soft;
            lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            var level = new GameObject("Level");
            CreatePrimitive(PrimitiveType.Plane, "Ground", level.transform, Vector3.zero, new Vector3(3f, 1f, 3f), assets.Ground, true);
            BuildWalls(level.transform, assets);

            var containers = new GameObject("Containers").transform;
            containers.SetParent(level.transform, false);
            PlaceContainer(assets, containers, "民用箱", ContainerAPosition, assets.LootA, ContainerType.CivilianCrate, 1.5f, 101);
            PlaceContainer(assets, containers, "弹药箱", ContainerBPosition, assets.LootB, ContainerType.AmmoBox, 1.5f, 202);
            PlaceContainer(assets, containers, "工具箱", ContainerCPosition, assets.LootC, ContainerType.Toolbox, 1.5f, 303);
            PlaceContainer(assets, containers, "医疗柜", MedicalPosition, assets.LootMedical, ContainerType.MedicalCabinet, 1.5f, 404);
            PlaceContainer(assets, containers, "上锁保险箱", SafePosition, assets.LootSafe, ContainerType.LockedSafe, 4f, 505);
            PlaceContainer(assets, containers, "尸体", CorpsePosition, assets.LootCorpse, ContainerType.Corpse, 1.5f, 606);

            var spawn = new GameObject("PlayerSpawn").transform;
            spawn.SetPositionAndRotation(SpawnPosition, Quaternion.identity);

            var player = (GameObject)PrefabUtility.InstantiatePrefab(assets.PlayerPrefab, scene);
            player.transform.SetPositionAndRotation(SpawnPosition, Quaternion.identity);
            var playerCharacter = player.GetComponent<PlayerCharacter>();

            var enemy = (GameObject)PrefabUtility.InstantiatePrefab(assets.EnemyPrefab, scene);
            enemy.transform.SetPositionAndRotation(EnemyPosition, Quaternion.Euler(0f, 180f, 0f));
            SetReference(enemy.GetComponent<MeleeEnemy>(), "target", player.GetComponent<Health>());

            var extraction = (GameObject)PrefabUtility.InstantiatePrefab(assets.ExtractionPrefab, scene);
            extraction.transform.position = ExtractionPosition;
            var zone = extraction.GetComponent<ExtractionZone>();
            SetReference(zone, "player", playerCharacter);

            var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            var camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = 50f;
            camera.farClipPlane = 200f;
            cameraObject.AddComponent<AudioListener>();
            var rig = cameraObject.AddComponent<FixedObliqueCamera>();
            rig.Target = player.transform;
            cameraObject.transform.SetPositionAndRotation(SpawnPosition + new Vector3(0f, 16f, -11f), Quaternion.Euler(55f, 0f, 0f));

            var sessionObject = new GameObject("RunSession");
            var session = sessionObject.AddComponent<RunSession>();
            SetReference(session, "player", playerCharacter);
            SetReference(session, "extractionZone", zone);
            SetReference(session, "playerSpawn", spawn);

            var hud = new GameObject("HUD").AddComponent<SliceHud>();
            SetReference(hud, "session", session);

            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));

            BakeNavMesh(level);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                throw new InvalidOperationException($"保存场景失败：{ScenePath}");
            }
        }

        private static void BuildWalls(Transform level, SliceAssets assets)
        {
            var walls = new GameObject("Walls").transform;
            walls.SetParent(level, false);

            // 外围
            CreateBlock(walls, "Wall_North", new Vector3(0f, 0f, 15.5f), new Vector2(32f, 1f), WallHeight, assets.Wall);
            CreateBlock(walls, "Wall_South", new Vector3(0f, 0f, -15.5f), new Vector2(32f, 1f), WallHeight, assets.Wall);
            CreateBlock(walls, "Wall_East", new Vector3(15.5f, 0f, 0f), new Vector2(1f, 32f), WallHeight, assets.Wall);
            CreateBlock(walls, "Wall_West", new Vector3(-15.5f, 0f, 0f), new Vector2(1f, 32f), WallHeight, assets.Wall);

            // 北侧长墙：隔出远离敌人的安全绕行走廊（出生点 → 西侧 → 北走廊 → 撤离点）。
            CreateBlock(walls, "Wall_CorridorDivider", new Vector3(0f, 0f, 7f), new Vector2(18f, 1f), WallHeight, assets.Wall);

            // 东南角死角：容器 C 只有一个东北向入口。
            CreateBlock(walls, "Wall_PocketWest", new Vector3(8f, 0f, -11f), new Vector2(1f, 8f), WallHeight, assets.Wall);
            CreateBlock(walls, "Wall_PocketNorth", new Vector3(10.25f, 0f, -7f), new Vector2(4.5f, 1f), WallHeight, assets.Wall);

            // 中央危险捷径上的矮掩体。
            CreateBlock(walls, "Cover_SouthWest", new Vector3(-3f, 0f, -3f), new Vector2(2.5f, 1f), CoverHeight, assets.Cover);
            CreateBlock(walls, "Cover_NorthEast", new Vector3(4f, 0f, 3f), new Vector2(1f, 2.5f), CoverHeight, assets.Cover);
        }

        private static void PlaceContainer(SliceAssets assets, Transform parent, string displayName, Vector3 position, LootTable loot, ContainerType type, float searchDuration, int seedOffset)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(assets.ContainerPrefab, parent);
            instance.name = $"LootContainer_{displayName.Replace(" ", string.Empty)}";
            instance.transform.position = position;
            var container = instance.GetComponent<LootContainer>();
            SetString(container, "displayName", displayName);
            SetReference(container, "lootTable", loot);
            SetFloat(container, "searchDuration", searchDuration);
            SetInt(container, "containerType", (int)type);
            SetInt(container, "seedOffset", seedOffset);
        }

        private static void BakeNavMesh(GameObject level)
        {
            var surface = level.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.BuildNavMesh();

            if (surface.navMeshData == null)
            {
                throw new InvalidOperationException("NavMesh 烘焙失败。");
            }

            if (AssetDatabase.LoadAssetAtPath<NavMeshData>(NavMeshPath) != null)
            {
                AssetDatabase.DeleteAsset(NavMeshPath);
            }

            AssetDatabase.CreateAsset(surface.navMeshData, NavMeshPath);
        }

        private static void RegisterSceneInBuildSettings()
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

        // ---------- 工具 ----------

        private static GameObject CreatePrimitive(PrimitiveType type, string objectName, Transform parent, Vector3 localPosition,
            Vector3 localScale, Material material, bool keepCollider)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = objectName;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = localScale;
            go.GetComponent<Renderer>().sharedMaterial = material;
            if (!keepCollider)
            {
                Object.DestroyImmediate(go.GetComponent<Collider>());
            }

            return go;
        }

        private static void CreateBlock(Transform parent, string objectName, Vector3 groundCenter, Vector2 footprint, float height, Material material)
        {
            CreatePrimitive(PrimitiveType.Cube, objectName, parent, groundCenter + Vector3.up * (height * 0.5f),
                new Vector3(footprint.x, height, footprint.y), material, true);
        }

        private static SerializedProperty FindProperty(SerializedObject serialized, string propertyName)
        {
            return serialized.FindProperty(propertyName)
                   ?? throw new InvalidOperationException($"{serialized.targetObject.GetType().Name} 没有序列化字段 '{propertyName}'。");
        }

        private static void SetReference(Object target, string propertyName, Object value)
        {
            var serialized = new SerializedObject(target);
            FindProperty(serialized, propertyName).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetObjectArray(Object target, string propertyName, params Object[] values)
        {
            var serialized = new SerializedObject(target);
            var property = FindProperty(serialized, propertyName);
            property.arraySize = values.Length;
            for (var i = 0; i < values.Length; i++) property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetString(Object target, string propertyName, string value)
        {
            var serialized = new SerializedObject(target);
            FindProperty(serialized, propertyName).stringValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetFloat(Object target, string propertyName, float value)
        {
            var serialized = new SerializedObject(target);
            FindProperty(serialized, propertyName).floatValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetInt(Object target, string propertyName, int value)
        {
            var serialized = new SerializedObject(target);
            FindProperty(serialized, propertyName).intValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
