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
        private static readonly Vector3 DarkSpot = new Vector3(-60f, 0.5f, -150f);

        private Map01Authority authority;
        private PlayerCharacter player;
        private int sequence;

        [UnitySetUp]
        public IEnumerator Load()
        {
            Map01Authority.SuppressMonsters = true;
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
