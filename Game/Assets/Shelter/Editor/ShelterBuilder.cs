using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace WildernessExtraction.Shelter.EditorTools
{
    /// <summary>
    /// 生成地下安全屋白盒场景（第一版只有靶场、仓库、工作台和出口）。可重复运行；材质与物品库在缺失时创建。
    /// 布局：中央炉火大厅，西侧仓库，东侧工坊（工作台），北侧楼梯间（出口，通往地面），南侧靶场。
    /// 镜头一侧（南侧）的墙矮，方便从斜上方看进屋里。
    /// </summary>
    public static class ShelterBuilder
    {
        public const string RootFolder = "Assets/Shelter";
        public const string ScenePath = RootFolder + "/Scenes/Shelter.unity";
        public const string ItemDatabasePath = RootFolder + "/Data/ItemDatabase.asset";
        private const string MaterialsFolder = RootFolder + "/Materials";
        private const string SeekerPrefabPath = "Assets/Game/Prefabs/Player_Seeker.prefab";
        private const string PlayerPrefabPath = "Assets/Game/Prefabs/Player.prefab";
        private const string DarknessConfigPath = "Assets/Map01/Data/Map01_DarknessConfig.asset";
        private const string RecipesPath = "Assets/Game/Data/Crafting/CraftingRecipes_Basic.asset";
        private const float WallHeight = 3.5f;
        private const float CutawayHeight = 1f;
        private const float WallThickness = 0.5f;

        private enum Side { North, South, East, West }

        private sealed class Room
        {
            public string Name;
            public Vector2 Center;
            public Vector2 Size;
            public Side[] Walls;
            public (Side side, float offset, float width)[] Doors = Array.Empty<(Side, float, float)>();
        }

        private static readonly Room[] Rooms =
        {
            new Room { Name = "Hall", Center = new Vector2(0f, 0f), Size = new Vector2(16f, 12f), Walls = new[] { Side.North, Side.South, Side.East, Side.West },
                Doors = new[] { (Side.West, 0f, 2.4f), (Side.East, 0f, 2.4f), (Side.North, 0f, 2.4f), (Side.South, 0f, 2.4f) } },
            new Room { Name = "Stash", Center = new Vector2(-12f, 0f), Size = new Vector2(8f, 10f), Walls = new[] { Side.North, Side.South, Side.West } },
            new Room { Name = "Workshop", Center = new Vector2(13f, 0f), Size = new Vector2(10f, 10f), Walls = new[] { Side.North, Side.South, Side.East } },
            new Room { Name = "Stairs", Center = new Vector2(0f, 9f), Size = new Vector2(8f, 6f), Walls = new[] { Side.North, Side.East, Side.West } },
            new Room { Name = "Range", Center = new Vector2(0f, -10f), Size = new Vector2(16f, 8f), Walls = new[] { Side.South, Side.East, Side.West } }
        };

        private sealed class Palette
        {
            public Material Floor, Wall, Prop, Wood, Metal, Fire, Bed, Paper;
        }

        [MenuItem("Wilderness/Shelter/Build Shelter")]
        private static void BuildFromMenu()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) Build();
        }

        [MenuItem("Wilderness/Shelter/Open Shelter")]
        private static void OpenFromMenu()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(ScenePath);
        }

        /// <summary>可由菜单或命令行 -executeMethod 调用。</summary>
        public static void Build()
        {
            EnsureFolder(RootFolder);
            EnsureFolder(RootFolder + "/Scenes");
            EnsureFolder(RootFolder + "/Data");
            EnsureFolder(MaterialsFolder);

            var database = BuildItemDatabase();
            var palette = CreatePalette();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.04f, 0.045f, 0.06f);

            var root = new GameObject("Shelter_Root").transform;
            var geometry = Group(root, "Geometry");
            var props = Group(root, "Props");
            var lights = Group(root, "Lights");
            var stations = Group(root, "Stations");

            // 一整块地面；房间之外是看不见底的黑暗（墙挡住，走不出去）。
            Cube(geometry, "Floor", new Vector3(1f, -0.25f, -0.5f), new Vector3(40f, 0.5f, 30f), palette.Floor, true);
            foreach (var room in Rooms)
            {
                BuildRoom(geometry, room, palette);
                var roomLight = new GameObject($"RoomLight_{room.Name}");
                roomLight.transform.SetParent(lights, false);
                roomLight.transform.position = new Vector3(room.Center.x, 0f, room.Center.y);
                var bulbs = new List<Light>();
                var lamp = new GameObject("Lamp");
                lamp.transform.SetParent(roomLight.transform, false);
                lamp.transform.localPosition = new Vector3(0f, WallHeight - 0.4f, 0f);
                var light = lamp.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = room.Name == "Hall" ? new Color(1f, 0.62f, 0.32f) : new Color(1f, 0.85f, 0.62f);
                light.intensity = room.Name == "Hall" ? 5f : 3.5f;
                light.range = Mathf.Max(room.Size.x, room.Size.y) * 0.85f;
                light.shadows = LightShadows.Soft;
                bulbs.Add(light);
                roomLight.AddComponent<ShelterRoomLight>().Configure(new Vector3(room.Size.x, WallHeight, room.Size.y), true, bulbs.ToArray());
            }

            BuildProps(props, palette);
            Station(stations, "Stash", ShelterStationKind.Stash, "仓库", new Vector3(-14f, 0f, 0f));
            Station(stations, "Workbench", ShelterStationKind.Workbench, "工作台", new Vector3(16f, 0f, 0f));
            Station(stations, "Exit", ShelterStationKind.Exit, "楼梯（出发去地面）", new Vector3(0f, 0f, 8f));
            var range = BuildRange(root, palette);

            var player = CreatePlayer(scene);
            CreateCamera(player.transform);

            var game = new GameObject("Shelter_Game");
            var controller = game.AddComponent<ShelterController>();
            var serialized = new SerializedObject(controller);
            serialized.FindProperty("player").objectReferenceValue = player.GetComponent<PlayerCharacter>();
            serialized.FindProperty("database").objectReferenceValue = database;
            serialized.FindProperty("recipes").objectReferenceValue = AssetDatabase.LoadAssetAtPath<CraftingRecipeBook>(RecipesPath);
            var starter = new List<ItemDefinition>();
            foreach (var id in new[] { "old_pistol", "short_knife", "fuel_pack", "fuel_pack", "ammo_9mm", "ammo_9mm", "ammo_9mm", "ammo_9mm", "ammo_9mm", "ammo_9mm", "ammo_9mm", "ammo_9mm", "dirty_bandage" })
            {
                var item = database.Find(id);
                if (item != null) starter.Add(item);
            }

            var starterProperty = serialized.FindProperty("starterKit");
            starterProperty.arraySize = starter.Count;
            for (var i = 0; i < starter.Count; i++) starterProperty.GetArrayElementAtIndex(i).objectReferenceValue = starter[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();

            var hud = game.AddComponent<ShelterHud>();
            var hudSerialized = new SerializedObject(hud);
            hudSerialized.FindProperty("controller").objectReferenceValue = controller;
            hudSerialized.FindProperty("range").objectReferenceValue = range;
            hudSerialized.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new InvalidOperationException($"保存场景失败：{ScenePath}");
            AssetDatabase.SaveAssets();
            Debug.Log($"[Shelter] 地下安全屋已生成：{ScenePath}");
        }

        // ---------- 物品库 ----------

        private static ItemDatabase BuildItemDatabase()
        {
            var database = AssetDatabase.LoadAssetAtPath<ItemDatabase>(ItemDatabasePath);
            if (database == null)
            {
                database = ScriptableObject.CreateInstance<ItemDatabase>();
                AssetDatabase.CreateAsset(database, ItemDatabasePath);
            }

            database.items.Clear();
            var folders = new List<string>();
            foreach (var folder in new[] { "Assets/Game/Data", "Assets/Map01/Data" })
            {
                if (AssetDatabase.IsValidFolder(folder)) folders.Add(folder);
            }

            var seen = new HashSet<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:ItemDefinition", folders.ToArray()))
            {
                var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (item != null && seen.Add(item.itemId)) database.items.Add(item);
            }

            EditorUtility.SetDirty(database);
            return database;
        }

        // ---------- 房间 ----------

        private static void BuildRoom(Transform parent, Room room, Palette palette)
        {
            var group = Group(parent, $"Room_{room.Name}");
            var half = room.Size * 0.5f;
            foreach (var side in room.Walls)
            {
                Vector2 from, to;
                switch (side)
                {
                    case Side.North: from = new Vector2(-half.x, half.y); to = new Vector2(half.x, half.y); break;
                    case Side.South: from = new Vector2(-half.x, -half.y); to = new Vector2(half.x, -half.y); break;
                    case Side.East: from = new Vector2(half.x, -half.y); to = new Vector2(half.x, half.y); break;
                    default: from = new Vector2(-half.x, -half.y); to = new Vector2(-half.x, half.y); break;
                }

                // 镜头在南侧上方：南墙做矮，让人能看进屋里。
                var height = side == Side.South ? CutawayHeight : WallHeight;
                var doors = new List<(float offset, float width)>();
                foreach (var door in room.Doors)
                {
                    if (door.side == side) doors.Add((door.offset, door.width));
                }

                doors.Sort((a, b) => a.offset.CompareTo(b.offset));
                var length = Vector2.Distance(from, to);
                var dir = (to - from).normalized;
                var cursor = 0f;
                var index = 0;
                foreach (var (offset, width) in doors)
                {
                    var start = length * 0.5f + offset - width * 0.5f;
                    Wall(group, $"Wall_{side}_{index++}", room.Center + from + dir * cursor, room.Center + from + dir * start, height, palette.Wall);
                    cursor = start + width;
                }

                Wall(group, $"Wall_{side}_{index}", room.Center + from + dir * cursor, room.Center + to, height, palette.Wall);
            }
        }

        private static void Wall(Transform parent, string name, Vector2 a, Vector2 b, float height, Material material)
        {
            var length = Vector2.Distance(a, b);
            if (length < 0.05f) return;
            var mid = (a + b) * 0.5f;
            var alongX = Mathf.Abs(b.x - a.x) > Mathf.Abs(b.y - a.y);
            var size = alongX ? new Vector3(length + WallThickness, height, WallThickness) : new Vector3(WallThickness, height, length + WallThickness);
            Cube(parent, name, new Vector3(mid.x, height * 0.5f, mid.y), size, material, true);
        }

        private static void BuildProps(Transform parent, Palette palette)
        {
            // 炉火：石圈 + 火焰 + 暖光（视觉；安全范围由大厅房间光负责）。
            var hearth = Group(parent, "Hearth");
            hearth.position = new Vector3(0f, 0f, 1.5f);
            Cylinder(hearth, "Stones", new Vector3(0f, 0.2f, 0f), new Vector3(2.2f, 0.2f, 2.2f), palette.Prop, true);
            Cylinder(hearth, "Fire", new Vector3(0f, 0.6f, 0f), new Vector3(0.9f, 0.4f, 0.9f), palette.Fire, false);
            var fireLight = new GameObject("FireLight").AddComponent<Light>();
            fireLight.transform.SetParent(hearth, false);
            fireLight.transform.localPosition = new Vector3(0f, 1.4f, 0f);
            fireLight.type = LightType.Point;
            fireLight.color = new Color(1f, 0.5f, 0.2f);
            fireLight.intensity = 4f;
            fireLight.range = 7f;

            // 仓库：靠墙的货架与箱子。
            for (var z = -4f; z <= 4f; z += 2f) Cube(parent, "Shelf", new Vector3(-15.3f, 1f, z), new Vector3(0.8f, 2f, 1.8f), palette.Wood, true);
            Cube(parent, "Crates", new Vector3(-10f, 0.5f, 4f), new Vector3(1.4f, 1f, 1.2f), palette.Wood, true);

            // 工坊：工作台与工具架。
            Cube(parent, "Workbench", new Vector3(17.2f, 0.5f, 0f), new Vector3(1.2f, 1f, 3f), palette.Wood, true);
            Cube(parent, "ToolRack", new Vector3(13f, 1f, 4.6f), new Vector3(3f, 2f, 0.4f), palette.Metal, true);

            // 楼梯间：通往地面的台阶与一张旧地图桌（出发在楼梯口交互）。
            Cube(parent, "MapTable", new Vector3(0f, 0.45f, 9f), new Vector3(2.4f, 0.9f, 1.4f), palette.Wood, true);
            Cube(parent, "MapSheet", new Vector3(0f, 0.92f, 9f), new Vector3(1.8f, 0.02f, 1f), palette.Paper, false);
            for (var i = 0; i < 5; i++) Cube(parent, $"Step_{i}", new Vector3(2.8f, 0.2f + i * 0.4f, 9.5f + i * 0.45f), new Vector3(1.6f, 0.4f, 0.45f), palette.Prop, false);
        }

        /// <summary>
        /// 靶场：南侧长房间。东头是射击位（齐腰的木台），西头三块靶子分别在约 8、10、12 米。
        /// 只有站在靶场里才能开枪；靶子被打中会记成绩并立即回满。
        /// </summary>
        private static ShelterRange BuildRange(Transform root, Palette palette)
        {
            var group = Group(root, "Range");
            group.position = new Vector3(0f, 0f, -10f);
            Cube(group, "FiringCounter", new Vector3(5.5f, 0.45f, -10f), new Vector3(0.5f, 0.9f, 6f), palette.Wood, true);
            Cube(group, "Backstop", new Vector3(-7.4f, 1.5f, -10f), new Vector3(0.6f, 3f, 7.4f), palette.Prop, true);
            var targets = new List<RangeTarget>();
            var lanes = new[] { (z: -7.8f, x: -2.5f), (z: -10f, x: -4.8f), (z: -12.2f, x: -6.8f) };
            for (var i = 0; i < lanes.Length; i++)
            {
                var target = Cube(group, $"Target_{i + 1}", new Vector3(lanes[i].x, 1.1f, lanes[i].z), new Vector3(0.9f, 1.8f, 0.25f), palette.Paper, true);
                target.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
                Cube(target.transform, "Stand", Vector3.zero, Vector3.one, palette.Wood, false).transform.localPosition = new Vector3(0f, -0.55f, 0f);
                var face = target.GetComponent<Renderer>();
                target.AddComponent<Health>();
                var rangeTarget = target.AddComponent<RangeTarget>();
                rangeTarget.Configure(face);
                targets.Add(rangeTarget);
            }

            var range = group.gameObject.AddComponent<ShelterRange>();
            range.Configure(new Vector3(16f, WallHeight, 8f), targets.ToArray());
            return range;
        }

        private static void Station(Transform parent, string name, ShelterStationKind kind, string displayName, Vector3 position)
        {
            var go = new GameObject($"Station_{name}");
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            go.AddComponent<ShelterStation>().Configure(kind, displayName);
        }

        // ---------- 玩家与相机 ----------

        private static GameObject CreatePlayer(UnityEngine.SceneManagement.Scene scene)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SeekerPrefabPath) ?? AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            if (prefab == null) throw new InvalidOperationException("缺少玩家预制体。");
            var player = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            player.transform.SetPositionAndRotation(new Vector3(0f, 0.05f, -3f), Quaternion.identity);
            var exposure = player.GetComponent<LightExposure>();
            var darkness = AssetDatabase.LoadAssetAtPath<DarknessConfig>(DarknessConfigPath);
            if (exposure != null && darkness != null)
            {
                // 安全屋同样遵守黑暗规则：只有亮着的房间安全。
                var serialized = new SerializedObject(exposure);
                serialized.FindProperty("config").objectReferenceValue = darkness;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.RecordPrefabInstancePropertyModifications(exposure);
            }

            return player;
        }

        private static void CreateCamera(Transform target)
        {
            var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            var camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = FixedObliqueCamera.DefaultFieldOfView;
            camera.nearClipPlane = FixedObliqueCamera.DefaultNearClip;
            camera.farClipPlane = 200f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            cameraObject.AddComponent<AudioListener>();
            var rig = cameraObject.AddComponent<FixedObliqueCamera>();
            rig.Target = target;
            cameraObject.transform.SetPositionAndRotation(target.position + Vector3.up * FixedObliqueCamera.DefaultFollowHeight + FixedObliqueCamera.DefaultOffset,
                Quaternion.Euler(FixedObliqueCamera.DefaultEulerAngles));
        }

        // ---------- 工具 ----------

        private static Palette CreatePalette()
        {
            return new Palette
            {
                Floor = Material("M_Shelter_Floor", new Color(0.26f, 0.25f, 0.24f)),
                Wall = Material("M_Shelter_Wall", new Color(0.42f, 0.4f, 0.37f)),
                Prop = Material("M_Shelter_Stone", new Color(0.35f, 0.33f, 0.31f)),
                Wood = Material("M_Shelter_Wood", new Color(0.45f, 0.33f, 0.22f)),
                Metal = Material("M_Shelter_Metal", new Color(0.36f, 0.42f, 0.45f)),
                Fire = Material("M_Shelter_Fire", new Color(1f, 0.55f, 0.2f), true),
                Bed = Material("M_Shelter_Bed", new Color(0.4f, 0.45f, 0.38f)),
                Paper = Material("M_Shelter_Paper", new Color(0.85f, 0.8f, 0.65f))
            };
        }

        private static Material Material(string name, Color color, bool unlit = false)
        {
            var path = $"{MaterialsFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            material = new Material(Shader.Find(unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit")) { color = color };
            if (!unlit) material.SetFloat("_Smoothness", 0.1f);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static Transform Group(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        private static GameObject Cube(Transform parent, string name, Vector3 position, Vector3 scale, Material material, bool collider)
            => Primitive(PrimitiveType.Cube, parent, name, position, scale, material, collider);

        private static GameObject Cylinder(Transform parent, string name, Vector3 localPosition, Vector3 scale, Material material, bool collider)
        {
            var go = Primitive(PrimitiveType.Cylinder, parent, name, Vector3.zero, scale, material, collider);
            go.transform.localPosition = localPosition;
            return go;
        }

        private static GameObject Primitive(PrimitiveType type, Transform parent, string name, Vector3 position, Vector3 scale, Material material, bool collider)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material;
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var slash = path.LastIndexOf('/');
            AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
        }
    }
}
