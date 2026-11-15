using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace WildernessExtraction.Map01.Tests
{
    /// <summary>
    /// 在生成的 Map01 场景（素材版）中验证：全 POI 往返可达、河只能经桥通过、屋顶本地显隐。
    /// 场景由菜单 Wilderness/Map01/Build Map01 生成；测试按路径加载，不依赖 Build Settings。
    /// </summary>
    public sealed class Map01WhiteboxTests
    {
        private const string ScenePath = "Assets/Map01/Scenes/Map01.unity";

        private PlayerCharacter player;

        [TearDown]
        public void RestoreMonsters()
        {
            Map01Authority.SuppressMonsters = false;
        }

        [UnitySetUp]
        public IEnumerator LoadMap()
        {
            // 这些测试只验证地图、导航与屋顶，不让怪物参与。
            Map01Authority.SuppressMonsters = true;
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath,
                new LoadSceneParameters(LoadSceneMode.Single));
#else
            Assert.Ignore("Map01 白盒测试只在编辑器中运行。");
#endif
            yield return null;
            player = Object.FindAnyObjectByType<PlayerCharacter>();
            Assert.IsNotNull(player, "场景中应有玩家");
        }

        [Test]
        public void SpawnPoints_MatchLayoutWithStableIds()
        {
            var points = Object.FindObjectsByType<MapSpawnPoint>(FindObjectsSortMode.None);
            var ids = new HashSet<string>();
            foreach (var point in points)
            {
                Assert.IsTrue(ids.Add(point.PointId), $"重复点位 ID：{point.PointId}");
            }

            Assert.AreEqual(Map01Layout.Points.Length, points.Length);
            foreach (var spec in Map01Layout.Points)
            {
                Assert.IsTrue(ids.Contains(spec.Id), $"场景缺少点位 {spec.Id}");
            }
        }

        [Test]
        public void EveryPoint_IsReachableFromSafehouse_AndBack()
        {
            var spawn = Find("map01_safehouse_player_spawn_01");
            Assert.IsTrue(NavMesh.SamplePosition(spawn.transform.position, out var start, 2f, NavMesh.AllAreas), "出生点不在 NavMesh 上");
            foreach (var point in Object.FindObjectsByType<MapSpawnPoint>(FindObjectsSortMode.None))
            {
                Assert.IsTrue(NavMesh.SamplePosition(point.transform.position, out var hit, 2f, NavMesh.AllAreas), $"{point.PointId} 不在 NavMesh 上");
                var there = new NavMeshPath();
                var back = new NavMeshPath();
                NavMesh.CalculatePath(start.position, hit.position, NavMesh.AllAreas, there);
                NavMesh.CalculatePath(hit.position, start.position, NavMesh.AllAreas, back);
                Assert.AreEqual(NavMeshPathStatus.PathComplete, there.status, $"安全屋到 {point.PointId} 不可达");
                Assert.AreEqual(NavMeshPathStatus.PathComplete, back.status, $"{point.PointId} 返回安全屋不可达");
            }
        }

        [Test]
        public void RiverSurface_IsNotWalkable()
        {
            foreach (var x in new[] { -200f, -100f, 0f, 120f, 200f })
            {
                var mid = (Map01Layout.NorthBankZ(x) + Map01Layout.SouthBankZ(x)) * 0.5f;
                Assert.IsFalse(NavMesh.SamplePosition(new Vector3(x, 0f, mid), out _, 1f, NavMesh.AllAreas), $"河中 x={x} 出现可行走面");
            }
        }

        [Test]
        public void AiPathAcrossRiver_UsesTheBridge()
        {
            const float x = -150f;
            var south = new Vector3(x, 0f, Map01Layout.SouthBankZ(x) - 15f);
            var north = new Vector3(x, 0f, Map01Layout.NorthBankZ(x) + 15f);
            Assert.IsTrue(NavMesh.SamplePosition(south, out var from, 3f, NavMesh.AllAreas));
            Assert.IsTrue(NavMesh.SamplePosition(north, out var to, 3f, NavMesh.AllAreas));
            var path = new NavMeshPath();
            NavMesh.CalculatePath(from.position, to.position, NavMesh.AllAreas, path);
            Assert.AreEqual(NavMeshPathStatus.PathComplete, path.status);

            // 沿路径每 0.5 米采样：所有落在河面范围内的采样点都必须在桥面宽度内。
            var crossings = 0;
            var corners = path.corners;
            for (var i = 0; i < corners.Length - 1; i++)
            {
                var a = corners[i];
                var b = corners[i + 1];
                var steps = Mathf.CeilToInt(Vector3.Distance(a, b) / 0.5f);
                for (var s = 0; s <= steps; s++)
                {
                    var p = Vector3.Lerp(a, b, s / (float)Mathf.Max(steps, 1));
                    if (!Map01Layout.IsInRiver(new Vector2(p.x, p.z)))
                    {
                        continue;
                    }

                    crossings++;
                    Assert.Less(Mathf.Abs(p.x - Map01Layout.BridgeX), Map01Layout.BridgeWidth * 0.5f, $"过河路径点 {p} 不在桥面上");
                }
            }

            Assert.Greater(crossings, 0, "过河路径应经过桥面");
        }

        [UnityTest]
        public IEnumerator Player_CannotWadeAcrossRiver()
        {
            const float x = -120f;
            var start = new Vector3(x, 0.1f, Map01Layout.SouthBankZ(x) - 3f);
            yield return Walk(start, Vector3.forward, 60f);
            // 斜岸会让角色沿墙滑动，所以按玩家最终所在的 x 判定。
            var p = player.transform.position;
            Assert.Less(p.z, Map01Layout.SouthBankZ(p.x), $"玩家不应涉水过河，最终位置 {p}");
        }

        [UnityTest]
        public IEnumerator Player_CrossesRiverOnBridge()
        {
            var x = Map01Layout.BridgeX;
            var start = new Vector3(x, 0.4f, Map01Layout.SouthBankZ(x) - Map01Layout.BankApproach - 2f);
            var distance = Map01Layout.NorthBankZ(x) - start.z + 4f;
            yield return Walk(start, Vector3.forward, distance);
            Assert.Greater(player.transform.position.z, Map01Layout.NorthBankZ(x) + 1f, "玩家应能经桥到达北岸");
        }

        [UnityTest]
        public IEnumerator Roof_HidesOnlyWhileLocalPlayerIsInside()
        {
            var factory = Building("map01_factory");
            var house = Building("map01_house_a");
            var physics = factory.transform.Find("Physics").GetComponentsInChildren<Collider>();
            yield return null;
            Assert.IsTrue(factory.RoofVisible, "玩家在室外时厂房屋顶完整");
            Assert.IsTrue(house.RoofVisible);
            AssertRoofRendering(factory, true);

            player.Motor.Teleport(factory.transform.position + new Vector3(-8f, 0.1f, -4f));
            yield return null;
            yield return null;
            Assert.IsFalse(factory.RoofVisible, "进入厂房后隐藏屋顶");
            Assert.IsFalse(factory.TransitionComplete, "屋顶应淡出而不是瞬间消失");
            yield return new WaitForSeconds(factory.FadeDuration + 0.1f);
            Assert.IsTrue(factory.TransitionComplete);
            AssertRoofRendering(factory, false);
            Assert.IsTrue(house.RoofVisible, "其他建筑不受影响");
            foreach (var collider in physics)
            {
                Assert.IsTrue(collider.enabled && collider.gameObject.activeInHierarchy, $"{collider.name} 碰撞不应随屋顶隐藏");
            }

            foreach (var roof in factory.RevealRenderers)
            {
                Assert.IsNull(roof.GetComponent<Collider>(), "屋顶只负责渲染");
            }

            // 穿过大厅与发电机房的门洞（两个判定体重叠处），屋顶不得闪回。
            for (var lx = -2f; lx <= 12f; lx += 0.5f)
            {
                player.Motor.Teleport(factory.transform.position + new Vector3(lx, 0.1f, 3f));
                yield return null;
                Assert.IsFalse(factory.RoofVisible, $"经过门洞 x={lx} 时屋顶闪回");
            }

            player.Motor.Teleport(factory.transform.position + new Vector3(-6f, 0.1f, -20f));
            yield return new WaitForSeconds(0.1f + factory.FadeDuration + 0.15f);
            Assert.IsTrue(factory.RoofVisible, "离开后恢复屋顶");
            Assert.IsTrue(factory.TransitionComplete);
            AssertRoofRendering(factory, true);
        }

        [UnityTest]
        public IEnumerator Roof_IgnoresNonLocalViewers()
        {
            var house = Building("map01_house_a");
            player.Motor.Teleport(house.transform.position + new Vector3(0f, 0.1f, -15f));

            // 模拟另一名客户端的玩家站在屋内：它不是本机观察者，不能改变本机屋顶。
            var remote = new GameObject("RemotePlayer");
            remote.transform.position = house.transform.position + new Vector3(-2f, 0.1f, 0f);
            remote.AddComponent<RevealViewer>().IsLocal = false;
            yield return null;
            yield return null;
            Assert.AreSame(player.GetComponent<RevealViewer>(), RevealViewer.Local);
            Assert.IsTrue(house.RoofVisible, "屋外的本机玩家仍看到屋顶");

            player.Motor.Teleport(house.transform.position + new Vector3(3f, 0.1f, 0f));
            yield return null;
            yield return null;
            Assert.IsFalse(house.RoofVisible, "本机玩家进入后隐藏");
            Object.Destroy(remote);
        }

        [UnityTest]
        public IEnumerator Roof_StartsHiddenInSafehouse_AndAllEnterableBuildingsHaveSeparateRoofs()
        {
            yield return null;
            yield return null;
            Assert.IsFalse(Building("map01_safehouse").RoofVisible, "玩家在安全屋内出生，安全屋屋顶应隐藏");
            foreach (var reveal in Object.FindObjectsByType<BuildingReveal>(FindObjectsSortMode.None))
            {
                Assert.Greater(reveal.RevealRenderers.Length, 0, $"{reveal.BuildingId} 缺少独立屋顶");
                Assert.Greater(reveal.InteriorVolumes.Length, 0, $"{reveal.BuildingId} 缺少室内判定体");
                foreach (var roof in reveal.RevealRenderers)
                {
                    Assert.AreEqual("RoofRevealGroup", roof.transform.parent.name, $"{reveal.BuildingId} 屋顶未放在 RoofRevealGroup 下");
                }
            }
        }

        private IEnumerator Walk(Vector3 start, Vector3 direction, float distance)
        {
            player.Motor.enabled = false;
            player.Motor.Teleport(start);
            yield return null;
            var controller = player.GetComponent<CharacterController>();
            const float step = 0.4f;
            for (var travelled = 0f; travelled < distance; travelled += step)
            {
                controller.Move(direction * step + Vector3.down * 0.2f);
                yield return null;
            }
        }

        private static void AssertRoofRendering(BuildingReveal reveal, bool visible)
        {
            foreach (var roof in reveal.RevealRenderers)
            {
                Assert.AreEqual(!visible, roof.forceRenderingOff, roof.name);
                Assert.IsTrue(roof.gameObject.activeInHierarchy, "屋顶对象本身不应被停用");
                StringAssert.DoesNotEndWith("_Fade", roof.sharedMaterial.name, "过渡结束后应换回原材质");
            }
        }

        private static MapSpawnPoint Find(string id)
        {
            foreach (var point in Object.FindObjectsByType<MapSpawnPoint>(FindObjectsSortMode.None))
            {
                if (point.PointId == id)
                {
                    return point;
                }
            }

            Assert.Fail($"缺少点位 {id}");
            return null;
        }

        private static BuildingReveal Building(string id)
        {
            foreach (var reveal in Object.FindObjectsByType<BuildingReveal>(FindObjectsSortMode.None))
            {
                if (reveal.BuildingId == id)
                {
                    return reveal;
                }
            }

            Assert.Fail($"缺少建筑 {id}");
            return null;
        }
    }
}
