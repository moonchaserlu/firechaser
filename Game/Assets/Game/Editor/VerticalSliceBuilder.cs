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

        // 光路原型灰盒（约 60×108 米，X 向右、Z 向北）：南端安全屋 → 近郊（z 6–31）→ 外围（z 33–63）→ 深层（z 64–100）。
        // z=32 的分隔墙只留西、东两个缺口：西侧是借固定灯的长保守路线，东侧是要靠火堆穿越长黑暗的短危险路线。
        private static readonly Vector3 MapCenter = new Vector3(0f, 0f, 46f);
        private static readonly Vector2 MapHalfSize = new Vector2(30f, 54f);
        private static readonly Vector3 SafeHouseCenter = Vector3.zero;
        private static readonly Vector3 SpawnPosition = new Vector3(0f, 0f, -3f);
        // 返程区在安全屋内、门口内侧。
        private static readonly Vector3 ExtractionPosition = new Vector3(0f, 0f, 1.5f);
        // 敌对人机出生点（外围微光岛 B、深层观察点）。
        private static readonly (string Name, Vector3 Position)[] BotSpawns =
        {
            ("外围人机", new Vector3(-5f, 0f, 50.5f)),
            ("深层人机", new Vector3(8f, 0f, 68.5f)),
        };

        private struct LampSpec
        {
            public string Name;
            public Vector3 Position;
            public float RadiusOverride;

            public LampSpec(string name, float x, float z, float radiusOverride = 0f)
            {
                Name = name;
                Position = new Vector3(x, 0f, z);
                RadiusOverride = radiusOverride;
            }
        }

        // 固定安全灯：近郊三盏相互衔接；西侧两盏+外围两座微光岛构成保守路线；深层只有一处小半径观察点。
        // 外围两岛之间、外围到深层之间都留有超过预警距离的黑暗间隔，固定灯不构成直达深层的免费链。
        private static readonly LampSpec[] FixedLamps =
        {
            new LampSpec("Lamp_Near_1", 0f, 11f),
            new LampSpec("Lamp_Near_2", -6f, 18f),
            new LampSpec("Lamp_Near_3", 5f, 24f),
            new LampSpec("Lamp_West", -18f, 26f),
            new LampSpec("Lamp_OuterIsland_A", -18f, 40f),
            new LampSpec("Lamp_OuterIsland_B", -6f, 52f),
            new LampSpec("Lamp_DeepLookout", 8f, 70f, 3.5f),
        };

        private struct EnemySpec
        {
            public string Name;
            public Vector3 Start;
            public Vector3[] Route;

            public EnemySpec(string name, Vector3 start, params Vector3[] route)
            {
                Name = name;
                Start = start;
                Route = route;
            }
        }

        // 近郊和安全屋入口不放怪物；外围一只、深层两只。
        private static readonly EnemySpec[] Enemies =
        {
            new EnemySpec("Monster_Outer", new Vector3(-10f, 0f, 44f), new Vector3(-10f, 0f, 44f), new Vector3(2f, 0f, 42f), new Vector3(-2f, 0f, 56f)),
            new EnemySpec("Monster_Deep_1", new Vector3(10f, 0f, 82f), new Vector3(4f, 0f, 80f), new Vector3(16f, 0f, 82f), new Vector3(10f, 0f, 92f)),
            new EnemySpec("Monster_Deep_2", new Vector3(-6f, 0f, 88f), new Vector3(-8f, 0f, 82f), new Vector3(-4f, 0f, 94f)),
        };

        // 随机事件候选点（M5 使用）：A 为外围两岛之间的旧火盆，B 为外围与深层交界的黑暗物资车。
        private static readonly Vector3 EventPointA = new Vector3(-12f, 0f, 46f);
        private static readonly Vector3 EventPointB = new Vector3(-1f, 0f, 64f);

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
            public AiBotConfig AiBot;
            public RunRulesConfig Rules;
            public DarknessConfig Darkness;
            public CampfireConfig Campfire;
            public BleedingConfig Bleeding;
            public ItemDefinition FuelItem;
            public LootTable LootStarter;
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
            public Material LightRing;
            public Material LampPost;
            public Material CampfireLog;
            public Material Flame;
            public Material PreviewValid;
            public Material PreviewInvalid;
            public Material Blood;
            public GameObject PlayerPrefab;
            public GameObject EnemyPrefab;
            public GameObject AiBotPrefab;
            public Material AiBotBody;
            public GameObject ContainerPrefab;
            public GameObject ExtractionPrefab;
            public GameObject SafeLampPrefab;
            public Campfire CampfirePrefab;
            public GameObject BloodMarkPrefab;
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

            assets.CampfirePrefab = BuildCampfirePrefab(assets);
            assets.BloodMarkPrefab = BuildBloodMarkPrefab(assets);
            assets.PlayerPrefab = BuildPlayerPrefab(assets);
            assets.EnemyPrefab = BuildEnemyPrefab(assets);
            assets.AiBotPrefab = BuildAiBotPrefab(assets);
            assets.ContainerPrefab = BuildContainerPrefab(assets);
            assets.ExtractionPrefab = BuildExtractionPrefab(assets);
            assets.SafeLampPrefab = BuildSafeLampPrefab(assets);

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
            assets.AiBot = LoadOrCreate<AiBotConfig>(DataFolder + "/AiBotConfig.asset");
            assets.Rules = LoadOrCreate<RunRulesConfig>(DataFolder + "/RunRulesConfig.asset");
            // 光路原型：背包固定 8 格，燃料与战利品竞争容量。
            assets.Rules.inventoryCapacity = 8;
            // 安全屋入口返程：连续停留 2 秒。
            assets.Rules.extractionDuration = 2f;
            EditorUtility.SetDirty(assets.Rules);
            assets.Darkness = LoadOrCreate<DarknessConfig>(DataFolder + "/DarknessConfig.asset");
            assets.Campfire = LoadOrCreate<CampfireConfig>(DataFolder + "/CampfireConfig.asset");
            assets.Bleeding = LoadOrCreate<BleedingConfig>(DataFolder + "/BleedingConfig.asset");
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

            var fuel = MakeItem("FuelPack", "fuel_pack", "燃料包", ItemCategory.Consumable, ItemRarity.Common, 5, .8f, 15, ItemTags.None, new Color(1f, .55f, .15f));
            assets.FuelItem = fuel;
            assets.Campfire.fuelItem = fuel;
            EditorUtility.SetDirty(assets.Campfire);

            // 近郊教程箱：随机掉落之外保底 2 个燃料包，避免软锁。
            assets.LootStarter = MakeLoot("CivilianStarter", 3, (canned,40f,1,2),(cloth,35f,1,2),(scrap,25f,1,2));
            assets.LootStarter.guaranteedEntries.Add(new LootTable.Entry(fuel, 2));
            assets.LootA = MakeLoot("Civilian", 3, (canned,35f,1,2),(cloth,30f,1,2),(scrap,20f,1,2),(fuel,15f,1,1));
            assets.LootB = MakeLoot("Ammo", 3, (ammo9,55f,4,10),(ammo12,25f,2,4),(ammo556,18f,3,6),(parts,2f,1,1));
            assets.LootC = MakeLoot("Toolbox", 3, (scrap,35f,1,3),(parts,25f,1,2),(battery,20f,1,2),(chemicals,10f,1,1),(fuel,10f,1,1));
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
            var table = LoadOrCreate<LootTable>($"{LootFolder}/Loot_{file}.asset"); table.randomRolls = rolls; table.entries.Clear(); table.guaranteedEntries.Clear();
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
            assets.AiBotBody = LoadOrCreateMaterial("M_AiBot", new Color(0.2f, 0.8f, 0.8f), false);
            assets.Container = LoadOrCreateMaterial("M_Container", new Color(0.85f, 0.65f, 0.3f), false);
            assets.Extraction = LoadOrCreateMaterial("M_Extraction", new Color(0.3f, 0.95f, 0.45f), true);
            assets.Tracer = LoadOrCreateMaterial("M_Tracer", new Color(1f, 0.92f, 0.45f), true);
            assets.LightRing = LoadOrCreateMaterial("M_SafeLightRing", new Color(1f, 0.72f, 0.32f), true);
            assets.LampPost = LoadOrCreateMaterial("M_LampPost", new Color(0.3f, 0.28f, 0.26f), false);
            assets.CampfireLog = LoadOrCreateMaterial("M_CampfireLog", new Color(0.35f, 0.22f, 0.12f), false);
            assets.Flame = LoadOrCreateMaterial("M_Flame", new Color(1f, 0.55f, 0.15f), true);
            assets.PreviewValid = LoadOrCreateMaterial("M_PreviewValid", new Color(0.35f, 1f, 0.45f), true);
            assets.PreviewInvalid = LoadOrCreateMaterial("M_PreviewInvalid", new Color(1f, 0.3f, 0.25f), true);
            assets.Blood = LoadOrCreateMaterial("M_Blood", new Color(0.55f, 0.04f, 0.04f), true);
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
                var exposure = root.AddComponent<LightExposure>();
                var character = root.AddComponent<PlayerCharacter>();
                root.AddComponent<CombatTarget>();

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
                // 每局携带 2 个燃料包。
                SetObjectArray(inventory, "startingItems", assets.PistolItem, assets.KnifeItem, assets.FuelItem, assets.FuelItem);
                SetReference(interactor, "rules", assets.Rules);
                SetReference(interactor, "input", input);
                SetReference(interactor, "inventory", inventory);
                SetReference(interactor, "health", health);
                SetReference(exposure, "config", assets.Darkness);

                var bleeding = root.AddComponent<BleedingStatus>();
                SetReference(bleeding, "config", assets.Bleeding);
                SetReference(bleeding, "markPrefab", assets.BloodMarkPrefab);

                var placer = root.AddComponent<CampfirePlacer>();
                SetReference(placer, "config", assets.Campfire);
                SetReference(placer, "campfirePrefab", assets.CampfirePrefab);
                SetReference(placer, "previewFootprint", CreateLine(root.transform, "CampfirePreviewFootprint", 0.1f, assets.PreviewValid));
                SetReference(placer, "previewRange", CreateLine(root.transform, "CampfirePreviewRange", 0.05f, assets.PreviewValid));
                SetReference(placer, "validMaterial", assets.PreviewValid);
                SetReference(placer, "invalidMaterial", assets.PreviewInvalid);
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
                SetReference(enemy, "trailRules", assets.Bleeding);
                SetReference(enemy, "bodyRenderer", body.GetComponent<Renderer>());

                return PrefabUtility.SaveAsPrefabAsset(root, PrefabsFolder + "/MeleeEnemy.prefab");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static GameObject BuildAiBotPrefab(SliceAssets assets)
        {
            var root = new GameObject("AiBot");
            try
            {
                var hitbox = root.AddComponent<CapsuleCollider>();
                hitbox.center = new Vector3(0f, 1f, 0f);
                hitbox.height = 2f;
                hitbox.radius = 0.4f;

                var agent = root.AddComponent<NavMeshAgent>();
                agent.radius = 0.4f;
                agent.height = 2f;
                agent.baseOffset = 0f;
                agent.angularSpeed = 720f;
                agent.acceleration = 20f;

                var body = CreatePrimitive(PrimitiveType.Capsule, "Body", root.transform, new Vector3(0f, 1f, 0f), new Vector3(0.8f, 1f, 0.8f), assets.AiBotBody, false);

                var labelObject = new GameObject("DebugLabel");
                labelObject.transform.SetParent(root.transform, false);
                labelObject.transform.localPosition = new Vector3(0f, 2.5f, 0f);
                var label = labelObject.AddComponent<TextMesh>();
                var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                label.font = font;
                label.fontSize = 48;
                label.characterSize = 0.06f;
                label.anchor = TextAnchor.LowerCenter;
                label.alignment = TextAlignment.Center;
                label.color = new Color(0.6f, 1f, 1f);
                labelObject.GetComponent<MeshRenderer>().sharedMaterial = font.material;

                root.AddComponent<Health>();
                var exposure = root.AddComponent<LightExposure>();
                var target = root.AddComponent<CombatTarget>();
                var bot = root.AddComponent<AiBotController>();
                SetReference(exposure, "config", assets.Darkness);
                SetInt(target, "kind", (int)CombatTargetKind.Bot);
                SetReference(bot, "config", assets.AiBot);
                SetReference(bot, "rules", assets.Rules);
                SetReference(bot, "darkness", assets.Darkness);
                SetReference(bot, "bodyRenderer", body.GetComponent<Renderer>());
                SetReference(bot, "debugLabel", label);

                // 枪械：与玩家相同的旧手枪配置与 9mm 弹药。
                var muzzle = new GameObject("Muzzle").transform;
                muzzle.SetParent(root.transform, false);
                muzzle.localPosition = new Vector3(0f, 1.3f, 0.55f);
                var tracer = CreateLine(root.transform, "Tracer", 0.05f, assets.Tracer);
                tracer.loop = false;
                tracer.useWorldSpace = true;
                tracer.positionCount = 2;
                SetReference(bot, "weapon", assets.Weapon);
                SetReference(bot, "weaponItem", assets.PistolItem);
                SetReference(bot, "muzzle", muzzle);
                SetReference(bot, "tracer", tracer);

                return PrefabUtility.SaveAsPrefabAsset(root, PrefabsFolder + "/AiBot.prefab");
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

        /// <summary>固定安全灯：灯柱 + 点光源 + 地面边界环，三者由 SafeLightSource 的玩法半径统一驱动。</summary>
        private static GameObject BuildSafeLampPrefab(SliceAssets assets)
        {
            var root = new GameObject("SafeLamp");
            try
            {
                CreatePrimitive(PrimitiveType.Cylinder, "Post", root.transform, new Vector3(0f, 1.25f, 0f), new Vector3(0.2f, 1.25f, 0.2f), assets.LampPost, false);
                CreatePrimitive(PrimitiveType.Sphere, "Bulb", root.transform, new Vector3(0f, 2.6f, 0f), new Vector3(0.45f, 0.45f, 0.45f), assets.LightRing, false);

                var lightObject = new GameObject("Light");
                lightObject.transform.SetParent(root.transform, false);
                lightObject.transform.localPosition = new Vector3(0f, 2.6f, 0f);
                var pointLight = lightObject.AddComponent<Light>();
                pointLight.type = LightType.Point;
                pointLight.color = new Color(1f, 0.74f, 0.42f);
                pointLight.intensity = 4f;
                pointLight.shadows = LightShadows.None;

                var boundaryObject = new GameObject("Boundary");
                boundaryObject.transform.SetParent(root.transform, false);
                var boundary = boundaryObject.AddComponent<LineRenderer>();
                boundary.useWorldSpace = false;
                boundary.loop = true;
                boundary.startWidth = 0.08f;
                boundary.endWidth = 0.08f;
                boundary.sharedMaterial = assets.LightRing;
                boundary.shadowCastingMode = ShadowCastingMode.Off;
                boundary.receiveShadows = false;

                var source = root.AddComponent<SafeLightSource>();
                SetReference(source, "config", assets.Darkness);
                SetInt(source, "kind", (int)SafeLightKind.FixedLamp);
                SetReference(source, "lightSource", pointLight);
                SetReference(source, "boundary", boundary);
                source.RefreshVisuals();

                return PrefabUtility.SaveAsPrefabAsset(root, PrefabsFolder + "/SafeLamp.prefab");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>玩家火堆：原木 + 火焰 + 点光源 + 边界环，安全半径与寿命来自 CampfireConfig。</summary>
        private static Campfire BuildCampfirePrefab(SliceAssets assets)
        {
            var root = new GameObject("Campfire");
            try
            {
                CreatePrimitive(PrimitiveType.Cylinder, "LogA", root.transform, new Vector3(0f, 0.12f, 0f), new Vector3(0.18f, 0.5f, 0.18f), assets.CampfireLog, false)
                    .transform.localRotation = Quaternion.Euler(90f, 30f, 0f);
                CreatePrimitive(PrimitiveType.Cylinder, "LogB", root.transform, new Vector3(0f, 0.12f, 0f), new Vector3(0.18f, 0.5f, 0.18f), assets.CampfireLog, false)
                    .transform.localRotation = Quaternion.Euler(90f, -30f, 0f);
                var flame = CreatePrimitive(PrimitiveType.Sphere, "Flame", root.transform, new Vector3(0f, 0.45f, 0f), new Vector3(0.45f, 0.6f, 0.45f), assets.Flame, false);

                var lightObject = new GameObject("Light");
                lightObject.transform.SetParent(root.transform, false);
                lightObject.transform.localPosition = new Vector3(0f, 1.6f, 0f);
                var pointLight = lightObject.AddComponent<Light>();
                pointLight.type = LightType.Point;
                pointLight.color = new Color(1f, 0.6f, 0.28f);
                pointLight.intensity = 5f;
                pointLight.shadows = LightShadows.None;

                var boundary = CreateLine(root.transform, "Boundary", 0.08f, assets.LightRing);
                boundary.useWorldSpace = false;

                var source = root.AddComponent<SafeLightSource>();
                SetReference(source, "config", assets.Darkness);
                SetInt(source, "kind", (int)SafeLightKind.Campfire);
                SetFloat(source, "radiusOverride", assets.Campfire.lightRadius);
                SetReference(source, "lightSource", pointLight);
                SetReference(source, "boundary", boundary);
                source.RefreshVisuals();

                var campfire = root.AddComponent<Campfire>();
                SetReference(campfire, "config", assets.Campfire);
                SetReference(campfire, "flame", flame);
                SetReference(campfire, "flameLight", pointLight);

                return PrefabUtility.SaveAsPrefabAsset(root, PrefabsFolder + "/Campfire.prefab").GetComponent<Campfire>();
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static GameObject BuildBloodMarkPrefab(SliceAssets assets)
        {
            var root = new GameObject("BloodMark");
            try
            {
                CreatePrimitive(PrimitiveType.Cylinder, "Splat", root.transform, Vector3.zero, new Vector3(0.55f, 0.005f, 0.4f), assets.Blood, false)
                    .GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                return PrefabUtility.SaveAsPrefabAsset(root, PrefabsFolder + "/BloodMark.prefab");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static LineRenderer CreateLine(Transform parent, string objectName, float width, Material material)
        {
            var go = new GameObject(objectName);
            go.transform.SetParent(parent, false);
            var line = go.AddComponent<LineRenderer>();
            line.loop = true;
            line.startWidth = width;
            line.endWidth = width;
            line.sharedMaterial = material;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.positionCount = 0;
            return line;
        }

        // ---------- 场景 ----------

        private static void BuildScene(SliceAssets assets)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 永久黑暗：方向光只保留最低可读度的冷色月光，不参与安全判定（安全只看 SafeLightSource）。
            var lightObject = new GameObject("Directional Light (Moonlight)");
            var sun = lightObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(0.55f, 0.62f, 0.85f);
            sun.intensity = 0.08f;
            sun.shadows = LightShadows.None;
            lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.05f, 0.055f, 0.075f);

            var level = new GameObject("Level");
            CreatePrimitive(PrimitiveType.Plane, "Ground", level.transform, MapCenter,
                new Vector3(MapHalfSize.x * 0.2f, 1f, MapHalfSize.y * 0.2f), assets.Ground, true);
            BuildWalls(level.transform, assets);

            var containers = new GameObject("Containers").transform;
            containers.SetParent(level.transform, false);
            // 近郊：教程箱（保底 2 个燃料包）与医疗柜，不放高价值物。
            var starter = PlaceContainer(assets, containers, "民用箱", new Vector3(-7f, 0f, 19.5f), assets.LootStarter, ContainerType.CivilianCrate, 1.5f, 101);
            // 燃料保底箱只留给玩家，AI Bot 不搜刮。
            SetBool(starter, "reservedForPlayer", true);
            PlaceContainer(assets, containers, "医疗柜", new Vector3(6.5f, 0f, 25f), assets.LootMedical, ContainerType.MedicalCabinet, 1.5f, 404);
            // 外围：普通容器，两座微光岛各一个，东侧黑暗中一个可能出燃料的民用箱。
            PlaceContainer(assets, containers, "弹药箱", new Vector3(-19.5f, 0f, 41f), assets.LootB, ContainerType.AmmoBox, 1.5f, 202);
            PlaceContainer(assets, containers, "工具箱", new Vector3(-4.5f, 0f, 53f), assets.LootC, ContainerType.Toolbox, 1.5f, 303);
            PlaceContainer(assets, containers, "外围民用箱", new Vector3(16f, 0f, 46f), assets.LootA, ContainerType.CivilianCrate, 1.5f, 707);
            // 深层：高价值保险箱（4 秒搜索）与尸体。
            PlaceContainer(assets, containers, "上锁保险箱", new Vector3(14f, 0f, 86f), assets.LootSafe, ContainerType.LockedSafe, 4f, 505);
            PlaceContainer(assets, containers, "尸体", new Vector3(2f, 0f, 84f), assets.LootCorpse, ContainerType.Corpse, 1.5f, 606);

            var spawn = new GameObject("PlayerSpawn").transform;
            spawn.SetPositionAndRotation(SpawnPosition, Quaternion.identity);

            var lights = new GameObject("SafeLights").transform;
            var safeHouse = PlaceSafeLight(assets, lights, "SafeHouseLight", SafeHouseCenter, SafeLightKind.SafeHouse);
            SetFloat(safeHouse.GetComponentInChildren<Light>(), "m_Intensity", 6f);
            foreach (var lamp in FixedLamps)
            {
                var instance = PlaceSafeLight(assets, lights, lamp.Name, lamp.Position, SafeLightKind.FixedLamp, lamp.RadiusOverride);
                if (lamp.RadiusOverride > 0f) SetFloat(instance.GetComponentInChildren<Light>(), "m_Intensity", 2.5f);
            }

            var events = new GameObject("EventPoints").transform;
            new GameObject("EventPoint_A_OldFirePit").transform.SetParent(events, false);
            events.GetChild(0).position = EventPointA;
            new GameObject("EventPoint_B_SupplyCart").transform.SetParent(events, false);
            events.GetChild(1).position = EventPointB;

            var player = (GameObject)PrefabUtility.InstantiatePrefab(assets.PlayerPrefab, scene);
            player.transform.SetPositionAndRotation(SpawnPosition, Quaternion.identity);
            var playerCharacter = player.GetComponent<PlayerCharacter>();

            var monsters = new GameObject("Monsters").transform;
            foreach (var spec in Enemies)
            {
                var enemy = (GameObject)PrefabUtility.InstantiatePrefab(assets.EnemyPrefab, monsters);
                enemy.name = spec.Name;
                enemy.transform.SetPositionAndRotation(spec.Start, Quaternion.Euler(0f, 180f, 0f));

                var route = new GameObject($"{spec.Name}_Patrol").transform;
                route.SetParent(monsters, false);
                var patrol = new Transform[spec.Route.Length];
                for (var i = 0; i < spec.Route.Length; i++)
                {
                    patrol[i] = new GameObject($"Patrol_{i + 1}").transform;
                    patrol[i].SetParent(route, false);
                    patrol[i].position = spec.Route[i];
                }

                SetObjectArray(enemy.GetComponent<MeleeEnemy>(), "patrolPoints", patrol);
            }

            var extraction = (GameObject)PrefabUtility.InstantiatePrefab(assets.ExtractionPrefab, scene);
            extraction.transform.position = ExtractionPosition;
            var zone = extraction.GetComponent<ExtractionZone>();
            SetReference(zone, "player", playerCharacter);

            // 敌对人机：在外围与深层的光源处出生，以出生点为地盘中心，不使用玩家的安全屋与撤离点。
            var bots = new GameObject("HostileBots").transform;
            for (var i = 0; i < BotSpawns.Length; i++)
            {
                var bot = (GameObject)PrefabUtility.InstantiatePrefab(assets.AiBotPrefab, bots);
                bot.name = $"Bot_{BotSpawns[i].Name}";
                bot.transform.SetPositionAndRotation(BotSpawns[i].Position, Quaternion.Euler(0f, 180f, 0f));
                SetString(bot.GetComponent<AiBotController>(), "botName", BotSpawns[i].Name);
            }

            var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            var camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = 50f;
            camera.farClipPlane = 200f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
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

            // 地图边界
            var w = MapHalfSize.x;
            var minZ = MapCenter.z - MapHalfSize.y;
            var maxZ = MapCenter.z + MapHalfSize.y;
            CreateBlock(walls, "Wall_North", new Vector3(0f, 0f, maxZ + 0.5f), new Vector2(w * 2f + 2f, 1f), WallHeight, assets.Wall);
            CreateBlock(walls, "Wall_South", new Vector3(0f, 0f, minZ - 0.5f), new Vector2(w * 2f + 2f, 1f), WallHeight, assets.Wall);
            CreateBlock(walls, "Wall_East", new Vector3(w + 0.5f, 0f, MapCenter.z), new Vector2(1f, MapHalfSize.y * 2f + 2f), WallHeight, assets.Wall);
            CreateBlock(walls, "Wall_West", new Vector3(-w - 0.5f, 0f, MapCenter.z), new Vector2(1f, MapHalfSize.y * 2f + 2f), WallHeight, assets.Wall);

            // 安全屋：10×10 米，北侧 3 米门洞。
            CreateBlock(walls, "SafeHouse_West", new Vector3(-5f, 0f, 0f), new Vector2(1f, 11f), WallHeight, assets.Wall);
            CreateBlock(walls, "SafeHouse_East", new Vector3(5f, 0f, 0f), new Vector2(1f, 11f), WallHeight, assets.Wall);
            CreateBlock(walls, "SafeHouse_South", new Vector3(0f, 0f, -5f), new Vector2(11f, 1f), WallHeight, assets.Wall);
            CreateBlock(walls, "SafeHouse_NorthWest", new Vector3(-3.25f, 0f, 5f), new Vector2(3.5f, 1f), WallHeight, assets.Wall);
            CreateBlock(walls, "SafeHouse_NorthEast", new Vector3(3.25f, 0f, 5f), new Vector2(3.5f, 1f), WallHeight, assets.Wall);

            // 近郊/外围分隔墙：西缺口 x∈[-22,-16]（保守路线），东缺口 x∈[6,12]（危险短路）。
            CreateBlock(walls, "Divider_West", new Vector3(-26f, 0f, 32f), new Vector2(8f, 1f), WallHeight, assets.Wall);
            CreateBlock(walls, "Divider_Center", new Vector3(-5f, 0f, 32f), new Vector2(22f, 1f), WallHeight, assets.Wall);
            CreateBlock(walls, "Divider_East", new Vector3(21f, 0f, 32f), new Vector2(18f, 1f), WallHeight, assets.Wall);

            // 外围废墟：打断视线，制造绕行。
            CreateBlock(walls, "Outer_RuinWall", new Vector3(6f, 0f, 50f), new Vector2(1f, 8f), WallHeight, assets.Wall);
            CreateBlock(walls, "Outer_RuinWall_East", new Vector3(21f, 0f, 56f), new Vector2(7f, 1f), WallHeight, assets.Wall);
            CreateBlock(walls, "Outer_Cover", new Vector3(-12f, 0f, 50f), new Vector2(2.5f, 1f), CoverHeight, assets.Cover);
            CreateBlock(walls, "Outer_Cover_East", new Vector3(14f, 0f, 40f), new Vector2(1f, 2.5f), CoverHeight, assets.Cover);

            // 深层：保险箱所在的半封闭角落与矮掩体。
            CreateBlock(walls, "Deep_SafeWall_West", new Vector3(10f, 0f, 86f), new Vector2(1f, 8f), WallHeight, assets.Wall);
            CreateBlock(walls, "Deep_SafeWall_North", new Vector3(14f, 0f, 90.5f), new Vector2(8f, 1f), WallHeight, assets.Wall);
            CreateBlock(walls, "Deep_Cover", new Vector3(0f, 0f, 78f), new Vector2(3f, 1f), CoverHeight, assets.Cover);
        }

        private static LootContainer PlaceContainer(SliceAssets assets, Transform parent, string displayName, Vector3 position, LootTable loot, ContainerType type, float searchDuration, int seedOffset)
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
            return container;
        }

        private static GameObject PlaceSafeLight(SliceAssets assets, Transform parent, string objectName, Vector3 position, SafeLightKind kind,
            float radiusOverride = 0f)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(assets.SafeLampPrefab, parent);
            instance.name = objectName;
            instance.transform.position = position;
            var source = instance.GetComponent<SafeLightSource>();
            SetInt(source, "kind", (int)kind);
            if (radiusOverride > 0f) SetFloat(source, "radiusOverride", radiusOverride);
            source.RefreshVisuals();
            foreach (var component in instance.GetComponentsInChildren<Component>())
            {
                PrefabUtility.RecordPrefabInstancePropertyModifications(component);
            }

            return instance;
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

        private static void SetBool(Object target, string propertyName, bool value)
        {
            var serialized = new SerializedObject(target);
            FindProperty(serialized, propertyName).boolValue = value;
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
