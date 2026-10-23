using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace WildernessExtraction.Map01.EditorTools
{
    /// <summary>
    /// 素材版场景的视觉布置：KayKit 树木与陈设、环境叙事道具、怪物污染层。
    /// 这里只放渲染对象，不添加任何碰撞，保证与白盒场景的物理和导航完全一致。
    /// </summary>
    public static class Map01Dressing
    {
        public const string ContaminationGroup = "MonsterContamination";
        public const string StorytellingGroup = "Props_Storytelling";
        private const int DressSeed = 7301;

        private static readonly string[] HealthyTrees =
        {
            "Tree_1_A", "Tree_1_B", "Tree_1_C", "Tree_2_A", "Tree_2_B", "Tree_2_C", "Tree_2_D", "Tree_2_E",
            "Tree_3_A", "Tree_3_B", "Tree_3_C", "Tree_4_A", "Tree_4_B", "Tree_4_C", "Tree_5_A", "Tree_5_B",
            "Tree_5_C", "Tree_6_A", "Tree_6_B", "Tree_6_C", "Tree_7_A", "Tree_7_B", "Tree_7_C"
        };

        private static readonly string[] BareTrees = { "Tree_Bare_1_A", "Tree_Bare_1_B", "Tree_Bare_1_C", "Tree_Bare_2_A", "Tree_Bare_2_B", "Tree_Bare_2_C" };
        private static readonly string[] Bushes = { "Bush_1_A", "Bush_1_C", "Bush_1_E", "Bush_2_A", "Bush_2_C", "Bush_3_A", "Bush_3_B", "Bush_4_A", "Bush_4_C", "Bush_4_E" };
        private static readonly string[] Grasses = { "Grass_1_A", "Grass_1_B", "Grass_1_C", "Grass_2_A", "Grass_2_B", "Grass_2_C" };
        private static readonly string[] Rocks = { "Rock_1_A", "Rock_1_D", "Rock_1_H", "Rock_2_A", "Rock_2_D", "Rock_3_A", "Rock_3_E", "Rock_4_A", "Rock_5_A", "Rock_6_A" };

        // ---------- 树 ----------

        /// <summary>按污染程度挑选正常、枯黄或枯死的树；污染树的视觉挂在 MonsterContamination 层。</summary>
        public static void PlaceTree(Map01Art art, Transform tree, Vector2 world, int seed, Transform mapRoot)
        {
            var random = new System.Random(seed * 7919 + 17);
            var contamination = Map01Layout.ContaminationAt(world);
            string model;
            var parent = tree;
            var height = Lerp(random, 6.5f, 9f);
            if (contamination < 0.3f || random.NextDouble() > (contamination - 0.3f) / 0.4f)
            {
                model = Map01Art.ForestModel(Pick(random, HealthyTrees), false);
            }
            else
            {
                parent = Group(Group(mapRoot, ContaminationGroup), "AbnormalTrees");
                if (contamination > 0.65f && random.NextDouble() < (contamination - 0.65f) / 0.35f + 0.25f)
                {
                    model = Map01Art.ForestModel(Pick(random, BareTrees), false);
                    height = Lerp(random, 5f, 7f);
                }
                else
                {
                    model = Map01Art.ForestModel(Pick(random, HealthyTrees), true);
                }
            }

            var instance = art.Place(model, art.Forest, parent, parent == tree ? Vector3.zero : parent.InverseTransformPoint(tree.position),
                (float)random.NextDouble() * 360f);
            if (instance == null)
            {
                return;
            }

            instance.name = parent == tree ? "Visual" : $"{tree.name}_Abnormal";
            FitHeight(instance, height, tree.position.y - 0.1f);
        }

        // ---------- 桥与室内 ----------

        public static void DressBridge(Map01Art art, Transform bridge, float x, float south, float north)
        {
            var group = Group(bridge, "Visual_KayKit");
            var half = Map01Layout.BridgeWidth * 0.5f;
            for (var z = south; z <= north + 0.01f; z += 4f)
            {
                foreach (var side in new[] { -1f, 1f })
                {
                    art.Place(Map01Art.DungeonModel("post"), art.Dungeon, group, new Vector3(x + side * half, 0f, z), 0f, 1f, Map01Layout.BridgeDeckHeight);
                }
            }

            // 防守失败的痕迹：北桥头倒下的路障、碎石和散落木箱，均在桥面两侧，不挡通道。
            var story = Group(bridge, StorytellingGroup);
            art.Place(Map01Art.DungeonModel("barrier"), art.Dungeon, story, new Vector3(x - half - 3f, 0f, north + 2f), 20f);
            art.Place(Map01Art.DungeonModel("barrier_half"), art.Dungeon, story, new Vector3(x + half + 2.5f, 0f, north + 4f), -35f);
            art.Place(Map01Art.DungeonModel("rubble_half"), art.Dungeon, story, new Vector3(x + half + 4f, 0f, north + 1f), 0f);
            art.Place(Map01Art.DungeonModel("crate_small"), art.Dungeon, story, new Vector3(x - half - 5f, 0f, north + 5f), 40f);
            // 南桥头：幸存者临时检查点。
            art.Place(Map01Art.DungeonModel("barrier_half"), art.Dungeon, story, new Vector3(x - half - 2.5f, 0f, south - 3f), 0f);
            art.Place(Map01Art.DungeonModel("box_stacked"), art.Dungeon, story, new Vector3(x + half + 3f, 0f, south - 2f), 10f);
        }

        /// <summary>用 KayKit 模型替换白盒陈设；返回 false 表示保留白盒方块（没有合适模型）。</summary>
        public static bool DressProp(Map01Art art, BuildingSpec spec, LocalRect prop, Transform interior)
        {
            var group = Group(interior, $"{prop.Name}_KayKit");
            var center = new Vector3(prop.Center.x, 0f, prop.Center.y);
            var yaw = prop.Size.y > prop.Size.x ? 90f : 0f;
            switch (prop.Name)
            {
                case var n when n.StartsWith("Shelf"):
                    // 落地货架：Dungeon 的 shelf/shelves 都是挂墙款，用无装饰的双层书架代替。
                    return art.FillRow(Map01Art.DungeonModel("bookcase_double"), art.Dungeon, group, prop) > 0;
                case "CrateStack":
                    art.Place(Map01Art.DungeonModel("crates_stacked"), art.Dungeon, group, center + new Vector3(-0.6f, 0f, 0f), 0f);
                    art.Place(Map01Art.DungeonModel("barrel_large"), art.Dungeon, group, center + new Vector3(0.9f, 0f, 0.6f), 0f);
                    return true;
                case "Workbench":
                    return art.FillRow(Map01Art.DungeonModel("table_long"), art.Dungeon, group, prop) > 0;
                case "MachineIsland":
                    art.Place(Map01Art.DungeonModel("crate_large"), art.Dungeon, group, center + new Vector3(-1f, 0f, 0f), 0f);
                    art.Place(Map01Art.DungeonModel("box_stacked"), art.Dungeon, group, center + new Vector3(1f, 0f, -0.5f), 0f);
                    art.Place(Map01Art.DungeonModel("barrel_large"), art.Dungeon, group, center + new Vector3(1f, 0f, 0.9f), 0f);
                    return true;
                case "Table":
                    art.Place(Map01Art.FurnitureModel("table_medium"), art.Furniture, group, center, yaw);
                    art.Place(Map01Art.FurnitureModel("chair_A_wood"), art.Furniture, group, center + new Vector3(0f, 0f, -0.9f), 0f);
                    art.Place(Map01Art.FurnitureModel("chair_A_wood"), art.Furniture, group, center + new Vector3(0.4f, 0f, 0.9f), 165f);
                    return true;
                case "Cabinet":
                    art.Place(Map01Art.FurnitureModel("cabinet_medium"), art.Furniture, group, center, 90f);
                    return true;
                case "Bed":
                    art.Place(Map01Art.FurnitureModel("bed_single_A"), art.Furniture, group, center, 0f);
                    return true;
                case "RecycleCounter":
                    art.FillRow(Map01Art.DungeonModel("table_long"), art.Dungeon, group, prop);
                    art.Place(Map01Art.DungeonModel("box_small"), art.Dungeon, group, center + new Vector3(0.8f, 0f, 0f), 15f, 1f, 0.9f);
                    return true;
                case "ShopTerminal":
                    art.Place(Map01Art.FurnitureModel("desk"), art.Furniture, group, center, 0f);
                    return true;
                case "BatterySlot":
                    art.Place(Map01Art.DungeonModel("barrel_small"), art.Dungeon, group, center, 0f);
                    return true;
                default:
                    Object.DestroyImmediate(group.gameObject);
                    return false;
            }
        }

        /// <summary>地板模块与靠墙陈设簇（每栋 2–5 簇，中间保留通道）。</summary>
        public static void DressInterior(Map01Art art, BuildingSpec spec, Transform interior)
        {
            // 住宅与安全屋铺木地板；仓库、厂房保留平整混凝土地面（Dungeon 石砖带明显地牢纹样，不用于现代工业建筑）。
            var wood = spec.Id == "map01_house_a" || spec.Id == "map01_safehouse";
            var floor = interior.Find("Floor");
            if (wood)
            {
                var tiles = Group(interior, "Floor_KayKit");
                art.TileFloor(Map01Art.DungeonModel("floor_wood_large"), art.Dungeon, tiles, Vector2.zero, spec.Size - new Vector2(0.4f, 0.4f), 0.03f);
                if (tiles.childCount > 0 && floor != null)
                {
                    Object.DestroyImmediate(floor.gameObject);
                }
            }
            else if (floor != null)
            {
                floor.GetComponent<Renderer>().sharedMaterial = art.Concrete;
            }

            var half = spec.Size * 0.5f;
            var clutter = Group(interior, "Clutter_KayKit");
            switch (spec.Id)
            {
                case "map01_warehouse":
                    // 入侵前：仓储；崩溃：空货架、散落箱子；幸存者：门口码放的分类箱。
                    art.Place(Map01Art.DungeonModel("box_stacked"), art.Dungeon, clutter, new Vector3(4f, 0f, -half.y + 1.6f), 0f);
                    art.Place(Map01Art.DungeonModel("box_large"), art.Dungeon, clutter, new Vector3(-4.5f, 0f, -half.y + 1.4f), 20f);
                    art.Place(Map01Art.DungeonModel("barrel_small_stack"), art.Dungeon, clutter, new Vector3(-half.x + 1.5f, 0f, half.y - 1.5f), 0f);
                    art.Place(Map01Art.DungeonModel("crate_small"), art.Dungeon, clutter, new Vector3(2f, 0f, 3f), 35f);
                    break;
                case "map01_factory":
                    art.Place(Map01Art.DungeonModel("scaffold_frame_large"), art.Dungeon, clutter, new Vector3(-8f, 0f, half.y - 1.2f), 0f);
                    art.Place(Map01Art.DungeonModel("barrel_large"), art.Dungeon, clutter, new Vector3(14.5f, 0f, -8.5f), 0f);
                    art.Place(Map01Art.DungeonModel("barrel_small"), art.Dungeon, clutter, new Vector3(15.5f, 0f, -7.5f), 0f);
                    art.Place(Map01Art.DungeonModel("bucket"), art.Dungeon, clutter, new Vector3(10f, 0f, 7f), 0f);
                    art.Place(Map01Art.DungeonModel("crate_large"), art.Dungeon, clutter, new Vector3(-15.5f, 0f, -9.5f), 0f);
                    break;
                case "map01_house_a":
                    art.Place(Map01Art.FurnitureModel("rug_rectangle_A"), art.Furniture, clutter, new Vector3(-3f, 0f, 1.5f), 0f);
                    art.Place(Map01Art.FurnitureModel("shelf_A_small"), art.Furniture, clutter, new Vector3(-1f, 0f, half.y - 0.4f), 180f);
                    art.Place(Map01Art.FurnitureModel("lamp_standing"), art.Furniture, clutter, new Vector3(-5.3f, 0f, -3.6f), 0f);
                    art.Place(Map01Art.FurnitureModel("cabinet_small"), art.Furniture, clutter, new Vector3(5.5f, 0f, 0.8f), 270f);
                    break;
                case "map01_safehouse":
                    // 幸存者改造：地铺、物资分类箱、电池槽旁的桶。
                    art.Place(Map01Art.DungeonModel("bed_floor"), art.Dungeon, clutter, new Vector3(-5f, 0f, 3.5f), 90f);
                    art.Place(Map01Art.DungeonModel("bed_floor"), art.Dungeon, clutter, new Vector3(-2.5f, 0f, 3.5f), 90f);
                    art.Place(Map01Art.DungeonModel("crate_small"), art.Dungeon, clutter, new Vector3(1f, 0f, 4.5f), 0f);
                    art.Place(Map01Art.DungeonModel("box_small"), art.Dungeon, clutter, new Vector3(2.2f, 0f, 4.6f), 25f);
                    break;
            }
        }

        // ---------- 全图 ----------

        public static void Dress(Transform mapRoot, Terrain terrain, Map01Art art)
        {
            var random = new System.Random(DressSeed);
            var props = mapRoot.Find("Props_Static");
            ScatterUndergrowth(art, terrain, Group(props, "Undergrowth"), Group(mapRoot, ContaminationGroup), random);
            PlaceStorytelling(art, terrain, Group(mapRoot, StorytellingGroup));
            PlaceContaminationMarks(art, terrain, Group(mapRoot, ContaminationGroup), random);
        }

        private static void ScatterUndergrowth(Map01Art art, Terrain terrain, Transform healthy, Transform contamination, System.Random random)
        {
            var abnormal = Group(contamination, "AbnormalUndergrowth");
            var zones = new (Vector2 center, Vector2 radii, int count)[]
            {
                (new Vector2(-91f, 19f), new Vector2(75f, 40f), 160),
                (new Vector2(-175f, -60f), new Vector2(60f, 70f), 110),
                (new Vector2(-30f, -140f), new Vector2(120f, 18f), 70),
                (new Vector2(150f, -60f), new Vector2(45f, 45f), 50),
                (new Vector2(-150f, 145f), new Vector2(80f, 12f), 50),
                (new Vector2(24f, -45f), new Vector2(60f, 28f), 60)
            };

            foreach (var zone in zones)
            {
                for (var i = 0; i < zone.count; i++)
                {
                    var p = SampleEllipse(random, zone.center, zone.radii);
                    if (!GroundAllowed(p, 1.5f))
                    {
                        continue;
                    }

                    var roll = random.NextDouble();
                    var contaminated = random.NextDouble() < (Map01Layout.ContaminationAt(p) - 0.3f) / 0.4f;
                    var parent = contaminated ? abnormal : healthy;
                    var position = GroundPosition(terrain, p);
                    string model;
                    if (roll < 0.15)
                    {
                        model = Map01Art.ForestModel(Pick(random, Rocks), false);
                    }
                    else if (roll < 0.6)
                    {
                        model = Map01Art.ForestModel(Pick(random, Bushes), contaminated);
                    }
                    else
                    {
                        model = Map01Art.ForestModel(Pick(random, Grasses), contaminated);
                    }

                    var instance = art.Place(model, art.Forest, parent, parent.InverseTransformPoint(position), (float)random.NextDouble() * 360f);
                    if (instance != null)
                    {
                        instance.transform.position += Vector3.down * 0.05f;
                    }
                }
            }
        }

        private static void PlaceStorytelling(Map01Art art, Terrain terrain, Transform group)
        {
            // 主安全屋：幸存者用路障和木箱围出的院落，北侧留出道路开口。
            var safehouse = Map01Layout.FindBuilding("map01_safehouse").Center;
            PlaceOnGround(art, terrain, group, "barrier", safehouse + new Vector2(-14f, 2f), 90f);
            PlaceOnGround(art, terrain, group, "barrier", safehouse + new Vector2(-14f, -4f), 90f);
            PlaceOnGround(art, terrain, group, "barrier_corner", safehouse + new Vector2(-14f, -9f), 0f);
            PlaceOnGround(art, terrain, group, "crates_stacked", safehouse + new Vector2(9f, 7f), 15f);
            PlaceOnGround(art, terrain, group, "barrel_large", safehouse + new Vector2(10.5f, 5f), 0f);

            // 仓库院子：散落的货箱与翻倒的桶。
            var warehouse = Map01Layout.FindBuilding("map01_warehouse").Center;
            PlaceOnGround(art, terrain, group, "crate_large", warehouse + new Vector2(-28f, -6f), 10f);
            PlaceOnGround(art, terrain, group, "box_stacked", warehouse + new Vector2(-28f, 4f), 0f);
            PlaceOnGround(art, terrain, group, "barrel_small", warehouse + new Vector2(27f, -10f), 0f);

            // 房区院落：仓促撤离留下的桶和箱子。
            var house = Map01Layout.FindBuilding("map01_house_a").Center;
            PlaceOnGround(art, terrain, group, "bucket", house + new Vector2(-8f, -7f), 0f);
            PlaceOnGround(art, terrain, group, "crate_small", house + new Vector2(8f, -7f), 30f);
            PlaceOnGround(art, terrain, group, "box_small", house + new Vector2(9f, -6f), 0f);

            // 厂房外：废料与临时掩体。
            var factory = Map01Layout.FindBuilding("map01_factory").Center;
            PlaceOnGround(art, terrain, group, "rubble_large", factory + new Vector2(21f, 6f), 0f);
            PlaceOnGround(art, terrain, group, "barrel_large", factory + new Vector2(-21f, -6f), 0f);
            PlaceOnGround(art, terrain, group, "barrier_half", factory + new Vector2(12f, -15f), 0f);
        }

        private static void PlaceContaminationMarks(Map01Art art, Terrain terrain, Transform contamination, System.Random random)
        {
            // 原创占位：孢子团与黏液斑，只用一种异常色，强度随污染值渐变。
            var spores = Group(contamination, "SporePods");
            var slime = Group(contamination, "SlimePatches");
            var placed = 0;
            for (var attempt = 0; attempt < 600 && placed < 60; attempt++)
            {
                var p = new Vector2(Lerp(random, -235f, 0f), Lerp(random, -60f, 155f));
                var c = Map01Layout.ContaminationAt(p);
                if (c < 0.45f || random.NextDouble() > c || !GroundAllowed(p, 2f))
                {
                    continue;
                }

                placed++;
                var position = GroundPosition(terrain, p);
                if (random.NextDouble() < 0.6)
                {
                    var size = 0.5f + c * 1.2f;
                    Primitive(PrimitiveType.Sphere, spores, position + Vector3.up * size * 0.3f, new Vector3(size, size * 0.7f, size), art.Contamination);
                }
                else
                {
                    var size = 1.5f + c * 3f;
                    Primitive(PrimitiveType.Cylinder, slime, position + Vector3.up * 0.04f, new Vector3(size, 0.02f, size * 0.7f), art.Contamination)
                        .transform.rotation = Quaternion.Euler(0f, (float)random.NextDouble() * 180f, 0f);
                }
            }

            // 仓库外墙的爪痕：破门方向朝向西北污染源。
            var claws = Group(contamination, "ClawMarks");
            var warehouse = Map01Layout.FindBuilding("map01_warehouse");
            var wallX = warehouse.Center.x - warehouse.Size.x * 0.5f - 0.22f;
            for (var i = 0; i < 4; i++)
            {
                var mark = Primitive(PrimitiveType.Cube, claws, new Vector3(wallX, 2.2f + i * 0.12f, warehouse.Center.y + 3f + i * 0.45f),
                    new Vector3(0.05f, 1.6f, 0.12f), art.Contamination);
                mark.transform.rotation = Quaternion.Euler(25f, 0f, 0f);
            }
        }

        // ---------- 工具 ----------

        private static void PlaceOnGround(Map01Art art, Terrain terrain, Transform group, string dungeonModel, Vector2 world, float yaw)
        {
            art.Place(Map01Art.DungeonModel(dungeonModel), art.Dungeon, group, group.InverseTransformPoint(GroundPosition(terrain, world)), yaw);
        }

        private static GameObject Primitive(PrimitiveType type, Transform parent, Vector3 position, Vector3 scale, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, true);
            go.transform.position = position;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material;
            return go;
        }

        private static bool GroundAllowed(Vector2 p, float clearance)
        {
            if (Mathf.Abs(p.x) > Map01Layout.WorldWidth * 0.5f - 2f || Mathf.Abs(p.y) > Map01Layout.WorldDepth * 0.5f - 2f)
            {
                return false;
            }

            if (Map01Layout.IsInRiver(p, 2f) || Map01Layout.DistanceToRoads(p) < Map01Layout.RoadWidth * 0.5f + clearance)
            {
                return false;
            }

            foreach (var building in Map01Layout.Buildings)
            {
                var local = p - building.Center;
                if (Mathf.Abs(local.x) < building.Size.x * 0.5f + 2f && Mathf.Abs(local.y) < building.Size.y * 0.5f + 2f)
                {
                    return false;
                }
            }

            foreach (var point in Map01Layout.Points)
            {
                if (Vector2.Distance(point.Position, p) < point.Radius + 1.5f)
                {
                    return false;
                }
            }

            return true;
        }

        private static Vector3 GroundPosition(Terrain terrain, Vector2 world)
        {
            var position = new Vector3(world.x, 0f, world.y);
            position.y = terrain.SampleHeight(position) + terrain.transform.position.y;
            return position;
        }

        private static void FitHeight(GameObject instance, float targetHeight, float groundY)
        {
            var bounds = Map01Art.WorldBounds(instance);
            if (bounds.size.y > 0.01f)
            {
                var factor = Mathf.Clamp(targetHeight / bounds.size.y, 0.3f, 6f);
                instance.transform.localScale *= factor;
            }

            bounds = Map01Art.WorldBounds(instance);
            instance.transform.position += Vector3.up * (groundY - bounds.min.y);
        }

        private static Transform Group(Transform parent, string name)
        {
            var existing = parent.Find(name);
            if (existing != null)
            {
                return existing;
            }

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        private static Vector2 SampleEllipse(System.Random random, Vector2 center, Vector2 radii)
        {
            var angle = (float)(random.NextDouble() * Math.PI * 2.0);
            var r = Mathf.Sqrt((float)random.NextDouble());
            return center + new Vector2(Mathf.Cos(angle) * radii.x, Mathf.Sin(angle) * radii.y) * r;
        }

        private static float Lerp(System.Random random, float min, float max) => min + (float)random.NextDouble() * (max - min);

        private static string Pick(System.Random random, IReadOnlyList<string> options) => options[random.Next(options.Count)];
    }
}
