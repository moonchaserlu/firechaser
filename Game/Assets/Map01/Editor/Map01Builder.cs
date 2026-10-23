using System;
using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace WildernessExtraction.Map01.EditorTools
{
    /// <summary>
    /// 生成 Map01 白盒场景（指南第 10 节阶段 1）。可重复运行：几何、地形、网格与 NavMesh 每次重建，
    /// 已存在的材质保留（美术可直接调色）。不修改 Assets/Game 下的任何资产，也不改 Build Settings。
    /// </summary>
    public static class Map01Builder
    {
        public const string RootFolder = "Assets/Map01";
        public const string ScenePath = ScenesFolder + "/Map01.unity";
        public const string PlayerPrefabPath = "Assets/Game/Prefabs/Player.prefab";

        private const string ScenesFolder = RootFolder + "/Scenes";
        private const string GeneratedFolder = RootFolder + "/Generated";
        private const string MaterialsFolder = RootFolder + "/Materials";
        private const string ReferencesFolder = RootFolder + "/References";
        private const string BlueprintAssetPath = ReferencesFolder + "/Map01_Blueprint.png";
        private const string NavMeshPath = GeneratedFolder + "/Map01_NavMesh.asset";
        private const string TerrainDataPath = GeneratedFolder + "/Map01_TerrainData.asset";
        private const string RiverMeshPath = GeneratedFolder + "/Map01_RiverMesh.asset";

        private const int HeightmapResolution = 513;
        private const int AlphamapResolution = 512;
        private const float TerrainMaxHeight = 40f;
        private const float WallThickness = 0.4f;
        private const float RiverSurfaceY = 0.06f;
        private const int NotWalkableArea = 1;
        private const int TreeSeed = 20261023;

        private sealed class Palette
        {
            public Material Wall, Partition, RoofWarehouse, RoofFactory, RoofHouse, RoofSafehouse, Floor, Prop;
            public Material Bridge, Rail, Water, TreeTrunk, TreeCanopy, Extraction, Blueprint, SolidHouse;
        }

        private sealed class Groups
        {
            public Transform Root, Blueprint, Terrain, Water, RoadsBridges, Buildings, Props, Collision, Navigation;
            public Transform SpawnPoints, PlayerSpawn, Extraction, Loot, Monsters, Events, SpecialRooms;
            public Transform Interactables, Dynamic, Markers;
        }

        [MenuItem("Wilderness/Map01/Build Map01 Whitebox")]
        private static void BuildFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            Build();
        }

        [MenuItem("Wilderness/Map01/Open Map01")]
        private static void OpenFromMenu()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                Debug.LogWarning("[Map01] 场景不存在，请先运行 Wilderness/Map01/Build Map01 Whitebox。");
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
            var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            if (playerPrefab == null)
            {
                throw new InvalidOperationException($"缺少 {PlayerPrefabPath}，请先运行 Wilderness/Build Vertical Slice。");
            }

            EnsureFolder(RootFolder);
            EnsureFolder(ScenesFolder);
            EnsureFolder(GeneratedFolder);
            EnsureFolder(MaterialsFolder);
            EnsureFolder(ReferencesFolder);

            var palette = CreatePalette();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            CreateLighting();

            var groups = CreateGroups();
            CreateBlueprint(groups, palette);
            var terrain = CreateTerrain(groups);
            CreateRiver(groups, palette);
            CreateBridge(groups, palette);
            CreateBoundary(groups);
            foreach (var building in Map01Layout.Buildings)
            {
                if (building.Enterable)
                {
                    BuildEnterable(groups, palette, building, terrain);
                }
                else
                {
                    BuildSolid(groups, palette, building);
                }
            }

            PlacePoints(groups, palette, terrain);
            ScatterTrees(groups, palette, terrain);
            BakeNavMesh(groups.Root.gameObject);

            var spawn = groups.PlayerSpawn.GetChild(0);
            CreatePlayerAndCamera(playerPrefab, spawn.position, scene);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                throw new InvalidOperationException($"保存场景失败：{ScenePath}");
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[Map01] 白盒场景已生成：{ScenePath}");
        }

        // ---------- 场景骨架 ----------

        private static void CreateLighting()
        {
            // 白盒阅读光：中性日光，仅用于检查布局与可读性，不代表终版光照方向。
            var sunObject = new GameObject("Directional Light (Whitebox)");
            var sun = sunObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.96f, 0.88f);
            sun.intensity = 1.1f;
            sun.shadows = LightShadows.Soft;
            sunObject.transform.rotation = Quaternion.Euler(50f, -35f, 0f);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.45f, 0.47f, 0.5f);
        }

        private static Groups CreateGroups()
        {
            var g = new Groups();
            g.Root = new GameObject("Map01_Root").transform;
            g.Blueprint = Group(g.Root, "BlueprintReference");
            g.Terrain = Group(g.Root, "Terrain_Static");
            g.Water = Group(g.Root, "Water");
            g.RoadsBridges = Group(g.Root, "Roads_Bridges");
            g.Buildings = Group(g.Root, "Buildings_Static");
            g.Props = Group(g.Root, "Props_Static");
            g.Collision = Group(g.Root, "Collision_Static");
            g.Navigation = Group(g.Root, "Navigation");
            g.SpawnPoints = Group(g.Root, "SpawnPoints");
            g.PlayerSpawn = Group(g.SpawnPoints, "PlayerSpawn");
            g.Extraction = Group(g.SpawnPoints, "ExtractionCandidates");
            g.Loot = Group(g.SpawnPoints, "LootSpawns");
            g.Monsters = Group(g.SpawnPoints, "MonsterSpawns");
            g.Events = Group(g.SpawnPoints, "EventSpawns");
            g.SpecialRooms = Group(g.SpawnPoints, "SpecialRoomSockets");
            g.Interactables = Group(g.Root, "Interactables");
            g.Dynamic = Group(g.Root, "Dynamic_Networked");
            g.Markers = Group(g.Root, "UI_Markers");
            return g;
        }

        private static void CreateBlueprint(Groups groups, Palette palette)
        {
            var quad = CreatePrimitive(PrimitiveType.Quad, "Blueprint_Map01", groups.Blueprint, new Vector3(0f, 0.05f, 0f),
                new Vector3(Map01Layout.WorldWidth, Map01Layout.WorldDepth, 1f), palette.Blueprint, false);
            quad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            quad.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            groups.Blueprint.gameObject.tag = "EditorOnly";
            quad.tag = "EditorOnly";
            groups.Blueprint.gameObject.AddComponent<BlueprintReference>();
        }

        // ---------- 地形 ----------

        private static Terrain CreateTerrain(Groups groups)
        {
            DeleteIfExists(TerrainDataPath);
            var data = new TerrainData { heightmapResolution = HeightmapResolution };
            data.size = new Vector3(Map01Layout.WorldWidth, TerrainMaxHeight, Map01Layout.WorldDepth);

            var heights = new float[HeightmapResolution, HeightmapResolution];
            for (var z = 0; z < HeightmapResolution; z++)
            {
                for (var x = 0; x < HeightmapResolution; x++)
                {
                    heights[z, x] = Map01Layout.TerrainHeight(HeightmapToWorld(x, z, HeightmapResolution)) / TerrainMaxHeight;
                }
            }

            data.SetHeights(0, 0, heights);
            // 先保存为资产，再写图层与权重图：权重贴图作为子资产随 TerrainData 一起持久化。
            AssetDatabase.CreateAsset(data, TerrainDataPath);

            data.terrainLayers = new[]
            {
                CreateTerrainLayer("Grass", new Color(0.42f, 0.5f, 0.32f)),
                CreateTerrainLayer("Road", new Color(0.66f, 0.57f, 0.42f)),
                CreateTerrainLayer("Plaza", new Color(0.72f, 0.66f, 0.52f))
            };
            data.alphamapResolution = AlphamapResolution;
            data.SetAlphamaps(0, 0, PaintAlphamaps());
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();

            var terrainObject = Terrain.CreateTerrainGameObject(data);
            terrainObject.name = "Terrain_Map01";
            terrainObject.transform.SetParent(groups.Terrain, false);
            terrainObject.transform.position = new Vector3(-Map01Layout.WorldWidth * 0.5f, 0f, -Map01Layout.WorldDepth * 0.5f);
            var terrain = terrainObject.GetComponent<Terrain>();
            var shader = Shader.Find("Universal Render Pipeline/Terrain/Lit");
            if (shader != null)
            {
                terrain.materialTemplate = LoadOrCreateMaterial("M_Map01_Terrain", Color.white, false, shader);
            }

            GameObjectUtility.SetStaticEditorFlags(terrainObject, StaticEditorFlags.NavigationStatic | StaticEditorFlags.BatchingStatic);
            return terrain;
        }

        private static float[,,] PaintAlphamaps()
        {
            var maps = new float[AlphamapResolution, AlphamapResolution, 3];
            var plazaCenters = new List<(Vector2 center, float radius)>();
            foreach (var point in Map01Layout.Points)
            {
                if (point.Type == SpawnPointType.ExtractionCandidate)
                {
                    plazaCenters.Add((point.Position, point.Radius + 6f));
                }
            }

            var safehouse = Map01Layout.FindBuilding("map01_safehouse");
            plazaCenters.Add((safehouse.Center + new Vector2(-8f, 4f), 22f));
            var house = Map01Layout.FindBuilding("map01_house_a");
            plazaCenters.Add((house.Center + new Vector2(-2f, -12f), 12f));

            var halfRoad = Map01Layout.RoadWidth * 0.5f;
            for (var z = 0; z < AlphamapResolution; z++)
            {
                for (var x = 0; x < AlphamapResolution; x++)
                {
                    var world = HeightmapToWorld(x, z, AlphamapResolution);
                    var road = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((Map01Layout.DistanceToRoads(world) - halfRoad + 1f) / 2f));
                    var plaza = 0f;
                    foreach (var (center, radius) in plazaCenters)
                    {
                        plaza = Mathf.Max(plaza, 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((Vector2.Distance(world, center) - radius + 3f) / 3f)));
                    }

                    road = Mathf.Max(road - plaza, 0f);
                    maps[z, x, 2] = plaza;
                    maps[z, x, 1] = road;
                    maps[z, x, 0] = Mathf.Max(0f, 1f - road - plaza);
                }
            }

            return maps;
        }

        private static TerrainLayer CreateTerrainLayer(string layerName, Color color)
        {
            var texturePath = $"{GeneratedFolder}/T_Map01_{layerName}.asset";
            var layerPath = $"{GeneratedFolder}/TL_Map01_{layerName}.terrainlayer";
            DeleteIfExists(layerPath);
            DeleteIfExists(texturePath);

            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = $"T_Map01_{layerName}" };
            var pixels = new Color[16];
            for (var i = 0; i < pixels.Length; i++)
            {
                pixels[i] = color;
            }

            texture.SetPixels(pixels);
            texture.Apply();
            AssetDatabase.CreateAsset(texture, texturePath);

            var layer = new TerrainLayer { diffuseTexture = texture, tileSize = new Vector2(4f, 4f) };
            AssetDatabase.CreateAsset(layer, layerPath);
            return layer;
        }

        private static Vector2 HeightmapToWorld(int x, int z, int resolution)
        {
            return new Vector2(
                -Map01Layout.WorldWidth * 0.5f + x / (float)(resolution - 1) * Map01Layout.WorldWidth,
                -Map01Layout.WorldDepth * 0.5f + z / (float)(resolution - 1) * Map01Layout.WorldDepth);
        }

        // ---------- 河与桥 ----------

        private static List<float> RiverSampleXs()
        {
            var xs = new List<float>();
            var half = Map01Layout.WorldWidth * 0.5f;
            for (var x = -half; x <= half + 0.01f; x += 5f)
            {
                xs.Add(x);
            }

            xs.Add(Map01Layout.BridgeX - Map01Layout.BridgeWidth * 0.5f);
            xs.Add(Map01Layout.BridgeX + Map01Layout.BridgeWidth * 0.5f);
            xs.Sort();
            return xs;
        }

        private static void CreateRiver(Groups groups, Palette palette)
        {
            DeleteIfExists(RiverMeshPath);
            var xs = RiverSampleXs();
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (var i = 0; i < xs.Count; i++)
            {
                vertices.Add(new Vector3(xs[i], RiverSurfaceY, Map01Layout.SouthBankZ(xs[i])));
                vertices.Add(new Vector3(xs[i], RiverSurfaceY, Map01Layout.NorthBankZ(xs[i])));
                if (i == 0)
                {
                    continue;
                }

                var s0 = (i - 1) * 2;
                var n0 = s0 + 1;
                var s1 = i * 2;
                var n1 = s1 + 1;
                triangles.AddRange(new[] { s0, n0, n1, s0, n1, s1 });
            }

            var mesh = new Mesh { name = "Map01_RiverMesh" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, RiverMeshPath);

            var river = new GameObject("River");
            river.transform.SetParent(groups.Water, false);
            river.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = river.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = palette.Water;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            river.AddComponent<MeshCollider>().sharedMesh = mesh;
            // 河面标记为不可行走：AI 只能经桥过河。
            var modifier = river.AddComponent<NavMeshModifier>();
            modifier.overrideArea = true;
            modifier.area = NotWalkableArea;

            // 河岸挡墙：玩家物理不能涉水，只在桥面宽度处留口。
            var banks = Group(groups.Collision, "RiverBankWalls");
            var gapMin = Map01Layout.BridgeX - Map01Layout.BridgeWidth * 0.5f - 0.01f;
            var gapMax = Map01Layout.BridgeX + Map01Layout.BridgeWidth * 0.5f + 0.01f;
            for (var i = 0; i < xs.Count - 1; i++)
            {
                var x0 = xs[i];
                var x1 = xs[i + 1];
                if (x0 >= gapMin && x1 <= gapMax)
                {
                    continue;
                }

                var touchesGap = Mathf.Abs(x1 - gapMin) < 0.05f || Mathf.Abs(x0 - gapMax) < 0.05f;
                var overlap = touchesGap ? 0f : 0.3f;
                CreateWallSegment(banks, $"NorthBank_{i:000}",
                    new Vector2(x0, Map01Layout.NorthBankZ(x0) - 0.5f), new Vector2(x1, Map01Layout.NorthBankZ(x1) - 0.5f), overlap);
                CreateWallSegment(banks, $"SouthBank_{i:000}",
                    new Vector2(x0, Map01Layout.SouthBankZ(x0) + 0.5f), new Vector2(x1, Map01Layout.SouthBankZ(x1) + 0.5f), overlap);
            }
        }

        private static void CreateWallSegment(Transform parent, string objectName, Vector2 a, Vector2 b, float overlap)
        {
            var delta = b - a;
            var length = delta.magnitude + overlap * 2f;
            var wall = new GameObject(objectName);
            wall.transform.SetParent(parent, false);
            var mid = (a + b) * 0.5f;
            wall.transform.position = new Vector3(mid.x, 2f, mid.y);
            wall.transform.rotation = Quaternion.Euler(0f, Mathf.Atan2(delta.x, delta.y) * Mathf.Rad2Deg, 0f);
            var box = wall.AddComponent<BoxCollider>();
            box.size = new Vector3(1f, 4f, length);
            GameObjectUtility.SetStaticEditorFlags(wall, StaticEditorFlags.NavigationStatic);
        }

        private static void CreateBridge(Groups groups, Palette palette)
        {
            var x = Map01Layout.BridgeX;
            var south = Map01Layout.SouthBankZ(x) - Map01Layout.BankApproach;
            var north = Map01Layout.NorthBankZ(x) + Map01Layout.BankApproach;
            var length = north - south;
            var center = (north + south) * 0.5f;
            var bridge = Group(groups.RoadsBridges, "Bridge_Main");

            CreatePrimitive(PrimitiveType.Cube, "Deck", bridge, new Vector3(x, Map01Layout.BridgeDeckHeight * 0.5f, center),
                new Vector3(Map01Layout.BridgeWidth, Map01Layout.BridgeDeckHeight, length), palette.Bridge, true);
            var railX = Map01Layout.BridgeWidth * 0.5f;
            foreach (var side in new[] { -1f, 1f })
            {
                CreatePrimitive(PrimitiveType.Cube, side < 0f ? "Rail_West" : "Rail_East", bridge,
                    new Vector3(x + side * railX, Map01Layout.BridgeDeckHeight + 0.55f, center), new Vector3(0.4f, 1.1f, length), palette.Rail, true);
                foreach (var end in new[] { south, north })
                {
                    CreatePrimitive(PrimitiveType.Cube, "Post", bridge, new Vector3(x + side * railX, 0.9f, end), new Vector3(0.8f, 1.8f, 0.8f), palette.Rail, true);
                }
            }

            foreach (Transform child in bridge)
            {
                GameObjectUtility.SetStaticEditorFlags(child.gameObject, StaticEditorFlags.NavigationStatic | StaticEditorFlags.BatchingStatic);
            }
        }

        private static void CreateBoundary(Groups groups)
        {
            var boundary = Group(groups.Collision, "MapBoundary");
            var hw = Map01Layout.WorldWidth * 0.5f;
            var hd = Map01Layout.WorldDepth * 0.5f;
            BoundaryWall(boundary, "North", new Vector3(0f, 4f, hd + 0.5f), new Vector3(Map01Layout.WorldWidth + 2f, 8f, 1f));
            BoundaryWall(boundary, "South", new Vector3(0f, 4f, -hd - 0.5f), new Vector3(Map01Layout.WorldWidth + 2f, 8f, 1f));
            BoundaryWall(boundary, "East", new Vector3(hw + 0.5f, 4f, 0f), new Vector3(1f, 8f, Map01Layout.WorldDepth + 2f));
            BoundaryWall(boundary, "West", new Vector3(-hw - 0.5f, 4f, 0f), new Vector3(1f, 8f, Map01Layout.WorldDepth + 2f));
        }

        private static void BoundaryWall(Transform parent, string side, Vector3 position, Vector3 size)
        {
            var wall = new GameObject($"Boundary_{side}");
            wall.transform.SetParent(parent, false);
            wall.transform.position = position;
            wall.AddComponent<BoxCollider>().size = size;
            GameObjectUtility.SetStaticEditorFlags(wall, StaticEditorFlags.NavigationStatic);
        }

        // ---------- 建筑 ----------

        private static void BuildEnterable(Groups groups, Palette palette, BuildingSpec spec, Terrain terrain)
        {
            var root = new GameObject(spec.Id).transform;
            root.SetParent(groups.Buildings, false);
            root.position = new Vector3(spec.Center.x, 0f, spec.Center.y);

            var exterior = Group(root, "ExteriorLower");
            var interior = Group(root, "InteriorVisuals");
            var roofGroup = Group(root, "RoofRevealGroup");
            var physics = Group(root, "Physics");
            Group(root, "Navigation");
            var volumesGroup = Group(root, "InteriorVolumes");
            var portals = Group(root, "EntrancePortals");
            Group(root, "LootSpawns");
            Group(root, "EventSpawns");

            var half = spec.Size * 0.5f;
            var h = spec.WallHeight;
            BuildSide(exterior, physics, palette.Wall, spec, WallSide.North, new Vector2(-half.x, half.y), new Vector2(half.x, half.y), h);
            BuildSide(exterior, physics, palette.Wall, spec, WallSide.South, new Vector2(-half.x, -half.y), new Vector2(half.x, -half.y), h);
            BuildSide(exterior, physics, palette.Wall, spec, WallSide.West, new Vector2(-half.x, -half.y), new Vector2(-half.x, half.y), h);
            BuildSide(exterior, physics, palette.Wall, spec, WallSide.East, new Vector2(half.x, -half.y), new Vector2(half.x, half.y), h);

            foreach (var door in spec.Doors)
            {
                var portal = new GameObject($"Portal_{door.Side}").transform;
                portal.SetParent(portals, false);
                portal.localPosition = DoorLocalPosition(spec, door);
            }

            CreatePrimitive(PrimitiveType.Cube, "Floor", interior, new Vector3(0f, 0.02f, 0f),
                new Vector3(spec.Size.x - WallThickness, 0.04f, spec.Size.y - WallThickness), palette.Floor, false);
            for (var i = 0; i < spec.Partitions.Length; i++)
            {
                var p = spec.Partitions[i];
                var dir = (p.To - p.From).normalized;
                var total = Vector2.Distance(p.From, p.To);
                var doorStart = p.DoorCenter - p.DoorWidth * 0.5f;
                var doorEnd = p.DoorCenter + p.DoorWidth * 0.5f;
                WallPiece(interior, physics, palette.Partition, $"Partition_{i}_A", p.From, p.From + dir * doorStart, h);
                WallPiece(interior, physics, palette.Partition, $"Partition_{i}_B", p.From + dir * doorEnd, p.From + dir * total, h);
            }

            foreach (var prop in spec.Props)
            {
                var position = new Vector3(prop.Center.x, prop.Height * 0.5f, prop.Center.y);
                var size = new Vector3(prop.Size.x, prop.Height, prop.Size.y);
                CreatePrimitive(PrimitiveType.Cube, prop.Name, interior, position, size, palette.Prop, false);
                PhysicsBox(physics, $"{prop.Name}_Collider", position, size);
            }

            var roofRenderers = BuildRoof(roofGroup, palette, spec);

            var volumes = new List<InteriorVolume>();
            foreach (var rect in spec.InteriorVolumes)
            {
                var volumeObject = new GameObject($"Volume_{rect.Name}");
                volumeObject.transform.SetParent(volumesGroup, false);
                volumeObject.transform.localPosition = new Vector3(rect.Center.x, 0f, rect.Center.y);
                var volume = volumeObject.AddComponent<InteriorVolume>();
                volume.Size = new Vector3(rect.Size.x, rect.Height, rect.Size.y);
                volumes.Add(volume);
            }

            var reveal = root.gameObject.AddComponent<BuildingReveal>();
            reveal.Configure(spec.Id, volumes.ToArray(), roofRenderers);

            foreach (Transform child in physics)
            {
                GameObjectUtility.SetStaticEditorFlags(child.gameObject, StaticEditorFlags.NavigationStatic);
            }
        }

        private static void BuildSide(Transform visuals, Transform physics, Material material, BuildingSpec spec, WallSide side,
            Vector2 from, Vector2 to, float height)
        {
            var doors = new List<DoorSpec>();
            foreach (var door in spec.Doors)
            {
                if (door.Side == side)
                {
                    doors.Add(door);
                }
            }

            doors.Sort((a, b) => a.Offset.CompareTo(b.Offset));
            var dir = (to - from).normalized;
            var total = Vector2.Distance(from, to);
            var cursor = 0f;
            var index = 0;
            foreach (var door in doors)
            {
                var start = total * 0.5f + door.Offset - door.Width * 0.5f;
                WallPiece(visuals, physics, material, $"Wall_{side}_{index++}", from + dir * cursor, from + dir * start, height);
                cursor = start + door.Width;
            }

            WallPiece(visuals, physics, material, $"Wall_{side}_{index}", from + dir * cursor, to, height);
        }

        private static Vector3 DoorLocalPosition(BuildingSpec spec, DoorSpec door)
        {
            var half = spec.Size * 0.5f;
            return door.Side switch
            {
                WallSide.North => new Vector3(door.Offset, 0f, half.y),
                WallSide.South => new Vector3(door.Offset, 0f, -half.y),
                WallSide.East => new Vector3(half.x, 0f, door.Offset),
                _ => new Vector3(-half.x, 0f, door.Offset)
            };
        }

        /// <summary>沿 XZ 轴对齐的墙段：视觉放在 visuals，碰撞放在 physics，两者互不从属。</summary>
        private static void WallPiece(Transform visuals, Transform physics, Material material, string objectName, Vector2 a, Vector2 b, float height)
        {
            var length = Vector2.Distance(a, b);
            if (length < 0.05f)
            {
                return;
            }

            var mid = (a + b) * 0.5f;
            var alongX = Mathf.Abs(b.x - a.x) > Mathf.Abs(b.y - a.y);
            // 墙段两端各延长半个墙厚，转角处封闭。
            var size = alongX
                ? new Vector3(length + WallThickness, height, WallThickness)
                : new Vector3(WallThickness, height, length + WallThickness);
            var position = new Vector3(mid.x, height * 0.5f, mid.y);
            CreatePrimitive(PrimitiveType.Cube, objectName, visuals, position, size, material, false);
            PhysicsBox(physics, $"{objectName}_Collider", position, size);
        }

        private static void PhysicsBox(Transform parent, string objectName, Vector3 localPosition, Vector3 size)
        {
            var go = new GameObject(objectName);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.AddComponent<BoxCollider>().size = size;
        }

        private static Renderer[] BuildRoof(Transform roofGroup, Palette palette, BuildingSpec spec)
        {
            var material = spec.Id switch
            {
                "map01_warehouse" => palette.RoofWarehouse,
                "map01_factory" => palette.RoofFactory,
                "map01_safehouse" => palette.RoofSafehouse,
                _ => palette.RoofHouse
            };

            var pieces = new List<GameObject>();
            var w = spec.Size.x + 0.8f;
            var d = spec.Size.y + 0.8f;
            var h = spec.WallHeight;
            switch (spec.Roof)
            {
                case RoofStyle.Gable:
                    pieces.AddRange(GableRoof(roofGroup, material, w, d, h));
                    break;
                case RoofStyle.Sawtooth:
                    pieces.Add(CreatePrimitive(PrimitiveType.Cube, "Roof_Slab", roofGroup, new Vector3(0f, h + 0.15f, 0f), new Vector3(w, 0.3f, d), material, false));
                    const float toothDepth = 4f;
                    var teeth = Mathf.FloorToInt(d / toothDepth);
                    for (var i = 0; i < teeth; i++)
                    {
                        var z = -d * 0.5f + toothDepth * (i + 0.5f);
                        var tooth = CreatePrimitive(PrimitiveType.Cube, $"Roof_Tooth_{i}", roofGroup, new Vector3(0f, h + 1.2f, z),
                            new Vector3(w - 0.4f, 0.25f, toothDepth / Mathf.Cos(30f * Mathf.Deg2Rad)), material, false);
                        tooth.transform.localRotation = Quaternion.Euler(-30f, 0f, 0f);
                        pieces.Add(tooth);
                    }

                    break;
                default:
                    pieces.Add(CreatePrimitive(PrimitiveType.Cube, "Roof_Slab", roofGroup, new Vector3(0f, h + 0.15f, 0f), new Vector3(w, 0.3f, d), material, false));
                    break;
            }

            var renderers = new Renderer[pieces.Count];
            for (var i = 0; i < pieces.Count; i++)
            {
                renderers[i] = pieces[i].GetComponent<Renderer>();
            }

            return renderers;
        }

        private static IEnumerable<GameObject> GableRoof(Transform parent, Material material, float w, float d, float wallHeight)
        {
            const float pitch = 30f;
            var run = d * 0.5f;
            var rise = run * Mathf.Tan(pitch * Mathf.Deg2Rad);
            var slope = run / Mathf.Cos(pitch * Mathf.Deg2Rad);
            foreach (var side in new[] { -1f, 1f })
            {
                var slab = CreatePrimitive(PrimitiveType.Cube, side < 0f ? "Roof_South" : "Roof_North", parent,
                    new Vector3(0f, wallHeight + rise * 0.5f + 0.15f, side * run * 0.5f), new Vector3(w, 0.25f, slope + 0.2f), material, false);
                slab.transform.localRotation = Quaternion.Euler(side * pitch, 0f, 0f);
                yield return slab;
            }
        }

        private static void BuildSolid(Groups groups, Palette palette, BuildingSpec spec)
        {
            var root = new GameObject(spec.Id).transform;
            root.SetParent(groups.Buildings, false);
            root.position = new Vector3(spec.Center.x, 0f, spec.Center.y);
            var body = CreatePrimitive(PrimitiveType.Cube, "Body", root, new Vector3(0f, spec.WallHeight * 0.5f, 0f),
                new Vector3(spec.Size.x, spec.WallHeight, spec.Size.y), palette.SolidHouse, true);
            GameObjectUtility.SetStaticEditorFlags(body, StaticEditorFlags.NavigationStatic | StaticEditorFlags.BatchingStatic);
            foreach (var piece in GableRoof(root, palette.RoofHouse, spec.Size.x + 0.8f, spec.Size.y + 0.8f, spec.WallHeight))
            {
                piece.name = $"Static_{piece.name}";
            }
        }

        // ---------- 点位与植被 ----------

        private static void PlacePoints(Groups groups, Palette palette, Terrain terrain)
        {
            foreach (var spec in Map01Layout.Points)
            {
                var parent = spec.Type switch
                {
                    SpawnPointType.PlayerSpawn => groups.PlayerSpawn,
                    SpawnPointType.ExtractionCandidate => groups.Extraction,
                    SpawnPointType.Loot => groups.Loot,
                    SpawnPointType.Monster => groups.Monsters,
                    SpawnPointType.Event => groups.Events,
                    _ => groups.SpecialRooms
                };

                // 建筑内的物资与事件点挂在建筑自己的分组下，便于整栋拆分或替换。
                if (!string.IsNullOrEmpty(spec.BuildingId) && (spec.Type == SpawnPointType.Loot || spec.Type == SpawnPointType.Event))
                {
                    var building = groups.Buildings.Find(spec.BuildingId);
                    parent = building.Find(spec.Type == SpawnPointType.Loot ? "LootSpawns" : "EventSpawns");
                }

                var go = new GameObject(spec.Id);
                go.transform.SetParent(parent, true);
                go.transform.position = GroundPoint(terrain, spec.Position);
                go.AddComponent<MapSpawnPoint>().Configure(spec);

                if (spec.Type == SpawnPointType.ExtractionCandidate)
                {
                    var marker = CreatePrimitive(PrimitiveType.Cylinder, $"Marker_{spec.Id}", groups.Markers,
                        go.transform.position + Vector3.up * 0.08f, new Vector3(spec.Radius * 2f, 0.02f, spec.Radius * 2f), palette.Extraction, false);
                    marker.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                }
            }
        }

        private static Vector3 GroundPoint(Terrain terrain, Vector2 world)
        {
            var position = new Vector3(world.x, 0f, world.y);
            position.y = terrain.SampleHeight(position) + terrain.transform.position.y;
            return position;
        }

        private static void ScatterTrees(Groups groups, Palette palette, Terrain terrain)
        {
            var zones = new (Vector2 center, Vector2 radii, int count)[]
            {
                (new Vector2(-91f, 19f), new Vector2(75f, 40f), 120),   // 树林
                (new Vector2(-175f, -60f), new Vector2(60f, 70f), 90),  // 西南林地
                (new Vector2(-30f, -140f), new Vector2(120f, 18f), 60), // 南缘
                (new Vector2(150f, -60f), new Vector2(45f, 45f), 45),   // 山东侧
                (new Vector2(215f, -10f), new Vector2(25f, 60f), 30),   // 东缘
                (new Vector2(-150f, 145f), new Vector2(80f, 12f), 30),  // 北岸西段
                (new Vector2(60f, 140f), new Vector2(70f, 15f), 35)     // 北岸中段
            };

            var avoid = new List<(Vector2 center, float radius)>();
            foreach (var point in Map01Layout.Points)
            {
                avoid.Add((point.Position, point.Radius + 6f));
            }

            var random = new System.Random(TreeSeed);
            var forest = Group(groups.Props, "Trees");
            var placed = new List<Vector2>();
            var index = 0;
            foreach (var zone in zones)
            {
                for (var attempt = 0; attempt < zone.count * 4 && placed.Count < index + zone.count; attempt++)
                {
                    var angle = (float)(random.NextDouble() * Math.PI * 2.0);
                    var r = Mathf.Sqrt((float)random.NextDouble());
                    var p = zone.center + new Vector2(Mathf.Cos(angle) * zone.radii.x, Mathf.Sin(angle) * zone.radii.y) * r;
                    if (!TreeAllowed(p, avoid, placed))
                    {
                        continue;
                    }

                    placed.Add(p);
                    var ground = GroundPoint(terrain, p);
                    var scale = 0.85f + (float)random.NextDouble() * 0.4f;
                    var tree = new GameObject($"Tree_{placed.Count:000}").transform;
                    tree.SetParent(forest, false);
                    tree.position = ground;
                    var trunk = CreatePrimitive(PrimitiveType.Cylinder, "Trunk", tree, new Vector3(0f, 2.2f * scale, 0f),
                        new Vector3(0.7f, 2.2f * scale, 0.7f), palette.TreeTrunk, true);
                    GameObjectUtility.SetStaticEditorFlags(trunk, StaticEditorFlags.NavigationStatic | StaticEditorFlags.BatchingStatic);
                    CreatePrimitive(PrimitiveType.Sphere, "Canopy", tree, new Vector3(0f, 5.4f * scale, 0f),
                        new Vector3(4.2f, 3.6f, 4.2f) * scale, palette.TreeCanopy, false);
                }

                index = placed.Count;
            }
        }

        private static bool TreeAllowed(Vector2 p, List<(Vector2 center, float radius)> avoid, List<Vector2> placed)
        {
            var hw = Map01Layout.WorldWidth * 0.5f - 3f;
            var hd = Map01Layout.WorldDepth * 0.5f - 3f;
            if (Mathf.Abs(p.x) > hw || Mathf.Abs(p.y) > hd)
            {
                return false;
            }

            // 树不能落在河里、堵住道路、桥头或贴着建筑。
            if (Map01Layout.IsInRiver(p, 4f) || Map01Layout.DistanceToRoads(p) < Map01Layout.RoadWidth * 0.5f + 3f)
            {
                return false;
            }

            if (Mathf.Abs(p.x - Map01Layout.BridgeX) < 14f && p.y > Map01Layout.SouthBankZ(p.x) - 14f && p.y < Map01Layout.NorthBankZ(p.x) + 14f)
            {
                return false;
            }

            foreach (var building in Map01Layout.Buildings)
            {
                var local = p - building.Center;
                if (Mathf.Abs(local.x) < building.Size.x * 0.5f + 6f && Mathf.Abs(local.y) < building.Size.y * 0.5f + 6f)
                {
                    return false;
                }
            }

            foreach (var (center, radius) in avoid)
            {
                if (Vector2.Distance(p, center) < radius)
                {
                    return false;
                }
            }

            foreach (var other in placed)
            {
                if ((p - other).sqrMagnitude < 4.5f * 4.5f)
                {
                    return false;
                }
            }

            return true;
        }

        // ---------- 导航与玩家 ----------

        private static void BakeNavMesh(GameObject root)
        {
            var surface = root.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.BuildNavMesh();
            if (surface.navMeshData == null)
            {
                throw new InvalidOperationException("Map01 NavMesh 烘焙失败。");
            }

            DeleteIfExists(NavMeshPath);
            AssetDatabase.CreateAsset(surface.navMeshData, NavMeshPath);
        }

        private static void CreatePlayerAndCamera(GameObject playerPrefab, Vector3 spawn, UnityEngine.SceneManagement.Scene scene)
        {
            var player = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab, scene);
            player.transform.SetPositionAndRotation(spawn, Quaternion.identity);
            // 白盒阶段关闭黑暗暴露：Map01 尚未布置光路，避免误判伤害。光照方向确认后再打开。
            var exposure = player.GetComponent<LightExposure>();
            if (exposure != null)
            {
                exposure.enabled = false;
                PrefabUtility.RecordPrefabInstancePropertyModifications(exposure);
            }

            player.AddComponent<RevealViewer>();

            var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            var camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = 50f;
            camera.farClipPlane = 400f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.62f, 0.7f, 0.76f);
            cameraObject.AddComponent<AudioListener>();
            var rig = cameraObject.AddComponent<FixedObliqueCamera>();
            rig.Target = player.transform;
            cameraObject.transform.SetPositionAndRotation(spawn + new Vector3(0f, 16f, -11f), Quaternion.Euler(55f, 0f, 0f));
        }

        // ---------- 资产 ----------

        private static Palette CreatePalette()
        {
            // 色板取自指南 3.2：混凝土灰、灰蓝金属、褪色青绿、尘土赭、旧砖红、木褐。
            return new Palette
            {
                Wall = LoadOrCreateMaterial("M_Map01_Wall", new Color(0.62f, 0.6f, 0.56f)),
                Partition = LoadOrCreateMaterial("M_Map01_Partition", new Color(0.74f, 0.71f, 0.64f)),
                RoofWarehouse = LoadOrCreateMaterial("M_Map01_RoofWarehouse", new Color(0.42f, 0.48f, 0.52f)),
                RoofFactory = LoadOrCreateMaterial("M_Map01_RoofFactory", new Color(0.36f, 0.5f, 0.48f)),
                RoofHouse = LoadOrCreateMaterial("M_Map01_RoofHouse", new Color(0.6f, 0.3f, 0.24f)),
                RoofSafehouse = LoadOrCreateMaterial("M_Map01_RoofSafehouse", new Color(0.66f, 0.5f, 0.28f)),
                Floor = LoadOrCreateMaterial("M_Map01_Floor", new Color(0.5f, 0.4f, 0.3f)),
                Prop = LoadOrCreateMaterial("M_Map01_Prop", new Color(0.55f, 0.42f, 0.28f)),
                Bridge = LoadOrCreateMaterial("M_Map01_Bridge", new Color(0.5f, 0.42f, 0.32f)),
                Rail = LoadOrCreateMaterial("M_Map01_Rail", new Color(0.36f, 0.38f, 0.4f)),
                Water = LoadOrCreateMaterial("M_Map01_Water", new Color(0.24f, 0.45f, 0.6f)),
                TreeTrunk = LoadOrCreateMaterial("M_Map01_TreeTrunk", new Color(0.36f, 0.27f, 0.2f)),
                TreeCanopy = LoadOrCreateMaterial("M_Map01_TreeCanopy", new Color(0.26f, 0.38f, 0.24f)),
                SolidHouse = LoadOrCreateMaterial("M_Map01_SolidHouse", new Color(0.66f, 0.6f, 0.52f)),
                Extraction = LoadOrCreateMaterial("M_Map01_Extraction", new Color(0.55f, 0.35f, 0.9f), true),
                Blueprint = LoadBlueprintMaterial()
            };
        }

        private static Material LoadBlueprintMaterial()
        {
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(BlueprintAssetPath) == null)
            {
                Debug.LogWarning($"[Map01] 未找到概念图 {BlueprintAssetPath}，蓝图参考平面将显示纯色。");
                return LoadOrCreateMaterial("M_Map01_Blueprint", Color.gray, true);
            }

            var material = LoadOrCreateMaterial("M_Map01_Blueprint", Color.white, true);
            material.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(BlueprintAssetPath);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material LoadOrCreateMaterial(string materialName, Color color, bool unlit = false, Shader shaderOverride = null)
        {
            var path = $"{MaterialsFolder}/{materialName}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
            {
                return material;
            }

            var shader = shaderOverride != null ? shaderOverride : Shader.Find(unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit");
            if (shader == null)
            {
                throw new InvalidOperationException("未找到 URP 着色器，请确认 URP 已正确配置。");
            }

            material = new Material(shader) { color = color };
            if (!unlit && shaderOverride == null)
            {
                // 哑光、高粗糙度（指南 3.2）。
                material.SetFloat("_Smoothness", 0.15f);
            }

            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        // ---------- 工具 ----------

        private static Transform Group(Transform parent, string objectName)
        {
            var go = new GameObject(objectName);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

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

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            var slash = path.LastIndexOf('/');
            AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
        }

        private static void DeleteIfExists(string path)
        {
            if (AssetDatabase.LoadMainAssetAtPath(path) != null)
            {
                AssetDatabase.DeleteAsset(path);
            }
        }
    }
}
