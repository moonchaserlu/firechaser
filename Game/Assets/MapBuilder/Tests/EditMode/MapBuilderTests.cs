using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using WildernessExtraction.Map01;
using WildernessExtraction.MapBuilder.EditorTools;

namespace WildernessExtraction.MapBuilder.Tests
{
    public sealed class MapBuilderTests
    {
        private const string TempDefinition = "Assets/MapBuilder/Tests/EditMode/__temp_MapDefinition.asset";

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(TempDefinition);
        }

        [Test]
        public void EdgeSnap_PullsGhostAgainstNeighbourEdge()
        {
            var other = new Bounds(Vector3.zero, new Vector3(2f, 2f, 2f));
            var ghost = new Bounds(new Vector3(2.3f, 0f, 0.2f), new Vector3(2f, 2f, 2f));
            var offset = MapBuilderPlacement.EdgeSnapOffset(ghost, new[] { other });
            Assert.That(offset.x, Is.EqualTo(-0.3f).Within(1e-4f), "应贴到右边缘");
            Assert.That(offset.z, Is.EqualTo(-0.2f).Within(1e-4f), "Z 方向应与邻居边对齐");
        }

        [Test]
        public void EdgeSnap_IgnoresFarObjects()
        {
            var other = new Bounds(Vector3.zero, Vector3.one * 2f);
            var ghost = new Bounds(new Vector3(5f, 0f, 0f), Vector3.one * 2f);
            Assert.That(MapBuilderPlacement.EdgeSnapOffset(ghost, new[] { other }), Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void Overlaps_TouchingIsFine_IntersectingIsNot()
        {
            var a = new Bounds(Vector3.zero, Vector3.one * 2f);
            Assert.That(MapBuilderPlacement.Overlaps(a, new Bounds(new Vector3(2f, 0f, 0f), Vector3.one * 2f)), Is.False, "贴边");
            Assert.That(MapBuilderPlacement.Overlaps(a, new Bounds(new Vector3(0f, 2f, 0f), Vector3.one * 2f)), Is.False, "叠放");
            Assert.That(MapBuilderPlacement.Overlaps(a, new Bounds(new Vector3(1f, 0f, 0f), Vector3.one * 2f)), Is.True, "穿插");
        }

        [Test]
        public void SnapToGrid_RoundsXZOnly()
        {
            var snapped = MapBuilderPlacement.SnapToGrid(new Vector3(1.26f, 0.7f, -2.74f), null, 0.5f);
            Assert.That(snapped, Is.EqualTo(new Vector3(1.5f, 0.7f, -2.5f)));
        }

        [Test]
        public void CreateMap_HasRootGroundAndGroups()
        {
            var root = MapBuilderMaps.CreateMap("TestMap", new Vector2(40f, 30f), save: false);
            Assert.That(root.mapId, Is.EqualTo("testmap"));
            Assert.That(root.transform.Find(MapAuthoringRoot.PropsGroup), Is.Not.Null);
            Assert.That(root.transform.Find(MapAuthoringRoot.PointsGroup), Is.Not.Null);
            var ground = root.transform.Find("地面");
            Assert.That(ground.GetComponent<Collider>(), Is.Not.Null, "地面要能被射线打到、能烘焙导航");
            Assert.That(MapBuilderMaps.FindRoot(), Is.EqualTo(root));
        }

        [Test]
        public void Place_PutsAssetUnderGroupWithMaterialAndColliders()
        {
            var root = MapBuilderMaps.CreateMap("TestMap", new Vector2(40f, 40f), save: false);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/ThirdPartyAssets/itch.io/KayLousberg/FurnitureBits/1.0_SOURCE/Models/armchair.fbx")
                ?? MapBuilderLibrary.FindPlaceable("Assets/ThirdPartyAssets/itch.io/KayLousberg/FurnitureBits").First();
            var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Map01/Art/Materials/M_KK_Furniture.mat");
            var group = new MapBuilderLibrary.Group { name = "家具", materialOverride = material, scale = 0.8f };
            var placed = MapBuilderPlacement.Place(root, new MapBuilderPlacement.Request
            {
                asset = model, group = group, position = new Vector3(3f, 0f, 4f), rotation = Quaternion.Euler(0f, 90f, 0f),
                scaleMultiplier = 0.8f, addColliders = true
            });

            Assert.That(placed.transform.parent.name, Is.EqualTo("家具"));
            Assert.That(placed.transform.parent.parent.name, Is.EqualTo(MapAuthoringRoot.PropsGroup));
            Assert.That(placed.transform.position, Is.EqualTo(new Vector3(3f, 0f, 4f)));
            Assert.That(placed.GetComponentsInChildren<Collider>().Length, Is.GreaterThan(0));
            Assert.That(placed.GetComponentsInChildren<Renderer>().All(r => r.sharedMaterials.All(m => m == material)), Is.True);
            Assert.That(MapBuilderPlacement.PlacedProps(root), Does.Contain(placed));
        }

        [Test]
        public void Points_GetUniqueIds_ChecksAndExport()
        {
            var root = MapBuilderMaps.CreateMap("TestMap", new Vector2(40f, 40f), save: false);
            var a = MapBuilderPoints.Place(root, SpawnPointType.Loot, new Vector3(1f, 0f, 1f), 1.5f, new[] { "indoor" });
            var b = MapBuilderPoints.Place(root, SpawnPointType.Loot, new Vector3(5f, 0f, 1f), 1.5f, null);
            Assert.That(a.PointId, Is.EqualTo("testmap_loot_01"));
            Assert.That(b.PointId, Is.EqualTo("testmap_loot_02"));
            Assert.That(a.Tags, Does.Contain("indoor"));

            var issues = MapBuilderChecks.Run(root).Select(i => i.Message).ToList();
            Assert.That(issues, Does.Contain("还没有出生点"));
            Assert.That(issues, Does.Contain("还没有撤离点"));

            MapBuilderPoints.Place(root, SpawnPointType.PlayerSpawn, Vector3.zero, 1.5f, null);
            MapBuilderPoints.Place(root, SpawnPointType.ExtractionCandidate, new Vector3(0f, 0f, 15f), 3f, null);
            issues = MapBuilderChecks.Run(root).Select(i => i.Message).ToList();
            Assert.That(issues, Has.None.Contains("出生点"));
            Assert.That(issues, Has.None.Contains("撤离点"));

            var map = MapBuilderPoints.Export(root, TempDefinition, out var report);
            Assert.That(map.mapId, Is.EqualTo("testmap"));
            Assert.That(map.points.Count, Is.EqualTo(4));
            Assert.That(map.Find("testmap_extract_01").pathLength, Is.EqualTo(15f).Within(0.01f), "没有导航时按直线距离");
            StringAssert.Contains("直线距离", report);
        }
    }
}
