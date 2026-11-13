using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace WildernessExtraction.Map01.Tests
{
    /// <summary>Map01 布局数据的静态校验：点位 ID、位置与建筑归属。</summary>
    public sealed class Map01LayoutTests
    {
        private static readonly Regex IdPattern = new Regex("^map01_[a-z0-9_]+$");

        [Test]
        public void PointIds_AreUniqueAndStable()
        {
            var ids = new HashSet<string>();
            foreach (var point in Map01Layout.Points)
            {
                StringAssert.IsMatch(IdPattern.ToString(), point.Id);
                Assert.IsTrue(ids.Add(point.Id), $"重复点位 ID：{point.Id}");
            }
        }

        [Test]
        public void Points_AreInsideMapAndOutOfRiver()
        {
            foreach (var point in Map01Layout.Points)
            {
                Assert.LessOrEqual(Mathf.Abs(point.Position.x), Map01Layout.WorldWidth * 0.5f - 2f, point.Id);
                Assert.LessOrEqual(Mathf.Abs(point.Position.y), Map01Layout.WorldDepth * 0.5f - 2f, point.Id);
                Assert.IsFalse(Map01Layout.IsInRiver(point.Position, 2f), $"{point.Id} 落在河里");
            }
        }

        [Test]
        public void BuildingPoints_LieInsideTheirInteriorVolumes()
        {
            foreach (var point in Map01Layout.Points)
            {
                if (string.IsNullOrEmpty(point.BuildingId))
                {
                    continue;
                }

                var building = Map01Layout.FindBuilding(point.BuildingId);
                Assert.IsNotNull(building, $"{point.Id} 引用了不存在的建筑 {point.BuildingId}");
                Assert.IsTrue(building.Enterable, $"{point.Id} 所在建筑不可进入");
                var local = point.Position - building.Center;
                var inside = false;
                foreach (var volume in building.InteriorVolumes)
                {
                    var d = local - volume.Center;
                    inside |= Mathf.Abs(d.x) <= volume.Size.x * 0.5f && Mathf.Abs(d.y) <= volume.Size.y * 0.5f;
                }

                Assert.IsTrue(inside, $"{point.Id} 不在 {building.Id} 的室内判定体中");
            }
        }

        [Test]
        public void Layout_HasRequiredPois()
        {
            var counts = new Dictionary<SpawnPointType, int>();
            foreach (var point in Map01Layout.Points)
            {
                counts.TryGetValue(point.Type, out var count);
                counts[point.Type] = count + 1;
            }

            Assert.AreEqual(1, counts[SpawnPointType.PlayerSpawn]);
            Assert.AreEqual(2, counts[SpawnPointType.ExtractionCandidate], "西北、西南两个撤离候选点");
            Assert.IsTrue(System.Array.Exists(Map01Layout.Points, p => p.Id == "map01_factory_generator_01" && p.BuildingId == "map01_factory"),
                "第一版发电机事件固定在厂房");
            Assert.IsNotNull(Map01Layout.FindBuilding("map01_factory"));
            Assert.IsNotNull(Map01Layout.FindBuilding("map01_house_a"));
        }

        [Test]
        public void Buildings_AreOutOfRiverAndOnFlatPads()
        {
            foreach (var building in Map01Layout.Buildings)
            {
                var half = building.Size * 0.5f;
                foreach (var corner in new[] { new Vector2(-half.x, -half.y), new Vector2(half.x, -half.y), new Vector2(-half.x, half.y), new Vector2(half.x, half.y) })
                {
                    var world = building.Center + corner;
                    Assert.IsFalse(Map01Layout.IsInRiver(world, 3f), $"{building.Id} 贴近或压在河上");
                    Assert.Less(Map01Layout.TerrainHeight(world), 0.05f, $"{building.Id} 地基未压平");
                }
            }
        }

        [Test]
        public void River_IsNorthOfSafehouseAndBridgeSpansIt()
        {
            var x = Map01Layout.BridgeX;
            var width = Map01Layout.NorthBankZ(x) - Map01Layout.SouthBankZ(x);
            Assert.Greater(width, 20f, "河在桥位应足够宽，不能被视为可跨越的小沟");
            var safehouse = Map01Layout.FindBuilding("map01_safehouse");
            Assert.Less(safehouse.Center.y, Map01Layout.SouthBankZ(safehouse.Center.x), "主安全屋位于河南岸");
            var warehouse = Map01Layout.FindBuilding("map01_warehouse");
            Assert.Greater(warehouse.Center.y, Map01Layout.NorthBankZ(warehouse.Center.x), "仓库位于河北岸，需要过桥");
        }
    }
}
