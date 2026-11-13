using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace WildernessExtraction.Map01.Tests
{
    /// <summary>永久黑暗是核心机制：离光会掉血；在山顶祭坛献祭别人的身份牌后变为白天，黑暗不再伤人。</summary>
    public sealed class Map01DarknessTests
    {
        private const string ScenePath = "Assets/Map01/Scenes/Map01.unity";
        // 南缘树林里离道路和灯都远的一点。
        private static readonly Vector3[] DarkCandidates =
        {
            new Vector3(-60f, 0.5f, -150f), new Vector3(-110f, 0.5f, -150f), new Vector3(-20f, 0.5f, -152f), new Vector3(60f, 0.5f, -150f)
        };

        /// <summary>离道路、灯和萤火虫都远的一点（从候选里挑第一个没有光的）。</summary>
        private static Vector3 DarkSpot
        {
            get
            {
                foreach (var candidate in DarkCandidates)
                {
                    if (!LightRegistry.Scene.IsLit(candidate)) return candidate;
                }

                return DarkCandidates[0];
            }
        }

        private Map01Authority authority;
        private PlayerCharacter player;
        private int sequence;

        [UnitySetUp]
        public IEnumerator Load()
        {
            Map01Authority.SuppressMonsters = true;
            // 原救援循环：第一块电池直接计入救援电量，庇护所常亮。
            Map01Authority.QuestModeOverride = false;
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            Assert.Ignore("只在编辑器中运行。");
#endif
            yield return null;
            authority = Object.FindAnyObjectByType<Map01Authority>();
            player = authority.Player;
        }

        [TearDown]
        public void Restore()
        {
            Map01Authority.SuppressMonsters = false;
            Map01Authority.QuestModeOverride = null;
        }

        [UnityTest]
        public IEnumerator Darkness_IsOnByDefault_SafehouseIsLit()
        {
            yield return null;
            Assert.IsNotNull(player.Exposure);
            Assert.IsTrue(player.Exposure.enabled, "黑暗暴露必须开启");
            Assert.IsFalse(authority.Daylight.IsDay, "开局是黑夜");
            Assert.IsTrue(LightRegistry.Scene.IsLit(player.transform.position), "出生在安全屋光里");
            Assert.IsFalse(LightRegistry.Scene.IsLit(DarkSpot), "测试点应在黑暗中");

            var before = player.Health.Current;
            player.Motor.Teleport(DarkSpot);
            yield return new WaitForSeconds(3.5f);
            Assert.Less(player.Health.Current, before, "离光超过预警时间后应掉血");
        }

        [UnityTest]
        public IEnumerator SacrificingIdentityTag_TurnsDay_AndStopsDarknessDamage()
        {
            var altar = authority.FindStation(Map01Station.Altar);
            Assert.IsNotNull(altar, "山顶应有祭坛");
            player.Motor.Teleport(altar.transform.position + new Vector3(1.2f, 0.3f, 0f));
            yield return null;

            Assert.AreEqual(InteractionResult.MissingItem, Send(altar), "没有身份牌不能献祭");
            player.Inventory.Inventory.TryAdd(authority.IdentityTag);
            Assert.AreEqual(InteractionResult.Accepted, Send(altar));
            Assert.IsTrue(authority.Daylight.IsDay);
            Assert.AreEqual(0, player.Inventory.Inventory.CountOf(authority.IdentityTag), "身份牌被消耗");
            Assert.AreEqual(InteractionResult.InvalidState, Send(altar), "已经是白天，不能重复献祭");

            player.Motor.Teleport(DarkSpot);
            var before = player.Health.Current;
            yield return new WaitForSeconds(3.5f);
            Assert.IsTrue(LightRegistry.Scene.IsLit(DarkSpot), "白天全图都算有光");
            Assert.AreEqual(before, player.Health.Current, "白天黑暗不再伤人");
        }

        [UnityTest]
        public IEnumerator LitInteriors_AreSafeWall_ToWall_UnlitInteriorsStayDark()
        {
            yield return null;
            var safehouse = Map01Layout.FindBuilding("map01_safehouse");
            var factory = Map01Layout.FindBuilding("map01_factory");
            var warehouse = Map01Layout.FindBuilding("map01_warehouse");

            Assert.IsTrue(LightRegistry.Scene.IsLit(World(safehouse, new Vector2(6f, -5f))), "安全屋室内角落也算有光");
            Assert.IsFalse(LightRegistry.Scene.IsLit(World(warehouse, new Vector2(-20f, 10f))), "没有灯的仓库室内是黑的");

            // 厂房大厅远离发电机房灯的角落：灯亮前黑，灯亮后整间安全。
            var hallCorner = World(factory, new Vector2(-15f, -9f));
            Assert.IsFalse(LightRegistry.Scene.IsLit(hallCorner));
            GameObject.Find("Lamp_FactoryGenerator").GetComponent<SafeLightSource>().SetLit(true);
            Assert.IsTrue(LightRegistry.Scene.IsLit(hallCorner), "厂房灯亮后整个室内都算有光");
            Assert.IsFalse(LightRegistry.Scene.IsLit(World(factory, new Vector2(-15f, -20f))), "门外仍按黑暗处理");
        }

        [UnityTest]
        public IEnumerator Fireflies_GlowAtNight_AndLeaveAtDawn()
        {
            yield return null;
            var group = GameObject.Find("Map01_Root/Props_Static/Fireflies");
            var swarms = group.GetComponentsInChildren<FireflySwarm>();
            Assert.GreaterOrEqual(swarms.Length, 10, "各片林地都应有萤火虫");
            foreach (var swarm in swarms)
            {
                Assert.IsTrue(swarm.Particles.isPlaying, $"{swarm.name} 夜里应在发光");
                Assert.IsTrue(swarm.IsLit);
                var glow = swarm.GetComponentInChildren<Light>();
                Assert.IsTrue(glow.enabled);
                Assert.GreaterOrEqual(glow.range, swarm.Radius, "光照范围应覆盖安全半径");
            }

            authority.Daylight.BeginDay();
            yield return null;
            foreach (var swarm in swarms)
            {
                Assert.IsFalse(swarm.Particles.isEmitting, $"{swarm.name} 天亮后应停止");
                Assert.IsFalse(swarm.GetComponentInChildren<Light>().enabled);
            }

            Assert.AreEqual(0, group.GetComponentsInChildren<Collider>().Length, "萤火虫没有碰撞");
        }

        [UnityTest]
        public IEnumerator StandingByFireflies_ProtectsFromDarkness()
        {
            yield return null;
            var swarm = GameObject.Find("Map01_Root/Props_Static/Fireflies").GetComponentsInChildren<FireflySwarm>()[0];
            var center = swarm.transform.position;
            Assert.IsTrue(LightRegistry.Scene.IsLit(center + new Vector3(swarm.Radius - 0.5f, 0f, 0f)), "萤火虫光圈内算有光");
            Assert.IsFalse(LightRegistry.Scene.IsLit(center + new Vector3(swarm.Radius + 1.5f, 0f, 0f)), "光圈外仍是黑暗");

            player.Motor.Teleport(center + new Vector3(1f, 0.5f, 0f));
            var before = player.Health.Current;
            yield return new WaitForSeconds(3.5f);
            Assert.AreEqual(before, player.Health.Current, "站在萤火虫旁边不掉血");
        }

        private static Vector3 World(BuildingSpec building, Vector2 local)
        {
            return new Vector3(building.Center.x + local.x, 1f, building.Center.y + local.y);
        }

        [Test]
        public void IdentityTag_IsGuaranteedOnACorpse_AndCannotBeRecycled()
        {
            Assert.IsTrue(authority.Content.Loot.Exists(l => l.ItemId == authority.IdentityTag.itemId), "每局至少一块身份牌");
            Assert.IsFalse(authority.IsRecyclable(authority.IdentityTag), "身份牌只能献祭，不能回收");
        }

        private InteractionResult Send(Map01Interactable station)
        {
            return authority.Request(new InteractionRequest(station.EntityId, InteractionAction.Sacrifice, ++sequence));
        }
    }
}
