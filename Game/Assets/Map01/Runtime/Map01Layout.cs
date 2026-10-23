using System;
using System.Collections.Generic;
using UnityEngine;

namespace WildernessExtraction.Map01
{
    public enum SpawnPointType
    {
        PlayerSpawn,
        ExtractionCandidate,
        Loot,
        Monster,
        Event,
        SpecialRoom
    }

    public enum WallSide
    {
        North,
        South,
        East,
        West
    }

    public enum RoofStyle
    {
        Flat,
        Gable,
        Sawtooth
    }

    /// <summary>静态点位：稳定 ID 是唯一事实来源，不依赖层级或节点名称。</summary>
    public sealed class PointSpec
    {
        public string Id;
        public SpawnPointType Type;
        /// <summary>世界 XZ，Y 由地形高度决定。</summary>
        public Vector2 Position;
        public float Radius = 2f;
        public float Weight = 1f;
        public int DepthTier;
        public string[] Tags = Array.Empty<string>();
        public int Capacity = 1;
        /// <summary>所在建筑 ID；为空表示室外。</summary>
        public string BuildingId;
    }

    public readonly struct DoorSpec
    {
        public readonly WallSide Side;
        /// <summary>沿墙方向相对墙中心的偏移（米）。北/南墙为 X，东/西墙为 Z。</summary>
        public readonly float Offset;
        public readonly float Width;

        public DoorSpec(WallSide side, float offset, float width)
        {
            Side = side;
            Offset = offset;
            Width = width;
        }
    }

    /// <summary>轴对齐的室内隔墙，局部坐标；门洞以隔墙起点为基准沿墙方向定位。</summary>
    public readonly struct PartitionSpec
    {
        public readonly Vector2 From;
        public readonly Vector2 To;
        public readonly float DoorCenter;
        public readonly float DoorWidth;

        public PartitionSpec(Vector2 from, Vector2 to, float doorCenter, float doorWidth)
        {
            From = from;
            To = to;
            DoorCenter = doorCenter;
            DoorWidth = doorWidth;
        }
    }

    /// <summary>局部坐标中的矩形（中心与尺寸）。</summary>
    public readonly struct LocalRect
    {
        public readonly string Name;
        public readonly Vector2 Center;
        public readonly Vector2 Size;
        public readonly float Height;

        public LocalRect(string name, Vector2 center, Vector2 size, float height = 1f)
        {
            Name = name;
            Center = center;
            Size = size;
            Height = height;
        }
    }

    public sealed class BuildingSpec
    {
        public string Id;
        public string DisplayName;
        public bool Enterable;
        public Vector2 Center;
        public Vector2 Size;
        public float WallHeight;
        public RoofStyle Roof;
        public DoorSpec[] Doors = Array.Empty<DoorSpec>();
        public PartitionSpec[] Partitions = Array.Empty<PartitionSpec>();
        public LocalRect[] InteriorVolumes = Array.Empty<LocalRect>();
        /// <summary>室内占位陈设（带碰撞），Height 为高度。</summary>
        public LocalRect[] Props = Array.Empty<LocalRect>();

        public Vector2 ToWorld(Vector2 local) => Center + local;
    }

    /// <summary>
    /// Map01 白盒布局。按指南第 2 节把 1536×1024 概念图映射到 480m×320m，原点居中、图片顶部为 +Z。
    /// 道路、河岸用像素折线记录，便于和蓝图对照调整。
    /// </summary>
    public static class Map01Layout
    {
        public const string MapId = "map01";
        public const int Version = 1;
        public const float WorldWidth = 480f;
        public const float WorldDepth = 320f;
        public const float ImageWidth = 1536f;
        public const float ImageHeight = 1024f;

        public const float RoadWidth = 6f;
        public const float BridgeX = 38.4f;
        public const float BridgeWidth = 8f;
        public const float BridgeDeckHeight = 0.25f;
        public const float BankApproach = 3f;

        public static readonly Vector2 HillCenter = new Vector2(24f, -45f);
        public static readonly Vector2 HillSigma = new Vector2(65f, 32f);
        public const float HillHeight = 16f;

        public static Vector2 PixelToWorld(float px, float py)
        {
            return new Vector2((px / ImageWidth - 0.5f) * WorldWidth, (0.5f - py / ImageHeight) * WorldDepth);
        }

        // ---------- 河 ----------

        // 河岸像素采样：x，北岸 y，南岸 y（图片 y 向下）。
        private static readonly Vector3[] RiverBankPixels =
        {
            new Vector3(0f, 300f, 570f),
            new Vector3(150f, 285f, 520f),
            new Vector3(300f, 240f, 470f),
            new Vector3(450f, 200f, 410f),
            new Vector3(600f, 180f, 365f),
            new Vector3(750f, 170f, 335f),
            new Vector3(891f, 176f, 326f),
            new Vector3(1000f, 190f, 350f),
            new Vector3(1150f, 225f, 390f),
            new Vector3(1300f, 265f, 410f),
            new Vector3(1450f, 280f, 395f),
            new Vector3(1536f, 285f, 390f)
        };

        /// <summary>河北岸（陆地南缘）的世界 Z。</summary>
        public static float NorthBankZ(float worldX) => BankZ(worldX, true);

        /// <summary>河南岸（陆地北缘）的世界 Z。</summary>
        public static float SouthBankZ(float worldX) => BankZ(worldX, false);

        public static bool IsInRiver(Vector2 world, float margin = 0f)
        {
            return world.y < NorthBankZ(world.x) + margin && world.y > SouthBankZ(world.x) - margin;
        }

        private static float BankZ(float worldX, bool north)
        {
            var px = (worldX / WorldWidth + 0.5f) * ImageWidth;
            px = Mathf.Clamp(px, RiverBankPixels[0].x, RiverBankPixels[RiverBankPixels.Length - 1].x);
            for (var i = 0; i < RiverBankPixels.Length - 1; i++)
            {
                var a = RiverBankPixels[i];
                var b = RiverBankPixels[i + 1];
                if (px > b.x)
                {
                    continue;
                }

                var t = Mathf.InverseLerp(a.x, b.x, px);
                var py = north ? Mathf.Lerp(a.y, b.y, t) : Mathf.Lerp(a.z, b.z, t);
                return PixelToWorld(px, py).y;
            }

            return PixelToWorld(px, north ? RiverBankPixels[RiverBankPixels.Length - 1].y : RiverBankPixels[RiverBankPixels.Length - 1].z).y;
        }

        // ---------- 道路 ----------

        public static readonly Vector2[][] RoadPixels =
        {
            // 北岸主路：西缘 → 仓库 → 桥北 → 房区院落
            new[] { P(0, 235), P(150, 255), P(330, 200), P(480, 135), P(620, 95), P(800, 85), P(891, 95), P(1050, 100), P(1120, 130), P(1180, 200), P(1260, 225), P(1343, 200) },
            // 西北撤离点支路
            new[] { P(110, 245), P(110, 165) },
            // 桥北引道
            new[] { P(891, 95), P(891, 180) },
            // 桥南 → 厂房 → 主安全屋
            new[] { P(891, 322), P(900, 400), P(960, 460), P(1040, 520), P(1110, 560), P(1200, 640), P(1260, 720), P(1300, 780), P(1340, 830), P(1376, 864) },
            // 厂房南门支路
            new[] { P(1080, 545), P(1087, 512) },
            // 南环路：主路 → 山南 → 西南撤离点
            new[] { P(1250, 710), P(1220, 780), P(1150, 830), P(1000, 860), P(800, 880), P(600, 870), P(450, 830), P(330, 830), P(270, 870), P(240, 930), P(200, 1024) },
            // 林间小路：树林 → 山北 → 桥南主路
            new[] { P(490, 470), P(520, 520), P(600, 550), P(700, 575), P(780, 560), P(900, 480), P(960, 460) }
        };

        public static IEnumerable<Vector2[]> RoadsWorld()
        {
            foreach (var road in RoadPixels)
            {
                var world = new Vector2[road.Length];
                for (var i = 0; i < road.Length; i++)
                {
                    world[i] = PixelToWorld(road[i].x, road[i].y);
                }

                yield return world;
            }
        }

        public static float DistanceToRoads(Vector2 world)
        {
            var best = float.MaxValue;
            foreach (var road in RoadsWorld())
            {
                for (var i = 0; i < road.Length - 1; i++)
                {
                    best = Mathf.Min(best, DistanceToSegment(world, road[i], road[i + 1]));
                }
            }

            return best;
        }

        public static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            var t = ab.sqrMagnitude > 0f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            return Vector2.Distance(p, a + ab * t);
        }

        // ---------- 地形 ----------

        /// <summary>白盒地形高度：中部山体为高斯隆起，建筑地基压平为 0。</summary>
        public static float TerrainHeight(Vector2 world)
        {
            var d = world - HillCenter;
            var e = (d.x / HillSigma.x) * (d.x / HillSigma.x) + (d.y / HillSigma.y) * (d.y / HillSigma.y);
            var height = HillHeight * Mathf.Exp(-e);
            if (height < 0.05f)
            {
                return 0f;
            }

            foreach (var building in Buildings)
            {
                var half = building.Size * 0.5f + new Vector2(6f, 6f);
                var local = world - building.Center;
                var outside = Mathf.Max(Mathf.Abs(local.x) - half.x, Mathf.Abs(local.y) - half.y);
                if (outside < 10f)
                {
                    height *= Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(outside / 10f));
                }
            }

            return height;
        }

        // ---------- 怪物污染 ----------

        /// <summary>污染源：西北深区（仓库与撤离点之间）和树林西缘。离主安全屋越远越重。</summary>
        public static readonly (Vector2 center, float radius)[] ContaminationSources =
        {
            (new Vector2(-170f, 135f), 115f),
            (new Vector2(-145f, 5f), 75f)
        };

        /// <summary>0 = 正常植被，1 = 严重污染。只驱动 MonsterContamination 层的表现，不影响玩法。</summary>
        public static float ContaminationAt(Vector2 world)
        {
            var value = 0f;
            foreach (var (center, radius) in ContaminationSources)
            {
                value = Mathf.Max(value, 1f - Vector2.Distance(world, center) / radius);
            }

            return Mathf.Clamp01(value);
        }

        // ---------- 建筑 ----------

        public static readonly BuildingSpec[] Buildings =
        {
            new BuildingSpec
            {
                Id = "map01_warehouse", DisplayName = "仓库", Enterable = true,
                Center = new Vector2(-96f, 131.2f), Size = new Vector2(48f, 26f), WallHeight = 7f, Roof = RoofStyle.Flat,
                Doors = new[] { new DoorSpec(WallSide.South, 0f, 6f), new DoorSpec(WallSide.East, 0f, 3f) },
                InteriorVolumes = new[] { new LocalRect("Hall", Vector2.zero, new Vector2(47.2f, 25.2f), 7f) },
                Props = new[]
                {
                    new LocalRect("Shelf_W2", new Vector2(-16f, 0f), new Vector2(1.5f, 14f), 2.5f),
                    new LocalRect("Shelf_W1", new Vector2(-9f, 0f), new Vector2(1.5f, 14f), 2.5f),
                    new LocalRect("Shelf_E1", new Vector2(9f, 0f), new Vector2(1.5f, 14f), 2.5f),
                    new LocalRect("Shelf_E2", new Vector2(16f, 0f), new Vector2(1.5f, 14f), 2.5f),
                    new LocalRect("CrateStack", new Vector2(20f, -9f), new Vector2(3f, 3f), 2f)
                }
            },
            new BuildingSpec
            {
                Id = "map01_factory", DisplayName = "厂房", Enterable = true,
                Center = new Vector2(105.6f, 12.8f), Size = new Vector2(34f, 22f), WallHeight = 7f, Roof = RoofStyle.Sawtooth,
                Doors = new[] { new DoorSpec(WallSide.South, -6f, 5f), new DoorSpec(WallSide.West, 2f, 3f) },
                // 发电机房隔墙：x = 7，门洞位于南起 14 米处。
                Partitions = new[] { new PartitionSpec(new Vector2(7f, -11f), new Vector2(7f, 11f), 14f, 2.5f) },
                InteriorVolumes = new[]
                {
                    new LocalRect("Hall", new Vector2(-4.6f, 0f), new Vector2(24.4f, 21.6f), 7f),
                    new LocalRect("GeneratorRoom", new Vector2(11.6f, 0f), new Vector2(10.4f, 21.6f), 7f)
                },
                Props = new[]
                {
                    new LocalRect("Generator", new Vector2(12f, 4f), new Vector2(3f, 2f), 2f),
                    new LocalRect("Workbench", new Vector2(-12f, 8f), new Vector2(4f, 1.2f), 1f),
                    new LocalRect("MachineIsland", new Vector2(-2f, 2f), new Vector2(4f, 3f), 1.8f)
                }
            },
            new BuildingSpec
            {
                Id = "map01_house_a", DisplayName = "房区住宅 A", Enterable = true,
                Center = new Vector2(182.4f, 112f), Size = new Vector2(12f, 9f), WallHeight = 3.5f, Roof = RoofStyle.Gable,
                Doors = new[] { new DoorSpec(WallSide.South, -3f, 2f) },
                // 起居室 / 卧室隔墙：x = 1，门洞位于南起 3 米处。
                Partitions = new[] { new PartitionSpec(new Vector2(1f, -4.5f), new Vector2(1f, 4.5f), 3f, 2f) },
                InteriorVolumes = new[]
                {
                    new LocalRect("LivingRoom", new Vector2(-2.1f, 0f), new Vector2(7.4f, 8.6f), 3.5f),
                    new LocalRect("Bedroom", new Vector2(3.1f, 0f), new Vector2(5.4f, 8.6f), 3.5f)
                },
                Props = new[]
                {
                    new LocalRect("Table", new Vector2(-3f, 1.5f), new Vector2(1.6f, 1f), 0.8f),
                    new LocalRect("Cabinet", new Vector2(-5.3f, 3.6f), new Vector2(0.8f, 1.2f), 1.8f),
                    new LocalRect("Bed", new Vector2(4f, 2.8f), new Vector2(2f, 2.2f), 0.6f)
                }
            },
            new BuildingSpec
            {
                Id = "map01_safehouse", DisplayName = "主安全屋", Enterable = true,
                Center = new Vector2(196.8f, -121.6f), Size = new Vector2(14f, 12f), WallHeight = 3.5f, Roof = RoofStyle.Gable,
                Doors = new[] { new DoorSpec(WallSide.North, -4f, 3f) },
                InteriorVolumes = new[] { new LocalRect("Main", Vector2.zero, new Vector2(13.2f, 11.2f), 3.5f) },
                Props = new[]
                {
                    new LocalRect("RecycleCounter", new Vector2(-4f, -4.2f), new Vector2(3f, 1f), 1f),
                    new LocalRect("ShopTerminal", new Vector2(3.5f, -4.2f), new Vector2(2f, 1f), 1.4f),
                    new LocalRect("BatterySlot", new Vector2(5.8f, 2f), new Vector2(0.8f, 1.6f), 1.2f)
                }
            },
            Solid("map01_house_b", P(1300, 40)),
            Solid("map01_house_c", P(1390, 55)),
            Solid("map01_house_d", P(1250, 95)),
            Solid("map01_house_e", P(1460, 110)),
            Solid("map01_house_f", P(1230, 170))
        };

        public static BuildingSpec FindBuilding(string id)
        {
            foreach (var building in Buildings)
            {
                if (building.Id == id)
                {
                    return building;
                }
            }

            return null;
        }

        private static BuildingSpec Solid(string id, Vector2 pixel)
        {
            return new BuildingSpec
            {
                Id = id, DisplayName = "住宅（不可进入）", Enterable = false,
                Center = PixelToWorld(pixel.x, pixel.y), Size = new Vector2(10f, 8f), WallHeight = 3.5f, Roof = RoofStyle.Gable
            };
        }

        // ---------- 点位 ----------

        public static readonly PointSpec[] Points =
        {
            Point("map01_safehouse_player_spawn_01", SpawnPointType.PlayerSpawn, 198.8f, -123.6f, 0, 3f, building: "map01_safehouse", capacity: 4, tags: new[] { "safehouse", "spawn" }),
            Point("map01_extract_nw_01", SpawnPointType.ExtractionCandidate, -206.4f, 115.2f, 2, 8f, tags: new[] { "extraction", "north_of_river" }),
            Point("map01_extract_sw_01", SpawnPointType.ExtractionCandidate, -177.6f, -134.4f, 2, 8f, tags: new[] { "extraction" }),

            Point("map01_warehouse_loot_01", SpawnPointType.Loot, -108f, 137.2f, 2, building: "map01_warehouse", tags: new[] { "warehouse", "recyclable", "high_density" }),
            Point("map01_warehouse_loot_02", SpawnPointType.Loot, -108f, 125.2f, 2, building: "map01_warehouse", tags: new[] { "warehouse", "recyclable", "high_density" }),
            Point("map01_warehouse_loot_03", SpawnPointType.Loot, -84f, 137.2f, 2, building: "map01_warehouse", tags: new[] { "warehouse", "recyclable", "high_density" }),
            Point("map01_warehouse_loot_04", SpawnPointType.Loot, -84f, 125.2f, 2, building: "map01_warehouse", tags: new[] { "warehouse", "recyclable", "high_density" }),
            Point("map01_warehouse_coopcarry_01", SpawnPointType.Loot, -96f, 139.2f, 2, 3f, building: "map01_warehouse", tags: new[] { "warehouse", "two_hand", "coop_carry" }),
            Point("map01_factory_loot_01", SpawnPointType.Loot, 95.6f, 18.8f, 1, building: "map01_factory", tags: new[] { "factory", "tools" }),
            Point("map01_factory_loot_02", SpawnPointType.Loot, 95.6f, 6.8f, 1, building: "map01_factory", tags: new[] { "factory", "recyclable" }),
            Point("map01_house_a_loot_01", SpawnPointType.Loot, 178.9f, 110f, 1, building: "map01_house_a", tags: new[] { "house", "food" }),
            Point("map01_house_a_loot_02", SpawnPointType.Loot, 186.4f, 110f, 1, building: "map01_house_a", tags: new[] { "house", "medical", "info" }),
            Point("map01_forest_loot_01", SpawnPointType.Loot, -100f, 10f, 1, tags: new[] { "forest" }),
            Point("map01_forest_loot_02", SpawnPointType.Loot, -75f, 5f, 1, tags: new[] { "forest" }),
            Point("map01_hill_loot_01", SpawnPointType.Loot, 10f, -40f, 1, tags: new[] { "hill" }),

            Point("map01_forest_monster_patrol_01", SpawnPointType.Monster, -91.2f, 19.2f, 1, 6f, tags: new[] { "forest", "patrol" }),
            Point("map01_hill_monster_patrol_01", SpawnPointType.Monster, 24f, -45f, 1, 6f, tags: new[] { "hill", "patrol" }),
            Point("map01_bridge_monster_guard_01", SpawnPointType.Monster, 38.4f, 112f, 2, 4f, tags: new[] { "bridge", "guard" }),
            Point("map01_warehouse_monster_guard_01", SpawnPointType.Monster, -96f, 131.2f, 2, 4f, building: "map01_warehouse", tags: new[] { "warehouse", "room_guard" }),
            Point("map01_factory_monster_guard_01", SpawnPointType.Monster, 101.6f, 8.8f, 1, 4f, building: "map01_factory", tags: new[] { "factory", "room_guard" }),

            Point("map01_factory_generator_01", SpawnPointType.Event, 117.6f, 13.8f, 1, 2f, building: "map01_factory", tags: new[] { "factory", "powered_room", "noisy", "event_generator" }),
            Point("map01_bridge_event_01", SpawnPointType.Event, 38.4f, 50f, 1, 4f, tags: new[] { "bridge", "chokepoint" }),

            Point("map01_house_a_special_room_01", SpawnPointType.SpecialRoom, 185.9f, 112.5f, 1, 2f, building: "map01_house_a", tags: new[] { "house", "special_room" }),
            Point("map01_factory_powered_room_01", SpawnPointType.SpecialRoom, 117.6f, 8f, 1, 2f, building: "map01_factory", tags: new[] { "factory", "powered_room" })
        };

        private static PointSpec Point(string id, SpawnPointType type, float x, float z, int tier, float radius = 1.5f,
            string building = null, int capacity = 1, string[] tags = null)
        {
            return new PointSpec
            {
                Id = id, Type = type, Position = new Vector2(x, z), DepthTier = tier, Radius = radius,
                BuildingId = building, Capacity = capacity, Tags = tags ?? Array.Empty<string>()
            };
        }

        private static Vector2 P(float px, float py) => new Vector2(px, py);
    }
}
