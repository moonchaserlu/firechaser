using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;

namespace WildernessExtraction.Map01.Tests
{
    /// <summary>阶段 3：固定种子可复现、风险档位按路径深度、高风险高收益且没有必拿路线。</summary>
    public sealed class Map01ContentTests
    {
        private const string MapPath = "Assets/Map01/Data/Map01_MapDefinition.asset";
        private const string TablesPath = "Assets/Map01/Data/Map01_ContentTables.asset";

        private MapDefinition map;
        private ContentTables tables;

        [SetUp]
        public void LoadData()
        {
            map = AssetDatabase.LoadAssetAtPath<MapDefinition>(MapPath);
            tables = AssetDatabase.LoadAssetAtPath<ContentTables>(TablesPath);
            Assert.IsNotNull(map, "缺少 MapDefinition，请先运行 Wilderness/Map01/Build Map01");
            Assert.IsNotNull(tables, "缺少 ContentTables");
        }

        [Test]
        public void MapDefinition_MatchesLayoutWithStableIds()
        {
            CollectionAssert.AreEquivalent(Map01Layout.Points.Select(p => p.Id), map.points.Select(p => p.pointId));
            foreach (var point in map.points)
            {
                Assert.Less(point.pathLength, float.PositiveInfinity, $"{point.pointId} 从安全屋不可达");
            }
        }

        [Test]
        public void DepthTiers_FollowPathLength_NotStraightLine()
        {
            Assert.AreEqual(0, map.Find("map01_safehouse_player_spawn_01").depthTier);
            foreach (var point in map.points)
            {
                Assert.AreEqual(DepthTiers.FromPathLength(point.pathLength, map.tier0MaxPath, map.tier1MaxPath), point.depthTier, point.pointId);
                var straight = UnityEngine.Vector3.Distance(point.position, map.Find("map01_safehouse_player_spawn_01").position);
                Assert.GreaterOrEqual(point.pathLength + 0.5f, straight, $"{point.pointId} 路径长度不应短于直线距离");
            }

            // 过桥后的仓库比同侧的厂房更深。
            Assert.Greater(map.Find("map01_warehouse_loot_01").pathLength, map.Find("map01_factory_loot_01").pathLength);
            Assert.AreEqual(2, map.Find("map01_warehouse_loot_01").depthTier, "仓库属于 Tier 2");
            Assert.AreEqual(1, map.Find("map01_factory_loot_01").depthTier, "厂房属于 Tier 1");
        }

        [Test]
        public void SameSeed_IsReproducible_DifferentSeedsOnlyChangeContent()
        {
            var a = ContentGenerator.Generate(map, tables, 12345);
            var b = ContentGenerator.Generate(map, tables, 12345);
            CollectionAssert.AreEqual(Signature(a), Signature(b));

            var different = 0;
            var validIds = new HashSet<string>(map.points.Select(p => p.pointId));
            for (var seed = 1; seed <= 50; seed++)
            {
                var c = ContentGenerator.Generate(map, tables, seed);
                if (!Signature(c).SequenceEqual(Signature(a))) different++;
                Assert.IsTrue(c.Loot.All(l => validIds.Contains(l.PointId)));
                Assert.IsTrue(c.Monsters.All(m => validIds.Contains(m.PointId)));
            }

            Assert.Greater(different, 45, "不同种子应产生不同内容");
        }

        [Test]
        public void HigherTiers_HaveHigherAveragePotentialValue()
        {
            var byTier = new double[3];
            var pointsPerTier = new int[3];
            foreach (var point in map.points.Where(p => p.type == SpawnPointType.Loot))
            {
                pointsPerTier[point.depthTier]++;
            }

            const int runs = 300;
            for (var seed = 0; seed < runs; seed++)
            {
                var content = ContentGenerator.Generate(map, tables, seed);
                foreach (var loot in content.Loot)
                {
                    byTier[map.Find(loot.PointId).depthTier] += loot.Value;
                }
            }

            var averages = new List<double>();
            for (var tier = 0; tier < 3; tier++)
            {
                if (pointsPerTier[tier] > 0) averages.Add(byTier[tier] / (runs * pointsPerTier[tier]));
            }

            for (var i = 1; i < averages.Count; i++)
            {
                Assert.Greater(averages[i], averages[i - 1], $"Tier 平均收益应递增：{string.Join(", ", averages.Select(v => v.ToString("F1")))}");
            }
        }

        [Test]
        public void NoSinglePointIsAlwaysTheBestPick()
        {
            var bestCounts = new Dictionary<string, int>();
            const int runs = 300;
            for (var seed = 0; seed < runs; seed++)
            {
                var content = ContentGenerator.Generate(map, tables, seed);
                var best = map.points.Where(p => p.type == SpawnPointType.Loot)
                    .OrderByDescending(p => content.ValueAt(p.pointId)).ThenBy(p => p.pointId).First().pointId;
                bestCounts.TryGetValue(best, out var count);
                bestCounts[best] = count + 1;
            }

            Assert.Less(bestCounts.Values.Max(), runs / 2, "单个点位不应在多数对局里都是收益最高的");
            Assert.GreaterOrEqual(bestCounts.Count, 4);
        }

        [Test]
        public void ExtractionAndEvents_FollowRules()
        {
            var extractionIds = new HashSet<string>();
            for (var seed = 0; seed < 100; seed++)
            {
                var content = ContentGenerator.Generate(map, tables, seed);
                Assert.AreEqual(SpawnPointType.ExtractionCandidate, map.Find(content.ExtractionPointId).type);
                extractionIds.Add(content.ExtractionPointId);
                CollectionAssert.Contains(content.ActiveEvents, "map01_factory_generator_01", "第一版发电机事件固定在厂房");
                foreach (var monster in content.Monsters)
                {
                    var point = map.Find(monster.PointId);
                    Assert.AreEqual(SpawnPointType.Monster, point.type);
                    if (monster.Behavior == MonsterBehavior.RoomGuard) Assert.IsTrue(point.HasTag("room_guard"));
                    if (monster.Behavior == MonsterBehavior.Patrol) Assert.IsTrue(point.HasTag("patrol"));
                }

                Assert.IsFalse(content.Monsters.Any(m => map.Find(m.PointId).depthTier == 0), "安全屋周边不刷怪");
            }

            Assert.AreEqual(2, extractionIds.Count, "两个撤离候选点都会被选中");
        }

        private static List<string> Signature(GeneratedContent content)
        {
            var list = content.Loot.Select(l => $"L:{l.PointId}:{l.ItemId}").ToList();
            list.AddRange(content.Monsters.Select(m => $"M:{m.PointId}:{m.MonsterId}"));
            list.AddRange(content.ActiveEvents.Select(e => $"E:{e}"));
            list.AddRange(content.SpecialRooms.Select(r => $"R:{r}"));
            list.Add($"X:{content.ExtractionPointId}");
            return list;
        }
    }
}
